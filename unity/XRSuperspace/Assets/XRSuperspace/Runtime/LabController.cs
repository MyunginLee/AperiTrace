using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace XRSuperspace
{
    public sealed class LabController : MonoBehaviour
    {
        public LabSettings Settings;
        public LabRenderer View;
        public Text Diagnostics;
        public Transform PhasonHandle;
        public LabState State { get; private set; }
        public string Status { get; private set; }="not loaded";
        public string DatasetName { get; private set; }="none";
        public string DatasetLabel=>State==null?DatasetLabels.Unvalidated:DatasetLabels.Canonical;
        public Double2 PhiMin { get; private set; }
        public Double2 PhiMax { get; private set; }
        public bool Busy { get; private set; }
        int datasetIndex;
        float nextPanel;
        public event Action<byte[]> DatasetLoaded;
        public event Action ModelChanging,ModelChanged,SelectionChanged;
        void Awake()
        {
            if(PhasonHandle==null) return;
            var old=PhasonHandle.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if(old!=null) {old.enabled=false;Destroy(old);}
            var control=PhasonHandle.GetComponent<PhasonHandleControl>();
            if(control==null) control=PhasonHandle.gameObject.AddComponent<PhasonHandleControl>();
            control.Lab=this;
        }
        void Start()=>Reload();
        public void Reload() { if(!Busy) StartCoroutine(Load()); }
        public void ResetFirstDataset() {if(Busy) throw new InvalidOperationException("dataset load is busy");datasetIndex=0;Reload();}
        public void NextDataset() { if(Busy) return;datasetIndex=(datasetIndex+1)%Settings.Datasets.Length;Reload(); }
        IEnumerator Read(string uri,Action<byte[]> received)
        {
            // Android StreamingAssets is inside an APK. Local jar/file URIs only;
            // no HTTP/cloud loader or network dependency is introduced.
            using var request=UnityWebRequest.Get(uri);
            yield return request.SendWebRequest();
            if(request.result!=UnityWebRequest.Result.Success) { Status="LOAD ERROR: "+request.error;yield break; }
            received(request.downloadHandler.data);
        }
        IEnumerator Load()
        {
            Busy=true;Status="loading and verifying local dataset";
            string relative=Settings.Datasets[datasetIndex];
            if(relative.Contains(":")||relative.Contains("..")||relative.StartsWith("/")||relative.StartsWith("\\")) { Status="LOAD ERROR: dataset must be a safe local generated path";Busy=false;yield break; }
            string basePath=Path.Combine(Application.streamingAssetsPath,"generated",relative).Replace('\\','/');
            string uri=basePath.Contains("://")?basePath:new Uri(basePath).AbsoluteUri;
            byte[] binary=null,json=null;
            yield return Read(uri,bytes=>binary=bytes);
            string metaPath=basePath.Substring(0,basePath.LastIndexOf('/')+1)+"metadata.json";
            yield return Read(metaPath.Contains("://")?metaPath:new Uri(metaPath).AbsoluteUri,bytes=>json=bytes);
            if(binary!=null&&json!=null)
            {
                try { SetDataset(binary,json,relative); }
                catch(Exception error) { Status="PARSER ERROR: "+error.Message;Debug.LogError("[XRSuperspace] "+Status); }
            }
            Busy=false;
        }
        public void SetDataset(byte[] binary,byte[] metadata,string name)
        {
            var data=Qc5dParser.Parse(binary);if(data.Candidates.Count>Settings.MaximumInteractiveCandidates) throw new FormatException("baseline interactive budget exceeded; no per-point objects will be spawned");
            if(metadata==null||metadata.Length>8*1024*1024) throw new FormatException("metadata absent or exceeds 8 MiB");
            var envelope=JObject.Parse(System.Text.Encoding.UTF8.GetString(metadata));
            DatasetLabels.RequireCanonical(envelope);
            if((string)envelope["schema_version"]!="1.0.0"||(string)envelope["authoritative_sha256"]!=data.Metadata.AuthoritativeSha256) throw new FormatException("sidecar version/source checksum disagrees with binary");
            var domain=envelope["metadata"]?["model"]?["parameters"]?["phason_domain"];
            if(domain==null) throw new FormatException("sidecar phason domain missing");
            var min=domain["min"].ToObject<double[]>();var max=domain["max"].ToObject<double[]>();
            if(min.Length!=2||max.Length!=2||Array.Exists(min,x=>double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>1e6)||Array.Exists(max,x=>double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>1e6)||min[0]>max[0]||min[1]>max[1]||data.Metadata.Phi0.X<min[0]||data.Metadata.Phi0.X>max[0]||data.Metadata.Phi0.Y<min[1]||data.Metadata.Phi0.Y>max[1]) throw new FormatException("sidecar phason domain invalid");
            PhiMin=new Double2(min[0],min[1]);PhiMax=new Double2(max[0],max[1]);
            State=new LabState(data);DatasetName=name;Status="PASS: schema, layout, SHA, IDs, coordinates, masks and graph";
            View.Rebuild(State);PositionHandle();Debug.Log("[XRSuperspace] loaded "+name+" candidates="+data.Candidates.Count);
            DatasetLoaded?.Invoke(metadata);
        }
        public void SetPhi(double x,double y)
        {
            if(State==null) return;x=Math.Max(PhiMin.X,Math.Min(PhiMax.X,x));y=Math.Max(PhiMin.Y,Math.Min(PhiMax.Y,y));
            if(x==State.Phi.X&&y==State.Phi.Y) return;
            try {ModelChanging?.Invoke();State.SetPhi(new Double2(x,y));View.Rebuild(State);PositionHandle();ModelChanged?.Invoke();}
            catch(ArgumentException error) {Status=error.Message;PositionHandle();}
            catch(InvalidOperationException error) {Status=error.Message;PositionHandle();}
        }
        public void ResetPhi() {PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();if(State!=null) SetPhi(State.Dataset.Metadata.Phi0.X,State.Dataset.Metadata.Phi0.Y);PositionHandle();}
        public void SyncHandlePose()=>PositionHandle();
        void PositionHandle() { if(PhasonHandle!=null&&State!=null) PhasonHandle.SetPositionAndRotation(View.InternalOrigin.position+View.InternalOrigin.TransformDirection(new Vector3((float)State.Phi.X,(float)State.Phi.Y,0)*View.InternalScale),View.InternalOrigin.rotation); }
        public void Select(int index) { if(State==null) return;State.Select(index);View.Rebuild(State);SelectionChanged?.Invoke(); }
        public void SetFilter(AnalysisFilter filter) {if(State==null||filter.Key==State.Model.Filter.Key) return;ModelChanging?.Invoke();if(State.Model.Apply(State.Phi,filter)) {View.Rebuild(State);ModelChanged?.Invoke();}}
        public void ApplyModel(Double2 phi,AnalysisFilter filter) {if(State==null||phi.X==State.Phi.X&&phi.Y==State.Phi.Y&&filter.Key==State.Model.Filter.Key) return;ModelChanging?.Invoke();if(State.Model.Apply(phi,filter)) {View.Rebuild(State);PositionHandle();ModelChanged?.Invoke();}}
        public void ToggleEdges() { View.ShowEdges=!View.ShowEdges;View.SetEdgeVisibility(); }
        void Update()
        {
            // No transform-to-model feedback loop: constrained XR input and desktop
            // input call SetPhi; only the committed model positions the handle.
            PositionHandle();
            if(Time.unscaledTime<nextPanel) return;nextPanel=Time.unscaledTime+0.2f;
            if(Diagnostics!=null) Diagnostics.text=DatasetLabel+"\n"+DiagnosticText();
        }
        public string DiagnosticText()
        {
            if(State==null) return "XR SUPERSPACE / DESKTOP LAB\n"+Status;
            var m=State.Dataset.Metadata;string selected=State.Selection.HasValue?State.Selection.Value.ToString():"none";
            int row=State.SelectedRow;string values=row<0?"":$"\nr=({State.Dataset.Project(State.Selection.Value,0):G5},{State.Dataset.Project(State.Selection.Value,1):G5},{State.Dataset.Project(State.Selection.Value,2):G5})\nu+phi=({State.Dataset.Project(State.Selection.Value,3)+State.Phi.X:G5},{State.Dataset.Project(State.Selection.Value,4)+State.Phi.Y:G5})";
            string tracking=GetComponent<LabXrMode>()?.TrackingStatus??"desktop";
            return $"XR SUPERSPACE · canonical dimensionless model\n{DatasetName}\nSHA-256 {m.BinarySha256}\n{Status}\nCandidates {State.Dataset.Candidates.Count} · selected {State.SelectedCount} · boundary {State.BoundaryCount}\nPhi ({State.Phi.X:F5}, {State.Phi.Y:F5}) · preview ε {m.PreviewEpsilon:G3}\nSchema {RuntimeMetadata.SchemaVersion} · {Time.unscaledDeltaTime*1000:F2} ms observed frame\nCPU double baseline / instanced preview · GPU compute not implemented\nMembership current · diffraction placeholder, not calculated\n{tracking}\nSelected exact n: {selected}{values}\nEdges drawn {Math.Min(State.Edges.Length,Settings.MaximumRenderedEdges)}/{State.Edges.Length}\nR reload · Tab dataset · E edges · 0 reset phi · X start PC OpenXR\nRMB orbit · MMB pan · wheel zoom · click point / drag cyan handle\nArrow keys adjust phi · desktop controls retained in XR";
        }
    }
}
