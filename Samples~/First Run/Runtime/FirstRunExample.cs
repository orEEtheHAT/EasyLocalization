using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace EasyLocalization.Samples
{
    public sealed class FirstRunExample : MonoBehaviour
    {
        public TableReference table;
        private LocalizedText score;
        private int points = 7;
        private LanguageState saved;
        private Text state;

        private async void Start()
        {
            try
            {
                await Localization.InitializeAsync();
                var canvas = new GameObject("Localization UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas.transform.SetParent(transform, false);
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1000, 700);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                if (!FindFirstObjectByType<EventSystem>()) new GameObject("UI Input (New Input System)", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform);
                TextAt(canvas.transform, "EasyLocalization", 270, 38);
                Bind(TextAt(canvas.transform, "", 190, 28), 1001);
                score = Bind(TextAt(canvas.transform, "", 130, 30), 1002); score.SetArguments(points);
                TextAt(canvas.transform, "Choose language / Выберите язык", 65, 22);
                int index = 0;
                foreach (var locale in Localization.Languages)
                {
                    string code = locale.Identifier.Code;
                    string label = locale.Identifier.CultureInfo?.NativeName ?? locale.LocaleName;
                    if (!string.IsNullOrEmpty(label)) label = char.ToUpperInvariant(label[0]) + label.Substring(1);
                    ButtonAt(canvas.transform, label + " (" + code + ")", -10 - 48 * index++, () => Select(code));
                }
                float y = -20 - 48 * index;
                ButtonAt(canvas.transform, "+1", y, () => score.SetArguments(++points));
                ButtonAt(canvas.transform, "Capture language", y - 48, () => { saved = Localization.CaptureState(); state.text = JsonUtility.ToJson(saved); });
                ButtonAt(canvas.transform, "Restore language", y - 96, () => Restore());
                state = TextAt(canvas.transform, "Language choice belongs to runtime state", y - 145, 17);
            }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private LocalizedText Bind(Text label, long id)
        {
            var binding = label.gameObject.AddComponent<LocalizedText>();
            binding.text = new LocalizedString(table, id);
            binding.textChanged.AddListener(value => label.text = value);
            binding.Refresh(); return binding;
        }
        private async void Select(string code) { try { await Localization.SetLanguageAsync(code); } catch (Exception ex) { Debug.LogException(ex); } }
        private async void Restore() { try { await Localization.RestoreStateAsync(saved); } catch (Exception ex) { Debug.LogException(ex); } }

        public static Text TextAt(Transform parent, string value, float y, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(900, 44); rect.anchoredPosition = new Vector2(0, y);
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            return text;
        }

        private static void ButtonAt(Transform parent, string label, float y, UnityAction click)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(340, 40); rect.anchoredPosition = new Vector2(0, y);
            go.GetComponent<Image>().color = new Color(.13f, .24f, .31f);
            go.GetComponent<Button>().onClick.AddListener(click);
            var text = TextAt(go.transform, label, 0, 19); text.rectTransform.sizeDelta = rect.sizeDelta; text.raycastTarget = false;
        }
    }
}
