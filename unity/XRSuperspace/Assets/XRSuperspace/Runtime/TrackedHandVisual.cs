using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace XRSuperspace
{
    // Original tracked silhouette, one reusable mesh, no collider or UI target.
    public sealed class TrackedHandVisual : MonoBehaviour
    {
        public bool Visible=>surface!=null&&surface.enabled;
        public int ValidSegments {get;private set;}
        static readonly XRHandJointID[][] fingers={
            new[]{XRHandJointID.Wrist,XRHandJointID.ThumbMetacarpal,XRHandJointID.ThumbProximal,XRHandJointID.ThumbDistal,XRHandJointID.ThumbTip},
            new[]{XRHandJointID.Wrist,XRHandJointID.IndexMetacarpal,XRHandJointID.IndexProximal,XRHandJointID.IndexIntermediate,XRHandJointID.IndexDistal,XRHandJointID.IndexTip},
            new[]{XRHandJointID.Wrist,XRHandJointID.MiddleMetacarpal,XRHandJointID.MiddleProximal,XRHandJointID.MiddleIntermediate,XRHandJointID.MiddleDistal,XRHandJointID.MiddleTip},
            new[]{XRHandJointID.Wrist,XRHandJointID.RingMetacarpal,XRHandJointID.RingProximal,XRHandJointID.RingIntermediate,XRHandJointID.RingDistal,XRHandJointID.RingTip},
            new[]{XRHandJointID.Wrist,XRHandJointID.LittleMetacarpal,XRHandJointID.LittleProximal,XRHandJointID.LittleIntermediate,XRHandJointID.LittleDistal,XRHandJointID.LittleTip}
        };
        static readonly XRHandJointID[] bases={XRHandJointID.ThumbProximal,XRHandJointID.IndexProximal,XRHandJointID.MiddleProximal,XRHandJointID.RingProximal,XRHandJointID.LittleProximal};
        readonly List<Vector3> vertices=new(700),normals=new(700);
        readonly List<int> triangles=new(2500);
        Mesh mesh;MeshRenderer surface;Material material;
        public void Configure()
        {
            if(mesh!=null) return;mesh=new Mesh {name="Tracked hand silhouette"};mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;surface=gameObject.AddComponent<MeshRenderer>();
            material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));surface.sharedMaterial=material;
            surface.shadowCastingMode=ShadowCastingMode.Off;surface.receiveShadows=false;surface.enabled=false;
        }
        public void Draw(XRHand hand,bool allowed,bool pinching)
        {
            if(mesh==null) Configure();vertices.Clear();triangles.Clear();normals.Clear();ValidSegments=0;
            if(!allowed||!hand.isTracked) {Hide();return;}
            foreach(var finger in fingers) for(int i=1;i<finger.Length;i++)
                if(hand.GetJoint(finger[i-1]).TryGetPose(out var a)&&hand.GetJoint(finger[i]).TryGetPose(out var b))
                {Tube(a.position,b.position,i==1?.0065f:.005f);ValidSegments++;}
            if(hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist)) for(int i=1;i<bases.Length;i++)
                if(hand.GetJoint(bases[i-1]).TryGetPose(out var a)&&hand.GetJoint(bases[i]).TryGetPose(out var b)) Palm(wrist.position,a.position,b.position);
            Commit(pinching);
        }
        void Tube(Vector3 a,Vector3 b,float radius)
        {
            Vector3 axis=b-a;if(axis.sqrMagnitude<1e-10f) return;axis.Normalize();
            Vector3 u=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right).normalized,v=Vector3.Cross(axis,u);int start=vertices.Count;
            const int sides=8;
            for(int ring=0;ring<2;ring++) for(int i=0;i<sides;i++)
            {float angle=2*Mathf.PI*i/sides;var radial=u*Mathf.Cos(angle)+v*Mathf.Sin(angle);vertices.Add((ring==0?a:b)+radial*radius);normals.Add(radial);}
            for(int i=0;i<sides;i++)
            {int n=(i+1)%sides;triangles.Add(start+i);triangles.Add(start+n);triangles.Add(start+sides+i);triangles.Add(start+n);triangles.Add(start+sides+n);triangles.Add(start+sides+i);}
            int ca=vertices.Count;vertices.Add(a);normals.Add(-axis);int cb=vertices.Count;vertices.Add(b);normals.Add(axis);
            for(int i=0;i<sides;i++)
            {int n=(i+1)%sides;triangles.Add(ca);triangles.Add(start+n);triangles.Add(start+i);triangles.Add(cb);triangles.Add(start+sides+i);triangles.Add(start+sides+n);}
        }
        void Palm(Vector3 a,Vector3 b,Vector3 c)
        {
            int start=vertices.Count;Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
            vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(a);vertices.Add(c);vertices.Add(b);
            for(int i=0;i<6;i++) {normals.Add(i<3?normal:-normal);triangles.Add(start+i);}
        }
        void Commit(bool pinching)
        {
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            material.SetColor("_BaseColor",pinching?new Color(.18f,.94f,.79f):new Color(.68f,.79f,.85f));surface.enabled=vertices.Count>0;
        }
        public void Hide() {if(surface!=null) surface.enabled=false;ValidSegments=0;}
#if UNITY_EDITOR
        public void PreviewSegment(Vector3 start,Vector3 end)
        {Configure();vertices.Clear();normals.Clear();triangles.Clear();Tube(start,end,.006f);ValidSegments=1;Commit(false);}
#endif
        void OnDestroy() {if(mesh!=null) Destroy(mesh);if(material!=null) Destroy(material);}
    }
}
