using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace XRSuperspace
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PreviewCandidate
    {
        public Vector4 Physical;
        public Vector2 Uv;
        public uint Allowed,Padding;
    }
    // No readback in Dispatch/Compact/draw. GetData exists only in explicit validation.
    public sealed class GpuPointBuffers : IDisposable
    {
        public readonly int Count;
        public readonly GraphicsBuffer Candidates,States,Active,Previous,PhysicalRows,InternalRows,PhysicalArgs,InternalArgs;
        readonly GraphicsBuffer planes,window;
        readonly ComputeShader shader;
        readonly int classify,compact;
        public long EstimatedBytes=>Count*52L+planes.count*16L+window.count*8L+40;
        public GpuPointBuffers(ComputeShader compute,PreviewCandidate[] data,Double2[] vertices,Mesh mesh)
        {
            if(compute==null||mesh==null||data==null) throw new ArgumentNullException();
            PreviewContract.ValidateCounts(data.Length,data.Length,data.Length);var half=PreviewContract.HalfSpaces(vertices);Count=data.Length;
            try {
            shader=UnityEngine.Object.Instantiate(compute);classify=shader.FindKernel("Classify");compact=shader.FindKernel("Compact");
            Candidates=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Count,32);Candidates.SetData(data);
            States=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Count,4);Active=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Count,4);Previous=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Count,4);
            var zero=new uint[Count];States.SetData(zero);Active.SetData(zero);Previous.SetData(zero);
            PhysicalRows=new GraphicsBuffer(GraphicsBuffer.Target.Append,Count,4);InternalRows=new GraphicsBuffer(GraphicsBuffer.Target.Append,Count,4);
            PhysicalArgs=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,5,4);InternalArgs=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,5,4);
            uint[] args={mesh.GetIndexCount(0),0,mesh.GetIndexStart(0),(uint)mesh.GetBaseVertex(0),0};PhysicalArgs.SetData(args);InternalArgs.SetData(args);
            var p=new Vector4[half.Length];var w=new Vector2[half.Length];for(int i=0;i<p.Length;i++) {p[i]=new Vector4((float)half[i].A,(float)half[i].B,(float)half[i].C,0);w[i]=new Vector2((float)vertices[i].X,(float)vertices[i].Y);}
            planes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,p.Length,16);planes.SetData(p);window=new GraphicsBuffer(GraphicsBuffer.Target.Structured,w.Length,8);window.SetData(w);
            foreach(int kernel in new[]{classify,compact}) {shader.SetBuffer(kernel,"_Candidates",Candidates);shader.SetBuffer(kernel,"_States",States);shader.SetBuffer(kernel,"_Active",Active);shader.SetBuffer(kernel,"_Previous",Previous);}
            shader.SetBuffer(classify,"_Planes",planes);shader.SetBuffer(classify,"_Window",window);shader.SetBuffer(compact,"_PhysicalRows",PhysicalRows);shader.SetBuffer(compact,"_InternalRows",InternalRows);shader.SetInt("_Count",Count);shader.SetInt("_EdgeCount",p.Length);
            } catch {Dispose();throw;}
        }
        public void UpdateCandidates(PreviewCandidate[] data) {PreviewContract.ValidateCounts(Count,data.Length,data.Length);Candidates.SetData(data);}
        public void Dispatch(Double2 phi,double epsilon,bool include,int stride,bool context,bool boundary,int selectedRow=-1)
        {
            if(double.IsNaN(phi.X)||double.IsInfinity(phi.X)||double.IsNaN(phi.Y)||double.IsInfinity(phi.Y)||epsilon<=0||double.IsNaN(epsilon)||double.IsInfinity(epsilon)) throw new ArgumentException("invalid preview operands");
            shader.SetVector("_Phi",new Vector4((float)phi.X,(float)phi.Y,0,0));shader.SetFloat("_Epsilon",(float)epsilon);shader.SetInt("_IncludeBoundary",include?1:0);shader.Dispatch(classify,(Count+63)/64,1,1);Compact(stride,context,boundary,selectedRow);
        }
        public void Compact(int stride,bool context,bool boundary,int selectedRow=-1)
        {
            if(stride<1||stride>Count) throw new ArgumentOutOfRangeException(nameof(stride));PhysicalRows.SetCounterValue(0);InternalRows.SetCounterValue(0);
            if(selectedRow< -1||selectedRow>=Count) throw new ArgumentOutOfRangeException(nameof(selectedRow));shader.SetInt("_SelectedRow",selectedRow);
            shader.SetInt("_Stride",stride);shader.SetInt("_Context",context?1:0);shader.SetInt("_BoundaryPoints",boundary?1:0);shader.Dispatch(compact,(Count+63)/64,1,1);
            GraphicsBuffer.CopyCount(PhysicalRows,PhysicalArgs,4);GraphicsBuffer.CopyCount(InternalRows,InternalArgs,4);
        }
        public void UploadAuthority(PointState[] states,bool[] active)
        {
            PreviewContract.ValidateCounts(Count,states.Length,active.Length);var s=new uint[Count];var a=new uint[Count];for(int i=0;i<Count;i++) {s[i]=(uint)states[i];a[i]=active[i]?1u:0u;}States.SetData(s);Active.SetData(a);Previous.SetData(a);
        }
        public void Read(uint[] states,uint[] active) {PreviewContract.ValidateCounts(Count,states.Length,active.Length);States.GetData(states);Active.GetData(active);}
        public void Dispose()
        {
            Candidates?.Dispose();States?.Dispose();Active?.Dispose();Previous?.Dispose();PhysicalRows?.Dispose();InternalRows?.Dispose();PhysicalArgs?.Dispose();InternalArgs?.Dispose();planes?.Dispose();window?.Dispose();
            if(shader!=null) {if(Application.isPlaying) UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
