using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace XRSuperspace.Editor
{
    [InitializeOnLoad]
    public static class GpuSmoke
    {
        const string Key="XRSuperspace.GpuSmoke";
        static GpuPreview preview;
        static LinkedInstrument instrument;
        static PreviewProfiler profiler;
        static RenderTexture target;
        static int phase,wait,objects;
        static double deadline;
        static double sampleUntil;
        static readonly JObject evidence=new();
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../results"));
        static LabController Lab=>preview.Lab;
        static GpuSmoke() {if(SessionState.GetBool(Key,false)) Attach();}
        public static void BatchRun()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) throw new InvalidOperationException("real GPU required");EditorSceneManager.OpenScene(GpuSetup.ScenePath,OpenSceneMode.Single);SessionState.SetBool(Key,true);Attach();EditorApplication.EnterPlaymode();
        }
        static void Attach() {deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        static void Check(bool value,string label) {if(!value) throw new InvalidOperationException(label);}
        static void Click(string name)=>preview.GetComponentsInChildren<Button>(true).Single(b=>b.name==name).onClick.Invoke();
        static void Capture(string name)
        {
            UnityEngine.Object.FindFirstObjectByType<PreviewControls>().Refresh();instrument.UI.Refresh();var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try {image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());}finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
        }
        static void LinkedRegression()
        {
            // Exercise instrument behavior under the GPU renderer with isolated output files.
            instrument.Preset(3);Check(Lab.State.Model.Journal.Count==2&&Lab.State.Model.Journal[1].Entered.Length==4,"event preset regression");
            string before=instrument.JournalJson();var session=instrument.Capture("2026-10-03T00:00:00.0000000+00:00");string json=InstrumentSession.Encode(session);instrument.Display(.25f,.1f);Check(instrument.JournalJson()==before,"display altered journal");instrument.ApplySession(InstrumentSession.Decode(json));Check(InstrumentSession.Encode(instrument.Capture(session.TimestampUtc))==json,"optimized session roundtrip");
            instrument.Record();Lab.SetPhi(-.119,.047);Lab.SetPhi(0,0);instrument.Record();Check(instrument.Path.Count==3,"optimized path recording");evidence["linked_events_display_session_path_regression"]=true;
        }
        static void SyntheticGpuCases()
        {
            Double2[] window={new(-1,-1),new(1,-1),new(1,1),new(-1,1)};var candidates=new[]{new PreviewCandidate {Uv=Vector2.zero,Allowed=1},new PreviewCandidate {Uv=new Vector2(1,0),Allowed=1},new PreviewCandidate {Uv=new Vector2(2,0),Allowed=1},new PreviewCandidate {Uv=new Vector2(1-5e-7f,0),Allowed=1}};
            using(var buffers=new GpuPointBuffers(preview.Classifier,candidates,window,Lab.View.PointMesh))
            {
                var states=new uint[4];var active=new uint[4];buffers.Dispatch(new Double2(0,0),1e-6,false,1,false,true);buffers.Read(states,active);Check(states.SequenceEqual(new uint[]{1,2,0,2})&&active.SequenceEqual(new uint[]{1,0,0,0}),"synthetic boundary/epsilon classification");
                evidence["synthetic_states"]=new JArray(states);evidence["synthetic_near_boundary_cpu_selected_gpu_boundary"]=Membership.Classify(Membership.Distance(window,new Double2(candidates[3].Uv.x,0)),1e-10)==PointState.Selected;
                buffers.Dispatch(new Double2(100,100),1e-6,false,1,false,false);buffers.Read(states,active);Check(active.All(a=>a==0)&&states.All(s=>s==0),"empty GPU set");
                bool malformed=false;try {buffers.Read(new uint[3],active);}catch(ArgumentException) {malformed=true;}Check(malformed,"malformed buffer count accepted");
            }
            Double2[] wide={new(-10,-10),new(10,-10),new(10,10),new(-10,10)};
            using(var buffers=new GpuPointBuffers(preview.Classifier,candidates,wide,Lab.View.PointMesh)) {var s=new uint[4];var a=new uint[4];buffers.Dispatch(new Double2(0,0),1e-6,false,1,false,false);buffers.Read(s,a);Check(s.All(x=>x==1)&&a.All(x=>x==1),"all-inside GPU set");}
            evidence["synthetic_empty_all_inside_malformed"]=true;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("GPU smoke exceeded 240 s");if(!EditorApplication.isPlaying||EditorApplication.isCompiling||wait-->0) return;
                if(phase==0)
                {
                    preview=UnityEngine.Object.FindFirstObjectByType<GpuPreview>();if(preview==null||preview.Lab.State==null||preview.Lab.Busy) return;
                    instrument=preview.GetComponent<LinkedInstrument>();profiler=preview.GetComponent<PreviewProfiler>();if(!instrument.Reciprocal.Current) return;
                    Check(preview.UsingGpu,"GPU backend unavailable: "+preview.Message);Check(Lab.State.Dataset.Candidates.Count==31,"golden load");
                    objects=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;target=new RenderTexture(2300,1700,24);target.Create();instrument.Camera.targetTexture=target;
                    // Wider camera includes the diagnostic status and original control panel.
                    instrument.Camera.fieldOfView=49;SyntheticGpuCases();Click("Validate GPU");Check(preview.Comparison.DisagreementIds.Length==0,"golden GPU disagreement");evidence["golden"]=JObject.FromObject(preview.Comparison);Click("Python fixture");Check(preview.Mode==PrecisionMode.PYTHON_FIXTURE,"fixture visible mode");phase=1;wait=30;return;
                }
                if(phase==1)
                {
                    Capture("gpu_fixture.png");var cases=new JArray();
                    foreach(var phi in new[]{new Double2(.25,.25),new Double2(-.25,-.25),new Double2(.25,-.25),new Double2(-.25,.25),new Double2(.181,.047)})
                    {
                        Lab.SetPhi(phi.X,phi.Y);Check(preview.Mode==PrecisionMode.GPU_PREVIEW,"changed phi did not invalidate precision");preview.Validate(false);Check(preview.Comparison.DisagreementIds.Length==0,"golden phason extreme disagreement");cases.Add(JObject.FromObject(preview.Comparison));
                    }
                    evidence["golden_extremes"]=cases;Lab.SetPhi(1e-7,0);preview.Validate(false);Check(preview.Mode==PrecisionMode.GPU_PREVIEW&&preview.Comparison.DisagreementIds.Length>0,"near-boundary discrepancy not exposed");Check(preview.Comparison.MaximumAuthoritativeDistanceMagnitude<2e-6,"unexpected far-boundary disagreement");evidence["near_boundary"]=JObject.FromObject(preview.Comparison);phase=2;wait=25;return;
                }
                if(phase==2)
                {
                    Capture("gpu_ambiguity.png");Click("Replace with CPU");Check(preview.Mode==PrecisionMode.CPU_VALIDATED,"replace precision mode");preview.Validate(false);Check(preview.Comparison.DisagreementIds.Length==8&&preview.Mode==PrecisionMode.GPU_PREVIEW,"repeat Validate compared CPU to itself");preview.Validate(true);evidence["replace_cpu_and_repeat_float_validation"]=true;LinkedRegression();instrument.Camera.fieldOfView=49;instrument.Camera.transform.position=new Vector3(0,.8f,-4.6f);Lab.SetPhi(0,0);Lab.NextDataset();phase=3;wait=30;return;
                }
                if(phase==3)
                {
                    if(Lab.Busy||Lab.State.Dataset.Candidates.Count!=981) return;Lab.NextDataset();phase=4;wait=30;return;
                }
                if(phase==4)
                {
                    if(Lab.Busy||Lab.State.Dataset.Candidates.Count!=10813||!instrument.Reciprocal.Current) return;
                    Check(preview.UsingGpu&&Lab.View.BatchCount==0,"medium GPU still makes CPU batches");Check(Lab.State.SelectedCount==5483&&Lab.State.BoundaryCount==340,"medium authority masks");
                    Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objects,"per-point GameObjects created");preview.Validate(false);Check(preview.Comparison.DisagreementIds.Length==0,"medium phi0 GPU disagreement");
                    string key=Lab.State.Model.Key("");int journal=Lab.State.Model.Journal.Count;var point=Lab.State.Model.Position(10);
                    for(int i=0;i<4;i++) {preview.SetTier(i);Check(Lab.State.Model.Key("")==key&&Lab.State.Model.Journal.Count==journal&&Lab.State.Model.Position(10).X==point.X,"LOD changed scientific data");}
                    Click("Desktop");evidence["medium_no_per_point_objects"]=true;evidence["gameobject_transform_count"]=objects;evidence["lod_preserves_scientific_state"]=true;
                    profiler.ResetSamples();sampleUntil=EditorApplication.timeSinceStartup+5;phase=5;wait=200;return;
                }
                if(phase==5)
                {
                    if(EditorApplication.timeSinceStartup<sampleUntil) return;
                    Check(preview.OwnedDrawAllocatedBytes==0,"GPU steady draw allocated managed memory");evidence["desktop_profile"]=JObject.FromObject(profiler.Snapshot());Capture("gpu_medium_gpu.png");
                    Click("Quest");Lab.Select(10);Check(Lab.View.PickNearest(Lab.View.PointPosition(10,false),false,1e-5f)==10,"LOD hides selected exact ID");profiler.ResetSamples();sampleUntil=EditorApplication.timeSinceStartup+5;phase=6;wait=180;return;
                }
                if(phase==6)
                {
                    if(EditorApplication.timeSinceStartup<sampleUntil) return;
                    Check(preview.VisibleCandidateBudget<=4096&&!preview.Tier.InternalView,"Quest display budget");evidence["quest_tier_editor_profile_not_device"]=JObject.FromObject(profiler.Snapshot());Capture("gpu_quest_tier_editor.png");Click("Desktop");Click("Force fallback");
                    Check(!preview.UsingGpu&&!Lab.View.ExternalPointDrawing&&Lab.View.BatchCount>0,"CPU instanced fallback not active");Check(Lab.State.SelectedCount==5483,"fallback changed membership");Lab.SetPhi(.181,.047);var cpu=Membership.Evaluate(Lab.State.Dataset,Lab.State.Phi);Check(cpu.SequenceEqual(Lab.State.States),"fallback precision mismatch");profiler.ResetSamples();sampleUntil=EditorApplication.timeSinceStartup+5;phase=7;wait=50;return;
                }
                if(phase==7)
                {
                    if(EditorApplication.timeSinceStartup<sampleUntil) return;
                    Capture("gpu_medium_fallback.png");evidence["fallback"]=JObject.FromObject(profiler.Snapshot());evidence["forced_fallback_correct"]=true;
                    Click("Force fallback");Check(preview.UsingGpu&&Lab.View.BatchCount==0,"GPU recovery");preview.Validate(true);Check(preview.Mode==PrecisionMode.CPU_VALIDATED,"recovery CPU validation");evidence["gpu_recovered"]=true;evidence["passed"]=true;evidence["builds"]= "NOT RUN — user preference; Editor only";Finish(0);
                }
            }
            catch(Exception e) {evidence["passed"]=false;evidence["error"]=e.ToString();Debug.LogError("[XRSuperspace] GPU smoke: "+e.Message);Finish(1);}
        }
        static void Finish(int code)
        {
            File.WriteAllText(Path.Combine(Output,"gpu_graphics_smoke.json"),evidence.ToString());SessionState.SetBool(Key,false);EditorApplication.update-=Tick;if(instrument!=null) instrument.Camera.targetTexture=null;if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}Debug.Log("[XRSuperspace] GPU graphics exit="+code+"; no Quest/device validation");EditorApplication.Exit(code);
        }
    }
}
