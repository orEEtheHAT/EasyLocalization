using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;

namespace EasyLocalization
{
    [Serializable]
    public sealed class CatalogTranslation
    {
        [Tooltip("Stable definition ID or result code, never the displayed name.")]
        public string id;
        public LocalizedString text = new LocalizedString();
    }

    [CreateAssetMenu(menuName = "EasyLocalization/Localized Catalog", fileName = "Localized Catalog")]
    public sealed class LocalizedCatalog : ScriptableObject
    {
        public CatalogTranslation[] entries = Array.Empty<CatalogTranslation>();
        public event Action Changed;

        public bool TryGet(string id, out LocalizedString text)
        {
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null && entry.id == id) { text = entry.text; return true; }
            text = null;
            return false;
        }

        public Task<string> TextAsync(string id, string fallback = null, params object[] arguments) =>
            TryGet(id, out var text) ? Localization.TextAsync(text, arguments, fallback ?? id) : Task.FromResult(fallback ?? id);

        public void SetEntries(CatalogTranslation[] values)
        {
            entries = values ?? Array.Empty<CatalogTranslation>();
            NotifyChanged();
        }

        /// <summary>Call after editing public entries in place. Caches subscribe to the same event as SetEntries.</summary>
        public void NotifyChanged()
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
