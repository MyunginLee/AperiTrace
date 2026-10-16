using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRSuperspace.Editor;

namespace XRSuperspace.Tests
{
    public sealed class PreviewTests
    {
        static RuntimeDataset Golden()=>Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1/canonical.qc5d"));
        [Test] public void CandidateGpuByteLayoutIsExplicit32Bytes()=>Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<PreviewCandidate>(),Is.EqualTo(32));
        [Test] public void RevisionAndTimingAreNotScientificStateOrJournalValues()
        {
            var m=new InteractionModel(Golden());string key=m.Key("");int revision=m.Revision;m.ClearJournal();Assert.That(m.Revision,Is.EqualTo(revision));Assert.That(m.Key(""),Is.EqualTo(key));Assert.That(m.LastMembershipMilliseconds,Is.GreaterThanOrEqualTo(0));m.Apply(new Double2(.181,.047),m.Filter);Assert.That(m.Revision,Is.EqualTo(revision+1));
        }
        [Test] public void CorruptGpuStateEnumsAreRejected()
        {
            var d=Golden();var cpu=Membership.Evaluate(d,new Double2(0,0));var s=cpu.Select(x=>(uint)x).ToArray();s[0]=3;Assert.Throws<ArgumentException>(()=>PreviewContract.Compare(d,new Double2(0,0),cpu,new bool[31],s,new uint[31]));
        }
        [Test] public void HalfSpacesAreOutwardUnitNormalsInOriginalCCWOrder()
        {
            var d=Golden();var p=PreviewContract.HalfSpaces(d.DoubleWindow);Assert.That(p.Length,Is.EqualTo(10));
            for(int i=0;i<p.Length;i++) {Assert.That(p[i].A*p[i].A+p[i].B*p[i].B,Is.EqualTo(1).Within(1e-14));Assert.That(p[i].Evaluate(d.DoubleWindow[i]),Is.EqualTo(0).Within(1e-14));Assert.That(p[i].Evaluate(d.DoubleWindow[(i+1)%10]),Is.EqualTo(0).Within(1e-14));Assert.That(p[i].Evaluate(new Double2(0,0)),Is.LessThan(0));}
        }
        [Test] public void BadWindowOrderAndDegenerateEdgesAreRejected()
        {
            var w=Golden().DoubleWindow.Reverse().ToArray();Assert.Throws<ArgumentException>(()=>PreviewContract.HalfSpaces(w));w[1]=w[0];Assert.Throws<ArgumentException>(()=>PreviewContract.HalfSpaces(w));
        }
        [TestCase(0,0,0)][TestCase(31,30,31)][TestCase(31,31,32)][TestCase(50001,50001,50001)]
        public void MalformedBufferCountsAreRejectedBeforeUpload(int n,int s,int a)=>Assert.Throws<ArgumentException>(()=>PreviewContract.ValidateCounts(n,s,a));
        [TestCase(.25,.25)][TestCase(-.25,-.25)][TestCase(.25,-.25)][TestCase(-.25,.25)]
        public void HalfSpaceSignsAgreeWithAuthoritativeDistanceAtPhasonExtremes(double x,double y)
        {
            var d=Golden();var planes=PreviewContract.HalfSpaces(d.DoubleWindow);
            foreach(var c in d.Candidates) {var p=new Double2(d.Project(c.Id,3)+x,d.Project(c.Id,4)+y);double distance=Membership.Distance(d.DoubleWindow,p);if(Math.Abs(distance)>1e-10) Assert.That(planes.All(h=>h.Evaluate(p)<=0),Is.EqualTo(distance>0));}
        }
        [Test] public void ReconciliationExposesExactDisagreementIdAndDistance()
        {
            var d=Golden();var states=Membership.Evaluate(d,new Double2(0,0));var allowed=Enumerable.Repeat(true,31).ToArray();var s=states.Select(x=>(uint)x).ToArray();var a=states.Select(x=>Membership.IsSelected(x,false)?1u:0u).ToArray();s[0]=1;a[0]=1;
            var r=PreviewContract.Compare(d,new Double2(0,0),states,allowed,s,a);Assert.That(r.DisagreementIds,Is.EqualTo(new[]{d.Candidates[0].Id.ToString()}));Assert.That(r.SelectedCountDifference,Is.EqualTo(1));Assert.That(r.MaximumAuthoritativeDistanceMagnitude,Is.GreaterThan(0));
        }
        [Test] public void FourTierAssetsAndScenePreserveScientificState()
        {
            var scene=EditorSceneManager.OpenScene(GpuSetup.ScenePath,OpenSceneMode.Single);var preview=UnityEngine.Object.FindFirstObjectByType<GpuPreview>();Assert.That(preview.Tiers.Select(t=>t.Title),Is.EqualTo(new[]{"Diagnostic","Desktop","Quest","Capture"}));Assert.That(preview.Classifier,Is.Not.Null);
            var model=new InteractionModel(Golden());string key=model.Key("");var coordinates=model.Dataset.Candidates.Select(c=>model.Position(model.Dataset.Candidates.ToList().IndexOf(c))).ToArray();
            foreach(var t in preview.Tiers) {int stride=t.Stride(10813);Assert.That((10813+stride-1)/stride,Is.LessThanOrEqualTo(t.PointBudget));Assert.That(model.Key(""),Is.EqualTo(key));for(int i=0;i<coordinates.Length;i++) Assert.That(model.Position(i).X,Is.EqualTo(coordinates[i].X));}
            Assert.That(model.SelectedCount,Is.EqualTo(11));Assert.That(scene.IsValid(),Is.True);
        }
        [Test] public void MediumDatasetHasExactIdsAndBoundedFallbackCoverage()
        {
            var d=Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/modelset_medium/modelset_medium.qc5d"));var m=new InteractionModel(d);Assert.That(d.Candidates.Count,Is.EqualTo(10813));Assert.That(m.SelectedCount,Is.EqualTo(5483));Assert.That(m.BoundaryCount,Is.EqualTo(340));Assert.That(m.Edges.Length,Is.EqualTo(14380));
        }
    }
}
