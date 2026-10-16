using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Display transforms only. Exact coordinates, membership, q and F are never
    // derived from the grid/zoom. Both panes use the desktop 1/2/5 spacing rule.
    [DefaultExecutionOrder(220)]
    public sealed class QuestViewTools : MonoBehaviour
    {
        public ScientificDemo Demo;
        public readonly float[] Zooms={1,1,1};
        public float[] ActualZooms=>(float[])current.Clone();
        readonly float[] current={1,1,1};
        readonly QuestPlaneGrid[] grids=new QuestPlaneGrid[3];
        readonly Vector3[] anchors=new Vector3[3];
        readonly Vector2[] pan=new Vector2[3];
        readonly Vector3[] colliderSizes=new Vector3[3];
        InputAction zoomInput,panInput;
        bool wasActive,anchored;
        int oldView=-1;
        float nextGrid;
        public bool CanNavigate=>Demo.QuestUI.Active&&!Demo.Guided&&!Demo.Menu.HasExample&&!Demo.PlaybackBlocked&&!Demo.QuestUI.Transitioning&&
            !Demo.PathRunning&&!Demo.Instrument.Playing&&!Demo.Instrument.Recording&&!Demo.Lab.Busy&&
            !(Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.Held??false);
        public static float ClampZoom(float zoom,bool linked)=>Mathf.Clamp(zoom,.25f,linked?1:2);
        void Start()
        {
            zoomInput=new InputAction("Inspect zoom",InputActionType.Value,"<XRController>{RightHand}/primary2DAxis");
            panInput=new InputAction("Inspect pan",InputActionType.Value,"<XRController>{LeftHand}/primary2DAxis");zoomInput.Enable();panInput.Enable();
            for(int i=0;i<3;i++)
            {
                var collider=Origin(i).GetComponent<BoxCollider>();if(collider!=null) colliderSizes[i]=collider.size;
                var node=new GameObject("Independent scientific grid "+(i+1),typeof(RectTransform));node.transform.SetParent(transform,false);
                var canvas=node.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                grids[i]=node.AddComponent<QuestPlaneGrid>();grids[i].raycastTarget=false;
            }
        }
        Transform Origin(int i)=>i==0?Demo.Lab.View.PhysicalOrigin:i==1?Demo.Lab.View.InternalOrigin:Demo.Instrument.Reciprocal.Origin;
        public void AnchorPlaced()
        {
            for(int i=0;i<3;i++) {anchors[i]=Origin(i).position;pan[i]=Vector2.zero;}anchored=true;
        }
        public void Zoom(float factor)
        {
            if(!float.IsFinite(factor)||factor<=0) throw new ArgumentException("Zoom must be positive and finite.");
            Demo.Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();
            int focus=Demo.Guided?0:Demo.Preferences.View;
            for(int i=0;i<3;i++) if(focus==0||focus==i+1) Zooms[i]=ClampZoom(Zooms[i]*factor,focus==0);
        }
        public void ResetView()
        {
            for(int i=0;i<3;i++) {Zooms[i]=1;pan[i]=Vector2.zero;}Demo.QuestUI.Recenter();
        }
        void Update()
        {
            bool active=Demo.QuestUI.Active;
            if(!active)
            {
                foreach(var grid in grids) if(grid!=null) grid.gameObject.SetActive(false);
                if(wasActive) {for(int i=0;i<3;i++) {var collider=Origin(i).GetComponent<BoxCollider>();if(collider!=null) collider.size=colliderSizes[i];}Demo.Lab.View.PhysicalZoom=Demo.Lab.View.InternalZoom=Demo.Instrument.Reciprocal.PresentationZoom=1;Demo.Instrument.Reciprocal.SetDisplay(null);if(Demo.Lab.State!=null) {Demo.Lab.View.Rebuild(Demo.Lab.State);Demo.Instrument.Reciprocal.Rebuild();}}
                wasActive=false;return;
            }
            if(Demo.Lab.State==null) return;
            if(!wasActive) {Demo.Instrument.Reciprocal.SetDisplay(Demo.Preferences.Diffraction);wasActive=true;}
            int focus=Demo.Guided?0:Demo.Preferences.View;
            if(oldView!=focus) {oldView=focus;for(int i=0;i<3;i++) {Zooms[i]=ClampZoom(Zooms[i],focus==0);pan[i]=Vector2.zero;}}
            if(CanNavigate&&focus!=0)
            {
                if(Demo.Preferences.Controllers!=1&&Tracked(false))
                {
                    float axis=zoomInput.ReadValue<Vector2>().y;if(Mathf.Abs(axis)>.15f) Zoom(Mathf.Exp(axis*Time.unscaledDeltaTime*1.5f));
                }
                if(Demo.Preferences.Controllers!=2&&Tracked(true)&&anchored)
                {
                    Vector2 axis=panInput.ReadValue<Vector2>();if(axis.magnitude>.15f) pan[focus-1]=Vector2.ClampMagnitude(pan[focus-1]+axis*Time.unscaledDeltaTime*.3f,.4f);
                }
            }
            bool changed=false;
            for(int i=0;i<3;i++)
            {
                float value=Demo.Preferences.ReducedMotion?Zooms[i]:Mathf.Lerp(current[i],Zooms[i],1-Mathf.Exp(-Time.unscaledDeltaTime*14));
                if(Mathf.Abs(value-Zooms[i])<.0001f) value=Zooms[i];changed|=value!=current[i];current[i]=value;
                var collider=Origin(i).GetComponent<BoxCollider>();if(collider!=null) collider.size=Vector3.Scale(colliderSizes[i],new Vector3(value,value,i==0?value:1));
                if(anchored&&!Demo.QuestUI.Transitioning)
                {
                    var origin=Origin(i);var position=anchors[i]+origin.right*pan[i].x+origin.up*pan[i].y;
                    if((position-origin.position).sqrMagnitude>1e-10f) {origin.position=position;changed=true;}
                }
            }
            Demo.Lab.View.PhysicalZoom=current[0];Demo.Lab.View.InternalZoom=current[1];Demo.Instrument.Reciprocal.PresentationZoom=current[2];
            if(changed) {Demo.Lab.View.Rebuild(Demo.Lab.State);Demo.Instrument.Reciprocal.Rebuild();Demo.Lab.SyncHandlePose();Demo.QuestUI.AcceptPresentationTransform();}
            if(Time.unscaledTime<nextGrid) return;nextGrid=Time.unscaledTime+.1f;
            for(int i=0;i<3;i++)
            {
                var grid=grids[i];if(grid==null) continue;var origin=Origin(i);var viewer=Demo.QuestUI.Viewer;
                bool visible=Demo.Preferences.GridVisible&&(focus==0||focus==i+1)&&
                    (i==0?Demo.Lab.View.PresentationPhysical:i==1?Demo.Lab.View.WindowLine.enabled:Demo.Instrument.Reciprocal.PresentationVisible);
                grid.gameObject.SetActive(visible);if(!visible||viewer==null) continue;
                double unit=i==0?Demo.Lab.View.PhysicalScale*Demo.Lab.State.Dataset.Metadata.DisplayScales.X:i==1?Demo.Lab.View.InternalScale:ReciprocalUnit();
                float extent=i==0?Demo.QuestUI.PhysicalEnvelope:i==1?Demo.QuestUI.InternalEnvelope:.63f;
                grid.transform.SetPositionAndRotation(origin.position-origin.forward*.008f,origin.rotation);
                grid.GetComponent<Canvas>().worldCamera=viewer;
                grid.Configure(extent,unit,Mathf.Max(.01f,Vector3.Distance(viewer.transform.position,origin.position)),Demo.Preferences.HighContrast,Demo.Preferences.ReducedMotion,
                    i==0?"dimensionless x/y":i==1?"dimensionless u/v":"model reciprocal qx/qy",current[i]);
            }
        }
        double ReciprocalUnit()
        {
            double extent=1;foreach(var peak in Demo.Instrument.Reciprocal.Peaks) extent=Math.Max(extent,Math.Max(Math.Abs(peak.Q.X),Math.Abs(peak.Q.Y)));
            return .55*current[2]/extent;
        }
        static bool Tracked(bool left)
        {
            if(!UnityEngine.XR.XRSettings.isDeviceActive) return false;
            var device=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(left?UnityEngine.XR.XRNode.LeftHand:UnityEngine.XR.XRNode.RightHand);
            return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool value)&&value;
        }
        void OnDestroy() {zoomInput?.Dispose();panInput?.Dispose();}
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class QuestPlaneGrid : MaskableGraphic
    {
        public double Minor {get;private set;}
        public double UnitScale {get;private set;}
        public float Extent {get;private set;}
        double previousMinor;
        float changedAt;
        bool contrast,reduced;
        Text caption;
        public void Configure(float extent,double units,float distance,bool highContrast,bool reducedMotion,string context,float zoom)
        {
            Extent=extent;UnitScale=units;contrast=highContrast;reduced=reducedMotion;
            // ~1 degree between minor lines, using an angular reference density
            // rather than claiming identical headset pixels across devices.
            double next=DesktopViewGrid.AdaptiveMinor(units*1375/distance);
            if(next!=Minor) {previousMinor=Minor;Minor=next;changedAt=Time.unscaledTime;}
            // Use normal UI pixel coordinates. Sub-pixel world-meter quads can
            // be discarded by the Canvas geometry pipeline before projection.
            rectTransform.localScale=Vector3.one*.001f;
            rectTransform.sizeDelta=new Vector2(extent*2000,extent*2000);
            if(caption==null)
            {
                var node=new GameObject("Grid units and scale",typeof(RectTransform));node.transform.SetParent(transform,false);
                caption=node.AddComponent<Text>();QuestUiTheme.Style(caption,24);caption.alignment=TextAnchor.MiddleCenter;
                caption.rectTransform.localScale=Vector3.one;caption.rectTransform.sizeDelta=new Vector2(1250,80);
            }
            caption.transform.localPosition=new Vector3(0,(-extent-.06f)*1000,-10);
            DesktopSemanticText.Apply(caption,context+" / minor "+Minor.ToString("G3",System.Globalization.CultureInfo.InvariantCulture)+" / zoom "+Mathf.RoundToInt(zoom*100)+"%",contrast);
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(Minor<=0||UnitScale<=0||Extent<=0) return;
            float blend=reduced||previousMinor<=0?1:Mathf.Clamp01((Time.unscaledTime-changedAt)/.12f);
            if(blend<1) Draw(mesh,previousMinor,1-blend);Draw(mesh,Minor,blend);
        }
        void Draw(VertexHelper mesh,double minor,float alpha)
        {
            float step=(float)(minor*UnitScale)*1000,extent=Extent*1000;if(step<=0) return;
            int count=Math.Min(100,Mathf.FloorToInt(extent/step));
            for(int i=-count;i<=count;i++)
            {
                float p=i*step;bool major=i%5==0;var c=contrast?new Color(.68f,.78f,.89f,major?.65f:.40f):new Color(.44f,.58f,.69f,major?.40f:.23f);c.a*=alpha;
                float thickness=major?1.4f:.85f;
                Quad(mesh,new Rect(Mathf.Max(-extent,p-thickness),-extent,Mathf.Min(extent,p+thickness)-Mathf.Max(-extent,p-thickness),extent*2),c);
                Quad(mesh,new Rect(-extent,Mathf.Max(-extent,p-thickness),extent*2,Mathf.Min(extent,p+thickness)-Mathf.Max(-extent,p-thickness)),c);
            }
        }
        static void Quad(VertexHelper mesh,Rect r,Color color)
        {
            int n=mesh.currentVertCount;mesh.AddVert(new Vector3(r.xMin,r.yMin),color,Vector2.zero);mesh.AddVert(new Vector3(r.xMin,r.yMax),color,Vector2.zero);mesh.AddVert(new Vector3(r.xMax,r.yMax),color,Vector2.zero);mesh.AddVert(new Vector3(r.xMax,r.yMin),color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
        void LateUpdate() {if(!reduced&&Time.unscaledTime-changedAt<.12f) SetVerticesDirty();}
    }
}
