using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRSuperspace.Editor;

namespace XRSuperspace.Tests
{
    public sealed class DemoTests
    {
        static RuntimeDataset Data()=>Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1/canonical.qc5d"));
        static InteractionModel PathModel()
        {
            var m=new InteractionModel(Data());for(int i=1;i<=16;i++) m.Apply(DemoNarrative.PathSample(i),m.Filter);return m;
        }
        [Test] public void TenScientificNarrativeStepsKeepClaimBoundaries()
        {
            Assert.That(DemoNarrative.Titles.Length,Is.EqualTo(10));Assert.That(DemoNarrative.Explanations.Length,Is.EqualTo(10));
            Assert.That(DemoNarrative.Explanations[3],Does.Contain("FIXED W"));Assert.That(DemoNarrative.Explanations[5],Does.Contain("not an inferred chemical bond"));
            Assert.That(DemoNarrative.Explanations[9],Does.Contain("No perception or learning claim"));Assert.Throws<ArgumentOutOfRangeException>(()=>DemoNarrative.ValidateStep(10));
        }
        [Test] public void FixedPathHasExactWaypointsAndBothEventKinds()
        {
            Assert.That(DemoNarrative.PathSample(0).X,Is.Zero);Assert.That(DemoNarrative.PathSample(8).X,Is.EqualTo(.181));
            Assert.That(DemoNarrative.PathSample(12).X,Is.EqualTo(-.119));Assert.That(DemoNarrative.PathSample(16).Y,Is.EqualTo(.047));
            var a=PathModel();Assert.That(a.Journal.Count,Is.EqualTo(17));Assert.That(a.Journal.SelectMany(f=>f.Entered).Any(),Is.True);Assert.That(a.Journal.SelectMany(f=>f.Exited).Any(),Is.True);
            Assert.That(JsonConvert.SerializeObject(a.Journal),Is.EqualTo(JsonConvert.SerializeObject(PathModel().Journal)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DemoNarrative.PathSample(-1));Assert.Throws<ArgumentOutOfRangeException>(()=>DemoNarrative.PathSample(17));
        }
        [Test] public void JournalRestoreRecomputesEventsAndPreservesScientificEndpoint()
        {
            var source=PathModel();var target=new InteractionModel(Data());target.Apply(source.Phi,source.Filter);string key=target.Key("reciprocal_h2_qz0");int revision=target.Revision;
            var frames=JsonConvert.DeserializeObject<ChangeFrame[]>(JsonConvert.SerializeObject(source.Journal));target.RestoreJournal(frames);
            Assert.That(target.Key("reciprocal_h2_qz0"),Is.EqualTo(key));Assert.That(target.Revision,Is.EqualTo(revision));Assert.That(JsonConvert.SerializeObject(target.Journal),Is.EqualTo(JsonConvert.SerializeObject(source.Journal)));
            frames[1].Entered=new[]{"999,0,0,0,0"};string before=JsonConvert.SerializeObject(target.Journal);Assert.Throws<FormatException>(()=>target.RestoreJournal(frames));Assert.That(JsonConvert.SerializeObject(target.Journal),Is.EqualTo(before));
        }
        [Test] public void DemoSceneIsSeparateAndSerializedWithoutChangingOriginalBuildList()
        {
            EditorSceneManager.OpenScene(DemoSetup.ScenePath);var demo=UnityEngine.Object.FindFirstObjectByType<ScientificDemo>();Assert.That(demo,Is.Not.Null);
            Assert.That(demo.Instrument,Is.Not.Null);Assert.That(demo.Capture.Demo,Is.SameAs(demo));Assert.That(demo.UI.Demo,Is.SameAs(demo));Assert.That(demo.Settings.Captures.Length,Is.EqualTo(11));
            foreach(var p in demo.Settings.Captures) p.Validate();Assert.That(EditorBuildSettings.scenes[0].path,Is.EqualTo(GpuSetup.ScenePath));
        }
        [TestCase("../escape")][TestCase("CON")][TestCase("nul")][TestCase("foo.png")][TestCase("a/b")][TestCase("")][TestCase("two words")]
        public void UnsafeCaptureNamesAreRejected(string name)=>Assert.Throws<ArgumentException>(()=>DemoCapture.SafeName(name));
        [Test] public void HashUsesExactUtf8StateBytes()=>Assert.That(DemoCapture.Hash("{}"),Is.EqualTo("44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"));
        [TestCase(255,1080)][TestCase(1920,255)][TestCase(4097,1080)][TestCase(4096,4096)]
        public void CaptureDimensionBudgetsAreEnforced(int width,int height)
        {
            var p=ScriptableObject.CreateInstance<DemoCapturePreset>();try {p.Width=width;p.Height=height;Assert.Throws<ArgumentException>(p.Validate);}finally {UnityEngine.Object.DestroyImmediate(p);}
        }
        [Test] public void CaptureRejectsNonfiniteCameraAndOutOfDomainPhi()
        {
            var p=ScriptableObject.CreateInstance<DemoCapturePreset>();try {p.Fov=float.NaN;Assert.Throws<ArgumentException>(p.Validate);p.Fov=42;p.PhiX=.251;Assert.Throws<ArgumentException>(p.Validate);p.PhiX=0;p.CameraPosition=new Vector3(float.PositiveInfinity,0,0);Assert.Throws<ArgumentException>(p.Validate);}finally {UnityEngine.Object.DestroyImmediate(p);}
        }
    }
}
