using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace.Editor
{
    [InitializeOnLoad]
    public static class DemoSmoke
    {
        const string Key="XRSuperspace.DemoSmoke";
        static ScientificDemo demo;
        static double deadline;
        static int phase,step,preset;
        static bool requested,pendingAdvance;
        static readonly JObject evidence=new JObject();
        static string Output=>SessionState.GetString(Key+".output","");
        static DemoSmoke() {if(SessionState.GetBool(Key,false)) Attach();}
        public static void BatchRun()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) throw new InvalidOperationException("actual graphics device required");
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-demo-output");if(i<0||i+1>=args.Length) throw new ArgumentException("-demo-output required");
            string output=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(output);if(Directory.GetFiles(output,"*.png").Length>0||File.Exists(Path.Combine(output,"evidence.json"))) throw new IOException("demo output already exists");
            SessionState.SetString(Key+".output",output);EditorSceneManager.OpenScene(DemoSetup.ScenePath,OpenSceneMode.Single);
            SessionState.SetBool(Key,true);Attach();EditorApplication.EnterPlaymode();
        }
        static void Attach() {deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        static void Check(bool ok,string message) {if(!ok) throw new InvalidOperationException(message);}
        static bool Capture(string name,int width=1920,int height=1080)
        {
            if(!requested) {demo.Capture.Begin(Output,name,width,height);requested=true;return false;}
            if(demo.Capture.Busy) return false;Check(demo.Capture.LastError==null&&File.Exists(demo.Capture.LastImage),"capture failed: "+demo.Capture.LastError);requested=false;return true;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("demo smoke exceeded 600 s");if(!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
                if(phase==0)
                {
                    demo=UnityEngine.Object.FindFirstObjectByType<ScientificDemo>();if(demo==null||demo.Lab.Busy||demo.Lab.State==null||!demo.Instrument.Reciprocal.Current) return;
                    Check(demo.Guided&&demo.Step==0,"guided baseline missing");demo.FreeMode();demo.Instrument.Search("-1,0,-1,0,0");Check(demo.Lab.State.Selection.Value.ToString()=="-1,0,-1,0,0","exact tuple search");
                    var before=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config);demo.Instrument.Reciprocal.SetIntensityThreshold(.2);demo.SetPalette(true);Check(before==demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config),"display palette/threshold mutated science");
                    demo.Instrument.SetPhiText("0.181","0.047");demo.Instrument.SetFilterText("0.6","0.9","0","0.01",true,true);Check(demo.Lab.State.Model.Filter.CropEnabled&&demo.Lab.State.Model.Filter.SliceEnabled,"free crop/slice unavailable");
                    string session=demo.SessionJson();demo.Lab.SetPhi(0,0);demo.LoadSessionJson(session);Check(demo.Lab.State.Phi.X==.181&&demo.Lab.State.Model.Filter.SliceEnabled,"demo session failed roundtrip");
                    var wrong=JObject.Parse(session);wrong["instrument"]["DatasetChecksum"]=new string('0',64);before=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config);
                    demo.Guard(()=>demo.LoadSessionJson(wrong.ToString()));Check(before==demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config)&&demo.LastFailure.Contains("CHECKSUM MISMATCH"),"wrong checksum not visibly refused before mutation");
                    demo.SaveSession();Check(File.Exists(demo.LastSessionPath),"session save missing");demo.LoadLastSession();demo.ValidateCpu();demo.SetPalette(false);demo.StartGuide(false);
                    evidence["free_exact_parameters_and_session_roundtrip"]=true;evidence["checksum_refusal_no_mutation"]=true;evidence["palette_threshold_display_only"]=true;evidence["actual_saved_session"]=Path.GetFileName(demo.LastSessionPath);
                    evidence["gpu"]=SystemInfo.graphicsDeviceName;evidence["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();phase=1;return;
                }
                if(phase==1)
                {
                    if(demo.PathRunning||!demo.Instrument.Reciprocal.Current) return;
                    if(pendingAdvance) {Check(demo.Advance(),"guide advance refused after CURRENT result");pendingAdvance=false;return;}
                    Check(demo.Step==step&&demo.ProvenanceText().Contains("CHECKSUM MISMATCH"),"guide/failure display changed");
                    if(!Capture("guide_"+step.ToString("00"))) return;
                    if(step==7) {Check(demo.Lab.State.Model.Journal.SelectMany(f=>f.Entered).Any()&&demo.Lab.State.Model.Journal.SelectMany(f=>f.Exited).Any(),"fixed path does not demonstrate both entry and exit");evidence["entered_and_exited_exact_ids"]=true;}
                    if(step==7) {string saved=demo.SessionJson(),journal=demo.Instrument.JournalJson();demo.FreeMode();demo.Lab.SetPhi(0,0);demo.LoadSessionJson(saved);Check(demo.Instrument.JournalJson()==journal&&demo.Guided&&demo.Step==7,"guided session did not restore exact event journal");evidence["guided_event_journal_roundtrip"]=true;}
                    step++;if(step<10) {pendingAdvance=true;return;}
                    evidence["guided_sequence_steps"]=10;phase=2;demo.ApplyPreset(demo.Settings.Captures[0]);return;
                }
                if(phase==2)
                {
                    var p=demo.Settings.Captures[preset];if(!demo.Instrument.Reciprocal.Current) return;if(!Capture(p.Id,p.Width,p.Height)) return;
                    preset++;if(preset<demo.Settings.Captures.Length) {demo.ApplyPreset(demo.Settings.Captures[preset]);return;}
                    evidence["capture_presets"]=preset;demo.ApplyPreset(demo.Settings.Captures[0]);demo.SetPalette(true);phase=3;return;
                }
                if(phase==3)
                {
                    if(!demo.Instrument.Reciprocal.Current||!Capture("alternative_palette")) return;
                    evidence["passed"]=true;evidence["quest_validation"]="NOT RUN";evidence["player_build"]="NOT RUN";Finish(0);
                }
            }
            catch(Exception e) {evidence["passed"]=false;evidence["error"]=e.ToString();Debug.LogError("[XRSuperspace] demo smoke: "+e.Message);Finish(1);}
        }
        static void Finish(int code)
        {
            using(var f=new FileStream(Path.Combine(Output,"evidence.json"),FileMode.CreateNew)) using(var writer=new StreamWriter(f)) writer.Write(evidence.ToString());
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Debug.Log("[XRSuperspace] Demo graphics exit="+code+"; no APK/Quest validation");EditorApplication.Exit(code);
        }
    }
}
