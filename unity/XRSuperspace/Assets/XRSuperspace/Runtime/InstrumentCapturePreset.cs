using UnityEngine;

namespace XRSuperspace
{
    [CreateAssetMenu(menuName="XR Superspace/Capture preset")]
    public sealed class InstrumentCapturePreset : ScriptableObject
    {
        public string Title;
        public double PhiX,PhiY;
        public string SelectedId="-1,0,-1,0,0";
        public Vector3 CameraPosition=new Vector3(0,.75f,-4.6f),CameraEuler,CameraPivot=new Vector3(0,.75f,1.3f);
        public Vector3 PhysicalOrigin=new Vector3(-1.65f,1.4f,1.3f),InternalOrigin=new Vector3(0,1.4f,1.3f),ReciprocalOrigin=new Vector3(1.65f,1.4f,1.3f),PanelOrigin=new Vector3(-2.3f,-1,1.1f);
        public float Fov=36,LayerSeparation=1,PointSize=1,BoundaryBand=.04f;
        public bool ShowCandidates,GraphVisible=true;
        public const string Checksum="2942d45e5d6bb1c171c2cbf535943217a4c4a7e5c3355a1fa9828af5ec2dd2e8";
    }
}
