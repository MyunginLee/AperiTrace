using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Explicit integration test only. Real UI/model rendering and simulated
    // Input System events; never label these as a human usability experiment.
    [DefaultExecutionOrder(1000)]
    public sealed class DesktopUxVerification : MonoBehaviour
    {
        public string Output;
        public Action<bool> Finished;
        public ScientificDemo Demo;
        public bool Done {get;private set;}
        public bool Passed {get;private set;}
        DesktopWorkspace workspace;
        Keyboard keyboard;
        Mouse mouse;
        InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        int stage,nextFrame,gallery,textSize;
        float nextTime,deadline;
        Vector2 pointer;
        Double2 heldPhi;
        string key,path,savedJson;
        int journal;
        bool exitSignal;
        float zoomBefore;
        Vector3 panBefore;
        float requestedWidth;
        int navigationView;
        int displayPage,displaySize,displayPalettePage;
        double[] minorBefore;
        int gridTextSize;
        bool watchGridFade,observedGridFade;
        static readonly string[] displayScreens={"Diffraction","Colormaps","DiffractionRange","DiffractionFilters","CustomColors"};
        readonly JObject evidence=new();
        void Start()
        {
            Directory.CreateDirectory(Output);deadline=Time.realtimeSinceStartup+240;
            background=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(mouse);
        }
        void Check(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
        void Wait(float seconds=.20f) {nextFrame=Time.frameCount+3;nextTime=Time.unscaledTime+seconds;}
        Button Button(string id)=>workspace.Shell.GetComponentsInChildren<Button>(true).First(b=>b.name==id&&b.gameObject.activeInHierarchy);
        Vector2 Center(Button button)=>RectTransformUtility.WorldToScreenPoint(null,button.GetComponent<RectTransform>().TransformPoint(button.GetComponent<RectTransform>().rect.center));
        void MouseAt(Vector2 point,bool pressed=false)
        {pointer=point;InputSystem.QueueStateEvent(mouse,new MouseState {position=point,buttons=(ushort)(pressed?1:0)});}
        void Key(params Key[] values)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(values));
        void Snapshot(string name)
        {
            workspace.Shell.RefreshLive();Canvas.ForceUpdateCanvases();CheckLayout();
            workspace.CaptureScreenshot(Path.Combine(Output,"screens"),name);
        }
        void CheckLayout()
        {
            Check(!workspace.Shell.GetComponentsInChildren<Text>(true).Any(t=>t.name=="offline-note"),"Scientific provenance notice must be contextual, not a permanent footer.");
            if(workspace.Shell.Modal=="About")
            {
                var about=workspace.Shell.GetComponentsInChildren<Text>();
                Check(about.Any(t=>t.name=="about-license"&&t.text.Contains("without an internet connection")),"About lost the offline-operation explanation.");
                Check(about.Any(t=>t.name=="about-science"&&t.text.Contains("calculated")&&t.text.Contains("not measured")),"About lost the diffraction provenance explanation.");
            }
            var clear=workspace.FrameClearCamera;
            Check(clear!=null&&clear.enabled&&clear.rect==new Rect(0,0,1,1)&&clear.clearFlags==CameraClearFlags.SolidColor&&clear.cullingMask==0&&clear.depth<Demo.Instrument.Camera.depth,"Full-window clear must precede the partial science viewport.");
            var rect=workspace.Shell.DiagramRect;
            Check(rect.x>=0&&rect.y>=0&&rect.xMax<=1.001f&&rect.yMax<=1.001f&&rect.width>.25f&&rect.height>.12f,"Diagram viewport is outside the window or too small.");
            string[] headings={"structure-heading","selection-heading","diffraction-heading"};
            var origins=new[]{Demo.Lab.View.PhysicalOrigin,Demo.Lab.View.InternalOrigin,Demo.Instrument.Reciprocal.Origin};
            var labels=workspace.Shell.GetComponentsInChildren<Text>();
            for(int i=0;i<headings.Length;i++)
            {
                var label=labels.FirstOrDefault(t=>t.name==headings[i]);if(label==null) continue;
                float actual=RectTransformUtility.WorldToScreenPoint(null,label.rectTransform.TransformPoint(label.rectTransform.rect.center)).x;
                float expected=Demo.Instrument.Camera.WorldToScreenPoint(origins[i].position).x;
                if(workspace.FocusView!=0) expected=Screen.width*(rect.x+rect.width*.5f);
                Check(Mathf.Abs(actual-expected)<1.5f,"Diagram heading is not aligned with its own view: "+headings[i]);
                Check(actual>=0&&actual<=Screen.width,"Diagram heading outside window: "+headings[i]);
            }
            foreach(var label in workspace.Shell.GetComponentsInChildren<Text>().Where(t=>t.gameObject.activeInHierarchy))
            {
                var size=label.rectTransform.rect.size;
                float height=label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text,label.GetGenerationSettings(size))/label.pixelsPerUnit;
                Check(height<=size.y+1.5f,"Clipped desktop text: "+label.name+" "+height+" > "+size.y+" @ "+size.x);
                Check(label.font.name.StartsWith("Inter")&&label.fontStyle==FontStyle.Normal,"Desktop typography uses a fallback or synthetic face.");
            }
            foreach(var button in workspace.Shell.GetComponentsInChildren<Button>().Where(b=>b.gameObject.activeInHierarchy))
            {
                var size=button.GetComponent<RectTransform>().rect.size;Check(size.x>=44&&size.y>=44,"Desktop action target too small: "+button.name);
                Check(button.GetComponents<Outline>().Length==1,"Stacked button outlines: "+button.name);
            }
        }
        void UiHit(string id)
        {
            var button=Button(id);var raycaster=workspace.Shell.GetComponent<GraphicRaycaster>();
            var hits=new System.Collections.Generic.List<RaycastResult>();raycaster.Raycast(new PointerEventData(EventSystem.current) {position=Center(button)},hits);
            Check(hits.Any(hit=>hit.gameObject==button.gameObject),"Desktop button not hit-testable: "+id);
        }
        void LateUpdate()
        {
            if(Done) return;
            if(watchGridFade&&workspace!=null&&workspace.Shell!=null&&workspace.Shell.Grid.Transitioning) observedGridFade=true;
            try
            {
                if(Time.realtimeSinceStartup>deadline) throw new TimeoutException("Desktop UX integration test timed out at stage "+stage);
                if(Demo==null) Demo=FindFirstObjectByType<ScientificDemo>();
                if(Demo==null||Demo.Lab.State==null||Demo.Lab.Busy||!Demo.Instrument.Reciprocal.Current) return;
                if(workspace==null) {workspace=DesktopWorkspace.Install(Demo);workspace.SuppressPreferenceWrites=true;workspace.IgnoreOsFocusForVerification=true;workspace.SessionRootOverride=Path.Combine(Output,"sessions");workspace.ScreenshotRootOverride=Path.Combine(Output,"screens");return;}
                if(!workspace.Active||Time.frameCount<nextFrame||Time.unscaledTime<nextTime) return;
                Demo.Lifecycle.SetFocused(true);
                Tick();
            }
            catch(Exception error)
            {
                File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());Debug.LogException(error);End(false);
            }
        }
        void Tick()
        {
            if(stage==0)
            {
                workspace.SetPaused(false);workspace.Preferences.TextSize=0;workspace.Preferences.HighContrast=false;workspace.Preferences.ReducedMotion=false;workspace.Preferences.AlternativePalette=false;workspace.ApplyPreferences(false);
                workspace.Shell.ShowModal("Welcome");Wait();stage=1;return;
            }
            if(stage==1)
            {
                Check(!Demo.UI.gameObject.activeInHierarchy&&!Demo.Instrument.UI.gameObject.activeInHierarchy,"Legacy panels remain visible.");
                Snapshot("welcome");UiHit("welcome-explore");MouseAt(Center(Button("welcome-explore")));Wait(.03f);stage=2;return;
            }
            if(stage==2) {MouseAt(pointer,true);Wait(.03f);stage=3;return;}
            if(stage==3) {MouseAt(pointer);Wait();stage=4;return;}
            if(stage==4)
            {
                var module=EventSystem.current.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                File.WriteAllText(Path.Combine(Output,"input_probe.json"),new JObject { ["mouse_position"]=JArray.FromObject(new[]{mouse.position.ReadValue().x,mouse.position.ReadValue().y}),
                    ["action_position"]=JArray.FromObject(new[]{module.point.action.ReadValue<Vector2>().x,module.point.action.ReadValue<Vector2>().y}),["point_enabled"]=module.point.action.enabled,
                    ["click_enabled"]=module.leftClick.action.enabled,["module"]=EventSystem.current.currentInputModule?.GetType().Name,["cursor_lock"]=Cursor.lockState.ToString(),["event_system_focused"]=EventSystem.current.isFocused }.ToString());
                Check(!workspace.Shell.ModalOpen&&!Demo.Guided,"Input System pointer click did not enter exploration.");evidence["actual_ui_raycast_and_simulated_mouse_click"]=true;
                Snapshot("workspace");Key(UnityEngine.InputSystem.Key.Tab);Wait(.03f);stage=5;return;
            }
            if(stage==5)
            {
                Key();Check(EventSystem.current.currentSelectedGameObject!=null,"Tab did not select a desktop action.");
                evidence["tab_focus_navigation"]=true;workspace.Shell.ShowModal("Help");Wait();stage=6;return;
            }
            if(stage==6)
            {
                Snapshot("help");Key(UnityEngine.InputSystem.Key.Escape);Wait(.03f);stage=7;return;
            }
            if(stage==7)
            {
                Key();Check(!workspace.Shell.ModalOpen,"Escape did not dismiss the dialog.");evidence["escape_dismisses_dialog"]=true;
                EventSystem.current.SetSelectedGameObject(null);Demo.Lab.SetPhi(.181,.047);Wait();stage=8;return;
            }
            if(stage==8)
            {
                heldPhi=Demo.Lab.State.Phi;journal=Demo.Lab.State.Model.Journal.Count;
                MouseAt(Demo.Instrument.Camera.WorldToScreenPoint(Demo.Lab.PhasonHandle.position));Wait(.03f);stage=9;return;
            }
            if(stage==9) {MouseAt(pointer,true);Wait(.03f);stage=10;return;}
            if(stage==10)
            {
                Check(workspace.Dragging,"Cyan knob cannot be acquired with the mouse.");
                Check(Demo.Lab.State.Phi.X==heldPhi.X&&Demo.Lab.State.Phi.Y==heldPhi.Y,"Knob jumps on initial press.");
                MouseAt(pointer+new Vector2(75,30),true);Wait(.05f);stage=11;return;
            }
            if(stage==11)
            {
                Check(Demo.Lab.State.Phi.X!=heldPhi.X||Demo.Lab.State.Phi.Y!=heldPhi.Y,"Mouse drag did not change the offset.");
                workspace.Shell.ShowModal("Menu");key=Demo.Lab.State.Model.Key("");journal=Demo.Lab.State.Model.Journal.Count;
                MouseAt(pointer+new Vector2(100,60),true);Wait();stage=12;return;
            }
            if(stage==12)
            {
                Check(!workspace.Dragging&&key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"Opening a dialog did not cancel/freeze the drag.");
                MouseAt(pointer);workspace.CancelModal();workspace.SetPaused(true);key=Demo.Lab.State.Model.Key("");Key(UnityEngine.InputSystem.Key.RightArrow);Wait(.12f);stage=13;return;
            }
            if(stage==13)
            {
                Check(key==Demo.Lab.State.Model.Key(""),"Paused keyboard input changes the model.");Key();workspace.SetPaused(false);
                Demo.Lifecycle.SetFocused(false);workspace.Nudge(.01,0);Check(key==Demo.Lab.State.Model.Key(""),"Unfocused input changes the model.");Demo.Lifecycle.SetFocused(true);
                evidence["mouse_drag_no_initial_jump_modal_cancel_pause_and_focus_safety"]=true;
                workspace.Explore();Demo.Lab.SetPhi(.181,.047);Wait();stage=14;return;
            }
            if(stage==14)
            {
                workspace.Save();path=Demo.LastSessionPath;savedJson=LocalSessionStore.Read(Demo.SessionFolder,path);key=Demo.Lab.State.Model.Key("");
                workspace.Nudge(-.05,0);Wait();stage=15;return;
            }
            if(stage==15)
            {
                workspace.Load(path);Check(workspace.Shell.Modal=="Confirm","Dirty session load skipped confirmation.");workspace.CancelModal();
                workspace.Load(path);workspace.ConfirmChange(false);Wait();stage=16;return;
            }
            if(stage==16)
            {
                Check(key==Demo.Lab.State.Model.Key(""),"Session restore changed scientific state.");evidence["real_save_load_dirty_confirmation_and_cancel"]=true;
                workspace.Preferences.AlternativePalette=true;workspace.ApplyPreferences(false);Demo.LoadSessionJson(savedJson);
                Check(Demo.AccessiblePalette,"Session load overrides desktop accessibility palette.");workspace.Preferences.AlternativePalette=false;workspace.ApplyPreferences(false);
                workspace.PinComparison();heldPhi=Demo.Comparison.Baseline.Phi;workspace.Nudge(.01,0);workspace.Nudge(.01,0);
                Check(Demo.Comparison.Baseline.Phi.X==heldPhi.X&&Demo.Comparison.Baseline.Phi.Y==heldPhi.Y,"Pinned baseline is recaptured by the next gesture.");workspace.PinComparison();
                evidence["local_palette_survives_load_and_pinned_reference_survives_gestures"]=true;
                workspace.Shell.ShowModal("Sessions");Wait();stage=17;return;
            }
            if(stage==17) {Snapshot("sessions");workspace.CancelModal();gallery=0;workspace.ChooseExample(gallery);Wait();stage=18;return;}
            if(stage==18)
            {
                Snapshot("example_"+gallery.ToString("00"));gallery++;
                if(gallery<QuestExampleLibrary.All.Count) {workspace.ChooseExample(gallery);Wait();return;}
                evidence["all_twenty_examples_ready_and_rendered"]=true;workspace.Explore();Demo.Lab.SetPhi(.181,.047);textSize=0;Wait();stage=19;return;
            }
            if(stage==19)
            {
                workspace.Preferences.TextSize=textSize;workspace.Preferences.HighContrast=textSize==2;workspace.Preferences.ReducedMotion=textSize==2;workspace.ApplyPreferences(false);workspace.Shell.ShowModal("Settings");Wait();stage=20;return;
            }
            if(stage==20)
            {
                Snapshot("settings_size_"+textSize);workspace.CancelModal();Wait();stage=32;return;
            }
            if(stage==32)
            {
                Snapshot("workspace_size_"+textSize);
                Demo.Instrument.Reciprocal.SelectedPeak=Array.FindIndex(Demo.Instrument.Reciprocal.Peaks,p=>p.Q.X==0&&p.Q.Y==0&&p.Q.Z==0);
                workspace.Shell.Inspect(2);Wait(.35f);stage=33;return;
            }
            if(stage==33)
            {
                Snapshot("inspector_size_"+textSize);workspace.Shell.ToggleInspector();Wait(.4f);stage=34;return;
            }
            if(stage==34)
            {
                Demo.Instrument.Reciprocal.SelectedPeak=-1;
                textSize++;if(textSize<3) {stage=19;return;}
                evidence["all_three_text_sizes_contrast_and_reduced_motion_no_clipping"]=true;workspace.Preferences.TextSize=0;workspace.Preferences.HighContrast=false;workspace.Preferences.ReducedMotion=false;workspace.ApplyPreferences(false);Wait();stage=21;return;
            }
            if(stage==21)
            {
                key=Demo.Lab.State.Model.Key("");journal=Demo.Lab.State.Model.Journal.Count;workspace.SetFocus(1);Wait();stage=22;return;
            }
            if(stage==22)
            {
                Snapshot("structure_focus");workspace.SetFocus(2);Wait();stage=23;return;
            }
            if(stage==23) {Snapshot("selection_focus");workspace.SetFocus(3);Wait();stage=24;return;}
            if(stage==24)
            {
                Snapshot("diffraction_focus");zoomBefore=Demo.Instrument.Camera.orthographicSize;
                MouseAt(Demo.Instrument.Camera.WorldToScreenPoint(Demo.Instrument.Reciprocal.Origin.position));Wait(.03f);stage=35;return;
            }
            if(stage==35)
            {
                InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,scroll=new Vector2(0,DesktopWorkspace.ScrollForNotches(1))});Wait(.03f);stage=36;return;
            }
            if(stage==36)
            {
                Check(workspace.ViewTransitioning,"Wheel zoom snapped instead of interpolating.");Wait(.35f);stage=37;return;
            }
            if(stage==37)
            {
                Check(!workspace.ViewTransitioning&&Mathf.Abs(Demo.Instrument.Camera.orthographicSize-zoomBefore/1.5f)<1e-5f,"Wheel up did not finish a 1.5x zoom.");Snapshot("wheel_zoom");
                evidence["simulated_wheel_zoom_direction_smoothing_and_model_invariance"]=true;
                workspace.ResetView();Check(key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"View focus/zoom changes the scientific state.");
                evidence["all_focus_views_preserve_model_and_trace"]=true;workspace.SetFocus(2);Wait(.3f);stage=60;return;
            }
            if(stage==60) {Key(UnityEngine.InputSystem.Key.G);Wait(.03f);stage=61;return;}
            if(stage==61)
            {
                Key();Check(workspace.GridVisible,"G did not enable grid.");Snapshot("grid_window");
                var previousCenter=workspace.ViewCenter;var previousSpan=workspace.ViewSpan;
                foreach(var invalid in new[]{("NaN","1"),("0","-1"),("0","1e-300"),("10001","1")})
                {
                    bool refused=false;try {workspace.ApplyView(invalid.Item1,"0",invalid.Item2,true);}catch(ArgumentException) {refused=true;}catch(FormatException) {refused=true;}
                    Check(refused&&workspace.ViewCenter==previousCenter&&workspace.ViewSpan==previousSpan,"Invalid camera input was not atomic.");
                }
                workspace.Shell.ShowModal("View");Wait();stage=62;return;
            }
            if(stage==62)
            {
                Snapshot("exact_view_controls");var fields=workspace.Shell.GetComponentsInChildren<InputField>();
                requestedWidth=workspace.ViewSpan.x*.6f;
                fields.First(f=>f.name=="view-center-x").text="0.125";fields.First(f=>f.name=="view-center-y").text="-0.25";
                fields.First(f=>f.name=="view-width").text=requestedWidth.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                Button("apply-view-width").onClick.Invoke();Wait(.3f);stage=63;return;
            }
            if(stage==63)
            {
                Check(Vector2.Distance(workspace.ViewCenter,new Vector2(.125f,-.25f))<1e-6f&&Mathf.Abs(workspace.ViewSpan.x-requestedWidth)<1e-5f,"Exact view UI did not apply calibrated center and width.");
                Check(Mathf.Abs(workspace.ViewSpan.x/workspace.ViewSpan.y-Demo.Instrument.Camera.aspect)<1e-5f,"Exact view distorted aspect ratio.");
                Snapshot("exact_view_applied");MouseAt(Demo.Instrument.Camera.pixelRect.center);Wait(.03f);stage=64;return;
            }
            if(stage==64) {InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,buttons=4});Wait(.03f);stage=65;return;}
            if(stage==65)
            {
                Check(workspace.Panning,"Middle button did not acquire pan.");panBefore=Demo.Instrument.Camera.transform.position;
                pointer+=new Vector2(80,40);InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,delta=new Vector2(80,40),buttons=4});Wait(.03f);stage=66;return;
            }
            if(stage==66)
            {
                Check(Demo.Instrument.Camera.transform.position.x<panBefore.x&&Demo.Instrument.Camera.transform.position.y<panBefore.y&&!workspace.ViewTransitioning,"Middle pan was delayed or moved in the wrong direction.");
                workspace.Shell.ShowModal("View");Check(!workspace.Panning,"Modal did not cancel middle pan.");MouseAt(pointer);workspace.CancelModal();Wait();stage=67;return;
            }
            if(stage==67) {Snapshot("middle_pan");workspace.SetFocus(1);Wait(.3f);stage=68;return;}
            if(stage==68) {Snapshot("grid_structure");workspace.SetFocus(3);Wait(.3f);stage=69;return;}
            if(stage==69)
            {
                Snapshot("grid_diffraction");workspace.ApplyView("500","0",workspace.ViewSpan.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),true);Wait(.3f);stage=74;return;
            }
            if(stage==74)
            {
                Snapshot("diffraction_offscreen_view");Check(workspace.ViewCenter.x>499,"Offscreen view was not applied.");
                UiHit("toggle-grid");Button("toggle-grid").onClick.Invoke();Check(!workspace.GridVisible,"Grid button failed to hide grid.");
                Check(key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"Grid, exact camera view or pan changed scientific state.");
                evidence["calibrated_grid_key_button_exact_view_aspect_atomicity_middle_pan_and_modal_safety"]=true;
                navigationView=0;workspace.SetFocus(0);Wait(.3f);stage=70;return;
            }
            if(stage==70) {zoomBefore=Demo.Instrument.Camera.orthographicSize;MouseAt(Demo.Instrument.Camera.pixelRect.center);Wait(.03f);stage=71;return;}
            if(stage==71) {InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,scroll=new Vector2(0,DesktopWorkspace.ScrollForNotches(1))});Wait(.20f);stage=72;return;}
            if(stage==72)
            {
                Check(!workspace.ViewTransitioning&&Mathf.Abs(Demo.Instrument.Camera.orthographicSize-zoomBefore/1.5f)<1e-5f,"Responsive zoom failed in view "+navigationView);
                Snapshot("fast_zoom_view_"+navigationView);InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,scroll=new Vector2(0,DesktopWorkspace.ScrollForNotches(-1))});Wait(.20f);stage=73;return;
            }
            if(stage==73)
            {
                Check(!workspace.ViewTransitioning&&Mathf.Abs(Demo.Instrument.Camera.orthographicSize-zoomBefore)<1e-5f,"Zoom was not reversible in view "+navigationView);
                Check(key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"Wheel navigation changed the science.");
                navigationView++;if(navigationView<4) {workspace.SetFocus(navigationView);Wait(.3f);stage=70;return;}
                evidence["all_four_views_fast_wheel_zoom_reversible_invariant"]=true;
                workspace.ResetView();workspace.Explore();workspace.Preferences.ReducedMotion=true;workspace.ApplyPreferences(false);
                if(workspace.GridVisible) workspace.ToggleGrid();key=Demo.Lab.State.Model.Key("");journal=Demo.Lab.State.Model.Journal.Count;Wait(.3f);stage=91;return;
            }
            if(stage==91) {Snapshot("grid_all_off");workspace.ToggleGrid();Wait(.3f);stage=92;return;}
            if(stage==92)
            {
                Snapshot("grid_all_on");
                Check(workspace.Shell.Grid.Regions.All(r=>r.Visible&&r.Segments.Count>0&&r.Major==r.Minor*5),"All views did not show three independent fine grids.");
                minorBefore=workspace.Shell.Grid.Regions.Select(r=>r.Minor).ToArray();
                MouseAt(Demo.Instrument.Camera.pixelRect.center);InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,scroll=new Vector2(0,DesktopWorkspace.ScrollForNotches(2))});Wait(.3f);stage=93;return;
            }
            if(stage==93)
            {
                Snapshot("grid_all_zoomed");
                Check(workspace.Shell.Grid.Regions.Where((r,i)=>r.Visible&&r.Minor<minorBefore[i]).Count()==3,"Zooming in did not provide finer spacing in every space.");
                InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,scroll=new Vector2(0,DesktopWorkspace.ScrollForNotches(-2))});Wait(.3f);stage=94;return;
            }
            if(stage==94)
            {
                Snapshot("grid_all_zoom_restored");Check(workspace.Shell.Grid.Regions.Where((r,i)=>r.Minor==minorBefore[i]).Count()==3,"Reversed zoom did not restore each independent grid interval.");
                Check(key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"Independent grids changed the scientific model or history.");
                gridTextSize=1;workspace.Preferences.TextSize=1;workspace.ApplyPreferences(false);workspace.Shell.Inspect(0);Wait(.35f);stage=95;return;
            }
            if(stage==95)
            {
                Snapshot("grid_all_inspector_size_"+gridTextSize);
                Check(workspace.Shell.Grid.Regions.Count(r=>r.Visible)==3,"Inspector hid an independent All views grid.");
                if(gridTextSize==1) {gridTextSize=2;workspace.Preferences.TextSize=2;workspace.ApplyPreferences(false);Wait(.3f);return;}
                evidence["three_independent_all_view_grids_adaptive_minor_major_spacing_zoom_and_inspector_text_sizes"]=true;
                workspace.ToggleGrid();workspace.Preferences.TextSize=0;workspace.Preferences.ReducedMotion=false;workspace.ApplyPreferences(false);workspace.Shell.ToggleInspector();workspace.SetFocus(2);workspace.ToggleGrid();Wait(.4f);stage=97;return;
            }
            if(stage==97)
            {
                watchGridFade=true;observedGridFade=false;minorBefore=new[]{workspace.Shell.Grid.MinorInterval};
                MouseAt(Demo.Instrument.Camera.pixelRect.center);InputSystem.QueueStateEvent(mouse,new MouseState {position=pointer,scroll=new Vector2(0,DesktopWorkspace.ScrollForNotches(4))});Wait(.35f);stage=98;return;
            }
            if(stage==98)
            {
                Check(observedGridFade&&workspace.Shell.Grid.MinorInterval<minorBefore[0],"Adaptive spacing did not crossfade during a normal-motion zoom.");
                watchGridFade=false;evidence["adaptive_grid_step_crossfade_observed"]=true;Wait(.2f);stage=99;return;
            }
            if(stage==99)
            {
                Check(!workspace.Shell.Grid.Transitioning&&!workspace.ViewTransitioning,"Grid crossfade did not finish.");
                Snapshot("grid_window_adaptive_zoom");workspace.ToggleGrid();workspace.SetFocus(0);Wait(.35f);stage=96;return;
            }
            if(stage==96)
            {
                Demo.Lab.SetPhi(.181,.047);workspace.Shell.Inspect(0);Wait(.35f);stage=40;return;
            }
            if(stage==40)
            {
                Snapshot("inspector_model");Check(workspace.Shell.InspectorOpen,"Live inspector failed to open.");
                var rendered=workspace.Shell.GetComponentsInChildren<Text>().First(t=>t.name=="inspector-values");
                Check(rendered.supportRichText&&rendered.color==Color.white&&rendered.text==DesktopSemanticText.Present(DesktopResearchTools.Overview(Demo),workspace.Preferences.HighContrast),"Inspector semantic presentation differs from its plain scientific source or double-tints its colors.");
                Check(rendered.text.Contains("#E8C088")&&rendered.text.Contains("#8CC7F5"),"Inspector numbers and variables lack distinct semantic roles.");
                evidence["semantic_inspector_keeps_full_precision_plain_copy_source_and_typed_colors"]=true;
                key=Demo.Lab.State.Model.Key("");journal=Demo.Lab.State.Model.Journal.Count;
                foreach(var input in new[]{("NaN","0"),("Infinity","0"),("1","0")})
                {
                    bool refused=false;try {workspace.ApplyOffsets(input.Item1,input.Item2);}catch(ArgumentException) {refused=true;}catch(FormatException) {refused=true;}
                    Check(refused&&key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"Invalid numerical input mutated scientific state.");
                }
                workspace.Shell.ShowModal("Precision");Wait();stage=41;return;
            }
            if(stage==41)
            {
                Snapshot("precision_controls");UiHit("apply-offset");
                var fields=workspace.Shell.GetComponentsInChildren<InputField>();fields.First(f=>f.name=="offset-x").text="0.125";fields.First(f=>f.name=="offset-y").text="-0.0625";
                Button("apply-offset").onClick.Invoke();Wait();stage=42;return;
            }
            if(stage==42)
            {
                Check(Demo.Lab.State.Phi.X==.125&&Demo.Lab.State.Phi.Y==-.0625,"Precision UI did not apply exact values.");
                Snapshot("precision_applied");Demo.Lab.Select(0);workspace.Shell.Inspect(1);Wait();stage=43;return;
            }
            if(stage==43)
            {
                Snapshot("inspector_point");workspace.SetFocus(3);Wait(.30f);stage=44;return;
            }
            if(stage==44)
            {
                int zero=Array.FindIndex(Demo.Instrument.Reciprocal.Peaks,p=>p.Q.X==0&&p.Q.Y==0&&p.Q.Z==0);Check(zero>=0,"No zero-vector diffraction sample.");
                gallery=zero;MouseAt(Demo.Instrument.Camera.WorldToScreenPoint(Demo.Instrument.Reciprocal.PeakPosition(zero)));Wait(.03f);stage=45;return;
            }
            if(stage==45) {MouseAt(pointer,true);Wait(.03f);stage=46;return;}
            if(stage==46) {MouseAt(pointer);Wait();stage=47;return;}
            if(stage==47)
            {
                Check(Demo.Instrument.Reciprocal.SelectedPeak==gallery,"Mouse peak picking did not expose the selected numerical sample.");
                Snapshot("inspector_peak");workspace.NumericalExportRootOverride=Path.Combine(Output,"numerical_exports");workspace.ExportNumbers();Wait();stage=48;return;
            }
            if(stage==48)
            {
                Snapshot("numerical_exported");Check(File.Exists(Path.Combine(workspace.LastNumericalExport,"state.json")),"Numerical export lacks linked state.");
                evidence["numerical_export_folder"]=workspace.LastNumericalExport;evidence["exact_offsets_invalid_input_atomicity_live_point_and_peak_inspection"]=true;
                workspace.CancelModal();workspace.Shell.ToggleInspector();Wait(.40f);stage=49;return;
            }
            if(stage==49)
            {
                Check(!workspace.Shell.InspectorOpen,"Inspector did not dismiss.");workspace.Shell.ShowModal("About");Wait();stage=75;return;
            }
            if(stage==75)
            {
                Snapshot("about_context_notice");evidence["no_permanent_footer_notice_offline_and_diffraction_provenance_retained_in_about"]=true;
                workspace.CancelModal();Wait();stage=76;return;
            }
            if(stage==76) {workspace.Explore();workspace.SetFocus(2);Wait(.3f);stage=77;return;}
            if(stage==77)
            {
                workspace.Save();InstrumentSession.Decode(JObject.Parse(LocalSessionStore.Read(Demo.SessionFolder,Demo.LastSessionPath))["instrument"].ToString());
                heldPhi=Demo.Lab.State.Phi;key=Demo.Lab.State.Model.Key("");journal=Demo.Lab.State.Model.Journal.Count;
                // UI hover -> press at handle -> move -> release in ONE input frame.
                // Previous tests waited between each event and missed the real bug.
                MouseAt(Center(Button("save")));
                var p=(Vector2)Demo.Instrument.Camera.WorldToScreenPoint(Demo.Lab.PhasonHandle.position);
                MouseAt(p,true);MouseAt(p+new Vector2(-80,40),true);MouseAt(p+new Vector2(-80,40));Wait();stage=78;return;
            }
            if(stage==78)
            {
                Check(Demo.Lab.State.Phi.X<heldPhi.X&&Demo.Lab.State.Phi.Y>heldPhi.Y&&!workspace.Dragging,"Coalesced rapid drag did not acquire its original press position, apply motion, and release.");
                Snapshot("rapid_drag_recovered");evidence["rapid_ui_to_knob_down_move_up_same_frame"]=true;
                workspace.SetPaused(true);key=Demo.Lab.State.Model.Key("");journal=Demo.Lab.State.Model.Journal.Count;
                var p=(Vector2)Demo.Instrument.Camera.WorldToScreenPoint(Demo.Lab.PhasonHandle.position);MouseAt(p,true);MouseAt(p+new Vector2(50,40));Wait();stage=79;return;
            }
            if(stage==79)
            {
                Check(key==Demo.Lab.State.Model.Key("")&&journal==Demo.Lab.State.Model.Journal.Count,"Paused raw events mutated the model.");
                Snapshot("clear_pause_recovery");UiHit("resume-input");Button("resume-input").onClick.Invoke();workspace.SetFocus(3);Wait(.3f);stage=80;return;
            }
            if(stage==80)
            {
                workspace.Save();InstrumentSession.Decode(JObject.Parse(LocalSessionStore.Read(Demo.SessionFolder,Demo.LastSessionPath))["instrument"].ToString());
                workspace.SetFocus(2);workspace.ApplyView("2000","-2000",workspace.ViewSpan.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),true);Wait(.3f);stage=81;return;
            }
            if(stage==81)
            {
                workspace.Save();InstrumentSession.Decode(JObject.Parse(LocalSessionStore.Read(Demo.SessionFolder,Demo.LastSessionPath))["instrument"].ToString());
                Check(Math.Abs(Demo.Instrument.Camera.transform.position.x)>100,"Far-pan fixture was not outside the session layout bound.");
                evidence["focused_and_far_panned_sessions_save_with_strict_unchanged_validator"]=true;
                workspace.ResetView();workspace.SetFocus(3);Wait(.3f);gallery=0;key=Demo.Lab.State.Model.Key(Demo.Instrument.Reciprocal.Config);journal=Demo.Lab.State.Model.Journal.Count;
                savedJson=Newtonsoft.Json.JsonConvert.SerializeObject(Demo.Instrument.Reciprocal.Values);stage=82;return;
            }
            if(stage==82)
            {
                if(gallery<DiffractionColors.Names.Length)
                {
                    var settings=new DiffractionDisplay {Palette=DiffractionColors.Names[gallery]};workspace.ApplyDiffraction(settings);Wait();stage=83;return;
                }
                var filter=new DiffractionDisplay {Scale="log",Minimum=.001,Maximum=.5,VisibleMinimum=.15,HideCenter=true};workspace.ApplyDiffraction(filter);Wait();stage=84;return;
            }
            if(stage==83)
            {
                Check(key==Demo.Lab.State.Model.Key(Demo.Instrument.Reciprocal.Config)&&journal==Demo.Lab.State.Model.Journal.Count&&savedJson==Newtonsoft.Json.JsonConvert.SerializeObject(Demo.Instrument.Reciprocal.Values),"A colormap changed scientific values or history.");
                Snapshot("colormap_"+gallery.ToString("00"));gallery++;stage=82;return;
            }
            if(stage==84)
            {
                int zero=Array.FindIndex(Demo.Instrument.Reciprocal.Peaks,p=>p.Q.X==0&&p.Q.Y==0&&p.Q.Z==0);
                Check(!Demo.Instrument.Reciprocal.VisiblePeak(zero)&&Demo.Instrument.Reciprocal.VisibleCount<381,"Display filters did not hide peaks.");
                Check(Demo.Instrument.Reciprocal.Pick(Demo.Instrument.Camera.ScreenPointToRay(Demo.Instrument.Camera.WorldToScreenPoint(Demo.Instrument.Reciprocal.PeakPosition(zero))),.001f)!=zero,"Hidden q=0 remains clickable.");
                Snapshot("custom_log_range_filtered");workspace.Shell.ShowModal("Diffraction");Wait();stage=85;return;
            }
            if(stage==85) {Snapshot("diffraction_display_controls");workspace.Shell.ShowModal("Colormaps");Wait();stage=86;return;}
            if(stage==86) {Snapshot("colormap_picker");workspace.Shell.ShowModal("DiffractionRange");Wait();stage=87;return;}
            if(stage==87) {Snapshot("diffraction_range_controls");workspace.Shell.ShowModal("DiffractionFilters");Wait();stage=88;return;}
            if(stage==88) {Snapshot("diffraction_filter_controls");workspace.Shell.ShowModal("CustomColors");Wait();stage=89;return;}
            if(stage==89)
            {
                Snapshot("custom_color_controls");
                Check(key==Demo.Lab.State.Model.Key(Demo.Instrument.Reciprocal.Config)&&journal==Demo.Lab.State.Model.Journal.Count&&savedJson==Newtonsoft.Json.JsonConvert.SerializeObject(Demo.Instrument.Reciprocal.Values),"Display filters changed scientific results.");
                bool refused=false;try {workspace.ApplyDiffraction(new DiffractionDisplay {Scale="log",Minimum=0});}catch(ArgumentException) {refused=true;}
                Check(refused&&workspace.Preferences.Diffraction.Scale=="log"&&workspace.Preferences.Diffraction.Minimum==.001,"Invalid display settings were not atomic.");
                evidence["all_19_colormaps_filters_log_range_do_not_change_scientific_results"]=true;
                displaySize=1;displayPage=displayPalettePage=0;workspace.Preferences.TextSize=displaySize;workspace.ApplyPreferences(false);workspace.Shell.ShowModal(displayScreens[displayPage]);Wait();stage=90;return;
            }
            if(stage==90)
            {
                Snapshot("display_size_"+displaySize+"_page_"+displayPage+"_palette_"+displayPalettePage);
                if(displayPage==1&&displayPalettePage<3) {Button("palette-next").onClick.Invoke();displayPalettePage++;Wait();return;}
                displayPage++;
                if(displayPage<displayScreens.Length) {if(displayPage==1) Button("choose-colormap").onClick.Invoke();else workspace.Shell.ShowModal(displayScreens[displayPage]);Wait();return;}
                if(displaySize==1) {displaySize=2;displayPage=0;displayPalettePage=0;workspace.Preferences.TextSize=2;workspace.ApplyPreferences(false);workspace.Shell.ShowModal(displayScreens[0]);Wait();return;}
                evidence["display_controls_all_palette_pages_large_and_extra_large_no_clipping"]=true;
                workspace.CancelModal();workspace.Preferences.TextSize=0;workspace.ApplyPreferences(false);workspace.ApplyDiffraction(new DiffractionDisplay());Demo.StartGuide(false);Wait();stage=25;return;
            }
            if(stage==25)
            {
                Snapshot("tour_"+Demo.Step.ToString("00"));
                if(Demo.Step<9) {Demo.Advance();Wait();if(Demo.PathRunning) {workspace.SetPaused(true);journal=Demo.PathIndex;stage=26;}return;}
                evidence["all_ten_tour_steps_ready_and_rendered"]=true;workspace.Explore();Demo.Lab.SetPhi(.181,.047);Wait();stage=27;return;
            }
            if(stage==26)
            {
                Check(Demo.PathIndex==journal,"Guided path advances while paused.");workspace.SetPaused(false);Wait(3);stage=25;return;
            }
            if(stage==27)
            {
                Snapshot("windows_before");Demo.Lab.SetPhi(-.25,-.25);Wait();stage=28;return;
            }
            if(stage==28)
            {
                Snapshot("windows_after");workspace.RequestQuit();Wait();stage=30;return;
            }
            if(stage==30)
            {
                string original=workspace.SessionRootOverride,blocked=Path.Combine(Output,"blocked_session_folder");
                File.WriteAllText(blocked,"Injected storage failure fixture; not user data.");workspace.SessionRootOverride=blocked;
                workspace.ExitRequested=()=>exitSignal=true;workspace.Run(()=>workspace.ConfirmQuit(true));
                Check(!exitSignal&&workspace.Shell.Modal=="Error","Failed save-and-quit still exited or hid the failure.");
                workspace.SessionRootOverride=original;Wait();stage=31;return;
            }
            if(stage==31)
            {
                Snapshot("save_failure");workspace.CancelModal();
                string corrupted=LocalSessionStore.Save(Demo.SessionFolder,"{\"schema\":\"invalid\"}");key=Demo.Lab.State.Model.Key("");
                workspace.Run(()=> {workspace.Load(corrupted);if(workspace.Shell.Modal=="Confirm") workspace.ConfirmChange(false);});
                Check(key==Demo.Lab.State.Model.Key("")&&workspace.Shell.Modal=="Error","Corrupt session changes the model or lacks an error dialog.");
                evidence["failed_save_and_quit_keeps_app_open_and_corrupt_load_is_atomic"]=true;
                workspace.CancelModal();workspace.RequestQuit();Wait();stage=29;return;
            }
            if(stage==29)
            {
                Check(EventSystem.current.currentSelectedGameObject.name=="confirm-cancel","Quit initially focuses a consequential action.");Snapshot("quit");
                workspace.CancelModal();Check(!workspace.Shell.ModalOpen,"Quit cancellation failed.");workspace.RequestQuit();workspace.ExitRequested=()=>exitSignal=true;workspace.ConfirmQuit(false);Check(exitSignal,"Confirmed quit did not signal exit.");
                evidence["quit_confirmation_cancel_safe_initial_focus_and_exit_signal"]=true;
                evidence["passed"]=true;evidence["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();evidence["graphics_device"]=SystemInfo.graphicsDeviceName;
                evidence["platform"]=Application.platform.ToString();evidence["scope"]="Actual Unity graphics and simulated Input System mouse/keyboard; NOT human usability, OS screen-reader or commercial certification.";
                File.WriteAllText(Path.Combine(Output,"desktop_ux_evidence.json"),evidence.ToString());End(true);
            }
        }
        void End(bool passed)
        {
            Done=true;Passed=passed;if(keyboard!=null) InputSystem.RemoveDevice(keyboard);if(mouse!=null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Finished?.Invoke(passed);
        }
    }
}
