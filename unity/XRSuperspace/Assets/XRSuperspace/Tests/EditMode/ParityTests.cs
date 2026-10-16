using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class ParityTests
    {
        static RuntimeDataset Golden()=>Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1/canonical.qc5d"));
        [Test] public void IndependentlyGeneratedBasisIsOrthonormal()
        {
            var q=CanonicalMath.GenerateBasis();
            for(int i=0;i<5;i++) for(int j=0;j<5;j++) {
                double dot=0;for(int k=0;k<5;k++) dot+=q[i,k]*q[j,k];
                Assert.That(dot,Is.EqualTo(i==j?1:0).Within(1e-12));
            }
        }
        [Test] public void ProjectionIsFromEquationsNotDecodedFloatCoordinates()
        {
            var parsed=Golden();var generated=CanonicalMath.Reconstruct(parsed);var q=CanonicalMath.GenerateBasis();
            Assert.That(generated.Candidates.Any(c=>c.Physical.X!=parsed.Candidates.First(x=>x.Id.Equals(c.Id)).Physical.X),Is.True);
            for(int i=0;i<generated.Candidates.Count;i++) {
                var c=generated.Candidates[i];Assert.That(c.StableHash,Is.EqualTo(TupleHash.Compute(c.Id)));
                for(int row=0;row<5;row++) Assert.That(generated.Project(c.Id,row),Is.EqualTo(CanonicalMath.Project(q,c.Id,row)));
            }
            Assert.That(generated.Edges.Count,Is.Zero);Assert.That(generated.Selected.Any(x=>x),Is.False);
        }
        [TestCase(-1)][TestCase(5)] public void InvalidProjectionRowRefused(int row)=>Assert.Throws<ArgumentException>(()=>CanonicalMath.Project(CanonicalMath.GenerateBasis(),new LatticeId(),row));
        [Test] public void NullReconstructionRefused()=>Assert.Throws<ArgumentNullException>(()=>CanonicalMath.Reconstruct(null));
        [Test] public void HullComesFromAll32CubeVerticesAndRetainsFiniteCornerDistance()
        {
            var window=Membership.GenerateWindow(CanonicalMath.GenerateBasis());Assert.That(window.Length,Is.EqualTo(10));
            double radius=window.Max(w=>Math.Sqrt(w.X*w.X+w.Y*w.Y));
            Assert.That(Membership.Distance(window,new Double2(0,0)),Is.GreaterThan(0));
            foreach(var w in window) Assert.That(Membership.Distance(window,w),Is.EqualTo(0).Within(1e-15));
            var p=new Double2(window[0].X*2,window[0].Y*2);
            Assert.That(Membership.Distance(window,p),Is.EqualTo(-radius).Within(1e-12));
        }
        [TestCase(0,0)][TestCase(.05,-.04)][TestCase(1e-7,-2e-8)][TestCase(.25,-.25)]
        public void GeneratedPhasonStatesGraphAndStructureFactorRemainConsistent(double x,double y)
        {
            var d=CanonicalMath.Reconstruct(Golden());var m=new InteractionModel(d);m.Apply(new Double2(x,y),m.Filter);
            var states=Membership.Evaluate(d,new Double2(x,y));Assert.That(m.States,Is.EqualTo(states));
            foreach(var edge in m.Edges) {
                Assert.That(m.Active[edge.Source]&&m.Active[edge.Target],Is.True);
                Assert.That(d.Candidates[edge.Source].Id.Neighbor(edge.Generator),Is.EqualTo(d.Candidates[edge.Target].Id));
            }
            var zero=ReciprocalCalculation.Sample(m,new Double3(0,0,0));Assert.That(zero[0],Is.EqualTo(m.SelectedCount));
            Assert.That(zero[1],Is.Zero);Assert.That(zero[2],Is.EqualTo((double)m.SelectedCount*m.SelectedCount));Assert.That(zero[3],Is.EqualTo(1));
            var positive=ReciprocalCalculation.Sample(m,new Double3(1,2,0));var negative=ReciprocalCalculation.Sample(m,new Double3(-1,-2,0));
            Assert.That(positive[0],Is.EqualTo(negative[0]).Within(1e-12));Assert.That(positive[1],Is.EqualTo(-negative[1]).Within(1e-12));
            Assert.That(positive[2],Is.EqualTo(negative[2]).Within(1e-12));
        }
        [Test] public void NearPhasonCaseIsNotOnScientificBoundary()
        {
            var d=CanonicalMath.Reconstruct(Golden());var phi=new Double2(1e-7,-2e-8);
            var distance=d.Candidates.Select(c=>Math.Abs(Membership.Distance(d.DoubleWindow,new Double2(d.Project(c.Id,3)+phi.X,d.Project(c.Id,4)+phi.Y)))).ToArray();
            Assert.That(distance.Min(),Is.GreaterThan(d.Metadata.AuthorityEpsilon));Assert.That(distance.Min(),Is.LessThan(d.Metadata.PreviewEpsilon));
            Assert.That(Membership.Evaluate(d,phi).Any(s=>s==PointState.Boundary),Is.False);
        }
    }
}
