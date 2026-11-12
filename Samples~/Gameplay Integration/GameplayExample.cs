using System;
using EasyBuilding;
using EasyInteraction;
using EasyResources;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EasyLocalization.Integrations
{
    public sealed class GameplayExample : MonoBehaviour
    {
        public GameplayTranslations translations;
        public ResourceDefinition resource;
        public BuildingDefinition building;
        public InteractionPresenter presenter;
        public InteractionTarget target;
        private Text names, prompt, result;
        private async void Start()
        {
            try
            {
                await Localization.InitializeAsync();
                var canvas=new GameObject("Gameplay translation UI",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
                canvas.transform.SetParent(transform,false); canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var scaler=canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1000,700);
                scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                if(!FindFirstObjectByType<EventSystem>())new GameObject("New Input System UI",typeof(EventSystem),typeof(InputSystemUIInputModule)).transform.SetParent(transform);
                Label(canvas.transform,"EasyLocalization + Gameplay",240,32);
                names=Label(canvas.transform,"",140,25); prompt=Label(canvas.transform,"",50,24); result=Label(canvas.transform,"",-35,24);
                Button(canvas.transform,"English",-140,()=>Select("en")); Button(canvas.transform,"Русский",-195,()=>Select("ru"));
                translations.cache.Changed+=Refresh; presenter.Changed+=OnPrompt;
                presenter.Actor.RequireVisibility=false; presenter.Actor.SelectTarget(target);
                Refresh();
            }
            catch(Exception ex){Debug.LogException(ex,this);}
        }
        private void OnDestroy(){if(translations&&translations.cache)translations.cache.Changed-=Refresh;if(presenter)presenter.Changed-=OnPrompt;}
        private async void Select(string code){try{await Localization.SetLanguageAsync(code);}catch(Exception ex){Debug.LogException(ex);}}
        private void OnPrompt(InteractionPrompt value){if(prompt)prompt.text=value.Label+" · "+value.Reason;}
        private void Refresh()
        {
            if(!names)return;
            names.text=translations.ResourceName(resource)+" / "+translations.BuildingName(building);
            result.text=translations.Resolve(PlacementResult.Fail(PlacementFailure.CannotAfford,"Not enough resources"));
            presenter.Refresh(); OnPrompt(presenter.Prompt);
        }
        private static Text Label(Transform parent,string text,float y,int size)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            var label=go.GetComponent<Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.text=text;label.fontSize=size;label.alignment=TextAnchor.MiddleCenter;
            label.rectTransform.sizeDelta=new Vector2(940,70);label.rectTransform.anchoredPosition=new Vector2(0,y);return label;
        }
        private static void Button(Transform parent,string text,float y,UnityEngine.Events.UnityAction clicked)
        {
            var go=new GameObject(text,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(320,44);rect.anchoredPosition=new Vector2(0,y);
            go.GetComponent<Image>().color=new Color(.13f,.24f,.31f);go.GetComponent<Button>().onClick.AddListener(clicked);
            var label=Label(go.transform,text,0,21);label.rectTransform.sizeDelta=rect.sizeDelta;label.raycastTarget=false;
        }
    }
}
