using System;

namespace XRSuperspace
{
    // Generated from the C5 equations, never copied from a fixture or renderer floats.
    public static class CanonicalMath
    {
        public static double[,] GenerateBasis()
        {
            var q = new double[5,5];
            double scale = Math.Sqrt(2.0/5.0);
            for(int j=0;j<5;j++)
            {
                double angle=2*Math.PI*j/5;
                q[0,j]=scale*Math.Cos(angle);q[1,j]=scale*Math.Sin(angle);
                q[2,j]=1/Math.Sqrt(5);
                q[3,j]=scale*Math.Cos(2*angle);q[4,j]=scale*Math.Sin(2*angle);
            }
            return q;
        }
        public static double Project(double[,] q,LatticeId id,int row)
        {
            if(q==null||q.GetLength(0)!=5||q.GetLength(1)!=5||row<0||row>=5)
                throw new ArgumentException("projection requires a 5x5 basis and valid row");
            double value=0;for(int j=0;j<5;j++) value+=q[row,j]*id[j];return value;
        }
        public static RuntimeDataset Reconstruct(RuntimeDataset parsed)
        {
            if(parsed==null) throw new ArgumentNullException(nameof(parsed));
            var q=GenerateBasis();var window=Membership.GenerateWindow(q);
            var points=new Candidate[parsed.Candidates.Count];
            for(int i=0;i<points.Length;i++)
            {
                var id=parsed.Candidates[i].Id;
                points[i]=new Candidate(id,new Double3(Project(q,id,0),Project(q,id,1),Project(q,id,2)),
                    new Double2(Project(q,id,3),Project(q,id,4)),0,TupleHash.Compute(id));
            }
            // Stored masks/graph/coordinates are deliberately not used to compute parity.
            return new RuntimeDataset(parsed.Metadata,q,points,window,window,Array.Empty<GraphEdge>(),
                new bool[points.Length],new bool[points.Length]);
        }
    }
}
