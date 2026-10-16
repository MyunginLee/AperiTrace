using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace XRSuperspace
{
    // Explicit built-player integration-test entry point. Inert in normal use.
    public sealed class WindowsPlayerSmoke : MonoBehaviour
    {
        string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(Application.isEditor||Application.platform!=RuntimePlatform.WindowsPlayer) return;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-aperitrace-smoke-output");
            if(index<0) return;
            if(index+1>=args.Length) {Debug.LogError("Smoke output argument missing.");Application.Quit(2);return;}
            string folder=Path.GetFullPath(args[index+1]);
            if(Directory.Exists(folder)) {Debug.LogError("Preserve previous smoke output.");Application.Quit(2);return;}
            Directory.CreateDirectory(folder);var runner=new GameObject("Explicit Windows player smoke").AddComponent<WindowsPlayerSmoke>();runner.output=folder;
        }
        IEnumerator Start()
        {
            var work=Run();Exception error=null;var stack=new Stack<IEnumerator>();stack.Push(work);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;try {more=current.MoveNext();}catch(Exception caught) {error=caught;}
                if(error!=null) break;
                if(!more) {(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(current.Current is IEnumerator nested) {stack.Push(nested);continue;}
                yield return current.Current;
            }
            while(stack.Count>0) (stack.Pop() as IDisposable)?.Dispose();
            if(error!=null) {File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Debug.LogException(error);var demo=FindFirstObjectByType<ScientificDemo>();if(demo!=null&&demo.Desktop!=null) demo.Desktop.enabled=false;Application.Quit(2);}
            else Application.Quit(0);
        }
        static void Check(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
        IEnumerator Ready(ScientificDemo demo)
        {
            float deadline=Time.realtimeSinceStartup+45;
            while((demo.Lab.State==null||demo.Lab.Busy||!demo.Instrument.Reciprocal.Current)&&Time.realtimeSinceStartup<deadline) yield return null;
            Check(demo.Lab.State!=null&&!demo.Lab.Busy&&demo.Instrument.Reciprocal.Current,"Player failed to reach CURRENT.");
        }
        IEnumerator Run()
        {
            var demo=FindFirstObjectByType<ScientificDemo>();Check(demo!=null,"Scientific demo scene not loaded.");
            yield return Ready(demo);
            Check(!UnityEngine.XR.XRSettings.isDeviceActive,"Desktop build unexpectedly started XR.");
            Check(demo.Instrument.Camera.enabled&&demo.Desktop!=null&&demo.Desktop.Active,"Desktop camera/UI unavailable.");
            bool presented=Array.IndexOf(Environment.GetCommandLineArgs(),"-aperitrace-presented-frame-check")>=0;
            if(presented) yield return DesktopPresentedFrameVerification.Run(demo.Desktop,output);
            // No desktop user preferences are saved or overwritten by this test.
            demo.Lifecycle.SetFocused(true);demo.Desktop.SetPaused(false);demo.Desktop.CancelModal();demo.StartGuide(false);
            float guideDeadline=Time.realtimeSinceStartup+60;
            while(demo.Step<9)
            {
                Check(Time.realtimeSinceStartup<guideDeadline,"Guide integration test timed out.");
                demo.Advance();
                while(demo.PathRunning) {demo.Lifecycle.SetFocused(true);Check(Time.realtimeSinceStartup<guideDeadline,"Guide path timed out.");yield return null;}
                yield return null;
            }
            demo.FreeMode();yield return null;yield return Ready(demo);
            Check(demo.Lab.GetComponent<DesktopInput>().enabled,"Mouse/keyboard component disabled in free mode.");
            demo.Lab.SetPhi(.181,.047);yield return Ready(demo);
            string session=demo.SessionJson(),key=demo.Lab.State.Model.Key("");
            string path=LocalSessionStore.Save(Path.Combine(output,"sessions"),session);
            demo.Lab.SetPhi(-.119,.047);yield return Ready(demo);
            demo.LoadSessionJson(LocalSessionStore.Read(Path.Combine(output,"sessions"),path));yield return Ready(demo);
            Check(demo.Lab.State.Model.Key("")==key,"Session restore changed scientific state.");
            demo.Capture.Begin(output,"windows_before",1920,1080);while(demo.Capture.Busy) yield return null;
            Check(demo.Capture.LastError==null&&File.Exists(Path.Combine(output,"windows_before.png")),"Before capture failed.");
            demo.Lab.SetPhi(-.25,-.25);yield return Ready(demo);
            demo.Capture.Begin(output,"windows_after",1920,1080);while(demo.Capture.Busy) yield return null;
            Check(demo.Capture.LastError==null&&File.Exists(Path.Combine(output,"windows_after.png")),"After capture failed.");
            var ux=demo.gameObject.AddComponent<DesktopUxVerification>();ux.Demo=demo;ux.Output=Path.Combine(output,"ux");
            while(!ux.Done) yield return null;Check(ux.Passed,"Desktop UX verification failed; inspect retained failure.");
            var report=new JObject { ["passed"]=true,["platform"]=Application.platform.ToString(),["unity_version"]=Application.unityVersion,
                ["graphics_api"]=SystemInfo.graphicsDeviceType.ToString(),["graphics_device"]=SystemInfo.graphicsDeviceName,["xr_active"]=false,
                ["guide_all_ten_steps"]=true,["desktop_input_component_enabled"]=true,["session_file_round_trip"]=true,
                ["before_after_camera_renders_and_state"]=true,["persistent_data_path"]=Application.persistentDataPath,
                ["desktop_ux_and_simulated_mouse_keyboard"]=true,
                ["presented_frame_history_verified"]=presented,
                ["scope"]="Actual built Windows player, real graphics and simulated Input System mouse/keyboard. NOT human usability, screen-reader or sustained performance validation." };
            File.WriteAllText(Path.Combine(output,"evidence.json"),report.ToString());
        }
    }
}
