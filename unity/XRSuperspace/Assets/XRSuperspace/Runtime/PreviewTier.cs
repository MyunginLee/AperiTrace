using UnityEngine;

namespace XRSuperspace
{
    [CreateAssetMenu(menuName="XR Superspace/Preview tier")]
    public sealed class PreviewTier : ScriptableObject
    {
        public string Title="Desktop";
        [Min(1)] public int PointBudget=20000;
        [Range(0,10000)] public int EdgeBudget=1024;
        public bool InternalView=true,Labels=true,BoundaryPoints,ValidateOnLoad;
        [Range(.1f,3)] public float PointSize=.22f;
        [Min(1)] public int FrameTarget=72;
        public int Stride(int count)=>PreviewContract.DisplayStride(count,PointBudget);
    }
}
