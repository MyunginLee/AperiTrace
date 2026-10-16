using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRSuperspace.Editor;

namespace XRSuperspace.Tests
{
    public sealed class MaterialAdapterTests
    {
        static string Folder=>Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1");
        static JObject Sidecar()=>JObject.Parse(File.ReadAllText(Path.Combine(Folder,"metadata.json")));
        [Test] public void HistoricalCanonicalNeedsNoRewriting()
        { Assert.DoesNotThrow(()=>DatasetLabels.RequireCanonical(Sidecar())); }
        [TestCase("REAL-MATERIAL DATASET")][TestCase("UNVALIDATED IMPORT")][TestCase("")]
        public void LabelCannotOverrideCanonicalDiscriminants(string label)
        {
            var json=Sidecar();json["label"]=label;
            StringAssert.Contains(DatasetLabels.Unvalidated,Assert.Throws<FormatException>(()=>DatasetLabels.RequireCanonical(json)).Message);
        }
        [TestCase("kind","imported_material")]
        [TestCase("coordinate_units","angstrom")]
        public void ScientificKindAndUnitsCannotBeReinterpreted(string field,string value)
        { var json=Sidecar();json["metadata"][field]=value;Assert.Throws<FormatException>(()=>DatasetLabels.RequireCanonical(json)); }
        [Test] public void MaterialKindIsRefusedEvenWithCanonicalCoordinates()
        { var json=Sidecar();json["metadata"]["material"]["kind"]="imported_material";Assert.Throws<FormatException>(()=>DatasetLabels.RequireCanonical(json)); }
        [Test] public void SeparateMaterialPreviewIsNotACanonicalSidecar()
        { Assert.Throws<FormatException>(()=>DatasetLabels.RequireCanonical(JObject.Parse("{\"schema_version\":\"xr-material-preview-1.0\",\"label\":\"REAL-MATERIAL DATASET\"}"))); }
        [Test] public void FailedImportLeavesLabStateAndSelectionUntouched()
        {
            EditorSceneManager.OpenScene(LabSetup.ScenePath,OpenSceneMode.Single);
            try
            {
                var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();
                var bytes=File.ReadAllBytes(Path.Combine(Folder,"canonical.qc5d"));
                lab.SetDataset(bytes,File.ReadAllBytes(Path.Combine(Folder,"metadata.json")),"golden");lab.Select(0);
                var before=lab.State;var selection=lab.State.Selection;
                var json=Sidecar();json["metadata"]["material"]["kind"]="imported_material";
                Assert.Throws<FormatException>(()=>lab.SetDataset(bytes,System.Text.Encoding.UTF8.GetBytes(json.ToString()),"bad"));
                Assert.That(lab.State,Is.SameAs(before));Assert.That(lab.State.Selection,Is.EqualTo(selection));
                Assert.That(lab.DatasetName,Is.EqualTo("golden"));Assert.That(lab.DatasetLabel,Is.EqualTo(DatasetLabels.Canonical));
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); }
        }
    }
}
