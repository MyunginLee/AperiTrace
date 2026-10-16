using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.LowLevel;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace XRSuperspace
{
    // Windows-only presentation/interaction policy. Scientific coordinates,
    // membership, graph, finite sums and session schema are never changed here.
    [DefaultExecutionOrder(240)]
    public sealed class DesktopWorkspace : MonoBehaviour
    {
        public ScientificDemo Demo;
        public DesktopShell Shell {get;private set;}
        public DesktopPreferences Preferences {get;private set;}
        public bool Active=>isActiveAndEnabled&&Shell!=null;
        public bool Paused {get;private set;}
        public bool PausedAfterFocusLoss {get;private set;}
        public string ManipulationNotice=>Paused?(PausedAfterFocusLoss?"Paused after switching windows. Resume to continue.":"Paused. Resume to continue."):Demo.Guided||Demo.Menu.HasExample?"Guided example. Choose Explore freely to move the cyan control.":Demo.Lab.State?.Model.Journal.Count>=InteractionModel.MaximumFrames?"Trace limit reached. Save your session before starting a new trace.":FocusView==1||FocusView==3?"The cyan control is in Window. Choose Window or All views to move it.":"";
        public bool Capturing {get;private set;}
        public int FocusView {get;private set;}
        public bool Dragging {get;private set;}
        public bool ReferencePinned {get;private set;}
        public bool BlocksInput=>Paused||Capturing||(Shell!=null&&(Shell.ModalOpen||Shell.Transitioning))||Demo.Lifecycle.Suspended;
        public bool CanInspect=>Active&&!BlocksInput&&!ViewTransitioning&&!Demo.PathRunning&&!Demo.Instrument.Playing&&!Demo.Instrument.Recording&&!Demo.Capture.Busy&&!Demo.Lab.Busy&&Demo.Lab.State!=null;
        public bool CanManipulate=>Active&&!BlocksInput&&!ViewTransitioning&&!Demo.Guided&&!Demo.Menu.HasExample&&!Demo.PathRunning&&!Demo.Instrument.Playing&&!Demo.Instrument.Recording&&!Demo.Capture.Busy&&!Demo.Lab.Busy&&Demo.Lab.State!=null&&Demo.Lab.State.Model.Journal.Count<InteractionModel.MaximumFrames;
        public bool CanAdjustOffsets=>Active&&!Paused&&!Capturing&&!Demo.Lifecycle.Suspended&&!Demo.Guided&&!Demo.Menu.HasExample&&!Demo.PathRunning&&!Demo.Instrument.Playing&&!Demo.Instrument.Recording&&!Demo.Capture.Busy&&!Demo.Lab.Busy&&Demo.Lab.State!=null&&Demo.Lab.State.Model.Journal.Count<InteractionModel.MaximumFrames;
        public bool CanSave=>Demo.Lab.State!=null&&!Demo.Lab.Busy&&!Demo.PathRunning&&!Demo.Instrument.Playing&&!Demo.Instrument.Recording&&!Demo.Capture.Busy&&!Capturing&&!Demo.Lifecycle.Suspended;
        // Only explicit test harnesses override these. Normal use is app-local.
        [NonSerialized] public string SessionRootOverride,ScreenshotRootOverride,NumericalExportRootOverride;
        [NonSerialized] public bool SuppressPreferenceWrites;
        [NonSerialized] public bool IgnoreOsFocusForVerification;
        public Action ExitRequested;
        public string LastScreenshot {get;private set;}
        public string LastNumericalExport {get;private set;}
        public string Feedback {get;private set;}="Start with the guided tour, or explore at your own pace.";
        bool allowQuit,arrowGesture;
        readonly List<(Vector2 position,bool down,bool held)> pointerEvents=new();
        readonly List<RaycastResult> pointerHits=new();
        Vector2 eventPosition;
        bool eventHeld;
        Action pending;
        string pendingTitle;
        Vector3 dragStart;
        Double2 dragPhi;
        float zoom=1;
        Vector2 viewPan;
        public bool GridVisible {get;private set;}
        public bool Panning {get;private set;}
        public float Zoom=>zoom;
        public static float WheelZoom(float current,float delta)=>Mathf.Clamp(current*Mathf.Pow(1.5f,Mathf.Clamp(delta/120f,-4,4)),.1f,20);
        public static float WheelZoomFromInput(float current,float delta,bool normalized)=>WheelZoom(current,normalized?delta*120:delta);
        public static bool NormalizedScroll=>InputSystem.settings.scrollDeltaBehavior==InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
        public static float ScrollForNotches(float notches)=>notches*(NormalizedScroll?1:120);
        float baseSize=1,viewDuration=.18f;
        public float UnitScaleFor(int view)
        {
                if(view==1) return Demo.Lab.View.PhysicalScale*(float)Demo.Lab.State.Dataset.Metadata.DisplayScales.X;
                if(view==2) return Demo.Lab.View.InternalScale;
                float extent=1;foreach(var peak in Demo.Instrument.Reciprocal.Peaks) extent=Mathf.Max(extent,(float)Math.Abs(peak.Q.X),(float)Math.Abs(peak.Q.Y));
                return .55f/extent;
        }
        public float ViewUnitScale=>UnitScaleFor(FocusView);
        public Vector2 ViewCenter=>viewPan/ViewUnitScale;
        public Vector2 ViewSpan=>new Vector2(2*baseSize/zoom*Demo.Instrument.Camera.aspect,2*baseSize/zoom)/ViewUnitScale;
        public void ToggleGrid() {GridVisible=!GridVisible;Feedback=GridVisible?"Grid on. Each space has independent axes; minor spacing adapts to zoom.":"Grid hidden.";}
        public void ApplyView(string horizontal,string vertical,string span,bool width)
        {
            if(FocusView==0) throw new InvalidOperationException("Focus Structure, Window or Diffraction first.");
            double x=LinkedInstrument.Number(horizontal),y=LinkedInstrument.Number(vertical),s=LinkedInstrument.Number(span);
            double height=width?s/Demo.Instrument.Camera.aspect:s;
            float targetBase=baseSize;
            if(FocusView==1)
            {
                float radius=Demo.Lab.View.Settings.PhysicalFitRadius+.1f;
                foreach(var candidate in Demo.Lab.State.Dataset.Candidates) radius=Mathf.Max(radius,(float)Math.Max(Math.Abs(candidate.Physical.X),Math.Abs(candidate.Physical.Y))*ViewUnitScale+.1f);
                targetBase=(radius+.12f)*Mathf.Max(1,1/Demo.Instrument.Camera.aspect);
            }
            double requested=2*targetBase/(height*ViewUnitScale);
            if(s<=0||requested<.1||requested>20||Math.Abs(x)>10000||Math.Abs(y)>10000) throw new ArgumentOutOfRangeException("view","Use a positive span within the 10% to 2000% zoom range and center coordinates within +/-10000.");
            CancelGesture();viewPan=new Vector2((float)x,(float)y)*ViewUnitScale;zoom=(float)requested;
            // Exact Structure x/y view is the unrotated scientific coordinate plane.
            if(FocusView==1) physicalYaw=physicalPitch=0;
            ViewTransitioning=false;AnimateNextFrame();Reframe();Shell.CloseModal();Feedback="Exact view applied. Scientific state unchanged.";
        }
        float physicalYaw,physicalPitch;
        bool animateNext,cameraInitialized;
        Vector3 cameraFrom,cameraTo;
        float sizeFrom,sizeTo,viewStarted;
        public bool ViewTransitioning {get;private set;}
        public bool DiagramHasFocus {get;private set;}
        LabState framedState;
        int framedStep=-1,framedFocus=-1;
        float framedLayer;
        Rect framedRect;
        InputSystemUIInputModule inputModule;
        bool ownsInputModule;
        PreviewControls[] legacyControls;
        TextMesh[] legacyLabels;
        public Camera FrameClearCamera {get;private set;}
        public static DesktopWorkspace Install(ScientificDemo demo)
        {
            if(demo.Desktop!=null) return demo.Desktop;
            var workspace=demo.gameObject.AddComponent<DesktopWorkspace>();workspace.Demo=demo;demo.Desktop=workspace;return workspace;
        }
        void OnEnable() {InputSystem.onEvent+=ObserveMouse;if(!Application.isEditor) Application.wantsToQuit+=InterceptQuit;}
        void OnDisable() {InputSystem.onEvent-=ObserveMouse;if(!Application.isEditor) Application.wantsToQuit-=InterceptQuit;CancelGesture();if(FrameClearCamera!=null) FrameClearCamera.enabled=false;}
        void ObserveMouse(InputEventPtr input,InputDevice device)
        {
            if(device is not Mouse mouse||mouse!=Mouse.current) return;
            if(!input.IsA<StateEvent>()&&!input.IsA<DeltaStateEvent>()) return;
            // Preserve the press coordinate before later OS events overwrite the
            // frame's final mouse position. Rapid down/move/up can share a frame.
            bool hasPosition=mouse.position.ReadValueFromEvent(input,out Vector2 position);
            bool hasButton=mouse.leftButton.ReadValueFromEvent(input,out float button);
            if(!hasPosition&&!hasButton) return;
            if(hasPosition) eventPosition=position;
            bool held=hasButton?button>=.5f:eventHeld;
            bool down=held&&!eventHeld;
            if(down||eventHeld) {if(pointerEvents.Count<1024) pointerEvents.Add((eventPosition,down,held));else CancelGesture();}
            eventHeld=held;
        }
        bool PointerOverUi(Vector2 position)
        {
            if(EventSystem.current==null||Shell==null) return false;
            pointerHits.Clear();Shell.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(EventSystem.current) {position=position},pointerHits);
            return pointerHits.Count>0;
        }
        void Start()
        {
            Preferences=DesktopPreferences.Load();
            Demo.Instrument.Reciprocal.SetDisplay(Preferences.Diffraction);
            Demo.Instrument.Reciprocal.ShowContributionVector=false;
            var events=EventSystem.current;if(events==null) throw new InvalidOperationException("Desktop EventSystem is missing.");
            foreach(var module in events.GetComponents<BaseInputModule>()) module.enabled=false;
            inputModule=events.GetComponent<InputSystemUIInputModule>();
            if(inputModule==null) {inputModule=events.gameObject.AddComponent<InputSystemUIInputModule>();ownsInputModule=true;}
            inputModule.enabled=true;inputModule.AssignDefaultActions();inputModule.deselectOnBackgroundClick=true;
            // The science camera intentionally uses a partial viewport. URP only
            // clears that viewport, so overlay UI would otherwise accumulate over
            // pixels left by previous dialogs. Clear the complete backbuffer first.
            var clearRoot=new GameObject("Desktop full-window clear");clearRoot.transform.SetParent(transform,false);
            FrameClearCamera=clearRoot.AddComponent<Camera>();FrameClearCamera.cullingMask=0;
            FrameClearCamera.clearFlags=CameraClearFlags.SolidColor;FrameClearCamera.backgroundColor=DesktopTheme.Background;
            FrameClearCamera.rect=new Rect(0,0,1,1);FrameClearCamera.depth=Demo.Instrument.Camera.depth-1;
            FrameClearCamera.allowHDR=false;FrameClearCamera.allowMSAA=false;
            var clearData=clearRoot.AddComponent<UniversalAdditionalCameraData>();clearData.allowXRRendering=false;
            clearData.renderPostProcessing=false;clearData.renderShadows=false;
            var root=new GameObject("AperiTrace desktop shell",typeof(RectTransform));root.transform.SetParent(transform,false);
            Shell=root.AddComponent<DesktopShell>();Shell.Workspace=this;Shell.Build();ApplyPreferences(false);
            if(!Preferences.SkipWelcome) Shell.ShowModal("Welcome");
        }
        bool InterceptQuit()
        {
            if(allowQuit) return true;
            if(Shell!=null) RequestQuit();return false;
        }
        public void RequestQuit() {CancelGesture();Shell.ShowModal("Quit");}
        public void ConfirmQuit(bool save)
        {
            if(Shell.Modal!="Quit") throw new InvalidOperationException("Confirm Quit first.");
            if(save) Save();else if(Capturing||Demo.Capture.Busy) throw new InvalidOperationException("Wait for capture to finish.");
            allowQuit=true;if(ExitRequested!=null) ExitRequested();else Application.Quit();
        }
        public void RequestChange(string title,Action change)
        {
            if(Demo.PathRunning||Demo.Instrument.Playing||Demo.Instrument.Recording||Capturing||Demo.Capture.Busy)
                throw new InvalidOperationException("Resume and finish playback or capture first.");
            CancelGesture();
            if(Demo.UnsavedChanges) {pending=change;pendingTitle=title;Shell.ShowModal("Confirm");}else change();
        }
        public string PendingTitle=>pendingTitle??"Replace current work";
        public void ConfirmChange(bool save)
        {
            if(pending==null) throw new InvalidOperationException("No replacement is pending.");
            if(!CanSave) throw new InvalidOperationException("Wait until the app is ready.");
            if(save) Save();var change=pending;pending=null;Shell.CloseModal();change();
        }
        public void CancelModal() {pending=null;if(Shell.Modal=="Welcome") Explore();else Shell.CloseModal();}
        public void StartGuide()
        {
            RequestChange("Restart the guided tour",()=> {Paused=false;ReferencePinned=false;Shell.CloseModal();Demo.StartGuide(false);FocusView=0;zoom=1;viewPan=Vector2.zero;Reframe();});
        }
        public void Explore()
        {
            CancelGesture();Paused=false;ReferencePinned=false;Shell.CloseModal();Demo.Menu.Explore();zoom=1;viewPan=Vector2.zero;Reframe();Feedback="Drag the cyan control. Press I for numerical inspection and precision controls.";
        }
        public void Next()
        {
            if(BlocksInput||!Demo.Guided) return;Demo.Advance();Reframe();
        }
        public void ChooseExample(int index)
        {
            if(!CanSave) throw new InvalidOperationException("Resume and finish playback or capture first.");
            CancelGesture();Paused=false;ReferencePinned=false;Shell.CloseModal();Demo.Menu.ChooseExample(index);FocusView=0;zoom=1;viewPan=Vector2.zero;Reframe();
            Feedback="Compare Before / After, then choose Explore to take control.";
        }
        public void ToggleExample()
        {
            if(BlocksInput||!CanSave) return;Demo.Menu.ToggleExampleState();Reframe();
        }
        public void Save()
        {
            if(!CanSave) throw new InvalidOperationException("Resume and finish playback or capture before saving.");
            Demo.SaveSession();Feedback="Session saved locally. Your previous saves are kept.";Shell.RefreshModal();
        }
        public void Load(string path)=>RequestChange("Load a saved session",()=> {Paused=false;Demo.LoadSession(path);FocusView=0;zoom=1;viewPan=Vector2.zero;Reframe();Feedback="Session restored.";});
        public void ClearTrace()=>RequestChange("Clear the trace and reset the knob",()=> {Demo.Instrument.ClearJournal();Demo.Lab.ResetPhi();Feedback="Trace cleared by your request.";});
        public void SetPaused(bool value) {Paused=value;PausedAfterFocusLoss=false;CancelGesture();Feedback=value?"Paused. Your current state is preserved.":"Resumed.";}
        public static void NormalizeSessionPresentation(InstrumentSession session)
        {
            // Desktop offscreen origins and navigation are temporary presentation,
            // not scientific state. Keep the existing cross-platform session valid.
            session.CameraPosition=new[]{0f,1.2f,-6.1f};session.CameraRotation=new[]{0f,0f,0f};session.CameraPivot=new[]{0f,1.2f,1.35f};
            session.PhysicalOrigin=new[]{-2.3f,1.2f,1.35f};session.InternalOrigin=new[]{0f,1.2f,1.35f};session.ReciprocalOrigin=new[]{2.3f,1.2f,1.35f};
            session.PanelOrigin=new[]{0f,1.2f,2f};session.CameraFov=42;
        }
        public void SetFocus(int view)
        {
            FocusView=Mathf.Clamp(view,0,3);zoom=1;viewPan=Vector2.zero;CancelGesture();AnimateNextFrame();Reframe();
        }
        public void ResetView() {zoom=1;viewPan=Vector2.zero;physicalYaw=physicalPitch=0;FocusView=0;AnimateNextFrame();Reframe();Feedback="All three views restored. The scientific state is unchanged.";}
        public void AnimateNextFrame() {animateNext=true;viewDuration=.18f;}
        public void Notify(string message)=>Feedback=message;
        public void ApplyOffsets(string x,string y,bool dismiss=true)
        {
            if(!CanAdjustOffsets) throw new InvalidOperationException("Use Free exploration and resume before changing exact offsets.");
            double a=LinkedInstrument.Number(x),b=LinkedInstrument.Number(y);
            if(a<Demo.Lab.PhiMin.X||a>Demo.Lab.PhiMax.X||b<Demo.Lab.PhiMin.Y||b>Demo.Lab.PhiMax.Y) throw new ArgumentOutOfRangeException("offset","Enter values inside the displayed domain. Values are not silently clamped.");
            if(!ReferencePinned) Demo.Comparison.BeginComparison();
            try {Demo.Lab.SetPhi(a,b);if(Demo.Lab.State.Phi.X!=a||Demo.Lab.State.Phi.Y!=b) throw new InvalidOperationException("Offset application was refused by the scientific model.");}
            finally {if(!ReferencePinned) Demo.Comparison.EndComparison();}
            if(dismiss) Shell.CloseModal();Feedback="Exact offsets applied. Wait for diffraction Ready.";
        }
        public void ExportNumbers()
        {
            LastNumericalExport=DesktopResearchTools.Export(this,NumericalExportRootOverride??Path.Combine(Application.persistentDataPath,"AperiTraceNumericalExports"));
            Feedback="Numerical CSV tables + checksum-linked state exported locally.";Shell.ShowModal("Exported");
        }
        public void ClearDiagramFocus()=>DiagramHasFocus=false;
        public void Nudge(double x,double y)
        {
            if(!CanManipulate) return;if(!ReferencePinned) Demo.Comparison.BeginComparison();Demo.Lab.SetPhi(Demo.Lab.State.Phi.X+x,Demo.Lab.State.Phi.Y+y);if(!ReferencePinned) Demo.Comparison.EndComparison();
        }
        public void PinComparison()
        {
            ReferencePinned=!ReferencePinned;if(ReferencePinned) Demo.Comparison.BeginComparison();else Demo.Comparison.EndComparison();
            Feedback=ReferencePinned?"Baseline pinned. Changes compare with this state.":"Reference unlocked. The next gesture starts a new comparison.";
        }
        public void ApplyPreferences(bool persist=true)
        {
            Demo.Preferences.HighContrast=Preferences.HighContrast;
            Demo.SetPalette(Preferences.AlternativePalette);Demo.Lab.View.FadeSeconds=Preferences.ReducedMotion?0:.15f;
            if(persist&&!SuppressPreferenceWrites) Preferences.Save();Shell.Restyle();
        }
        public void ApplyDiffraction(DiffractionDisplay settings)
        {
            settings.Validate();Preferences.Diffraction=settings.Copy();Demo.Instrument.Reciprocal.SetDisplay(settings);
            if(!SuppressPreferenceWrites) Preferences.Save();
            Shell.Restyle();Feedback="Diffraction display updated. Numerical intensities and exported results are unchanged.";
        }
        public void Run(Action action)
        {
            try {action();}catch(Exception error) {Feedback="Not completed: "+error.Message;Shell.ShowError(Feedback);Debug.LogWarning("[AperiTrace] "+Feedback);}
        }
        public string CaptureScreenshot(string folder=null,string name=null)
        {
            if(!CanSave||!Demo.Instrument.Reciprocal.Current) throw new InvalidOperationException("Wait for diffraction Ready before taking a screenshot.");
            if(ViewTransitioning||Shell.Transitioning) throw new InvalidOperationException("Wait for the view transition to finish before taking a screenshot.");
            CancelGesture();Capturing=true;
            try
            {
                folder??=ScreenshotRootOverride??Path.Combine(Application.persistentDataPath,"AperiTraceDesktopScreenshots");
                name??="desktop-"+Guid.NewGuid().ToString("N");LastScreenshot=DesktopFrameCapture.Save(this,folder,name);
                Feedback="Screenshot + scientific state saved locally.";return LastScreenshot;
            }
            finally {Capturing=false;}
        }
        public void CancelGesture()
        {
            if((Dragging||arrowGesture)&&!ReferencePinned) Demo?.Comparison?.EndComparison();Dragging=arrowGesture=Panning=false;Demo?.Lab?.SyncHandlePose();
            pointerEvents.Clear();
        }
        bool Pointer(Vector2 screen,out Vector3 point)
        {
            var origin=Demo.Lab.View.InternalOrigin;var ray=Demo.Instrument.Camera.ScreenPointToRay(screen);
            var plane=new Plane(origin.forward,origin.position);
            if(plane.Raycast(ray,out float distance)) {point=ray.GetPoint(distance);return true;}point=default;return false;
        }
        public void ProcessInput()
        {
            if(!CanInspect) {CancelGesture();return;}
            if(!CanManipulate&&(Dragging||arrowGesture)) CancelGesture();
            var keyboard=Keyboard.current;var selected=EventSystem.current?.currentSelectedGameObject;
            bool uiFocus=selected!=null&&selected.GetComponent<Selectable>()!=null;
            bool arrows=CanManipulate&&DiagramHasFocus&&!uiFocus&&keyboard!=null&&(keyboard.leftArrowKey.isPressed||keyboard.rightArrowKey.isPressed||keyboard.upArrowKey.isPressed||keyboard.downArrowKey.isPressed);
            if(arrows)
            {
                if(!arrowGesture) {if(!ReferencePinned) Demo.Comparison.BeginComparison();arrowGesture=true;}
                double step=Time.unscaledDeltaTime*.12;var phi=Demo.Lab.State.Phi;
                Demo.Lab.SetPhi(phi.X+step*((keyboard.rightArrowKey.isPressed?1:0)-(keyboard.leftArrowKey.isPressed?1:0)),phi.Y+step*((keyboard.upArrowKey.isPressed?1:0)-(keyboard.downArrowKey.isPressed?1:0)));
            }
            else if(arrowGesture) {arrowGesture=false;if(!ReferencePinned) Demo.Comparison.EndComparison();}
            var samples=pointerEvents.ToArray();pointerEvents.Clear();
            foreach(var sample in samples) ProcessPointer(sample.position,sample.down,sample.held);
        }
        void ProcessPointer(Vector2 screen,bool down,bool held)
        {
            bool overUi=down&&PointerOverUi(screen);
            if(down&&overUi) DiagramHasFocus=false;
            if(down&&!overUi&&Demo.Instrument.Camera.pixelRect.Contains(screen))
            {
                if(EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(null);
                DiagramHasFocus=true;
                Vector3 knob=Demo.Instrument.Camera.WorldToScreenPoint(Demo.Lab.PhasonHandle.position);
                if(CanManipulate&&(FocusView==0||FocusView==2)&&Vector2.Distance(screen,knob)<=24&&Pointer(screen,out dragStart))
                {Dragging=true;dragPhi=Demo.Lab.State.Phi;if(!ReferencePinned) Demo.Comparison.BeginComparison();}
                else
                {
                    var ray=Demo.Instrument.Camera.ScreenPointToRay(screen);
                    int a=(FocusView==0||FocusView==1)&&Demo.Lab.View.PresentationPhysical?Demo.Lab.View.Pick(ray,false):-1,b=(FocusView==0||FocusView==2)&&Demo.Lab.View.PresentationInternal?Demo.Lab.View.Pick(ray,true):-1;
                    if(a>=0||b>=0) {Demo.Lab.Select(a>=0?a:b);Shell.Inspect(1);}
                    else if(FocusView==0||FocusView==3) {int peak=Demo.Instrument.Reciprocal.Pick(ray);if(peak>=0) {Demo.Instrument.Reciprocal.SelectedPeak=peak;Shell.Inspect(2);}}
                }
            }
            if(Dragging&&Pointer(screen,out var current))
            {var delta=PhasonHandleControl.PlaneDelta(Demo.Lab.View.InternalOrigin,dragStart,current,Demo.Lab.View.InternalScale);Demo.Lab.SetPhi(dragPhi.X+delta.x,dragPhi.Y+delta.y);}
            if(Dragging&&!held) CancelGesture();
        }
        void InspectInput()
        {
            var mouse=Mouse.current;if(mouse==null||BlocksInput||Demo.Lab.State==null||Demo.Capture.Busy) {Panning=false;return;}
            bool inside=Demo.Instrument.Camera.pixelRect.Contains(mouse.position.ReadValue())&&!PointerOverUi(mouse.position.ReadValue());
            if(mouse.middleButton.wasReleasedThisFrame||!mouse.middleButton.isPressed) Panning=false;
            if(inside&&mouse.middleButton.wasPressedThisFrame&&!Dragging) Panning=true;
            if(Panning)
            {
                var camera=Demo.Instrument.Camera;viewPan-=mouse.delta.ReadValue()*(2*camera.orthographicSize/Mathf.Max(1,camera.pixelHeight));
                ViewTransitioning=false;animateNext=false;Reframe();return;
            }
            if(!inside||Dragging) return;
            bool changed=false;
            if(mouse.scroll.ReadValue().y!=0) {zoom=WheelZoomFromInput(zoom,mouse.scroll.ReadValue().y,NormalizedScroll);changed=true;}
            if(FocusView==1&&mouse.rightButton.isPressed)
            {
                var delta=mouse.delta.ReadValue();physicalYaw+=delta.x*.2f;physicalPitch=Mathf.Clamp(physicalPitch-delta.y*.2f,-70,70);changed|=delta!=Vector2.zero;
            }
            if(changed) {AnimateNextFrame();viewDuration=.085f;Reframe();}
        }
        void OnApplicationFocus(bool focused) {if(!IgnoreOsFocusForVerification&&!focused&&Active&&!Paused) {SetPaused(true);PausedAfterFocusLoss=true;}}
        void Update()
        {
            if(!Active) return;if(BlocksInput) CancelGesture();
            InspectInput();
            var key=Keyboard.current;if(key==null||QuestUiTheme.HasTextInputFocus()) return;
            if(key.tabKey.wasPressedThisFrame) {Shell.FocusNext(key.shiftKey.isPressed?-1:1);return;}
            bool ctrl=key.ctrlKey.isPressed;
            if(ctrl&&key.sKey.wasPressedThisFrame) Run(()=> {if(key.shiftKey.isPressed) CaptureScreenshot();else Save();});
            if(ctrl&&key.oKey.wasPressedThisFrame) Shell.ShowModal("Sessions");
            if(key.escapeKey.wasPressedThisFrame) {if(Shell.ModalOpen) CancelModal();else if(Shell.InspectorOpen) Shell.ToggleInspector();else Shell.ShowModal("Menu");return;}
            if(key.iKey.wasPressedThisFrame&&!Shell.ModalOpen) Shell.ToggleInspector();
            if(Shell.ModalOpen||Demo.Lifecycle.Suspended) return;
            if(key.pKey.wasPressedThisFrame) SetPaused(!Paused);
            if(Paused) return;
            if(key.fKey.wasPressedThisFrame) Explore();if(key.gKey.wasPressedThisFrame) ToggleGrid();if(key.nKey.wasPressedThisFrame) Next();
            if(key.homeKey.wasPressedThisFrame) ResetView();if(key.digit0Key.wasPressedThisFrame&&CanManipulate) Run(()=>Demo.Lab.ResetPhi());
            if(key.eKey.wasPressedThisFrame&&CanManipulate) Demo.Lab.ToggleEdges();
        }
        void LateUpdate()
        {
            if(!Active) return;
            Demo.UI.gameObject.SetActive(false);Demo.Instrument.UI.gameObject.SetActive(false);
            legacyControls??=Demo.Lab.GetComponentsInChildren<PreviewControls>(true);legacyLabels??=Demo.Lab.GetComponentsInChildren<TextMesh>(true);
            foreach(var control in legacyControls) if(control!=null) control.gameObject.SetActive(false);
            foreach(var text in legacyLabels) if(text!=null) text.gameObject.SetActive(false);
            Demo.Instrument.Reciprocal.ShowContributionLabel=false;
            if(Demo.Lab.State!=null&&(framedState!=Demo.Lab.State||framedStep!=Demo.Step||framedFocus!=FocusView||framedLayer!=Demo.Lab.View.LayerSeparation||framedRect!=Shell.DiagramRect)) Reframe();
            if(ViewTransitioning)
            {
                var camera=Demo.Instrument.Camera;float t=Preferences.ReducedMotion?1:Mathf.Clamp01((Time.unscaledTime-viewStarted)/viewDuration),eased=DesktopMotion.Ease(t);
                camera.transform.position=Vector3.Lerp(cameraFrom,cameraTo,eased);camera.orthographicSize=Mathf.Lerp(sizeFrom,sizeTo,eased);Shell.AlignViewHeadings(camera);
                if(t>=1) ViewTransitioning=false;
            }
            Shell.RefreshLive();
        }
        public void Reframe()
        {
            if(Shell==null||Demo.Lab.State==null) return;Canvas.ForceUpdateCanvases();
            var view=Demo.Lab.View;var rect=Shell.DiagramRect;var camera=Demo.Instrument.Camera;
            var oldPosition=camera.transform.position;float oldSize=camera.orthographicSize;
            var physicalRotation=Quaternion.Euler(physicalPitch,physicalYaw,0);
            if(view.PhysicalOrigin.rotation!=physicalRotation) {view.PhysicalOrigin.rotation=physicalRotation;view.Rebuild(Demo.Lab.State);}
            float internalRadius=.65f;
            foreach(var candidate in Demo.Lab.State.Dataset.Candidates)
            {
                float x=Mathf.Abs((float)candidate.Internal.X)+Mathf.Max(Mathf.Abs((float)Demo.Lab.PhiMin.X),Mathf.Abs((float)Demo.Lab.PhiMax.X));
                float y=Mathf.Abs((float)candidate.Internal.Y)+Mathf.Max(Mathf.Abs((float)Demo.Lab.PhiMin.Y),Mathf.Abs((float)Demo.Lab.PhiMax.Y));
                internalRadius=Mathf.Max(internalRadius,Mathf.Max(x,y)*view.InternalScale+.08f);
            }
            float physicalRadius=view.Settings.PhysicalFitRadius+.10f,qRadius=.65f,gap=.32f;
            for(int i=0;i<Demo.Lab.State.Dataset.Candidates.Count;i++) {var point=view.PointPosition(i,false)-view.PhysicalOrigin.position;physicalRadius=Mathf.Max(physicalRadius,Mathf.Max(Mathf.Abs(point.x),Mathf.Abs(point.y))+.1f);}
            float left=-(physicalRadius+internalRadius+gap),right=internalRadius+qRadius+gap;
            float center=FocusView==1?left:FocusView==3?right:0;
            view.PhysicalOrigin.SetPositionAndRotation(new Vector3(FocusView==0||FocusView==1?left: -30,1.2f,FocusView==0||FocusView==1?1.35f:1000),physicalRotation);
            view.InternalOrigin.SetPositionAndRotation(new Vector3(FocusView==0||FocusView==2?0:30,1.2f,FocusView==0||FocusView==2?1.35f:1000),Quaternion.identity);
            Demo.Instrument.Reciprocal.Origin.SetPositionAndRotation(new Vector3(FocusView==0||FocusView==3?right:40,1.2f,FocusView==0||FocusView==3?1.35f:1000),Quaternion.identity);
            camera.orthographic=true;camera.rect=rect;camera.transform.SetPositionAndRotation(new Vector3(center+viewPan.x,1.2f+viewPan.y,-6.1f),Quaternion.identity);
            camera.aspect=Mathf.Max(.1f,Screen.width*rect.width/Mathf.Max(1,Screen.height*rect.height));camera.nearClipPlane=.01f;camera.farClipPlane=100;
            camera.backgroundColor=DesktopTheme.Background;
            camera.clearFlags=CameraClearFlags.SolidColor;
            if(FrameClearCamera!=null) {FrameClearCamera.enabled=isActiveAndEnabled;FrameClearCamera.depth=camera.depth-1;}
            float halfWidth=FocusView==0?Mathf.Max(-left+physicalRadius,right+qRadius)+.18f:FocusView==1?physicalRadius+.12f:FocusView==2?internalRadius+.12f:qRadius+.12f;
            float halfHeight=FocusView==0?Mathf.Max(physicalRadius,internalRadius)+.12f:FocusView==1?physicalRadius+.12f:FocusView==2?internalRadius+.12f:qRadius+.12f;
            baseSize=Mathf.Max(halfHeight,halfWidth/camera.aspect);camera.orthographicSize=baseSize/zoom;
            if(animateNext&&cameraInitialized&&!Preferences.ReducedMotion&&(Vector3.SqrMagnitude(oldPosition-camera.transform.position)>1e-8f||Mathf.Abs(oldSize-camera.orthographicSize)>1e-5f))
            {cameraFrom=oldPosition;sizeFrom=oldSize;viewStarted=Time.unscaledTime;ViewTransitioning=true;}
            if(ViewTransitioning)
            {cameraTo=camera.transform.position;sizeTo=camera.orthographicSize;camera.transform.position=oldPosition;camera.orthographicSize=oldSize;}
            animateNext=false;cameraInitialized=true;
            Shell.AlignViewHeadings(camera);
            view.Rebuild(Demo.Lab.State);Demo.Instrument.Reciprocal.Rebuild();Demo.Lab.SyncHandlePose();
            framedState=Demo.Lab.State;framedStep=Demo.Step;framedFocus=FocusView;framedLayer=view.LayerSeparation;framedRect=rect;
        }
        void OnDestroy() {if(ownsInputModule&&inputModule!=null) Destroy(inputModule);}
    }
}
