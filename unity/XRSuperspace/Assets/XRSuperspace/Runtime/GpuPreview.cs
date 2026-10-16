using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace
{
    public enum PrecisionMode {GPU_PREVIEW,CPU_VALIDATED,PYTHON_FIXTURE}
    public sealed class GpuPreview : MonoBehaviour
    {
        public LabController Lab;
        public ComputeShader Classifier;
        public Material GpuMaterial;
        public PreviewTier[] Tiers;
        public int TierIndex=1;
        public int FallbackCandidateLimit=20000;
        public bool ForceFallback;
        public PrecisionMode Mode {get;private set;}=PrecisionMode.GPU_PREVIEW;
        public bool UsingGpu=>buffers!=null;
        public string Backend {get;private set;}="not loaded";
        public string Message {get;private set;}="Validate captures phi and compares exact IDs; no automatic readback.";
        public PrecisionComparison Comparison {get;private set;}
        public double DispatchSubmissionMilliseconds {get;private set;}
        public long OwnedDrawAllocatedBytes {get;private set;}
        public long BufferBytes=>buffers?.EstimatedBytes??0;
        public PreviewTier Tier=>Tiers[TierIndex];
        public int DrawStride=>Lab.State==null?1:Tier.Stride(Lab.State.Dataset.Candidates.Count);
        public int VisibleCandidateBudget=>Lab.State==null?0:(Lab.State.Dataset.Candidates.Count+DrawStride-1)/DrawStride;
        GpuPointBuffers buffers;
        InteractionModel lastModel;
        int lastRevision=-1;
        PreviewCandidate[] candidates;
        Material ownedMaterial;
        MaterialPropertyBlock physical,internalView;
        readonly Bounds bounds=new(Vector3.zero,Vector3.one*100);
        float changedAt;
        bool configuring;
        TextMesh[] labels;
        void OnEnable() {Lab.View.Rebuilt+=Rebuilt;Lab.DatasetLoaded+=Loaded;}
        void OnDisable() {Lab.View.Rebuilt-=Rebuilt;Lab.DatasetLoaded-=Loaded;Release();Lab.View.ExternalPointDrawing=false;}
        void Loaded(byte[] metadata) {if(Tier.ValidateOnLoad&&UsingGpu) Validate(true);}
        void Awake()
        {
            physical=new MaterialPropertyBlock();internalView=new MaterialPropertyBlock();var staticLabels=new System.Collections.Generic.List<TextMesh>();
            foreach(var label in Lab.GetComponentsInChildren<TextMesh>(true)) if(label.name=="Physical Label"||label.name=="Internal Label"||label.name=="Reciprocal Label") staticLabels.Add(label);
            labels=staticLabels.ToArray();Configure(false);
        }
        public void SetTier(int index)
        {
            if(index<0||index>=Tiers.Length) throw new ArgumentOutOfRangeException(nameof(index));TierIndex=index;Configure(true);
        }
        public void SetFallback(bool force) {ForceFallback=force;Release();lastModel=null;lastRevision=-1;Configure(true);}
        void Configure(bool rebuild)
        {
            if(Tiers==null||Tiers.Length!=4||Tier==null) throw new InvalidOperationException("four preview tier assets required");
            Lab.Settings.MaximumRenderedEdges=Tier.EdgeBudget;Lab.View.DisplayPointBudget=Tier.PointBudget;Lab.View.DisplayInternal=Tier.InternalView;Lab.View.PointSize=Tier.PointSize;
            // Fallback is discrete, not an allocation-heavy CPU fade loop.
            Lab.View.FadeSeconds=0;foreach(var label in labels) label.gameObject.SetActive(Tier.Labels);
            var reciprocal=Lab.GetComponent<ReciprocalInstrument>();if(reciprocal!=null) reciprocal.ShowContributionLabel=Tier.Labels;
            bool supported=!ForceFallback&&SystemInfo.supportsComputeShaders&&SystemInfo.supportsInstancing&&SystemInfo.supportsIndirectArgumentsBuffer&&Classifier!=null&&GpuMaterial!=null&&GpuMaterial.shader.isSupported;
            Lab.View.ExternalPointDrawing=supported;Backend=supported?"GPU compute / compact indirect points":"CPU_INSTANCED_FALLBACK (bounded, C# double)";
            if(!supported) {Release();Mode=PrecisionMode.CPU_VALIDATED;}
            if(rebuild&&Lab.State!=null) Lab.View.Rebuild(Lab.State);
        }
        void Rebuilt()
        {
            if(configuring||Lab.State==null) return;var model=Lab.State.Model;bool newData=lastModel==null||lastModel.Dataset!=model.Dataset;
            if(!Lab.View.ExternalPointDrawing)
            {
                if(newData||lastModel!=model||lastRevision!=model.Revision) {Comparison=null;DispatchSubmissionMilliseconds=0;Message="CPU fallback uses current C# double scientific state; GPU comparison unavailable.";}
                if(model.Active.Length>FallbackCandidateLimit) {Lab.View.ExternalPointDrawing=true;Backend="CPU fallback refused: candidate budget exceeded (no truncated scientific set)";throw new InvalidOperationException(Backend);}
                lastModel=model;lastRevision=model.Revision;return;
            }
            try
            {
                if(newData)
                {
                    Release();candidates=new PreviewCandidate[model.Active.Length];
                    for(int i=0;i<candidates.Length;i++) {var p=model.Dataset.Candidates[i];candidates[i]=new PreviewCandidate {Physical=new Vector4((float)p.Physical.X,(float)p.Physical.Y,(float)p.Physical.Z,0),Uv=new Vector2((float)p.Internal.X,(float)p.Internal.Y),Allowed=1};}
                    var w=new Double2[model.Dataset.DoubleWindow.Count];for(int i=0;i<w.Length;i++) w[i]=model.Dataset.DoubleWindow[i];buffers=new GpuPointBuffers(Classifier,candidates,w,Lab.View.PointMesh);
                    ownedMaterial=new Material(GpuMaterial) {enableInstancing=true};Bind(physical,buffers.PhysicalRows);Bind(internalView,buffers.InternalRows);
                }
                if(newData||lastModel!=model||lastRevision!=model.Revision)
                {
                    for(int i=0;i<candidates.Length;i++) candidates[i].Allowed=model.Filter.Contains(model.Dataset,model.Dataset.Candidates[i].Id)?1u:0u;
                    buffers.UpdateCandidates(candidates);var timer=Stopwatch.StartNew();buffers.Dispatch(model.Phi,model.Dataset.Metadata.PreviewEpsilon,model.Dataset.Metadata.IncludeBoundary,DrawStride,Lab.View.ShowCandidates,Tier.BoundaryPoints,Lab.State.SelectedRow);timer.Stop();DispatchSubmissionMilliseconds=timer.Elapsed.TotalMilliseconds;
                    Mode=PrecisionMode.GPU_PREVIEW;Comparison=null;changedAt=Time.unscaledTime;Message="GPU float ambiguity is unverified until Validate; CPU scientific state remains authoritative.";
                }
                else buffers.Compact(DrawStride,Lab.View.ShowCandidates,Tier.BoundaryPoints,Lab.State.SelectedRow);
                lastModel=model;lastRevision=model.Revision;Properties(physical,false);Properties(internalView,true);
            }
            catch(Exception e)
            {
                Release();ForceFallback=true;Message="GPU unavailable: "+e.Message;configuring=true;try {Configure(false);if(model.Active.Length<=FallbackCandidateLimit) Lab.View.Rebuild(Lab.State);else throw new InvalidOperationException("CPU fallback budget exceeded; dataset refused",e);}finally {configuring=false;}
            }
        }
        void Bind(MaterialPropertyBlock p,GraphicsBuffer rows) {p.SetBuffer("_Candidates",buffers.Candidates);p.SetBuffer("_Rows",rows);p.SetBuffer("_States",buffers.States);p.SetBuffer("_Active",buffers.Active);p.SetBuffer("_Previous",buffers.Previous);}
        void Properties(MaterialPropertyBlock p,bool internalSpace)
        {
            var view=Lab.View;var scales=Lab.State.Dataset.Metadata.DisplayScales;
            p.SetVector("_Origin",internalSpace?view.InternalOrigin.position:view.PhysicalOrigin.position);p.SetVector("_Scales",new Vector4((float)scales.X*view.PhysicalScale,(float)scales.X*view.PhysicalScale,(float)scales.Y*view.PhysicalScale*view.LayerSeparation,view.InternalScale));
            p.SetVector("_Phi",new Vector4((float)Lab.State.Phi.X,(float)Lab.State.Phi.Y,0,0));p.SetInt("_Internal",internalSpace?1:0);p.SetInt("_Context",view.ShowCandidates?1:0);p.SetInt("_BoundaryPoints",Tier.BoundaryPoints?1:0);p.SetInt("_SelectedRow",Lab.State.SelectedRow);
            p.SetFloat("_Radius",Lab.Settings.PointRadius*view.PointSize*2);p.SetColor("_SelectedColor",Lab.Settings.SelectedColor);p.SetColor("_RejectedColor",Lab.Settings.RejectedColor);p.SetColor("_BoundaryColor",Lab.Settings.BoundaryColor);
        }
        public void Validate(bool replace)
        {
            if(!UsingGpu) {Mode=PrecisionMode.CPU_VALIDATED;Message="CPU fallback: scientific C# double state; no GPU comparison available.";return;}
            var model=Lab.State.Model;int revision=model.Revision;Double2 phi=model.Phi;var cpuTimer=Stopwatch.StartNew();var cpu=Membership.Evaluate(model.Dataset,phi);cpuTimer.Stop();var allowed=new bool[cpu.Length];for(int i=0;i<allowed.Length;i++) allowed[i]=model.Filter.Contains(model.Dataset,model.Dataset.Candidates[i].Id);
            // Re-run float preview even if a preceding action uploaded CPU/fixture masks.
            // Otherwise a second Validate would misleadingly compare CPU to itself.
            buffers.Dispatch(phi,model.Dataset.Metadata.PreviewEpsilon,model.Dataset.Metadata.IncludeBoundary,DrawStride,Lab.View.ShowCandidates,Tier.BoundaryPoints,Lab.State.SelectedRow);Mode=PrecisionMode.GPU_PREVIEW;
            var s=new uint[cpu.Length];var a=new uint[cpu.Length];var readTimer=Stopwatch.StartNew();buffers.Read(s,a);readTimer.Stop();
            if(model!=Lab.State.Model||revision!=model.Revision) throw new InvalidOperationException("validation snapshot changed; discarded");
            Comparison=PreviewContract.Compare(model.Dataset,phi,cpu,allowed,s,a);Comparison.CpuMembershipMilliseconds=cpuTimer.Elapsed.TotalMilliseconds;Comparison.ReadbackMilliseconds=readTimer.Elapsed.TotalMilliseconds;
            if(replace) {buffers.UploadAuthority(cpu,model.Active);buffers.Compact(DrawStride,Lab.View.ShowCandidates,Tier.BoundaryPoints,Lab.State.SelectedRow);Mode=PrecisionMode.CPU_VALIDATED;}else if(Comparison.DisagreementIds.Length==0) Mode=PrecisionMode.CPU_VALIDATED;
            Message=$"Checked phi: GPU−CPU selected {Comparison.SelectedCountDifference}; boundary GPU {Comparison.GpuBoundary} / CPU {Comparison.CpuBoundary}; disagreements {Comparison.DisagreementIds.Length}, max |double d|={Comparison.MaximumAuthoritativeDistanceMagnitude:G5}. "+(replace?"CPU state replaced preview.":"Preview not replaced.");
        }
        public void UsePythonFixture()
        {
            var model=Lab.State?.Model;if(model==null||model.Dataset.Metadata.BinarySha256!="2942d45e5d6bb1c171c2cbf535943217a4c4a7e5c3355a1fa9828af5ec2dd2e8"||model.Phi.X!=model.Dataset.Metadata.Phi0.X||model.Phi.Y!=model.Dataset.Metadata.Phi0.Y||model.Filter.CropEnabled||model.Filter.SliceEnabled) throw new InvalidOperationException("PYTHON_FIXTURE requires immutable golden checksum, phi0 and no subset");
            for(int i=0;i<model.Active.Length;i++) if(model.Active[i]!=model.Dataset.Selected[i]||(model.States[i]==PointState.Boundary)!=model.Dataset.Boundary[i]) throw new InvalidOperationException("fixture mask disagreement");
            if(UsingGpu) {buffers.UploadAuthority(model.States,model.Active);buffers.Compact(DrawStride,Lab.View.ShowCandidates,Tier.BoundaryPoints,Lab.State.SelectedRow);}Mode=PrecisionMode.PYTHON_FIXTURE;Message="Checksum-verified immutable Python fixture masks at phi0; not a GPU parity claim.";
        }
        static readonly int FadeId=Shader.PropertyToID("_Fade");
        void LateUpdate()
        {
            if(!UsingGpu) return;long before=GC.GetAllocatedBytesForCurrentThread();float fade=Mathf.Clamp01((Time.unscaledTime-changedAt)/.4f);physical.SetFloat(FadeId,fade);
            if(Lab.View.PresentationPhysical) Graphics.DrawMeshInstancedIndirect(Lab.View.PointMesh,0,ownedMaterial,bounds,buffers.PhysicalArgs,0,physical,ShadowCastingMode.Off,false,gameObject.layer);
            if(Tier.InternalView&&Lab.View.PresentationInternal) Graphics.DrawMeshInstancedIndirect(Lab.View.PointMesh,0,ownedMaterial,bounds,buffers.InternalArgs,0,internalView,ShadowCastingMode.Off,false,gameObject.layer);
            OwnedDrawAllocatedBytes=Math.Max(OwnedDrawAllocatedBytes,GC.GetAllocatedBytesForCurrentThread()-before);
        }
        void Release() {buffers?.Dispose();buffers=null;if(ownedMaterial!=null) {if(Application.isPlaying) Destroy(ownedMaterial);else DestroyImmediate(ownedMaterial);}ownedMaterial=null;}
    }
}
