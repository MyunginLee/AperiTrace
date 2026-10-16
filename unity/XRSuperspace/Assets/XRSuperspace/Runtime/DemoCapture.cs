using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace
{
    public sealed class DemoCapture : MonoBehaviour
    {
        public ScientificDemo Demo;
        public bool Busy {get;private set;}
        public string LastImage {get;private set;}
        public string LastError {get;private set;}
        public static string SafeName(string value)
        {
            if(value==null||!Regex.IsMatch(value,"^[a-z][a-z0-9_]{0,63}$")||Regex.IsMatch(value,"^(con|prn|aux|nul|com[0-9]|lpt[0-9])$")) throw new ArgumentException("unsafe capture name");return value;
        }
        public static string Hash(string value)=>Qc5dParser.Sha256(Encoding.UTF8.GetBytes(value));
        static JArray Vector(Vector3 v)=>new JArray(v.x,v.y,v.z);
        public JObject State(int width,int height)
        {
            var lab=Demo.Lab;var model=lab.State.Model;var data=model.Dataset;var rec=Demo.Instrument.Reciprocal;
            var session=JObject.Parse(Demo.SessionJson());((JObject)session["instrument"]).Remove("TimestampUtc");
            var ids=data.Candidates.Select(p=>p.Id.ToString()).ToArray();var selected=ids.Where((_,i)=>model.Active[i]);var boundary=ids.Where((_,i)=>model.States[i]==PointState.Boundary);
            var focus=lab.State.Selection;var coordinates=focus.HasValue?new JArray(Enumerable.Range(0,5).Select(j=>data.Project(focus.Value,j))):null;
            return new JObject {
                ["schema"]="xr-demo-capture-state/1",["session"]=session,["dataset_label"]=DatasetLabels.Canonical,["runtime_schema"]=RuntimeMetadata.SchemaVersion,
                ["resolution"]=new JArray(width,height),["dataset_sha256"]=data.Metadata.BinarySha256,["authority_sha256"]=data.Metadata.AuthoritativeSha256,
                ["model_key"]=model.Key(rec.Config),["selected_ids"]=new JArray(selected),["boundary_ids"]=new JArray(boundary),
                ["edges"]=new JArray(model.Edges.Select(e=>new JArray(ids[e.Source],ids[e.Target],e.Generator,e.Sign))),
                ["journal"]=JArray.FromObject(model.Journal),["focus_exact_coordinates"]=coordinates,
                ["diffraction"]=new JObject { ["current"]=rec.Current,["status"]=rec.Status,["result_key"]=rec.ResultKey,["config"]=rec.Config,
                    ["q"]=new JArray(rec.Peaks.Select(p=>new JArray(p.Q.X,p.Q.Y,p.Q.Z))),["h_contributors"]=new JArray(rec.Peaks.Select(p=>new JArray(p.H.Select(h=>h.ToString())))),["values"]=JArray.FromObject(rec.Values) },
                ["precision"]=Demo.Preview.Mode.ToString(),["backend"]=Demo.Preview.Backend,["authority_epsilon"]=data.Metadata.AuthorityEpsilon,["preview_epsilon"]=data.Metadata.PreviewEpsilon,
                ["presentation"]=new JObject { ["background"]=JArray.FromObject(new[]{Demo.Settings.Background.r,Demo.Settings.Background.g,Demo.Settings.Background.b,Demo.Settings.Background.a}),["selected_color"]=JArray.FromObject(new[]{lab.Settings.SelectedColor.r,lab.Settings.SelectedColor.g,lab.Settings.SelectedColor.b,lab.Settings.SelectedColor.a}),
                    ["boundary_color"]=JArray.FromObject(new[]{lab.Settings.BoundaryColor.r,lab.Settings.BoundaryColor.g,lab.Settings.BoundaryColor.b,lab.Settings.BoundaryColor.a}),["rejected_color"]=JArray.FromObject(new[]{lab.Settings.RejectedColor.r,lab.Settings.RejectedColor.g,lab.Settings.RejectedColor.b,lab.Settings.RejectedColor.a}),
                    ["physical_scale"]=lab.View.PhysicalScale,["internal_scale"]=lab.View.InternalScale,["demo_panel_origin"]=Vector(Demo.UI.transform.position),["controls_visible"]=Demo.UI.Controls.activeSelf },
                ["story_text"]=Demo.StoryText(),["provenance_text"]=Demo.ProvenanceText()
            };
        }
        public void CapturePreset(DemoCapturePreset preset)
        {
            if(Busy) throw new InvalidOperationException("capture already in progress");Demo.ApplyPreset(preset);
            string root=Path.Combine(Application.persistentDataPath,"SuperspaceDemoCaptures",Guid.NewGuid().ToString("N"));
            Begin(root,preset.Id,preset.Width,preset.Height);
        }
        public void Begin(string folder,string name,int width,int height)
        {
            SafeName(name);if(Busy||Demo.PathRunning||Demo.Instrument.Playing) throw new InvalidOperationException("finish/stop playback and capture first");
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) throw new InvalidOperationException("real graphics device required; no synthetic image fallback");
            if(width<256||height<256||width>4096||height>4096||(long)width*height>9000000) throw new ArgumentException("capture dimensions outside budget");
            folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);
            foreach(string ext in new[]{".png",".json"}) if(File.Exists(Path.Combine(folder,name+ext))) throw new IOException("capture exists; never overwrite");
            Busy=true;LastError=null;LastImage=null;StartCoroutine(Guarded(Run(folder,name,width,height)));
        }
        IEnumerator Guarded(IEnumerator work)
        {
            try
            {
                while(true)
                {
                    bool more=false;Exception error=null;try {more=work.MoveNext();}catch(Exception e) {error=e;}
                    if(error!=null) {Fail(error);yield break;}if(!more) yield break;yield return work.Current;
                }
            }
            finally {(work as IDisposable)?.Dispose();Busy=false;}
        }
        IEnumerator Run(string folder,string name,int width,int height)
        {
            var camera=Demo.Instrument.Camera;var previous=camera.targetTexture;var active=RenderTexture.active;RenderTexture target=null;Texture2D pixels=null;
            bool controls=Demo.UI.Controls.activeSelf,automatic=Demo.AutoAdvance,freeUI=Demo.Instrument.UI.gameObject.activeSelf;
            try
            {
                Demo.AutoAdvance=false;Demo.UI.Controls.SetActive(false);Demo.Instrument.UI.gameObject.SetActive(false);float pointSize=Demo.Lab.View.PointSize;Demo.Preview.SetFallback(true);Demo.Lab.View.PointSize=pointSize;Demo.Lab.View.Rebuild(Demo.Lab.State);Demo.ValidateCpu();
                target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
                float deadline=Time.realtimeSinceStartup+30;
                while(!Demo.Instrument.Reciprocal.Current&&Time.realtimeSinceStartup<deadline) yield return null;
                if(!Demo.Instrument.Reciprocal.Current) {Fail(new TimeoutException("CURRENT diffraction unavailable; no capture emitted"));yield break;}
                var observation=JObject.FromObject(Demo.Lab.GetComponent<PreviewProfiler>().Snapshot());
                Demo.FrozenPerformance=(int)observation["frames"]>0?observation:null;Demo.UI.Refresh();
                string state=State(width,height).ToString(Formatting.None);yield return null;yield return null;yield return null;
                if(state!=State(width,height).ToString(Formatting.None)) {Fail(new InvalidOperationException("state changed while rendering; image refused"));yield break;}
                // Windows captures explicitly render, rather than assuming the
                // player has scheduled a camera pass before CPU pixel readback.
                if(Application.platform==RuntimePlatform.WindowsPlayer)
                {
                    Demo.Lab.View.DrawForCamera(camera);Demo.Instrument.Reciprocal.DrawForCamera(camera);camera.Render();
                }
                pixels=new Texture2D(width,height,TextureFormat.RGB24,false);RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                byte[] png=pixels.EncodeToPNG();string imagePath=Path.Combine(folder,name+".png");
                var sidecar=new JObject { ["schema"]="xr-demo-capture/1",["name"]=name,["captured_utc"]=DateTimeOffset.UtcNow.ToString("O"),
                    ["image"]=name+".png",["image_sha256"]=Qc5dParser.Sha256(png),["state_json"]=state,["state_sha256"]=Hash(state),["state"]=JObject.Parse(state),
                    ["observations"]=observation,["performance_text"]=Demo.UI.PerformanceText(),["capture_method"]="Unity URP actual camera RenderTexture; CPU-double classified instances; no synthetic fallback",
                    ["device_validation"]="NOT RUN",["pixel_reproducibility"]="not guaranteed across graphics drivers/platforms; exact state and scientific tolerances are checked" };
                using(var f=new FileStream(imagePath,FileMode.CreateNew)) f.Write(png,0,png.Length);
                using(var f=new FileStream(Path.Combine(folder,name+".json"),FileMode.CreateNew)) using(var writer=new StreamWriter(f,new UTF8Encoding(false))) writer.Write(sidecar.ToString(Formatting.Indented));
                LastImage=imagePath;
            }
            finally
            {
                camera.targetTexture=previous;RenderTexture.active=active;if(pixels!=null) Destroy(pixels);if(target!=null) {target.Release();Destroy(target);}
                Demo.UI.Controls.SetActive(controls);Demo.Instrument.UI.gameObject.SetActive(freeUI);Demo.AutoAdvance=automatic;Busy=false;Demo.UI.Refresh();
            }
        }
        void Fail(Exception error) {LastError=error.Message;Demo.ReportFailure(error);}
    }
}
