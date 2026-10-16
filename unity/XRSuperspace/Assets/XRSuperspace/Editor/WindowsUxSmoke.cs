using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace XRSuperspace.Editor
{
    [InitializeOnLoad]
    public static class WindowsUxSmoke
    {
        const string Key="AperiTrace.WindowsUxSmoke";
        static double deadline;
        static WindowsUxSmoke() {if(SessionState.GetBool(Key,false)) Attach();}
        static void Attach() {deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        public static void BatchRun()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-desktop-ux-output");
            if(index<0||index+1>=args.Length) throw new ArgumentException("-desktop-ux-output required");
            string output=Path.GetFullPath(args[index+1]);if(Directory.Exists(output)) throw new IOException("Preserve existing desktop UX evidence.");
            SessionState.SetString(Key+".output",output);SessionState.SetBool(Key,true);EditorSceneManager.OpenScene(DemoSetup.ScenePath,OpenSceneMode.Single);Attach();EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup>deadline) {Finish(false);return;}
            if(!EditorApplication.isPlaying||UnityEngine.Object.FindFirstObjectByType<DesktopUxVerification>()!=null) return;
            var demo=UnityEngine.Object.FindFirstObjectByType<ScientificDemo>();if(demo==null||demo.Lab.State==null||!demo.Instrument.Reciprocal.Current) return;
            var workspace=DesktopWorkspace.Install(demo);workspace.SuppressPreferenceWrites=true;
            var runner=demo.gameObject.AddComponent<DesktopUxVerification>();runner.Demo=demo;runner.Output=SessionState.GetString(Key+".output","");runner.Finished=Finish;
        }
        static void Finish(bool passed) {SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(passed?0:2);}
    }
}
