using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace EasyLocalization.Tests
{
    public sealed class LanguageTests
    {
        private LocalizationSettings previous, settings;
        private LocalizationConfig config;
        private Provider provider;
        private TestLocales locales;
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private LocalizedString reference => new LocalizedString("Runtime Test", 1001);

        private sealed class TestLocales : ILocalesProvider
        {
            public List<Locale> Locales { get; } = new List<Locale>();
            public Locale GetLocale(LocaleIdentifier id) => Locales.FirstOrDefault(l => l.Identifier == id);
            public void AddLocale(Locale locale) => Locales.Add(locale);
            public bool RemoveLocale(Locale locale) => Locales.Remove(locale);
        }
        private sealed class Delayed<T> : AsyncOperationBase<T> where T : LocalizationTable
        {
            public T Value;
            protected override void Execute() { }
            public void Finish() => Complete(Value, true, (string)null);
        }
        private sealed class Provider : ITableProvider
        {
            public readonly Dictionary<string, StringTable> Tables = new Dictionary<string, StringTable>();
            public Action Finish;
            public bool DeferFrench;
            public AsyncOperationHandle<TTable> ProvideTableAsync<TTable>(string tableCollectionName, Locale locale) where TTable : LocalizationTable
            {
                if (!Tables.TryGetValue(locale.Identifier.Code, out var table)) return default;
                if (DeferFrench && locale.Identifier.Code == "fr")
                {
                    var operation = new Delayed<TTable> { Value = table as TTable };
                    Finish = operation.Finish;
                    return Addressables.ResourceManager.StartOperation(operation, default);
                }
                return Addressables.ResourceManager.CreateCompletedOperation(table as TTable, (string)null);
            }
        }

        [OneTimeSetUp] public void Setup()
        {
            previous = LocalizationSettings.Instance;
            settings = ScriptableObject.CreateInstance<LocalizationSettings>(); created.Add(settings);
            locales = new TestLocales(); provider = new Provider();
            foreach (string code in new[] { "en", "ru", "fr" })
            {
                var locale = Locale.CreateLocale(code); created.Add(locale); locales.AddLocale(locale);
                var shared = ScriptableObject.CreateInstance<SharedTableData>(); created.Add(shared);
                shared.TableCollectionName = "Runtime Test";
                shared.AddKey("welcome", 1001); shared.AddKey("missing", 1002); shared.AddKey("blank", 1003);
                var table = ScriptableObject.CreateInstance<StringTable>(); created.Add(table);
                table.SharedData = shared; table.LocaleIdentifier = code;
                var entry = table.AddEntry(1001, code == "ru" ? "Привет, {0}!" : code == "fr" ? "Bonjour, {0}!" : "Hello, {0}!"); entry.IsSmart = true;
                if (code == "en") table.AddEntry(1002, "Fallback");
                table.AddEntry(1003, ""); provider.Tables.Add(code, table);
            }
            settings.SetAvailableLocales(locales);
            settings.SetStringDatabase(new LocalizedStringDatabase { TableProvider = provider });
            LocalizationSettings.Instance = settings;
            LocalizationSettings.StartupLocaleSelectors.Clear();
            LocalizationSettings.StartupLocaleSelectors.Add(new SpecificLocaleSelector { LocaleId = "en" });
            config = ScriptableObject.CreateInstance<LocalizationConfig>(); created.Add(config);
            config.fallbackLanguage = "en"; config.preloadTables = new TableReference[] { "Runtime Test" };
        }
        [OneTimeTearDown] public void Cleanup()
        {
            Localization.Reset();
            LocalizationSettings.Instance = previous;
            foreach (var asset in created) UnityEngine.Object.DestroyImmediate(asset);
        }

        private static IEnumerator Wait(Task task)
        {
            float end = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Async localization operation timed out.");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }
        [UnitySetUp] public IEnumerator Ready() { yield return Wait(Localization.InitializeAsync(config)); yield return Wait(Localization.SetLanguageAsync("en")); }
        [UnityTest] public IEnumerator ParametersUseNativeSmartStrings() { var task=Localization.TextAsync(reference,new object[]{"Alex"}); yield return Wait(task); Assert.That(task.Result,Is.EqualTo("Hello, Alex!")); }
        [UnityTest] public IEnumerator LanguageSwitchAndStateRestoreUseStableCode()
        {
            var state=Localization.CaptureState(); yield return Wait(Localization.SetLanguageAsync("ru"));
            Assert.That(Localization.LanguageCode,Is.EqualTo("ru"));
            var text=Localization.TextAsync(reference,new object[]{"Alex"}); yield return Wait(text); Assert.That(text.Result,Is.EqualTo("Привет, Alex!"));
            yield return Wait(Localization.RestoreStateAsync(state)); Assert.That(Localization.LanguageCode,Is.EqualTo("en"));
        }
        [UnityTest] public IEnumerator UnavailableLanguageFallsBack() { yield return Wait(Localization.SetLanguageAsync("de")); Assert.That(Localization.LanguageCode,Is.EqualTo("en")); }
        [UnityTest] public IEnumerator MissingTranslationUsesConfiguredFallback() { yield return Wait(Localization.SetLanguageAsync("ru")); var t=Localization.TextAsync(new LocalizedString("Runtime Test",1002)); yield return Wait(t); Assert.That(t.Result,Is.EqualTo("Fallback")); }
        [UnityTest] public IEnumerator ExplicitEmptyTranslationStaysEmpty() { yield return Wait(Localization.SetLanguageAsync("ru")); var t=Localization.TextAsync(new LocalizedString("Runtime Test",1003),fallback:"not blank"); yield return Wait(t); Assert.That(t.Result,Is.Empty); }
        [UnityTest] public IEnumerator MissingKeyUsesCallerFallback() { var t=Localization.TextAsync(new LocalizedString("Runtime Test",9999),fallback:"Unknown"); yield return Wait(t); Assert.That(t.Result,Is.EqualTo("Unknown")); }
        [UnityTest] public IEnumerator NewestRequestWinsWhenOlderLanguageLoadsLast()
        {
            provider.DeferFrench=true;
            var older=Localization.SetLanguageAsync("fr");
            yield return null;
            var newer=Localization.SetLanguageAsync("ru"); yield return Wait(newer);
            Assert.That(provider.Finish,Is.Not.Null); provider.Finish(); yield return Wait(older);
            Assert.That(Localization.LanguageCode,Is.EqualTo("ru"));
            provider.DeferFrench=false;
        }
        [UnityTest] public IEnumerator LocalizedTextUpdatesByEventsAndLatestArguments()
        {
            var go=new GameObject("Localized text test"); go.SetActive(false);
            var text=go.AddComponent<LocalizedText>(); text.text=reference;
            go.SetActive(true); text.SetArguments("first"); text.SetArguments("last");
            yield return Wait(Localization.SetLanguageAsync("ru")); yield return null;
            Assert.That(text.Value,Is.EqualTo("Привет, last!"));
            UnityEngine.Object.Destroy(go);
        }
        [UnityTest] public IEnumerator CancellationDoesNotChangeLanguage()
        {
            using(var cts=new CancellationTokenSource())
            {
                cts.Cancel(); var task=Localization.SetLanguageAsync("ru",cts.Token);
                while(!task.IsCompleted) yield return null;
                Assert.That(task.IsCanceled,Is.True); Assert.That(Localization.LanguageCode,Is.EqualTo("en"));
            }
        }
    }
}
