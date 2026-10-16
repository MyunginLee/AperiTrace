using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace XRSuperspace.Editor
{
    [InitializeOnLoad]
    public static class QuestExperienceSmoke
    {
        const string Key="XRSuperspace.QuestExperienceSmoke";
        static ScientificDemo demo;
        static double deadline;
        static int phase;
        static int menuExample;
        static int menuHistory;
        static string screenshotKey;
        static InputDevice menuLeft,menuRight;
        static int inputFrame;
        static float settledAt;
        static readonly JObject evidence=new();
        static string Output=>SessionState.GetString(Key+".output","");
        static QuestExperienceSmoke() {if(SessionState.GetBool(Key,false)) Attach();}
        static void Attach() {deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        public static void BatchRun()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) throw new InvalidOperationException("Real Editor graphics required");
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-quest-ux-output");
            if(index<0||index+1>=args.Length) throw new ArgumentException("-quest-ux-output required");
            var output=Path.GetFullPath(args[index+1]);if(Directory.Exists(output)&&Directory.GetFiles(output).Length>0) throw new IOException("Preserve existing evidence");
            Directory.CreateDirectory(output);SessionState.SetString(Key+".output",output);
            EditorSceneManager.OpenScene(DemoSetup.ScenePath,OpenSceneMode.Single);SessionState.SetBool(Key,true);Attach();EditorApplication.EnterPlaymode();
        }
        static void Check(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
        static void Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach(var panel in new[]{demo.QuestUI.Group,demo.Menu.Group})
            {
                if(panel.alpha<.99f) continue;
                var corners=new Vector3[4];panel.GetComponent<RectTransform>().GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    var uv=demo.Instrument.Camera.WorldToViewportPoint(corner);
                    Check(uv.z>0&&uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1,"Panel outside the simulated forward viewport: "+name+" "+uv);
                }
            }
            foreach(var label in demo.QuestUI.GetComponentsInChildren<UnityEngine.UI.Text>().Concat(demo.Menu.GetComponentsInChildren<UnityEngine.UI.Text>()).Where(t=>t.gameObject.activeInHierarchy))
            {
                var size=label.rectTransform.rect.size;
                float preferred=label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text,label.GetGenerationSettings(size))/label.pixelsPerUnit;
                Check(preferred<=size.y+1,"UI text clips: "+label.name+" "+preferred+" > "+size.y);
            }
            var camera=demo.Instrument.Camera;var rt=new RenderTexture(1920,1080,24);
            var previous=camera.targetTexture;var active=RenderTexture.active;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            }
            finally {camera.targetTexture=previous;RenderTexture.active=active;UnityEngine.Object.Destroy(image);rt.Release();UnityEngine.Object.Destroy(rt);}
        }
        static void ExerciseHandle()
        {
            var lab=demo.Lab;var control=lab.PhasonHandle.GetComponent<PhasonHandleControl>();
            Check(control!=null&&lab.PhasonHandle.GetComponent<XRGrabInteractable>()==null,"Free grab was not replaced");
            var source=new GameObject("Simulated direct controller");var sphere=source.AddComponent<SphereCollider>();sphere.isTrigger=true;sphere.radius=.05f;
            var body=source.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var direct=source.AddComponent<XRDirectInteractor>();direct.attachTransform=source.transform;
            direct.selectInput=new XRInputButtonReader {inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue,manualPerformed=true,manualValue=1};
            var manager=UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();direct.interactionManager=manager;
            try
            {
                lab.ResetPhi();string baseline=lab.State.Model.Key(demo.Instrument.Reciprocal.Config);
                for(int i=0;i<20;i++)
                {
                    source.transform.position=lab.PhasonHandle.position;var start=source.transform.position;
                    manager.SelectEnter((IXRSelectInteractor)direct,(IXRSelectInteractable)control);Check(control.Held,"Grab did not enter");
                    source.transform.position=start+lab.View.InternalOrigin.TransformDirection(new Vector3(.06f,.02f,15));
                    control.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                    Check(Math.Abs(lab.State.Phi.X)>0,"Plane input did not update phi");
                    var local=lab.View.InternalOrigin.InverseTransformDirection(lab.PhasonHandle.position-lab.View.InternalOrigin.position);
                    Check(Math.Abs(local.z)<1e-6,"Handle left its plane");
                    source.transform.position=start+lab.View.InternalOrigin.TransformDirection(new Vector3(50,-50,0));
                    control.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                    Check(lab.State.Phi.X==lab.PhiMax.X&&lab.State.Phi.Y==lab.PhiMin.Y,"Large movement escaped bounds");
                    source.transform.position=start;control.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                    Check(lab.State.Model.Key(demo.Instrument.Reciprocal.Config)==baseline,"Returning pointer changed baseline state");
                    lab.ResetPhi();Check(!control.Held&&control.interactorsSelecting.Count==0,"Reset did not cancel selection");
                    lab.PhasonHandle.position+=Vector3.one*40;lab.SyncHandlePose();
                    local=lab.View.InternalOrigin.InverseTransformDirection(lab.PhasonHandle.position-lab.View.InternalOrigin.position);
                    Check(local.magnitude<1e-6,"Reset/reprojection could not recover handle");
                }
                evidence["simulated_grab_release_reset_cycles"]=20;
                evidence["plane_depth_clamp_return_and_recovery"]=true;
            }
            finally {control.CancelGrab();UnityEngine.Object.Destroy(source);}
        }
        static void PreviewPose()
        {
            demo.Instrument.Camera.transform.SetPositionAndRotation(new Vector3(0,1.6f,-.3f),Quaternion.Euler(0,25,0));
            demo.Instrument.Camera.fieldOfView=65;demo.QuestUI.Recenter();
        }
        static void InteractionRegression()
        {
            var rays=demo.Lab.GetComponent<LabXrMode>().XrControllers.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true);
            int controllerPointers=0;
            foreach(var ray in rays)
            {
                if(ray.GetComponent<ControllerAimRay>() is not ControllerAimRay aim) continue;
                controllerPointers++;
                var line=ray.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
                Check(ray.rayOriginTransform==aim.AimOrigin&&line.lineOriginTransform==aim.AimOrigin,"Visual and hit-test controller poses differ");
                Check(!ray.blendVisualLinePoints&&!line.overrideInteractorLineOrigin&&!line.smoothMovement&&!line.snapEndpointIfAvailable&&line.lineBendRatio==1f&&line.bendingEnabledInteractionLayers.value==0,"Controller pointer retains bending/snapping/smoothing");
            }
            Check(controllerPointers==2,"Both controller pointers must be configured");
            evidence["both_controller_visuals_and_hit_tests_share_straight_unsnapped_aim_pose"]=true;
            Canvas.ForceUpdateCanvases();
            foreach(var group in new[]{demo.QuestUI.Group,demo.Menu.Group})
            {
                var canvas=group.GetComponent<Canvas>();Check(canvas.worldCamera!=null,"Event camera not assigned");
                if(group.alpha>.99f) Check(canvas.worldCamera==demo.Instrument.Camera,"Visible panel uses wrong event camera");
                foreach(var graphic in group.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                    if(graphic.raycastTarget) Check(graphic.GetComponent<UnityEngine.UI.Button>()!=null,"Non-action graphic intercepts rays: "+graphic.name);
            }
            var events=UnityEngine.EventSystems.EventSystem.current;
            var button=demo.Menu.GetComponentsInChildren<UnityEngine.UI.Button>(true).First();
            events.SetSelectedGameObject(button.gameObject);demo.Menu.Refresh();
            Check(ReferenceEquals(events.currentSelectedGameObject,null),"Menu rebuild retained selected old object");
            Check(!QuestUiTheme.HasTextInputFocus(),"Keyboard guard retained a detached object");
            var caster=demo.QuestUI.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            var panel=demo.QuestUI.GetComponent<RectTransform>();
            var backgroundPoint=panel.TransformPoint(new Vector3(-panel.rect.width*.45f,0,0));
            var data=new UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceEventData(events) {
                rayPoints=new System.Collections.Generic.List<Vector3> {backgroundPoint-panel.forward,backgroundPoint+panel.forward},layerMask=-1 };
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();caster.Raycast(data,hits);
            Check(hits.Count==0,"Coach background still blocks a tracked ray");
            var action=demo.QuestUI.GetComponentsInChildren<UnityEngine.UI.Button>().First(b=>b.interactable);
            var center=action.GetComponent<RectTransform>().TransformPoint(action.GetComponent<RectTransform>().rect.center);
            data.rayPoints=new System.Collections.Generic.List<Vector3> {center-panel.forward,center+panel.forward};hits.Clear();caster.Raycast(data,hits);
            Check(hits.Any(h=>h.gameObject==action.gameObject),"Action buttons no longer receive tracked rays");
            float angle=demo.QuestUI.LinkedViewAngle,radius=demo.QuestUI.LinkedViewRadius;
            Check(angle-Mathf.Atan2(demo.QuestUI.PhysicalEnvelope,radius)*Mathf.Rad2Deg-Mathf.Atan2(demo.QuestUI.InternalEnvelope,radius)*Mathf.Rad2Deg>=4.99f,"Linked left/center envelopes overlap");
            Check(angle-Mathf.Atan2(.63f,radius)*Mathf.Rad2Deg-Mathf.Atan2(demo.QuestUI.InternalEnvelope,radius)*Mathf.Rad2Deg>=4.99f,"Linked center/right envelopes overlap");
            var eye=demo.Instrument.Camera.transform;
            float Bearing(Vector3 point) {var delta=point-eye.position;return Mathf.Atan2(Vector3.Dot(delta,eye.right),Vector3.Dot(delta,eye.forward))*Mathf.Rad2Deg;}
            var physical=demo.Lab.State.Dataset.Candidates.Select((_,i)=>Bearing(demo.Lab.View.PointPosition(i,false))).ToArray();
            var internalBearings=new System.Collections.Generic.List<float>();
            foreach(var candidate in demo.Lab.State.Dataset.Candidates) foreach(float x in new[]{-.25f,.25f}) foreach(float y in new[]{-.25f,.25f})
                internalBearings.Add(Bearing(demo.Lab.View.InternalOrigin.TransformPoint(new Vector3((float)candidate.Internal.X+x,(float)candidate.Internal.Y+y,0)*demo.Lab.View.InternalScale)));
            var reciprocal=demo.Instrument.Reciprocal.Peaks.Select((_,i)=>Bearing(demo.Instrument.Reciprocal.PeakPosition(i))).ToArray();
            Check(physical.Max()+1<internalBearings.Min()&&internalBearings.Max()+1<reciprocal.Min(),"Actual projected candidate envelopes overlap");
            evidence["tracked_ray_passes_nonaction_background_and_hits_buttons"]=true;
            evidence["menu_rebuild_clears_live_eventsystem_selection"]=true;
            evidence["linked_view_angular_envelopes_separated"]=true;
            evidence["actual_projected_candidate_extremes_and_q_views_have_clear_gap"]=true;
        }
        static int productStage;
        static string savedKey,sessionPath,savedJournal;
        static QuestPreferences savedPreferences;
        static string preferenceSnapshot;
        static float pauseStarted;
        static int pauseIndex;
        static int pauseFrames;
        static void ProductChecks()
        {
            if(productStage==0)
            {
                savedPreferences=Newtonsoft.Json.JsonConvert.DeserializeObject<QuestPreferences>(Newtonsoft.Json.JsonConvert.SerializeObject(demo.Preferences));
                demo.SessionOutputRoot=Path.Combine(Output,"sessions");demo.Menu.Explore();demo.Menu.ShowScreen("Home");
                demo.QuestUI.ForcePreview=true;PreviewPose();productStage=1;return;
            }
            if(demo.QuestUI.Transitioning||demo.Menu.Transitioning||!demo.Instrument.Reciprocal.Current) return;
            if(productStage==1)
            {
                demo.Lab.SetPhi(.181,.047);demo.SaveSession();savedKey=demo.Lab.State.Model.Key("");
                sessionPath=demo.LastSessionPath;savedJournal=Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal);
                Check(!demo.UnsavedChanges,"Successful save still dirty");demo.Lab.SetPhi(-.119,.047);
                Check(demo.UnsavedChanges,"Model change not marked unsaved");
                int count=demo.Lab.State.Model.Journal.Count;demo.Menu.RequestRestart();
                Check(demo.Menu.ConfirmationPending&&demo.Lab.State.Model.Journal.Count==count,"Restart silently discarded trace");
                demo.Menu.ShowScreen("Home");Check(demo.Lab.State.Model.Journal.Count==count,"Cancel changed trace");
                demo.Preferences.AlternativePalette=true;demo.SetPalette(true);
                demo.Menu.RequestLoad(sessionPath);Check(demo.Menu.ConfirmationPending,"Load replaced dirty work without confirmation");
                demo.Menu.ConfirmReplacement(false);
                Check(demo.Lab.State.Model.Key("")==savedKey&&savedJournal==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal),"Saved state/journal did not restore");
                Check(!demo.UnsavedChanges,"Restored state not clean");
                Check(demo.AccessiblePalette,"Session load overrode the device's accessible palette");
                demo.Preferences.AlternativePalette=false;demo.SetPalette(false);
                evidence["session_restore_preserves_device_comfort_palette"]=true;
                string bad=LocalSessionStore.Save(demo.SessionFolder,"{\"schema\":\"invalid\"}");
                bool refused=false;try {demo.LoadSession(bad);}catch(FormatException) {refused=true;}
                Check(refused&&demo.Lab.State.Model.Key("")==savedKey,"Corrupt load changed model");
                evidence["session_save_restore_dirty_cancel_and_corrupt_load"]=true;
                var left=new GameObject("Left Controller");var right=new GameObject("Right Controller");
                try
                {
                    demo.Preferences.Controllers=1;Check(QuestProductInput.Allows(left.transform,demo)&&!QuestProductInput.Allows(right.transform,demo),"Left-only input gate failed");
                    demo.Preferences.Controllers=2;Check(!QuestProductInput.Allows(left.transform,demo)&&QuestProductInput.Allows(right.transform,demo),"Right-only input gate failed");
                }
                finally {UnityEngine.Object.Destroy(left);UnityEngine.Object.Destroy(right);demo.Preferences.Controllers=0;}
                string originalFolder=demo.SessionOutputRoot;
                string blockedFolder=Path.Combine(Output,"blocked-session-folder");File.WriteAllText(blockedFolder,"Injected non-directory storage target");
                demo.SessionOutputRoot=blockedFolder;int failedExits=0;System.Action onExit=()=>failedExits++;demo.Menu.ExitRequested+=onExit;
                demo.Menu.RequestQuit();bool failed=false;
                try {demo.Menu.ConfirmQuit(true);}catch(IOException) {failed=true;}
                finally {demo.SessionOutputRoot=originalFolder;demo.Menu.ExitRequested-=onExit;demo.Menu.ShowScreen("Home");}
                Check(failed&&failedExits==0&&demo.LastSessionPath==sessionPath,"Failed save changed last path or exited the app");
                evidence["failed_save_and_quit_does_not_exit_or_replace_last_good_file"]=true;
                evidence["simulated_single_left_or_right_input_filter"]=true;
                demo.StartGuide(false);PreviewPose();productStage=2;return;
            }
            if(productStage==2)
            {
                for(int i=0;i<6;i++) Check(demo.Advance(),"Could not start pause test");
                demo.Lifecycle.SetPaused(true);pauseIndex=demo.PathIndex;pauseStarted=Time.unscaledTime;productStage=3;return;
            }
            if(productStage==3)
            {
                Check(demo.PathIndex==pauseIndex,"Fixed example advanced while paused");
                Check(!demo.QuestUI.Group.interactable&&demo.Menu.InputBlocked,"Pause failed to block coach/model");
                if(Time.unscaledTime-pauseStarted<.8f) return;
                demo.Lifecycle.SetFocused(false);demo.Lifecycle.SetPaused(false);Check(demo.Lifecycle.Suspended,"Pause release overrode lost focus");
                demo.Lifecycle.SetFocused(true);productStage=4;return;
            }
            if(productStage==4)
            {
                if(demo.PathRunning) return;
                Check(demo.PathIndex==16,"Guide did not resume after focus recovery");
                evidence["simulated_pause_focus_independence_and_exact_path_resume"]=true;
                demo.Menu.Explore();demo.Instrument.Record();demo.Lab.SetPhi(.05,0);demo.Lab.SetPhi(.1,0);demo.Instrument.Record();
                demo.Instrument.Play();demo.Lifecycle.SetPaused(true);pauseFrames=demo.Lab.State.Model.Journal.Count;pauseStarted=Time.unscaledTime;productStage=20;return;
            }
            if(productStage==20)
            {
                Check(demo.Lab.State.Model.Journal.Count==pauseFrames,"Recorded path advanced while paused");
                if(Time.unscaledTime-pauseStarted<.8f) return;
                demo.Lifecycle.SetPaused(false);productStage=21;return;
            }
            if(productStage==21)
            {
                if(demo.Instrument.Playing) return;
                Check(demo.Lab.State.Phi.X==.1&&demo.Lab.State.Phi.Y==0,"Recorded path did not resume at its exact endpoint");
                evidence["recorded_playback_pause_and_exact_resume"]=true;demo.Menu.Open();productStage=5;return;
            }
            if(productStage>=5&&productStage<8)
            {
                int size=productStage-5;demo.Preferences.TextSize=size;demo.Preferences.HighContrast=size==2;
                demo.Menu.ApplyPreferences();productStage+=10;return;
            }
            if(productStage>=15&&productStage<18)
            {
                int size=productStage-15;
                foreach(string screen in new[]{"Home","Examples","Sessions","Comfort","Help","Files"})
                {
                    demo.Menu.ShowScreen(screen);Capture("product_"+screen.ToLower()+"_text_"+size);
                    if(screen=="Comfort") {demo.Menu.ChangePage(1);Capture("product_comfort_motion_text_"+size);}
                }
                demo.Menu.RequestQuit();Capture("product_quit_text_"+size);
                demo.Menu.ShowScreen("Home");productStage-=9;return;
            }
            if(productStage==8)
            {
                demo.Menu.Close();demo.Preferences.TextSize=2;demo.Preferences.UiSize=2;demo.StartGuide(false);PreviewPose();productStage=23;return;
            }
            if(productStage==23)
            {
                if(demo.PathRunning) return;
                Capture("product_guide_extra_"+demo.Step);
                if(demo.Step<9) {Check(demo.Advance(),"Extra-large guide advance refused");return;}
                evidence["all_ten_guide_steps_extra_large_text_and_ui_no_clipping"]=true;
                demo.Menu.Explore();demo.Preferences.TextSize=1;demo.Preferences.UiSize=0;demo.Preferences.ReducedMotion=true;
                demo.Menu.ApplyPreferences();savedKey=demo.Lab.State.Model.Key("");savedJournal=Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal);productStage=9;return;
            }
            if(productStage==9)
            {
                demo.Preferences.View=2;demo.Menu.ApplyPreferences();productStage=10;return;
            }
            if(productStage==10)
            {
                Check(savedKey==demo.Lab.State.Model.Key("")&&savedJournal==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal),"Comfort/focus changed scientific state");
                Capture("product_selection_focus");evidence["three_text_sizes_all_menu_pages_no_clipping"]=true;
                evidence["reduced_motion_focus_and_comfort_preserve_model_and_trace"]=true;
                demo.Preferences.View=1;demo.Menu.ApplyPreferences();productStage=24;return;
            }
            if(productStage==24)
            {
                Capture("product_structure_focus");demo.QuestUI.UseKnob();Check(demo.Preferences.View==2,"Inspection has no direct return to knob");productStage=25;return;
            }
            if(productStage==25)
            {
                demo.Preferences.View=3;demo.Menu.ApplyPreferences();productStage=26;return;
            }
            if(productStage==26)
            {
                Capture("product_diffraction_focus");demo.QuestUI.UseKnob();productStage=27;return;
            }
            if(productStage==27)
            {
                Check(demo.Preferences.View==2&&savedKey==demo.Lab.State.Model.Key("")&&savedJournal==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal),"Focused inspection/control changed science");
                Check(!demo.Instrument.Reciprocal.ShowContributionLabel,"Small expert annotation leaked into Quest help");
                evidence["all_focus_views_and_one_click_return_to_knob_preserve_science"]=true;
                demo.Menu.Open();productStage=31;return;
            }
            if(productStage==31)
            {
                Check(demo.Menu.IsOpen&&demo.Menu.Group.alpha==1&&!demo.Menu.Transitioning,"Research pages must be captured with the actual visible menu");
                ResearchChecks();
                demo.Menu.Close();settledAt=Time.unscaledTime+.4f;productStage=28;return;
            }
            if(productStage==28)
            {
                if(Time.unscaledTime<settledAt) return;
                var grids=demo.GetComponent<QuestViewTools>().GetComponentsInChildren<QuestPlaneGrid>();
                Check(grids.Length==3&&grids.All(g=>g.gameObject.activeInHierarchy&&g.Minor>0&&!g.raycastTarget),"All views does not show three non-interactive independent grids");
                Canvas.ForceUpdateCanvases();
                evidence["grid_mesh_diagnostics"]=new JArray(grids.Select(g=>new JObject { ["name"]=g.name,["vertices"]=g.canvasRenderer.GetMesh()?.vertexCount??0,["culled"]=g.canvasRenderer.cull,["alpha"]=g.canvasRenderer.GetAlpha(),["rect"]=g.rectTransform.rect.ToString(),["scale"]=g.transform.lossyScale.ToString(),["material"]=g.canvasRenderer.GetMaterial()?.name??"none" }));
                Capture("research_all_grids_on");demo.Preferences.GridVisible=false;settledAt=Time.unscaledTime+.2f;productStage=29;return;
            }
            if(productStage==29)
            {
                if(Time.unscaledTime<settledAt) return;Capture("research_all_grids_off");
                demo.Preferences.View=2;demo.Preferences.GridVisible=true;demo.Menu.ApplyPreferences();
                demo.GetComponent<QuestViewTools>().Zoom(1.5f);settledAt=Time.unscaledTime+.5f;productStage=30;return;
            }
            if(productStage==30)
            {
                if(Time.unscaledTime<settledAt) return;Capture("research_selection_zoom_grid");
                Check(savedKey==demo.Lab.State.Model.Key("")&&savedJournal==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal),"Grid/zoom changed model or history");
                evidence["rendered_three_independent_grids_and_focused_zoom_preserve_science"]=true;
                // Avoid changing the user's preferences through an automated graphics run.
                evidence["passed"]=true;evidence["graphics_device"]=SystemInfo.graphicsDeviceName;
                evidence["scope"]="Actual Editor graphics with simulated head/controller input; NOT a headset capture or device test";Finish(0);
            }
        }
        static void ResearchChecks()
        {
            string model=demo.Lab.State.Model.Key(""),journal=Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal);
            string values=Newtonsoft.Json.JsonConvert.SerializeObject(demo.Instrument.Reciprocal.Values);
            demo.Preferences.ReducedMotion=true;
            for(int size=0;size<3;size++)
            {
                demo.Preferences.TextSize=size;demo.Preferences.UiSize=size;demo.Preferences.HighContrast=size==2;demo.Menu.ApplyPreferences();
                foreach(string screen in new[]{"Inspector","ViewTools","Offset","Diffraction","Colormaps","ColorRange","Filters","CustomColors"})
                {
                    Check(demo.Menu.Group.alpha==1,"Research screenshot has hidden UI");
                    demo.Menu.ShowScreen(screen);Capture("research_"+screen.ToLower()+"_text_"+size);
                    var scroll=demo.Menu.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
                    Check(scroll!=null&&scroll.movementType==UnityEngine.UI.ScrollRect.MovementType.Clamped,"Menu has no bounded scrolling");
                    scroll.verticalNormalizedPosition=0;Capture("research_"+screen.ToLower()+"_bottom_"+size);
                    Check(demo.Menu.transform.Find("Always reachable navigation")!=null,"Close/back controls are not pinned");
                }
            }
            foreach(string palette in DiffractionColors.Names)
            {
                var settings=demo.Preferences.Diffraction.Copy();settings.Palette=palette;demo.Menu.ApplyDisplay(settings);
                Check(model==demo.Lab.State.Model.Key("")&&journal==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal)&&values==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Instrument.Reciprocal.Values),"Display changed scientific results");
            }
            var valid=demo.Preferences.Diffraction.Copy();var invalid=valid.Copy();invalid.Maximum=-1;
            bool refused=false;try {demo.Menu.ApplyDisplay(invalid);}catch(ArgumentException) {refused=true;}
            Check(refused&&Newtonsoft.Json.JsonConvert.SerializeObject(valid)==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Preferences.Diffraction),"Invalid display draft mutated preferences");
            demo.Menu.SetPaused(true);Check(!demo.GetComponent<QuestViewTools>().CanNavigate&&demo.Menu.InputBlocked,"Manual pause failed to block navigation");demo.Menu.SetPaused(false);
            var tools=demo.GetComponent<QuestViewTools>();demo.Preferences.View=2;tools.Zoom(2);Check(tools.Zooms[1]==2,"Focused zoom did not change presentation target");tools.ResetView();
            Check(model==demo.Lab.State.Model.Key("")&&journal==Newtonsoft.Json.JsonConvert.SerializeObject(demo.Lab.State.Model.Journal),"Zoom/reset mutated science");
            demo.Preferences.View=0;demo.Preferences.GridVisible=true;demo.Menu.ApplyPreferences();demo.Menu.ShowScreen("ViewTools");Capture("research_linked_grid_controls");
            evidence["unified_research_pages_three_text_sizes_scrolling_and_pinned_navigation"]=true;
            evidence["nineteen_colormaps_invalid_draft_atomicity_and_scientific_invariance"]=true;
            evidence["manual_pause_and_zoom_target_safety"]=true;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Quest UX Editor smoke timed out");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
                demo=UnityEngine.Object.FindFirstObjectByType<ScientificDemo>();
                if(demo==null||demo.Lab.State==null||demo.Lab.Busy||demo.QuestUI.Group==null) return;
                if(phase==0)
                {
                    if(!demo.Instrument.Reciprocal.Current) return;
                    preferenceSnapshot=QuestPreferences.Snapshot();
                    demo.SessionOutputRoot=Path.Combine(Output,"sessions");
                    demo.Lifecycle.SetFocused(true);demo.Lifecycle.SetPaused(false);
                    demo.Preferences.TextSize=demo.Preferences.UiSize=demo.Preferences.View=demo.Preferences.Controllers=demo.Preferences.PanelHeight=0;
                    demo.Preferences.ReducedMotion=demo.Preferences.HighContrast=demo.Preferences.AlternativePalette=false;
                    demo.FreeMode();demo.QuestUI.ForcePreview=true;PreviewPose();phase=1;return;
                }
                if(demo.QuestUI.Transitioning||!demo.Instrument.Reciprocal.Current) return;
                if(phase==1)
                {
                    Check(!demo.UI.gameObject.activeSelf&&!demo.Instrument.UI.gameObject.activeSelf,"Legacy overhead UI remained visible");
                    var key=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config);
                    demo.QuestUI.Recenter();Check(key==demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config),"Recenter mutated scientific state");
                    settledAt=Time.unscaledTime+1;phase=2;return;
                }
                if(phase==2)
                {
                    if(Time.unscaledTime<settledAt) return;
                    ExerciseHandle();
                    var delta=demo.QuestUI.transform.position-demo.Instrument.Camera.transform.position;
                    Check(delta.y<-.1f&&delta.y>-.7f,"Card is not below eye height");
                    Check(Vector3.Angle(delta,demo.Instrument.Camera.transform.forward)<25,"Card is outside forward field");
                    var window=demo.Lab.View.WindowLine;
                    Check(Vector3.Distance(window.GetPosition(0),window.GetPosition(window.positionCount-1))<1e-6,"Rotated window does not close");
                    Capture("free_exploration_editor_preview");evidence["world_anchor_eye_height_and_scientific_state_preservation"]=true;
                    InteractionRegression();
                    demo.StartGuide(false);PreviewPose();phase=3;return;
                }
                if(phase==3)
                {
                    Check(demo.QuestUI.Group.alpha==1,"Coach did not fade in");Capture("welcome_editor_preview");
                    for(int i=0;i<5;i++) Check(demo.Advance(),"Guide advance refused");phase=4;return;
                }
                if(phase==4)
                {
                    Capture("connections_editor_preview");Check(demo.Advance(),"Fixed example refused");phase=5;return;
                }
                if(phase==5)
                {
                    if(demo.PathRunning) return;
                    Check(demo.Step==6&&demo.PathIndex==16,"Fixed path did not complete");
                    Check(!demo.Lab.PhasonHandle.GetComponent<PhasonHandleControl>().Held,"Guide retained a grab");
                    Capture("fixed_path_editor_preview");evidence["guided_path_cannot_be_dragged"]=true;
                    demo.Advance();demo.Advance();demo.Advance();phase=6;return;
                }
                if(phase==6)
                {
                    Capture("try_it_editor_preview");evidence["coach_fades_and_all_ten_steps"]=true;
                    demo.FreeMode();demo.Lab.SetPhi(.181,.047);demo.Comparison.BeginComparison();
                    demo.Lab.SetPhi(-.10,.047);demo.Lab.SetPhi(-.119,.047);
                    Check(demo.Comparison.Baseline.Phi.X==.181,"Held comparison baseline moved");
                    Check(demo.Comparison.Added==4&&demo.Comparison.Removed==4,"Comparison changed identities disagree with fixture");
                    demo.Comparison.EndComparison();settledAt=Time.unscaledTime+1;phase=7;return;
                }
                if(phase==7)
                {
                    if(Time.unscaledTime<settledAt) return;
                    Check(demo.Comparison.Visible&&demo.Comparison.RibbonSegments>0,"Comparison ribbons are absent");
                    foreach(var badge in demo.Comparison.GetComponentsInChildren<TextMesh>())
                        Check(badge.GetComponent<Renderer>().localBounds.size.x<demo.Settings.QuestViewSpacing*.8f,"Local comparison legends overlap");
                    Capture("before_after_editor_preview");string key=demo.Lab.State.Model.Key("");int frames=demo.Lab.State.Model.Journal.Count;
                    var baseline=demo.Comparison.Baseline;demo.QuestUI.Recenter();
                    Check(ReferenceEquals(baseline,demo.Comparison.Baseline),"Recenter replaced comparison reference");
                    Check(key==demo.Lab.State.Model.Key("")&&frames==demo.Lab.State.Model.Journal.Count,"Comparison/recenter mutated model/history");
                    demo.Comparison.Enabled=false;settledAt=Time.unscaledTime+1;phase=8;return;
                }
                if(phase==8)
                {
                    if(Time.unscaledTime<settledAt) return;
                    Check(!demo.Comparison.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled&&r.gameObject.activeInHierarchy),"Hidden comparison still rendered");
                    evidence["sampled_before_after_pinned_baseline_recenter_and_toggle"]=true;
                    demo.Comparison.Enabled=true;screenshotKey=demo.Lab.State.Model.Key("");
                    InputSystem.RegisterLayout("{\"name\":\"AperiTraceSmokeMenu\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"menuButton\",\"layout\":\"Button\",\"offset\":128,\"format\":\"FLT\",\"sizeInBits\":32}]}");
                    menuLeft=InputSystem.AddDevice("AperiTraceSmokeMenu");menuRight=InputSystem.AddDevice("AperiTraceSmokeMenu");
                    InputSystem.SetDeviceUsage(menuLeft,"LeftHand");InputSystem.SetDeviceUsage(menuRight,"RightHand");
                    InputSystem.QueueDeltaStateEvent((ButtonControl)menuRight["menuButton"],1f);inputFrame=Time.frameCount;phase=20;return;
                }
                if(phase==20)
                {
                    if(Time.frameCount<=inputFrame+1) return;Check(!demo.Menu.IsOpen,"Right controller opened app menu");
                    InputSystem.QueueDeltaStateEvent((ButtonControl)menuLeft["menuButton"],1f);inputFrame=Time.frameCount;phase=21;return;
                }
                if(phase==21)
                {
                    if(Time.frameCount<=inputFrame+1) return;Check(demo.Menu.IsOpen,"Left menu press did not open app menu");
                    settledAt=Time.unscaledTime+1;phase=22;return;
                }
                if(phase==22)
                {
                    if(Time.unscaledTime<settledAt) return;Check(demo.Menu.IsOpen&&!demo.Menu.Transitioning,"Held menu button repeatedly toggled");
                    Check(screenshotKey==demo.Lab.State.Model.Key(""),"Opening menu changed scientific state");
                    InputSystem.QueueDeltaStateEvent((ButtonControl)menuLeft["menuButton"],0f);
                    evidence["simulated_left_menu_edge_and_right_button_exclusion"]=true;phase=9;return;
                }
                if(phase==9)
                {
                    if(demo.Menu.Transitioning) return;
                    Check(demo.Menu.IsOpen&&demo.Menu.InputBlocked,"Menu did not block model interaction");
                    Capture("menu_home_editor_preview");demo.Menu.ShowScreen("Examples");Capture("menu_first_page_editor_preview");demo.Menu.ChangePage(4);Capture("menu_last_page_editor_preview");
                    InputSystem.QueueDeltaStateEvent((ButtonControl)menuLeft["menuButton"],1f);inputFrame=Time.frameCount;phase=23;return;
                }
                if(phase==23)
                {
                    if(Time.frameCount<=inputFrame+1||demo.Menu.Transitioning) return;Check(!demo.Menu.IsOpen,"Second left menu press did not close menu");
                    InputSystem.QueueDeltaStateEvent((ButtonControl)menuLeft["menuButton"],0f);inputFrame=Time.frameCount;phase=24;return;
                }
                if(phase==24)
                {
                    if(Time.frameCount<=inputFrame+1) return;InputSystem.QueueDeltaStateEvent((ButtonControl)menuLeft["menuButton"],1f);inputFrame=Time.frameCount;phase=25;return;
                }
                if(phase==25)
                {
                    if(Time.frameCount<=inputFrame+1||demo.Menu.Transitioning) return;Check(demo.Menu.IsOpen,"Third left menu press did not reopen menu");
                    InputSystem.QueueDeltaStateEvent((ButtonControl)menuLeft["menuButton"],0f);
                    evidence["simulated_menu_close_and_reopen"]=true;menuExample=0;menuHistory=demo.Lab.State.Model.Journal.Count;demo.Menu.ChooseExample(menuExample);phase=10;return;
                }
                if(phase==10)
                {
                    if(demo.Menu.Transitioning) return;
                    var example=QuestExampleLibrary.All[menuExample];var expected=new InteractionModel(demo.Lab.State.Dataset);expected.Apply(example.After,example.AfterFilter);
                    Check(expected.Active.SequenceEqual(demo.Lab.State.Model.Active),"Example after subset disagrees: "+example.Id);
                    Check(demo.Lab.State.Model.Journal.Count>=menuHistory,"Example erased history");
                    Capture("example_"+(menuExample+1).ToString("00")+"_"+example.Id);
                    demo.Menu.ToggleExampleState();expected.Apply(example.Before,example.BeforeFilter);
                    Check(expected.Active.SequenceEqual(demo.Lab.State.Model.Active),"Example before subset disagrees: "+example.Id);
                    demo.Menu.ToggleExampleState();
                    if(++menuExample<QuestExampleLibrary.All.Count) {demo.Menu.ChooseExample(menuExample);return;}
                    evidence["twenty_examples_before_after_and_history_preserved"]=true;
                    demo.Menu.ChooseExample(15);demo.Menu.Open();demo.Preferences.View=3;demo.QuestUI.Recenter();phase=11;return;
                }
                if(phase==11)
                {
                    if(demo.Menu.Transitioning) return;
                    demo.Menu.Screenshot.EditorOutputRoot=Path.Combine(Output,"app_screenshots");
                    screenshotKey=demo.Lab.State.Model.Key("");demo.Menu.TakeScreenshot();phase=12;return;
                }
                if(phase==12)
                {
                    if(demo.Menu.Screenshot.Busy) return;
                    Check(demo.Menu.Screenshot.LastError==null&&File.Exists(demo.Menu.Screenshot.LastImage),"Screenshot failed: "+demo.Menu.Screenshot.LastError);
                    string path=demo.Menu.Screenshot.LastImage;var sidecar=JObject.Parse(File.ReadAllText(Path.ChangeExtension(path,".json")));
                    Check((string)sidecar["image_sha256"]==Qc5dParser.Sha256(File.ReadAllBytes(path)),"Screenshot PNG hash mismatch");
                    var pixels=new Texture2D(2,2);try
                    {
                        Check(pixels.LoadImage(File.ReadAllBytes(path)),"Screenshot PNG cannot be decoded");
                        int center=Array.FindIndex(demo.Instrument.Reciprocal.Peaks,p=>Math.Abs(p.Q.X)<1e-10&&Math.Abs(p.Q.Y)<1e-10);
                        var uv=demo.Instrument.Camera.WorldToViewportPoint(demo.Instrument.Reciprocal.PeakPosition(center));
                        Check(uv.z>0&&uv.x>0&&uv.x<1&&uv.y>0&&uv.y<1,"Screenshot test must face the actual reciprocal view");
                        var pixel=pixels.GetPixel(Mathf.RoundToInt(uv.x*(pixels.width-1)),Mathf.RoundToInt(uv.y*(pixels.height-1)));
                        Check(pixel.maxColorComponent>.2f,"Instanced finite diffraction is missing from app screenshot");
                    }
                    finally {UnityEngine.Object.Destroy(pixels);}
                    Check(screenshotKey==demo.Lab.State.Model.Key("")&&demo.Instrument.Reciprocal.Current,"Screenshot changed model or accepted stale diffraction");
                    evidence["mono_app_screenshot_png_and_state"]=Path.GetFileName(path);demo.Preferences.View=0;demo.QuestUI.Recenter();Capture("menu_screenshot_saved_editor_preview");
                    int exits=0;demo.Menu.ExitRequested+=()=>exits++;
                    bool refused=false;try {demo.Menu.ConfirmQuit(false);}catch(InvalidOperationException) {refused=true;}
                    Check(refused&&exits==0,"Quit did not require explicit confirmation");
                    demo.Menu.RequestQuit();Capture("menu_quit_confirmation_editor_preview");demo.Menu.ConfirmQuit(true);
                    Check(exits==1&&demo.Menu.ExitSignalSent&&File.Exists(demo.LastSessionPath),"Save & quit signalling failed");
                    evidence["quit_confirmation_and_save_exit_signal_editor_stub"]=true;
                    demo.QuestUI.ForcePreview=false;inputFrame=Time.frameCount;phase=13;return;
                }
                if(phase==13)
                {
                    if(Time.frameCount<=inputFrame+1) return;
                    Check(!demo.Menu.IsOpen&&!demo.Menu.GetComponent<Canvas>().enabled,"Menu leaked into desktop mode");
                    evidence["menu_hidden_outside_quest_mode"]=true;
                    phase=14;
                }
                if(phase==14) ProductChecks();
            }
            catch(Exception error) {evidence["passed"]=false;evidence["error"]=error.ToString();Debug.LogError("[XRSuperspace] Quest UX Editor smoke: "+error);Finish(1);}
        }
        static void Finish(int code)
        {
            QuestPreferences.Restore(preferenceSnapshot);
            if(menuLeft!=null) InputSystem.RemoveDevice(menuLeft);if(menuRight!=null) InputSystem.RemoveDevice(menuRight);
            if(menuLeft!=null||menuRight!=null) InputSystem.RemoveLayout("AperiTraceSmokeMenu");
            File.WriteAllText(Path.Combine(Output,"evidence.json"),evidence.ToString());SessionState.SetBool(Key,false);
            EditorApplication.update-=Tick;EditorApplication.Exit(code);
        }
    }
}
