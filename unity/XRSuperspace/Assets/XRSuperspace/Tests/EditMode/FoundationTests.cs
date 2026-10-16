using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using XRSuperspace.Editor;

namespace XRSuperspace.Tests
{
    public sealed class FoundationTests
    {
        static string Fixture=>Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1");
        static byte[] Bytes()=>File.ReadAllBytes(Path.Combine(Fixture,"canonical.qc5d"));
        static RuntimeDataset Data()=>Qc5dParser.Parse(Bytes());
        static JObject Json(string name)=>JObject.Parse(File.ReadAllText(Path.Combine(Fixture,name+".json")));
        static void Rehash(byte[] bytes)
        {
            using var sha=SHA256.Create();
            Array.Copy(sha.ComputeHash(bytes,192,bytes.Length-224),0,bytes,72,32);
            Array.Copy(sha.ComputeHash(bytes,0,bytes.Length-32),0,bytes,bytes.Length-32,32);
        }
        [Test] public void GoldenDigestAndCountsArePinned()
        {
            Assert.That(Qc5dParser.Sha256(Bytes()),Is.EqualTo("2942d45e5d6bb1c171c2cbf535943217a4c4a7e5c3355a1fa9828af5ec2dd2e8"));
            var data=Data();Assert.That(data.Candidates.Count,Is.EqualTo(31));Assert.That(data.Selected.Count(x=>x),Is.EqualTo(11));Assert.That(data.Boundary.Count(x=>x),Is.EqualTo(10));Assert.That(data.Edges.Count,Is.EqualTo(10));
            Assert.That(data.Metadata.AuthoritativeSourceFileVerified,Is.False);
        }
        [Test] public void StableIdsHashesAndMembershipMatchAllFourGoldenFrames()
        {
            var state=new LabState(Data());state.Select(0);var id=state.Selection.Value;
            foreach(var point in state.Dataset.Candidates) Assert.That(TupleHash.Compute(point.Id),Is.EqualTo(point.StableHash));
            foreach(var frame in Json("expected_phasons")["frames"])
            {
                state.SetPhi(new Double2((double)frame["phi"][0],(double)frame["phi"][1]));
                var selected=state.Dataset.Candidates.Where((p,i)=>Membership.IsSelected(state.States[i],state.Dataset.Metadata.IncludeBoundary)).Select(p=>p.Id.ToString());
                var boundary=state.Dataset.Candidates.Where((p,i)=>state.States[i]==PointState.Boundary).Select(p=>p.Id.ToString());
                Assert.That(selected,Is.EqualTo(frame["selected_ids"].Values<string>()));Assert.That(boundary,Is.EqualTo(frame["boundary_ids"].Values<string>()));Assert.That(state.Selection.Value,Is.EqualTo(id));Assert.That(state.SelectedRow,Is.EqualTo(0));
            }
            Assert.Throws<ArgumentException>(()=>state.SetPhi(new Double2(double.NaN,0)));
        }
        [Test] public void BasisAndGeneratedDoubleWindowMatchAuthoritativeJson()
        {
            var data=Data();var basis=Json("basis");var window=Json("window")["vertices_uv"];
            for(int row=0;row<5;row++) for(int j=0;j<5;j++) Assert.That(data.Basis(row,j),Is.EqualTo((double)basis[row<3?"P_parallel":"P_perp"][row<3?row:row-3][j]));
            for(int i=0;i<10;i++) {Assert.That(data.DoubleWindow[i].X,Is.EqualTo((double)window[i][0]).Within(1e-12));Assert.That(data.DoubleWindow[i].Y,Is.EqualTo((double)window[i][1]).Within(1e-12));}
        }
        [Test] public void FullSmallDatasetPassesIndependentParserChecks()
        {
            var data=Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/modelset_small/modelset_small.qc5d"));
            Assert.That(data.Candidates.Count,Is.EqualTo(981));Assert.That(data.Selected.Count(x=>x),Is.EqualTo(451));Assert.That(data.Boundary.Count(x=>x),Is.EqualTo(60));Assert.That(data.Edges.Count,Is.EqualTo(980));
            Assert.That(data.Metadata.BinarySha256,Is.EqualTo("536db6bfca96d1460bfc8632c2f3fa328e09c1dd6a3b7abf6fea8fb5a4302d0e"));
        }
        [TestCase(0,0,"magic")]
        [TestCase(8,2,"schema")]
        [TestCase(10,1,"schema")]
        [TestCase(12,0,"endianness")]
        [TestCase(16,0,"flags")]
        [TestCase(20,0,"header")]
        [TestCase(24,255,"count")]
        [TestCase(28,11,"count")]
        [TestCase(32,255,"count")]
        [TestCase(176,0,"stride")]
        [TestCase(180,0,"stride")]
        [TestCase(184,1,"reserved")]
        public void RechecksummedMalformedHeadersAreRejected(int offset,byte value,string reason)
        {
            var bytes=Bytes();bytes[offset]=value;Rehash(bytes);var error=Assert.Throws<FormatException>(()=>Qc5dParser.Parse(bytes));StringAssert.Contains(reason,error.Message);
        }
        [TestCase(199,"basis")]
        [TestCase(392,"window")]
        [TestCase(472,"ID")]
        [TestCase(495,"coordinate")]
        [TestCase(515,"distance")]
        [TestCase(516,"hash")]
        [TestCase(2084,"graph")]
        [TestCase(2204,"mask")]
        public void RechecksummedPayloadCorruptionIsRejected(int offset,string kind)
        {
            var bytes=Bytes();if(kind=="distance") {bytes[512]=0;bytes[513]=0;bytes[514]=128;bytes[515]=63;}else bytes[offset]^=128;
            Rehash(bytes);Assert.Throws<FormatException>(()=>Qc5dParser.Parse(bytes),kind);
        }
        [TestCase(0)][TestCase(192)][TestCase(2297)]
        public void TruncationFailsClearly(int length)=>Assert.Throws<FormatException>(()=>Qc5dParser.Parse(Bytes().Take(length).ToArray()));
        [Test] public void ExtraBytesAreRejected()=>Assert.Throws<FormatException>(()=>Qc5dParser.Parse(Bytes().Concat(new byte[]{0}).ToArray()));
        [Test] public void PayloadAndTrailerChecksumsAreBothEnforced()
        {
            var bytes=Bytes();bytes[500]^=1;StringAssert.Contains("integrity",Assert.Throws<FormatException>(()=>Qc5dParser.Parse(bytes)).Message);
            using var sha=SHA256.Create();Array.Copy(sha.ComputeHash(bytes,0,bytes.Length-32),0,bytes,bytes.Length-32,32);
            StringAssert.Contains("payload",Assert.Throws<FormatException>(()=>Qc5dParser.Parse(bytes)).Message);
            Assert.Throws<ArgumentNullException>(()=>Qc5dParser.Parse(null));
        }
        [Test] public void ExactTupleIdentityDoesNotDependOnDisplayCoordinates()
        {
            var a=new LatticeId(int.MinValue,-2,0,4,int.MaxValue);Assert.That(a.ToString(),Is.EqualTo("-2147483648,-2,0,4,2147483647"));Assert.That(a,Is.EqualTo(new LatticeId(int.MinValue,-2,0,4,int.MaxValue)));Assert.Throws<OverflowException>(()=>a.Neighbor(4));
        }
        [Test] public void OpenXrAndPlatformPresetsAreSerializedForBothTargets()
        {
            var pipeline=GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline,Is.Not.Null);Assert.That(pipeline.msaaSampleCount,Is.EqualTo(4));Assert.That(pipeline.supportsHDR,Is.False);
            foreach(var group in new[]{BuildTargetGroup.Standalone,BuildTargetGroup.Android})
            {
                var settings=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                Assert.That(settings,Is.Not.Null);Assert.That(settings.InitManagerOnStart,Is.EqualTo(group==BuildTargetGroup.Android));
                Assert.That(settings.Manager.activeLoaders.Any(x=>x.GetType().FullName=="UnityEngine.XR.OpenXR.OpenXRLoader"),Is.True);
                Assert.That(FeatureHelpers.GetFeatureWithIdForBuildTarget(group,"com.unity.openxr.feature.input.oculustouch").enabled,Is.True);
                Assert.That(FeatureHelpers.GetFeatureWithIdForBuildTarget(group,"com.unity.openxr.feature.input.handtrackingsubsystem").enabled,Is.True);
            }
            Assert.That(FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android,"com.unity.openxr.feature.metaquest").enabled,Is.True);
            Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android),Is.EqualTo(ScriptingImplementation.IL2CPP));
            Assert.That(PlayerSettings.Android.targetArchitectures,Is.EqualTo(AndroidArchitecture.ARM64));Assert.That(PlayerSettings.Android.minSdkVersion,Is.EqualTo(AndroidSdkVersions.AndroidApiLevel32));
        }
        [Test] public void SceneSurvivesSerializationWithActionsAndPersistentButtons()
        {
            var scene=EditorSceneManager.OpenScene(LabSetup.ScenePath,OpenSceneMode.Single);
            try
            {
                var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();Assert.That(lab,Is.Not.Null);Assert.That(lab.Settings,Is.Not.Null);Assert.That(lab.View.InternalOrigin,Is.Not.Null);Assert.That(lab.View.PhysicalOrigin,Is.Not.Null);
                Assert.That(lab.GetComponent<LabXrMode>().Actions.actionMaps.Count,Is.EqualTo(1));
                foreach(var reference in AssetDatabase.FindAssets("t:InputActionReference",new[]{LabSetup.Root+"/Settings/InputRefs"})) Assert.That(AssetDatabase.LoadAssetAtPath<InputActionReference>(AssetDatabase.GUIDToAssetPath(reference)).action,Is.Not.Null);
                foreach(var button in lab.GetComponentsInChildren<Button>()) {Assert.That(button.onClick.GetPersistentEventCount(),Is.EqualTo(1));Assert.That(button.onClick.GetPersistentTarget(0),Is.EqualTo(lab));}
                Assert.That(lab.GetComponentsInChildren<Button>().Length,Is.EqualTo(4));
                Assert.That(UnityEngine.Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,Is.EqualTo(4));
                Assert.That(UnityEngine.Object.FindObjectsByType<XRDirectInteractor>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,Is.EqualTo(4));
                foreach(var group in UnityEngine.Object.FindObjectsByType<XRInteractionGroup>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                { Assert.That(group.startingGroupMembers.Count,Is.EqualTo(2));Assert.That(group.startingGroupMembers[0],Is.TypeOf<XRDirectInteractor>());Assert.That(group.startingGroupMembers[1],Is.TypeOf<XRRayInteractor>()); }
                Assert.That(UnityEngine.Object.FindObjectsByType<HandPointer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,Is.EqualTo(2));
                Assert.That(AssetDatabase.LoadAssetAtPath<LabBuildPreset>(LabSetup.Root+"/Settings/BuildPresets/Quest3Android.asset").Target,Is.EqualTo(BuildTarget.Android));
                Assert.Throws<IOException>(()=>LabSetup.Create(false));
            }
            finally {EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Test] public void LinkedRenderingRemainsBoundedAndPhasonDoesNotMovePhysicalPoints()
        {
            EditorSceneManager.OpenScene(LabSetup.ScenePath,OpenSceneMode.Single);
            try
            {
                var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();var objects=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
                lab.SetDataset(Bytes(),File.ReadAllBytes(Path.Combine(Fixture,"metadata.json")),"golden");lab.Select(0);var id=lab.State.Selection;var physical=lab.View.PointPosition(0,false);var internalPoint=lab.View.PointPosition(0,true);
                lab.SetPhi(.181,.047);Assert.That(lab.State.Selection,Is.EqualTo(id));Assert.That(lab.View.PointPosition(0,false),Is.EqualTo(physical));Assert.That(lab.View.PointPosition(0,true).x-internalPoint.x,Is.EqualTo(.181f*lab.View.InternalScale).Within(1e-6));Assert.That(lab.State.SelectedCount,Is.EqualTo(15));
                Assert.That(lab.View.PickNearest(lab.View.PointPosition(0,true),true,1e-4f),Is.EqualTo(0));lab.ResetPhi();Assert.That(lab.State.SelectedCount,Is.EqualTo(11));
                string directory=Path.Combine(Application.streamingAssetsPath,"generated/modelset_small");lab.SetDataset(File.ReadAllBytes(Path.Combine(directory,"modelset_small.qc5d")),File.ReadAllBytes(Path.Combine(directory,"metadata.json")),"small");
                Assert.That(lab.View.BatchCount,Is.EqualTo(4));Assert.That(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,Is.EqualTo(objects));
                StringAssert.Contains("GPU compute not implemented",lab.DiagnosticText());StringAssert.Contains("SHA-256",lab.DiagnosticText());
            }
            finally {EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
