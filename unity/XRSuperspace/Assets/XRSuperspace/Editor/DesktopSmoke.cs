using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace XRSuperspace.Editor
{
    // A real graphics/PlayMode smoke run, separate from non-graphics EditMode.
    // Captures come from the running lab camera, never from a reconstructed image.
    [InitializeOnLoad]
    public static class DesktopSmoke
    {
        const string Key="XRSuperspace.DesktopSmoke";
        static int phase,waitFrames;
        static double deadline;
        static double captureAfter;
        static RenderTexture target;
        static LabController lab;
        static Camera camera;
        static Keyboard keyboard;
        static Mouse mouse;
        static InputSettings originalInputSettings,smokeInputSettings;
        static Vector3 originalPosition;
        static Quaternion originalRotation;
        static int objectCount;
        static LatticeId selection;
        static Vector3 selectedPhysical;
        static readonly JObject evidence=new JObject();
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../results"));
        static DesktopSmoke() { if(SessionState.GetBool(Key,false)) Attach(); }
        public static void BatchRun()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!SystemInfo.supportsInstancing) throw new InvalidOperationException("graphics smoke requires a real instancing-capable graphics device");
            EditorSceneManager.OpenScene(LabSetup.ScenePath,OpenSceneMode.Single);
            SessionState.SetBool(Key,true);Attach();EditorApplication.EnterPlaymode();
        }
        static void Attach() { deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update-=Tick;EditorApplication.update+=Tick; }
        static void Check(bool result,string message) { if(!result) throw new InvalidOperationException(message); }
        static void Capture(string name)
        {
            var prior=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try {texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,name),texture.EncodeToPNG());}
            finally {RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(texture);}
        }
        static Button Button(string name)=>lab.GetComponentsInChildren<Button>().Single(x=>x.name==name);
        static void MouseInput(MouseState state) { InputSystem.QueueStateEvent(mouse,state);InputSystem.Update();lab.GetComponent<DesktopInput>().SendMessage("Update"); }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("desktop smoke exceeded 120 seconds");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
                if(waitFrames-->0) return;
                if(phase==0)
                {
                    lab=UnityEngine.Object.FindFirstObjectByType<LabController>();if(lab==null||lab.Busy||lab.State==null) return;
                    Check(lab.State.Dataset.Candidates.Count==31,"golden StreamingAssets load failed");
                    camera=lab.GetComponent<LabXrMode>().DesktopCamera;Check(camera.enabled,"desktop fallback camera must be active without HMD");
                    target=new RenderTexture(1600,1000,24);target.Create();camera.targetTexture=target;
                    originalPosition=camera.transform.position;originalRotation=camera.transform.rotation;
                    objectCount=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
                    Check(lab.GetComponent<LabXrMode>().XrControllers.activeSelf==false,"XR endpoints must be dormant in desktop fallback");
                    // Batch mode has no focused GameView. Use a transient settings
                    // copy to allow synthetic events; never save these overrides.
                    originalInputSettings=InputSystem.settings;smokeInputSettings=UnityEngine.Object.Instantiate(originalInputSettings);
                    smokeInputSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                    smokeInputSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings=smokeInputSettings;
                    keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
                    evidence["graphics_device"]=SystemInfo.graphicsDeviceName;evidence["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();evidence["golden_candidates"]=31;
                    phase=1;waitFrames=20;captureAfter=EditorApplication.timeSinceStartup+.4;return;
                }
                if(phase==1)
                {
                    if(EditorApplication.timeSinceStartup<captureAfter) return;
                    Capture("desktop_golden.png");
                    var point=lab.View.PointPosition(0,true);int picked=lab.View.Pick(new Ray(point-Vector3.forward*2,Vector3.forward),true,1e-4f);Check(picked==0,"internal stable-ID ray selection failed");lab.Select(picked);selection=lab.State.Selection.Value;
                    selectedPhysical=lab.View.PointPosition(0,false);
                    MouseInput(new MouseState {position=camera.WorldToScreenPoint(lab.PhasonHandle.position)}.WithButton(MouseButton.Left));
                    phase=10;waitFrames=2;return;
                }
                if(phase==10)
                {
                    Vector3 movedHandle=lab.PhasonHandle.position+new Vector3(.181f,.047f,0)*lab.View.InternalScale;
                    MouseInput(new MouseState {position=camera.WorldToScreenPoint(movedHandle)}.WithButton(MouseButton.Left));MouseInput(new MouseState());
                    Check(Math.Abs(lab.State.Phi.X-.181)<.02&&Math.Abs(lab.State.Phi.Y-.047)<.02,"two-dimensional desktop handle drag failed: "+lab.State.Phi.X+","+lab.State.Phi.Y);
                    // Exact reference value follows the synthetic pixel-space drag.
                    lab.SetPhi(.181,.047);Check(lab.State.SelectedCount==15&&lab.State.BoundaryCount==0,"golden shifted membership mismatch");Check(lab.State.Selection.Value.Equals(selection),"phason changed selected identity");Check(lab.View.PointPosition(0,false)==selectedPhysical,"phason moved scientific physical point");
                    var desktop=lab.GetComponent<DesktopInput>();
                    MouseInput(new MouseState {delta=new Vector2(30,10)}.WithButton(MouseButton.Right));Check(camera.transform.rotation!=originalRotation,"desktop orbit failed");
                    var pivot=desktop.Pivot;MouseInput(new MouseState {delta=new Vector2(10,10)}.WithButton(MouseButton.Middle));Check(desktop.Pivot!=pivot,"desktop pan failed");
                    var position=camera.transform.position;MouseInput(new MouseState {scroll=new Vector2(0,120)});Check(camera.transform.position!=position,"desktop zoom failed");
                    MouseInput(new MouseState());camera.transform.SetPositionAndRotation(originalPosition,originalRotation);
                    evidence["orbit_pan_zoom_synthetic_input"]=true;evidence["two_axis_handle_synthetic_input"]=true;evidence["stable_ray_selection"]=selection.ToString();evidence["shifted_selected"]=15;
                    phase=2;waitFrames=12;captureAfter=EditorApplication.timeSinceStartup+.4;return;
                }
                if(phase==2)
                {
                    if(EditorApplication.timeSinceStartup<captureAfter) return;
                    Check(lab.Diagnostics.text.Contains("selected 15")&&lab.Diagnostics.text.Contains(selection.ToString()),"shifted diagnostics failed to refresh");
                    Capture("desktop_shifted.png");bool edges=lab.View.ShowEdges;Button("Edges").onClick.Invoke();Check(lab.View.ShowEdges!=edges,"serialized edge button failed");Button("Edges").onClick.Invoke();
                    Button("Reset phi").onClick.Invoke();Check(lab.State.SelectedCount==11,"serialized reset button failed");
                    Button("Reload").onClick.Invoke();phase=3;waitFrames=12;return;
                }
                if(phase==3)
                {
                    if(lab.Busy) return;Check(lab.State.Dataset.Candidates.Count==31&&lab.Status.StartsWith("PASS"),"local reload failed");Button("Dataset").onClick.Invoke();phase=4;waitFrames=15;return;
                }
                if(phase==4)
                {
                    if(lab.Busy) return;Check(lab.State.Dataset.Candidates.Count==981&&lab.State.SelectedCount==451,"small dataset switch failed");Check(lab.View.BatchCount==4,"small dataset instancing batch count incorrect");Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objectCount,"per-point GameObjects were created");
                    evidence["small_candidates"]=981;evidence["small_selected"]=451;evidence["instance_batches_two_views"]=4;evidence["gameobjects_before_after_equal"]=true;evidence["persistent_buttons_reload_reset_edges_dataset"]=true;
                    phase=5;waitFrames=20;captureAfter=EditorApplication.timeSinceStartup+.4;return;
                }
                if(phase==5)
                {
                    if(EditorApplication.timeSinceStartup<captureAfter) return;
                    Check(lab.Diagnostics.text.Contains("Candidates 981")&&lab.Diagnostics.text.Contains("selected 451"),"small diagnostics failed to refresh");
                    Capture("desktop_small.png");evidence["passed"]=true;evidence["device_validation"]="not performed";evidence["diagnostics"]=lab.DiagnosticText();Finish(0);return;
                }
            }
            catch(Exception error) {evidence["passed"]=false;evidence["error"]=error.ToString();Debug.LogError("[XRSuperspace] desktop smoke: "+error.Message);Finish(1);}
        }
        static void Finish(int exit)
        {
            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"desktop_smoke.json"),evidence.ToString());
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;if(camera!=null) camera.targetTexture=null;
            if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(keyboard!=null) InputSystem.RemoveDevice(keyboard);if(mouse!=null) InputSystem.RemoveDevice(mouse);
            if(originalInputSettings!=null) InputSystem.settings=originalInputSettings;
            if(smokeInputSettings!=null) UnityEngine.Object.DestroyImmediate(smokeInputSettings);
            Debug.Log("[XRSuperspace] graphics/PlayMode smoke exit="+exit+"; device testing remains pending");EditorApplication.Exit(exit);
        }
    }
}
