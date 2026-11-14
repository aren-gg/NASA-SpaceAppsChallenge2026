using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace MarsWeather
{
    /// <summary>Demo UI only. Do not include this component in the merged gameplay scene.</summary>
    public sealed class WeatherPreview : MonoBehaviour
    {
        public WeatherController weather;
        Text heading, detail;
        readonly string[] names={"CLEAR","SOLAR FLARE","DUST STORM","HEAT STORM","COLD NIGHT"};
        readonly string[] notes={"Clear weather • soft daylight and a muted mineral sky", "Solar particle alert • sky glow is an artistic warning, not a radiation measurement", "Windblown dust • layered haze, reduced visibility and warm scattering", "Heat wave • dense rising flecks, tall thermal wisps and subtle sky shimmer", "Cold night • dim blue light, distant stars and a dark violet horizon"};
        void Start()
        {
            var canvasObject=new GameObject("Preview controls",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform,false);
            canvasObject.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1440,900);
            var root=canvasObject.transform;
            if(FindObjectOfType<EventSystem>()==null) { var events=new GameObject("Preview input",typeof(EventSystem),typeof(StandaloneInputModule)); events.transform.SetParent(transform); }
            heading=Text(root,"WEATHER LAB",36,new Vector2(.04f,.85f),new Vector2(.8f,.93f));
            detail=Text(root,notes[0],17,new Vector2(.04f,.78f),new Vector2(.9f,.85f));
            Text(root,"MARS  /  ATMOSPHERE STUDIES",13,new Vector2(.04f,.94f),new Vector2(.65f,.99f));
            for(int i=0;i<5;i++)
            {
                int index=i;
                var go=new GameObject(names[i],typeof(RectTransform),typeof(Image),typeof(Button)); go.transform.SetParent(root,false);
                Place((RectTransform)go.transform,new Vector2(.04f+i*.185f,.075f),new Vector2(.21f+i*.185f,.14f));
                go.GetComponent<Image>().color=new Color(.14f,.10f,.18f,.93f);
                go.GetComponent<Button>().onClick.AddListener(()=>weather.Activate((WeatherEvent)index,weather.intensity));
                Text(go.transform,(i+1)+"  "+names[i],16,Vector2.zero,Vector2.one).alignment=TextAnchor.MiddleCenter;
            }
            Text(root,"1–5 switch event  •  3-second transitions  •  reusable weather prefab",14,new Vector2(.04f,.025f),new Vector2(.95f,.065f));
        }
        void Update()
        {
            for(int i=0;i<5;i++) if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i))) weather.Activate((WeatherEvent)i);
            if(heading!=null) { heading.text=names[(int)weather.Current]; detail.text=notes[(int)weather.Current]; }
        }
        static Text Text(Transform parent,string text,int size,Vector2 min,Vector2 max)
        {
            var go=new GameObject(text,typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent,false); Place((RectTransform)go.transform,min,max);
            var label=go.GetComponent<Text>(); label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize=size; label.text=text;
            label.color=new Color(1,.87f,.78f); label.alignment=TextAnchor.MiddleLeft; label.raycastTarget=false; return label;
        }
        static void Place(RectTransform r,Vector2 min,Vector2 max) {r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;}
    }
}
