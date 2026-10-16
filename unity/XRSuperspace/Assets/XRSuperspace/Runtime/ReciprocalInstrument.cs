using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace XRSuperspace
{
    public sealed class ReciprocalPeak
    {
        public Double3 Q;
        public LatticeId[] H;
        public double[] Reference;
    }
    public sealed class ReciprocalInstrument : MonoBehaviour
    {
        public LabController Lab;
        public Transform Origin;
        public string Config=>"reciprocal_h2_qz0";
        public ReciprocalPeak[] Peaks {get;private set;}=Array.Empty<ReciprocalPeak>();
        public double[][] Values {get;private set;}
        public string ResultKey {get;private set;}
        InteractionModel resultModel;
        int resultRevision=-1;
        public bool Current=>Lab.State!=null&&resultModel==Lab.State.Model&&resultRevision==resultModel.Revision;
        string status="NO DATA";
        public string Status {get=>Current?"CURRENT — C# double finite-patch result (Python reference remains authority)":status;private set=>status=value;}
        public int SelectedPeak=-1;
        public float SettlingSeconds=.25f;
        public bool ShowContributionLabel=true;
        public bool ShowContributionVector=true;
        public bool PresentationVisible=true;
        public float PresentationOpacity=1;
        public float PresentationZoom=1;
        public DiffractionDisplay Display {get;private set;}
        public void SetDisplay(DiffractionDisplay settings) {settings?.Validate();Display=settings?.Copy();renderedRevision=-1;}
        public bool VisiblePeak(int row)=>row>=0&&row<Peaks.Length&&Values!=null&&row<Values.Length&&Values[row][3]>=IntensityThreshold&&(Display==null||Display.Visible(Values[row][3],Peaks[row].Q));
        public int VisibleCount=>Enumerable.Range(0,Peaks.Length).Count(VisiblePeak);
        public (double min,double max) ColorRange=>Display?.Range(Values,Peaks,IntensityThreshold)??(0.0,1.0);
        public Color PeakColor(int row)
        {
            if(row==SelectedPeak) return Color.white;
            if(!Current) return new Color(.6f,.35f,.1f,.4f);
            var range=ColorRange;return Display!=null?Display.ColorAt(Values[row][3],range.min,range.max):new Color(.3f+.7f*(float)Math.Sqrt(Math.Max(0,Values[row][3])),.4f+.5f*(float)Math.Sqrt(Math.Max(0,Values[row][3])),1);
        }
        public double IntensityThreshold {get;private set;}
        public void SetIntensityThreshold(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||value<0||value>1) throw new ArgumentException("diffraction display threshold must be in [0,1]");
            IntensityThreshold=value;renderedRevision=-1;
        }
        float changedAt;
        int generation;
        bool calculating;
        // Diagnostic timing only: CPU sum excludes coroutine yields; latency
        // includes settling/yields from latest model change to CURRENT publication.
        double lastChangeRealtime;
        public double LastCalculationCpuMilliseconds {get;private set;}
        public double LastUpdateLatencyMilliseconds {get;private set;}
        public int MeasuredResultRevision {get;private set;}=-1;
        Vector3[] positions=Array.Empty<Vector3>();
        Material material;
        Material contributionMaterial;
        LineRenderer contribution;
        TextMesh contributionLabel;
        readonly System.Collections.Generic.List<(Matrix4x4[] matrices,RenderParams parameters)> drawBatches=new();
        double[][] renderedValues;
        LabState renderedState;
        int renderedRevision=-1,renderedPeak=-2;
        bool renderedCurrent;
        bool renderedLabel;
        bool renderedVector;
        LatticeId? renderedSelection;
        Vector3 renderedOrigin;
        void Awake()
        {
            contributionMaterial=new Material(Lab.View.LineMaterial) {name="Selected n contribution"};contributionMaterial.SetColor("_BaseColor",Color.white);
            var go=new GameObject("Selected real n unit phasor");go.transform.SetParent(transform,false);contribution=go.AddComponent<LineRenderer>();contribution.sharedMaterial=contributionMaterial;contribution.startWidth=contribution.endWidth=.006f;contribution.positionCount=2;
            var label=new GameObject("Real n contribution label");label.transform.SetParent(transform,false);contributionLabel=label.AddComponent<TextMesh>();contributionLabel.fontSize=24;contributionLabel.characterSize=.009f;contributionLabel.anchor=TextAnchor.MiddleCenter;contributionLabel.color=Color.white;
        }
        public Vector3 PeakPosition(int row)=>positions[row];
        void OnEnable() {Lab.DatasetLoaded+=DatasetLoaded;Lab.ModelChanged+=Changed;}
        void OnDisable() {Lab.DatasetLoaded-=DatasetLoaded;Lab.ModelChanged-=Changed;}
        void DatasetLoaded(byte[] metadata)
        {
            generation++;Peaks=Array.Empty<ReciprocalPeak>();Values=null;ResultKey=null;SelectedPeak=-1;Status="LOADING checksum-linked sampling";StartCoroutine(Load(generation,Lab.State.Dataset));
        }
        void Changed() {changedAt=Time.unscaledTime;if(!Current) lastChangeRealtime=Time.realtimeSinceStartupAsDouble;Status="STALE — awaiting settled phason/crop";}
        IEnumerator Load(int token,RuntimeDataset data)
        {
            string path=Path.Combine(Application.streamingAssetsPath,"generated",Path.GetDirectoryName(Lab.DatasetName),"interaction_reciprocal.json").Replace('\\','/');
            using var request=UnityWebRequest.Get(path.Contains("://")?path:new Uri(path).AbsoluteUri);yield return request.SendWebRequest();
            if(token!=generation) yield break;
            if(request.result!=UnityWebRequest.Result.Success) {Status="ERROR: reciprocal local sidecar "+request.error;yield break;}
            try {Peaks=Parse(request.downloadHandler.text,data);Changed();}
            catch(Exception e) {Status="ERROR: "+e.Message;Debug.LogError("[XRSuperspace] "+Status);}
        }
        public static ReciprocalPeak[] Parse(string json,RuntimeDataset data)
        {
            if(json==null||json.Length>4*1024*1024) throw new FormatException("reciprocal sidecar size invalid");
            var root=JObject.Parse(json);
            if((string)root["schema"]!="xr-superspace-reciprocal/1"||(string)root["dataset_checksum"]!=data.Metadata.BinarySha256||(string)root["authority_checksum"]!=data.Metadata.AuthoritativeSha256||(string)root["sampling"]!="reciprocal_h2_qz0"||(string)root["weights"]!="real_unit"||(string)root["phase"]!="positive_exp_i_q_dot_r"||(double)root["qz_tolerance"]!=1e-10||(double)root["group_tolerance"]!=1e-10||(string)root["normalization"]!="I/N_selected_squared; zero_when_empty"||(string)root["crop"]!="exported_scientific_patch_no_taper") throw new FormatException("reciprocal checksum/config disagreement");
            var phi=root["phi"].ToObject<double[]>();if(phi.Length!=2||phi[0]!=data.Metadata.Phi0.X||phi[1]!=data.Metadata.Phi0.Y||(int)root["selected_count"]!=data.Selected.Count(x=>x)) throw new FormatException("reciprocal reference state disagreement");
            var rows=(JArray)root["peaks"];if(rows==null||rows.Count!=381) throw new FormatException("reciprocal_h2_qz0 requires the complete 381-group sampling");
            var seen=new System.Collections.Generic.HashSet<LatticeId>();
            var peaks=rows.Select(row=>
            {
                if(row["h"].Children().SelectMany(n=>n.Children()).Any(n=>n.Type!=JTokenType.Integer)) throw new FormatException("reciprocal h must be exact integers");
                var q=row["q"].ToObject<double[]>();var h=row["h"].ToObject<int[][]>();var reference=row["reference"].ToObject<double[]>();
                if(q.Length!=3||q.Any(x=>double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>100)||Math.Abs(q[2])>1e-10||h.Length==0||h.Length>3125||reference.Length!=4||reference.Any(x=>double.IsNaN(x)||double.IsInfinity(x))) throw new FormatException("invalid peak");
                var tuples=h.Select(n=>
                {
                    if(n.Length!=5||n.Any(x=>Math.Abs((long)x)>2)) throw new FormatException("invalid reciprocal h");var id=new LatticeId(n[0],n[1],n[2],n[3],n[4]);
                    double error=0;for(int j=0;j<3;j++) {double delta=2*Math.PI*data.Project(id,j)-q[j];error+=delta*delta;}
                    if(Math.Sqrt(error)>1.01e-10||!seen.Add(id)) throw new FormatException("reciprocal projection/group mismatch");return id;
                }).ToArray();return new ReciprocalPeak {Q=new Double3(q[0],q[1],q[2]),H=tuples,Reference=reference};
            }).ToArray();
            if(seen.Count!=381) throw new FormatException("incomplete reciprocal contributors");for(int i=1;i<peaks.Length;i++) if(peaks[i-1].H[0].CompareTo(peaks[i].H[0])>=0) throw new FormatException("reciprocal groups must use deterministic lexicographic h order");return peaks;
        }
        void Update() {if(!Current&&!calculating&&Peaks.Length>0&&Time.unscaledTime-changedAt>=SettlingSeconds) StartCoroutine(Calculate());}
        IEnumerator Calculate()
        {
            calculating=true;int token=generation;var model=Lab.State.Model;int revision=model.Revision;string key=model.Key(Config);var values=new double[Peaks.Length][];Status="STALE — C# double recomputing";
            var cpu=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<values.Length;i++)
            {
                if(token!=generation||model!=Lab.State.Model||revision!=model.Revision) {calculating=false;yield break;}
                values[i]=ReciprocalCalculation.Sample(model,Peaks[i].Q);
                if(i%16==15) {cpu.Stop();yield return null;cpu.Start();}
            }
            cpu.Stop();LastCalculationCpuMilliseconds=cpu.Elapsed.TotalMilliseconds;
            LastUpdateLatencyMilliseconds=(Time.realtimeSinceStartupAsDouble-lastChangeRealtime)*1000;MeasuredResultRevision=revision;
            Values=values;ResultKey=key;resultModel=model;resultRevision=revision;Status="CURRENT — C# double finite-patch result (Python reference remains authority)";calculating=false;Rebuild();
        }
        public void Rebuild()
        {
            renderedRevision=-1;
            positions=new Vector3[Peaks.Length];double extent=1;foreach(var p in Peaks) extent=Math.Max(extent,Math.Max(Math.Abs(p.Q.X),Math.Abs(p.Q.Y)));
            for(int i=0;i<positions.Length;i++) positions[i]=Origin.position+Origin.TransformDirection(new Vector3((float)(Peaks[i].Q.X/extent),(float)(Peaks[i].Q.Y/extent),0)*.55f*PresentationZoom);
        }
        public int Pick(Ray ray,float radius=.025f)
        {
            if(!PresentationVisible) return -1;
            int best=-1;float distance=radius;for(int i=0;i<positions.Length;i++) {if(Values!=null&&!VisiblePeak(i)) continue;var delta=positions[i]-ray.origin;float along=Vector3.Dot(delta,ray.direction);float d=(delta-along*ray.direction).magnitude;if(along>=0&&d<distance) {best=i;distance=d;}}return best;
        }
        public string Inspector()
        {
            string head=$"RECIPROCAL qz≈0 / separate parameter view\n{Status}\nH≤2, |qz|≤1e-10, {Peaks.Length} groups · +exp(i q.r)\nunit weights · hard crop/slice · I/N² · no interpolation\nsource {Lab.State?.Dataset.Metadata.BinarySha256}\n";
            if(Lab.State!=null&&Lab.State.Selection.HasValue) head+=$"Linked real-point n={Lab.State.Selection.Value} [{(Lab.State.Model.Active[Lab.State.SelectedRow]?"contributes":"excluded")}] — n is NOT h\n";
            if(SelectedPeak<0||SelectedPeak>=Peaks.Length) return head+"Click a peak or enter its row index.";
            var peak=Peaks[SelectedPeak];var q=peak.Q;head+=$"Peak {SelectedPeak}: q=({q.X:G8},{q.Y:G8},{q.Z:G3})\nh contributors: "+string.Join(" | ",peak.H.Select(x=>x.ToString()));
            if(Values!=null) head+=$"\nI/N²={Values[SelectedPeak][3]:G8}";
            if(Lab.State.Selection.HasValue) {var n=Lab.State.Selection.Value;double phase=q.X*Lab.State.Dataset.Project(n,0)+q.Y*Lab.State.Dataset.Project(n,1)+q.Z*Lab.State.Dataset.Project(n,2);head+=$"\nunit phasor=({Math.Cos(phase):G5},{Math.Sin(phase):G5}); excluded points have zero scattering weight";}
            return head;
        }
        void RebuildDraw()
        {
            bool linked=Lab.State!=null&&Lab.State.Selection.HasValue;contribution.gameObject.SetActive(linked&&ShowContributionVector&&SelectedPeak>=0&&SelectedPeak<Peaks.Length);contributionLabel.gameObject.SetActive(linked&&ShowContributionLabel);
            if(linked)
            {
                var center=Origin.position+Origin.TransformDirection(new Vector3(.66f,-.66f,-.02f));contributionLabel.transform.position=center-Origin.up*.14f;contributionLabel.transform.rotation=Origin.rotation;contributionLabel.text="n=["+Lab.State.Selection.Value+"]\n"+(SelectedPeak<0?"choose q for phase":Lab.State.Model.Active[Lab.State.SelectedRow]?"unit contribution to chosen q":"excluded: zero weight");
                if(SelectedPeak>=0&&SelectedPeak<Peaks.Length) {var q=Peaks[SelectedPeak].Q;var n=Lab.State.Selection.Value;double phase=q.X*Lab.State.Dataset.Project(n,0)+q.Y*Lab.State.Dataset.Project(n,1)+q.Z*Lab.State.Dataset.Project(n,2);float radius=Lab.State.Model.Active[Lab.State.SelectedRow]?.11f:0;contribution.SetPosition(0,center);contribution.SetPosition(1,center+Origin.TransformDirection(new Vector3((float)Math.Cos(phase),(float)Math.Sin(phase),0)*radius));}
            }
            drawBatches.Clear();if(Values==null||positions.Length!=Values.Length) return;
            if(material==null) {material=new Material(Lab.View.PointMaterial) {name="Reciprocal preview instances"};material.enableInstancing=true;}
            for(int start=0;start<positions.Length;start+=512)
            {
                int count=Math.Min(512,positions.Length-start);var matrices=new Matrix4x4[count];var colors=new Vector4[count];
                var range=ColorRange;
                for(int j=0;j<count;j++)
                {
                    int i=start+j;float value=(float)Math.Sqrt(Math.Max(0,Values[i][3]));float size=VisiblePeak(i)? .006f+.024f*value:0;
                    Color color=i==SelectedPeak?Color.white:!Current?new Color(.6f,.35f,.1f,.4f):Display!=null?Display.ColorAt(Values[i][3],range.min,range.max):new Color(.3f+.7f*value,.4f+.5f*value,1,.35f+.65f*value);
                    colors[j]=color;matrices[j]=Matrix4x4.TRS(positions[i],Quaternion.identity,Vector3.one*size*2);
                }
                var properties=new MaterialPropertyBlock();properties.SetVectorArray("_InstanceColor",colors);drawBatches.Add((matrices,new RenderParams(material) {matProps=properties}));
            }
        }
        void LateUpdate()
        {
            if(!PresentationVisible) {contribution.gameObject.SetActive(false);contributionLabel.gameObject.SetActive(false);renderedRevision=-1;return;}
            var state=Lab.State;int revision=state?.Model.Revision??-1;bool current=Current;
            if(renderedValues!=Values||renderedState!=state||renderedRevision!=revision||renderedPeak!=SelectedPeak||renderedCurrent!=current||renderedLabel!=ShowContributionLabel||renderedVector!=ShowContributionVector||!Nullable.Equals(renderedSelection,state?.Selection)||renderedOrigin!=Origin.position)
            {
                renderedValues=Values;renderedState=state;renderedRevision=revision;renderedPeak=SelectedPeak;renderedCurrent=current;renderedLabel=ShowContributionLabel;renderedVector=ShowContributionVector;renderedSelection=state?.Selection;renderedOrigin=Origin.position;RebuildDraw();
            }
            foreach(var batch in drawBatches) {batch.parameters.matProps.SetFloat("_PresentationAlpha",PresentationOpacity);Graphics.RenderMeshInstanced(batch.parameters,Lab.View.PointMesh,0,batch.matrices);}
        }
        public void DrawForCamera(Camera camera)
        {
            if(!PresentationVisible) return;
            RebuildDraw();foreach(var batch in drawBatches) {batch.parameters.matProps.SetFloat("_PresentationAlpha",PresentationOpacity);var parameters=batch.parameters;parameters.camera=camera;Graphics.RenderMeshInstanced(parameters,Lab.View.PointMesh,0,batch.matrices);}
        }
        void OnDestroy() {foreach(var owned in new[]{material,contributionMaterial}) if(owned!=null) {if(Application.isPlaying) Destroy(owned);else DestroyImmediate(owned);}}
    }
}
