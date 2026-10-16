using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;

namespace XRSuperspace.Editor
{
    // Portable text-serialized build presets; applying never builds an APK/player.
    // These are not undocumented/native Unity BuildProfile internals.
    public sealed class LabBuildPreset : ScriptableObject
    {
        public BuildTarget Target=BuildTarget.StandaloneWindows64;
        public string Scene="Assets/XRSuperspace/Scenes/SuperspaceLab.unity";
        public bool InitializeXrOnStart;
        [ContextMenu("Apply settings (does not build)")]
        public void Apply()
        {
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Scene,true)};
            var group=BuildPipeline.GetBuildTargetGroup(Target);
            var xr=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if(xr!=null) { xr.InitManagerOnStart=InitializeXrOnStart;EditorUtility.SetDirty(xr); }
            if(group==BuildTargetGroup.Android)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
                PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
                PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[XRSuperspace] applied "+name+"; select target in Build Profiles when preparing a build. No build started.");
        }
    }
}
