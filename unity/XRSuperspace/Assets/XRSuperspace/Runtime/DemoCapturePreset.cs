using System;
using UnityEngine;

namespace XRSuperspace
{
    [CreateAssetMenu(menuName="XR Superspace/Scientific capture preset")]
    public sealed class DemoCapturePreset : ScriptableObject
    {
        public string Id="paper_16x9",Title="Paper/video 16:9";
        public int Width=1920,Height=1080;
        public double PhiX,PhiY;
        public Vector3 CameraPosition=new Vector3(0,2,-6.1f),CameraEuler;
        public float Fov=42;
        public bool Physical=true,Internal=true,Reciprocal=true,Performance;
        public void Validate()
        {
            DemoCapture.SafeName(Id);
            if(Width<256||Height<256||Width>4096||Height>4096||(long)Width*Height>9000000||float.IsNaN(Fov)||Fov<10||Fov>100||
               double.IsNaN(PhiX)||double.IsNaN(PhiY)||Math.Abs(PhiX)>.25||Math.Abs(PhiY)>.25)
                throw new ArgumentException("capture preset exceeds fixed dimensions/canonical domain");
            foreach(float x in new[]{CameraPosition.x,CameraPosition.y,CameraPosition.z,CameraEuler.x,CameraEuler.y,CameraEuler.z})
                if(float.IsNaN(x)||float.IsInfinity(x)||Math.Abs(x)>100) throw new ArgumentException("invalid preset camera");
        }
    }
}
