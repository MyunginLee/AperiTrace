using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Opt-in native regression: actual backbuffer AFTER normal cameras and UI.
    public static class DesktopPresentedFrameVerification
    {
        static IEnumerator Settle(DesktopWorkspace workspace)
        {
            float until=Time.unscaledTime+.25f;
            for(int i=0;i<24||Time.unscaledTime<until;i++) {workspace.Demo.Lifecycle.SetFocused(true);yield return null;}
            yield return new WaitForEndOfFrame();
        }
        static Color32[] Capture(string folder,string name)
        {
            var previous=RenderTexture.active;
            var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=null;image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());return image.GetPixels32();
            }
            finally {RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
        }
        static int Changes(Color32[] baseline,Color32[] actual,Rect diagram,int width,int height,out int checkedPixels,out int rawChanged,out int maximumDelta)
        {
            int changed=0;checkedPixels=rawChanged=maximumDelta=0;
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                if(diagram.Contains(new Vector2((x+.5f)/width,(y+.5f)/height))) continue;
                int index=y*width+x;checkedPixels++;var a=baseline[index];var b=actual[index];
                int delta=Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
                if(delta>0) rawChanged++;maximumDelta=Mathf.Max(maximumDelta,delta);
                // A normalized ScrollRect can shift a fractional clip edge by
                // floating-point epsilon: allow ONE 8-bit quantization level,
                // not visible ghosts. Record raw differences and their maximum.
                if(delta>1) changed++;
            }
            return changed;
        }
        static void ScrollTo(DesktopWorkspace workspace,string name,float position)
        {
            foreach(var scroll in workspace.Shell.GetComponentsInChildren<ScrollRect>())
                if(scroll.name==name) {scroll.StopMovement();scroll.verticalNormalizedPosition=position;scroll.StopMovement();}
        }
        public static IEnumerator Run(DesktopWorkspace workspace,string output)
        {
            if(Application.isBatchMode) throw new InvalidOperationException("Presented-frame checks require a non-batch Windows player.");
            string folder=Path.Combine(output,"presented");Directory.CreateDirectory(folder);
            workspace.SuppressPreferenceWrites=true;workspace.IgnoreOsFocusForVerification=true;workspace.SetPaused(false);workspace.Preferences.TextSize=0;workspace.Preferences.HighContrast=false;
            workspace.Preferences.ReducedMotion=true;workspace.Preferences.AlternativePalette=false;workspace.ApplyPreferences(false);
            workspace.Explore();workspace.ResetView();workspace.Demo.Lab.SetPhi(.181,.047);
            while(workspace.Demo.Lab.Busy||!workspace.Demo.Instrument.Reciprocal.Current) yield return null;
            if(EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(null);
            yield return Settle(workspace);
            // Normalize the baseline using the SAME scroll setter as the return
            // path. Layout's initial fractional offset is not pixel-identical
            // to a normalized-position setter on all Canvas scale factors.
            ScrollTo(workspace,"navigation-scroll",0);yield return Settle(workspace);
            ScrollTo(workspace,"navigation-scroll",1);yield return Settle(workspace);
            int width=Screen.width,height=Screen.height;Rect diagram=workspace.Shell.DiagramRect;
            var baseline=Capture(folder,"workspace_baseline");var rows=new JArray();
            string baselineFeedback=workspace.Feedback;
            // Deliberately reproduce the original partial-clear defect only in
            // this explicit fixture, then restore the production clear camera.
            int reproduced;
            try
            {
                workspace.FrameClearCamera.enabled=false;workspace.Shell.ShowModal("Examples");yield return Settle(workspace);
                workspace.CancelModal();yield return Settle(workspace);
                reproduced=Changes(baseline,Capture(folder,"counterfactual_no_full_clear"),diagram,width,height,out _,out _,out _);
            }
            finally {workspace.FrameClearCamera.enabled=true;workspace.CancelModal();}
            if(reproduced==0) throw new InvalidOperationException("Frame-history fixture did not detect the original missing-clear defect.");
            yield return Settle(workspace);
            string[] screens={"Menu","Examples","Settings","Help","Sessions","About","Quit","Welcome","Examples","Menu","Settings","Help"};
            string scientificKey=workspace.Demo.Lab.State.Model.Key("");
            for(int cycle=0;cycle<screens.Length;cycle++)
            {
                workspace.Shell.ShowModal(screens[cycle]);yield return Settle(workspace);
                Capture(folder,"open_"+cycle.ToString("00")+"_"+screens[cycle].ToLowerInvariant());
                ScrollTo(workspace,"modal-scroll",0);
                yield return Settle(workspace);Capture(folder,"scrolled_"+cycle.ToString("00"));
                if(screens[cycle]=="Settings")
                {
                    workspace.Preferences.TextSize=2;workspace.Preferences.HighContrast=true;workspace.ApplyPreferences(false);
                    yield return Settle(workspace);Capture(folder,"large_"+cycle.ToString("00"));
                    workspace.Preferences.TextSize=0;workspace.Preferences.HighContrast=false;workspace.ApplyPreferences(false);
                    yield return Settle(workspace);
                }
                workspace.CancelModal();yield return Settle(workspace);
                workspace.Shell.Inspect(cycle%3);yield return Settle(workspace);
                ScrollTo(workspace,"inspector-scroll",0);yield return Settle(workspace);Capture(folder,"inspector_"+cycle.ToString("00"));
                workspace.Shell.ToggleInspector();yield return Settle(workspace);
                workspace.Notify(baselineFeedback);workspace.Shell.RefreshLive();yield return Settle(workspace);
                var actual=Capture(folder,"closed_"+cycle.ToString("00"));
                if(width!=Screen.width||height!=Screen.height||actual.Length!=baseline.Length) throw new InvalidOperationException("Presented-frame resolution changed unexpectedly.");
                int changed=Changes(baseline,actual,diagram,width,height,out int checkedPixels,out int rawChanged,out int maximumDelta);
                int modalRoots=0,chromeRoots=0;
                foreach(Transform node in workspace.Shell.transform)
                {if(node.gameObject.activeInHierarchy&&node.name=="modal-dimmer") modalRoots++;if(node.gameObject.activeInHierarchy&&node.name=="desktop-chrome") chromeRoots++;}
                rows.Add(new JObject { ["cycle"]=cycle,["modal"]=screens[cycle],["outside_diagram_pixels_checked"]=checkedPixels,["changed_pixels"]=changed,["raw_changed_pixels"]=rawChanged,["maximum_channel_delta"]=maximumDelta,["active_modal_roots"]=modalRoots,["active_chrome_roots"]=chromeRoots });
                File.WriteAllText(Path.Combine(folder,"cycles.json"),rows.ToString());
                if(changed!=0||modalRoots!=0||chromeRoots!=1) throw new InvalidOperationException("Presented-frame residue after "+screens[cycle]+": "+changed+" changed chrome/margin pixels.");
                if(scientificKey!=workspace.Demo.Lab.State.Model.Key("")) throw new InvalidOperationException("Dialog regression altered the scientific model.");
            }
            workspace.SetFocus(2);workspace.ToggleGrid();yield return Settle(workspace);
            Capture(folder,"grid_window_presented");workspace.CaptureScreenshot(folder,"grid_window_exported");
            workspace.ToggleGrid();workspace.ResetView();yield return Settle(workspace);
            if(scientificKey!=workspace.Demo.Lab.State.Model.Key("")) throw new InvalidOperationException("Presented calibrated grid altered scientific state.");
            workspace.Preferences.ReducedMotion=false;
            string motionKey=workspace.Demo.Lab.State.Model.Key("");int motionJournal=workspace.Demo.Lab.State.Model.Journal.Count;
            float initialX=workspace.Demo.Instrument.Camera.transform.position.x;workspace.SetFocus(1);
            float middleAt=Time.unscaledTime+.05f;while(Time.unscaledTime<middleAt) yield return null;
            bool tweenObserved=workspace.ViewTransitioning&&workspace.Demo.Instrument.Camera.transform.position.x!=initialX;
            yield return new WaitForEndOfFrame();Capture(folder,"motion_focus_mid");yield return Settle(workspace);
            workspace.Shell.ShowModal("Help");yield return null;workspace.CancelModal();
            bool inputBlockedDuringDismiss=workspace.BlocksInput;
            workspace.Shell.ShowModal("Settings");yield return Settle(workspace);workspace.CancelModal();yield return Settle(workspace);
            workspace.ResetView();yield return Settle(workspace);
            if(!tweenObserved||!inputBlockedDuringDismiss||motionKey!=workspace.Demo.Lab.State.Model.Key("")||motionJournal!=workspace.Demo.Lab.State.Model.Journal.Count)
                throw new InvalidOperationException("Motion did not interpolate presentation, safely interrupt, or preserve scientific state.");
            File.WriteAllText(Path.Combine(folder,"evidence.json"),new JObject { ["passed"]=true,["width"]=width,["height"]=height,["cycles"]=rows,
                ["smooth_camera_motion_observed"]=tweenObserved,["dismissal_blocks_input"]=inputBlockedDuringDismiss,["motion_and_interruption_preserve_model_and_journal"]=true,
                ["counterfactual_missing_clear_changed_pixels"]=reproduced,
                ["ui_channel_quantization_tolerance"]=1,
                ["capture_method"]="Texture2D.ReadPixels from active=null after WaitForEndOfFrame; ordinary player backbuffer, no manual camera rendering or compositor",
                ["scope"]="Same-host comparison outside the diagram with one 8-bit level for fractional scroll-edge quantization. Raw differences recorded. Controlled reduced motion and fixed model; not human usability or screen-reader certification." }.ToString());
        }
    }
}
