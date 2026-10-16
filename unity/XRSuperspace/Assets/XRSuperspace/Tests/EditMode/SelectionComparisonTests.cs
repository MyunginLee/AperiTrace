using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class SelectionComparisonTests
    {
        static LabState State()=>new LabState(Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1/canonical.qc5d")));
        [Test] public void SnapshotIsCopiedAndDoesNotChangeScientificHistory()
        {
            var state=State();string key=state.Model.Key("");int frames=state.Model.Journal.Count;
            var reference=new SelectionComparison(state);var old=(bool[])state.Model.Active.Clone();
            Assert.That(state.Model.Key(""),Is.EqualTo(key));Assert.That(state.Model.Journal.Count,Is.EqualTo(frames));
            state.SetPhi(new Double2(.25,.10));
            for(int i=0;i<old.Length;i++) Assert.That(reference.WasActive(i),Is.EqualTo(old[i]));
            Assert.That(reference.Phi.X,Is.EqualTo(state.Dataset.Metadata.Phi0.X));
        }
        [Test] public void CountsAreEndpointSetDifferencesNotIntermediateCrossings()
        {
            var state=State();var reference=new SelectionComparison(state);var old=(bool[])state.Model.Active.Clone();
            state.SetPhi(new Double2(.25,.10));var now=state.Model.Active;var counts=reference.Counts(now);
            Assert.That(counts.added,Is.EqualTo(Enumerable.Range(0,now.Length).Count(i=>!old[i]&&now[i])));
            Assert.That(counts.removed,Is.EqualTo(Enumerable.Range(0,now.Length).Count(i=>old[i]&&!now[i])));
            Assert.That(counts.retained+counts.added,Is.EqualTo(state.SelectedCount));
            state.SetPhi(reference.Phi);counts=reference.Counts(state.Model.Active);
            Assert.That(counts.added+counts.removed,Is.Zero,"Return to baseline must not keep accumulated crossings");
        }
        [Test] public void RemovedEdgesUseStableUndirectedEndpoints()
        {
            var state=State();var reference=new SelectionComparison(state);var old=state.Edges.ToArray();
            Assert.That(reference.RemovedEdges(old.Select(e=>new GraphEdge(e.Target,e.Source,e.Generator)).ToArray()),Is.Empty);
            state.SetPhi(new Double2(.25,.10));var keys=state.Edges.Select(SelectionComparison.EdgeKey).ToHashSet();
            var expected=old.Where(e=>!keys.Contains(SelectionComparison.EdgeKey(e))).Select(SelectionComparison.EdgeKey);
            Assert.That(reference.RemovedEdges(state.Edges).Select(SelectionComparison.EdgeKey),Is.EqualTo(expected));
        }
        [Test] public void DashesHaveRealGapsAndRemainBounded()
        {
            var dashes=SelectionComparisonOverlay.DashIntervals(.2f,.028f,.019f);
            Assert.That(dashes.Length,Is.GreaterThan(1));
            for(int i=0;i<dashes.Length;i++) {Assert.That(dashes[i].x,Is.InRange(0,1));Assert.That(dashes[i].y,Is.InRange(dashes[i].x,1));if(i>0) Assert.That(dashes[i].x,Is.GreaterThan(dashes[i-1].y));}
            Assert.That(SelectionComparisonOverlay.DashIntervals(10000,.01f,.01f).Length,Is.LessThanOrEqualTo(256));
            Assert.That(SelectionComparisonOverlay.DashIntervals(float.NaN,.01f,.01f),Is.Empty);
            Assert.That(SelectionComparisonOverlay.DashIntervals(1,0,.01f),Is.Empty);
        }
        [Test] public void ComparisonRespectsScientificFiltersNotJustWindowColour()
        {
            var state=State();var reference=new SelectionComparison(state);var filter=new AnalysisFilter {CropEnabled=true,Radius=.3,ZMax=1};
            state.Model.Apply(state.Phi,filter);var counts=reference.Counts(state.Model.Active);
            Assert.That(counts.added,Is.Zero);Assert.That(counts.removed,Is.GreaterThan(0));
            Assert.That(counts.retained,Is.EqualTo(state.SelectedCount));
        }
    }
}
