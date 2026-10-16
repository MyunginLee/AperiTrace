using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRSuperspace.Editor
{
    public static class LabSetup
    {
        public const string Root="Assets/XRSuperspace",ScenePath=Root+"/Scenes/SuperspaceLab.unity";
        [MenuItem("XR Superspace/Create SuperspaceLab (safe)")]
        public static void CreateMenu() { if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;Create(false); }
        [MenuItem("XR Superspace/Rebuild generated SuperspaceLab")]
        public static void RebuildMenu() { if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()||!EditorUtility.DisplayDialog("Rebuild generated lab?","Only SuperspaceLab.unity will be replaced. Preserve any manual scene edits first.","Rebuild","Cancel")) return;Create(true); }
        public static void BatchSetup()=>Create(Environment.GetCommandLineArgs().Contains("-superspace-rebuild"));
        static void Folder(string path) { if(AssetDatabase.IsValidFolder(path)) return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path)); }
        static T Asset<T>(string path,Func<T> make) where T:UnityEngine.Object
        {
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);if(existing!=null) return existing;
            if(File.Exists(path)) throw new IOException("unrecognized asset already exists: "+path);
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));var result=make();AssetDatabase.CreateAsset(result,path);return result;
        }
        static GameObject Node(string name,Transform parent=null) { var go=new GameObject(name);if(parent!=null) go.transform.SetParent(parent,false);return go; }
        static Material Material(string name,Color color,bool instanced=false)
        {
            return Asset(Root+"/Materials/"+name+".mat",()=> { var shader=Shader.Find(instanced?"XRSuperspace/InstancedPoint":"Universal Render Pipeline/Unlit");if(shader==null) throw new InvalidOperationException("required shader missing");var mat=new Material(shader) {name=name,enableInstancing=instanced};mat.SetColor(instanced?"_InstanceColor":"_BaseColor",color);return mat; });
        }
        static LineRenderer Line(string name,Transform parent,Material material,Vector3[] positions,float width=0.005f)
        {
            var line=Node(name,parent).AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.positionCount=positions.Length;line.SetPositions(positions);return line;
        }
        static void Label(string name,Transform parent,Vector3 position,string value,int size=40)
        {
            var go=Node(name,parent);go.transform.position=position;var text=go.AddComponent<TextMesh>();text.text=value;text.fontSize=size;text.characterSize=0.025f;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(0.75f,0.84f,0.94f);
        }
        static InputActionAsset Actions()
        {
            string path=Root+"/Settings/LabInput.inputactions";
            if(!File.Exists(path))
            {
                var asset=ScriptableObject.CreateInstance<InputActionAsset>();var map=new InputActionMap("LabXR");asset.AddActionMap(map);
                void Add(string name,string type,string binding)=>map.AddAction(name,type=="Button"?InputActionType.Button:InputActionType.Value,binding,expectedControlLayout:type);
                Add("HeadPosition","Vector3","<XRHMD>/centerEyePosition");Add("HeadRotation","Quaternion","<XRHMD>/centerEyeRotation");
                foreach(string side in new[]{"Left","Right"}) { string device="<XRController>{"+side+"Hand}";Add(side+"Position","Vector3",device+"/devicePosition");Add(side+"Rotation","Quaternion",device+"/deviceRotation");Add(side+"Select","Button",device+"/triggerPressed");Add(side+"Grip","Button",device+"/gripPressed"); }
                Folder(Root+"/Settings");File.WriteAllText(path,asset.ToJson());UnityEngine.Object.DestroyImmediate(asset);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            }
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }
        static InputActionReference Reference(InputActionAsset actions,string action)
            =>Asset(Root+"/Settings/InputRefs/"+action+".asset",()=> { var reference=ScriptableObject.CreateInstance<InputActionReference>();reference.Set(actions,"LabXR",action);return reference; });
        static XRInputButtonReader Reader(InputActionAsset actions,string action)=>new XRInputButtonReader {inputSourceMode=XRInputButtonReader.InputSourceMode.InputActionReference,inputActionReferencePerformed=Reference(actions,action),inputActionReferenceValue=Reference(actions,action)};
        static void Pose(Transform transform,InputActionAsset actions,string prefix)
        {
            var driver=transform.gameObject.AddComponent<TrackedPoseDriver>();driver.positionInput=new InputActionProperty(Reference(actions,prefix+"Position"));driver.rotationInput=new InputActionProperty(Reference(actions,prefix+"Rotation"));
        }
        static (XRRayInteractor ray,XRDirectInteractor direct) Interactors(Transform parent,XRInteractionManager manager,InputActionAsset actions,string side,bool manual)
        {
            var group=parent.gameObject.AddComponent<XRInteractionGroup>();group.interactionManager=manager;
            var directObject=Node("Direct",parent);var sphere=directObject.AddComponent<SphereCollider>();sphere.radius=0.055f;sphere.isTrigger=true;var body=directObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var direct=directObject.AddComponent<XRDirectInteractor>();direct.interactionManager=manager;
            var ray=Node("Ray",parent).AddComponent<XRRayInteractor>();ray.interactionManager=manager;ray.rayOriginTransform=ray.transform;ray.maxRaycastDistance=5;ray.enableUIInteraction=true;
            if(manual) { ray.selectInput=new XRInputButtonReader {inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue};ray.uiPressInput=new XRInputButtonReader {inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue};direct.selectInput=new XRInputButtonReader {inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue}; }
            else { ray.selectInput=Reader(actions,side+"Select");ray.uiPressInput=Reader(actions,side+"Select");direct.selectInput=Reader(actions,side+"Grip"); }
            var line=ray.gameObject.AddComponent<LineRenderer>();line.startWidth=line.endWidth=0.003f;line.sharedMaterial=Material("ControllerRay",Color.cyan);ray.gameObject.AddComponent<XRInteractorLineVisual>();
            group.startingGroupMembers=new System.Collections.Generic.List<UnityEngine.Object>{direct,ray};return (ray,direct);
        }
        static void ConfigureXr()
        {
            var store=Asset(Root+"/Settings/XRGeneralSettings.asset",()=>ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>());
            EditorBuildSettings.AddConfigObject(UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,store,true);
            foreach(var group in new[]{BuildTargetGroup.Standalone,BuildTargetGroup.Android})
            {
                if(!store.HasSettingsForBuildTarget(group)) store.CreateDefaultSettingsForBuildTarget(group);
                if(!store.HasManagerSettingsForBuildTarget(group)) store.CreateDefaultManagerSettingsForBuildTarget(group);
                var settings=store.SettingsForBuildTarget(group);settings.InitManagerOnStart=group==BuildTargetGroup.Android;
                if(!XRPackageMetadataStore.AssignLoader(settings.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",group)) throw new InvalidOperationException("OpenXR loader assignment failed for "+group);
                FeatureHelpers.RefreshFeatures(group);
                string[] ids=group==BuildTargetGroup.Android?new[]{"com.unity.openxr.feature.input.oculustouch","com.unity.openxr.feature.metaquest","com.unity.openxr.feature.input.handtrackingsubsystem"}:new[]{"com.unity.openxr.feature.input.oculustouch","com.unity.openxr.feature.input.handtrackingsubsystem"};
                foreach(string id in ids) { var feature=FeatureHelpers.GetFeatureWithIdForBuildTarget(group,id);if(feature==null) throw new InvalidOperationException("required OpenXR feature unavailable: "+id);feature.enabled=true;EditorUtility.SetDirty(feature); }
                EditorUtility.SetDirty(settings);
            }
            EditorUtility.SetDirty(store);
        }
        public static void Create(bool rebuild)
        {
            if(File.Exists(ScenePath)&&!rebuild) throw new IOException("SuperspaceLab exists; use explicit rebuild to preserve manual scene edits");
            if(!File.Exists("Assets/StreamingAssets/generated/canonical_small_v1/canonical.qc5d")) throw new FileNotFoundException("Stage validated data with scripts/stage_unity_data.py first");
            Folder(Root+"/Scenes");Folder(Root+"/Settings");
            // NewScene unloads unused native assets. Load persistent action assets
            // only after that transition so references are not invalidated.
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSettings.serializationMode=SerializationMode.ForceText;
            PlayerSettings.companyName="XR Superspace";PlayerSettings.productName="XR Superspace";
            var player=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);var input=player.FindProperty("activeInputHandler");if(input==null) throw new InvalidOperationException("Unity Input System setting unavailable");input.intValue=1;player.ApplyModifiedPropertiesWithoutUndo();
            var renderer=Asset(Root+"/Settings/LabRenderer.asset",()=>ScriptableObject.CreateInstance<UniversalRendererData>());
            var pipeline=Asset(Root+"/Settings/LabURP.asset",()=>UniversalRenderPipelineAsset.Create(renderer));pipeline.msaaSampleCount=4;pipeline.supportsHDR=false;pipeline.renderScale=1;EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            ConfigureXr();
            var actions=Actions();var settings=Asset(Root+"/Settings/LabSettings.asset",()=>ScriptableObject.CreateInstance<LabSettings>());
            var lab=Node("SuperspaceLab").AddComponent<LabController>();lab.Settings=settings;
            var view=lab.gameObject.AddComponent<LabRenderer>();lab.View=view;view.Settings=settings;
            var physical=Node("Physical Space",lab.transform);physical.transform.position=new Vector3(-0.85f,1.4f,1.3f);view.PhysicalOrigin=physical.transform;
            var internalView=Node("Internal Space",lab.transform);internalView.transform.position=new Vector3(0.85f,1.4f,1.3f);view.InternalOrigin=internalView.transform;
            foreach(var node in new[]{physical,internalView}) { var box=node.AddComponent<BoxCollider>();box.size=node==physical?new Vector3(1.4f,1.4f,0.8f):new Vector3(1.4f,1.4f,0.02f);box.isTrigger=false;var selectable=node.AddComponent<LabViewInteractable>();selectable.Lab=lab;selectable.InternalView=node==internalView; }
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Sphere);view.PointMesh=primitive.GetComponent<MeshFilter>().sharedMesh;UnityEngine.Object.DestroyImmediate(primitive);
            view.PointMaterial=Material("Points",Color.cyan,true);view.LineMaterial=Material("Graph",new Color(0.2f,0.5f,0.55f));
            view.WindowLine=Line("Acceptance Window",lab.transform,Material("Window",new Color(0.6f,0.8f,1)),Array.Empty<Vector3>(),0.008f);
            var graph=Node("Selected Graph",lab.transform);view.GraphMesh=graph.AddComponent<MeshFilter>();graph.AddComponent<MeshRenderer>().sharedMaterial=view.LineMaterial;
            var reciprocal=Node("Reciprocal Space — placeholder",lab.transform);reciprocal.transform.position=new Vector3(0.85f,0.42f,1.3f);
            Line("Reciprocal Placeholder Border",reciprocal.transform,Material("Placeholder",new Color(0.25f,0.3f,0.4f)),new[]{new Vector3(0.25f,0.05f,1.3f),new Vector3(1.45f,0.05f,1.3f),new Vector3(1.45f,0.75f,1.3f),new Vector3(0.25f,0.75f,1.3f),new Vector3(0.25f,0.05f,1.3f)});
            Label("Physical Label",lab.transform,new Vector3(-0.85f,2.16f,1.3f),"PHYSICAL SPACE");Label("Internal Label",lab.transform,new Vector3(0.85f,2.16f,1.3f),"INTERNAL SPACE");Label("Reciprocal Label",reciprocal.transform,new Vector3(0.85f,0.46f,1.3f),"RECIPROCAL SPACE\nPlaceholder — not calculated",28);
            var handle=GameObject.CreatePrimitive(PrimitiveType.Cube);handle.name="Phason Handle";handle.transform.SetParent(lab.transform);handle.transform.position=internalView.transform.position+new Vector3(0,0,-0.05f);handle.transform.localScale=Vector3.one*0.055f;handle.GetComponent<MeshRenderer>().sharedMaterial=Material("Phason",Color.cyan);var rb=handle.AddComponent<Rigidbody>();rb.isKinematic=true;rb.useGravity=false;var grab=handle.AddComponent<XRGrabInteractable>();grab.trackRotation=false;lab.PhasonHandle=handle.transform;
            var desktop=Node("Desktop Camera").AddComponent<Camera>();desktop.tag="MainCamera";desktop.transform.position=new Vector3(0,1.13f,-2.5f);desktop.fieldOfView=36;desktop.backgroundColor=new Color(0.025f,0.035f,0.055f);desktop.clearFlags=CameraClearFlags.SolidColor;desktop.nearClipPlane=0.03f;
            var controller=lab.gameObject.AddComponent<DesktopInput>();controller.Lab=lab;controller.ViewCamera=desktop;
            var manager=Node("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var origin=Node("XR Origin").AddComponent<XROrigin>();origin.transform.position=new Vector3(0,0,-0.3f);origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            var tracking=Node("Camera Offset",origin.transform).transform;origin.CameraFloorOffsetObject=tracking.gameObject;
            var xrCamera=Node("XR Camera",tracking).AddComponent<Camera>();xrCamera.nearClipPlane=0.03f;xrCamera.backgroundColor=desktop.backgroundColor;xrCamera.clearFlags=CameraClearFlags.SolidColor;origin.Camera=xrCamera;Pose(xrCamera.transform,actions,"Head");
            var endpoints=Node("XR Endpoints",tracking);
            foreach(string side in new[]{"Left","Right"})
            {
                var tracked=Node(side+" Controller",endpoints.transform).transform;Pose(tracked,actions,side);Interactors(tracked,manager,actions,side,false);
                var monitor=Node(side+" Hand Monitor",endpoints.transform).AddComponent<HandPointer>();monitor.Left=side=="Left";monitor.TrackingSpace=tracking;
                var endpoint=Node(side+" Tracked Hand",monitor.transform).transform;var pointers=Interactors(endpoint,manager,actions,side,true);monitor.Endpoint=endpoint;monitor.RayInteractor=pointers.ray;monitor.DirectInteractor=pointers.direct;endpoint.gameObject.SetActive(false);
            }
            var mode=lab.gameObject.AddComponent<LabXrMode>();mode.DesktopCamera=desktop;mode.XrCamera=xrCamera;mode.XrControllers=endpoints;mode.Actions=actions;
            Panel(lab,desktop,manager);
            foreach(var target in new[]{BuildTarget.StandaloneWindows64,BuildTarget.Android}) { string name=target==BuildTarget.Android?"Quest3Android":"DesktopDebug";var preset=Asset(Root+"/Settings/BuildPresets/"+name+".asset",()=>ScriptableObject.CreateInstance<LabBuildPreset>());preset.Target=target;preset.InitializeXrOnStart=target==BuildTarget.Android;EditorUtility.SetDirty(preset);preset.Apply(); }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("[XRSuperspace] SuperspaceLab created; desktop default, OpenXR controller/hand paths, no locomotion. No build performed.");
        }
        static void Panel(LabController lab,Camera camera,XRInteractionManager manager)
        {
            var canvasGo=Node("Metadata and Debug Panel",lab.transform);var canvas=canvasGo.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            var rect=canvas.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(840,600);rect.position=new Vector3(-0.88f,0.39f,1.1f);rect.localScale=Vector3.one*0.0012f;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();var image=canvasGo.AddComponent<Image>();image.color=new Color(0.04f,0.065f,0.10f,0.98f);
            var textGo=Node("Diagnostics",canvasGo.transform);var text=textGo.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=22;text.color=new Color(0.85f,0.91f,0.96f);text.alignment=TextAnchor.UpperLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Overflow;
            var tr=text.rectTransform;tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(18,65);tr.offsetMax=new Vector2(-18,-18);lab.Diagnostics=text;
            void Button(string name,int index,UnityEngine.Events.UnityAction click)
            {
                var go=Node(name,canvasGo.transform);var img=go.AddComponent<Image>();img.color=new Color(0.12f,0.22f,0.3f);var button=go.AddComponent<Button>();UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,click);
                var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,0);r.pivot=new Vector2(0,0);r.anchoredPosition=new Vector2(18+index*202,14);r.sizeDelta=new Vector2(190,40);
                var label=Node("Label",go.transform).AddComponent<Text>();label.font=text.font;label.fontSize=20;label.alignment=TextAnchor.MiddleCenter;label.text=name;var lr=label.rectTransform;lr.anchorMin=Vector2.zero;lr.anchorMax=Vector2.one;lr.offsetMin=lr.offsetMax=Vector2.zero;
            }
            Button("Reload",0,lab.Reload);Button("Dataset",1,lab.NextDataset);Button("Edges",2,lab.ToggleEdges);Button("Reset phi",3,lab.ResetPhi);
            var eventSystem=Node("XR and Desktop Event System");eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();var module=eventSystem.AddComponent<XRUIInputModule>();module.enableMouseInput=true;module.enableXRInput=true;
        }
    }
}
