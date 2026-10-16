using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace
{
    // A bounded 2D parameter control, not a freely thrown 3D object.
    public sealed class PhasonHandleControl : XRSimpleInteractable
    {
        public LabController Lab;
        IXRSelectInteractor owner;
        Vector3 startPointer;
        Double2 startPhi;
        Vector3 normalScale;
        Material feedback;
        void Start()
        {
            normalScale=transform.localScale;var renderer=GetComponent<Renderer>();
            if(GetComponent<BoxCollider>() is BoxCollider box) box.size=new Vector3(1.6f,1.6f,1.2f);
            if(renderer!=null) {feedback=new Material(renderer.sharedMaterial);renderer.sharedMaterial=feedback;}
        }
        void Feedback(bool selected,bool hovered)
        {
            if(feedback!=null) feedback.SetColor("_BaseColor",selected?new Color(.15f,1,.75f):hovered?new Color(.4f,1,1):Color.cyan);
            if(normalScale!=Vector3.zero) transform.localScale=normalScale*(selected?1.18f:hovered?1.12f:1);
        }
        protected override void OnHoverEntered(HoverEnterEventArgs args) {base.OnHoverEntered(args);Feedback(Held,true);}
        protected override void OnHoverExited(HoverExitEventArgs args) {base.OnHoverExited(args);Feedback(Held,false);}
        public bool Held=>owner!=null;
        bool OwnerTracked()
        {
            if(!UnityEngine.XR.XRSettings.isDeviceActive) return true;
            if(owner is XRRayInteractor ray&&ray.GetComponent<ControllerAimRay>() is ControllerAimRay aim) return aim.Tracked;
            var controller=owner.transform;
            while(controller!=null)
            {
                if(controller.name=="Left Controller"||controller.name=="Right Controller")
                {
                    var device=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(controller.name=="Left Controller"?UnityEngine.XR.XRNode.LeftHand:UnityEngine.XR.XRNode.RightHand);
                    return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool tracked)&&tracked;
                }
                var hand=controller.GetComponent<HandPointer>();if(hand!=null) return hand.Available;
                controller=controller.parent;
            }
            return true;
        }
        bool CanManipulate=>Lab!=null&&Lab.State!=null&&!Lab.Busy&&Lab.State.Model.Journal.Count<InteractionModel.MaximumFrames&&
            (Lab.GetComponent<ScientificDemo>() is not ScientificDemo demo||!demo.Guided&&!demo.Capture.Busy&&!demo.Menu.InputBlocked&&!demo.Menu.HasExample&&!demo.QuestUI.Transitioning)&&
            (Lab.GetComponent<LinkedInstrument>() is not LinkedInstrument instrument||!instrument.Playing);

        public override bool IsSelectableBy(IXRSelectInteractor interactor)=>CanManipulate&&QuestProductInput.Allows(interactor.transform,Lab.GetComponent<ScientificDemo>())&&base.IsSelectableBy(interactor);
        bool Pointer(IXRSelectInteractor interactor,out Vector3 point)
        {
            if(interactor is XRRayInteractor ray)
            {
                var origin=ray.rayOriginTransform;
                var plane=new Plane(Lab.View.InternalOrigin.forward,Lab.View.InternalOrigin.position);
                if(origin!=null&&plane.Raycast(new Ray(origin.position,origin.forward),out float distance)&&distance<ray.maxRaycastDistance)
                {point=origin.position+origin.forward*distance;return Finite(point);}
                point=default;return false;
            }
            point=interactor.GetAttachTransform(this).position;return Finite(point);
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if(!CanManipulate||!Pointer(args.interactorObject,out startPointer)) return;
            owner=args.interactorObject;startPhi=Lab.State.Phi;Feedback(true,true);
            Lab.GetComponent<ScientificDemo>()?.Comparison.BeginComparison();
        }
        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);if(owner==args.interactorObject) {owner=null;Lab.GetComponent<ScientificDemo>()?.Comparison.EndComparison();}
            Feedback(false,isHovered);Lab?.SyncHandlePose();
        }
        public void CancelGrab()
        {
            bool wasHeld=owner!=null;
            owner=null;
            if(wasHeld) Lab?.GetComponent<ScientificDemo>()?.Comparison.EndComparison();
            while(interactorsSelecting.Count>0&&interactionManager!=null)
                interactionManager.SelectExit(interactorsSelecting[0],this);
            Lab?.SyncHandlePose();
        }
        public static Vector2 PlaneDelta(Transform frame,Vector3 start,Vector3 current,float scale)
        {
            if(frame==null||!Finite(start)||!Finite(current)||!float.IsFinite(scale)||scale<=0) return Vector2.zero;
            var delta=frame.InverseTransformDirection(current-start)/scale;
            return new Vector2(delta.x,delta.y);
        }
        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase phase)
        {
            base.ProcessInteractable(phase);
            if(phase!=XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            if(owner!=null)
            {
                if(!CanManipulate||!QuestProductInput.Allows(owner.transform,Lab.GetComponent<ScientificDemo>())||!owner.isSelectActive||!OwnerTracked()) {CancelGrab();return;}
                if(Pointer(owner,out var current))
                {
                    var delta=PlaneDelta(Lab.View.InternalOrigin,startPointer,current,Lab.View.InternalScale);
                    Lab.SetPhi(startPhi.X+delta.x,startPhi.Y+delta.y);
                }
            }
            Lab?.SyncHandlePose();
        }
        protected override void OnDisable() {if(owner!=null) Lab?.GetComponent<ScientificDemo>()?.Comparison.EndComparison();base.OnDisable();owner=null;Lab?.SyncHandlePose();}
        void LateUpdate()=>Lab?.SyncHandlePose();
        protected override void OnDestroy() {base.OnDestroy();if(feedback!=null) Destroy(feedback);}
    }
}
