using UnityEngine;

namespace XRSuperspace
{
    [CreateAssetMenu(menuName="XR Superspace/Lab settings")]
    public sealed class LabSettings : ScriptableObject
    {
        public string[] Datasets={"canonical_small_v1/canonical.qc5d","modelset_small/modelset_small.qc5d"};
        [Range(31,50000)] public int MaximumInteractiveCandidates=20000;
        [Range(1,10000)] public int MaximumRenderedEdges=2048;
        public float PhysicalFitRadius=0.65f;
        public float InternalFitScale=0.55f;
        public float PointRadius=0.018f;
        public Color SelectedColor=new Color(0.15f,0.85f,0.75f);
        public Color RejectedColor=new Color(0.22f,0.28f,0.36f);
        public Color BoundaryColor=new Color(1f,0.65f,0.1f);
    }
}
