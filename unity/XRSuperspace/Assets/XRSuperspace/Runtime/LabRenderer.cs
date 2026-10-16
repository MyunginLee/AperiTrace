using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace
{
    public sealed class LabRenderer : MonoBehaviour
    {
        public LabSettings Settings;
        public Transform PhysicalOrigin,InternalOrigin;
        public Mesh PointMesh;
        public Material PointMaterial,LineMaterial;
        public LineRenderer WindowLine;
        public MeshFilter GraphMesh;
        public bool ShowEdges=true;
        public bool InstrumentMode,ShowCandidates;
        // The GPU renderer owns drawing; retain the bounded CPU picker/window/graph.
        public bool ExternalPointDrawing;
        public int DisplayPointBudget=50000;
        public bool DisplayInternal=true;
        // Presentation reveal only; scientific masks, coordinates and graph remain unchanged.
        public bool PresentationPhysical=true,PresentationInternal=true;
        public event Action Rebuilt;
        public float LayerSeparation=1,PointSize=1,BoundaryBand=.04f,FadeSeconds=.4f;
        public float PhysicalOpacity=1,InternalOpacity=1;
        public float PhysicalScale {get;private set;}
        float uvDisplayScale=1;
        public float PhysicalZoom=1,InternalZoom=1;
        public float InternalScale=>Settings.InternalFitScale*uvDisplayScale*InternalZoom;
        public int BatchCount=>batches.Count;
        readonly List<(Matrix4x4[] matrices,MaterialPropertyBlock props,int view)> batches=new();
        Vector3[] physical=Array.Empty<Vector3>(),internalPoints=Array.Empty<Vector3>();
        Mesh ownedGraph;
        LabState current;
        bool[] previousActive=Array.Empty<bool>();
        float[] fadeFrom=Array.Empty<float>(),fadeTo=Array.Empty<float>();
        float fadeStarted;
        string lastKey;
        public float FadeAt(int row,float time)=>Mathf.Lerp(fadeFrom[row],fadeTo[row],FadeSeconds<=0?1:Mathf.Clamp01((time-fadeStarted)/FadeSeconds));
        public Vector3 PhysicalFromScientific(Double3 p)=>PhysicalOrigin.position+PhysicalOrigin.TransformDirection(new Vector3((float)(p.X*current.Dataset.Metadata.DisplayScales.X),(float)(p.Y*current.Dataset.Metadata.DisplayScales.X),(float)(p.Z*current.Dataset.Metadata.DisplayScales.Y)*LayerSeparation)*PhysicalScale);
        public Vector3 PointPosition(int row,bool internalView)=>internalView?internalPoints[row]:physical[row];
        public void Rebuild(LabState state)
        {
            current=state;string key=state.Model.Key("");
            if(previousActive.Length!=state.Model.Active.Length||lastKey!=key)
            {
                int n=state.Model.Active.Length;bool reset=previousActive.Length!=n;
                var from=new float[n];var to=new float[n];
                for(int i=0;i<n;i++) {from[i]=reset?(state.Model.Active[i]?1:0):FadeAt(i,Time.unscaledTime);to[i]=state.Model.Active[i]?1:0;}
                fadeFrom=from;fadeTo=to;previousActive=(bool[])state.Model.Active.Clone();fadeStarted=Time.unscaledTime;lastKey=key;
            }
            batches.Clear();int count=state.Dataset.Candidates.Count;physical=new Vector3[count];internalPoints=new Vector3[count];
            double extent=0;var scales=state.Dataset.Metadata.DisplayScales;uvDisplayScale=(float)scales.Z;int selectedRow=state.SelectedRow;
            for(int i=0;i<count;i++) { var p=state.Dataset.Candidates[i].Physical;extent=Math.Max(extent,Math.Max(Math.Abs(p.X*scales.X),Math.Max(Math.Abs(p.Y*scales.X),Math.Abs(p.Z*scales.Y)))); }
            float fit=Settings.PhysicalFitRadius/(float)Math.Max(extent,1e-3)*PhysicalZoom;
            PhysicalScale=fit;
            for(int i=0;i<count;i++) { var p=state.Dataset.Candidates[i].Physical;var uv=state.Dataset.Candidates[i].Internal;
                physical[i]=PhysicalOrigin.position+PhysicalOrigin.TransformDirection(new Vector3((float)(p.X*scales.X),(float)(p.Y*scales.X),(float)(p.Z*scales.Y)*LayerSeparation)*fit);
                internalPoints[i]=InternalOrigin.position+InternalOrigin.TransformDirection(new Vector3((float)(uv.X+state.Phi.X),(float)(uv.Y+state.Phi.Y),0)*InternalScale); }
            int stride=PreviewContract.DisplayStride(count,DisplayPointBudget);
            int visible=(count+stride-1)/stride;
            for(int view=0;!ExternalPointDrawing&&view<(DisplayInternal?2:1);view++) for(int start=0;start<visible;start+=512)
            {
                if(view==0?!PresentationPhysical:!PresentationInternal) continue;
                int size=Math.Min(512,visible-start);var matrices=new Matrix4x4[size];var colors=new Vector4[size];
                for(int j=0;j<size;j++) { int i=(start+j)*stride;if(i==0&&selectedRow>=0&&selectedRow%stride!=0) i=selectedRow;Color color=state.States[i] switch {PointState.Selected=>Settings.SelectedColor,PointState.Boundary=>Settings.BoundaryColor,_=>Settings.RejectedColor};
                    bool highlighted=selectedRow==i;colors[j]=highlighted?Color.white:color;float radius=Settings.PointRadius*(highlighted?1.7f:1);
                    if(InstrumentMode)
                    {
                        var id=state.Dataset.Candidates[i].Id;double distance=Membership.Distance(state.Dataset.DoubleWindow,new Double2(state.Dataset.Project(id,3)+state.Phi.X,state.Dataset.Project(id,4)+state.Phi.Y));
                        if(Math.Abs(distance)<=BoundaryBand&&!highlighted) colors[j]=Settings.BoundaryColor;
                        if(view==0&&!ShowCandidates&&!highlighted) {var c=colors[j];c.w=FadeAt(i,Time.unscaledTime);colors[j]=c;}
                        radius*=PointSize;
                    }
                    matrices[j]=Matrix4x4.TRS(view==0?physical[i]:internalPoints[i],Quaternion.identity,Vector3.one*radius*2); }
                var props=new MaterialPropertyBlock();props.SetVectorArray("_InstanceColor",colors);batches.Add((matrices,props,view));
            }
            WindowLine.positionCount=state.Dataset.Window.Count+1;for(int i=0;i<WindowLine.positionCount;i++) { var p=state.Dataset.Window[i%state.Dataset.Window.Count];WindowLine.SetPosition(i,InternalOrigin.position+InternalOrigin.TransformDirection(new Vector3((float)p.X,(float)p.Y,0)*InternalScale)); }
            int edgeCount=Math.Min(state.Edges.Length,Settings.MaximumRenderedEdges);var verts=new Vector3[edgeCount*2];var indices=new int[verts.Length];for(int i=0;i<edgeCount;i++) { verts[i*2]=physical[state.Edges[i].Source];verts[i*2+1]=physical[state.Edges[i].Target];indices[i*2]=i*2;indices[i*2+1]=i*2+1; }
            if(ownedGraph==null) ownedGraph=new Mesh {name="bounded selected graph"};ownedGraph.Clear();ownedGraph.vertices=verts;ownedGraph.SetIndices(indices,MeshTopology.Lines,0);ownedGraph.RecalculateBounds();GraphMesh.sharedMesh=ownedGraph;SetEdgeVisibility();
            Rebuilt?.Invoke();
        }
        public void SetEdgeVisibility()=>GraphMesh.gameObject.SetActive(ShowEdges);
        public int Pick(Ray ray,bool internalView,float radius=0.045f)
        {
            var points=internalView?internalPoints:physical;int best=-1;float bestDistance=float.PositiveInfinity;int selected=current.SelectedRow,stride=PreviewContract.DisplayStride(points.Length,DisplayPointBudget);
            if(internalView&&!DisplayInternal) return -1;
            for(int i=0;i<points.Length;i++) {if(i%stride!=0&&i!=selected||i==0&&selected>=0&&selected%stride!=0) continue;if(!internalView&&InstrumentMode&&!ShowCandidates&&!current.Model.Active[i]&&selected!=i) continue;Vector3 delta=points[i]-ray.origin;float along=Vector3.Dot(delta,ray.direction);if(along<0) continue;float distance=(delta-ray.direction*along).magnitude;
                if(distance<=radius&&(distance<bestDistance||Mathf.Approximately(distance,bestDistance)&&i<best)) { best=i;bestDistance=distance; } }
            return best;
        }
        public int PickNearest(Vector3 position,bool internalView,float radius=0.12f)
        {
            var points=internalView?internalPoints:physical;int best=-1;float distance=radius*radius;int selected=current.SelectedRow,stride=PreviewContract.DisplayStride(points.Length,DisplayPointBudget);
            if(internalView&&!DisplayInternal) return -1;
            for(int i=0;i<points.Length;i++) {if(i%stride!=0&&i!=selected||i==0&&selected>=0&&selected%stride!=0) continue;if(!internalView&&InstrumentMode&&!ShowCandidates&&!current.Model.Active[i]&&selected!=i) continue;float candidate=(points[i]-position).sqrMagnitude;if(candidate<distance) {best=i;distance=candidate;} }
            return best;
        }
        void LateUpdate()
        {
            if(ExternalPointDrawing) return;
            if(InstrumentMode&&current!=null&&Time.unscaledTime-fadeStarted<FadeSeconds) Rebuild(current);
            if(PointMaterial==null||PointMesh==null) return;
            DrawForCamera(null);
        }
        public void DrawForCamera(Camera camera)
        {
            if(ExternalPointDrawing||PointMaterial==null||PointMesh==null) return;
            foreach(var batch in batches) {batch.props.SetFloat("_PresentationAlpha",batch.view==0?PhysicalOpacity:InternalOpacity);var parameters=new RenderParams(PointMaterial) {camera=camera,matProps=batch.props,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};Graphics.RenderMeshInstanced(parameters,PointMesh,0,batch.matrices);}
        }
        void OnDestroy() { if(ownedGraph!=null) { if(Application.isPlaying) Destroy(ownedGraph);else DestroyImmediate(ownedGraph); } }
    }
}
