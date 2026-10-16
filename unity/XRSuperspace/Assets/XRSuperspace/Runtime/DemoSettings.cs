using UnityEngine;

namespace XRSuperspace
{
    [CreateAssetMenu(menuName="XR Superspace/Scientific demo settings")]
    public sealed class DemoSettings : ScriptableObject
    {
        public bool StartGuided=true;
        public float StageSeconds=6,SampleSeconds=.15f;
        public string FocusId="-1,0,-1,0,0";
        public Color Background=new Color(.035f,.04f,.045f);
        public Vector3 PanelPosition=new Vector3(0,4.24f,1.1f);
        [Header("Quest presentation (desktop capture settings remain unchanged)")]
        [Range(1.3f,2.5f)] public float QuestCardDistance=1.75f;
        [Range(.05f,.8f)] public float QuestCardBelowEyes=.6f;
        [Range(0,.45f)] public float QuestViewAboveEyes=.24f;
        [Range(1.8f,3)] public float QuestViewDistance=2.3f;
        [Range(.8f,1.4f)] public float QuestViewSpacing=1.2f;
        [Range(.2f,1)] public float QuestFadeSeconds=.45f;
        [Range(4,20)] public float QuestReadSeconds=6;
        [Range(10,30)] public float QuestStageSeconds=14;
        [Header("Quest sampled-state comparison (display only)")]
        [Range(16,128)] public int ComparisonPointBudget=64;
        [Range(16,256)] public int ComparisonEdgeBudget=128;
        public float ComparisonRadius=.033f,ComparisonWidth=.0035f,ComparisonDash=.028f,ComparisonGap=.019f;
        [Header("App screenshot (mono, app-local PNG + state)")]
        public int QuestScreenshotWidth=1920,QuestScreenshotHeight=1080;
        public Color Selected=new Color(.15f,.8f,.7f),Boundary=new Color(1,.65f,.1f),Rejected=new Color(.35f,.38f,.42f);
        // Accessibility-friendly categorical option, not tested accessibility evidence.
        public Color AccessibleSelected=new Color(.1f,.55f,1),AccessibleBoundary=new Color(1,.6f,.05f),AccessibleRejected=new Color(.5f,.5f,.5f);
        public DemoCapturePreset[] Captures;
    }
}
