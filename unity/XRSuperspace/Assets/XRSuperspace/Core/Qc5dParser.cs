using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace XRSuperspace
{
    public static class Qc5dParser
    {
        public const int HeaderBytes=192,MaximumBytes=256*1024*1024,MaximumCandidates=2000000;
        sealed class Reader
        {
            readonly byte[] bytes;public int Position;
            public Reader(byte[] data,int offset=0) { bytes=data;Position=offset; }
            public byte Byte() { if(Position>=bytes.Length) throw new FormatException("truncated runtime field");return bytes[Position++]; }
            public ushort U16()=>(ushort)(Byte()|Byte()<<8);
            public uint U32()=>(uint)(U16()|((uint)U16()<<16));
            public int I32()=>unchecked((int)U32());
            public ulong U64() { ulong lo=U32();return lo|((ulong)U32()<<32); }
            public float F32() { float x=BitConverter.Int32BitsToSingle(I32());RequireFinite(x,"float32");return x; }
            public double F64() { double x=BitConverter.Int64BitsToDouble(unchecked((long)U64()));RequireFinite(x,"float64");return x; }
            public string Digest() { var value=new byte[32];for(int i=0;i<32;i++) value[i]=Byte();return Hex(value); }
        }
        static void Require(bool pass,string message) { if(!pass) throw new FormatException(message); }
        static void RequireFinite(double x,string name)=>Require(!double.IsNaN(x)&&!double.IsInfinity(x),name+" is nonfinite");
        public static string Hex(byte[] value)=>BitConverter.ToString(value).Replace("-","").ToLowerInvariant();
        public static string Sha256(byte[] bytes,int offset=0,int count=-1) { using var sha=SHA256.Create();return Hex(sha.ComputeHash(bytes,offset,count<0?bytes.Length-offset:count)); }
        public static RuntimeDataset Load(string path)
        {
            Require(new FileInfo(path).Length<=MaximumBytes,"runtime exceeds 256 MiB byte budget");return Parse(File.ReadAllBytes(path));
        }
        public static RuntimeDataset Parse(byte[] input)
        {
            if(input==null) throw new ArgumentNullException(nameof(input));
            Require(input.Length>=HeaderBytes+32&&input.Length<=MaximumBytes,"runtime truncated or exceeds byte budget");
            var r=new Reader(input);byte[] magic={81,67,53,68,82,84,0,0};foreach(byte b in magic) Require(r.Byte()==b,"runtime magic is not QC5DRT");
            Require(r.U16()==1&&r.U16()==0,"unsupported runtime schema; expected 1.0.0");
            Require(r.U32()==0x01020304,"runtime endianness marker mismatch");
            uint flags=r.U32();Require(flags==3||flags==7,"runtime flags must be 3 or 7");
            Require(r.U32()==HeaderBytes,"runtime header length mismatch");
            uint count=r.U32(),vertices=r.U32();ulong edgeCount=r.U64();
            Require(count<=MaximumCandidates&&vertices==10&&edgeCount<=5UL*count,"runtime counts exceed bounds");
            ulong length=504UL+54UL*count+12UL*edgeCount;Require(length==(ulong)input.Length,"runtime count/length mismatch, truncation or trailing bytes");
            string authority=r.Digest(),payload=r.Digest();Require(authority!=new string('0',64),"authoritative source digest is absent");
            var scales=new Double3(r.F64(),r.F64(),r.F64());var phi=new Double2(r.F64(),r.F64());
            double epsilon=r.F64(),preview=r.F64(),atol=r.F64(),distanceAtol=r.F64();
            Require(scales.X>0&&scales.Y>0&&scales.Z>0&&Math.Max(scales.X,Math.Max(scales.Y,scales.Z))<=1e6,"invalid display scales");
            Require(Math.Abs(phi.X)<=1e6&&Math.Abs(phi.Y)<=1e6&&epsilon>=0&&epsilon<=1e6&&preview>=0&&preview<=1e-3,"invalid phi/epsilon");
            Require(atol>0&&atol<=1e-6&&distanceAtol>0&&distanceAtol<=1e-6,"invalid conversion tolerances");
            Require(r.U32()==52&&r.U32()==12&&r.U64()==0,"record stride/reserved mismatch");
            string trailer=new Reader(input,input.Length-32).Digest();Require(Sha256(input,0,input.Length-32)==trailer,"runtime integrity checksum mismatch");
            Require(Sha256(input,HeaderBytes,input.Length-HeaderBytes-32)==payload,"runtime payload checksum mismatch");
            var q=new double[5,5];for(int i=0;i<5;i++) for(int j=0;j<5;j++) { double value=r.F64();double expected=i switch {0=>Math.Sqrt(2.0/5)*Math.Cos(2*Math.PI*j/5),1=>Math.Sqrt(2.0/5)*Math.Sin(2*Math.PI*j/5),2=>1/Math.Sqrt(5),3=>Math.Sqrt(2.0/5)*Math.Cos(4*Math.PI*j/5),_=>Math.Sqrt(2.0/5)*Math.Sin(4*Math.PI*j/5)};
                Require(Math.Abs(value-expected)<=1e-12,"basis differs from canonical C5 equations");q[i,j]=value; }
            var doubleWindow=Membership.GenerateWindow(q);var window=new Double2[10];for(int i=0;i<10;i++) { window[i]=new Double2(r.F32(),r.F32());Require(Math.Abs(window[i].X-doubleWindow[i].X)<=atol&&Math.Abs(window[i].Y-doubleWindow[i].Y)<=atol,"window differs from generated cube hull"); }
            var points=new Candidate[(int)count];var identities=new HashSet<LatticeId>();var hashes=new HashSet<ulong>();
            double Project(LatticeId id,int row) { double v=0;for(int j=0;j<5;j++) v+=q[row,j]*id[j];return v; }
            for(int i=0;i<points.Length;i++)
            {
                var id=new LatticeId(r.I32(),r.I32(),r.I32(),r.I32(),r.I32());
                var physical=new Double3(r.F32(),r.F32(),r.F32());var uv=new Double2(r.F32(),r.F32());float distance=r.F32();ulong hash=r.U64();
                Require(identities.Add(id),"duplicate stable integer ID");Require(i==0||points[i-1].Id.CompareTo(id)<0,"candidate IDs must be tuple-lexicographic");
                Require(hashes.Add(hash)&&TupleHash.Compute(id)==hash,"stable hash collision or tuple/hash mismatch");
                Require(Math.Abs(physical.X-Project(id,0))<=atol&&Math.Abs(physical.Y-Project(id,1))<=atol&&Math.Abs(physical.Z-Project(id,2))<=atol&&Math.Abs(uv.X-Project(id,3))<=atol&&Math.Abs(uv.Y-Project(id,4))<=atol,"coordinate loss exceeds declared tolerance");
                double d=Membership.Distance(doubleWindow,new Double2(Project(id,3)+phi.X,Project(id,4)+phi.Y));
                Require(Math.Abs(distance-d)<=distanceAtol,"signed distance mismatch or excessive loss");
                points[i]=new Candidate(id,physical,uv,distance,hash);
            }
            var edges=new GraphEdge[(int)edgeCount];for(int i=0;i<edges.Length;i++) { uint a=r.U32(),b=r.U32();byte j=r.Byte();sbyte sign=unchecked((sbyte)r.Byte());ushort reserved=r.U16();Require(a<count&&b<count&&j<5&&sign==1&&reserved==0,"invalid graph endpoint/generator/sign/reserved");edges[i]=new GraphEdge((int)a,(int)b,j,sign); }
            bool[] ReadMask() { var result=new bool[(int)count];for(int i=0;i<result.Length;i++) { byte value=r.Byte();Require(value<=1,"mask byte must be 0 or 1");result[i]=value==1; }return result; }
            bool[] selected=ReadMask(),boundary=ReadMask();
            var metadata=new RuntimeMetadata { BinarySha256=Sha256(input),AuthoritativeSha256=authority,PayloadSha256=payload,DisplayScales=scales,Phi0=phi,AuthorityEpsilon=epsilon,PreviewEpsilon=preview,CoordinateTolerance=atol,DistanceTolerance=distanceAtol,IncludeBoundary=(flags&4)!=0 };
            var data=new RuntimeDataset(metadata,q,points,window,doubleWindow,edges,selected,boundary);
            var states=Membership.Evaluate(data,phi);for(int i=0;i<states.Length;i++) Require(boundary[i]==(states[i]==PointState.Boundary)&&selected[i]==Membership.IsSelected(states[i],metadata.IncludeBoundary),"saved masks differ from double phi0 membership");
            var expectedEdges=Membership.Graph(data,states);Require(expectedEdges.Length==edges.Length,"graph is incomplete or duplicated");for(int i=0;i<edges.Length;i++) Require(edges[i].Source==expectedEdges[i].Source&&edges[i].Target==expectedEdges[i].Target&&edges[i].Generator==expectedEdges[i].Generator,"graph differs from complete selected canonical graph");
            return data;
        }
    }
}
