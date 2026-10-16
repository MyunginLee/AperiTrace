using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace XRSuperspace.Editor
{
    // Explicit local-device build, never called by setup/test/figure workflows.
    // Existing scenes, package pins, scientific exports and desktop settings stay intact.
    public static class QuestDemoBuild
    {
        public const string ApplicationId = "org.xrsuperspace.demo";

        static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static string OutputPath()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
            string builds = Path.Combine(root,"builds") + Path.DirectorySeparatorChar;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args,"-superspace-apk-output");
            string path = index >= 0 && index + 1 < args.Length ? args[index+1] :
                Path.Combine(builds,"QuestDemo-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),"XRSuperspace-0.1.0-quest.apk");
            path = Path.GetFullPath(path);
            if(!path.StartsWith(builds,StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path)!=".apk")
                throw new ArgumentException("APK output must be a named .apk within this project's builds directory");
            if(File.Exists(path) || File.Exists(Path.Combine(Path.GetDirectoryName(path),"build_manifest.json")))
                throw new IOException("preserve existing build output: "+path);
            return path;
        }

        public static void Build()
        {
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
                throw new InvalidOperationException("Start Unity with -buildTarget Android; do not switch during execution");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))
                throw new InvalidOperationException("Installed Android Build Support is required; no installation attempted");
            if(!File.Exists(DemoSetup.ScenePath)) throw new FileNotFoundException("Existing scientific demo scene required");
            string output = OutputPath();
            string dataRoot = Path.Combine(Application.streamingAssetsPath,"generated");
            string[] dataPaths = Directory.GetFiles(dataRoot,"*",SearchOption.AllDirectories)
                .Where(p=>!p.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(dataPaths.Length==0) throw new InvalidOperationException("No verified local datasets to package");
            var dataHashes = new JObject();
            foreach(string path in dataPaths)
                dataHashes[Path.GetRelativePath(Application.streamingAssetsPath,path).Replace('\\','/')] = Hash(path);
            Qc5dParser.Load(Path.Combine(dataRoot,"canonical_small_v1/canonical.qc5d"));
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if(xr?.Manager==null || !xr.Manager.activeLoaders.Any(l=>l is OpenXRLoader))
                throw new InvalidOperationException("Existing Android OpenXR loader is required");
            foreach(string id in new[]{"com.unity.openxr.feature.metaquest","com.unity.openxr.feature.input.oculustouch","com.unity.openxr.feature.input.handtrackingsubsystem"})
            {
                var feature=FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android,id);
                if(feature==null || !feature.enabled)
                    throw new InvalidOperationException("Existing enabled OpenXR feature required: "+id);
            }

            var named = NamedBuildTarget.Android;
            string oldId = PlayerSettings.GetApplicationIdentifier(named), oldVersion = PlayerSettings.bundleVersion,oldName=PlayerSettings.productName;
            int oldCode = PlayerSettings.Android.bundleVersionCode;
            var oldBackend = PlayerSettings.GetScriptingBackend(named);
            var oldArchitectures = PlayerSettings.Android.targetArchitectures;
            var oldMin = PlayerSettings.Android.minSdkVersion; var oldTarget = PlayerSettings.Android.targetSdkVersion;
            var oldColor = PlayerSettings.colorSpace;
            bool oldDefaultApis = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
            var oldApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            var oldStripping = PlayerSettings.GetManagedStrippingLevel(named);
            bool oldBundle = EditorUserBuildSettings.buildAppBundle, oldKeystore = PlayerSettings.Android.useCustomKeystore;
            bool oldExport = EditorUserBuildSettings.exportAsGoogleAndroidProject, oldInit = xr.InitManagerOnStart;
            var handAim=FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android,"com.unity.openxr.feature.input.metahandtrackingaim");
            if(handAim==null) throw new InvalidOperationException("Installed Meta Hand Tracking Aim feature required");
            bool oldHandAim=handAim.enabled;
            try
            {
                handAim.enabled=true;EditorUtility.SetDirty(handAim);
                PlayerSettings.SetApplicationIdentifier(named,ApplicationId);
                PlayerSettings.productName="AperiTrace";
                PlayerSettings.bundleVersion="0.1.0"; PlayerSettings.Android.bundleVersionCode=7;
                PlayerSettings.SetScriptingBackend(named,ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
                PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
                PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
                PlayerSettings.colorSpace=ColorSpace.Linear;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.Vulkan});
                // Keep project DTO members used by Newtonsoft reflection in this development build.
                PlayerSettings.SetManagedStrippingLevel(named,ManagedStrippingLevel.Minimal);
                PlayerSettings.Android.useCustomKeystore=false;
                EditorUserBuildSettings.buildAppBundle=false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject=false;
                xr.InitManagerOnStart=true; EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
                var issues = new List<OpenXRFeature.ValidationRule>();
                OpenXRProjectValidation.GetCurrentValidationIssues(issues,BuildTargetGroup.Android);
                foreach(var issue in issues) Debug.LogWarning("[XRSuperspace] OpenXR validation: "+issue.message);
                if(issues.Any(i=>i.error)) throw new InvalidOperationException("OpenXR project validation has blocking errors; no auto-fix applied");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                Debug.Log("[XRSuperspace] Building scientific demo APK: "+output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=new[]{DemoSetup.ScenePath},target=BuildTarget.Android,
                    locationPathName=output,options=BuildOptions.Development
                });
                if(report.summary.result!=BuildResult.Succeeded)
                    throw new InvalidOperationException("Quest demo build failed: "+report.summary.result);
                foreach(string path in dataPaths)
                    if((string)dataHashes[Path.GetRelativePath(Application.streamingAssetsPath,path).Replace('\\','/')]!=Hash(path))
                        throw new InvalidOperationException("Frozen dataset changed while building: "+path);
                var manifest = new JObject {
                    ["result"]="Succeeded",["unity_version"]=Application.unityVersion,
                    ["scene"]=DemoSetup.ScenePath,["application_id"]=ApplicationId,
                    ["version"]=PlayerSettings.bundleVersion,["version_code"]=PlayerSettings.Android.bundleVersionCode,["product_name"]=PlayerSettings.productName,["development_build"]=true,
                    ["signing"]="Android development/debug key; not a store release",
                    ["architecture"]="ARM64",["scripting_backend"]="IL2CPP",
                    ["graphics_api"]="Vulkan",["color_space"]="Linear",["minimum_api"]=32,
                    ["target_api"]="Automatic; inspect packaged manifest for resolved SDK",
                    ["apk"]=Path.GetFileName(output),["apk_sha256"]=Hash(output),
                    ["apk_bytes"]=new FileInfo(output).Length,["build_seconds"]=report.summary.totalTime.TotalSeconds,
                    ["scene_dependencies"]=new JArray(AssetDatabase.GetDependencies(DemoSetup.ScenePath,true).OrderBy(p=>p,StringComparer.Ordinal)),
                    ["meta_hand_tracking_aim_enabled"]=handAim.enabled,
                    ["streaming_assets_sha256"]=dataHashes,["openxr_validation_warnings"]=new JArray(issues.Select(i=>i.message)),
                    ["headset_execution"]="NOT RUN",["installation"]="NOT RUN",
                    ["performance_stereo_hand_tracking_and_device_parity"]="PENDING actual device checks"
                };
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build_manifest.json"),manifest.ToString());
                Debug.Log("[XRSuperspace] APK build succeeded; device execution/validation NOT RUN");
            }
            finally
            {
                handAim.enabled=oldHandAim;EditorUtility.SetDirty(handAim);
                PlayerSettings.SetApplicationIdentifier(named,oldId); PlayerSettings.bundleVersion=oldVersion;
                PlayerSettings.productName=oldName;
                PlayerSettings.Android.bundleVersionCode=oldCode; PlayerSettings.SetScriptingBackend(named,oldBackend);
                PlayerSettings.Android.targetArchitectures=oldArchitectures;
                PlayerSettings.Android.minSdkVersion=oldMin; PlayerSettings.Android.targetSdkVersion=oldTarget;
                PlayerSettings.colorSpace=oldColor; PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,oldApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,oldDefaultApis);
                PlayerSettings.SetManagedStrippingLevel(named,oldStripping);
                PlayerSettings.Android.useCustomKeystore=oldKeystore; EditorUserBuildSettings.buildAppBundle=oldBundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject=oldExport; xr.InitManagerOnStart=oldInit;
                EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
            }
        }
    }
}
