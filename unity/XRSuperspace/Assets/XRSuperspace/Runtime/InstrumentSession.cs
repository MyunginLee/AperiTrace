using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace XRSuperspace
{
    public sealed class InstrumentSession
    {
        public string Schema="xr-superspace-session/1",TimestampUtc,DatasetChecksum,AuthorityChecksum,SelectedStableId;
        public double[] Phi;
        public AnalysisFilter Filter;
        public float LayerSeparation=1,PointSize=1,BoundaryBand=.04f;
        public bool ShowCandidates,GraphVisible=true;
        public string DiffractionConfig="reciprocal_h2_qz0";
        public int SelectedPeak=-1;
        public float[] CameraPosition,CameraRotation,CameraPivot,PhysicalOrigin,InternalOrigin,ReciprocalOrigin,PanelOrigin;
        public float CameraFov;
        public double[][] PhasonPath=Array.Empty<double[]>();
        public AnalysisFilter PhasonPathFilter=new AnalysisFilter();
        public double[][] PhasonTrace=Array.Empty<double[]>();
        public static string Encode(InstrumentSession state)=>JsonConvert.SerializeObject(state,Formatting.Indented,new JsonSerializerSettings {Culture=CultureInfo.InvariantCulture,FloatFormatHandling=FloatFormatHandling.String});
        public static InstrumentSession Decode(string json)
        {
            if(json==null||json.Length>16*1024*1024) throw new FormatException("session missing or larger than 16 MiB");
            var root=JObject.Parse(json);foreach(string name in new[]{"Schema","TimestampUtc","DatasetChecksum","AuthorityChecksum","Phi","Filter","LayerSeparation","PointSize","BoundaryBand","ShowCandidates","GraphVisible","DiffractionConfig","SelectedPeak","SelectedStableId","CameraPosition","CameraRotation","CameraPivot","PhysicalOrigin","InternalOrigin","ReciprocalOrigin","PanelOrigin","CameraFov","PhasonPath","PhasonPathFilter","PhasonTrace"}) if(root.Property(name)==null) throw new FormatException("missing session field "+name);
            var state=JsonConvert.DeserializeObject<InstrumentSession>(json,new JsonSerializerSettings {MaxDepth=32,MissingMemberHandling=MissingMemberHandling.Error});
            if(state==null||state.Schema!="xr-superspace-session/1"||!DateTimeOffset.TryParse(state.TimestampUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out _)||!Digest(state.DatasetChecksum)||!Digest(state.AuthorityChecksum)) throw new FormatException("session schema/time/checksum invalid");
            if(state.Phi==null||state.Phi.Length!=2||state.Phi.Any(x=>!Finite(x))) throw new FormatException("invalid phi");
            if(state.Filter==null) throw new FormatException("filter missing");state.Filter.Validate();
            foreach(var vector in new[]{state.CameraPosition,state.CameraRotation,state.CameraPivot,state.PhysicalOrigin,state.InternalOrigin,state.ReciprocalOrigin,state.PanelOrigin}) if(vector==null||vector.Length!=3||vector.Any(x=>!Finite(x)||Math.Abs(x)>100)) throw new FormatException("invalid camera/layout");
            if(!Finite(state.LayerSeparation)||state.LayerSeparation<.25||state.LayerSeparation>4||!Finite(state.PointSize)||state.PointSize<.25||state.PointSize>3||!Finite(state.BoundaryBand)||state.BoundaryBand<0||state.BoundaryBand>.2||!Finite(state.CameraFov)||state.CameraFov<10||state.CameraFov>100||state.DiffractionConfig!="reciprocal_h2_qz0"||state.SelectedPeak< -1||state.SelectedPeak>=4096) throw new FormatException("unsupported display/diffraction state");
            if(state.SelectedStableId!=null) ParseTuple(state.SelectedStableId);
            if(state.PhasonPath==null||state.PhasonPath.Length>4096||state.PhasonPath.Any(p=>p==null||p.Length!=2||p.Any(x=>!Finite(x)))) throw new FormatException("invalid recorded path");
            if(state.PhasonPathFilter==null) throw new FormatException("path filter missing");state.PhasonPathFilter.Validate();
            if(state.PhasonTrace==null||state.PhasonTrace.Length>512||state.PhasonTrace.Any(p=>p==null||p.Length!=2||p.Any(x=>!Finite(x)))) throw new FormatException("invalid display trace");
            return state;
        }
        static bool Digest(string s)=>s!=null&&s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        public static LatticeId ParseTuple(string text)
        {
            string[] values=text.Split(new[]{',',' ',';'},StringSplitOptions.RemoveEmptyEntries);
            if(values.Length!=5) throw new FormatException("enter exactly five int32 values");
            var n=values.Select(x=>int.Parse(x,NumberStyles.Integer,CultureInfo.InvariantCulture)).ToArray();return new LatticeId(n[0],n[1],n[2],n[3],n[4]);
        }
        public bool Matches(RuntimeDataset data)=>DatasetChecksum==data.Metadata.BinarySha256&&AuthorityChecksum==data.Metadata.AuthoritativeSha256;
        public static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        public static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
    }
}
