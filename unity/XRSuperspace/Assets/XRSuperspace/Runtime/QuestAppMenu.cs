using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRSuperspace
{
    [DefaultExecutionOrder(210)]
    public sealed partial class QuestAppMenu : MonoBehaviour
    {
        public const string MenuBinding="<XRController>{LeftHand}/menuButton";
        public ScientificDemo Demo;
        public QuestScreenshot Screenshot {get;private set;}
        public bool IsOpen {get;private set;}
        public bool Transitioning {get;private set;}
        public bool Paused {get;private set;}
        public bool InputBlocked=>Paused||IsOpen||Transitioning||(Screenshot!=null&&Screenshot.Busy)||(Demo.Lifecycle!=null&&Demo.Lifecycle.Suspended);
        public int Page {get;private set;}
        public int ExampleIndex {get;private set;}=-1;
        public bool ShowingAfter {get;private set;}=true;
        public bool HasExample=>ExampleIndex>=0;
        public bool ExitSignalSent {get;private set;}
        public event Action ExitRequested;
        public string Feedback {get;private set;}="Start with a guided example, or explore at your own pace.";
        public CanvasGroup Group {get;private set;}
        public string ExampleHeading=>HasExample?$"{ExampleIndex+1:00}: {QuestExampleLibrary.All[ExampleIndex].Title}":"";
        public string ExampleInstruction=>HasExample?QuestExampleLibrary.All[ExampleIndex].Instruction:"";
        public string PrimaryText=>ShowingAfter?"Show before":"Show after";
        public bool CanSave=>Demo.Lab.State!=null&&!Demo.Lab.Busy&&!Demo.PathRunning&&!Demo.Instrument.Playing&&!Demo.Instrument.Recording&&!Demo.Capture.Busy&&!Screenshot.Busy&&!Demo.Lifecycle.Suspended;
        public string Screen {get;private set;}="Home";
        public bool ConfirmationPending=>pending!=null;
        InputAction menu;
        Canvas canvas;
        RectTransform content,scrollHost;
        bool rebuilding;
        float cursor;
        Coroutine transition;
        bool trajectoryOverride,savedTrajectory,confirmQuit;
        Action pending;
        string pendingDescription;
        static readonly string[] sizes={"Normal","Large","Extra large"};
        static readonly string[] views={"Linked views","Structure focus","Selection focus","Diffraction focus"};
        RectTransform Node(string name,Transform parent,float x,float y,float w,float h)
        {
            var node=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();node.SetParent(parent,false);
            node.anchorMin=node.anchorMax=node.pivot=new Vector2(0,1);node.anchoredPosition=new Vector2(x,-y);node.sizeDelta=new Vector2(w,h);return node;
        }
        Text Label(string name,Transform parent,float x,float y,float w,float h,int size,int weight=0)
        {
            var label=Node(name,parent,x,y,w,h).gameObject.AddComponent<Text>();
            QuestUiTheme.Style(label,Mathf.RoundToInt(size*Demo.Preferences.TextScale),Math.Min(weight,1));return label;
        }
        float Height(string text,int size,float width)
        {
            var generator=new TextGenerator();var settings=new TextGenerationSettings {font=QuestUiTheme.Font(1),fontSize=Mathf.RoundToInt(size*Demo.Preferences.TextScale),
                scaleFactor=1,lineSpacing=1.12f,generateOutOfBounds=true,richText=true,textAnchor=TextAnchor.UpperLeft,
                generationExtents=new Vector2(width,0),horizontalOverflow=HorizontalWrapMode.Wrap,verticalOverflow=VerticalWrapMode.Overflow};
            return Mathf.Max(64,generator.GetPreferredHeight(text,settings)+24);
        }
        Button MakeButton(string text,float x,float y,float width,float height,Action action,bool primary=false,bool destructive=false,bool enabled=true)
        {
            var node=Node(text,content,x,y,width,height);DesktopTheme.Paint(node,QuestUiTheme.Surface,true,12);var button=node.gameObject.AddComponent<Button>();
            var label=Label("Action",node,14,8,width-28,height-16,28,primary?1:0);DesktopSemanticText.Apply(label,text,Demo.Preferences.HighContrast);label.alignment=TextAnchor.MiddleLeft;
            QuestUiTheme.Button(button,primary,destructive,Demo.Preferences.HighContrast);button.interactable=enabled;
            button.onClick.AddListener(()=>Run(action));return button;
        }
        void Row(string text,Action action,bool primary=false,bool destructive=false,bool enabled=true)
        {
            float h=Height(text,28,916);MakeButton(text,28,cursor,944,h,action,primary,destructive,enabled);cursor+=h+12;
        }
        void Rail(string first,Action a,string second,Action b,bool firstEnabled=true,bool secondEnabled=true,bool primaryFirst=false,bool destructiveSecond=false)
        {
            float h=Mathf.Max(Height(first,28,420),Height(second,28,420));
            MakeButton(first,28,cursor,466,h,a,primary:primaryFirst,enabled:firstEnabled);MakeButton(second,506,cursor,466,h,b,destructive:destructiveSecond,enabled:secondEnabled);cursor+=h+12;
        }
        void Awake()
        {
            menu=new InputAction("Application menu",InputActionType.Button,MenuBinding);menu.Enable();
            Screenshot=gameObject.AddComponent<QuestScreenshot>();Screenshot.Menu=this;
        }
        void Start()
        {
            canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Demo.QuestUI.Viewer;((RectTransform)transform).pivot=new Vector2(.5f,.5f);
            DesktopTheme.Paint((RectTransform)transform,QuestUiTheme.Background,false,18);
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            Group=gameObject.AddComponent<CanvasGroup>();Group.alpha=0;Group.blocksRaycasts=Group.interactable=false;Refresh();
        }
        void Run(Action action)
        {
            if(Transitioning||Demo.Lifecycle.Suspended||Screenshot.Busy) return;
            try {action();}catch(Exception error) {Feedback="Not completed: "+error.Message;Debug.LogWarning("[XRSuperspace] menu: "+error.Message);}
            Refresh();
        }
        public void SetFeedback(string message) {Feedback=message;Refresh();}
        void Place()
        {
            if(canvas==null) return;var camera=Demo.QuestUI.Viewer;if(camera==null) return;
            var rotation=QuestExperienceUI.HorizontalFacing(camera.transform.forward);
            transform.SetPositionAndRotation(camera.transform.position+rotation*Vector3.forward*Demo.Settings.QuestCardDistance-Vector3.up*.18f,rotation);canvas.worldCamera=camera;
        }
        public void Toggle() {if(IsOpen) Close();else Open();}
        public void Open()
        {
            if(Group==null||IsOpen||!Demo.QuestUI.Active||Transitioning||Screenshot.Busy||Demo.Lifecycle.Suspended) return;
            Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();IsOpen=true;Screen="Home";pending=null;confirmQuit=false;
            Place();Refresh();transition=StartCoroutine(Fade(true));
        }
        public void Close()
        {
            if(!IsOpen||Transitioning||Screenshot.Busy) return;
            pending=null;confirmQuit=false;transition=StartCoroutine(Fade(false));
        }
        IEnumerator Fade(bool opening)
        {
            Transitioning=true;Group.blocksRaycasts=Group.interactable=false;float duration=Demo.QuestUI.FadeDuration*.5f;
            if(opening) yield return Demo.QuestUI.FadeForMenu(false,duration);float initial=Group.alpha;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime) {Group.alpha=Mathf.Lerp(initial,opening?1:0,DesktopMotion.Ease(t/duration));yield return null;}
            Group.alpha=opening?1:0;
            if(!opening) {IsOpen=false;Demo.QuestUI.RefreshStory();yield return Demo.QuestUI.FadeForMenu(true,duration);}
            Transitioning=false;transition=null;Refresh();
        }
        public void ShowScreen(string screen) {Screen=screen;Page=0;pending=null;confirmQuit=false;Refresh();}
        public void SetPaused(bool value)
        {
            Paused=value;Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();
            Feedback=value?"Paused. Your scientific state is preserved.":"Resumed. Choose Explore to move the knob.";
            Demo.QuestUI.RefreshStory();Refresh();
        }
        public void ChangePage(int delta)
        {
            if(Screen!="Sessions"&&Screen!="Comfort") Screen="Examples";
            int count=Screen=="Sessions"?LocalSessionStore.List(Demo.SessionFolder).Length:Screen=="Comfort"?8:QuestExampleLibrary.All.Count;
            Page=Mathf.Clamp(Page+delta,0,Mathf.Max(0,(count-1)/4));Refresh();
        }
        public void ClearExample()
        {
            ExampleIndex=-1;
            if(trajectoryOverride&&Demo!=null&&Demo.Lab!=null&&Demo.Lab.GetComponent<InstrumentOverlay>() is InstrumentOverlay overlay) overlay.ShowTrajectory=savedTrajectory;
            trajectoryOverride=false;
        }
        public void Explore() {ClearExample();Demo.Comparison.EndComparison();Demo.FreeMode();Demo.QuestUI.RefreshStory();}
        public void ChooseExample(int index)
        {
            if(index<0||index>=QuestExampleLibrary.All.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if(Demo.Lifecycle.Suspended||Demo.Capture.Busy||Screenshot.Busy||Demo.Instrument.Playing||Demo.Instrument.Recording) throw new InvalidOperationException("Finish capture or playback before loading an example.");
            var example=QuestExampleLibrary.All[index];example.Validate();
            QuestExampleLibrary.EnsureBudget(Demo.Lab.State,(example.Before,example.BeforeFilter),(example.After,example.AfterFilter));
            Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();Demo.FreeMode();Demo.Comparison.EndComparison();
            Demo.Lab.ApplyModel(example.Before,example.BeforeFilter);Demo.Comparison.BeginComparison();
            Demo.Lab.ApplyModel(example.After,example.AfterFilter);Demo.Comparison.EndComparison();
            ExampleIndex=index;ShowingAfter=true;ApplyDisplay(example,true);Demo.Lab.Select(Demo.Instrument.Resolve(Demo.Settings.FocusId));
            Feedback="Compare before / after, then choose Explore.";Demo.QuestUI.RefreshStory();if(IsOpen) Close();
        }
        void ApplyDisplay(QuestExample example,bool after)
        {
            if(Demo.Lab.GetComponent<InstrumentOverlay>() is InstrumentOverlay overlay)
            {
                if(!trajectoryOverride) {savedTrajectory=overlay.ShowTrajectory;trajectoryOverride=true;}overlay.ShowTrajectory=false;
            }
            var view=Demo.Lab.View;view.ShowCandidates=example.Candidates;view.ShowEdges=example.Edges;view.BoundaryBand=example.Band;
            view.PointSize=after?example.PointSize:1;view.LayerSeparation=after?example.Layer:1;
            Demo.Instrument.Reciprocal.SetIntensityThreshold(0);view.Rebuild(Demo.Lab.State);Demo.Instrument.Reciprocal.Rebuild();
        }
        public void ToggleExampleState()
        {
            if(!HasExample||Demo.Lifecycle.Suspended) return;var example=QuestExampleLibrary.All[ExampleIndex];bool after=!ShowingAfter;
            var phi=after?example.After:example.Before;var filter=after?example.AfterFilter:example.BeforeFilter;
            QuestExampleLibrary.EnsureBudget(Demo.Lab.State,(phi,filter));Demo.Comparison.BeginComparison();Demo.Lab.ApplyModel(phi,filter);Demo.Comparison.EndComparison();
            ShowingAfter=after;ApplyDisplay(example,after);Demo.QuestUI.RefreshStory();
        }
        public void TakeScreenshot() {if(!CanSave) throw new InvalidOperationException("Finish playback or capture before taking a screenshot.");Screenshot.Begin();}
        public void RequestReplacement(string description,Action action)
        {
            if(Demo.Lifecycle.Suspended) throw new InvalidOperationException("Resume the app first.");
            if(Demo.PathRunning||Demo.Instrument.Playing||Demo.Capture.Busy||Screenshot.Busy) throw new InvalidOperationException("Finish playback or capture first.");
            if(Demo.UnsavedChanges) {pending=action;pendingDescription=description;Screen="Confirm";Refresh();}else action();
        }
        public void RequestRestart()=>RequestReplacement("Restart the guided tour",()=> {Demo.StartGuide(false);Demo.QuestUI.RefreshStory();Close();});
        public void RequestLoad(string path)=>RequestReplacement("Load a saved session",()=> {Demo.LoadSession(path);Feedback="Session restored.";Close();});
        public void ConfirmReplacement(bool save)
        {
            if(pending==null) throw new InvalidOperationException("Choose an action to confirm first.");
            if(!CanSave) throw new InvalidOperationException("Wait until the app is ready.");
            if(save) Demo.SaveSession();var action=pending;pending=null;action();
        }
        public void RequestClear()=>RequestReplacement("Clear the trace and reset the knob",()=> {
            Demo.Lab.PhasonHandle.GetComponent<PhasonHandleControl>().CancelGrab();Demo.Instrument.ClearJournal();Demo.Lab.ResetPhi();Demo.QuestUI.RefreshStory();Feedback="Trace cleared by your request.";Close();
        });
        public void RequestQuit() {confirmQuit=true;pending=null;Screen="Quit";Refresh();}
        public void ConfirmQuit(bool save)
        {
            if(!confirmQuit) throw new InvalidOperationException("Choose Quit and confirm first.");
            if(Demo.Lifecycle.Suspended||Screenshot.Busy||Demo.Capture.Busy) throw new InvalidOperationException("Resume and finish capture before quitting.");
            if(save) {if(!CanSave) throw new InvalidOperationException("Finish playback before saving.");Demo.SaveSession();}
            ExitSignalSent=true;if(ExitRequested!=null) ExitRequested();else Application.Quit();
        }
        public void ApplyPreferences()
        {
            Demo.Preferences.Save();Demo.SetPalette(Demo.Preferences.AlternativePalette);
            Demo.Instrument.Reciprocal.SetDisplay(Demo.Preferences.Diffraction);Demo.QuestUI.Recenter();Refresh();
        }
        void Pages(int count)=>Rail("Previous page",()=>ChangePage(-1),"Next page",()=>ChangePage(1),Page>0,Page<(count-1)/4);
        public void Refresh()
        {
            if(canvas==null||rebuilding) return;rebuilding=true;
            try {
            if(scrollHost!=null) {QuestUiTheme.ClearSelectionWithin(scrollHost);scrollHost.gameObject.SetActive(false);Destroy(scrollHost.gameObject);}
            scrollHost=Node("Bounded menu viewport",transform,0,0,1000,900);
            var mask=scrollHost.gameObject.AddComponent<RectMask2D>();
            var scroll=scrollHost.gameObject.AddComponent<ScrollRect>();
            var viewportGraphic=scrollHost.gameObject.AddComponent<Image>();viewportGraphic.color=new Color(0,0,0,.001f);viewportGraphic.raycastTarget=false;
            content=Node("Menu content",scrollHost,0,0,1000,1000);
            scroll.viewport=scrollHost;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            scroll.inertia=!Demo.Preferences.ReducedMotion;scroll.scrollSensitivity=52;
            string title=Screen=="Quit"?"Quit AperiTrace?":Screen=="Confirm"?pendingDescription:Screen=="Home"?"AperiTrace":Screen=="Comfort"?"Accessibility and comfort":Screen=="ViewTools"?"Views and navigation":Screen=="ColorRange"?"Color range and scale":Screen=="CustomColors"?"Custom gradient":Screen=="Keypad"?"Edit a value":Screen;
            float titleHeight=Height(title,34,944);var heading=Label("Menu heading",content,28,18,944,titleHeight,34,2);heading.text=title;cursor=18+titleHeight;
            string description=Screen=="Home"?"Explore how selection changes a structure and its calculated diffraction.":
                Screen=="Examples"?$"Page {Page+1} / 5. Foundations 01-04; membership 05-15; diffraction and subsets 16-20.":
                Screen=="Sessions"?"Save or restore a scientific session. Screenshots are separate app renders.":
                Screen=="Comfort"?$"Page {Page+1} / 2. Saved locally. Focus applies in Explore; the guided tour uses linked views.":
                Screen=="Quit"?"Saved files are kept. Preserve unsaved work or discard it.":
                Screen=="Confirm"?"This replaces unsaved work. Save first, discard it, or cancel.":
                "Use the trigger to select controls. Walking is not required.";
            float h=Height(description,26,944);var note=Label("Menu explanation",content,28,cursor,944,h,26);note.text=description;cursor+=h+12;
            if(Screen=="Home")
            {
                Row("Learn / Examples",()=>ShowScreen("Examples"),true);Row("Sessions / Screenshot",()=>ShowScreen("Sessions"));
                Row("Explore / move knob",()=> {Explore();Close();});
                Row("Inspect / numerical results",()=>ShowScreen("Inspector"));
                Row("View, colors and grid",()=>ShowScreen("ViewTools"));
                Row("Accessibility and comfort",()=>ShowScreen("Comfort"));Row("Help / About",()=>ShowScreen("Help"));
                Row(Paused?"Resume calculation playback":"Pause playback and movement",()=>SetPaused(!Paused));
            }
            else if(Screen=="Examples")
            {
                Row("Start guided tour",RequestRestart,true,enabled:!Demo.PathRunning);
                for(int i=0;i<4;i++) {int index=Page*4+i;if(index<QuestExampleLibrary.All.Count) Row($"{index+1:00}   {QuestExampleLibrary.All[index].Title}",()=>ChooseExample(index));}
                Pages(QuestExampleLibrary.All.Count);
            }
            else if(Screen=="Sessions")
            {
                Rail("Save session",Demo.SaveSession,"Screenshot",TakeScreenshot,CanSave,CanSave,primaryFirst:true);
                var files=LocalSessionStore.List(Demo.SessionFolder);Page=Mathf.Clamp(Page,0,Mathf.Max(0,(files.Length-1)/4));
                for(int i=0;i<4;i++) {int index=Page*4+i;if(index<files.Length) {string path=files[index],stem=Path.GetFileNameWithoutExtension(path);Row("Load: "+File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-dd HH:mm:ss")+" UTC ["+stem.Substring(Math.Max(0,stem.Length-6))+"]",()=>RequestLoad(path),enabled:CanSave);}}
                if(files.Length>4) Pages(files.Length);
                Rail("File locations",()=>ShowScreen("Files"),"Clear trace...",RequestClear,secondEnabled:CanSave,destructiveSecond:true);
            }
            else if(Screen=="Comfort")
            {
                var p=Demo.Preferences;
                if(Page==0)
                {
                    Row("Text size: "+sizes[p.TextSize],()=> {p.TextSize=(p.TextSize+1)%3;ApplyPreferences();});
                    Row("UI size: "+sizes[p.UiSize],()=> {p.UiSize=(p.UiSize+1)%3;ApplyPreferences();});
                    Row("Contrast: "+(p.HighContrast?"High":"Standard"),()=> {p.HighContrast=!p.HighContrast;ApplyPreferences();});
                    Row("Scientific palette: "+(p.AlternativePalette?"Blue / amber":"Teal / amber"),()=> {p.AlternativePalette=!p.AlternativePalette;ApplyPreferences();});
                }
                else
                {
                    Row("Motion: "+(p.ReducedMotion?"Reduced":"Gentle fades"),()=> {p.ReducedMotion=!p.ReducedMotion;ApplyPreferences();});
                    Row("View: "+views[p.View],()=> {p.View=(p.View+1)%4;ApplyPreferences();});
                    Row("Panel position: "+new[]{"Standard","Lower","Raised"}[p.PanelHeight],()=> {p.PanelHeight=(p.PanelHeight+1)%3;ApplyPreferences();});
                    Row("Controller input: "+new[]{"Both","Left only","Right only"}[p.Controllers],()=> {p.Controllers=(p.Controllers+1)%3;ApplyPreferences();});
                }
                Pages(8);
            }
            else if(Screen=="Quit"||Screen=="Confirm")
            {
                Row(Screen=="Quit"?"Save & quit":"Save & continue",()=> {if(Screen=="Quit") ConfirmQuit(true);else ConfirmReplacement(true);},true,enabled:CanSave);
                Row(Screen=="Quit"?"Quit without saving":"Discard & continue",()=> {if(Screen=="Quit") ConfirmQuit(false);else ConfirmReplacement(false);},destructive:true,enabled:!Demo.Capture.Busy&&!Screenshot.Busy);
                Row("Cancel",()=>ShowScreen("Home"));
            }
            else if(BuildResearchScreen(Screen)) { }
            else
            {
                string text=Screen=="Files"?
                    "Sessions: SuperspaceDemoSessions\nImages: AperiTraceScreenshots\nApp-local files can be copied with an authorized USB / SideQuest connection. Update without uninstalling; uninstall can remove files. PNGs are mono app renders, not Quest Gallery images.":
                    "Drag the cyan knob with the trigger. Solid rings mean added; dashed rings mean removed. The knob changes selection, not physical positions. Lines are lattice neighbours, not chemical bonds. Diffraction is calculated, not a measured X-ray image.\nAperiTrace 0.1.0 | myungin@umd.edu\nInter 4.1: SIL Open Font License 1.1. Offline; no cloud.";
                float height=Height(text,28,944);var info=Label("Readable details",content,28,cursor,944,height,28);info.text=text;cursor+=height+16;
                Rail("Guided tour",RequestRestart,"Examples",()=>ShowScreen("Examples"));
            }
            if(Screen!="Quit"&&Screen!="Confirm")
            {
                if(Screen!="Home") Rail("Back to menu",()=>ShowScreen("Home"),"Recenter",()=> {Demo.QuestUI.Recenter();Place();});
                else Rail("Recenter",()=> {Demo.QuestUI.Recenter();Place();},"Quit",RequestQuit,destructiveSecond:true);
                Row("Resume",Close,true);
            }
            string message=(Demo.UnsavedChanges?"Unsaved work. ":"All changes saved. ")+(Demo.PathRunning||Demo.Instrument.Playing?"Playback is paused by this menu. Resume to finish before saving.":Feedback);
            float feedbackHeight=Height(message,24,944);var status=Label("Menu feedback",content,28,cursor,944,feedbackHeight,24);status.text=message;cursor+=feedbackHeight+18;
            content.sizeDelta=new Vector2(1000,cursor);
            // Fixed angular budget at all text sizes. Long pages scroll rather
            // than growing above/below the viewer or covering the knob.
            float viewportHeight=Mathf.Min(cursor,780/Demo.Preferences.UiScale);
            scrollHost.sizeDelta=new Vector2(1000,viewportHeight);
            ((RectTransform)transform).sizeDelta=new Vector2(1000,viewportHeight);
            transform.localScale=Vector3.one*.0011f*Demo.Preferences.UiScale;
            BuildFixedFooter(viewportHeight);
            } finally {rebuilding=false;}
        }
        void Update()
        {
            if(Group==null) return;
            if(!Demo.QuestUI.Active)
            {
                if(transition!=null) StopCoroutine(transition);transition=null;Transitioning=false;IsOpen=false;
                Group.alpha=0;Group.interactable=Group.blocksRaycasts=false;canvas.enabled=false;return;
            }
            canvas.enabled=true;
            RefreshResearchValues();
            if(!Demo.Lifecycle.Suspended&&!Screenshot.Busy&&(menu.WasPressedThisFrame()||(Keyboard.current?.mKey.wasPressedThisFrame??false))) Toggle();
            Group.interactable=Group.blocksRaycasts=IsOpen&&!Transitioning&&!Demo.Lifecycle.Suspended&&!Screenshot.Busy;
        }
        void OnDestroy() {menu?.Dispose();ClearExample();}
    }
}
