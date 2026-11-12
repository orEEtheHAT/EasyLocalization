using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyLocalization
{
    /// <summary>Publishes a complete snapshot. Consumers use Changed for both catalog and language updates.</summary>
    [AddComponentMenu("EasyLocalization/Localized Catalog Cache")]
    public sealed class LocalizedCatalogCache : MonoBehaviour
    {
        public LocalizedCatalog catalog;
        private LocalizedCatalog subscribedCatalog;
        private Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        private int generation;
        private long appliedRevision = -1;
        private CancellationTokenSource pending;

        public event Action Changed;
        public LocalizedCatalog Catalog
        {
            get => catalog;
            set
            {
                if (ReferenceEquals(catalog, value)) return;
                catalog = value;
                Refresh();
            }
        }

        public bool IsReady => isActiveAndEnabled && Localization.IsCurrentRevision(appliedRevision);
        public string Get(string id, string fallback) =>
            IsReady && id != null && values.TryGetValue(id, out var text) ? text : fallback;

        private void OnEnable()
        {
            Localization.Changed += Refresh;
            BindCatalog();
            Refresh();
        }

        private void OnDisable()
        {
            Localization.Changed -= Refresh;
            if (!ReferenceEquals(subscribedCatalog, null)) subscribedCatalog.Changed -= Refresh;
            subscribedCatalog = null;
            generation++;
            CancelPending();
            values.Clear();
            appliedRevision = -1;
        }

        private void BindCatalog()
        {
            if (ReferenceEquals(subscribedCatalog, catalog)) return;
            if (!ReferenceEquals(subscribedCatalog, null)) subscribedCatalog.Changed -= Refresh;
            subscribedCatalog = isActiveAndEnabled ? catalog : null;
            if (subscribedCatalog) subscribedCatalog.Changed += Refresh;
        }

        public async void Refresh()
        {
            var operation = RefreshAsync();
            int version = generation;
            try { await operation; }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (this && isActiveAndEnabled && version == generation) Debug.LogException(exception, this); }
        }

        public async Task RefreshAsync()
        {
            int version = ++generation;
            CancelPending();
            BindCatalog();
            if (!isActiveAndEnabled) return;
            var source = catalog;
            if (!source)
            {
                values.Clear();
                appliedRevision = -1;
                Publish();
                return;
            }
            var request = pending = new CancellationTokenSource();
            var token = request.Token;
            try
            {
                await Localization.InitializeForRequestAsync(token);
                token.ThrowIfCancellationRequested();
                long revision = Localization.Revision;
                var next = new Dictionary<string, string>(StringComparer.Ordinal);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in source.entries ?? Array.Empty<CatalogTranslation>())
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
                        throw new InvalidOperationException("Catalog IDs must be nonempty and unique. Fix the Localized Catalog entries.");
                    var value = await Localization.TextOrNullAsync(entry.text, token);
                    if (value != null) next.Add(entry.id, value);
                }
                if (!this || !isActiveAndEnabled || version != generation || !ReferenceEquals(source, catalog) ||
                    !Localization.IsCurrentRevision(revision)) return;
                values = next;
                appliedRevision = revision;
                Publish();
            }
            finally
            {
                if (ReferenceEquals(pending, request)) pending = null;
                request.Dispose();
            }
        }

        private void Publish()
        {
            var handlers = Changed;
            if (handlers == null) return;
            int version = generation;
            foreach (Action handler in handlers.GetInvocationList())
            {
                if (!this || !isActiveAndEnabled || version != generation) return;
                try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }

        private void CancelPending()
        {
            var previous = pending;
            pending = null;
            previous?.Cancel();
            previous?.Dispose();
        }
    }
}
