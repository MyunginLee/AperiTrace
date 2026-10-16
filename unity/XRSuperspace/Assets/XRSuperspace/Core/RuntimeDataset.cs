using System;
using System.Collections.Generic;
using System.Globalization;

namespace XRSuperspace
{
    public readonly struct LatticeId : IEquatable<LatticeId>, IComparable<LatticeId>
    {
        public readonly int N0, N1, N2, N3, N4;
        public LatticeId(int a, int b, int c, int d, int e) { N0=a; N1=b; N2=c; N3=d; N4=e; }
        public int this[int j] => j switch { 0=>N0, 1=>N1, 2=>N2, 3=>N3, 4=>N4, _=>throw new ArgumentOutOfRangeException(nameof(j)) };
        public LatticeId Neighbor(int j)
        {
            int[] values={N0,N1,N2,N3,N4}; values[j]=checked(values[j]+1);
            return new LatticeId(values[0],values[1],values[2],values[3],values[4]);
        }
        public bool Equals(LatticeId other) => CompareTo(other)==0;
        public override bool Equals(object obj) => obj is LatticeId other && Equals(other);
        public override int GetHashCode() { unchecked { int h=N0; for(int j=1;j<5;j++) h=h*397^this[j]; return h; } }
        public int CompareTo(LatticeId other) { for(int j=0;j<5;j++) { int c=this[j].CompareTo(other[j]); if(c!=0) return c; } return 0; }
        public override string ToString() => string.Join(",",new[]{N0.ToString(CultureInfo.InvariantCulture),N1.ToString(CultureInfo.InvariantCulture),N2.ToString(CultureInfo.InvariantCulture),N3.ToString(CultureInfo.InvariantCulture),N4.ToString(CultureInfo.InvariantCulture)});
    }
    public readonly struct Double2
    {
        public readonly double X,Y;
        public Double2(double x,double y) { X=x;Y=y; }
    }
    public readonly struct Double3
    {
        public readonly double X,Y,Z;
        public Double3(double x,double y,double z) { X=x;Y=y;Z=z; }
    }
    public readonly struct Candidate
    {
        public readonly LatticeId Id;
        public readonly Double3 Physical;
        public readonly Double2 Internal;
        public readonly float Distance;
        public readonly ulong StableHash;
        public Candidate(LatticeId id,Double3 physical,Double2 uv,float distance,ulong hash) { Id=id; Physical=physical; Internal=uv; Distance=distance; StableHash=hash; }
    }
    public readonly struct GraphEdge
    {
        public readonly int Source,Target,Generator;
        public readonly sbyte Sign;
        public GraphEdge(int source,int target,int generator,sbyte sign=1) { Source=source;Target=target;Generator=generator;Sign=sign; }
    }
    public sealed class RuntimeMetadata
    {
        public const string SchemaVersion="1.0.0";
        public string BinarySha256 { get; internal set; }
        public string PayloadSha256 { get; internal set; }
        public string AuthoritativeSha256 { get; internal set; }
        // A standalone binary does not verify that its historical source file exists.
        public bool AuthoritativeSourceFileVerified => false;
        public bool IncludeBoundary { get; internal set; }
        public Double3 DisplayScales { get; internal set; }
        public Double2 Phi0 { get; internal set; }
        public double AuthorityEpsilon { get; internal set; }
        public double PreviewEpsilon { get; internal set; }
        public double CoordinateTolerance { get; internal set; }
        public double DistanceTolerance { get; internal set; }
    }
    public sealed class RuntimeDataset
    {
        public RuntimeMetadata Metadata { get; }
        public IReadOnlyList<Candidate> Candidates { get; }
        public IReadOnlyList<Double2> Window { get; }
        public IReadOnlyList<Double2> DoubleWindow { get; }
        public IReadOnlyList<GraphEdge> Edges { get; }
        public IReadOnlyList<bool> Selected { get; }
        public IReadOnlyList<bool> Boundary { get; }
        readonly double[,] basis;
        internal RuntimeDataset(RuntimeMetadata metadata,double[,] q,Candidate[] points,Double2[] window,Double2[] doubleWindow,GraphEdge[] edges,bool[] selected,bool[] boundary)
        {
            Metadata=metadata; basis=q; Candidates=Array.AsReadOnly(points);Window=Array.AsReadOnly(window);DoubleWindow=Array.AsReadOnly(doubleWindow);
            Edges=Array.AsReadOnly(edges);Selected=Array.AsReadOnly(selected);Boundary=Array.AsReadOnly(boundary);
        }
        public double Basis(int row,int column)=>basis[row,column];
        public double Project(LatticeId id,int row) { double value=0;for(int j=0;j<5;j++) value+=basis[row,j]*id[j];return value; }
    }
}
