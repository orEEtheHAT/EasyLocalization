using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace EasyLocalization
{
    /// <summary>Main-thread API over Unity Localization. No network or automatic persistence.</summary>
    public static class Localization
    {
        private sealed class Session
        {
            public readonly LocalizationSettings Settings;
            public readonly LocalizationConfig Configuration;
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public readonly CancellationToken Token;
            public Task Initialization;
            public Action<Locale> LocaleChanged;
            public long SelectionRequest;

            public Session(LocalizationSettings settings, LocalizationConfig config)
            {
                Settings = settings;
                Configuration = config;
                Token = Lifetime.Token;
            }
        }

        private static Session current;
        private static long revision;

        /// <summary>Language changes, including readiness of replacement settings, even for the same code.</summary>
        public static event Action<string> LanguageChanged;
        /// <summary>One refresh path for language, ready settings and explicitly changed translation data.</summary>
        public static event Action Changed;

        public static string LanguageCode => ReadLanguageCode(GetSession());
        /// <summary>Initial configuration of the current session; capture it when temporarily replacing Unity settings.</summary>
        public static LocalizationConfig Configuration => GetSession().Configuration;
        public static IReadOnlyList<Locale> Languages => GetSession().Settings.GetAvailableLocales().Locales;
        internal static long Revision => revision;
        internal static bool IsCurrentRevision(long value) => value == revision && current != null && IsCurrent(current);

        /// <summary>Shares initialization only for the same settings and configuration.</summary>
        public static Task InitializeAsync(LocalizationConfig config = null) => Initialize(GetSession(config));

        /// <summary>
        /// Detaches from the actual previous settings and cancels its pending requests.
        /// Keeps public event subscribers. Does not destroy/reset Unity settings or change the selected locale.
        /// </summary>
        public static void Reset()
        {
            var previous = current;
            current = null;
            revision++;
            if (previous == null) return;
            if (previous.LocaleChanged != null)
                previous.Settings.OnSelectedLocaleChanged -= previous.LocaleChanged;
            previous.Lifetime.Cancel();
            previous.Lifetime.Dispose();
        }

        /// <summary>Use after externally replacing settings or changing the initial configuration.</summary>
        public static Task ReinitializeAsync(LocalizationConfig config = null)
        {
            Reset();
            return InitializeAsync(config);
        }

        /// <summary>Call after changing translation data in place. Does not poll or save configuration.</summary>
        public static void Refresh() => Publish(GetSession(), false);

        private static Session GetSession(LocalizationConfig config = null)
        {
            var settings = LocalizationSettings.Instance;
            if (!settings)
                throw new InvalidOperationException("Assign valid Unity Localization Settings before initializing EasyLocalization.");
            bool differentSettings = current == null || !ReferenceEquals(current.Settings, settings);
            bool differentConfiguration = config && !ReferenceEquals(current?.Configuration, config);
            bool retry = current?.Initialization != null &&
                (current.Initialization.IsFaulted || current.Initialization.IsCanceled);
            if (differentSettings || differentConfiguration || retry)
            {
                // A new settings owner must not inherit the preceding owner's configuration.
                var configuration = config ? config : !differentSettings && current.Configuration
                    ? current.Configuration : Resources.Load<LocalizationConfig>("EasyLocalizationConfig");
                Reset();
                current = new Session(settings, configuration);
            }
            return current;
        }

        private static Task Initialize(Session session)
        {
            if (session.Initialization == null)
            {
                // Store the task before code which can synchronously notify subscribers.
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                session.Initialization = completion.Task;
                _ = InitializeCoreAsync(session, completion);
            }
            return session.Initialization;
        }

        private static async Task InitializeCoreAsync(Session session, TaskCompletionSource<bool> completion)
        {
            try
            {
                await Complete(session.Settings.GetInitializationOperation(), session);
                var locales = session.Settings.GetAvailableLocales();
                if (locales == null || locales.Locales.Count == 0)
                    throw new InvalidOperationException("No languages. Open Tools > EasyLocalization > Translations and run First setup.");
                var locale = session.Settings.GetSelectedLocaleAsync().Result;
                if (!locale)
                {
                    locale = ResolveLocale(session, null);
                    session.Settings.SetSelectedLocale(locale);
                }
                session.LocaleChanged = _ => { if (IsCurrent(session)) Publish(session, true); };
                session.Settings.OnSelectedLocaleChanged += session.LocaleChanged;
                await PreloadAsync(session, locale, session.Token);
                CheckCurrent(session);
                completion.TrySetResult(true);
                Publish(session, true);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(session.Token); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        /// <returns>The actual code. A superseded language request never commits over a newer request.</returns>
        public static async Task<string> SetLanguageAsync(string code, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var session = GetSession();
            long request = ++session.SelectionRequest;
            await WaitForInitialization(session, cancellation);
            var locale = ResolveLocale(session, code);
            await PreloadAsync(session, locale, cancellation);
            CheckCurrent(session, cancellation);
            if (request == session.SelectionRequest) session.Settings.SetSelectedLocale(locale);
            CheckCurrent(session, cancellation);
            return ReadLanguageCode(session);
        }

        public static LanguageState CaptureState() => new LanguageState(LanguageCode);
        public static Task<string> RestoreStateAsync(LanguageState state, CancellationToken cancellation = default) =>
            SetLanguageAsync(state.languageCode, cancellation);

        /// <summary>
        /// Awaits initialization and returns the latest language. Existing empty entries stay empty.
        /// Settings replacement/reset cancels this request instead of returning another session's data.
        /// </summary>
        public static Task<string> TextAsync(LocalizedString reference, object[] arguments = null,
            string fallback = null, CancellationToken cancellation = default) =>
            ResolveTextAsync(reference, arguments, fallback, false, cancellation);

        // Cache misses retain the consumer's definition/message fallback; an explicit empty entry is still valid.
        internal static Task<string> TextOrNullAsync(LocalizedString reference, CancellationToken cancellation) =>
            ResolveTextAsync(reference, null, null, true, cancellation);

        private static async Task<string> ResolveTextAsync(LocalizedString reference, object[] arguments,
            string fallback, bool nullWhenMissing, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (reference == null || reference.IsEmpty) return nullWhenMissing ? null : fallback ?? "";
            var session = GetSession();
            await WaitForInitialization(session, cancellation);
            while (true)
            {
                CheckCurrent(session, cancellation);
                long version = revision;
                var locale = reference.LocaleOverride ? reference.LocaleOverride : session.Settings.GetSelectedLocaleAsync().Result;
                var database = session.Settings.GetStringDatabase();
                var found = await Complete(database.GetTableEntryAsync(reference.TableReference,
                    reference.TableEntryReference, locale, FallbackBehavior.DontUseFallback), session, cancellation);
                if (found.Entry == null)
                {
                    var defaultLocale = ResolveLocale(session, session.Configuration ? session.Configuration.fallbackLanguage : "en");
                    if (defaultLocale != locale)
                    {
                        locale = defaultLocale;
                        found = await Complete(database.GetTableEntryAsync(reference.TableReference,
                            reference.TableEntryReference, locale, FallbackBehavior.DontUseFallback), session, cancellation);
                    }
                }
                string text;
                if (found.Entry == null) text = nullWhenMissing ? null : fallback ?? "[" + reference.TableEntryReference + "]";
                else if (string.IsNullOrEmpty(found.Entry.Value)) text = "";
                else text = await Complete(database.GetLocalizedStringAsync(reference.TableReference,
                    reference.TableEntryReference, (IList<object>)arguments ?? reference.Arguments,
                    locale, FallbackBehavior.DontUseFallback, reference), session, cancellation);
                CheckCurrent(session, cancellation);
                if (version == revision) return text;
            }
        }

        public static Task<string> TextAsync(TableReference table, TableEntryReference entry, params object[] arguments) =>
            TextAsync(new LocalizedString(table, entry), arguments);

        private static async Task WaitForInitialization(Session session, CancellationToken cancellation)
        {
            CheckCurrent(session, cancellation);
            var initialization = Initialize(session);
            if (!initialization.IsCompleted && cancellation.CanBeCanceled)
            {
                var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellation.Register(() => stopped.TrySetCanceled(cancellation)))
                {
                    var completed = await Task.WhenAny(initialization, stopped.Task);
                    await completed;
                }
            }
            else await initialization;
            CheckCurrent(session, cancellation);
        }

        internal static Task InitializeForRequestAsync(CancellationToken cancellation) =>
            WaitForInitialization(GetSession(), cancellation);

        private static Locale ResolveLocale(Session session, string code)
        {
            var locales = session.Settings.GetAvailableLocales();
            if (locales == null || locales.Locales.Count == 0)
                throw new InvalidOperationException("No languages are available. Configure Locales, then call Localization.ReinitializeAsync().");
            var locale = string.IsNullOrWhiteSpace(code) ? null : locales.GetLocale(code);
            if (!locale) locale = locales.GetLocale(session.Configuration ? session.Configuration.fallbackLanguage : "en");
            return locale ? locale : locales.Locales[0];
        }

        private static string ReadLanguageCode(Session session)
        {
            var operation = session.Settings.GetSelectedLocaleAsync();
            return operation.IsDone ? operation.Result?.Identifier.Code : null;
        }

        private static async Task PreloadAsync(Session session, Locale locale, CancellationToken cancellation)
        {
            if (!session.Configuration || session.Configuration.preloadTables == null) return;
            foreach (var table in session.Configuration.preloadTables)
            {
                CheckCurrent(session, cancellation);
                await Complete(session.Settings.GetStringDatabase().GetTableAsync(table, locale), session, cancellation);
            }
        }

        private static bool IsCurrent(Session session) => ReferenceEquals(current, session) && session.Settings &&
            ReferenceEquals(LocalizationSettings.Instance, session.Settings);

        private static void CheckCurrent(Session session, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!IsCurrent(session)) throw new OperationCanceledException("The localization session was replaced or reset.", session.Token);
            session.Token.ThrowIfCancellationRequested();
        }

        // Acquire while waiting, including cancellation; the database owns its original reference.
        private static async Task<T> Complete<T>(AsyncOperationHandle<T> operation, Session session,
            CancellationToken cancellation = default)
        {
            CheckCurrent(session, cancellation);
            Addressables.ResourceManager.Acquire(operation);
            try
            {
                if (!operation.IsDone)
                {
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action<AsyncOperationHandle<T>> finished = _ => completion.TrySetResult(true);
                    operation.Completed += finished;
                    try
                    {
                        using (session.Token.Register(() => completion.TrySetCanceled(session.Token)))
                        using (cancellation.Register(() => completion.TrySetCanceled(cancellation)))
                            await completion.Task;
                    }
                    finally { if (operation.IsValid()) operation.Completed -= finished; }
                }
                CheckCurrent(session, cancellation);
                if (operation.Status != AsyncOperationStatus.Succeeded)
                    throw operation.OperationException ?? new InvalidOperationException("Unity Localization could not load the requested data.");
                return operation.Result;
            }
            finally { if (operation.IsValid()) Addressables.Release(operation); }
        }

        private static void Publish(Session session, bool languageChanged)
        {
            if (!IsCurrent(session)) return;
            long published = ++revision;
            if (languageChanged && LanguageChanged != null)
            {
                string code = ReadLanguageCode(session);
                foreach (Action<string> handler in LanguageChanged.GetInvocationList())
                {
                    if (revision != published || !IsCurrent(session)) return;
                    try { handler(code); } catch (Exception exception) { Debug.LogException(exception); }
                }
            }
            var changed = Changed;
            if (changed == null) return;
            foreach (Action handler in changed.GetInvocationList())
            {
                if (revision != published || !IsCurrent(session)) return;
                try { handler(); } catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            Reset();
            LanguageChanged = null;
            Changed = null;
        }
    }
}
