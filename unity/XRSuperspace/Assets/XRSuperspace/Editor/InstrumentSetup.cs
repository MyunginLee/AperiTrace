using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace.Editor
{
    public static class InstrumentSetup
    {
        public const string ScenePath=LabSetup.Root+"/Scenes/SuperspaceInstrument.unity";
        [MenuItem("XR Superspace/Create linked instrument (safe)")]
        public static void CreateMenu() {if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Create(false);}
        public static void BatchSetup()=>Create(Environment.GetCommandLineArgs().Contains("-instrument-rebuild"));
        public static void Create(bool rebuild)
        {
            if(File.Exists(ScenePath)&&!rebuild) throw new IOException("SuperspaceInstrument exists: preserve manual edits; deliberate -instrument-rebuild is required");
            var scene=EditorSceneManager.OpenScene(LabSetup.ScenePath,OpenSceneMode.Single);var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();
            lab.name="SuperspaceInstrument";lab.Diagnostics.transform.parent.gameObject.SetActive(false);lab.Diagnostics=null;
            var placeholder=lab.transform.Find("Reciprocal Space — placeholder");if(placeholder!=null) UnityEngine.Object.DestroyImmediate(placeholder.gameObject);
            var view=lab.View;view.InstrumentMode=true;view.ShowCandidates=false;view.PhysicalOrigin.position=new Vector3(-1.65f,1.4f,1.3f);view.InternalOrigin.position=new Vector3(0,1.4f,1.3f);
            string materialPath=LabSetup.Root+"/Materials/TransitionPoints.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {if(File.Exists(materialPath)) throw new IOException("unknown material at "+materialPath);var shader=Shader.Find("XRSuperspace/TransitionPoint");if(shader==null) throw new InvalidOperationException("transition shader not imported");material=new Material(shader) {enableInstancing=true};AssetDatabase.CreateAsset(material,materialPath);}view.PointMaterial=material;
            var camera=lab.GetComponent<LabXrMode>().DesktopCamera;camera.transform.SetPositionAndRotation(new Vector3(0,.75f,-4.6f),Quaternion.identity);camera.fieldOfView=36;lab.GetComponent<DesktopInput>().Pivot=new Vector3(0,.75f,1.3f);
            var origin=new GameObject("Reciprocal Space — qz≈0").transform;origin.SetParent(lab.transform,false);origin.position=new Vector3(1.65f,1.4f,1.3f);
            var reciprocal=lab.gameObject.AddComponent<ReciprocalInstrument>();reciprocal.Lab=lab;reciprocal.Origin=origin;
            var box=origin.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(1.3f,1.3f,.04f);origin.gameObject.AddComponent<ReciprocalViewInteractable>().Instrument=reciprocal;
            var instrument=lab.gameObject.AddComponent<LinkedInstrument>();instrument.Lab=lab;instrument.Reciprocal=reciprocal;instrument.Camera=camera;
            var panel=new GameObject("Linked instrument controls",typeof(RectTransform));panel.transform.SetParent(lab.transform,false);panel.transform.position=new Vector3(-2.3f,-1,1.1f);var ui=panel.AddComponent<InstrumentUI>();ui.Instrument=instrument;instrument.UI=ui;
            lab.gameObject.AddComponent<InstrumentOverlay>().Instrument=instrument;
            string folder=LabSetup.Root+"/Settings/CapturePresets";if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(LabSetup.Root+"/Settings","CapturePresets");
            string[] titles={"Parent → physical overview","Internal window close-up","Selected n across all views","Phason entry / exit","Physical + diffraction"};instrument.Presets=new InstrumentCapturePreset[5];
            for(int i=0;i<5;i++)
            {
                string path=folder+"/Preset"+(i+1)+".asset";var p=AssetDatabase.LoadAssetAtPath<InstrumentCapturePreset>(path);
                if(p==null) {if(File.Exists(path)) throw new IOException("unknown capture preset");p=ScriptableObject.CreateInstance<InstrumentCapturePreset>();p.Title=titles[i];p.ShowCandidates=i==0;p.SelectedId=i==0?null:"-1,0,-1,0,0";if(i==1) {p.CameraPosition=new Vector3(0,1.4f,-1.1f);p.CameraPivot=new Vector3(0,1.4f,1.3f);}if(i==3) {p.PhiX=.181;p.PhiY=.047;}AssetDatabase.CreateAsset(p,path);}instrument.Presets[i]=p;
            }
            foreach(var text in lab.GetComponentsInChildren<TextMesh>()) {if(text.name=="Physical Label") text.transform.position=new Vector3(-1.65f,2.5f,1.3f);if(text.name=="Internal Label") {text.transform.position=new Vector3(0,2.5f,1.3f);text.text="INTERNAL / PARAMETER SPACE";text.fontSize=30;}}
            var label=new GameObject("Reciprocal Label").AddComponent<TextMesh>();label.transform.SetParent(lab.transform,false);label.transform.position=new Vector3(1.65f,2.5f,1.3f);label.text="RECIPROCAL / qz≈0";label.fontSize=30;label.characterSize=.025f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(.75f,.84f,.94f);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true),new EditorBuildSettingsScene(LabSetup.ScenePath,false)};
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();Debug.Log("[XRSuperspace] Linked instrument scene saved; laboratory scene preserved. No APK/device validation.");
        }
    }
}
