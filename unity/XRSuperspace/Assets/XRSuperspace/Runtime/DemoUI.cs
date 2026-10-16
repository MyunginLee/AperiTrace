using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRSuperspace
{
    // World-space UI remains in the camera capture and in the existing XR rig.
    public sealed class DemoUI : MonoBehaviour
    {
        public ScientificDemo Demo;
        Text story,provenance,performance,presetLabel;
        public GameObject Controls {get;private set;}
        Font font;
        int preset;
        float nextRefresh;
        GameObject Node(string name,Transform parent,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
            var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);return go;
        }
        Text Label(string name,Transform parent,float x,float y,float width,float height,int size=22)
        {
            var t=Node(name,parent,x,y,width,height).AddComponent<Text>();t.font=font;t.fontSize=size;t.color=new Color(.9f,.94f,.96f);
            t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;t.raycastTarget=false;return t;
        }
        void Button(string name,float x,float y,float width,Action action)
        {
            var go=Node(name,Controls.transform,x,y,width,42);go.AddComponent<Image>().color=new Color(.12f,.2f,.24f);
            go.AddComponent<Button>().onClick.AddListener(()=>Demo.Guard(action));var t=Label(name+" label",go.transform,0,0,width,42,23);t.text=name;t.alignment=TextAnchor.MiddleCenter;
        }
        void Awake()
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Demo.Instrument.Camera;
            var rect=GetComponent<RectTransform>();rect.sizeDelta=new Vector2(2400,750);rect.pivot=new Vector2(.5f,1);rect.localScale=Vector3.one*.0022f;rect.anchoredPosition3D=Demo.Settings.PanelPosition;
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();gameObject.AddComponent<Image>().color=new Color(.04f,.055f,.065f,.98f);
            story=Label("Scientific narrative",transform,22,16,2356,196,34);
            provenance=Label("Always-visible provenance and failures",transform,22,220,2356,320,28);
            performance=Label("Observed performance only",transform,22,550,2356,68,26);
            Controls=Node("Demo controls",transform,22,625,2356,115);
            Button("Guide",0,0,145,()=>Demo.StartGuide(false));Button("Free",155,0,145,Demo.FreeMode);
            Button("Exhibition",310,0,180,()=>Demo.SetExhibition(!Demo.Exhibition));Button("Next",500,0,145,()=>Demo.Advance());
            Button("Auto / pause",655,0,200,()=>Demo.AutoAdvance=!Demo.AutoAdvance);
            Button("Palette",865,0,145,()=>Demo.SetPalette(!Demo.AccessiblePalette));Button("CPU validate",1020,0,210,Demo.ValidateCpu);
            Button("Save demo",1240,0,185,Demo.SaveSession);Button("Load demo",1435,0,185,Demo.LoadLastSession);
            Button("Canonical reset",1630,0,250,Demo.ResetCanonical);
            var input=Node("Diffraction threshold [0,1]",Controls.transform,0,52,150,42);input.AddComponent<Image>().color=new Color(.12f,.2f,.24f);
            var field=input.AddComponent<InputField>();field.textComponent=Label("Threshold text",input.transform,8,2,134,40,24);field.text="0";
            Button("Set I/N^2 threshold",160,52,290,()=>Demo.Instrument.Reciprocal.SetIntensityThreshold(double.Parse(field.text,NumberStyles.Float,CultureInfo.InvariantCulture)));
            Button("Performance",460,52,210,()=>Demo.PerformanceOverlay=!Demo.PerformanceOverlay);
            Button("Next preset",680,52,210,()=> {preset=(preset+1)%Demo.Settings.Captures.Length;Refresh();});
            Button("Capture preset",900,52,240,()=>Demo.Capture.CapturePreset(Demo.Settings.Captures[preset]));
            presetLabel=Label("Selected capture preset",Controls.transform,1160,52,1180,60,22);
        }
        public string PerformanceText()
        {
            if(!Demo.PerformanceOverlay) return "Performance overlay OFF. No learning/perception/accessibility validation; no Quest result.";
            var p=Demo.FrozenPerformance;
            if(p==null) return "Performance ON: observational sample not yet frozen. Not Quest throughput or sustained player performance.";
            return $"Editor observation: {p["frames"]} frames; p50 {p["frame_ms_p50"]:F2} / p95 {p["frame_ms_p95"]:F2} / p99 {p["frame_ms_p99"]:F2} ms; {p["graphics_api"]}. GPU duration unavailable; Quest NOT RUN.";
        }
        public void Refresh()
        {
            if(story==null) return;story.text=Demo.StoryText();provenance.text=Demo.ProvenanceText();performance.text=PerformanceText();
            presetLabel.text="Capture: "+Demo.Settings.Captures[preset].Title+" · "+Demo.Settings.Captures[preset].Width+" x "+Demo.Settings.Captures[preset].Height+" · "+(Demo.AccessiblePalette?"blue/orange palette (not validated)":"teal/amber palette");
        }
        void Update() {if(Time.unscaledTime>=nextRefresh) {nextRefresh=Time.unscaledTime+.15f;Refresh();}}
    }
}
