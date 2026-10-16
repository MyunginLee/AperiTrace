using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace.Editor
{
    // Real XRI/UI processing with synthetic pinch states, NOT actual tracking evidence.
    [DefaultExecutionOrder(-15000)]
    public sealed class HandPinchFramePublisher : MonoBehaviour
    {
        public XRRayInteractor Ray;
        public bool Pressed;
        void Update() {HandPointer.Write(Ray.selectInput,Pressed);HandPointer.Write(Ray.uiPressInput,Pressed);}
    }
    [InitializeOnLoad]
    public static class HandInteractionSmoke
    {
        const string Key="AperiTrace.HandSmoke";
        static double deadline;
        static int phase,frame;
        static ScientificDemo demo;
        static XRRayInteractor ray;
        static HandPinchFramePublisher publisher;
        static string output;
        static readonly JObject evidence=new JObject { ["scope"]="Real Editor XRI/UI pipeline with synthetic hand pinch states; not Quest tracking or human usability" };
        static HandInteractionSmoke() {if(SessionState.GetBool(Key,false)) Attach();}
        static void Attach() {deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        public static void BatchRun()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-hand-output");
            if(index<0||index+1>=args.Length) throw new ArgumentException("-hand-output required");
            string path=Path.GetFullPath(args[index+1]);if(Directory.Exists(path)) throw new IOException("Preserve prior evidence");
            Directory.CreateDirectory(path);SessionState.SetString(Key+".output",path);SessionState.SetBool(Key,true);
            EditorSceneManager.OpenScene(DemoSetup.ScenePath,OpenSceneMode.Single);Attach();EditorApplication.EnterPlaymode();
        }
        static void Check(bool condition,string error) {if(!condition) throw new InvalidOperationException(error);}
        static Button Target(string text)=>demo.QuestUI.GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text==text);
        static void Aim(Button button)
        {var position=button.transform.TransformPoint(((RectTransform)button.transform).rect.center);ray.transform.SetPositionAndRotation(demo.Instrument.Camera.transform.position,Quaternion.LookRotation(position-demo.Instrument.Camera.transform.position));}
        static void Tick()
        {
            try
            {
                if(output==null) output=SessionState.GetString(Key+".output","");
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Hand input pipeline timed out at phase "+phase+" state="+evidence["last_state"]);
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
                if(demo==null) demo=UnityEngine.Object.FindFirstObjectByType<ScientificDemo>();
                if(demo==null||demo.Lab.State==null||demo.Lab.Busy||demo.QuestUI.Group==null||!demo.Instrument.Reciprocal.Current) return;
                evidence["last_state"]=new JObject { ["phase"]=phase,["alpha"]=demo.QuestUI.Group.alpha,["blocks"]=demo.QuestUI.Group.blocksRaycasts,
                    ["transition"]=demo.QuestUI.Transitioning,["suspended"]=demo.Lifecycle.Suspended,["menu_blocked"]=demo.Menu.InputBlocked,["step"]=demo.Step };
                if(phase==0)
                {
                    demo.Lifecycle.SetFocused(true);demo.Lifecycle.SetPaused(false);
                    demo.Preferences.Controllers=demo.Preferences.View=demo.Preferences.TextSize=demo.Preferences.UiSize=0;
                    demo.QuestUI.ForcePreview=true;demo.Preferences.ReducedMotion=true;demo.StartGuide();
                    var go=new GameObject("Synthetic hand ray");ray=go.AddComponent<XRRayInteractor>();
                    ray.rayOriginTransform=ray.transform;ray.maxRaycastDistance=5;ray.enableUIInteraction=true;
                    ray.selectInput=new XRInputButtonReader();ray.uiPressInput=new XRInputButtonReader();
                    publisher=go.AddComponent<HandPinchFramePublisher>();publisher.Ray=ray;phase=1;frame=Time.frameCount;return;
                }
                if(Time.frameCount<frame+4||demo.QuestUI.Transitioning) return;
                if(phase==1)
                {if(demo.QuestUI.Group.alpha<.99f||!demo.QuestUI.Group.blocksRaycasts||!Target("Next").interactable) return;Aim(Target("Next"));frame=Time.frameCount;phase=2;return;}
                if(phase==2)
                {
                    if(!ray.TryGetCurrentUIRaycastResult(out var hit)||hit.gameObject==null) {Aim(Target("Next"));return;}
                    evidence["initial_ui_hit"]=hit.gameObject.name;publisher.Pressed=true;frame=Time.frameCount;phase=3;return;
                }
                if(phase==3) {publisher.Pressed=false;frame=Time.frameCount;phase=4;return;}
                if(phase==4)
                {
                    Check(demo.Step==1,"Pinch press/release failed to activate Next exactly once; step="+demo.Step);
                    evidence["pinch_ui_click_advanced_once"]=true;
                    var hand=new GameObject("Synthetic visible hand").AddComponent<TrackedHandVisual>();
                    hand.transform.position=demo.Instrument.Camera.transform.position+demo.Instrument.Camera.transform.forward*.5f;
                    hand.PreviewSegment(new Vector3(-.035f,-.04f,0),new Vector3(.035f,.04f,0));
                    var camera=demo.Instrument.Camera;var rt=new RenderTexture(1280,720,24);var old=camera.targetTexture;var active=RenderTexture.active;
                    var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                    try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,"synthetic_hand_visible.png"),image.EncodeToPNG());}
                    finally {camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.Destroy(image);rt.Release();UnityEngine.Object.Destroy(rt);}
                    Check(hand.Visible&&hand.GetComponent<Renderer>().sharedMaterial.shader.isSupported,"Hand visual shader/renderer unavailable");
                    evidence["synthetic_hand_renderer_and_capture"]=true;UnityEngine.Object.Destroy(hand.gameObject);
                    evidence["passed"]=true;Finish(0);
                }
            }
            catch(Exception error) {evidence["passed"]=false;evidence["error"]=error.ToString();Debug.LogError(error);Finish(1);}
        }
        static void Finish(int code)
        {File.WriteAllText(Path.Combine(output,"evidence.json"),evidence.ToString());SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
