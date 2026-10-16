using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace
{
    // A copied, sampled-state reference. Never an interpolated membership or an event detector.
    public sealed class SelectionComparison
    {
        public readonly LabState Source;
        public readonly Double2 Phi;
        readonly bool[] active;
        readonly GraphEdge[] edges;
        public SelectionComparison(LabState state)
        {
            Source=state??throw new ArgumentNullException(nameof(state));Phi=state.Phi;
            active=(bool[])state.Model.Active.Clone();edges=(GraphEdge[])state.Edges.Clone();
        }
        public bool WasActive(int row)=>active[row];
        public int Change(int row,bool now)=>now?(active[row]?0:1):(active[row]?-1:0);
        public static long EdgeKey(GraphEdge edge)=>((long)Math.Min(edge.Source,edge.Target)<<32)|(uint)Math.Max(edge.Source,edge.Target);
        public GraphEdge[] RemovedEdges(GraphEdge[] now)
        {
            var current=new HashSet<long>();foreach(var edge in now) current.Add(EdgeKey(edge));
            var removed=new List<GraphEdge>();foreach(var edge in edges) if(!current.Contains(EdgeKey(edge))) removed.Add(edge);
            return removed.ToArray();
        }
        public (int added,int removed,int retained) Counts(bool[] now)
        {
            if(now.Length!=active.Length) throw new ArgumentException("comparison candidate count changed");
            int added=0,removed=0,retained=0;
            for(int i=0;i<now.Length;i++) {int change=Change(i,now[i]);if(change>0) added++;else if(change<0) removed++;else if(now[i]) retained++;}
            return (added,removed,retained);
        }
    }

    // Batched ribbons, not one GameObject per point/dash. Visible only in the Quest coach.
    [DefaultExecutionOrder(250)]
    public sealed class SelectionComparisonOverlay : MonoBehaviour
    {
        public ScientificDemo Demo;
        public SelectionComparison Baseline {get;private set;}
        public bool Pinned {get;private set;}
        bool PresentationActive=>(Demo.QuestUI!=null&&Demo.QuestUI.Active)||(Demo.Desktop!=null&&Demo.Desktop.Active);
        public bool Visible=>PresentationActive&&Baseline!=null&&
            Baseline.Source==Demo.Lab.State&&(!Demo.Guided||Demo.Step>=6);
        public bool Enabled=true;
        public string Summary {get;private set;}="No change yet";
        public int Added {get;private set;}
        public int Removed {get;private set;}
        public int Retained {get;private set;}
        public int RibbonSegments {get;private set;}
        readonly List<(Mesh mesh,MeshRenderer renderer,Material material,int view)> layers=new();
        readonly List<TextMesh> badges=new();
        bool dirty=true,previousEnabled,wasActive;
        Vector3 oldPhysical,oldInternal;
        Quaternion oldPhysicalRotation,oldInternalRotation;
        float oldScale,oldPhysicalScale,oldLayer,oldSize;
        void Start()
        {
            Demo.Lab.ModelChanging+=BeforeChange;Demo.Lab.ModelChanged+=Changed;Demo.Lab.View.Rebuilt+=Rebuilt;
            for(int view=0;view<2;view++)
            {
                for(int kind=0;kind<3;kind++)
                {
                    var go=new GameObject("Comparison "+view+" / "+kind);go.transform.SetParent(transform,false);
                    var mesh=new Mesh {name=go.name,indexFormat=IndexFormat.UInt32};go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();var material=new Material(Demo.Lab.View.LineMaterial);
                    material.SetColor("_BaseColor",kind==0?new Color(.62f,.68f,.72f):kind==1?new Color(1,.35f,.73f):Demo.Lab.Settings.SelectedColor);
                    renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                    layers.Add((mesh,renderer,material,view));
                }
                var label=new GameObject("Local change legend").AddComponent<TextMesh>();label.transform.SetParent(transform,false);
                label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.fontSize=64;
                label.characterSize=.01f;label.color=new Color(.78f,.87f,.9f);badges.Add(label);
                label.font=QuestUiTheme.Font();label.GetComponent<Renderer>().sharedMaterial=label.font.material;
            }
            if(PresentationActive&&Demo.Lab.State!=null) Capture();
        }
        public void Capture()
        {
            Baseline=Demo.Lab.State==null?null:new SelectionComparison(Demo.Lab.State);dirty=true;UpdateSummary();
        }
        public void BeginComparison() {if(!PresentationActive) {Pinned=false;return;}Capture();Pinned=true;}
        public void EndComparison()=>Pinned=false;
        void BeforeChange() {if(PresentationActive&&!Pinned) Capture();}
        void Changed() {if(PresentationActive) {dirty=true;UpdateSummary();}}
        void Rebuilt()
        {
            dirty=true;
            for(int i=0;i<layers.Count;i++) if(i%3==2) layers[i].material.SetColor("_BaseColor",Demo.Lab.Settings.SelectedColor);
        }
        void UpdateSummary()
        {
            if(Baseline==null||Baseline.Source!=Demo.Lab.State) {Added=Removed=Retained=0;Summary="No comparison yet";return;}
            var counts=Baseline.Counts(Demo.Lab.State.Model.Active);Added=counts.added;Removed=counts.removed;Retained=counts.retained;
            Summary=$"Added {Added} | Removed {Removed} | Retained {Retained}";
        }
        // Each dash is an actual separated ribbon; solid edges never connect across spaces.
        public static Vector2[] DashIntervals(float length,float dash,float gap)
        {
            if(!float.IsFinite(length)||length<=0||!float.IsFinite(dash)||dash<=0||!float.IsFinite(gap)||gap<=0) return Array.Empty<Vector2>();
            var intervals=new List<Vector2>();
            for(float start=0;start<length&&intervals.Count<256;start+=dash+gap) intervals.Add(new Vector2(start/length,Mathf.Min(start+dash,length)/length));
            return intervals.ToArray();
        }
        sealed class Ribbons
        {
            public readonly List<Vector3> Vertices=new();public readonly List<int> Indices=new();
            readonly Transform owner;readonly Vector3 normal;readonly float width;
            public Ribbons(Transform owner,Vector3 normal,float width) {this.owner=owner;this.normal=normal;this.width=width;}
            public void Segment(Vector3 a,Vector3 b)
            {
                var side=Vector3.Cross(normal,b-a);if(side.sqrMagnitude<1e-12) return;side=side.normalized*width*.5f;
                int i=Vertices.Count;foreach(var p in new[]{a-side,a+side,b+side,b-side}) Vertices.Add(owner.InverseTransformPoint(p));
                Indices.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            }
            public void Dashed(Vector3 a,Vector3 b,float dash,float gap)
            {
                foreach(var interval in DashIntervals(Vector3.Distance(a,b),dash,gap)) Segment(Vector3.Lerp(a,b,interval.x),Vector3.Lerp(a,b,interval.y));
            }
            public void Ring(Vector3 center,Quaternion rotation,float radius,bool dashed)
            {
                for(int i=0;i<32;i++) {if(dashed&&i%4>=2) continue;
                    float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;
                    Segment(center+rotation*new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0),center+rotation*new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,0));}
            }
        }
        Vector3 PreviousPoint(int row)
        {
            var lab=Demo.Lab;var uv=lab.State.Dataset.Candidates[row].Internal;
            return lab.View.InternalOrigin.position+lab.View.InternalOrigin.TransformDirection(new Vector3((float)(uv.X+Baseline.Phi.X),(float)(uv.Y+Baseline.Phi.Y),0)*lab.View.InternalScale);
        }
        void Build()
        {
            var lab=Demo.Lab;var view=lab.View;RibbonSegments=0;
            var removedEdges=Baseline.RemovedEdges(lab.State.Edges);
            for(int space=0;space<2;space++)
            {
                var origin=space==0?view.PhysicalOrigin:view.InternalOrigin;
                var ribbons=new[]{new Ribbons(transform,origin.forward,Demo.Settings.ComparisonWidth),new Ribbons(transform,origin.forward,Demo.Settings.ComparisonWidth),new Ribbons(transform,origin.forward,Demo.Settings.ComparisonWidth)};
                int shown=0;var active=lab.State.Model.Active;int focusedRow=lab.State.SelectedRow;
                for(int i=0;i<active.Length;i++)
                {
                    if(shown>=Demo.Settings.ComparisonPointBudget) break;
                    int change=Baseline.Change(i,active[i]);
                    bool focus=focusedRow==i;
                    if(change==0&&!focus) continue;shown++;
                    var now=view.PointPosition(i,space==1);var old=space==0?now:PreviousPoint(i);
                    // Physical coordinates do not move: only selection changes there.
                    if(change!=0) ribbons[change<0?1:2].Ring(now,origin.rotation,Demo.Settings.ComparisonRadius,change<0);
                    if(space==1&&Vector3.Distance(old,now)>.008f)
                    {
                        ribbons[0].Ring(old,origin.rotation,Demo.Settings.ComparisonRadius,true);
                        ribbons[0].Dashed(old,now,Demo.Settings.ComparisonDash,Demo.Settings.ComparisonGap);
                    }
                }
                if(space==0&&view.ShowEdges)
                {
                    int count=Math.Min(removedEdges.Length,Demo.Settings.ComparisonEdgeBudget);
                    for(int i=0;i<count;i++) {var edge=removedEdges[i];ribbons[1].Dashed(view.PointPosition(edge.Source,false),view.PointPosition(edge.Target,false),Demo.Settings.ComparisonDash,Demo.Settings.ComparisonGap);}
                }
                for(int kind=0;kind<3;kind++)
                {
                    var layer=layers[space*3+kind];layer.mesh.Clear();layer.mesh.SetVertices(ribbons[kind].Vertices);layer.mesh.SetTriangles(ribbons[kind].Indices,0);layer.mesh.RecalculateBounds();
                    if(kind==2) layer.material.SetColor("_BaseColor",lab.Settings.SelectedColor);
                    RibbonSegments+=ribbons[kind].Indices.Count/6;
                }
                float envelope=space==0?Mathf.Max(.73f,Demo.QuestUI.PhysicalEnvelope):Mathf.Max(.7f,Demo.QuestUI.InternalEnvelope);
                var badge=badges[space];badge.transform.SetPositionAndRotation(origin.position+Vector3.up*(envelope+.10f),origin.rotation);
                int changed=Added+Removed;string limit=changed>Demo.Settings.ComparisonPointBudget?" *":"";
                badge.text=$"Added {Added} | Removed {Removed}{limit}\nIN {lab.State.SelectedCount} | OUT {active.Length-lab.State.SelectedCount}";
            }
        }
        void LateUpdate()
        {
            if(layers.Count==0) return;
            var lab=Demo.Lab;var view=lab.View;
            bool active=PresentationActive;
            if(active&&!wasActive&&!Pinned) Capture();wasActive=active;
            if(!active) {foreach(var layer in layers) layer.renderer.enabled=false;foreach(var badge in badges) badge.gameObject.SetActive(false);return;}
            if(lab.State!=null&&(Baseline==null||Baseline.Source!=lab.State)) {Pinned=false;Capture();}
            bool show=Visible&&Enabled;
            if(show&&(dirty||Enabled!=previousEnabled||oldPhysical!=view.PhysicalOrigin.position||oldInternal!=view.InternalOrigin.position||oldPhysicalRotation!=view.PhysicalOrigin.rotation||oldInternalRotation!=view.InternalOrigin.rotation||oldScale!=view.InternalScale||oldPhysicalScale!=view.PhysicalScale||oldLayer!=view.LayerSeparation||oldSize!=view.PointSize))
            {
                Build();dirty=false;oldPhysical=view.PhysicalOrigin.position;oldInternal=view.InternalOrigin.position;
                oldPhysicalRotation=view.PhysicalOrigin.rotation;oldInternalRotation=view.InternalOrigin.rotation;
                oldScale=view.InternalScale;oldPhysicalScale=view.PhysicalScale;oldLayer=view.LayerSeparation;oldSize=view.PointSize;
            }
            previousEnabled=Enabled;
            foreach(var layer in layers) layer.renderer.enabled=show&&(layer.view==0?view.PresentationPhysical:view.PresentationInternal);
            for(int i=0;i<badges.Count;i++) badges[i].gameObject.SetActive(Demo.QuestUI.Active&&show&&(i==0?view.PresentationPhysical:view.PresentationInternal));
        }
        void OnDestroy()
        {
            if(Demo!=null&&Demo.Lab!=null) {Demo.Lab.ModelChanging-=BeforeChange;Demo.Lab.ModelChanged-=Changed;Demo.Lab.View.Rebuilt-=Rebuilt;}
            foreach(var layer in layers) {if(layer.mesh!=null) Destroy(layer.mesh);if(layer.material!=null) Destroy(layer.material);}
        }
    }
}
