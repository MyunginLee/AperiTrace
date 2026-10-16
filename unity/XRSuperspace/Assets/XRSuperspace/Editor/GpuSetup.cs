using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace XRSuperspace.Editor
{
    public static class GpuSetup
    {
        public const string ScenePath=LabSetup.Root+"/Scenes/SuperspaceGpuLab.unity";
        public static void BatchSetup()=>Create(Environment.GetCommandLineArgs().Contains("-gpu-rebuild"));
        [MenuItem("XR Superspace/Create GPU lab (safe)")]
        static void Menu() {if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Create(false);}
        public static void Create(bool rebuild)
        {
            if(File.Exists(ScenePath)&&!rebuild) throw new IOException("GPU scene exists; deliberate -gpu-rebuild required");
            var scene=EditorSceneManager.OpenScene(InstrumentSetup.ScenePath,OpenSceneMode.Single);var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();lab.name="SuperspaceGpuLab";
            string settingsPath=LabSetup.Root+"/Settings/GpuLabSettings.asset";var settings=AssetDatabase.LoadAssetAtPath<LabSettings>(settingsPath);
            if(settings==null) {if(File.Exists(settingsPath)) throw new IOException("unknown GPU settings");settings=UnityEngine.Object.Instantiate(lab.Settings);settings.Datasets=new[]{"canonical_small_v1/canonical.qc5d","modelset_small/modelset_small.qc5d","modelset_medium/modelset_medium.qc5d"};AssetDatabase.CreateAsset(settings,settingsPath);}lab.Settings=lab.View.Settings=settings;
            var preview=lab.gameObject.AddComponent<GpuPreview>();preview.Lab=lab;preview.Classifier=AssetDatabase.LoadAssetAtPath<ComputeShader>(LabSetup.Root+"/Shaders/PreviewMembership.compute");
            string materialPath=LabSetup.Root+"/Materials/GpuPoints.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {if(File.Exists(materialPath)) throw new IOException("unknown GPU material");material=new Material(Shader.Find("XRSuperspace/GpuPoint")) {enableInstancing=true};AssetDatabase.CreateAsset(material,materialPath);}preview.GpuMaterial=material;
            string folder=LabSetup.Root+"/Settings/PreviewTiers";if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(LabSetup.Root+"/Settings","PreviewTiers");preview.Tiers=new PreviewTier[4];
            string[] titles={"Diagnostic","Desktop","Quest","Capture"};int[] budgets={1024,20000,4096,50000},edges={1024,1024,256,10000};
            for(int i=0;i<4;i++) {string path=folder+"/"+titles[i]+".asset";var tier=AssetDatabase.LoadAssetAtPath<PreviewTier>(path);if(tier==null) {if(File.Exists(path)) throw new IOException("unknown tier");tier=ScriptableObject.CreateInstance<PreviewTier>();tier.Title=titles[i];tier.PointBudget=budgets[i];tier.EdgeBudget=edges[i];tier.InternalView=i!=2;tier.Labels=i!=2;tier.ValidateOnLoad=i==0;tier.BoundaryPoints=i==0;tier.PointSize=i==3?.35f:.22f;tier.FrameTarget=i==2?72:60;AssetDatabase.CreateAsset(tier,path);}preview.Tiers[i]=tier;}
            var profiler=lab.gameObject.AddComponent<PreviewProfiler>();profiler.Preview=preview;
            var panel=new GameObject("GPU precision and LOD controls",typeof(RectTransform));panel.transform.SetParent(lab.transform,false);var panelRect=(RectTransform)panel.transform;panelRect.anchoredPosition3D=new Vector3(0,2.97f,1.1f);var controls=panel.AddComponent<PreviewControls>();controls.Preview=preview;controls.Profiler=profiler;
            ((RectTransform)lab.GetComponent<LinkedInstrument>().UI.transform).anchoredPosition3D=new Vector3(-2.3f,-1,1.1f);
            CameraFit(lab);
            // The original rig, materials and linked controls remain intact.
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true),new EditorBuildSettingsScene(InstrumentSetup.ScenePath,false),new EditorBuildSettingsScene(LabSetup.ScenePath,false)};
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();Debug.Log("[XRSuperspace] GPU scene saved; no APK/device validation");
        }
        static void CameraFit(LabController lab) {var camera=lab.GetComponent<LabXrMode>().DesktopCamera;camera.fieldOfView=49;camera.transform.position=new Vector3(0,.8f,-4.6f);}
    }
}
