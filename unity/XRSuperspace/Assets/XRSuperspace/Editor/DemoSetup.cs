using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace XRSuperspace.Editor
{
    public static class DemoSetup
    {
        public const string ScenePath=LabSetup.Root+"/Scenes/SuperspaceDemo.unity";
        public static void BatchSetup()=>Create();
        [MenuItem("XR Superspace/Create scientific demo (safe)")]
        public static void Create()
        {
            if(File.Exists(ScenePath)) throw new IOException("Demo scene already exists; safe setup never overwrites");
            var scene=EditorSceneManager.OpenScene(GpuSetup.ScenePath,OpenSceneMode.Single);var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();
            string folder=LabSetup.Root+"/Settings/Demo";if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(LabSetup.Root+"/Settings","Demo");
            string[] ids={"paper_16x9","square_overview","high_resolution","internal_window","linked_point","phason_before","phason_after","diffraction_before","diffraction_after","performance_off","performance_on"};
            string[] titles={"Paper / video 16:9","Square linked overview","High-resolution still","Internal acceptance-window diagram","Exact physical/internal linked n","Phason before","Phason after","Diffraction before","Diffraction after","Performance overlay OFF","Performance overlay ON"};
            var settings=ScriptableObject.CreateInstance<DemoSettings>();settings.Captures=new DemoCapturePreset[ids.Length];
            for(int i=0;i<ids.Length;i++)
            {
                string path=folder+"/"+ids[i]+".asset";if(File.Exists(path)) throw new IOException("existing capture preset: "+path);
                var p=ScriptableObject.CreateInstance<DemoCapturePreset>();p.Id=ids[i];p.Title=titles[i];
                if(i==1) p.Width=p.Height=1600;if(i==2) {p.Width=3840;p.Height=2160;}if(i==3||i==4) p.Reciprocal=false;
                if(i==3) p.Physical=false;if(i==6||i==8) {p.PhiX=.181;p.PhiY=.047;}p.Performance=i==10;p.Validate();
                AssetDatabase.CreateAsset(p,path);settings.Captures[i]=p;
            }
            string settingsPath=folder+"/DemoSettings.asset";if(File.Exists(settingsPath)) throw new IOException("existing demo settings");AssetDatabase.CreateAsset(settings,settingsPath);
            var demo=lab.gameObject.AddComponent<ScientificDemo>();demo.Instrument=lab.GetComponent<LinkedInstrument>();demo.Preview=lab.GetComponent<GpuPreview>();demo.Settings=settings;
            var capture=lab.gameObject.AddComponent<DemoCapture>();capture.Demo=demo;demo.Capture=capture;
            var panel=new GameObject("Scientific tour, provenance and capture",typeof(RectTransform));panel.transform.SetParent(lab.transform,false);((RectTransform)panel.transform).anchoredPosition3D=settings.PanelPosition;
            var ui=panel.AddComponent<DemoUI>();ui.Demo=demo;demo.UI=ui;
            demo.Instrument.Camera.transform.SetPositionAndRotation(new Vector3(0,2,-6.1f),Quaternion.identity);demo.Instrument.Camera.fieldOfView=42;
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("[XRSuperspace] Scientific demo created; original scenes, build settings and XR packages preserved. No build/device check.");
        }
    }
}
