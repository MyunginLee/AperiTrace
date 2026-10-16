using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace XRSuperspace
{
    public sealed class LinkedInstrument : MonoBehaviour
    {
        public LabController Lab;
        public ReciprocalInstrument Reciprocal;
        public InstrumentUI UI;
        public InstrumentCapturePreset[] Presets;
        public Camera Camera;
        public string Message {get;private set;}="Ready: model coordinates and display transforms are separate.";
        public string LastFailure {get;private set;}="none";
        public bool Recording {get;private set;}
        public bool Playing {get;private set;}
        IReadOnlyList<double[]> pathView,traceView;
        public IReadOnlyList<double[]> Path=>pathView??=path.AsReadOnly();
        readonly List<double[]> path=new();
        readonly List<double[]> trace=new();
        public IReadOnlyList<double[]> Trace=>traceView??=trace.AsReadOnly();
        AnalysisFilter pathFilter=new AnalysisFilter();
        AnalysisFilter coverage=new AnalysisFilter();
        string pendingSession,lastPath;
        string Folder=>System.IO.Path.Combine(Application.persistentDataPath,"SuperspaceSessions");
        public string LastSessionPath=>lastPath;
        void OnEnable() {Lab.DatasetLoaded+=Loaded;Lab.ModelChanged+=Changed;}
        void OnDisable() {Lab.DatasetLoaded-=Loaded;Lab.ModelChanged-=Changed;StopAllCoroutines();Playing=Recording=false;}
        void Loaded(byte[] json)
        {
            var crop=JObject.Parse(Encoding.UTF8.GetString(json))["metadata"]["model"]["parameters"]["physical_crop"];
            string kind=(string)crop["kind"];coverage=new AnalysisFilter {CropKind=kind,Radius=(double)(crop["radial_max"]??crop["radius"]??crop["radius_max"]),ZMax=kind=="sphere"?(double)(crop["radius"]??crop["radius_max"]):(double)crop["z_abs_max"]};
            coverage.Validate();var defaults=coverage.Copy();defaults.CropEnabled=false;Lab.SetFilter(defaults);Lab.State.Model.ClearJournal();path.Clear();pathFilter=defaults.Copy();pendingSession=null;Recording=false;Playing=false;StopAllCoroutines();
            Message="Dataset verified. Sessions with another checksum require explicit override.";
            trace.Clear();trace.Add(new[]{Lab.State.Phi.X,Lab.State.Phi.Y});
        }
        void Changed()
        {
            var current=Lab.State.Phi;if(trace.Count==0||trace[trace.Count-1][0]!=current.X||trace[trace.Count-1][1]!=current.Y) {trace.Add(new[]{current.X,current.Y});if(trace.Count>512) trace.RemoveAt(0);}
            if(Recording)
            {
                if(pathFilter.Key!=Lab.State.Model.Filter.Key) {Recording=false;Message="Recording stopped: crop/slice changed. Start a new path for that model.";}
                else if(path.Count>=4096) {Recording=false;Message="Path budget reached (4096 samples).";}
                else {var phi=Lab.State.Phi;if(path.Count==0||path[path.Count-1][0]!=phi.X||path[path.Count-1][1]!=phi.Y) path.Add(new[]{phi.X,phi.Y});}
            }
        }
        public void Guard(Action operation) {try {operation();} catch(Exception e) {LastFailure=e.GetType().Name+": "+e.Message;Message=e.Message;Debug.LogWarning("[XRSuperspace] "+Message);}}
        public void SetPhiText(string x,string y)=>Guard(()=> {Lab.SetPhi(Number(x),Number(y));Message="Numeric phi applied, bounded to exported domain.";});
        public static double Number(string text) {double value=double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);if(double.IsNaN(value)||double.IsInfinity(value)) throw new FormatException("finite numbers only");return value;}
        public void SnapZero()=>Lab.SetPhi(0,0);
        public int Resolve(string id) {var tuple=InstrumentSession.ParseTuple(id);for(int i=0;i<Lab.State.Dataset.Candidates.Count;i++) if(Lab.State.Dataset.Candidates[i].Id.Equals(tuple)) return i;throw new ArgumentException("exact tuple is outside this exported candidate patch");}
        public void Search(string text)=>Guard(()=> {Lab.Select(Resolve(text));Message="Same exact n linked in physical/internal views and reciprocal contribution inspector.";});
        void ValidateFilter(AnalysisFilter filter)
        {
            filter.Validate();if(filter.CropKind!=coverage.CropKind||filter.Radius>coverage.Radius||filter.ZMax>coverage.ZMax) throw new ArgumentException("crop cannot expand beyond exported scientific coverage");
        }
        public void SetFilterText(string radius,string zmax,string slice,string width,bool toggleCrop,bool toggleSlice)=>Guard(()=>
        {
            var f=Lab.State.Model.Filter.Copy();f.Radius=Number(radius);f.ZMax=Number(zmax);f.SliceZ=Number(slice);f.SliceHalfWidth=Number(width);if(toggleCrop) f.CropEnabled=!f.CropEnabled;if(toggleSlice) f.SliceEnabled=!f.SliceEnabled;ValidateFilter(f);Lab.SetFilter(f);Message="Scientific crop/slice applied to exact coordinates; membership and subset shown separately.";
        });
        public void Display(float layerDelta,float sizeDelta)
        {
            Lab.View.LayerSeparation=Mathf.Clamp(Lab.View.LayerSeparation+layerDelta,.25f,4);Lab.View.PointSize=Mathf.Clamp(Lab.View.PointSize+sizeDelta,.25f,3);Lab.View.Rebuild(Lab.State);Message="Display only: scientific state, graph, events and diffraction unchanged.";
        }
        public void ToggleContext() {Lab.View.ShowCandidates=!Lab.View.ShowCandidates;Lab.View.Rebuild(Lab.State);}
        public void Record()
        {
            if(Playing) {Message="Stop playback before recording.";return;}
            Recording=!Recording;if(Recording) {path.Clear();pathFilter=Lab.State.Model.Filter.Copy();path.Add(new[]{Lab.State.Phi.X,Lab.State.Phi.Y});}Message=Recording?"Recording exact phi samples (not interpolated).":"Path recorded: "+path.Count+" samples.";
        }
        public void Play() {if(Playing) {StopAllCoroutines();Playing=false;Message="Playback stopped.";return;}if(path.Count==0) {Message="Record a path first.";return;}Recording=false;StartCoroutine(Playback());}
        IEnumerator Playback()
        {
            Playing=true;var samples=path.Select(p=>(double[])p.Clone()).ToArray();
            foreach(var phi in samples)
            {
                while(Lab.GetComponent<ScientificDemo>() is ScientificDemo blocked&&blocked.PlaybackBlocked) yield return null;
                bool ok=true;try {Lab.ApplyModel(new Double2(phi[0],phi[1]),pathFilter);} catch(Exception e) {Message=e.Message;ok=false;}
                if(!ok) break;
                for(float remaining=.25f;remaining>0;) {yield return null;if(Lab.GetComponent<ScientificDemo>() is not ScientificDemo demo||!demo.PlaybackBlocked) remaining-=Time.unscaledDeltaTime;}
            }
            Playing=false;Message="Playback finished: exact ordered samples; timer changes presentation only.";
        }
        public InstrumentSession Capture(string timestamp=null)
        {
            var view=Lab.View;return new InstrumentSession {TimestampUtc=timestamp??DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture),DatasetChecksum=Lab.State.Dataset.Metadata.BinarySha256,AuthorityChecksum=Lab.State.Dataset.Metadata.AuthoritativeSha256,Phi=new[]{Lab.State.Phi.X,Lab.State.Phi.Y},Filter=Lab.State.Model.Filter.Copy(),SelectedStableId=Lab.State.Selection?.ToString(),LayerSeparation=view.LayerSeparation,PointSize=view.PointSize,BoundaryBand=view.BoundaryBand,ShowCandidates=view.ShowCandidates,GraphVisible=view.ShowEdges,DiffractionConfig=Reciprocal.Config,SelectedPeak=Reciprocal.SelectedPeak,CameraPosition=InstrumentSession.V(Camera.transform.position),CameraRotation=InstrumentSession.V(Camera.transform.eulerAngles),CameraPivot=InstrumentSession.V(Lab.GetComponent<DesktopInput>().Pivot),CameraFov=Camera.fieldOfView,PhysicalOrigin=InstrumentSession.V(view.PhysicalOrigin.position),InternalOrigin=InstrumentSession.V(view.InternalOrigin.position),ReciprocalOrigin=InstrumentSession.V(Reciprocal.Origin.position),PanelOrigin=InstrumentSession.V(UI.transform.position),PhasonPath=path.Select(p=>(double[])p.Clone()).ToArray(),PhasonPathFilter=pathFilter.Copy(),PhasonTrace=trace.Select(p=>(double[])p.Clone()).ToArray()};
        }
        public void ApplySession(InstrumentSession session,bool checksumOverride=false)
        {
            // Validate everything before any mutation, including checksum and coverage.
            session=InstrumentSession.Decode(InstrumentSession.Encode(session));
            if(!session.Matches(Lab.State.Dataset)&&!checksumOverride) throw new InvalidOperationException("CHECKSUM MISMATCH: nothing loaded. Use the explicit Override button only if intended.");
            void Phi(double[] p) {if(p[0]<Lab.PhiMin.X||p[0]>Lab.PhiMax.X||p[1]<Lab.PhiMin.Y||p[1]>Lab.PhiMax.Y) throw new ArgumentException("session phi outside exported domain");}
            Phi(session.Phi);foreach(var p in session.PhasonPath) Phi(p);foreach(var p in session.PhasonTrace) Phi(p);ValidateFilter(session.Filter);ValidateFilter(session.PhasonPathFilter);
            int row=session.SelectedStableId==null?-1:Resolve(session.SelectedStableId);
            Lab.ApplyModel(new Double2(session.Phi[0],session.Phi[1]),session.Filter);
            StopAllCoroutines();Recording=Playing=false;path.Clear();path.AddRange(session.PhasonPath.Select(p=>(double[])p.Clone()));pathFilter=session.PhasonPathFilter.Copy();
            trace.Clear();trace.AddRange(session.PhasonTrace.Select(p=>(double[])p.Clone()));
            var view=Lab.View;view.LayerSeparation=session.LayerSeparation;view.PointSize=session.PointSize;view.BoundaryBand=session.BoundaryBand;view.ShowCandidates=session.ShowCandidates;view.ShowEdges=session.GraphVisible;
            view.PhysicalOrigin.position=InstrumentSession.V(session.PhysicalOrigin);view.InternalOrigin.position=InstrumentSession.V(session.InternalOrigin);Reciprocal.Origin.position=InstrumentSession.V(session.ReciprocalOrigin);UI.transform.position=InstrumentSession.V(session.PanelOrigin);
            if(row<0) Lab.State.ClearSelection();else Lab.Select(row);view.Rebuild(Lab.State);Reciprocal.SelectedPeak=session.SelectedPeak;Reciprocal.Rebuild();
            Camera.transform.SetPositionAndRotation(InstrumentSession.V(session.CameraPosition),Quaternion.Euler(InstrumentSession.V(session.CameraRotation)));Camera.fieldOfView=session.CameraFov;var input=Lab.GetComponent<DesktopInput>();input.Pivot=InstrumentSession.V(session.CameraPivot);input.SyncCamera();
            Message=checksumOverride?"Explicit checksum override applied; original session checksums retained in source file.":"Session restored with matching dataset checksum.";
        }
        string WriteNew(string prefix,string payload)
        {
            Directory.CreateDirectory(Folder);string file=System.IO.Path.Combine(Folder,prefix+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")+".json");
            using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write);using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write(payload);return file;
        }
        public void Save()=>Guard(()=> {lastPath=WriteNew("session",InstrumentSession.Encode(Capture()));Message="Saved new local session: "+lastPath;});
        public void LoadLast()=>Guard(()=>
        {
            if(lastPath==null) lastPath=Directory.GetFiles(Folder,"session-*.json").OrderBy(x=>x,StringComparer.Ordinal).LastOrDefault();if(lastPath==null) throw new FileNotFoundException("no saved local session");
            if(new FileInfo(lastPath).Length>16*1024*1024) throw new FormatException("session too large");LoadJson(File.ReadAllText(lastPath));
        });
        public void LoadJson(string json) {pendingSession=json;ApplySession(InstrumentSession.Decode(pendingSession));pendingSession=null;}
        public void OverrideLast()=>Guard(()=> {if(pendingSession==null) throw new InvalidOperationException("no refused pending session; Load first");ApplySession(InstrumentSession.Decode(pendingSession),true);pendingSession=null;});
        public string JournalJson()=>JsonConvert.SerializeObject(new {schema="xr-superspace-events/1",frames=Lab.State.Model.Journal},Formatting.Indented);
        public void ExportJournal()=>Guard(()=> {Message="Event log saved: "+WriteNew("events",JournalJson());});
        public void ClearJournal() {Lab.State.Model.ClearJournal();Message="Explicitly cleared event journal; frame 0 is the current model baseline.";}
        public void Preset(int index)=>Guard(()=>
        {
            if(Lab.State.Dataset.Metadata.BinarySha256!=InstrumentCapturePreset.Checksum) throw new InvalidOperationException("preset requires canonical_small_v1: use Dataset button first");
            var p=Presets[index];var session=Capture();session.Phi=new[]{p.PhiX,p.PhiY};session.Filter=coverage.Copy();session.Filter.CropEnabled=session.Filter.SliceEnabled=false;session.Filter.SliceZ=0;session.Filter.SliceHalfWidth=.25;session.SelectedStableId=string.IsNullOrWhiteSpace(p.SelectedId)?null:p.SelectedId;session.LayerSeparation=p.LayerSeparation;session.PointSize=p.PointSize;session.BoundaryBand=p.BoundaryBand;session.GraphVisible=p.GraphVisible;session.ShowCandidates=p.ShowCandidates;session.SelectedPeak=-1;session.PhasonPath=Array.Empty<double[]>();session.PhasonPathFilter=session.Filter.Copy();session.PhasonTrace=index==3?new[]{new[]{0.0,0.0},new[]{p.PhiX,p.PhiY}}:new[]{new[]{p.PhiX,p.PhiY}};session.CameraPosition=InstrumentSession.V(p.CameraPosition);session.CameraRotation=InstrumentSession.V(p.CameraEuler);session.CameraPivot=InstrumentSession.V(p.CameraPivot);session.CameraFov=p.Fov;session.PhysicalOrigin=InstrumentSession.V(p.PhysicalOrigin);session.InternalOrigin=InstrumentSession.V(p.InternalOrigin);session.ReciprocalOrigin=InstrumentSession.V(p.ReciprocalOrigin);session.PanelOrigin=InstrumentSession.V(p.PanelOrigin);
            if(index==3) {Lab.ApplyModel(new Double2(0,0),session.Filter);Lab.State.Model.ClearJournal();}ApplySession(session);if(index!=3) Lab.State.Model.ClearJournal();Message="Deterministic preset: "+p.Title;
        });
        public string Inspector()
        {
            if(Lab.State==null) return "Loading linked instrument…";
            var s=Lab.State;var b=new StringBuilder();b.AppendLine($"5D IDENTITY / candidates {s.Dataset.Candidates.Count} · active {s.SelectedCount} · boundary {s.BoundaryCount}");b.AppendLine($"phi=({s.Phi.X:G8},{s.Phi.Y:G8}) · u+phi in FIXED W");b.AppendLine($"crop {s.Model.Filter.CropKind} {(s.Model.Filter.CropEnabled?"ON":"OFF")} · slice {(s.Model.Filter.SliceEnabled?"ON":"OFF")} · graph {s.Edges.Length}");b.AppendLine($"display: layer×{Lab.View.LayerSeparation:G3}, size×{Lab.View.PointSize:G3}, diagnostic band {Lab.View.BoundaryBand:G3} (not ε)");
            int row=s.SelectedRow;if(row<0) return b+"Select a point in either view or search exact n.";
            var n=s.Selection.Value;double x=s.Dataset.Project(n,0),y=s.Dataset.Project(n,1),z=s.Dataset.Project(n,2),u=s.Dataset.Project(n,3),v=s.Dataset.Project(n,4);double d=Membership.Distance(s.Dataset.DoubleWindow,new Double2(u+s.Phi.X,v+s.Phi.Y));
            b.AppendLine($"n = [{n}] · BLAKE2b-8 {s.Dataset.Candidates[row].StableHash:x16}");b.AppendLine($"r=({x:G17}, {y:G17}, {z:G17})");b.AppendLine($"u=({u:G17}, {v:G17})");b.AppendLine($"u+phi=({u+s.Phi.X:G17}, {v+s.Phi.Y:G17})");b.AppendLine($"window {s.States[row]} · active {s.Model.Active[row]} · signed d={d:G17}");b.AppendLine($"layer z={z:G17} · sum(n)/sqrt(5)={(n.N0+(double)n.N1+n.N2+n.N3+n.N4)/Math.Sqrt(5):G17}");
            var lookup=new HashSet<LatticeId>(s.Dataset.Candidates.Select(p=>p.Id));b.Append("Neighbors present in patch: ");for(int j=0;j<5;j++) foreach(int sign in new[]{-1,1}) {int[] values={n.N0,n.N1,n.N2,n.N3,n.N4};long next=(long)values[j]+sign;if(next<int.MinValue||next>int.MaxValue) continue;values[j]=(int)next;var id=new LatticeId(values[0],values[1],values[2],values[3],values[4]);if(lookup.Contains(id)) b.Append($"{(sign>0?"+":"-")}e{j} [{id}] ");}
            b.AppendLine("\nSpecies/decorator: absent (canonical real-unit points).");return b.ToString();
        }
    }
}
