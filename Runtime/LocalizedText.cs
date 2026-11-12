using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;

namespace EasyLocalization
{
    [AddComponentMenu("EasyLocalization/Localized Text")]
    public sealed class LocalizedText : MonoBehaviour
    {
        public LocalizedString text = new LocalizedString();
        public string fallback = "";
        public UnityEvent<string> textChanged = new UnityEvent<string>();
        private object[] arguments;
        private int generation;
        private CancellationTokenSource pending;
        public string Value { get; private set; } = "";

        private void OnEnable() { Localization.Changed += Refresh; Refresh(); }
        private void OnDisable() { Localization.Changed -= Refresh; generation++; CancelPending(); }
        public void SetArguments(params object[] values) { arguments = values; Refresh(); }

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
            if (!isActiveAndEnabled) return;
            var request = pending = new CancellationTokenSource();
            try
            {
                var value = await Localization.TextAsync(text, arguments, fallback, request.Token);
                if (!this || !isActiveAndEnabled || version != generation) return;
                Value = value;
                textChanged.Invoke(value);
            }
            finally
            {
                if (ReferenceEquals(pending, request)) pending = null;
                request.Dispose();
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
