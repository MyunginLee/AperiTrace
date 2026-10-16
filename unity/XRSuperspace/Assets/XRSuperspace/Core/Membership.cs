using System;
using System.Collections.Generic;

namespace XRSuperspace
{
    public enum PointState { Rejected, Selected, Boundary }
    public static class Membership
    {
        public static Double2[] GenerateWindow(double[,] q)
        {
            var points=new List<Double2>();
            for(int bits=0;bits<32;bits++) { double x=0,y=0;for(int j=0;j<5;j++) { double n=((bits>>(4-j))&1)==0 ? -0.5:0.5;x+=q[3,j]*n;y+=q[4,j]*n; } points.Add(new Double2(x,y)); }
            points.Sort((a,b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y.CompareTo(b.Y));
            var lower=new List<Double2>();var upper=new List<Double2>();
            void Add(List<Double2> hull,Double2 p) { while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=1e-14) hull.RemoveAt(hull.Count-1);hull.Add(p); }
            foreach(var p in points) Add(lower,p);for(int i=points.Count-1;i>=0;i--) Add(upper,points[i]);
            lower.RemoveAt(lower.Count-1);upper.RemoveAt(upper.Count-1);lower.AddRange(upper);
            if(lower.Count!=10) throw new FormatException("canonical cube hull must contain 10 vertices");
            return lower.ToArray();
        }
        static double Cross(Double2 a,Double2 b,Double2 c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        public static double Distance(IReadOnlyList<Double2> window,Double2 point)
        {
            double closest=double.PositiveInfinity;bool inside=true;
            for(int j=0;j<window.Count;j++) { var a=window[j];var b=window[(j+1)%window.Count];double dx=b.X-a.X,dy=b.Y-a.Y;
                if(Cross(a,b,point)<0) inside=false;
                double t=Math.Max(0,Math.Min(1,((point.X-a.X)*dx+(point.Y-a.Y)*dy)/(dx*dx+dy*dy)));
                double ex=point.X-a.X-t*dx,ey=point.Y-a.Y-t*dy;closest=Math.Min(closest,Math.Sqrt(ex*ex+ey*ey)); }
            return inside?closest:-closest;
        }
        public static PointState Classify(double distance,double epsilon)=>Math.Abs(distance)<=epsilon?PointState.Boundary:distance>epsilon?PointState.Selected:PointState.Rejected;
        public static bool IsSelected(PointState state,bool include)=>state==PointState.Selected||(include&&state==PointState.Boundary);
        public static PointState[] Evaluate(RuntimeDataset data,Double2 phi)
        {
            var states=new PointState[data.Candidates.Count];
            for(int i=0;i<states.Length;i++) { var id=data.Candidates[i].Id;var uv=new Double2(data.Project(id,3)+phi.X,data.Project(id,4)+phi.Y);
                states[i]=Classify(Distance(data.DoubleWindow,uv),data.Metadata.AuthorityEpsilon); }
            return states;
        }
        public static GraphEdge[] Graph(RuntimeDataset data,PointState[] states)
        {
            var index=new Dictionary<LatticeId,int>();for(int i=0;i<data.Candidates.Count;i++) if(IsSelected(states[i],data.Metadata.IncludeBoundary)) index.Add(data.Candidates[i].Id,i);
            var edges=new List<GraphEdge>();for(int i=0;i<data.Candidates.Count;i++) if(index.ContainsKey(data.Candidates[i].Id)) for(int j=0;j<5;j++)
            { if(data.Candidates[i].Id[j]==int.MaxValue) continue;if(index.TryGetValue(data.Candidates[i].Id.Neighbor(j),out int target)) edges.Add(new GraphEdge(i,target,j)); }
            return edges.ToArray();
        }
    }
}
