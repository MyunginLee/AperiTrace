using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace XRSuperspace.Editor
{
    [InitializeOnLoad]
    public static class InstrumentSmoke
    {
        const string Key="XRSuperspace.InstrumentSmoke";
        static int phase,wait,preset;
        static double deadline;
        static double captureAfter;
        static LinkedInstrument instrument;
        static RenderTexture target;
        static string journal;
        static InstrumentSession saved;
        static LatticeId selected;
        static int objectCount;
        static Mouse mouse;
        static InputSettings originalSettings,testSettings;
        static readonly JObject evidence=new JObject();
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../results"));
        static LabController Lab=>instrument.Lab;
        static ReciprocalInstrument Reciprocal=>instrument.Reciprocal;
        static InstrumentSmoke() {if(SessionState.GetBool(Key,false)) Attach();}
        public static void BatchRun()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) throw new InvalidOperationException("Instrument graphics requires a real device");
            EditorSceneManager.OpenScene(InstrumentSetup.ScenePath,OpenSceneMode.Single);SessionState.SetBool(Key,true);Attach();EditorApplication.EnterPlaymode();
        }
        static void Attach() {deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        static void Check(bool success,string message) {if(!success) throw new InvalidOperationException(message);}
        static Button Button(string name)=>instrument.UI.GetComponentsInChildren<Button>().Single(b=>b.name==name);
        static void Click(string name)=>Button(name).onClick.Invoke();
        static void Field(string name,string value)=>instrument.UI.GetComponentsInChildren<InputField>().Single(f=>f.name==name).text=value;
        static void Capture(string name)
        {
            captureAfter=0;
            instrument.UI.Refresh();var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);try {image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());}finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        static bool ReadyToCapture() {if(captureAfter==0) {instrument.UI.Refresh();captureAfter=EditorApplication.timeSinceStartup+.3;return false;}return EditorApplication.timeSinceStartup>=captureAfter;}
        static void MouseInput(MouseState state) {InputSystem.QueueStateEvent(mouse,state);InputSystem.Update();Lab.GetComponent<DesktopInput>().SendMessage("Update");}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Instrument graphics exceeded 180 s");if(!EditorApplication.isPlaying||EditorApplication.isCompiling||wait-->0) return;
                if(phase==0)
                {
                    instrument=UnityEngine.Object.FindFirstObjectByType<LinkedInstrument>();if(instrument==null||Lab.Busy||Lab.State==null||!Reciprocal.Current) return;
                    Check(Lab.State.Dataset.Candidates.Count==31&&Reciprocal.Peaks.Length==381,"checksum-linked golden load");
                    target=new RenderTexture(2000,1400,24);target.Create();instrument.Camera.targetTexture=target;
                    originalSettings=InputSystem.settings;testSettings=UnityEngine.Object.Instantiate(originalSettings);testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=testSettings;mouse=InputSystem.AddDevice<Mouse>();
                    objectCount=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;evidence["graphics_device"]=SystemInfo.graphicsDeviceName;evidence["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();evidence["golden_checksum"]=Lab.State.Dataset.Metadata.BinarySha256;phase=1;wait=20;return;
                }
                if(phase==1)
                {
                    Click("3. Selected n across all views");Field("Exact n","-1,0,-1,0,0");Click("Find exact n");selected=Lab.State.Selection.Value;Check(selected.ToString()=="-1,0,-1,0,0","exact tuple search");
                    var point=Lab.View.PointPosition(Lab.State.SelectedRow,true);int row=Lab.View.PickNearest(point,true,1e-4f);Lab.Select(row);Check(Lab.State.Selection.Value.Equals(selected),"internal linked identity");point=Lab.View.PointPosition(row,false);row=Lab.View.PickNearest(point,false,1e-4f);Lab.Select(row);Check(Lab.State.Selection.Value.Equals(selected),"physical linked identity");
                    Field("Peak row","0");Click("Select peak");Check(Reciprocal.Inspector().Contains("n="+selected)&&Reciprocal.Inspector().Contains("h contributors"),"reciprocal point context distinct from peak h");
                    var positions=new Vector3[Lab.View.WindowLine.positionCount];Lab.View.WindowLine.GetPositions(positions);evidence["fixed_window_before"]=new JArray(positions.Select(p=>new JArray(p.x,p.y,p.z)));
                    Click("Record / stop");Lab.SetPhi(.181,.047);Check(!Reciprocal.Current,"stale immediately on phi mutation");Check(Lab.State.Model.Journal.Last().Entered.Length==4&&Lab.State.SelectedCount==15,"events logged without waiting for fade");
                    int entered=Lab.State.Dataset.Candidates.Select((p,i)=>(p,i)).First(pair=>pair.p.Id.ToString()==Lab.State.Model.Journal.Last().Entered[0]).i;Check(Lab.View.FadeAt(entered,Time.unscaledTime)<.1f,"entered alpha starts before completion");
                    for(int i=0;i<positions.Length;i++) Check(positions[i]==Lab.View.WindowLine.GetPosition(i),"wrong phason window sign");Check(Lab.View.PointPosition(row,false)==point,"phi moved physical scientific point");
                    Lab.SetPhi(-.119,.047);Check(Lab.State.Model.Journal.Last().Exited.Length==4,"exit IDs not logged immediately");Check(Lab.State.Edges.All(e=>Lab.State.Model.Active[e.Source]&&Lab.State.Model.Active[e.Target]),"exit fade kept invalid graph edge");Lab.SetPhi(0,0);Click("Record / stop");Check(instrument.Path.Count==4,"exact path recording");Click("Play / stop");evidence["linked_exact_id"]=selected.ToString();evidence["fixed_window_positive_phason"]=true;evidence["instant_events_and_entry_alpha"]=true;phase=2;wait=20;return;
                }
                if(phase==2)
                {
                    if(instrument.Playing||!Reciprocal.Current) return;Check(Lab.State.Phi.X==0&&Lab.State.Phi.Y==0,"ordered path playback endpoint");evidence["path_record_playback"]=true;
                    // Exercise a real pixel-space handle drag, then exact numeric entry.
                    MouseInput(new MouseState {position=instrument.Camera.WorldToScreenPoint(Lab.PhasonHandle.position)}.WithButton(MouseButton.Left));
                    MouseInput(new MouseState {position=instrument.Camera.WorldToScreenPoint(Lab.PhasonHandle.position+new Vector3(.181f,.047f,0)*Lab.View.InternalScale)}.WithButton(MouseButton.Left));MouseInput(new MouseState());
                    Check(Math.Abs(Lab.State.Phi.X-.181)<.02&&Math.Abs(Lab.State.Phi.Y-.047)<.02,"two-axis pointer drag");Field("Phi x","0.181");Field("Phi y","0.047");Click("Set phi");Check(Lab.State.SelectedCount==15,"numeric phason");journal=instrument.JournalJson();saved=instrument.Capture("2026-10-03T00:00:00.0000000+00:00");string json=InstrumentSession.Encode(saved);File.WriteAllText(Path.Combine(Output,"instrument_session.json"),json);
                    Click("Layer +");Click("Size +");Check(instrument.JournalJson()==journal,"display transforms changed event log");Lab.SetPhi(0,0);instrument.ApplySession(InstrumentSession.Decode(json));Check(InstrumentSession.Encode(instrument.Capture(saved.TimestampUtc))==json,"session did not roundtrip camera/layout/path/model exactly");
                    var wrong=InstrumentSession.Decode(json);wrong.DatasetChecksum=new string('0',64);string before=Lab.State.Model.Key(Reciprocal.Config);bool refused=false;try {instrument.LoadJson(InstrumentSession.Encode(wrong));}catch(InvalidOperationException e) {refused=e.Message.Contains("CHECKSUM MISMATCH");}Check(refused&&Lab.State.Model.Key(Reciprocal.Config)==before,"wrong checksum silently applied");Click("Override checksum");Check(instrument.Message.Contains("Explicit checksum override"),"override missing");instrument.ApplySession(saved);Click("Save session");Check(File.Exists(instrument.LastSessionPath),"Save UI wrote no session");Click("Load latest");Check(instrument.Message.Contains("matching dataset"),"Load UI roundtrip");
                    evidence["numeric_entry_and_synthetic_handle"]=true;evidence["session_roundtrip"]=true;evidence["checksum_refusal_no_mutation_and_explicit_override"]=true;evidence["local_session_file"]=instrument.LastSessionPath;journal=instrument.JournalJson();phase=3;wait=30;return;
                }
                if(phase==3)
                {
                    if(!Reciprocal.Current||!ReadyToCapture()) return;Check(Reciprocal.Status.StartsWith("CURRENT"),"current result text remained stale");Check(instrument.JournalJson()==journal,"animation modified deterministic log");evidence["animations_do_not_modify_events"]=true;evidence["stale_to_current"]=true;Capture("instrument_linked.png");
                    Field("Crop radius","0.6");Field("Crop z","0.9");Field("Slice z","0");Field("Slice width","0.01");Click("Toggle crop");Click("Toggle slice");Check(Lab.State.Model.Filter.CropEnabled&&Lab.State.Model.Filter.SliceEnabled&&Lab.State.SelectedCount<15,"scientific subset controls");Check(Lab.State.Edges.All(e=>Lab.State.Model.Active[e.Source]&&Lab.State.Model.Active[e.Target]),"graph endpoints outside subset");Check(!Reciprocal.Current,"subset diffraction not stale");phase=4;wait=25;return;
                }
                if(phase==4)
                {
                    if(!Reciprocal.Current||!ReadyToCapture()) return;evidence["crop_slice_graph_consistency"]=true;Capture("instrument_subset.png");
                    preset=0;Click("1. Parent → physical overview");phase=5;wait=30;return;
                }
                if(phase==5)
                {
                    if(!Reciprocal.Current||!ReadyToCapture()) return;var p=instrument.Presets[preset];Check(instrument.Message=="Deterministic preset: "+p.Title,"preset refused rather than applied");Check(Lab.State.Phi.X==p.PhiX&&Lab.State.Phi.Y==p.PhiY&&Lab.View.ShowCandidates==p.ShowCandidates&&instrument.Camera.transform.position==p.CameraPosition,"preset model/camera differs from saved definition");Capture("instrument_preset_"+(preset+1)+".png");
                    if(preset==3) Check(Lab.State.Model.Journal.Count==2&&Lab.State.Model.Journal[1].Entered.Length==4,"event preset lacks reproducible baseline and entry");
                    preset++;if(preset<5) {instrument.Preset(preset);wait=30;return;}evidence["five_deterministic_capture_presets"]=true;Click("Dataset");phase=6;wait=30;return;
                }
                if(phase==6)
                {
                    if(Lab.Busy||!Reciprocal.Current||!ReadyToCapture()) return;Check(Lab.State.Dataset.Candidates.Count==981&&Lab.State.SelectedCount==451,"full small load/calculation");Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objectCount,"per-point GameObjects created");
                    double maximum=0;for(int i=0;i<Reciprocal.Peaks.Length;i++) maximum=Math.Max(maximum,Math.Abs(Reciprocal.Values[i][3]-Reciprocal.Peaks[i].Reference[3]));Check(maximum<1e-12,"runtime small Python parity");evidence["small_checksum"]=Lab.State.Dataset.Metadata.BinarySha256;evidence["small_selected"]=451;evidence["python_normalized_max_abs_error"]=maximum;evidence["bounded_gameobjects"]=true;Capture("instrument_small.png");File.WriteAllText(Path.Combine(Output,"instrument_events.json"),instrument.JournalJson());evidence["passed"]=true;evidence["device_validation"]="not performed";Finish(0);
                }
            }
            catch(Exception e) {evidence["passed"]=false;evidence["error"]=e.ToString();Debug.LogError("[XRSuperspace] Instrument smoke: "+e.Message);Finish(1);}
        }
        static void Finish(int code)
        {
            File.WriteAllText(Path.Combine(Output,"instrument_graphics_smoke.json"),evidence.ToString());SessionState.SetBool(Key,false);EditorApplication.update-=Tick;if(instrument!=null) instrument.Camera.targetTexture=null;if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}if(mouse!=null) InputSystem.RemoveDevice(mouse);if(originalSettings!=null) InputSystem.settings=originalSettings;if(testSettings!=null) UnityEngine.Object.DestroyImmediate(testSettings);Debug.Log("[XRSuperspace] Instrument graphics exit="+code+"; no device validation");EditorApplication.Exit(code);
        }
    }
}
