using System;
using UnityEngine;

namespace EasyLocalization
{
    /// <summary>Shared cache binding for optional typed adapters. Changed means names/messages may be read again.</summary>
    [RequireComponent(typeof(LocalizedCatalogCache))]
    public abstract class LocalizedCatalogAdapter : MonoBehaviour
    {
        public LocalizedCatalogCache cache;
        private LocalizedCatalogCache subscribedCache;
        public event Action Changed;

        public LocalizedCatalogCache Cache
        {
            get => cache;
            set
            {
                if (ReferenceEquals(cache, value)) return;
                cache = value;
                BindCache();
                if (isActiveAndEnabled) OnTranslationsChanged();
            }
        }

        protected virtual void OnEnable() { BindCache(); OnTranslationsChanged(); }
        protected virtual void OnDisable()
        {
            if (!ReferenceEquals(subscribedCache, null)) subscribedCache.Changed -= OnTranslationsChanged;
            subscribedCache = null;
        }

        private void BindCache()
        {
            if (!cache) cache = GetComponent<LocalizedCatalogCache>();
            var next = isActiveAndEnabled ? cache : null;
            if (ReferenceEquals(subscribedCache, next)) return;
            if (!ReferenceEquals(subscribedCache, null)) subscribedCache.Changed -= OnTranslationsChanged;
            subscribedCache = next;
            if (subscribedCache) subscribedCache.Changed += OnTranslationsChanged;
        }

        public string Translate(string key, string fallback) => cache ? cache.Get(key, fallback) : fallback;

        /// <summary>Rebinds a directly assigned cache field and requests a new complete translation snapshot.</summary>
        public void Refresh()
        {
            BindCache();
            if (!isActiveAndEnabled) return;
            if (cache) cache.Refresh(); else OnTranslationsChanged();
        }

        protected virtual void OnTranslationsChanged()
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                if (!this || !isActiveAndEnabled) return;
                try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }
    }
}
