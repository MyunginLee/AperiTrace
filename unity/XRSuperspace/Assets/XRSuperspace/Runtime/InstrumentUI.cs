using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRSuperspace
{
    // Built once from code: all listeners are re-bound on each run, not transient scene state.
    public sealed class InstrumentUI : MonoBehaviour
    {
        public LinkedInstrument Instrument;
        Text identity,reciprocal,status;
        ScrollRect identityScroll;
        void ScrollColumn(Text text,float x,string name)
        {
            var viewport=Node(name,transform,new Vector2(x,282),new Vector2(642,303));viewport.AddComponent<Image>().color=new Color(0,0,0,0);viewport.AddComponent<RectMask2D>();text.transform.SetParent(viewport.transform,false);var content=text.rectTransform;content.anchorMin=content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);content.anchoredPosition=Vector2.zero;text.verticalOverflow=VerticalWrapMode.Overflow;var scroll=viewport.AddComponent<ScrollRect>();scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=content;scroll.horizontal=false;scroll.scrollSensitivity=24;
        }
        InputField tuple,phiX,phiY,radius,zmax,slice,width,peak;
        Font font;
        float nextText;
        string dataset;
        LabState shownState;
        int shownRevision=-1,shownPeak=-2,shownJournal=-1,shownPath=-1;
        LatticeId? shownSelection;
        string shownMessage,shownReciprocalStatus,shownLabStatus;
        double[][] shownValues;
        bool shownRecording,shownPlaying;
        float shownLayer,shownSize,shownBand;
        GameObject Node(string name,Transform parent,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;rect.anchoredPosition=position;rect.sizeDelta=size;return go;
        }
        Text Text(string name,Transform parent,string value,Vector2 position,Vector2 size,int fontSize=21)
        {
            var text=Node(name,parent,position,size).AddComponent<Text>();text.font=font;text.fontSize=fontSize;text.text=value;text.color=new Color(.86f,.93f,1);text.alignment=TextAnchor.UpperLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;text.raycastTarget=false;return text;
        }
        InputField Field(string name,string value,float x,float y,float size)
        {
            var go=Node(name,transform,new Vector2(x,y),new Vector2(size,36));go.AddComponent<Image>().color=new Color(.11f,.18f,.25f);var field=go.AddComponent<InputField>();var text=Text(name+" text",go.transform,value,new Vector2(6,2),new Vector2(size-12,32),22);field.textComponent=text;field.text=value;return field;
        }
        void Button(string name,float x,float y,float size,Action action)
        {
            var go=Node(name,transform,new Vector2(x,y),new Vector2(size,36));go.AddComponent<Image>().color=new Color(.1f,.24f,.31f);var button=go.AddComponent<Button>();button.onClick.AddListener(()=>Instrument.Guard(action));var label=Text("Label",go.transform,name,new Vector2(0,0),new Vector2(size,36),21);label.alignment=TextAnchor.MiddleCenter;
        }
        void Awake()
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Instrument.Camera;
            var rect=GetComponent<RectTransform>();rect.sizeDelta=new Vector2(2000,600);rect.pivot=Vector2.zero;rect.localScale=Vector3.one*.0023f;
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();gameObject.AddComponent<Image>().color=new Color(.025f,.048f,.075f,.98f);
            identity=Text("Exact identity",transform,"Loading…",new Vector2(18,282),new Vector2(642,303),20);
            var viewport=Node("Identity scroll viewport",transform,new Vector2(18,282),new Vector2(642,303));viewport.AddComponent<Image>().color=new Color(0,0,0,0);viewport.AddComponent<RectMask2D>();identity.transform.SetParent(viewport.transform,false);var content=identity.rectTransform;content.anchorMin=content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);content.anchoredPosition=Vector2.zero;identity.verticalOverflow=VerticalWrapMode.Overflow;
            identityScroll=viewport.AddComponent<ScrollRect>();identityScroll.viewport=viewport.GetComponent<RectTransform>();identityScroll.content=content;identityScroll.horizontal=false;identityScroll.scrollSensitivity=24;
            reciprocal=Text("Reciprocal inspector",transform,"Loading…",new Vector2(684,282),new Vector2(642,303),21);
            status=Text("Session events",transform,"",new Vector2(1350,282),new Vector2(632,303),22);
            ScrollColumn(reciprocal,684,"Reciprocal scroll viewport");ScrollColumn(status,1350,"Session scroll viewport");
            tuple=Field("Exact n","-1,0,-1,0,0",18,236,444);Button("Find exact n",474,236,186,()=>Instrument.Search(tuple.text));
            phiX=Field("Phi x","0",18,190,144);phiY=Field("Phi y","0",174,190,144);Button("Set phi",330,190,150,()=>Instrument.SetPhiText(phiX.text,phiY.text));Button("Snap zero",492,190,168,Instrument.SnapZero);
            Text("Crop label",transform,"Crop radius / |z|max · slice z / half width",new Vector2(18,148),new Vector2(642,32),21);
            radius=Field("Crop radius","0.9",18,112,144);zmax=Field("Crop z","0.9",174,112,144);slice=Field("Slice z","0",330,112,144);width=Field("Slice width","0.25",486,112,174);
            void Filter(bool crop,bool plane)=>Instrument.SetFilterText(radius.text,zmax.text,slice.text,width.text,crop,plane);
            Button("Apply subset",18,66,198,()=>Filter(false,false));Button("Toggle crop",228,66,198,()=>Filter(true,false));Button("Toggle slice",438,66,222,()=>Filter(false,true));
            Button("Reload",18,20,144,Instrument.Lab.Reload);Button("Dataset",174,20,144,Instrument.Lab.NextDataset);Button("Edges",330,20,144,Instrument.Lab.ToggleEdges);Button("Reset phi",486,20,174,Instrument.Lab.ResetPhi);
            peak=Field("Peak row","0",684,236,144);Button("Select peak",840,236,204,()=> {int row=int.Parse(peak.text,CultureInfo.InvariantCulture);if(row<0||row>=Instrument.Reciprocal.Peaks.Length) throw new ArgumentException("peak row outside loaded sampling");Instrument.Reciprocal.SelectedPeak=row;});Button("Parent context",1056,236,270,Instrument.ToggleContext);
            Button("Layer −",684,190,150,()=>Instrument.Display(-.25f,0));Button("Layer +",846,190,150,()=>Instrument.Display(.25f,0));Button("Size −",1008,190,150,()=>Instrument.Display(0,-.25f));Button("Size +",1170,190,156,()=>Instrument.Display(0,.25f));
            Button("Record / stop",684,144,306,Instrument.Record);Button("Play / stop",1002,144,324,Instrument.Play);
            Button("Save session",684,98,306,Instrument.Save);Button("Load latest",1002,98,324,Instrument.LoadLast);
            Button("Override checksum",684,52,306,Instrument.OverrideLast);Button("Export events",1002,52,324,Instrument.ExportJournal);
            Button("Clear journal (explicit)",684,6,642,Instrument.ClearJournal);
            for(int i=0;i<5;i++) {int index=i;Button((i+1)+". "+Instrument.Presets[i].Title,1350,236-i*46,632,()=>Instrument.Preset(index));}
        }
        public void Refresh()
        {
            var state=Instrument.Lab.State;shownState=state;shownRevision=state?.Model.Revision??-1;shownSelection=state?.Selection;shownPeak=Instrument.Reciprocal.SelectedPeak;shownValues=Instrument.Reciprocal.Values;shownJournal=state?.Model.Journal.Count??0;shownPath=Instrument.Path.Count;shownMessage=Instrument.Message;shownReciprocalStatus=Instrument.Reciprocal.Status;shownLabStatus=Instrument.Lab.Status;shownRecording=Instrument.Recording;shownPlaying=Instrument.Playing;shownLayer=Instrument.Lab.View.LayerSeparation;shownSize=Instrument.Lab.View.PointSize;shownBand=Instrument.Lab.View.BoundaryBand;
            if(Instrument.Lab.State==null) {identity.text=Instrument.Lab.DiagnosticText();return;}
            var model=Instrument.Lab.State.Model;
            if(dataset!=model.Dataset.Metadata.BinarySha256) {dataset=model.Dataset.Metadata.BinarySha256;radius.text=model.Filter.Radius.ToString("R",CultureInfo.InvariantCulture);zmax.text=model.Filter.ZMax.ToString("R",CultureInfo.InvariantCulture);}
            identity.text=Instrument.Inspector();reciprocal.text=Instrument.Reciprocal.Inspector();var frame=model.Journal[model.Journal.Count-1];
            identity.rectTransform.sizeDelta=new Vector2(642,Mathf.Max(303,identity.preferredHeight+12));
            status.text=$"SESSION / EVENTS — schema v1\n{Instrument.Message}\nFrame {frame.Frame} · entered {frame.Entered.Length} · exited {frame.Exited.Length}\nSelected IDs logged immediately; fades never change model\nPath {Instrument.Path.Count} samples · record {Instrument.Recording} · play {Instrument.Playing}\nBand amber = diagnostic; scientific ε {model.Dataset.Metadata.AuthorityEpsilon:G3}\nInternal plane is a parameter view, NOT material space.\nCanonical dimensionless model; not a real alloy.\nRMB orbit · MMB pan · wheel zoom · arrows phi\nClick points / peaks · drag cyan phi handle\nKeyboard/mouse retained; XR device checks pending.";
            reciprocal.rectTransform.sizeDelta=new Vector2(642,Mathf.Max(303,reciprocal.preferredHeight+12));status.rectTransform.sizeDelta=new Vector2(642,Mathf.Max(303,status.preferredHeight+12));
        }
        void Update()
        {
            if(Time.unscaledTime>=nextText)
            {
                nextText=Time.unscaledTime+.15f;var state=Instrument.Lab.State;var view=Instrument.Lab.View;
                if(shownState!=state||shownRevision!=(state?.Model.Revision??-1)||!Nullable.Equals(shownSelection,state?.Selection)||shownPeak!=Instrument.Reciprocal.SelectedPeak||shownValues!=Instrument.Reciprocal.Values||shownJournal!=(state?.Model.Journal.Count??0)||shownPath!=Instrument.Path.Count||shownMessage!=Instrument.Message||shownReciprocalStatus!=Instrument.Reciprocal.Status||shownLabStatus!=Instrument.Lab.Status||shownRecording!=Instrument.Recording||shownPlaying!=Instrument.Playing||shownLayer!=view.LayerSeparation||shownSize!=view.PointSize||shownBand!=view.BoundaryBand) Refresh();
            }
            var mouse=Mouse.current;if(mouse==null||!mouse.leftButton.wasPressedThisFrame||!Instrument.Camera.enabled||EventSystem.current!=null&&EventSystem.current.IsPointerOverGameObject()) return;
            int row=Instrument.Reciprocal.Pick(Instrument.Camera.ScreenPointToRay(mouse.position.ReadValue()));if(row>=0) Instrument.Reciprocal.SelectedPeak=row;
        }
    }
}
