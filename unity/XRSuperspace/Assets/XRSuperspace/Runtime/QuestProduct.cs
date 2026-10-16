using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace XRSuperspace
{
    [Serializable]
    public sealed class QuestPreferences
    {
        public int TextSize,UiSize,View,PanelHeight,Controllers;
        public bool HighContrast,ReducedMotion,AlternativePalette;
        public bool GridVisible;
        public DiffractionDisplay Diffraction=new DiffractionDisplay();
        const string Key="AperiTrace.Comfort.v1";
        public float TextScale=>new[]{1f,1.18f,1.35f}[TextSize];
        public float UiScale=>new[]{1f,1.08f,1.16f}[UiSize];
        public void Validate()
        {
            TextSize=Mathf.Clamp(TextSize,0,2);UiSize=Mathf.Clamp(UiSize,0,2);
            View=Mathf.Clamp(View,0,3);PanelHeight=Mathf.Clamp(PanelHeight,0,2);Controllers=Mathf.Clamp(Controllers,0,2);
            try {Diffraction??=new DiffractionDisplay();Diffraction.Validate();}catch {Diffraction=new DiffractionDisplay();}
        }
        public static QuestPreferences Load()
        {
            QuestPreferences value;
            try {value=JsonConvert.DeserializeObject<QuestPreferences>(PlayerPrefs.GetString(Key,"{}"))??new QuestPreferences();}
            catch {value=new QuestPreferences();}
            value.Validate();return value;
        }
        public void Save() {Validate();PlayerPrefs.SetString(Key,JsonConvert.SerializeObject(this));PlayerPrefs.Save();}
#if UNITY_EDITOR
        public static string Snapshot()=>PlayerPrefs.HasKey(Key)?PlayerPrefs.GetString(Key):null;
        public static void Restore(string json) {if(json==null) PlayerPrefs.DeleteKey(Key);else PlayerPrefs.SetString(Key,json);PlayerPrefs.Save();}
#endif
    }
    public static class QuestProductInput
    {
        public static bool Allows(Transform source,ScientificDemo demo)
        {
            if(demo==null) return true;if(demo.Lifecycle.Suspended) return false;
            int mode=demo.Preferences.Controllers;if(mode==0) return true;
            for(var node=source;node!=null;node=node.parent)
            {
                if(node.name=="Left Controller") return mode==1;
                if(node.name=="Right Controller") return mode==2;
                if(node.GetComponent<HandPointer>() is HandPointer hand) return mode==(hand.Left?1:2);
            }
            return true;
        }
    }

    public static class QuestUiTheme
    {
        // Unity objects can be detached while their managed reference survives.
        // Never use ?. on EventSystem's selected GameObject before GetComponent.
        public static bool HasTextInputFocus()
        {
            var events=EventSystem.current;if(events==null) return false;
            var selected=events.currentSelectedGameObject;
            return selected!=null&&selected.GetComponent<InputField>()!=null;
        }
        public static void ClearSelectionWithin(Transform retiring)
        {
            var events=EventSystem.current;if(events==null||retiring==null) return;
            var selected=events.currentSelectedGameObject;
            if(selected!=null&&selected.transform.IsChildOf(retiring)) events.SetSelectedGameObject(null);
        }
        public static readonly Color Background=DesktopTheme.Panel;
        public static readonly Color Text=new Color(.94f,.97f,1);
        public static readonly Color Secondary=new Color(.75f,.82f,.88f);
        public static readonly Color Accent=new Color(.06f,.43f,.40f);
        public static readonly Color Surface=new Color(.10f,.15f,.21f);
        public static readonly Color Destructive=new Color(.40f,.075f,.10f);
        static Font regular,medium,semibold;
        public static Font Font(int weight=0)
        {
            regular??=Resources.Load<Font>("QuestFonts/Inter-Regular");
            medium??=Resources.Load<Font>("QuestFonts/Inter-Medium");
            semibold??=Resources.Load<Font>("QuestFonts/Inter-SemiBold");
            return (weight==2?semibold:weight==1?medium:regular)??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        public static void Style(Text label,int size,int weight=0)
        {
            label.font=Font(weight);label.fontStyle=FontStyle.Normal;label.fontSize=size;
            label.color=Text;label.raycastTarget=false;label.supportRichText=false;
            label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;
            label.lineSpacing=1.12f;
        }
        public static void Button(Button button,bool primary=false,bool destructive=false,bool contrast=false)
        {
            var image=button.GetComponent<Image>();image.color=destructive?Destructive:primary?Accent:contrast?new Color(.20f,.25f,.30f):Surface;
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.06f,1.06f,1.06f);
            colors.selectedColor=new Color(1.10f,1.10f,1.10f);colors.pressedColor=new Color(.72f,.85f,.85f);
            colors.disabledColor=new Color(.65f,.65f,.65f,.65f);colors.colorMultiplier=1;
            colors.fadeDuration=contrast?0:.08f;button.colors=colors;button.navigation=new Navigation {mode=Navigation.Mode.Automatic};
            if(button.GetComponent<QuestFocusFeedback>()==null) button.gameObject.AddComponent<QuestFocusFeedback>();
        }
    }
    public sealed class QuestFocusFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
    {
        Outline outline;
        Button button;
        bool hovered,selected;
        void Awake()
        {
            button=GetComponent<Button>();outline=gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(.94f,.97f,1,.85f);outline.effectDistance=new Vector2(2,-2);outline.useGraphicAlpha=false;outline.enabled=false;
        }
        void Update()=>outline.enabled=(hovered||selected)&&button.IsInteractable();
        public void OnPointerEnter(PointerEventData e)=>hovered=true;
        public void OnPointerExit(PointerEventData e)=>hovered=false;
        public void OnSelect(BaseEventData e)=>selected=true;
        public void OnDeselect(BaseEventData e)=>selected=false;
        void OnDisable() {hovered=selected=false;if(outline!=null) outline.enabled=false;}
    }

    public static class LocalSessionStore
    {
        public static string Save(string folder,string json,Action<string> beforePublish=null)
        {
            if(json==null||Encoding.UTF8.GetByteCount(json)>16*1024*1024) throw new FormatException("Session exceeds the local size limit.");
            JObject.Parse(json); // Serialize and validate before creating any file.
            Directory.CreateDirectory(folder);
            string name="demo-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
            string target=Path.Combine(folder,name+".json"),temporary=Path.Combine(folder,name+".part");
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    byte[] bytes=new UTF8Encoding(false).GetBytes(json);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
                beforePublish?.Invoke(temporary);File.Move(temporary,target);return target;
            }
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
        }
        public static string[] List(string folder)=>Directory.Exists(folder)?Directory.GetFiles(folder,"demo-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(200).ToArray():Array.Empty<string>();
        public static string Read(string folder,string path)
        {
            string root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar,full=Path.GetFullPath(path);
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(full)!=Path.GetFullPath(folder)||
                !Path.GetFileName(full).StartsWith("demo-",StringComparison.Ordinal)||Path.GetExtension(full)!=".json")
                throw new IOException("Choose a session from this app's local session list.");
            if(new FileInfo(full).Length>16*1024*1024) throw new FormatException("Session exceeds the local size limit.");
            return File.ReadAllText(full);
        }
    }

    [DefaultExecutionOrder(-100)]
    public sealed class QuestLifecycle : MonoBehaviour
    {
        public ScientificDemo Demo;
        bool paused,focused=true;
        public bool Suspended=>paused||!focused;
        public void SetPaused(bool value) {paused=value;Protect();}
        public void SetFocused(bool value) {focused=value;Protect();}
        void Protect() {if(Suspended) Demo?.Lab?.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();}
        void OnApplicationPause(bool value)=>SetPaused(value);
        void OnApplicationFocus(bool value)=>SetFocused(value);
    }
}
