using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace XRSuperspace.Editor
{
    // Reads input data, not expected results. Never saves scene/assets.
    [InitializeOnLoad]
    public static class ExperimentBatch
    {
        const string SessionKey="XRSuperspace.Experiment.RunDirectory";
        static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        static string directory;
        static JObject config;
        static List<Dictionary<string,object>> tiers;
        static LabController lab;
        static GpuPreview preview;
        static PreviewProfiler profiler;
        static RenderTexture target;
        static int tierIndex, repetition, warmup, frame, lastFrame=-1;
        static bool prepared;
        static double deadline;
        static List<double> samples;
        static List<object> repetitions;

        static ExperimentBatch()
        {
            string saved=SessionState.GetString(SessionKey,"");
            if(saved.Length>0) {directory=saved;EditorApplication.delayCall+=Resume;}
        }
        public static string ContainedPath(string parent,string relative)
        {
            var root=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var resolved=Path.GetFullPath(Path.Combine(parent,relative));
            if(!resolved.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("experiment path escapes output directory");
            return resolved;
        }
        static string FileInRun(string relative)=>ContainedPath(directory,relative);
        static void Write(string relative,object value)
        {
            string path=FileInRun(relative);Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonConvert.SerializeObject(value,Formatting.Indented)+"\n");
        }
        static JObject ReadConfig()=>JObject.Parse(File.ReadAllText(FileInRun("suite_config.json")));
        public static double Percentile(IEnumerable<double> data,double p)
        {
            var a=data.OrderBy(x=>x).ToArray();
            if(a.Length==0||p<0||p>1||a.Any(x=>double.IsNaN(x)||double.IsInfinity(x)||x<0)) throw new ArgumentException("invalid timing samples");
            return a[(int)Math.Ceiling((a.Length-1)*p)];
        }
        static double[] Timings(Action operation,int warm,int count)
        {
            for(int i=0;i<warm;i++) operation();
            var a=new double[count];
            for(int i=0;i<count;i++) {var timer=Stopwatch.StartNew();operation();timer.Stop();a[i]=timer.Elapsed.TotalMilliseconds;}
            return a;
        }
        static void Basis()
        {
            var input=JObject.Parse(File.ReadAllText(FileInRun("E1_basis/vectors.json")));
            var vectors=(JArray)input["vectors"];var q=CanonicalMath.GenerateBasis();
            var projections=new List<double[]>();var reconstructions=new List<double[]>();
            foreach(JArray n in vectors)
            {
                var id=new LatticeId((int)n[0],(int)n[1],(int)n[2],(int)n[3],(int)n[4]);
                var projected=Enumerable.Range(0,5).Select(i=>CanonicalMath.Project(q,id,i)).ToArray();
                var recovered=new double[5];for(int j=0;j<5;j++) for(int i=0;i<5;i++) recovered[j]+=projected[i]*q[i,j];
                projections.Add(projected);reconstructions.Add(recovered);
            }
            Write("E1_basis/csharp.json",new {Q=Enumerable.Range(0,5).Select(i=>Enumerable.Range(0,5).Select(j=>q[i,j]).ToArray()).ToArray(),
                projected=projections,reconstruction=reconstructions,
                input_sha256=Qc5dParser.Sha256(File.ReadAllBytes(FileInRun("E1_basis/vectors.json"))),
                computation="Independent C5 equations; explicit double accumulation, exact input integer tuples"});
        }
        static void Parity(string output,string binary,JObject parity)
        {
            var d=Qc5dParser.Load(binary);
            Write(output+"/csharp_computed.json",ParityExport.CpuSnapshot(d,parity));
            Write(output+"/gpu_computed.json",ParityExport.GpuSnapshot(d,parity));
        }
        public static void Run()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-experiments-run-dir");
                if(i<0||i+1>=args.Length) throw new ArgumentException("-experiments-run-dir is required");
                string runs=Path.Combine(Root,"results/experiments");
                directory=ContainedPath(runs,args[i+1]);
                if(!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
                config=ReadConfig();Basis();
                var parity=JObject.Parse(File.ReadAllText(Path.Combine(Root,"configs/parity_v1.json")));
                Parity("E6_parity",Path.Combine(Root,"tests/fixtures/canonical_small_v1/canonical.qc5d"),parity);
                Parity("E8_reproducibility",FileInRun("E8_reproducibility/clean_A/model/model.qc5d"),parity);
                // Persist only this operation's path across domain reload, not global EditorPrefs.
                SessionState.SetString(SessionKey,directory);
                EditorSceneManager.OpenScene("Assets/XRSuperspace/Scenes/SuperspaceGpuLab.unity",OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch(Exception e) {Fail(e);}
        }
        static void Resume()
        {
            try
            {
                config=ReadConfig();tiers=new List<Dictionary<string,object>>();
                int warm=(int)config["E7"]["cpu_warmup"],count=(int)config["E7"]["cpu_repetitions"];
                foreach(var input in (JArray)config["E7"]["tiers"])
                {
                    string name=(string)input["name"],binary=FileInRun("E7_performance/"+name+"/model.qc5d");
                    RuntimeDataset d=null;var load=Timings(()=>d=Qc5dParser.Load(binary),warm,count);
                    PointState[] states=null;var membership=Timings(()=>states=Membership.Evaluate(d,new Double2(0,0)),warm,count);
                    string refusal=null;
                    try {PreviewContract.ValidateCounts(d.Candidates.Count,d.Candidates.Count,d.Candidates.Count);}
                    catch(ArgumentException e) {refusal=e.Message;}
                    tiers.Add(new Dictionary<string,object> {
                        ["name"]=name,["candidates"]=d.Candidates.Count,["selected"]=states.Count(x=>Membership.IsSelected(x,d.Metadata.IncludeBoundary)),
                        ["states"]=states.Select(x=>(int)x).ToArray(),["load_ms"]=load,["load_median_ms"]=Percentile(load,.5),
                        ["membership_ms"]=membership,["membership_median_ms"]=Percentile(membership,.5),
                        ["capacity_status"]=refusal==null?"ACCEPTED":"REFUSED_CONFIGURED_CAPACITY",
                        ["refusal_reason"]=refusal,["lod_index"]=(int)input["lod_index"],
                        ["frame_ms_p50"]=null,["frame_ms_p95"]=null,["frame_ms_p99"]=null,
                        ["render_status"]=refusal==null?"PENDING":"NOT RUN: no truncated scientific candidate set",
                        ["process_peak_working_set_bytes"]=Process.GetCurrentProcess().PeakWorkingSet64});
                }
                // Direct capacity contract boundary; no out-of-budget GPU allocation.
                PreviewContract.ValidateCounts(50000,50000,50000);
                bool refuses=false;try {PreviewContract.ValidateCounts(50001,50001,50001);}catch(ArgumentException){refuses=true;}
                Write("E7_performance/capacity_contract.json",new {accepted_count=50000,rejected_count=50001,rejected=refuses,scope="configured guard, not hardware throughput"});
                deadline=EditorApplication.timeSinceStartup+600;
                EditorApplication.update+=Tick;
            }
            catch(Exception e){Fail(e);}
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("PlayMode measurement exceeded 600 seconds");
                if(!EditorApplication.isPlaying) return;
                if(!prepared)
                {
                    lab=UnityEngine.Object.FindFirstObjectByType<LabController>();
                    if(lab==null||lab.State==null||lab.Busy) return;
                    preview=lab.GetComponent<GpuPreview>();profiler=lab.GetComponent<PreviewProfiler>();
                    foreach(var component in lab.GetComponents<MonoBehaviour>())
                        if(component is ReciprocalInstrument||component is LinkedInstrument||component is InstrumentUI||component is InstrumentOverlay||component is PreviewControls)
                        {component.enabled=false;component.StopAllCoroutines();}
                    foreach(var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled=false;
                    var settings=UnityEngine.Object.Instantiate(lab.Settings);settings.MaximumInteractiveCandidates=50000;
                    lab.Settings=settings;lab.View.Settings=settings;
                    var size=config["E7"]["target_resolution"];
                    target=new RenderTexture((int)size[0],(int)size[1],24);target.Create();
                    lab.GetComponent<LabXrMode>().DesktopCamera.targetTexture=target;
                    QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                    prepared=true;BeginTier();return;
                }
                if(lastFrame==Time.frameCount) return;lastFrame=Time.frameCount;
                if(warmup>0) {warmup--;if(warmup==0) profiler.ResetSamples();return;}
                samples.Add(Time.unscaledDeltaTime*1000.0);frame++;
                if(frame<(int)config["E7"]["render_frames"]) return;
                var snap=profiler.Snapshot();
                repetitions.Add(new {repetition,raw_frame_ms=samples.ToArray(),frame_ms_p50=Percentile(samples,.5),
                    frame_ms_p95=Percentile(samples,.95),frame_ms_p99=Percentile(samples,.99),profiler=snap});
                repetition++;
                if(repetition<(int)config["E7"]["render_repetitions"]) {BeginRepetition();return;}
                var all=repetitions.SelectMany(x=>JObject.FromObject(x)["raw_frame_ms"].ToObject<double[]>()).ToArray();
                var row=tiers[tierIndex];row["repetitions"]=repetitions;row["frame_ms_p50"]=Percentile(all,.5);
                row["frame_ms_p95"]=Percentile(all,.95);row["frame_ms_p99"]=Percentile(all,.99);
                row["desktop_p95_reference_met"]=Percentile(all,.95)<=(double)config["E7"]["frame_p95_reference_ms"];
                row["render_status"]="MEASURED_EDITOR_GPU";row["backend"]=preview.Backend;row["active_lod"]=preview.Tier.Title;
                row["display_stride"]=preview.DrawStride;row["buffer_bytes_estimate"]=preview.BufferBytes;
                row["process_peak_working_set_bytes"]=Process.GetCurrentProcess().PeakWorkingSet64;
                tierIndex++;BeginTier();
            }
            catch(Exception e){Fail(e);}
        }
        static void BeginTier()
        {
            while(tierIndex<tiers.Count&&(string)tiers[tierIndex]["capacity_status"]!="ACCEPTED") tierIndex++;
            if(tierIndex==tiers.Count) {Complete();return;}
            var row=tiers[tierIndex];string name=(string)row["name"];
            lab.SetDataset(File.ReadAllBytes(FileInRun("E7_performance/"+name+"/model.qc5d")),
                File.ReadAllBytes(FileInRun("E7_performance/"+name+"/metadata.json")),name);
            preview.SetTier((int)row["lod_index"]);
            if(!preview.UsingGpu) throw new NotSupportedException("Actual GPU renderer unavailable: "+preview.Backend);
            lab.View.ShowEdges=true;
            repetition=0;repetitions=new List<object>();BeginRepetition();
            Debug.Log("[XRSuperspace] Frame measurement: "+name+" candidates="+row["candidates"]);
        }
        static void BeginRepetition()
        {
            warmup=(int)config["E7"]["render_warmup_frames"];frame=0;lastFrame=Time.frameCount;
            samples=new List<double>();profiler.ResetSamples();
        }
        static void Complete()
        {
            Write("E7_performance/unity.json",new {tiers,environment="Unity Editor batch PlayMode; point/graph isolated workload",
                unity=Application.unityVersion,os=SystemInfo.operatingSystem,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,
                api=SystemInfo.graphicsDeviceType.ToString(),resolution=config["E7"]["target_resolution"],
                warmup_frames=config["E7"]["render_warmup_frames"],frames_per_repetition=config["E7"]["render_frames"],
                render_repetitions=config["E7"]["render_repetitions"],quantile="sorted samples[ceil((n-1)*p)]",
                quest="NOT RUN: deferred by user",gpu_duration="unavailable: dispatch submission is CPU time"});
            SessionState.EraseString(SessionKey);EditorApplication.update-=Tick;
            Debug.Log("[XRSuperspace] GPU and frame measurements exported.");
            EditorApplication.Exit(0);
        }
        static void Fail(Exception error)
        {
            SessionState.EraseString(SessionKey);EditorApplication.update-=Tick;
            if(directory!=null&&Directory.Exists(directory)) Write("unity_failure.json",new {error=error.ToString(),tiers});
            Debug.LogException(error);EditorApplication.Exit(1);
        }
    }
}
