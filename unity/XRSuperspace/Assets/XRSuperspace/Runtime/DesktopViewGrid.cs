using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Independent scientific-coordinate grids, clipped to separate plot regions.
    // Presentation only: never mutates model coordinates, masks or history.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DesktopViewGrid : MaskableGraphic
    {
        public DesktopWorkspace Workspace;
        public Text[] TickLabels;
        public Text[] Captions;
        public sealed class Region
        {
            public int View;
            public bool Visible,Projected;
            public Rect PixelRect,PlotRect;
            public Transform Origin;
            public float UnitScale,PixelsPerUnit;
            public double Minor,Major,OldMinor,MinX,MaxX,MinY,MaxY;
            public float ChangedAt,Blend=1;
            public string Units;
            public string CaptionValue;
            public float CaptionWidth;
            public readonly List<float[]> Segments=new();
        }
        readonly Region[] regions={new Region {View=1},new Region {View=2},new Region {View=3}};
        bool initialized,lastVisible,lastContrast,lastPhysical,lastInternal,lastReciprocal;
        int lastFocus;
        float lastScale;
        Rect lastRect;
        Matrix4x4 lastCamera,lastProjection;
        readonly Matrix4x4[] lastOrigins=new Matrix4x4[3];
        readonly float[] lastUnits=new float[3];
        public Region[] Regions=>regions;
        public double Interval=>Workspace.FocusView==0?0:regions[Workspace.FocusView-1].Major;
        public double MinorInterval=>Workspace.FocusView==0?0:regions[Workspace.FocusView-1].Minor;
        public bool Transitioning=>Array.Exists(regions,r=>r.Visible&&r.Blend<1);
        public static double NiceInterval(double target)
        {
            if(target<=0||double.IsNaN(target)||double.IsInfinity(target)) return 1;
            double power=Math.Pow(10,Math.Floor(Math.Log10(target))),fraction=target/power;
            return (fraction<=1?1:fraction<=2?2:fraction<=5?5:10)*power;
        }
        public static double AdaptiveMinor(double pixelsPerUnit,double canvasScale=1)
        {
            if(!(pixelsPerUnit>0)||double.IsInfinity(pixelsPerUnit)||!(canvasScale>0)||double.IsInfinity(canvasScale)) return 1;
            double target=24*canvasScale/pixelsPerUnit;
            double power=Math.Pow(10,Math.Floor(Math.Log10(target))),f=target/power;
            // Nearest 1/2/5 interval gives ~15..38 logical pixels per small cell,
            // independently of display DPI, scientific space and zoom.
            return power*(f<Math.Sqrt(2)?1:f<Math.Sqrt(10)?2:f<Math.Sqrt(50)?5:10);
        }
        public static Rect Pane(Rect viewport,float first,float second,int view,float gap)
        {
            float left=view==1?viewport.xMin:(view==2?first:second)+gap*.5f;
            float right=view==3?viewport.xMax:(view==1?first:second)-gap*.5f;
            left=Mathf.Clamp(left,viewport.xMin,viewport.xMax);right=Mathf.Clamp(right,viewport.xMin,viewport.xMax);
            return new Rect(left,viewport.yMin,Mathf.Max(0,right-left),viewport.height);
        }
        public static bool Clip(Rect r,ref Vector2 a,ref Vector2 b)
        {
            Vector2 delta=b-a;float lo=0,hi=1;
            if(!ClipEdge(-delta.x,a.x-r.xMin,ref lo,ref hi)||!ClipEdge(delta.x,r.xMax-a.x,ref lo,ref hi)||
               !ClipEdge(-delta.y,a.y-r.yMin,ref lo,ref hi)||!ClipEdge(delta.y,r.yMax-a.y,ref lo,ref hi)) return false;
            b=a+delta*hi;a+=delta*lo;return true;
        }
        static bool ClipEdge(float p,float q,ref float lo,ref float hi)
        {
            if(Mathf.Abs(p)<1e-9f) return q>=0;
            float t=q/p;if(p<0) {if(t>hi) return false;lo=Mathf.Max(lo,t);}else {if(t<lo) return false;hi=Mathf.Min(hi,t);}return true;
        }
        public void Refresh()
        {
            if(Workspace==null||Workspace.Demo.Lab.State==null||canvas==null) return;
            var demo=Workspace.Demo;var camera=demo.Instrument.Camera;var viewport=camera.pixelRect;
            float scale=canvas.scaleFactor;
            var origins=new[]{demo.Lab.View.PhysicalOrigin,demo.Lab.View.InternalOrigin,demo.Instrument.Reciprocal.Origin};
            bool physical=demo.Lab.View.PresentationPhysical,internalView=demo.Lab.View.PresentationInternal||demo.Lab.View.WindowLine.enabled,reciprocal=demo.Instrument.Reciprocal.PresentationVisible;
            bool changed=!initialized||lastVisible!=Workspace.GridVisible||lastFocus!=Workspace.FocusView||lastScale!=scale||lastContrast!=Workspace.Preferences.HighContrast||
                lastPhysical!=physical||lastInternal!=internalView||lastReciprocal!=reciprocal||lastRect!=viewport||!lastCamera.Equals(camera.worldToCameraMatrix)||!lastProjection.Equals(camera.projectionMatrix);
            for(int i=0;i<3;i++) changed|=!lastOrigins[i].Equals(origins[i].localToWorldMatrix)||lastUnits[i]!=Workspace.UnitScaleFor(i+1);
            if(!changed&&!Transitioning) return;
            initialized=true;lastVisible=Workspace.GridVisible;lastFocus=Workspace.FocusView;lastScale=scale;lastContrast=Workspace.Preferences.HighContrast;
            lastPhysical=physical;lastInternal=internalView;lastReciprocal=reciprocal;lastRect=viewport;lastCamera=camera.worldToCameraMatrix;lastProjection=camera.projectionMatrix;
            for(int i=0;i<3;i++) {lastOrigins[i]=origins[i].localToWorldMatrix;lastUnits[i]=Workspace.UnitScaleFor(i+1);}
            float first=(camera.WorldToScreenPoint(origins[0].position).x+camera.WorldToScreenPoint(origins[1].position).x)*.5f;
            float second=(camera.WorldToScreenPoint(origins[1].position).x+camera.WorldToScreenPoint(origins[2].position).x)*.5f;
            for(int i=0;i<3;i++)
            {
                var r=regions[i];bool before=r.Visible;
                r.Visible=Workspace.GridVisible&&(Workspace.FocusView==0||Workspace.FocusView==r.View)&&
                    (i==0?demo.Lab.View.PresentationPhysical:i==1?demo.Lab.View.PresentationInternal||demo.Lab.View.WindowLine.enabled:demo.Instrument.Reciprocal.PresentationVisible);
                r.Origin=origins[i];r.UnitScale=Workspace.UnitScaleFor(r.View);
                r.Projected=Quaternion.Angle(r.Origin.rotation,Quaternion.identity)>.01f;
                r.Units=i==0?"dimensionless x/y":i==1?"dimensionless u/v":"model reciprocal qx/qy";
                r.PixelRect=Workspace.FocusView==0?Pane(viewport,first,second,r.View,12*scale):viewport;
                r.PlotRect=new Rect(r.PixelRect.xMin+4*scale,r.PixelRect.yMin+88*scale,Mathf.Max(0,r.PixelRect.width-8*scale),Mathf.Max(0,r.PixelRect.height-148*scale));
                r.Visible&=r.PlotRect.width>=90*scale&&r.PlotRect.height>=50*scale;
                if(r.Visible&&!Prepare(r,camera,scale)) r.Visible=false;
                if(r.Visible)
                {
                    double step=AdaptiveMinor(r.PixelsPerUnit,scale);
                    if(!before||r.Minor<=0) {r.Minor=step;r.OldMinor=step;r.Blend=1;}
                    else if(Math.Abs(step/r.Minor-1)>1e-6) {r.OldMinor=r.Minor;r.Minor=step;r.ChangedAt=Time.unscaledTime;r.Blend=0;}
                    r.Major=r.Minor*5;
                    if(Workspace.Preferences.ReducedMotion) r.Blend=1;
                    else r.Blend=Mathf.Clamp01((Time.unscaledTime-r.ChangedAt)/.12f);
                }
                else r.Blend=1;
                RefreshLabels(r,i,scale);
            }
            SetVerticesDirty();
        }
        bool Prepare(Region r,Camera camera,float scale)
        {
            var plane=new Plane(r.Origin.forward,r.Origin.position);
            double minX=double.PositiveInfinity,maxX=double.NegativeInfinity,minY=minX,maxY=maxX;
            foreach(var c in new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,0),new Vector2(1,1)})
            {
                var pixel=r.PlotRect.min+Vector2.Scale(c,r.PlotRect.size);var ray=camera.ScreenPointToRay(pixel);
                if(!plane.Raycast(ray,out float distance)||distance>1000) return false;
                var local=r.Origin.InverseTransformPoint(ray.GetPoint(distance))/r.UnitScale;
                minX=Math.Min(minX,local.x);maxX=Math.Max(maxX,local.x);minY=Math.Min(minY,local.y);maxY=Math.Max(maxY,local.y);
            }
            var zero=(Vector2)camera.WorldToScreenPoint(r.Origin.position);
            float x=Vector2.Distance(zero,camera.WorldToScreenPoint(r.Origin.TransformPoint(Vector3.right*r.UnitScale)));
            float y=Vector2.Distance(zero,camera.WorldToScreenPoint(r.Origin.TransformPoint(Vector3.up*r.UnitScale)));
            r.PixelsPerUnit=Mathf.Min(x,y);
            if(r.PixelsPerUnit<1e-5f||!float.IsFinite(r.PixelsPerUnit)) return false;
            r.MinX=minX;r.MaxX=maxX;r.MinY=minY;r.MaxY=maxY;return true;
        }
        Vector2 Local(Vector2 pixel)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,pixel,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var point);return point;
        }
        void RefreshLabels(Region r,int regionIndex,float scale)
        {
            if(Captions!=null&&Captions.Length>regionIndex)
            {
                var caption=Captions[regionIndex];bool show=r.Visible&&Workspace.FocusView==0&&r.PixelRect.width>=160*scale;
                caption.gameObject.SetActive(show);
                if(show)
                {
                    caption.rectTransform.anchoredPosition=Local(new Vector2(r.PixelRect.center.x,r.PixelRect.yMin+31*scale));
                    caption.rectTransform.sizeDelta=new Vector2(r.PixelRect.width/scale-16,58);
                    string unit=regionIndex==0?(r.Projected?"Projected x/y":"x/y / unitless"):regionIndex==1?"u/v / unitless":"qx/qy / reciprocal";
                    string raw=$"{unit}\nminor {r.Minor:G4} / major {r.Major:G4}";
                    if(r.CaptionValue!=raw||r.CaptionWidth!=caption.rectTransform.sizeDelta.x)
                    {
                        r.CaptionValue=raw;r.CaptionWidth=caption.rectTransform.sizeDelta.x;
                        DesktopSemanticText.Apply(caption,raw,Workspace.Preferences.HighContrast,DesktopTheme.Muted);
                    }
                    if(DesktopTheme.Measure(caption.text,caption.fontSize,caption.rectTransform.sizeDelta.x,true)>58) caption.gameObject.SetActive(false);
                }
            }
            if(TickLabels==null||TickLabels.Length<(regionIndex+1)*6) return;
            for(int j=0;j<6;j++)
            {
                var label=TickLabels[regionIndex*6+j];label.gameObject.SetActive(false);
                if(!r.Visible||r.Projected) continue;
                bool horizontal=j<3;double low=horizontal?r.MinX:r.MinY,high=horizontal?r.MaxX:r.MaxY;
                double value=Math.Round((low+high)*.5/r.Major)*r.Major+(j%3-1)*r.Major;
                if(value<low||value>high) continue;
                var world=r.Origin.TransformPoint(new Vector3(horizontal?(float)value:0,horizontal?0:(float)value,0)*r.UnitScale);
                var p=(Vector2)Workspace.Demo.Instrument.Camera.WorldToScreenPoint(world);
                if(horizontal&&(p.x<r.PlotRect.xMin+48*scale||p.x>r.PlotRect.xMax-48*scale)) continue;
                if(!horizontal&&(p.y<r.PlotRect.yMin+14*scale||p.y>r.PlotRect.yMax-14*scale)) continue;
                label.rectTransform.anchoredPosition=Local(horizontal?new Vector2(p.x,r.PixelRect.yMin+76*scale):new Vector2(r.PixelRect.xMin+52*scale,p.y));
                label.text=(Math.Abs(value)<r.Major*1e-8?0:value).ToString("G4",CultureInfo.InvariantCulture);label.gameObject.SetActive(true);
            }
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            foreach(var r in regions)
            {
                r.Segments.Clear();
                if(!r.Visible) continue;
                if(r.Blend<1) Draw(mesh,r,r.OldMinor,1-DesktopMotion.Ease(r.Blend));
                Draw(mesh,r,r.Minor,r.Blend<1?DesktopMotion.Ease(r.Blend):1);
                Line(mesh,r,new Vector2(0,(float)r.MinY),new Vector2(0,(float)r.MaxY),2,1);
                Line(mesh,r,new Vector2((float)r.MinX,0),new Vector2((float)r.MaxX,0),2,1);
            }
        }
        void Draw(VertexHelper mesh,Region r,double minor,float alpha)
        {
            if(alpha<=0||minor<=0) return;
            double startX=Math.Ceiling(r.MinX/minor),startY=Math.Ceiling(r.MinY/minor);
            int countX=Math.Min(512,(int)Math.Ceiling((r.MaxX-r.MinX)/minor)+1),countY=Math.Min(512,(int)Math.Ceiling((r.MaxY-r.MinY)/minor)+1);
            for(int i=0;i<countX;i++) {double n=startX+i,x=n*minor;if(x>r.MaxX+minor*1e-6) break;if(Math.Abs(n)<.01) continue;Line(mesh,r,new Vector2((float)x,(float)r.MinY),new Vector2((float)x,(float)r.MaxY),Math.Abs(n%5)<.01?1:0,alpha);}
            for(int i=0;i<countY;i++) {double n=startY+i,y=n*minor;if(y>r.MaxY+minor*1e-6) break;if(Math.Abs(n)<.01) continue;Line(mesh,r,new Vector2((float)r.MinX,(float)y),new Vector2((float)r.MaxX,(float)y),Math.Abs(n%5)<.01?1:0,alpha);}
        }
        void Line(VertexHelper mesh,Region r,Vector2 a,Vector2 b,int weight,float alpha)
        {
            var camera=Workspace.Demo.Instrument.Camera;
            Vector2 p=camera.WorldToScreenPoint(r.Origin.TransformPoint(new Vector3(a.x,a.y,0)*r.UnitScale));
            Vector2 q=camera.WorldToScreenPoint(r.Origin.TransformPoint(new Vector3(b.x,b.y,0)*r.UnitScale));
            if(!Clip(r.PlotRect,ref p,ref q)) return;
            r.Segments.Add(new[]{p.x,p.y,q.x,q.y,(float)weight,alpha});
            p=Local(p);q=Local(q);var normal=new Vector2(-(q-p).y,(q-p).x).normalized*(weight==2?.65f:weight==1?.45f:.3f)/canvas.scaleFactor;
            bool contrast=Workspace.Preferences.HighContrast;
            float opacity=(weight==2?(contrast?.55f:.32f):weight==1?(contrast?.30f:.17f):(contrast?.15f:.075f))*alpha;
            var tint=new Color(.55f,.65f,.76f,opacity);int start=mesh.currentVertCount;
            foreach(var position in new[]{p-normal,p+normal,q+normal,q-normal}) {var vertex=UIVertex.simpleVert;vertex.position=position;vertex.color=tint;mesh.AddVert(vertex);}
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
    }
}
