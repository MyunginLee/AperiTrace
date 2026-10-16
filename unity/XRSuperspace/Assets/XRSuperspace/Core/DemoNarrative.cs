using System;

namespace XRSuperspace
{
    public static class DemoNarrative
    {
        public static readonly string[] Titles={
            "1 / Exact five-integer identity", "2 / Exact 3D + 2D decomposition",
            "3 / Reveal the internal acceptance window", "4 / Selection in fixed W",
            "5 / Reveal the physical point set", "6 / Parent-lattice adjacency",
            "7 / Translate the phason coordinate", "8 / Track entered / exited stable IDs",
            "9 / The associated diffraction state", "10 / Linked overview"
        };
        public static readonly string[] Explanations={
            "n=(n0,n1,n2,n3,n4) is an exact Z5 tuple, not an array row or a visual position.",
            "r=P_parallel n and u=P_perp n retain the linked 5D state. The two displays are separated only for presentation.",
            "W is the derived projection of all 32 vertices of the centered 5D unit cube, not an arbitrary drawn decagon.",
            "Selection tests u+phi in FIXED W. Blue/teal=selected, amber=boundary, gray=rejected. White highlights the same exact n.",
            "Reveal the precomputed selected 3D aperiodic model. This is a canonical dimensionless model, not a refined alloy.",
            "Edges join selected n and n +/- e_j. The graph is parent-lattice adjacency, not an inferred chemical bond.",
            "Replay 17 fixed samples: (0,0) -> (0.181,0.047) -> (-0.119,0.047) -> (0.181,0.047). Physical r does not move; internal coordinates translate.",
            "Membership events are logged at each exact sample, independently of presentation timing. Between-sample crossing times are not measured.",
            "The same active finite patch supplies +exp(i q.r), real unit weights and I/N^2. Stale/current state is explicit; n is not reciprocal h.",
            "Interactive computational tomography: a linked representation of a 5D state as 3D physical plus 2D internal coordinates. No perception or learning claim."
        };
        public static Double2 PathSample(int index)
        {
            if(index<0||index>16) throw new ArgumentOutOfRangeException(nameof(index));
            if(index<=8) return index==8?new Double2(.181,.047):new Double2(.181*index/8,.047*index/8);
            if(index<=12) return index==12?new Double2(-.119,.047):new Double2(.181+(-.119-.181)*(index-8)/4,.047);
            return index==16?new Double2(.181,.047):new Double2(-.119+(.181+.119)*(index-12)/4,.047);
        }
        public static void ValidateStep(int step) {if(step<0||step>=10) throw new ArgumentOutOfRangeException(nameof(step));}
    }
}
