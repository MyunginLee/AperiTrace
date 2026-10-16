using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace XRSuperspace.Editor
{
    public static class PreviewBuilds
    {
        public static void DesktopDevelopment()=>Build(BuildTarget.StandaloneWindows64,"PreviewDesktop/AperiTrace.exe",BuildOptions.Development|BuildOptions.AllowDebugging);
        public static void Capture()=>Build(BuildTarget.StandaloneWindows64,"PreviewCapture/AperiTrace.exe",BuildOptions.None);
        public static void Quest()=>Build(BuildTarget.Android,"PreviewQuest/AperiTrace.apk",BuildOptions.Development);
        static void Build(BuildTarget target,string name,BuildOptions options)
        {
            // SDK/toolchain paths remain Unity-configured. Never install or infer them.
            string output=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../../builds",name));
            if(File.Exists(output)) throw new IOException("build output already exists; preserve it: "+output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{GpuSetup.ScenePath},target=target,locationPathName=output,options=options});
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("requested build failed: "+report.summary.result);
        }
    }
}
