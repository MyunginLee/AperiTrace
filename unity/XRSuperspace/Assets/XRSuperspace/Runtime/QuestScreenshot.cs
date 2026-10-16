using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace XRSuperspace
{
    // Monoscopic app render from the captured head pose. Not OS capture, passthrough or stereo.
    public sealed class QuestScreenshot : MonoBehaviour
    {
        public QuestAppMenu Menu;
        public bool Busy {get;private set;}
        public string LastImage {get;private set;}
        public string LastError {get;private set;}
#if UNITY_EDITOR
        public string EditorOutputRoot;
#endif
        public void Begin()
        {
            if(Busy||!Menu.CanSave) throw new InvalidOperationException("Screenshot requires settled, non-playing application state.");
            Busy=true;LastError=null;StartCoroutine(Guarded(Run()));
        }
        IEnumerator Guarded(IEnumerator work)
        {
            try
            {
                while(true)
                {
                    bool more=false;Exception failure=null;try {more=work.MoveNext();}catch(Exception error) {failure=error;}
                    if(failure!=null) {LastError=failure.Message;Menu.SetFeedback("Screenshot failed: "+failure.Message);yield break;}
                    if(!more) yield break;yield return work.Current;
                }
            }
            finally {(work as IDisposable)?.Dispose();Busy=false;Menu.Refresh();}
        }
        IEnumerator Run()
        {
            var demo=Menu.Demo;Menu.SetFeedback("Preparing app screenshot. Wait for diffraction Ready...");
            float deadline=Time.realtimeSinceStartup+30;
            while(!demo.Instrument.Reciprocal.Current&&Time.realtimeSinceStartup<deadline) yield return null;
            if(demo.Lifecycle.Suspended) throw new InvalidOperationException("Screenshot interrupted by application pause.");
            if(!demo.Instrument.Reciprocal.Current) throw new TimeoutException("Current diffraction unavailable; no image emitted.");
            var viewer=demo.QuestUI.Viewer;if(viewer==null) throw new InvalidOperationException("Viewer unavailable.");
            int width=demo.Settings.QuestScreenshotWidth,height=demo.Settings.QuestScreenshotHeight;
            if(width<256||height<256||width>4096||height>4096||(long)width*height>9000000) throw new InvalidOperationException("Screenshot size outside budget.");
            var go=new GameObject("Temporary mono screenshot camera");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.CopyFrom(viewer);
            camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.transform.SetPositionAndRotation(viewer.transform.position,viewer.transform.rotation);
            camera.aspect=(float)width/height;
            var urp=go.AddComponent<UniversalAdditionalCameraData>();urp.allowXRRendering=false;urp.renderType=CameraRenderType.Base;
            var target=new RenderTexture(width,height,24);Texture2D pixels=null;var previous=RenderTexture.active;
            float menuAlpha=Menu.Group.alpha,coachAlpha=demo.QuestUI.Group.alpha;
            try
            {
                Menu.Group.alpha=demo.QuestUI.Group.alpha=0;yield return null;yield return null;
                string key=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config);int revision=demo.Lab.State.Model.Revision;
                var state=demo.Capture.State(width,height);
                state["app_screenshot"]=new JObject { ["example"]=Menu.HasExample?QuestExampleLibrary.All[Menu.ExampleIndex].Id:"none",["after"]=Menu.ShowingAfter,
                    ["camera_position"]=JArray.FromObject(new[]{camera.transform.position.x,camera.transform.position.y,camera.transform.position.z}),
                    ["camera_rotation"]=JArray.FromObject(new[]{camera.transform.rotation.x,camera.transform.rotation.y,camera.transform.rotation.z,camera.transform.rotation.w}),
                    ["fov"]=camera.fieldOfView,["aspect"]=camera.aspect,["stereo"]=false,["menu_and_coach_hidden"]=true,
                    ["trajectory_visible"]=demo.Lab.GetComponent<InstrumentOverlay>()?.ShowTrajectory??false };
                state["app_screenshot"]["diffraction_display"]=JObject.FromObject(demo.Preferences.Diffraction);
                state["app_screenshot"]["grid_visible"]=demo.Preferences.GridVisible;
                state["app_screenshot"]["view_zoom"]=JArray.FromObject(demo.GetComponent<QuestViewTools>().ActualZooms);
                var baseline=demo.Comparison.Baseline;
                if(baseline!=null&&baseline.Source==demo.Lab.State) state["comparison_reference"]=new JObject { ["phi"]=new JArray(baseline.Phi.X,baseline.Phi.Y),["active_ids"]=new JArray(demo.Lab.State.Dataset.Candidates.Where((_,i)=>baseline.WasActive(i)).Select(p=>p.Id.ToString())),["visible"]=demo.Comparison.Visible&&demo.Comparison.Enabled };
                target.Create();camera.targetTexture=target;
                if(demo.Lab.View.ExternalPointDrawing) throw new InvalidOperationException("App screenshot requires the Quest CPU-instance presentation path.");
                demo.Lab.View.DrawForCamera(camera);demo.Instrument.Reciprocal.DrawForCamera(camera);
                camera.Render();RenderTexture.active=target;
                pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                if(demo.Lifecycle.Suspended||revision!=demo.Lab.State.Model.Revision||key!=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config)||!demo.Instrument.Reciprocal.Current) throw new InvalidOperationException("Application paused or scientific state changed; image refused.");
                byte[] png=pixels.EncodeToPNG();string folder=Path.Combine(Application.persistentDataPath,"AperiTraceScreenshots");
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(EditorOutputRoot)) folder=EditorOutputRoot;
#endif
                Directory.CreateDirectory(folder);string name="app-"+Guid.NewGuid().ToString("N"),path=Path.Combine(folder,name+".png");
                var sidecar=new JObject { ["schema"]="aperitrace-app-screenshot/1",["captured_utc"]=DateTimeOffset.UtcNow.ToString("O"),["image"]=name+".png",["image_sha256"]=Qc5dParser.Sha256(png),["state"]=state,
                    ["capture_method"]="Unity URP monoscopic app render from captured viewer pose; not a headset stereo, OS or passthrough screenshot",["device_validation"]="NOT RUN by automated Editor checks" };
                using(var stream=new FileStream(path,FileMode.CreateNew)) stream.Write(png,0,png.Length);
                using(var stream=new FileStream(Path.Combine(folder,name+".json"),FileMode.CreateNew)) using(var writer=new StreamWriter(stream,new UTF8Encoding(false))) writer.Write(sidecar.ToString());
                LastImage=path;Menu.SetFeedback("Saved app PNG + state to AperiTraceScreenshots.\nApp-local files; not Quest Gallery.");
            }
            finally
            {
                Menu.Group.alpha=menuAlpha;demo.QuestUI.Group.alpha=coachAlpha;RenderTexture.active=previous;camera.targetTexture=null;
                if(pixels!=null) Destroy(pixels);target.Release();Destroy(target);Destroy(go);
            }
        }
    }
}
