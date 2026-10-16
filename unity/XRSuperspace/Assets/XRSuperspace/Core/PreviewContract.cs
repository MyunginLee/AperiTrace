using System;
using System.Collections.Generic;

namespace XRSuperspace
{
    public readonly struct WindowHalfSpace
    {
        public readonly double A,B,C;
        public WindowHalfSpace(double a,double b,double c) {A=a;B=b;C=c;}
        // Outward unit normal: a*u+b*v+c <= 0 is inside a CCW polygon.
        public double Evaluate(Double2 p)=>A*p.X+B*p.Y+C;
    }
    public static class PreviewContract
    {
        public const int MaximumCandidates=50000;
        public static WindowHalfSpace[] HalfSpaces(IReadOnlyList<Double2> window)
        {
            if(window==null||window.Count<3||window.Count>64) throw new ArgumentException("convex CCW window needs 3..64 vertices");
            var result=new WindowHalfSpace[window.Count];
            for(int i=0;i<window.Count;i++)
            {
                var a=window[i];var b=window[(i+1)%window.Count];double dx=b.X-a.X,dy=b.Y-a.Y,length=Math.Sqrt(dx*dx+dy*dy);
                if(double.IsNaN(length)||double.IsInfinity(length)||length<=0) throw new ArgumentException("invalid window edge");
                result[i]=new WindowHalfSpace(dy/length,-dx/length,(dx*a.Y-dy*a.X)/length);
                for(int j=0;j<window.Count;j++) if(result[i].Evaluate(window[j])>1e-12) throw new ArgumentException("window must be convex and CCW");
            }
            return result;
        }
        public static int DisplayStride(int candidates,int budget)
        {
            if(candidates<0||candidates>MaximumCandidates||budget<1) throw new ArgumentOutOfRangeException();
            return Math.Max(1,(candidates+budget-1)/budget);
        }
        public static void ValidateCounts(int candidates,int states,int allowed)
        {
            if(candidates<1||candidates>MaximumCandidates||states!=candidates||allowed!=candidates) throw new ArgumentException("preview buffer counts do not match bounded candidates");
        }
        public static PrecisionComparison Compare(RuntimeDataset data,Double2 phi,PointState[] cpu,bool[] allowed,uint[] gpuStates,uint[] gpuActive)
        {
            ValidateCounts(data.Candidates.Count,cpu.Length,allowed.Length);ValidateCounts(cpu.Length,gpuStates.Length,gpuActive.Length);
            var ids=new List<string>();int cs=0,gs=0,cb=0,gb=0;double maximum=0;
            for(int i=0;i<cpu.Length;i++)
            {
                if(gpuStates[i]>2||gpuActive[i]>1) throw new ArgumentException("invalid GPU state");
                bool selected=Membership.IsSelected(cpu[i],data.Metadata.IncludeBoundary)&&allowed[i];
                if(selected) cs++;if(gpuActive[i]!=0) gs++;if(cpu[i]==PointState.Boundary) cb++;if(gpuStates[i]==2) gb++;
                if((uint)cpu[i]!=gpuStates[i]||selected!=(gpuActive[i]!=0))
                {
                    var id=data.Candidates[i].Id;ids.Add(id.ToString());maximum=Math.Max(maximum,Math.Abs(Membership.Distance(data.DoubleWindow,new Double2(data.Project(id,3)+phi.X,data.Project(id,4)+phi.Y))));
                }
            }
            return new PrecisionComparison {Phi=new[]{phi.X,phi.Y},DatasetChecksum=data.Metadata.BinarySha256,CpuSelected=cs,GpuSelected=gs,SelectedCountDifference=gs-cs,CpuBoundary=cb,GpuBoundary=gb,DisagreementIds=ids.ToArray(),MaximumAuthoritativeDistanceMagnitude=maximum};
        }
    }
    public sealed class PrecisionComparison
    {
        public double[] Phi;
        public string DatasetChecksum;
        public int CpuSelected,GpuSelected,SelectedCountDifference,CpuBoundary,GpuBoundary;
        public string[] DisagreementIds;
        public double MaximumAuthoritativeDistanceMagnitude,CpuMembershipMilliseconds,ReadbackMilliseconds;
    }
}
