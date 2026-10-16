using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace XRSuperspace
{
    public sealed class DesktopInput : MonoBehaviour
    {
        public LabController Lab;
        public Camera ViewCamera;
        public Vector3 Pivot=new Vector3(0,1.15f,1.35f);
        Vector2 angles;
        float distance=3.8f;
        bool draggingHandle;
        void Start()=>SyncCamera();
        public void SyncCamera() { if(ViewCamera!=null) { distance=Vector3.Distance(ViewCamera.transform.position,Pivot);angles=new Vector2(ViewCamera.transform.eulerAngles.y,ViewCamera.transform.eulerAngles.x); } }
        void Update()
        {
            var demo=Lab.GetComponent<ScientificDemo>();
            if(demo!=null&&demo.Desktop!=null) {demo.Desktop.ProcessInput();return;}
            var keyboard=Keyboard.current;
            if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null&&EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>()!=null) return;
            if(keyboard!=null) { if(keyboard.rKey.wasPressedThisFrame) Lab.Reload();if(keyboard.eKey.wasPressedThisFrame) Lab.ToggleEdges();if(keyboard.tabKey.wasPressedThisFrame) Lab.NextDataset();if(keyboard.digit0Key.wasPressedThisFrame) Lab.ResetPhi();
                if(Lab.State!=null) { double step=Time.unscaledDeltaTime*0.12;Lab.SetPhi(Lab.State.Phi.X+(keyboard.rightArrowKey.isPressed?step:0)-(keyboard.leftArrowKey.isPressed?step:0),Lab.State.Phi.Y+(keyboard.upArrowKey.isPressed?step:0)-(keyboard.downArrowKey.isPressed?step:0)); } }
            var mouse=Mouse.current;if(mouse==null||ViewCamera==null||!ViewCamera.enabled) return;
            if(EventSystem.current!=null&&EventSystem.current.IsPointerOverGameObject()&&!draggingHandle) return;
            Vector2 delta=mouse.delta.ReadValue();bool moved=false;
            if(mouse.rightButton.isPressed) { angles+=new Vector2(delta.x,-delta.y)*0.15f;angles.y=Mathf.Clamp(angles.y,-70,70);moved=true; }
            if(mouse.middleButton.isPressed) { Pivot+=(-ViewCamera.transform.right*delta.x-ViewCamera.transform.up*delta.y)*distance*0.001f;moved=true; }
            float scroll=mouse.scroll.ReadValue().y;if(scroll!=0) { distance=Mathf.Clamp(distance-scroll*0.002f,1,8);moved=true; }
            if(moved) { var rotation=Quaternion.Euler(angles.y,angles.x,0);ViewCamera.transform.SetPositionAndRotation(Pivot-rotation*Vector3.forward*distance,rotation); }
            Ray ray=ViewCamera.ScreenPointToRay(mouse.position.ReadValue());
            if(mouse.leftButton.wasPressedThisFrame&&Lab.State!=null) { draggingHandle=Vector3.Distance(ViewCamera.WorldToScreenPoint(Lab.PhasonHandle.position),new Vector3(mouse.position.ReadValue().x,mouse.position.ReadValue().y,ViewCamera.WorldToScreenPoint(Lab.PhasonHandle.position).z))<22;
                if(!draggingHandle) { int a=Lab.View.Pick(ray,false),b=Lab.View.Pick(ray,true);if(a>=0) Lab.Select(a);else if(b>=0) Lab.Select(b); } }
            if(draggingHandle&&mouse.leftButton.isPressed) { var plane=new Plane(Lab.View.InternalOrigin.forward,Lab.View.InternalOrigin.position);if(plane.Raycast(ray,out float enter)) { var local=Lab.View.InternalOrigin.InverseTransformDirection(ray.GetPoint(enter)-Lab.View.InternalOrigin.position)/Lab.View.InternalScale;Lab.SetPhi(local.x,local.y); } }
            if(mouse.leftButton.wasReleasedThisFrame) draggingHandle=false;
        }
    }
}
