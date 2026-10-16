using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace
{
    // Publish pinch edges before the UI module (-200) and interaction manager (-105).
    [DefaultExecutionOrder(-15000)]
    public sealed class HandPointer : MonoBehaviour
    {
        public bool Left;
        public Transform TrackingSpace;
        public Transform Endpoint;
        public XRRayInteractor RayInteractor;
        public XRDirectInteractor DirectInteractor;
        public bool Available { get; private set; }
        public bool Pinching {get;private set;}
        public bool UsingNativeAim {get;private set;}
        public int PressEdges {get;private set;}
        public int ReleaseEdges {get;private set;}
        public bool HandVisible=>visual!=null&&visual.Visible;
        readonly List<XRHandSubsystem> subsystems=new();
        readonly HandPinchGate gate=new();
        float nextDiscovery;
        bool pinching;
        ScientificDemo demo;
        Transform aimOrigin;
        TrackedHandVisual visual;
        UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual pointerLine;
        void OnEnable()=>Application.onBeforeRender+=BeforeRender;
        void Start()
        {
            if(TrackingSpace==null||Endpoint==null||RayInteractor==null||DirectInteractor==null) return;
            aimOrigin=new GameObject((Left?"Left":"Right")+" hand aim").transform;
            aimOrigin.SetParent(TrackingSpace,false);
            var line=RayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            pointerLine=line;
            ControllerAimRay.ConfigureStraightPointer(RayInteractor,line,aimOrigin);
            visual=new GameObject((Left?"Left":"Right")+" tracked hand").AddComponent<TrackedHandVisual>();
            visual.transform.SetParent(TrackingSpace,false);visual.Configure();
        }
        public static bool DistancePinch(float gap,bool previous)=>float.IsFinite(gap)&&gap>=0&&gap<(previous?.035f:.025f);
        public static bool NativeAimAllowed(MetaAimFlags flags)=>
            (flags&(MetaAimFlags.Computed|MetaAimFlags.Valid))==(MetaAimFlags.Computed|MetaAimFlags.Valid)&&
            (flags&MetaAimFlags.SystemGesture)==0;
        void Update()
        {
            if(aimOrigin==null) return;
            if(Time.unscaledTime>=nextDiscovery) { subsystems.Clear();SubsystemManager.GetSubsystems(subsystems);nextDiscovery=Time.unscaledTime+1; }
            Available=false;UsingNativeAim=false;bool rawPinch=false,rayReady=false,drawn=false;
            if(demo==null) demo=FindFirstObjectByType<ScientificDemo>();
            // Manual pause still permits UI Resume; scene manipulation has its own guard.
            bool allowed=QuestProductInput.Allows(transform,demo);
            foreach(var subsystem in subsystems)
            {
                if(!subsystem.running) continue;var hand=Left?subsystem.leftHand:subsystem.rightHand;
                if(!hand.isTracked) continue;
                visual.Draw(hand,allowed,Pinching);drawn=true;
                if(!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip)||!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb)) break;
                Available=true;Endpoint.position=TrackingSpace.TransformPoint((tip.position+thumb.position)*.5f);
                Endpoint.rotation=TrackingSpace.rotation*hand.rootPose.rotation;
                if(subsystem.TryGetAimState(Left?Handedness.Left:Handedness.Right,out var aimState))
                {
                    var meta=new MetaAimHandState(in aimState);UsingNativeAim=true;
                    bool valid=NativeAimAllowed(meta.aimFlags)&&meta.TryGetAimPose(out _);
                    rawPinch=valid&&meta.indexPressed;
                    if(valid&&meta.TryGetAimPose(out var pose))
                    {aimOrigin.SetPositionAndRotation(TrackingSpace.TransformPoint(pose.position),TrackingSpace.rotation*pose.rotation);rayReady=true;}
                }
                else if(hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out var proximal))
                {
                    pinching=DistancePinch(Vector3.Distance(tip.position,thumb.position),pinching);rawPinch=pinching;
                    // A shoulder-to-hand fallback stays stable as the index bends into a pinch.
                    var camera=demo?.QuestUI?.Viewer;Vector3 origin=TrackingSpace.TransformPoint(proximal.position);
                    Vector3 direction=camera!=null?origin-(camera.transform.position+camera.transform.right*(Left?-.18f:.18f)-camera.transform.up*.15f):TrackingSpace.TransformDirection(tip.position-proximal.position);
                    if(direction.sqrMagnitude>1e-8f) {aimOrigin.SetPositionAndRotation(origin,Quaternion.LookRotation(direction,TrackingSpace.up));rayReady=true;}
                }
                break;
            }
            if(!drawn) visual.Hide();
            Endpoint.gameObject.SetActive(Available&&allowed);
            bool previous=Pinching;Pinching=gate.Update(Available&&allowed&&(!UsingNativeAim||rayReady),rawPinch);
            if(Pinching&&!previous) PressEdges++;if(!Pinching&&previous) ReleaseEdges++;
            RayInteractor.enabled=Available&&allowed&&rayReady;
            if(pointerLine!=null) pointerLine.enabled=RayInteractor.enabled;
            if(!RayInteractor.enabled&&RayInteractor.GetComponent<LineRenderer>() is LineRenderer line) line.enabled=false;
            Write(RayInteractor.selectInput,Pinching&&rayReady);Write(RayInteractor.uiPressInput,Pinching&&rayReady);Write(DirectInteractor.selectInput,Pinching);
            if(!Available||!allowed) pinching=false;
        }
        void BeforeRender()
        {
            // Pose-only refresh: never consume/publish input edges before rendering.
            if(visual==null||!isActiveAndEnabled) return;
            foreach(var subsystem in subsystems)
            {if(!subsystem.running) continue;var hand=Left?subsystem.leftHand:subsystem.rightHand;
                if(hand.isTracked) {visual.Draw(hand,QuestProductInput.Allows(transform,demo),Pinching);return;}}
            visual.Hide();
        }
        public static void Write(XRInputButtonReader reader,bool pressed)
        {
            if(reader==null) return;reader.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            bool previous=reader.manualPerformed;reader.manualPerformed=pressed;reader.manualValue=pressed?1:0;
            if(pressed&&!previous) reader.manualFramePerformed=Time.frameCount;
            if(!pressed&&previous) reader.manualFrameCompleted=Time.frameCount;
        }
        void OnDisable()
        {
            Application.onBeforeRender-=BeforeRender;Available=false;Pinching=false;gate.Reset();pinching=false;
            Write(RayInteractor?.selectInput,false);Write(RayInteractor?.uiPressInput,false);Write(DirectInteractor?.selectInput,false);
            if(Endpoint!=null) Endpoint.gameObject.SetActive(false);visual?.Hide();
        }
        void OnDestroy() {if(aimOrigin!=null) Destroy(aimOrigin.gameObject);if(visual!=null) Destroy(visual.gameObject);}
    }
    // Tracking/focus restoration must observe a released pinch before selecting.
    public sealed class HandPinchGate
    {
        bool armed;
        public bool Update(bool available,bool pressed)
        {if(!available) {armed=false;return false;}if(!pressed) armed=true;return armed&&pressed;}
        public void Reset()=>armed=false;
    }
}
