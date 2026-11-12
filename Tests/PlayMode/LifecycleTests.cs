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
    public sealed class LifecycleTests
    {
        private const string TableName = "Lifecycle Test";
        private readonly List<Context> contexts = new List<Context>();
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<Action> unsubscribe = new List<Action>();
        private LocalizationSettings previousSettings;
        private LocalizationConfig previousConfig;

        private sealed class TestLocales : ILocalesProvider
        {
            public List<Locale> LocalesList = new List<Locale>();
            public List<Locale> Locales => LocalesList;
            public Locale GetLocale(LocaleIdentifier id) => LocalesList.Find(l => l.Identifier == id);
            public void AddLocale(Locale locale) => LocalesList.Add(locale);
            public bool RemoveLocale(Locale locale) => LocalesList.Remove(locale);
        }

        private sealed class Delayed<T> : AsyncOperationBase<T> where T : LocalizationTable
        {
            public T Value;
            private bool finished;
            protected override void Execute() { }
            public void Finish()
            {
                if (finished) return;
                finished = true;
                Complete(Value, true, (string)null);
            }
        }

        private sealed class Provider : ITableProvider
        {
            public readonly Dictionary<string, StringTable> Tables = new Dictionary<string, StringTable>();
            public readonly List<Action> Pending = new List<Action>();
            public string DeferredLanguage;
            public AsyncOperationHandle<T> ProvideTableAsync<T>(string name, Locale locale) where T : LocalizationTable
            {
                if (name != TableName || !Tables.TryGetValue(locale.Identifier.Code, out var table)) return default;
                if (locale.Identifier.Code == DeferredLanguage)
                {
                    var delayed = new Delayed<T> { Value = table as T };
                    Pending.Add(delayed.Finish);
                    return Addressables.ResourceManager.StartOperation(delayed, default);
                }
                return Addressables.ResourceManager.CreateCompletedOperation(table as T, (string)null);
            }
            public void FinishAll()
            {
                DeferredLanguage = null;
                foreach (var finish in Pending) finish();
                Pending.Clear();
            }
        }

        private sealed class Context : IDisposable
        {
            public readonly LocalizationSettings Settings;
            public readonly LocalizationConfig Config;
            public readonly Provider Provider = new Provider();
            private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
            public Context(string name, bool preload, string fallback)
            {
                Settings = ScriptableObject.CreateInstance<LocalizationSettings>(); owned.Add(Settings);
                Config = ScriptableObject.CreateInstance<LocalizationConfig>(); owned.Add(Config);
                Config.fallbackLanguage = fallback;
                Config.preloadTables = preload ? new TableReference[] { TableName } : Array.Empty<TableReference>();
                var locales = new TestLocales();
                var shared = ScriptableObject.CreateInstance<SharedTableData>(); owned.Add(shared);
                shared.TableCollectionName = TableName;
                shared.AddKey("caption", 1); shared.AddKey("fallback", 2);
                foreach (string code in new[] { "en", "ru", "fr" })
                {
                    var locale = Locale.CreateLocale(code); owned.Add(locale); locales.AddLocale(locale);
                    var table = ScriptableObject.CreateInstance<StringTable>(); owned.Add(table);
                    table.SharedData = shared; table.LocaleIdentifier = code;
                    table.AddEntry(1, name + "/" + code + " {0}").IsSmart = true;
                    if (code != "fr") table.AddEntry(2, name + "/" + code + " fallback");
                    Provider.Tables.Add(code, table);
                }
                Settings.SetAvailableLocales(locales);
                Settings.SetStringDatabase(new LocalizedStringDatabase { TableProvider = Provider });
                Settings.GetStartupLocaleSelectors().Clear();
                Settings.GetStartupLocaleSelectors().Add(new SpecificLocaleSelector { LocaleId = "en" });
            }
            public void Dispose()
            {
                Provider.FinishAll();
                Settings.ResetState();
                foreach (var value in owned) UnityEngine.Object.DestroyImmediate(value);
            }
        }

        [SetUp] public void Before()
        {
            previousSettings = LocalizationSettings.Instance;
            previousConfig = Localization.Configuration;
            Localization.Reset();
        }

        [UnityTearDown] public IEnumerator After()
        {
            foreach (var action in unsubscribe) action();
            unsubscribe.Clear();
            foreach (var value in objects) if (value) UnityEngine.Object.DestroyImmediate(value);
            objects.Clear();
            Localization.Reset();
            foreach (var context in contexts) context.Dispose();
            contexts.Clear();
            LocalizationSettings.Instance = previousSettings;
            yield return Wait(Localization.ReinitializeAsync(previousConfig));
        }

        private Context Activate(string name, bool preload = false, string fallback = "en")
        {
            var context = new Context(name, preload, fallback);
            contexts.Add(context);
            LocalizationSettings.Instance = context.Settings; // Intentionally no EasyLocalization.Reset here.
            return context;
        }

        private static IEnumerator Wait(Task task, bool cancelled = false)
        {
            float end = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Localization request did not complete within 10 seconds.");
            if (cancelled) Assert.That(task.IsCanceled, Is.True, task.Exception?.ToString());
            else
            {
                if (task.IsFaulted) throw task.Exception.GetBaseException();
                Assert.That(task.IsCanceled, Is.False, "Unexpected request cancellation.");
            }
        }

        private static IEnumerator Until(Func<bool> predicate)
        {
            float end = Time.realtimeSinceStartup + 10;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, "Expected event-driven presentation was not delivered.");
        }

        private LocalizedText CreateText()
        {
            var go = new GameObject("Lifecycle text"); objects.Add(go); go.SetActive(false);
            var text = go.AddComponent<LocalizedText>(); text.text = new LocalizedString(TableName, 1);
            text.SetArguments("latest"); go.SetActive(true); return text;
        }

        [UnityTest] public IEnumerator SettingsReplacementAutomaticallyUsesNewConfigurationAndDatabase()
        {
            var a = Activate("A", true); var first = Localization.InitializeAsync(a.Config); yield return Wait(first);
            var b = Activate("B", true, "ru"); var second = Localization.InitializeAsync(b.Config); yield return Wait(second);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(Localization.Configuration, Is.SameAs(b.Config));
            yield return Wait(Localization.SetLanguageAsync("fr"));
            var text = Localization.TextAsync(new LocalizedString(TableName, 2)); yield return Wait(text);
            Assert.That(text.Result, Is.EqualTo("B/ru fallback"));
        }

        [UnityTest] public IEnumerator ReplacedPendingLanguageIsCancelledBeforeOldLoadFinishes()
        {
            var a = Activate("A", true); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.DeferredLanguage = "ru";
            var old = Localization.SetLanguageAsync("ru");
            Assert.That(a.Provider.Pending.Count, Is.EqualTo(1));
            var b = Activate("B", true); yield return Wait(Localization.InitializeAsync(b.Config));
            yield return Wait(old, true);
            a.Provider.FinishAll(); yield return null;
            Assert.That(Localization.LanguageCode, Is.EqualTo("en"));
            var text = Localization.TextAsync(new LocalizedString(TableName, 1), new object[] { "latest" }); yield return Wait(text);
            Assert.That(text.Result, Is.EqualTo("B/en latest"));
        }

        [UnityTest] public IEnumerator ResetCancelsPendingStringAndPreservesPublicObservers()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.DeferredLanguage = "en";
            var old = Localization.TextAsync(new LocalizedString(TableName, 1), new object[] { "old" });
            int notifications = 0; Action changed = () => notifications++;
            Localization.Changed += changed; unsubscribe.Add(() => Localization.Changed -= changed);
            Localization.Reset(); yield return Wait(old, true);
            var b = Activate("B"); yield return Wait(Localization.ReinitializeAsync(b.Config));
            Assert.That(notifications, Is.EqualTo(1));
            a.Provider.FinishAll(); yield return null;
            Assert.That(notifications, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator OldSettingsCallbacksAreDetachedAndIgnored()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            int notifications = 0; Action<string> changed = _ => notifications++;
            Localization.LanguageChanged += changed; unsubscribe.Add(() => Localization.LanguageChanged -= changed);
            var b = Activate("B"); yield return Wait(Localization.InitializeAsync(b.Config));
            int ready = notifications;
            a.Settings.ForceRefresh(); yield return null;
            Assert.That(notifications, Is.EqualTo(ready));
            b.Settings.ForceRefresh(); yield return null;
            Assert.That(notifications, Is.EqualTo(ready + 1));
        }

        [UnityTest] public IEnumerator ReinitializeSameSettingsChangesFallbackWithoutDuplicatingCallbacks()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            yield return Wait(Localization.SetLanguageAsync("fr"));
            var config = ScriptableObject.CreateInstance<LocalizationConfig>(); objects.Add(config); config.fallbackLanguage = "ru";
            for (int i = 0; i < 3; i++) yield return Wait(Localization.ReinitializeAsync(config));
            int notifications = 0; Action changed = () => notifications++;
            Localization.Changed += changed; unsubscribe.Add(() => Localization.Changed -= changed);
            a.Settings.ForceRefresh(); yield return null;
            Assert.That(notifications, Is.EqualTo(1));
            var text = Localization.TextAsync(new LocalizedString(TableName, 2)); yield return Wait(text);
            Assert.That(text.Result, Is.EqualTo("A/ru fallback"));
        }

        [UnityTest] public IEnumerator EarlierInitializationsCannotNotifyAfterThirdSettingsBecomeReady()
        {
            var a = Activate("A", true); a.Provider.DeferredLanguage = "en"; var first = Localization.InitializeAsync(a.Config);
            var b = Activate("B", true); b.Provider.DeferredLanguage = "en"; var second = Localization.InitializeAsync(b.Config);
            int notifications = 0; Action changed = () => notifications++;
            Localization.Changed += changed; unsubscribe.Add(() => Localization.Changed -= changed);
            var c = Activate("C", true); yield return Wait(Localization.InitializeAsync(c.Config));
            yield return Wait(first, true); yield return Wait(second, true);
            a.Provider.FinishAll(); b.Provider.FinishAll(); yield return null;
            Assert.That(notifications, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator CallerCancellationStopsWaitingWithoutChangingLanguage()
        {
            var a = Activate("A", true); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.DeferredLanguage = "ru";
            using (var cancellation = new CancellationTokenSource())
            {
                var request = Localization.SetLanguageAsync("ru", cancellation.Token);
                cancellation.Cancel(); yield return Wait(request, true);
                Assert.That(Localization.LanguageCode, Is.EqualTo("en"));
            }
            a.Provider.FinishAll();
            yield return Wait(Localization.SetLanguageAsync("ru"));
            Assert.That(Localization.LanguageCode, Is.EqualTo("ru"));
        }

        [UnityTest] public IEnumerator DisabledTextCannotPublishLateResultAndCanBeReenabled()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.DeferredLanguage = "en";
            var text = CreateText(); int changes = 0; text.textChanged.AddListener(_ => changes++);
            text.enabled = false; a.Provider.FinishAll();
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(changes, Is.Zero);
            text.enabled = true; yield return Until(() => text.Value == "A/en latest");
            Assert.That(changes, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator OnlyLatestArgumentsPublishAfterDeferredLoad()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.DeferredLanguage = "en"; var text = CreateText(); var values = new List<string>();
            text.textChanged.AddListener(values.Add);
            text.SetArguments("old"); text.SetArguments("new"); a.Provider.FinishAll();
            yield return Until(() => text.Value == "A/en new");
            Assert.That(values, Is.EqualTo(new[] { "A/en new" }));
        }

        [UnityTest] public IEnumerator DataAndLanguageRefreshUseTheSameTextNotificationPath()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            var text = CreateText(); yield return Until(() => text.Value == "A/en latest");
            a.Provider.Tables["en"].GetEntry(1).Value = "Updated {0}";
            Localization.Refresh(); yield return Until(() => text.Value == "Updated latest");
            yield return Wait(Localization.SetLanguageAsync("ru"));
            yield return Until(() => text.Value == "A/ru latest");
        }

        [UnityTest] public IEnumerator CatalogDataReplacementAndLanguageUseOneChangedEvent()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            var catalog = ScriptableObject.CreateInstance<LocalizedCatalog>(); objects.Add(catalog);
            catalog.SetEntries(new[] { new CatalogTranslation { id = "resource.stable-id", text = new LocalizedString(TableName, 2) } });
            var go = new GameObject("Catalog cache"); objects.Add(go); go.SetActive(false);
            var cache = go.AddComponent<LocalizedCatalogCache>(); cache.catalog = catalog; go.SetActive(true);
            yield return Until(() => cache.Get("resource.stable-id", "fallback") == "A/en fallback");
            int changes = 0; cache.Changed += () => changes++;
            a.Provider.Tables["en"].GetEntry(2).Value = "New definition name";
            catalog.NotifyChanged(); yield return Until(() => cache.Get("resource.stable-id", "fallback") == "New definition name");
            yield return Wait(Localization.SetLanguageAsync("ru"));
            yield return Until(() => cache.Get("resource.stable-id", "fallback") == "A/ru fallback");
            Assert.That(changes, Is.EqualTo(2));
            cache.Catalog = null;
            Assert.That(cache.Get("resource.stable-id", "fallback"), Is.EqualTo("fallback"));
            Assert.That(changes, Is.EqualTo(3));
        }

        [UnityTest] public IEnumerator CacheDoesNotServeThePreviousSettingsSnapshot()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            var catalog = ScriptableObject.CreateInstance<LocalizedCatalog>(); objects.Add(catalog);
            catalog.SetEntries(new[] { new CatalogTranslation { id = "stable", text = new LocalizedString(TableName, 2) } });
            var go = new GameObject("Cache"); objects.Add(go); go.SetActive(false);
            var cache = go.AddComponent<LocalizedCatalogCache>(); cache.catalog = catalog; go.SetActive(true);
            yield return Until(() => cache.Get("stable", "fallback") == "A/en fallback");
            var b = Activate("B");
            Assert.That(cache.Get("stable", "fallback"), Is.EqualTo("fallback"));
            yield return Wait(Localization.InitializeAsync(b.Config));
            yield return Until(() => cache.Get("stable", "fallback") == "B/en fallback");
        }

        [UnityTest] public IEnumerator MissingCatalogTranslationUsesCallerFallbackButExplicitEmptyStaysEmpty()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.Tables["en"].GetEntry(2).Value = "";
            var catalog = ScriptableObject.CreateInstance<LocalizedCatalog>(); objects.Add(catalog);
            catalog.SetEntries(new[] {
                new CatalogTranslation { id = "empty", text = new LocalizedString(TableName, 2) },
                new CatalogTranslation { id = "missing", text = new LocalizedString(TableName, 999) },
                new CatalogTranslation { id = "unassigned" }
            });
            var go = new GameObject("Fallback cache"); objects.Add(go); go.SetActive(false);
            var cache = go.AddComponent<LocalizedCatalogCache>(); cache.catalog = catalog; go.SetActive(true);
            yield return Until(() => cache.IsReady);
            Assert.That(cache.Get("empty", "Definition name"), Is.Empty);
            Assert.That(cache.Get("missing", "Definition name"), Is.EqualTo("Definition name"));
            Assert.That(cache.Get("unassigned", "Definition name"), Is.EqualTo("Definition name"));
            Assert.That(cache.Get("unknown", "Definition name"), Is.EqualTo("Definition name"));
        }

        [UnityTest] public IEnumerator DisabledCacheCancelsPendingSnapshotAndRebindsOnEnable()
        {
            var a = Activate("A"); yield return Wait(Localization.InitializeAsync(a.Config));
            a.Provider.DeferredLanguage = "en";
            var catalog = ScriptableObject.CreateInstance<LocalizedCatalog>(); objects.Add(catalog);
            catalog.SetEntries(new[] { new CatalogTranslation { id = "stable", text = new LocalizedString(TableName, 2) } });
            var go = new GameObject("Disabled cache"); objects.Add(go); go.SetActive(false);
            var cache = go.AddComponent<LocalizedCatalogCache>(); cache.catalog = catalog;
            int changes = 0; cache.Changed += () => changes++;
            go.SetActive(true); cache.enabled = false; a.Provider.FinishAll();
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(changes, Is.Zero);
            Assert.That(cache.Get("stable", "fallback"), Is.EqualTo("fallback"));
            cache.enabled = true;
            yield return Until(() => cache.Get("stable", "fallback") == "A/en fallback");
            Assert.That(changes, Is.EqualTo(1));
            cache.Catalog = null;
            int detached = changes; catalog.NotifyChanged(); yield return null;
            Assert.That(changes, Is.EqualTo(detached));
        }

        [UnityTest] public IEnumerator CancellingCallerDuringInitializationDoesNotCancelSharedInitialization()
        {
            var a = Activate("A", true); a.Provider.DeferredLanguage = "en";
            var initialization = Localization.InitializeAsync(a.Config);
            using (var cancellation = new CancellationTokenSource())
            {
                var text = Localization.TextAsync(new LocalizedString(TableName, 2), cancellation: cancellation.Token);
                cancellation.Cancel(); yield return Wait(text, true);
                Assert.That(initialization.IsCompleted, Is.False);
            }
            a.Provider.FinishAll(); yield return Wait(initialization);
            var next = Localization.TextAsync(new LocalizedString(TableName, 2)); yield return Wait(next);
            Assert.That(next.Result, Is.EqualTo("A/en fallback"));
        }
    }
}
