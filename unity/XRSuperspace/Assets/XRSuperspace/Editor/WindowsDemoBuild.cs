using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace.Editor
{
    public static class WindowsDemoBuild
    {
        static string Hash(string path)
        {
            using var sha=SHA256.Create();using var file=File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();
        }
        public static void Build()
        {
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.StandaloneWindows64||!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Start the pinned Editor with -buildTarget Win64 and installed Windows support.");
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-aperitrace-windows-output");
            if(index<0||index+1>=args.Length) throw new ArgumentException("-aperitrace-windows-output required");
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../..")),output=Path.GetFullPath(args[index+1]);
            if(!output.StartsWith(Path.Combine(root,"builds")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(output)!=".exe")
                throw new ArgumentException("Use a fresh executable under this project's builds directory.");
            string folder=Path.GetDirectoryName(output);
            if(File.Exists(output)||File.Exists(Path.Combine(folder,"build_manifest.json"))) throw new IOException("Preserve existing build output.");
            if(!File.Exists(DemoSetup.ScenePath)) throw new FileNotFoundException("Scientific demo scene missing.");
            var data=Path.Combine(Application.streamingAssetsPath,"generated");
            Qc5dParser.Load(Path.Combine(data,"canonical_small_v1/canonical.qc5d"));
            var named=NamedBuildTarget.Standalone;
            var xr=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var loaders=xr!=null&&xr.Manager!=null?xr.Manager.activeLoaders.ToList():null;
            bool init=xr!=null&&xr.InitManagerOnStart;
            string product=PlayerSettings.productName,version=PlayerSettings.bundleVersion;
            var backend=PlayerSettings.GetScriptingBackend(named);var api=PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64),resize=PlayerSettings.resizableWindow,background=PlayerSettings.runInBackground;
            int width=PlayerSettings.defaultScreenWidth,height=PlayerSettings.defaultScreenHeight;
            var full=PlayerSettings.fullScreenMode;
            try
            {
                PlayerSettings.productName="AperiTrace";PlayerSettings.bundleVersion="0.1.0";
                PlayerSettings.SetScriptingBackend(named,ScriptingImplementation.Mono2x);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
                PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;
                PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=false;
                if(xr!=null)
                {
                    xr.InitManagerOnStart=false;EditorUtility.SetDirty(xr);
                    if(xr.Manager!=null)
                    {
                        if(!xr.Manager.TrySetLoaders(new System.Collections.Generic.List<UnityEngine.XR.Management.XRLoader>())) throw new InvalidOperationException("Unable to prepare headset-free desktop build.");
                        EditorUtility.SetDirty(xr.Manager);
                    }
                }
                AssetDatabase.SaveAssets();Directory.CreateDirectory(folder);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {target=BuildTarget.StandaloneWindows64,scenes=new[]{DemoSetup.ScenePath},locationPathName=output,options=BuildOptions.None});
                if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Windows build failed: "+report.summary.result);
                File.Copy(Path.Combine(root,"packaging/windows/LICENSE.txt"),Path.Combine(folder,"LICENSE.txt"),false);
                File.Copy(Path.Combine(root,"THIRD_PARTY_NOTICES.txt"),Path.Combine(folder,"THIRD_PARTY_NOTICES.txt"),false);
                File.Copy(Path.Combine(Application.dataPath,"XRSuperspace/Resources/QuestFonts/LICENSE.txt"),Path.Combine(folder,"INTER_LICENSE.txt"),false);
                File.Copy(Path.Combine(Application.streamingAssetsPath,"DIFFRACTION_PALETTES_LICENSE.txt"),Path.Combine(folder,"DIFFRACTION_PALETTES_LICENSE.txt"),false);
                var files=new JObject();
                foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".log")))
                    files[Path.GetRelativePath(folder,file).Replace('\\','/')]=Hash(file);
                var manifest=new JObject { ["result"]="Succeeded",["product_name"]="AperiTrace",["version"]="0.1.0",["platform"]="Windows x86-64",
                    ["unity_version"]=Application.unityVersion,["scene"]=DemoSetup.ScenePath,["scripting_backend"]="Mono",["graphics_api"]="Direct3D11",
                    ["development_build"]=false,["xr_initialize_on_start"]=false,["xr_loaders_at_build"]=new JArray(),["executable"]=Path.GetFileName(output),["executable_sha256"]=Hash(output),
                    ["build_seconds"]=report.summary.totalTime.TotalSeconds,["files_sha256"]=files,["player_execution"]="Not yet tested by build step" };
                File.WriteAllText(Path.Combine(folder,"build_manifest.json"),manifest.ToString());
                File.WriteAllText(Path.Combine(folder,"README.txt"),
                    "AperiTrace 0.1.0 | Windows 64-bit portable desktop application\n\n"+
                    "Extract the whole ZIP, then run AperiTrace.exe. Keep AperiTrace_Data, UnityPlayer.dll, MonoBleedingEdge and the other bundled files together. No Unity Editor, Python, internet or VR headset is required.\n\n"+
                    "The start screen offers Open workspace, Guided tour and saved sessions. Inspect (I) opens Model / Point / Peak values. Precision controls edits scientific offsets. View... instead edits camera horizontal/vertical center and width or height, preserving aspect ratio. Wheel zoom: 1.5x per notch, range 10% to 2000%, in every view. Middle-drag pans directly; right-drag rotates Structure. G or Grid toggles a calibrated grid, hidden by default; focus a view to see its scientific axes and live scale. Structure exact view resets rotation to the x/y plane. Grid coordinates are dimensionless or model reciprocal units, never measured material coordinates. F explores; N advances; Guided tour is available through Menu or Start screen. Drag the cyan control or use arrow keys after clicking a diagram. Home restores all views; E toggles edges; 0 resets the scientific offset. Export CSV saves checksum-linked numbers locally.\n\n"+
                    "Ctrl+S saves a new session. Ctrl+O opens saved sessions. Ctrl+Shift+S saves a screenshot and checksum-linked scientific JSON. P pauses/resumes; Esc opens/closes the menu. Tab / Shift+Tab moves focus; Enter activates.\n\n"+
                    "Color & filters... offers 18 Matplotlib colormaps and a custom three-color gradient, reversal, Linear / Square root / Log10 scaling, custom intensity range, auto range, intensity and reciprocal-radius visibility filters, and central-peak hiding. Filters affect display and picking only: all 381 scientific samples and CSV exports remain unchanged. The legend discloses the range, scale, visible count, selected-white-peak exception and unchanged square-root marker sizes. Settings are local preferences; screenshot sidecars record them. Matplotlib colormap license is included.\n\n"+
                    "Switching to another window safely pauses input. A central Resume button explains the lock; returning to the window never moves the model automatically. Rapid drags retain the true press coordinate even if down/move/up arrive in one frame. Desktop sessions store a canonical cross-platform layout, not temporary offscreen origins or the zoom/pan viewport. Screenshot sidecars record the actual desktop view.\n\n"+
                    "G / Grid shows independent grids in All views and every individual view. Structure and Window are dimensionless; Diffraction uses model reciprocal units. Each pane has its own origin and scale. Fine 1/2/5 intervals adapt to approximately 24 logical pixels per small cell; major lines mark every five small cells. Numbers label major lines only. Grid changes crossfade for 120 ms unless Reduced motion is enabled. Rotated Structure shows a projected grid without misleading axis tick labels. Grid is hidden by default and does not change scientific coordinates.\n\n"+
                    "Settings include three text sizes, high contrast, a blue/amber scientific palette and reduced motion. Settings do not alter the scientific model. Loading/restarting/clearing unsaved work requests save/discard/cancel. X / Alt+F4 opens a guarded quit dialog; forced process termination cannot preserve unsaved memory.\n\n"+
                    "Local files are under Windows LocalLow (company/product), not beside the executable. Sessions: SuperspaceDemoSessions. Screenshots: AperiTraceDesktopScreenshots. Numerical tables: AperiTraceNumericalExports. Sessions menu can open that data folder. No cloud or telemetry.\n\n"+
                    "Diffraction is calculated, not a measured X-ray image. This is a mathematical model, not a refined material or literal extra dimensions. MIT-licensed project; see LICENSE.txt and third-party notices. Support: myungin@umd.edu. Screen-reader integration and cross-machine commercial deployment remain unverified.\n");
            }
            finally
            {
                PlayerSettings.productName=product;PlayerSettings.bundleVersion=version;PlayerSettings.SetScriptingBackend(named,backend);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,api);PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,defaults);
                PlayerSettings.fullScreenMode=full;PlayerSettings.defaultScreenWidth=width;PlayerSettings.defaultScreenHeight=height;
                PlayerSettings.resizableWindow=resize;PlayerSettings.runInBackground=background;
                if(xr!=null)
                {
                    xr.InitManagerOnStart=init;EditorUtility.SetDirty(xr);
                    if(xr.Manager!=null&&loaders!=null)
                    {
                        if(!xr.Manager.TrySetLoaders(loaders)) throw new InvalidOperationException("Unable to restore original standalone XR loaders.");
                        EditorUtility.SetDirty(xr.Manager);
                    }
                }
                AssetDatabase.SaveAssets();
            }
        }
    }
}
