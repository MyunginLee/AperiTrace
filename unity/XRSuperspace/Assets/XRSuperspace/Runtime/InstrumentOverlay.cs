using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRSuperspace
{
    public sealed class InstrumentOverlay : MonoBehaviour
    {
        public LinkedInstrument Instrument;
        public bool ShowTrajectory=true;
        LineRenderer vector,trajectory,bandInner,bandOuter,crop,slice,sliceLow,sliceHigh;
        Material material;
        readonly List<Material> lineMaterials=new();
        LabState previousState;
        int previousRevision=-1,previousTrace=-1;
        LatticeId? previousSelection;
        Vector3 previousPhysical,previousInternal;
        float previousLayer,previousBand;
        Quaternion previousPhysicalRotation,previousInternalRotation;
        LineRenderer Line(string name,Color color,float width=.004f)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();var colored=new Material(material) {name=name+" material"};colored.SetColor("_BaseColor",color);lineMaterials.Add(colored);line.sharedMaterial=colored;line.startColor=line.endColor=color;line.startWidth=line.endWidth=width;line.useWorldSpace=true;return line;
        }
        void Awake()
        {
            material=new Material(Instrument.Lab.View.LineMaterial) {name="Instrument overlay"};vector=Line("Positive phi vector",Color.cyan,.008f);trajectory=Line("Selected n trajectory",Color.white);bandInner=Line("Diagnostic band inner",new Color(1,.6f,.1f));bandOuter=Line("Diagnostic band outer",new Color(1,.6f,.1f));crop=Line("Scientific crop",new Color(.2f,.5f,.6f));slice=Line("Scientific slice center",new Color(.8f,.3f,.8f));sliceLow=Line("Slice lower bound",new Color(.4f,.15f,.4f));sliceHigh=Line("Slice upper bound",new Color(.4f,.15f,.4f));
        }
        void Band(LineRenderer line,double offset)
        {
            var view=Instrument.Lab.View;var window=Instrument.Lab.State.Dataset.DoubleWindow;line.positionCount=window.Count+1;
            for(int i=0;i<=window.Count;i++)
            {
                int j=i%window.Count;var a=window[(j+window.Count-1)%window.Count];var b=window[j];var c=window[(j+1)%window.Count];double ax=b.X-a.X,ay=b.Y-a.Y,bx=c.X-b.X,by=c.Y-b.Y;double al=Math.Sqrt(ax*ax+ay*ay),bl=Math.Sqrt(bx*bx+by*by);
                // Intersection of the two unit-normal shifted edge lines.
                double n1x=-ay/al,n1y=ax/al,n2x=-by/bl,n2y=bx/bl,den=1+n1x*n2x+n1y*n2y;
                double x=b.X+offset*(n1x+n2x)/den,y=b.Y+offset*(n1y+n2y)/den;
                line.SetPosition(i,view.InternalOrigin.position+view.InternalOrigin.TransformDirection(new Vector3((float)x,(float)y,0)*view.InternalScale));
            }
        }
        void Plane(LineRenderer line,double z,double radius)
        {
            var view=Instrument.Lab.View;line.positionCount=5;for(int i=0;i<5;i++) line.SetPosition(i,view.PhysicalFromScientific(new Double3(i==1||i==2?radius:-radius,i==2||i==3?radius:-radius,z)));
        }
        void LateUpdate()
        {
            var lab=Instrument.Lab;if(lab.State==null) return;var view=lab.View;var state=lab.State;
            // Reveal flags affect presentation only, including diagnostic overlays.
            bandInner.enabled=bandOuter.enabled=view.WindowLine.enabled&&view.BoundaryBand>0;
            vector.enabled=view.PresentationInternal;trajectory.enabled=view.PresentationInternal&&ShowTrajectory;
            crop.enabled=slice.enabled=sliceLow.enabled=sliceHigh.enabled=view.PresentationPhysical;
            if(previousState==state&&previousRevision==state.Model.Revision&&previousTrace==Instrument.Trace.Count&&Nullable.Equals(previousSelection,state.Selection)&&previousPhysical==view.PhysicalOrigin.position&&previousInternal==view.InternalOrigin.position&&previousPhysicalRotation==view.PhysicalOrigin.rotation&&previousInternalRotation==view.InternalOrigin.rotation&&previousLayer==view.LayerSeparation&&previousBand==view.BoundaryBand) return;
            previousState=state;previousRevision=state.Model.Revision;previousTrace=Instrument.Trace.Count;previousSelection=state.Selection;previousPhysical=view.PhysicalOrigin.position;previousInternal=view.InternalOrigin.position;previousLayer=view.LayerSeparation;previousBand=view.BoundaryBand;
            previousPhysicalRotation=view.PhysicalOrigin.rotation;previousInternalRotation=view.InternalOrigin.rotation;
            vector.positionCount=2;vector.SetPosition(0,view.InternalOrigin.position);vector.SetPosition(1,view.InternalOrigin.position+view.InternalOrigin.TransformDirection(new Vector3((float)state.Phi.X,(float)state.Phi.Y,0)*view.InternalScale));
            Band(bandInner,view.BoundaryBand);Band(bandOuter,-view.BoundaryBand);
            var positions=new List<Vector3>();if(state.Selection.HasValue) {var id=state.Selection.Value;double u=state.Dataset.Project(id,3),v=state.Dataset.Project(id,4);foreach(var phi in Instrument.Trace) positions.Add(view.InternalOrigin.position+view.InternalOrigin.TransformDirection(new Vector3((float)(u+phi[0]),(float)(v+phi[1]),-.012f)*view.InternalScale));}
            trajectory.positionCount=positions.Count;trajectory.SetPositions(positions.ToArray());
            var filter=state.Model.Filter;crop.gameObject.SetActive(filter.CropEnabled);slice.gameObject.SetActive(filter.SliceEnabled);sliceLow.gameObject.SetActive(filter.SliceEnabled);sliceHigh.gameObject.SetActive(filter.SliceEnabled);
            if(filter.CropEnabled) {crop.positionCount=132;for(int i=0;i<132;i++) {int ring=i/66,j=i%66;double angle=Math.PI*2*(j%65)/64;crop.SetPosition(i,view.PhysicalFromScientific(new Double3(filter.Radius*Math.Cos(angle),filter.Radius*Math.Sin(angle),ring==0?-filter.ZMax:filter.ZMax)));}}
            if(filter.SliceEnabled) {Plane(slice,filter.SliceZ,filter.Radius);Plane(sliceLow,filter.SliceZ-filter.SliceHalfWidth,filter.Radius);Plane(sliceHigh,filter.SliceZ+filter.SliceHalfWidth,filter.Radius);}
        }
        void OnDestroy() {foreach(var m in lineMaterials) {if(Application.isPlaying) Destroy(m);else DestroyImmediate(m);}if(material!=null) {if(Application.isPlaying) Destroy(material);else DestroyImmediate(material);}}
    }
}
