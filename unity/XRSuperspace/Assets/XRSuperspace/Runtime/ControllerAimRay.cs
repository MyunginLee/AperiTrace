using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace
{
    // The controller body/direct-grab stays on grip pose. Only the ray uses aim pose.
    public sealed class ControllerAimRay : MonoBehaviour
    {
        public Transform AimOrigin {get;private set;}
        InputAction position,rotation,tracking;
        XRRayInteractor target;
        bool leftHand;
        ScientificDemo demo;
        UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual visual;
        public bool Tracked=>tracking!=null&&tracking.ReadValue<float>()>.5f;
        public bool LeftHand=>leftHand;
        // The visible pointer must use exactly the pose used for hit testing.
        // Template line overrides bend a grip-origin line toward an aim-pose hit.
        public static void ConfigureStraightPointer(XRRayInteractor ray,UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual line,Transform origin)
        {
            ray.rayOriginTransform=origin;
            ray.lineType=XRRayInteractor.LineType.StraightLine;
            ray.blendVisualLinePoints=false;
            if(line==null) return;
            line.overrideInteractorLineOrigin=false;line.lineOriginTransform=origin;
            line.smoothMovement=false;line.snapEndpointIfAvailable=false;
            line.bendingEnabledInteractionLayers=0;line.lineBendRatio=1f;
        }
        public static string PositionBinding(bool left)=>"<XRController>{"+(left?"Left":"Right")+"Hand}/pointerPosition";
        public static string RotationBinding(bool left)=>"<XRController>{"+(left?"Left":"Right")+"Hand}/pointerRotation";
        public void Configure(XRRayInteractor ray,Transform trackingSpace,bool left)
        {
            if(AimOrigin!=null) return;leftHand=left;target=ray;visual=ray.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            var go=new GameObject((left?"Left":"Right")+" controller aim pose");
            go.transform.SetParent(trackingSpace,false);AimOrigin=go.transform;
            position=new InputAction("Aim position",InputActionType.Value,PositionBinding(left),expectedControlType:"Vector3");
            rotation=new InputAction("Aim rotation",InputActionType.Value,RotationBinding(left),expectedControlType:"Quaternion");
            tracking=new InputAction("Aim tracking",InputActionType.Value,"<XRController>{"+(left?"Left":"Right")+"Hand}/isTracked");
            var driver=go.AddComponent<TrackedPoseDriver>();
            driver.positionInput=new InputActionProperty(position);driver.rotationInput=new InputActionProperty(rotation);
            driver.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            position.Enable();rotation.Enable();tracking.Enable();ConfigureStraightPointer(ray,visual,AimOrigin);
        }
        void Update()
        {
            if(!UnityEngine.XR.XRSettings.isDeviceActive||target==null) return;
            if(demo==null) demo=FindFirstObjectByType<ScientificDemo>();
            bool allowed=demo==null||!demo.Lifecycle.Suspended&&(demo.Preferences.Controllers==0||demo.Preferences.Controllers==(leftHand?1:2));
            target.enabled=Tracked&&allowed;if(visual!=null) visual.enabled=Tracked&&allowed;
            if((!Tracked||!allowed)&&target.GetComponent<LineRenderer>() is LineRenderer line) line.enabled=false;
        }
        void OnDestroy()
        {
            if(AimOrigin!=null) Destroy(AimOrigin.gameObject);
            position?.Dispose();rotation?.Dispose();tracking?.Dispose();
        }
    }
}
