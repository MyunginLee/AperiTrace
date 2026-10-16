using System;

namespace XRSuperspace
{
    // Unkeyed BLAKE2b digest_size=8, specialized to one 20-byte exact tuple.
    // Algorithm/parameter initialization: RFC 7693 sections 3.1–3.3.
    public static class TupleHash
    {
        static readonly ulong[] IV={0x6a09e667f3bcc908UL,0xbb67ae8584caa73bUL,0x3c6ef372fe94f82bUL,0xa54ff53a5f1d36f1UL,0x510e527fade682d1UL,0x9b05688c2b3e6c1fUL,0x1f83d9abfb41bd6bUL,0x5be0cd19137e2179UL};
        static readonly byte[,] Sigma={
            {0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15},
            {14,10,4,8,9,15,13,6,1,12,0,2,11,7,5,3},
            {11,8,12,0,5,2,15,13,10,14,3,6,7,1,9,4},
            {7,9,3,1,13,12,11,14,2,6,5,10,4,0,15,8},
            {9,0,5,7,2,4,10,15,14,1,11,12,6,8,3,13},
            {2,12,6,10,0,11,8,3,4,13,7,5,15,14,1,9},
            {12,5,1,15,14,13,4,10,0,7,6,3,9,2,8,11},
            {13,11,7,14,12,1,3,9,5,0,15,4,8,6,2,10},
            {6,15,14,9,11,3,0,8,12,2,13,7,1,4,10,5},
            {10,2,8,4,7,6,1,5,15,11,9,14,3,12,13,0}};
        static ulong Rotate(ulong x,int n)=>(x>>n)|(x<<(64-n));
        static void G(ulong[] v,int a,int b,int c,int d,ulong x,ulong y)
        {
            unchecked { v[a]+=v[b]+x;v[d]=Rotate(v[d]^v[a],32);v[c]+=v[d];v[b]=Rotate(v[b]^v[c],24);
                v[a]+=v[b]+y;v[d]=Rotate(v[d]^v[a],16);v[c]+=v[d];v[b]=Rotate(v[b]^v[c],63); }
        }
        public static ulong Compute(LatticeId id)
        {
            var m=new ulong[16];for(int j=0;j<5;j++) m[j/2]|=(ulong)unchecked((uint)id[j])<<((j%2)*32);
            var h=(ulong[])IV.Clone();h[0]^=0x01010008UL;
            var v=new ulong[16];Array.Copy(h,v,8);Array.Copy(IV,0,v,8,8);v[12]^=20;v[14]^=ulong.MaxValue;
            for(int round=0;round<12;round++) { int s=round%10;
                G(v,0,4,8,12,m[Sigma[s,0]],m[Sigma[s,1]]);G(v,1,5,9,13,m[Sigma[s,2]],m[Sigma[s,3]]);
                G(v,2,6,10,14,m[Sigma[s,4]],m[Sigma[s,5]]);G(v,3,7,11,15,m[Sigma[s,6]],m[Sigma[s,7]]);
                G(v,0,5,10,15,m[Sigma[s,8]],m[Sigma[s,9]]);G(v,1,6,11,12,m[Sigma[s,10]],m[Sigma[s,11]]);
                G(v,2,7,8,13,m[Sigma[s,12]],m[Sigma[s,13]]);G(v,3,4,9,14,m[Sigma[s,14]],m[Sigma[s,15]]); }
            return h[0]^v[0]^v[8];
        }
    }
}
