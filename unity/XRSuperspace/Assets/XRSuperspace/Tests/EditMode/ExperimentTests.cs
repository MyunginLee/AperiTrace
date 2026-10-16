using System;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XRSuperspace.Editor;

namespace XRSuperspace.Tests
{
    public sealed class ExperimentTests
    {
        [TestCase("../escape.json")]
        [TestCase("../../other/run.json")]
        public void ExperimentOutputCannotEscape(string relative)
        {
            string parent=Path.Combine(Path.GetTempPath(),"aperitrace-experiment-path-test");
            Assert.Throws<ArgumentException>(()=>ExperimentBatch.ContainedPath(parent,relative));
        }
        [Test] public void ContainedNestedInputIsAllowed()
        {
            string parent=Path.Combine(Path.GetTempPath(),"aperitrace-experiment-path-test");
            Assert.That(ExperimentBatch.ContainedPath(parent,"E1_basis/vectors.json"),Is.EqualTo(Path.GetFullPath(Path.Combine(parent,"E1_basis/vectors.json"))));
        }
        [Test] public void QuantileUsesDeclaredUpperIndex()
        {
            Assert.That(ExperimentBatch.Percentile(new double[]{4,1,3,2},.5),Is.EqualTo(3));
            Assert.That(ExperimentBatch.Percentile(new double[]{4,1,3,2},.95),Is.EqualTo(4));
        }
        [Test] public void UnmeasuredFramesCannotBecomeZero()
            =>Assert.Throws<ArgumentException>(()=>ExperimentBatch.Percentile(Array.Empty<double>(),.5));
        [Test] public void NonfiniteFrameTraceIsRejected()
            =>Assert.Throws<ArgumentException>(()=>ExperimentBatch.Percentile(new[]{double.NaN},.5));
        [Test] public void ExactConfiguredCapacityBoundary()
        {
            Assert.DoesNotThrow(()=>PreviewContract.ValidateCounts(50000,50000,50000));
            Assert.Throws<ArgumentException>(()=>PreviewContract.ValidateCounts(50001,50001,50001));
        }
        [Test] public void FreshSnapshotKeepsLegacyParserSizeAndIds()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
            var d=Qc5dParser.Load(Path.Combine(root,"tests/fixtures/canonical_small_v1/canonical.qc5d"));
            var config=JObject.Parse(File.ReadAllText(Path.Combine(root,"configs/parity_v1.json")));
            var snapshot=JObject.FromObject(ParityExport.CpuSnapshot(d,config));
            Assert.That((long)snapshot["parser"]["header"]["binary_size_bytes"],Is.EqualTo(2298));
            Assert.That(((JArray)snapshot["candidate_ids"]).Count,Is.EqualTo(31));
            Assert.That(((JArray)snapshot["cases"]).Count,Is.EqualTo(4));
        }
    }
}
