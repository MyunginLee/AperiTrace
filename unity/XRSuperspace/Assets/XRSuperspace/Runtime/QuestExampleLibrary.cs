using System;
using System.Collections.Generic;
using System.Linq;

namespace XRSuperspace
{
    public sealed class QuestExample
    {
        public string Id {get;}
        public string Title {get;}
        public string Instruction {get;}
        public Double2 Before {get;}
        public Double2 After {get;}
        readonly AnalysisFilter beforeFilter,afterFilter;
        public AnalysisFilter BeforeFilter=>beforeFilter.Copy();
        public AnalysisFilter AfterFilter=>afterFilter.Copy();
        public bool Candidates {get;}
        public bool Edges {get;}
        public float Band {get;}
        public float PointSize {get;}
        public float Layer {get;}
        public QuestExample(string id,string title,string instruction,Double2 before,Double2 after,
            bool candidates=false,bool edges=true,float band=0,float size=1,float layer=1,
            AnalysisFilter beforeFilter=null,AnalysisFilter afterFilter=null)
        {
            Id=id;Title=title;Instruction=instruction;Before=before;After=after;Candidates=candidates;Edges=edges;Band=band;PointSize=size;Layer=layer;
            this.beforeFilter=(beforeFilter??QuestExampleLibrary.FullFilter()).Copy();this.afterFilter=(afterFilter??QuestExampleLibrary.FullFilter()).Copy();Validate();
        }
        public void Validate()
        {
            DemoCapture.SafeName(Id);
            foreach(var phi in new[]{Before,After}) if(!double.IsFinite(phi.X)||!double.IsFinite(phi.Y)||Math.Abs(phi.X)>.25||Math.Abs(phi.Y)>.25) throw new ArgumentException("example outside canonical offset domain");
            foreach(var filter in new[]{beforeFilter,afterFilter}) {filter.Validate();if(filter.CropKind!="cylinder"||filter.Radius>.9||filter.ZMax>.9||Math.Abs(filter.SliceZ)>.9) throw new ArgumentException("example outside canonical scientific coverage");}
            if(string.IsNullOrWhiteSpace(Title)||string.IsNullOrWhiteSpace(Instruction)||Title.Length>45||!float.IsFinite(Band)||Band<0||Band>.1f||!float.IsFinite(PointSize)||PointSize<.25f||PointSize>3||!float.IsFinite(Layer)||Layer<.25f||Layer>4) throw new ArgumentException("invalid example presentation");
        }
    }
    public static class QuestExampleLibrary
    {
        public static AnalysisFilter FullFilter()=>new AnalysisFilter {Radius=.9,ZMax=.9};
        static Double2 P(double x,double y)=>new Double2(x,y);
        static readonly Double2 zero=P(0,0),right=P(.181,.047),left=P(-.119,.047);
        public static IReadOnlyList<QuestExample> All {get;}=Array.AsReadOnly(new[]{
            new QuestExample("start_here","Start here: 5D to 3D + 2D","A 5D point has a 3D position and 2D selection coordinates.\nLeft: structure. Middle: selection rule. Right: finite diffraction.\nChoose Show before / after, or Explore to try the knob.\nThese are mathematical coordinates, not physical extra dimensions.",zero,zero,true),
            new QuestExample("same_identity","One identity in two views","White marks the same exact identity in both views.\nIts physical position is fixed; its internal coordinates shift.\nThe separation between displays is presentation only.",zero,right,true),
            new QuestExample("window_context","Inside and outside the window","The polygon stays fixed while internal coordinates shift.\nTeal is selected; grey is outside. White is a focused identity.\nIN / OUT means selected / not selected, not right / wrong.",zero,right,true),
            new QuestExample("selected_structure","Build the selected structure","Compare before / after without graph connections.\nSolid rings mark added points; pink dashes mark removed points.\nPhysical coordinates do not move when membership changes.",zero,right,false,false),
            new QuestExample("add_points","Add four points","Four identities enter the active set in this example.\nSolid rings mark additions. Current connections are solid.\nChoose Show before to inspect the starting structure.",zero,right),
            new QuestExample("remove_points","Remove four points","Four identities leave when the offset returns to zero.\nPink dashed rings and connections show what disappeared.\nNo point has been deleted from the candidate dataset.",right,zero),
            new QuestExample("same_count","Same count, different identities","Both endpoints have 15 points, but four leave and four enter.\nCounting points alone does not identify the structure.\nRead the solid and dashed markers in either view.",right,left),
            new QuestExample("upward","Move the selection upward","Compare a positive vertical offset with the starting state.\nThe window stays fixed; internal coordinates move upward.\nRead the local Added / Removed counts.",zero,P(0,.2)),
            new QuestExample("downward","Move the selection downward","Compare a negative vertical offset with the starting state.\nInternal movement is not physical movement of the structure.\nLook for changed membership and connections.",zero,P(0,-.2)),
            new QuestExample("diagonal","Shift on both internal axes","Both components of the 2D offset change.\nGrey dashes show previous internal locations and displacement.\nThe physical view marks selection changes, not motion.",zero,P(.2,.2)),
            new QuestExample("opposite_diagonal","Shift along the opposite diagonal","Compare the negative diagonal offset with the initial state.\nThe same rule acts on the same candidate identities.\nInspect either view using its local comparison.",zero,P(-.2,-.2)),
            new QuestExample("domain_corner","Reach the allowed domain corner","The offset reaches (+0.25,+0.25), the allowed upper corner.\nThe knob stays on its plane and inside the exported domain.\nExplore lets you test the bound without throwing the handle.",zero,P(.25,.25)),
            new QuestExample("tiny_motion","Tiny movement, same structure","These nearby offsets select exactly the same identities.\nInternal coordinates move slightly; Added / Removed stay zero.\nA parameter change need not change the selected structure.",right,P(.181001,.047)),
            new QuestExample("return","Return to the baseline","The after state restores the canonical zero offset.\nCoordinates and selection determine the reproducible structure.\nIdentities persist when points enter or leave.",left,zero),
            new QuestExample("connections","Why connections disappear","Compare equal-count structures with different identities.\nPink dashed edges existed before but are absent now.\nThese are parent-lattice neighbours, not chemical bonds.",right,left),
            new QuestExample("diffraction","Finite diffraction responds","Selected points contribute interfering waves with equal weights.\nPeak size and brightness encode intensity, not peak motion.\nWait for Diffraction: Ready after switching before / after.\nThis is a dimensionless calculation, not a measured X-ray image.",zero,right),
            new QuestExample("unchanged_diffraction","Same set, same diffraction","A tiny offset change leaves the selected set unchanged.\nThe finite diffraction intensities are unchanged too.\nDisplay updates need not indicate a new structure.",right,P(.181001,.047)),
            new QuestExample("crop","Crop the finite physical patch","After applies a cylinder of radius 0.6 to physical coordinates.\nThis is a scientific subset, not a display zoom.\nThe active set, graph and diffraction use the same filtered points.",right,right,afterFilter:new AnalysisFilter {CropEnabled=true,Radius=.6,ZMax=.9}),
            new QuestExample("slice","Inspect a thin physical slice","After selects z = 0 with slice half-width 0.01.\nThe slice acts on physical coordinates, apart from the window.\nRead IN / OUT and wait for its finite diffraction.",right,right,afterFilter:new AnalysisFilter {SliceEnabled=true,SliceZ=0,SliceHalfWidth=.01,Radius=.9,ZMax=.9}),
            new QuestExample("display_only","Bigger display, same calculation","After enlarges markers and separates physical layers for display.\nCoordinates, selection and diffraction remain unchanged.\nA display transform is not a physical deformation.",right,right,size:1.5f,layer:1.8f)
        });
        public static void RequireCanonical(LabState state)
        {
            if(state==null||state.Dataset.Metadata.BinarySha256!=InstrumentCapturePreset.Checksum) throw new InvalidOperationException("Examples require the canonical dataset; no dataset or trace was replaced.");
        }
        public static void EnsureBudget(LabState state,params (Double2 phi,AnalysisFilter filter)[] samples)
        {
            RequireCanonical(state);
            var trial=new InteractionModel(state.Dataset);trial.Apply(state.Phi,state.Model.Filter);trial.ClearJournal();
            foreach(var sample in samples) trial.Apply(sample.phi,sample.filter);
            int Recorded(ChangeFrame f)=>f.Selected.Length+f.Boundary.Length+f.Entered.Length+f.Exited.Length;
            if(state.Model.Journal.Count+trial.Journal.Count-1>InteractionModel.MaximumFrames||state.Model.Journal.Sum(Recorded)+trial.Journal.Skip(1).Sum(Recorded)>InteractionModel.MaximumRecordedIds)
                throw new InvalidOperationException("Trace is full. Save and explicitly clear it before loading an example.");
        }
    }
}
