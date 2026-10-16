using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRSuperspace
{
    // Presentation-only coach. The scientific model, journal and session schema are unchanged.
    [DefaultExecutionOrder(200)]
    public sealed class QuestExperienceUI : MonoBehaviour
    {
        public ScientificDemo Demo;
        public bool ForcePreview;
        public bool Active=>ForcePreview||XRSettings.isDeviceActive;
        public CanvasGroup Group {get;private set;}
        public bool Transitioning {get;private set;}
        public float FadeDuration=>Demo.Preferences.ReducedMotion?0:Demo.Settings.QuestFadeSeconds;
        public Camera Viewer=>ForcePreview?Demo.Instrument.Camera:Demo.Lab.GetComponent<LabXrMode>().XrCamera;
        static readonly string[] titles={
            "AperiTrace", "One point, two views", "The selection window", "Which points belong?",
            "The physical structure", "Why the lines change", "Watch a short example",
            "What changed?", "The diffraction pattern", "Now explore it yourself"
        };
        static readonly string[] body={
            "A selection rule decides which points form a structure.\nChange the rule and inspect its calculated diffraction.\nAim and choose Next with trigger or pinch. Take your time.",
            "The white marker is the same point in both views.\nThe left view shows its physical position; the middle view shows its selection coordinates.\nThese are linked views, not separate objects.",
            "The polygon in the middle is a fixed selection window.\nIt decides which candidate points are included.\nNothing needs to be grabbed yet. Choose Next when ready.",
            "Teal points are selected; grey points are outside.\nAmber indicates proximity to the displayed boundary band.\nThis is a mathematical model, not a refined material.",
            "Look left: selected points form the physical structure.\nTheir positions are fixed. Changing the middle selection rule can make points enter or leave.\nNext shows their connections.",
            "A line joins selected neighbours of the parent lattice.\nWhen a point leaves the selected set, its lines disappear.\nThese lines are not chemical bonds.",
            "Watch the cyan knob shift the selection coordinates.\nThe window stays fixed; the structure responds.\nNext unlocks when this example finishes.",
            "Solid rings mark additions; dashed rings mark removals.\nRead either view; you do not need to watch both.\nThis compares sampled states, not every intervening crossing.",
            "Look right: the finite-diffraction pattern uses the selected structure.\nAfter a change, wait for Ready before inspecting it.\nA real-space point is not a one-to-one diffraction peak.",
            "Choose Try it. Aim at the cyan knob and hold trigger or pinch.\nMove slowly left/right or up/down, then release.\nReset knob starts again. No walking or reaching is needed."
        };
        Text heading,copy,status;
        Button next,reset,details,changes;
        bool showingDetails,wasActive,confirmClear,shownHistoryFull;
        bool collapsed;
        int lastRevision=-1;
        Button menuButton;
        int shownStep=-2;
        bool shownGuided;
        float readableAt,nextStatus;
        Coroutine transition;
        readonly List<(Transform node,Vector3 position,Quaternion rotation)> saved=new();
        readonly List<(TextMesh label,string text,Font font,Material material,int size,float characters)> savedLabels=new();
        readonly List<(Renderer renderer,Material original,Material owned,Color color,bool internalView)> fadedLines=new();
        LabState layoutState;
        Vector3 anchorHead,layoutInternal;
        Quaternion layoutRotation;
        float originalFade;
        bool originalContributionLabel;
        PreviewControls[] precisionPanels;
        Canvas canvas;
        bool HistoryFull=>Demo.Lab.State!=null&&Demo.Lab.State.Model.Journal.Count>=InteractionModel.MaximumFrames;
        public static string GuideInstruction(int step) {DemoNarrative.ValidateStep(step);return body[step];}

        RectTransform Node(string name,Transform parent,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));var rect=go.GetComponent<RectTransform>();
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);return rect;
        }
        Text Label(string name,float x,float y,float w,float h,int size,Color color)
        {
            var label=Node(name,transform,x,y,w,h).gameObject.AddComponent<Text>();
            QuestUiTheme.Style(label,size);label.color=color;return label;
        }
        Button Action(string text,float x,float width,System.Action action)
        {
            var node=Node(text,transform,x,403,width,72);node.gameObject.AddComponent<Image>();
            var button=node.gameObject.AddComponent<Button>();button.onClick.AddListener(()=>Demo.Guard(action));
            var label=Node("Label",node,10,4,width-20,64).gameObject.AddComponent<Text>();
            QuestUiTheme.Style(label,25,1);label.text=text;label.alignment=TextAnchor.MiddleCenter;
            QuestUiTheme.Button(button,text=="Next");return button;
        }
        void Start()
        {
            canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Viewer;
            precisionPanels=Demo.Lab.GetComponentsInChildren<PreviewControls>(true);
            var rect=(RectTransform)transform;rect.sizeDelta=new Vector2(1000,480);rect.pivot=new Vector2(.5f,.5f);rect.localScale=Vector3.one*.0011f;
            DesktopTheme.Paint(rect,QuestUiTheme.Background,false,18);
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();Group=gameObject.AddComponent<CanvasGroup>();Group.alpha=0;Group.interactable=Group.blocksRaycasts=false;
            heading=Label("Title",28,22,704,56,38,Color.white);
            copy=Label("One clear instruction",28,88,944,210,32,new Color(.86f,.93f,.94f));
            status=Label("Live state and failures",28,309,944,80,24,new Color(.45f,.88f,.81f));
            next=Action("Next",28,224,()=> {if(Demo.Menu.HasExample) Demo.Menu.ToggleExampleState();else if(Demo.Guided&&Demo.Step<9) Demo.Advance();else if(Demo.Guided) Demo.FreeMode();else Demo.SaveSession();});
            reset=Action("Reset knob",268,224,ResetOrClear);
            details=Action("Help",508,224,()=> {if(Demo.Menu.HasExample) Demo.Menu.Explore();else {showingDetails=!showingDetails;collapsed=false;ShowStory();}});
            menuButton=Action("Menu",748,224,()=>Demo.Menu.Toggle());menuButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(748,-18);
            changes=Action("Changes",748,224,()=> {Demo.Comparison.Enabled=!Demo.Comparison.Enabled;ShowStory();});
        }
        public static Quaternion HorizontalFacing(Vector3 forward)
        {
            forward.y=0;return forward.sqrMagnitude<.001f?Quaternion.identity:Quaternion.LookRotation(forward.normalized,Vector3.up);
        }
        public static float LinkedAngle(float physicalRadius,float internalRadius,float reciprocalRadius,float distance)
        {
            float halfA=Mathf.Atan2(physicalRadius,distance)*Mathf.Rad2Deg;
            float halfB=Mathf.Atan2(internalRadius,distance)*Mathf.Rad2Deg;
            float halfC=Mathf.Atan2(reciprocalRadius,distance)*Mathf.Rad2Deg;
            return Mathf.Max(halfA+halfB,halfB+halfC)+5;
        }
        public float LinkedViewRadius {get;private set;}
        public float LinkedViewAngle {get;private set;}
        public float PhysicalEnvelope {get;private set;}
        public float InternalEnvelope {get;private set;}
        float TextHeight(Text label,float width)
        {
            return Mathf.Ceil(label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text,label.GetGenerationSettings(new Vector2(width,0)))/label.pixelsPerUnit)+12;
        }
        void PlaceCard()
        {
            if(Viewer==null||Group==null) return;
            float height=((RectTransform)transform).sizeDelta.y*transform.localScale.y;
            float offset=Demo.Preferences.PanelHeight==1?.13f:Demo.Preferences.PanelHeight==2?-.08f:0;
            float gap=Demo.Preferences.TextSize==2?.26f:.34f;
            float below=Mathf.Max(.45f,height*.5f+gap+offset);
            transform.SetPositionAndRotation(anchorHead+layoutRotation*Vector3.forward*Demo.Settings.QuestCardDistance-Vector3.up*below,layoutRotation);
        }
        void Layout()
        {
            if(heading==null) return;
            float textScale=Demo.Preferences.TextScale;
            QuestUiTheme.Style(heading,Mathf.RoundToInt((Demo.Menu.HasExample?28:34)*textScale),1);
            QuestUiTheme.Style(copy,Mathf.RoundToInt(30*textScale));
            QuestUiTheme.Style(status,Mathf.RoundToInt(24*textScale));status.supportRichText=true;status.color=Color.white;
            float y=18,titleHeight=Mathf.Max(72,TextHeight(heading,704));
            heading.rectTransform.anchoredPosition=new Vector2(28,-y);heading.rectTransform.sizeDelta=new Vector2(704,titleHeight);y+=titleHeight+12;
            bool full=Demo.Guided||Demo.Menu.HasExample||showingDetails||!collapsed||HistoryFull;
            copy.gameObject.SetActive(full);
            if(full) {float h=TextHeight(copy,944);copy.rectTransform.anchoredPosition=new Vector2(28,-y);copy.rectTransform.sizeDelta=new Vector2(944,h);y+=h+16;}
            float statusHeight=Mathf.Max(60,TextHeight(status,944));
            status.rectTransform.anchoredPosition=new Vector2(28,-y);status.rectTransform.sizeDelta=new Vector2(944,statusHeight);y+=statusHeight+18;
            foreach(var button in new[]{next,reset,details,changes})
            {
                var r=button.GetComponent<RectTransform>();r.anchoredPosition=new Vector2(r.anchoredPosition.x,-y);
                var text=button.GetComponentInChildren<Text>();QuestUiTheme.Style(text,Mathf.RoundToInt(25*textScale),1);
                float buttonHeight=Mathf.Max(72,TextHeight(text,204)+16);r.sizeDelta=new Vector2(224,buttonHeight);
                text.rectTransform.sizeDelta=new Vector2(204,buttonHeight-8);
                QuestUiTheme.Button(button,button==next,false,Demo.Preferences.HighContrast);
            }
            var menuText=menuButton.GetComponentInChildren<Text>();QuestUiTheme.Style(menuText,Mathf.RoundToInt(25*textScale),1);
            QuestUiTheme.Button(menuButton,false,false,Demo.Preferences.HighContrast);
            float footerHeight=Mathf.Max(next.GetComponent<RectTransform>().sizeDelta.y,reset.GetComponent<RectTransform>().sizeDelta.y);
            footerHeight=Mathf.Max(footerHeight,Mathf.Max(details.GetComponent<RectTransform>().sizeDelta.y,changes.GetComponent<RectTransform>().sizeDelta.y));
            ((RectTransform)transform).sizeDelta=new Vector2(1000,y+footerHeight+18);
            transform.localScale=Vector3.one*.0011f*Demo.Preferences.UiScale;PlaceCard();
        }
        void ResetOrClear()
        {
            if(!HistoryFull&&!Demo.Guided&&!Demo.Menu.HasExample&&(Demo.Preferences.View==1||Demo.Preferences.View==3)) {UseKnob();return;}
            if(Demo.Menu.HasExample&&!HistoryFull) {Demo.Menu.ChooseExample(Demo.Menu.ExampleIndex);return;}
            if(!HistoryFull) {Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();Demo.Lab.ResetPhi();return;}
            Demo.Menu.Open();Demo.Menu.RequestClear();
        }
        public void UseKnob() {Demo.Preferences.View=2;Demo.Menu.ApplyPreferences();}
        public void Recenter()
        {
            if(!Active||Viewer==null||Group==null) return;
            Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();
            if(transition!=null) StopCoroutine(transition);transition=StartCoroutine(MoveCard(true));
        }
        void SavePresentation()
        {
            var lab=Demo.Lab;originalFade=lab.View.FadeSeconds;originalContributionLabel=Demo.Instrument.Reciprocal.ShowContributionLabel;
            foreach(var node in new[]{lab.View.PhysicalOrigin,lab.View.InternalOrigin,Demo.Instrument.Reciprocal.Origin}) saved.Add((node,node.position,node.rotation));
            foreach(var label in lab.GetComponentsInChildren<TextMesh>(true))
                if(label.name=="Physical Label"||label.name=="Internal Label"||label.name=="Reciprocal Label") {saved.Add((label.transform,label.transform.position,label.transform.rotation));savedLabels.Add((label,label.text,label.font,label.GetComponent<Renderer>().sharedMaterial,label.fontSize,label.characterSize));}
            foreach(var renderer in lab.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer is not LineRenderer&&renderer.gameObject!=lab.View.GraphMesh.gameObject) continue;
                var original=renderer.sharedMaterial;if(original==null||!original.HasProperty("_BaseColor")) continue;
                var owned=new Material(original);var color=original.GetColor("_BaseColor");
                owned.SetFloat("_Surface",1);owned.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                owned.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);owned.SetInt("_ZWrite",0);
                owned.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");owned.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;renderer.sharedMaterial=owned;
                bool internalView=renderer.gameObject!=lab.View.GraphMesh.gameObject&&
                    (renderer==lab.View.WindowLine||renderer.name.Contains("phi")||renderer.name.Contains("trajectory")||renderer.name.Contains("Diagnostic"));
                fadedLines.Add((renderer,original,owned,color,internalView));
            }
        }
        void Place()
        {
            var eye=Viewer.transform;var rotation=HorizontalFacing(eye.forward);anchorHead=eye.position;layoutRotation=rotation;
            PlaceCard();
            float lift=Demo.Preferences.TextSize==2?.10f:0;
            var view=Demo.Lab.View;
            PhysicalEnvelope=view.Settings.PhysicalFitRadius+.08f;
            InternalEnvelope=.7f;
            if(Demo.Lab.State!=null)
            {
                float extent=0;
                foreach(var point in Demo.Lab.State.Dataset.Candidates)
                {
                    float x=(float)Mathf.Abs((float)point.Internal.X)+Mathf.Max(Mathf.Abs((float)Demo.Lab.PhiMin.X),Mathf.Abs((float)Demo.Lab.PhiMax.X));
                    float y=(float)Mathf.Abs((float)point.Internal.Y)+Mathf.Max(Mathf.Abs((float)Demo.Lab.PhiMin.Y),Mathf.Abs((float)Demo.Lab.PhiMax.Y));
                    extent=Mathf.Max(extent,Mathf.Sqrt(x*x+y*y)*view.InternalScale);
                }
                InternalEnvelope=Mathf.Max(InternalEnvelope,extent+.08f);
            }
            LinkedViewRadius=Mathf.Max(2.4f,Demo.Settings.QuestViewDistance);
            // Tangent-facing diagrams reserve their full candidate/offset envelope.
            // Widen the radius for larger exports instead of allowing angular overlap.
            for(int i=0;i<40&&LinkedAngle(PhysicalEnvelope,InternalEnvelope,.63f,LinkedViewRadius)>42;i++) LinkedViewRadius*=1.05f;
            LinkedViewAngle=LinkedAngle(PhysicalEnvelope,InternalEnvelope,.63f,LinkedViewRadius);
            Vector3 elevatedEye=eye.position+Vector3.up*(Demo.Settings.QuestViewAboveEyes+lift);
            var center=elevatedEye+rotation*Vector3.forward*LinkedViewRadius;
            var leftRotation=rotation*Quaternion.Euler(0,-LinkedViewAngle,0);
            var rightRotation=rotation*Quaternion.Euler(0,LinkedViewAngle,0);
            view.PhysicalOrigin.SetPositionAndRotation(elevatedEye+leftRotation*Vector3.forward*LinkedViewRadius,leftRotation);
            view.InternalOrigin.SetPositionAndRotation(center,rotation);
            Demo.Instrument.Reciprocal.Origin.SetPositionAndRotation(elevatedEye+rightRotation*Vector3.forward*LinkedViewRadius,rightRotation);
            int focus=Demo.Guided?0:Demo.Preferences.View;
            if(focus!=0)
            {
                view.PhysicalOrigin.position=center+rotation*Vector3.right*(focus==1?0:-8);
                view.InternalOrigin.position=center+rotation*Vector3.right*(focus==2?0:8);
                Demo.Instrument.Reciprocal.Origin.position=center+rotation*Vector3.right*(focus==3?0:12);
            }
            foreach(var label in Demo.Lab.GetComponentsInChildren<TextMesh>(true))
            {
                Transform origin=label.name=="Physical Label"?view.PhysicalOrigin:label.name=="Internal Label"?view.InternalOrigin:label.name=="Reciprocal Label"?Demo.Instrument.Reciprocal.Origin:null;
                if(origin!=null)
                {
                    float envelope=label.name=="Physical Label"?PhysicalEnvelope:label.name=="Internal Label"?InternalEnvelope:.63f;
                    label.transform.SetPositionAndRotation(origin.position+Vector3.up*(envelope+.32f),origin.rotation);
                    label.font=QuestUiTheme.Font(1);label.fontSize=48;label.characterSize=.009f;label.GetComponent<Renderer>().sharedMaterial=label.font.material;
                    label.text=label.name=="Physical Label"?"Structure":label.name=="Internal Label"?"Selection window":"Finite diffraction";
                }
            }
            layoutState=Demo.Lab.State;layoutInternal=view.InternalOrigin.position;layoutRotation=rotation;
            Demo.GetComponent<QuestViewTools>()?.AnchorPlaced();
            if(layoutState!=null) {view.Rebuild(layoutState);Demo.Instrument.Reciprocal.Rebuild();Demo.Lab.SyncHandlePose();}
        }
        IEnumerator MoveCard(bool relocate)
        {
            Transitioning=true;Group.interactable=Group.blocksRaycasts=false;
            float duration=FadeDuration;
            float initial=Group.alpha;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime) {Group.alpha=Mathf.Lerp(initial,0,t/duration);yield return null;}
            Group.alpha=0;if(relocate) Place();ShowStory();
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime) {Group.alpha=Demo.Menu.IsOpen?0:Mathf.SmoothStep(0,1,t/duration);yield return null;}
            Group.alpha=Demo.Menu.IsOpen?0:1;Group.interactable=Group.blocksRaycasts=!Demo.Menu.InputBlocked;readableAt=Time.unscaledTime;
            Transitioning=false;transition=null;
        }
        void ShowStory()
        {
            if(heading==null) return;
            int step=Mathf.Clamp(Demo.Step,0,9);heading.text=Demo.Guided?$"{step+1} / 10   {titles[step]}":"AperiTrace / Explore";
            copy.text=showingDetails?"Hold trigger on the cyan knob, move slowly, then release.\nSolid rings: added. Dashed rings: removed.\nThis is a mathematical model, not physical extra dimensions.":Demo.Guided?body[step]:
                "Hold trigger on the cyan knob. Move slowly, then release.\nSolid rings: added. Dashed rings: removed.\nOpen Menu for examples, sessions, and comfort settings.";
            if(HistoryFull&&!Demo.Guided)
            {
                heading.text="Local trace is full";
                copy.text="Save trace preserves the recorded states in a local session.\nTo continue, choose Clear trace, then Confirm clear.\nClearing deletes the unsaved local trace, not the reference dataset.";
            }
            next.GetComponentInChildren<Text>().text=Demo.Guided?Demo.Step<9?"Next":"Try it":"Save trace";
            reset.GetComponentInChildren<Text>().text=HistoryFull?confirmClear?"Confirm clear":"Clear trace":"Reset knob";
            next.gameObject.SetActive(true);reset.interactable=!Demo.Guided;
            changes.GetComponentInChildren<Text>().text=Demo.Comparison.Enabled?"Changes: on":"Changes: off";
            changes.gameObject.SetActive(!Demo.Guided||Demo.Step>=6);
            if(Demo.Menu.HasExample&&!HistoryFull)
            {
                heading.text=Demo.Menu.ExampleHeading;copy.text=Demo.Menu.ExampleInstruction;
                next.GetComponentInChildren<Text>().text=Demo.Menu.PrimaryText;
                reset.GetComponentInChildren<Text>().text="Replay case";
            }
            details.GetComponentInChildren<Text>().text=Demo.Menu.HasExample?"Explore":showingDetails?"Hide help":"Help";
            if(!Demo.Guided&&!Demo.Menu.HasExample&&(Demo.Preferences.View==1||Demo.Preferences.View==3))
            {
                heading.text="AperiTrace / Inspect";
                copy.text="This focused view is for inspection.\nChoose Use knob to switch to the selection view.";
                if(!HistoryFull) reset.GetComponentInChildren<Text>().text="Use knob";
            }
            if(Demo.AccessiblePalette) copy.text=copy.text.Replace("Teal","Blue");
            Layout();
        }
        public void RefreshStory()=>ShowStory();
        public void AcceptPresentationTransform()=>layoutInternal=Demo.Lab.View.InternalOrigin.position;
        public IEnumerator FadeForMenu(bool show,float duration)
        {
            if(transition!=null) StopCoroutine(transition);transition=null;Transitioning=true;
            Group.interactable=Group.blocksRaycasts=false;if(show) ShowStory();float initial=Group.alpha;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime) {Group.alpha=Mathf.Lerp(initial,show?1:0,t/duration);yield return null;}
            Group.alpha=show?1:0;Transitioning=false;if(show) readableAt=Time.unscaledTime;
        }
        void RestorePresentation()
        {
            foreach(var item in saved) if(item.node!=null) item.node.SetPositionAndRotation(item.position,item.rotation);
            foreach(var item in savedLabels) if(item.label!=null) {item.label.text=item.text;item.label.font=item.font;item.label.fontSize=item.size;item.label.characterSize=item.characters;item.label.GetComponent<Renderer>().sharedMaterial=item.material;}savedLabels.Clear();
            saved.Clear();foreach(var item in fadedLines) {if(item.renderer!=null) item.renderer.sharedMaterial=item.original;if(item.owned!=null) Destroy(item.owned);}fadedLines.Clear();
            Demo.Lab.View.PhysicalOpacity=Demo.Lab.View.InternalOpacity=Demo.Instrument.Reciprocal.PresentationOpacity=1;
            Demo.Instrument.Reciprocal.ShowContributionLabel=originalContributionLabel;
            Demo.Lab.View.FadeSeconds=originalFade;if(Demo.Lab.State!=null) {Demo.Lab.View.Rebuild(Demo.Lab.State);Demo.Instrument.Reciprocal.Rebuild();Demo.Lab.SyncHandlePose();}
        }
        void Update()
        {
            if(Group==null) return;
            if(Active!=wasActive)
            {
                wasActive=Active;
                if(Active) {SavePresentation();float pointSize=Demo.Lab.View.PointSize;Demo.Preview.SetFallback(true);Demo.Lab.View.PointSize=pointSize;Recenter();}
                else {if(transition!=null) StopCoroutine(transition);transition=null;Transitioning=false;Group.alpha=0;Group.blocksRaycasts=false;RestorePresentation();}
                Demo.UI.gameObject.SetActive(!Active);
            }
            if(!Active||Viewer==null) return;
            canvas.worldCamera=Viewer;
            Demo.UI.gameObject.SetActive(false);Demo.Instrument.UI.gameObject.SetActive(false);
            foreach(var panel in precisionPanels) if(panel!=null) panel.gameObject.SetActive(false);
            if(Demo.Lab.State!=null&&(layoutState!=Demo.Lab.State||Demo.Lab.View.InternalOrigin.position!=layoutInternal||Demo.Lab.View.InternalOrigin.rotation!=layoutRotation)&&!Transitioning) Recenter();
            if(shownStep!=Demo.Step||shownGuided!=Demo.Guided)
            {
                bool relocate=shownGuided!=Demo.Guided;
                shownStep=Demo.Step;shownGuided=Demo.Guided;showingDetails=false;collapsed=!Demo.Guided&&!Demo.Menu.HasExample;lastRevision=-1;
                if(!Transitioning) {if(transition!=null) StopCoroutine(transition);transition=StartCoroutine(MoveCard(relocate));}
            }
            if(shownHistoryFull!=HistoryFull) {shownHistoryFull=HistoryFull;confirmClear=false;ShowStory();}
            bool held=Demo.Lab.PhasonHandle!=null&&Demo.Lab.PhasonHandle.GetComponent<PhasonHandleControl>().Held;
            Group.interactable=!Transitioning&&!Demo.Menu.InputBlocked&&Demo.Lab.State!=null&&!Demo.Lab.Busy&&!Demo.Capture.Busy&&!held;
            Group.blocksRaycasts=Group.interactable;
            next.interactable=!Transitioning&&Time.unscaledTime>=readableAt&&!Demo.PathRunning&&(!Demo.Menu.HasExample||Demo.Instrument.Reciprocal.Current)&&(!Demo.Guided||Demo.Step!=7||Demo.Instrument.Reciprocal.Current);
            if(Demo.Lab.State!=null&&!Demo.Guided&&!Demo.Menu.HasExample)
            {
                int revision=Demo.Lab.State.Model.Revision;
                if(lastRevision>=0&&lastRevision!=revision&&!collapsed) {collapsed=true;ShowStory();}
                lastRevision=revision;
            }
            var view=Demo.Lab.View;float increment=Time.unscaledDeltaTime/Mathf.Max(.001f,FadeDuration);
            view.PhysicalOpacity=Mathf.MoveTowards(view.PhysicalOpacity,view.PresentationPhysical?1:0,increment);
            view.InternalOpacity=Mathf.MoveTowards(view.InternalOpacity,view.PresentationInternal||view.WindowLine.enabled?1:0,increment);
            var reciprocal=Demo.Instrument.Reciprocal;reciprocal.PresentationOpacity=Mathf.MoveTowards(reciprocal.PresentationOpacity,reciprocal.PresentationVisible?1:0,increment);
            view.FadeSeconds=FadeDuration;
            reciprocal.ShowContributionLabel=false; // Expert phase annotations stay in the desktop inspector, not basic Quest help.
            foreach(var label in Demo.Lab.GetComponentsInChildren<TextMesh>(true))
            {
                if(label.name=="Physical Label") label.GetComponent<Renderer>().enabled=view.PresentationPhysical;
                if(label.name=="Internal Label") label.GetComponent<Renderer>().enabled=view.WindowLine.enabled;
                if(label.name=="Reciprocal Label") label.GetComponent<Renderer>().enabled=reciprocal.PresentationVisible;
            }
            foreach(var item in fadedLines) {var c=item.color;c.a*=item.renderer.name.Contains("correspondence")?1:item.internalView?view.InternalOpacity:view.PhysicalOpacity;item.owned.SetColor("_BaseColor",c);}
            if(Time.unscaledTime>=nextStatus)
            {
                nextStatus=Time.unscaledTime+.15f;
                if(HistoryFull) {status.color=new Color(1,.7f,.3f);status.text="Movement paused: save or explicitly clear the local trace to continue.";}
                else if(Demo.LastFailure!="none") {status.color=new Color(1,.7f,.3f);status.text="Needs attention: "+Demo.LastFailure;}
                else if(Demo.Lab.State==null||Demo.Lab.Busy) status.text="Loading the verified local model...";
                else {status.color=QuestUiTheme.Secondary;status.text=$"N = {Demo.Lab.State.SelectedCount} / Diffraction {(reciprocal.Current?"Ready":"Updating")} / {(Demo.UnsavedChanges?"Unsaved":"Saved")}\n"+(Demo.Menu.Paused?"Paused / Menu to resume.":Demo.Menu.HasExample?$"Showing {(Demo.Menu.ShowingAfter?"after":"before")}. Explore unlocks the knob.":Demo.PathRunning?"Example playing; Next unlocks when it finishes.":Demo.Guided?"Choose Next when ready.":"Menu / Inspector, colors, grid, sessions and quit.");}
                if(Demo.Lifecycle.Suspended) status.text="Paused. Resume in the headset to continue.";
                DesktopSemanticText.Apply(status,status.text,Demo.Preferences.HighContrast,QuestUiTheme.Secondary);
                Layout();
            }
        }
        void OnDestroy()
        {
            // Do not rebuild or reactivate scene objects during destruction.
            foreach(var item in fadedLines) {if(item.renderer!=null) item.renderer.sharedMaterial=item.original;if(item.owned!=null) Destroy(item.owned);}
            fadedLines.Clear();
        }
    }
}
