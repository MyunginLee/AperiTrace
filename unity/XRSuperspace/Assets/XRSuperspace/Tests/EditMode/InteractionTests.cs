using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRSuperspace.Editor;

namespace XRSuperspace.Tests
{
    public sealed class InteractionTests
    {
        static string Folder(string name="canonical_small_v1")=>Path.Combine(Application.streamingAssetsPath,"generated",name);
        static RuntimeDataset Data(string name="canonical_small_v1")=>Qc5dParser.Load(Path.Combine(Folder(name),name=="canonical_small_v1"?"canonical.qc5d":"modelset_small.qc5d"));
        static InstrumentSession Session()=>new InstrumentSession {TimestampUtc="2026-10-03T00:00:00.0000000+00:00",DatasetChecksum=Data().Metadata.BinarySha256,AuthorityChecksum=Data().Metadata.AuthoritativeSha256,Phi=new[]{.181,.047},Filter=new AnalysisFilter {Radius=.9,ZMax=.9},SelectedStableId="-1,0,-1,0,0",CameraPosition=new[]{0f,.6f,-4.2f},CameraRotation=new float[3],CameraPivot=new[]{0f,.6f,1.3f},PhysicalOrigin=new[]{-1.65f,1.4f,1.3f},InternalOrigin=new[]{0f,1.4f,1.3f},ReciprocalOrigin=new[]{1.65f,1.4f,1.3f},PanelOrigin=new[]{-2.3f,-1f,1.1f},CameraFov=36,PhasonPath=new[]{new[]{0.0,0.0},new[]{.181,.047}},PhasonPathFilter=new AnalysisFilter {Radius=.9,ZMax=.9}};
        [Test] public void ExactTupleSearchKeepsInt32Identity()
        {
            Assert.That(InstrumentSession.ParseTuple("-1 0 -1 0 0"),Is.EqualTo(Data().Candidates[0].Id));Assert.Throws<FormatException>(()=>InstrumentSession.ParseTuple("1,2"));Assert.Throws<OverflowException>(()=>InstrumentSession.ParseTuple("2147483648,0,0,0,0"));
        }
        [Test] public void JournalEventsMatchGoldenCountsAndAreDeterministic()
        {
            string Run() {var model=new InteractionModel(Data());foreach(var phi in new[]{new Double2(.181,.047),new Double2(-.119,.047),new Double2(0,0)}) model.Apply(phi,model.Filter);return JsonConvert.SerializeObject(model.Journal);}
            Assert.That(Run(),Is.EqualTo(Run()));var frames=JArray.Parse(Run());Assert.That(frames.Count,Is.EqualTo(4));int[] entered={0,4,4,0},exited={0,0,4,4};for(int i=0;i<4;i++) {Assert.That(frames[i]["Entered"].Count(),Is.EqualTo(entered[i]));Assert.That(frames[i]["Exited"].Count(),Is.EqualTo(exited[i]));Assert.That((int)frames[i]["Frame"],Is.EqualTo(i));Assert.That((string)frames[i]["DatasetChecksum"],Is.EqualTo(Data().Metadata.BinarySha256));}
        }
        [Test] public void EventBudgetRefusesBeforeMutationAndExplicitClearRecovers()
        {
            var m=new InteractionModel(Data());while(m.Journal.Count<InteractionModel.MaximumFrames) m.Apply(new Double2(m.Journal.Count%2==0?.181:-.119,.047),m.Filter);
            string key=m.Key("");Assert.Throws<InvalidOperationException>(()=>m.Apply(new Double2(0,0),m.Filter));Assert.That(m.Key(""),Is.EqualTo(key));m.ClearJournal();Assert.That(m.Journal.Count,Is.EqualTo(1));m.Apply(new Double2(0,0),m.Filter);Assert.That(m.SelectedCount,Is.EqualTo(11));
        }
        [Test] public void ScientificCropSliceAndGraphUseExactCoordinates()
        {
            var m=new InteractionModel(Data());var f=new AnalysisFilter {CropEnabled=true,Radius=.6,ZMax=.9,SliceEnabled=true,SliceZ=0,SliceHalfWidth=.01};m.Apply(new Double2(.181,.047),f);
            for(int i=0;i<m.Active.Length;i++) if(m.Active[i]) {var p=m.Position(i);Assert.That(p.X*p.X+p.Y*p.Y,Is.LessThanOrEqualTo(.36));Assert.That(Math.Abs(p.Z),Is.LessThanOrEqualTo(.01));}
            foreach(var edge in m.Edges) {Assert.That(m.Active[edge.Source]&&m.Active[edge.Target],Is.True);Assert.That(m.Dataset.Candidates[edge.Source].Id.Neighbor(edge.Generator),Is.EqualTo(m.Dataset.Candidates[edge.Target].Id));}
            Assert.That(m.SelectedCount,Is.LessThan(15));Assert.That(m.States.Count(x=>Membership.IsSelected(x,m.Dataset.Metadata.IncludeBoundary)),Is.EqualTo(15));
        }
        [Test] public void SessionRoundtripIsByteReproducibleForFixedTimestamp()
        {
            string json=InstrumentSession.Encode(Session());Assert.That(InstrumentSession.Encode(InstrumentSession.Decode(json)),Is.EqualTo(json));Assert.That(InstrumentSession.Decode(json).Matches(Data()),Is.True);Assert.That(InstrumentSession.Decode(json).Matches(Data("modelset_small")),Is.False);
        }
        [TestCase("Schema")][TestCase("Phi")][TestCase("CameraPosition")][TestCase("DatasetChecksum")]
        public void MissingSessionFieldsAreRejected(string field)
        {
            var root=JObject.Parse(InstrumentSession.Encode(Session()));root.Remove(field);Assert.Throws<FormatException>(()=>InstrumentSession.Decode(root.ToString()));
        }
        [Test] public void InvalidNonfiniteSessionAndUnsupportedConfigurationAreRejected()
        {
            var s=Session();s.Phi[0]=double.NaN;Assert.Throws<FormatException>(()=>InstrumentSession.Decode(InstrumentSession.Encode(s)));s=Session();s.DiffractionConfig="interpolated_authority";Assert.Throws<FormatException>(()=>InstrumentSession.Decode(InstrumentSession.Encode(s)));
        }
        [TestCase("canonical_small_v1")][TestCase("modelset_small")]
        public void All381ReciprocalSamplesMatchPythonComplex128(string name)
        {
            var data=Data(name);var model=new InteractionModel(data);var peaks=ReciprocalInstrument.Parse(File.ReadAllText(Path.Combine(Folder(name),"interaction_reciprocal.json")),data);Assert.That(peaks.Length,Is.EqualTo(381));
            foreach(var peak in peaks) {var actual=ReciprocalCalculation.Sample(model,peak.Q);for(int j=0;j<4;j++) Assert.That(actual[j],Is.EqualTo(peak.Reference[j]).Within(j<3?1e-9:1e-12),"q/h "+peak.H[0]+" component "+j);}
        }
        [Test] public void ReciprocalSidecarWrongDatasetAndWrongProjectionAreRejected()
        {
            string json=File.ReadAllText(Path.Combine(Folder(),"interaction_reciprocal.json"));Assert.Throws<FormatException>(()=>ReciprocalInstrument.Parse(json,Data("modelset_small")));var root=JObject.Parse(json);root["peaks"][0]["q"][0]=99;Assert.Throws<FormatException>(()=>ReciprocalInstrument.Parse(root.ToString(),Data()));
        }
        [Test] public void EmptySubsetProducesZeroRatherThanNaN()
        {
            var m=new InteractionModel(Data());m.Apply(m.Phi,new AnalysisFilter {SliceEnabled=true,SliceZ=10,SliceHalfWidth=.01});Assert.That(m.SelectedCount,Is.Zero);Assert.That(ReciprocalCalculation.Sample(m,new Double3(0,0,0)),Is.EqualTo(new double[4]));
        }
        [TestCase("phi")][TestCase("selected_count")][TestCase("fractional_h")][TestCase("missing_peak")]
        public void ReciprocalReferenceStateAndSamplingCompletenessAreValidated(string field)
        {
            var root=JObject.Parse(File.ReadAllText(Path.Combine(Folder(),"interaction_reciprocal.json")));
            if(field=="phi") root["phi"][0]=.181;if(field=="selected_count") root["selected_count"]=999;if(field=="fractional_h") root["peaks"][0]["h"][0][0]=-1.5;if(field=="missing_peak") ((JArray)root["peaks"]).RemoveAt(0);Assert.Throws<FormatException>(()=>ReciprocalInstrument.Parse(root.ToString(),Data()));
        }
        [Test] public void DiffractionStateKeyChangesForPhiAndFilterNotSelection()
        {
            var state=new LabState(Data());string key=state.Model.Key("reciprocal_h2_qz0");state.Select(0);Assert.That(state.Model.Key("reciprocal_h2_qz0"),Is.EqualTo(key));state.SetPhi(new Double2(.181,.047));Assert.That(state.Model.Key("reciprocal_h2_qz0"),Is.Not.EqualTo(key));key=state.Model.Key("reciprocal_h2_qz0");var f=state.Model.Filter.Copy();f.SliceEnabled=true;state.Model.Apply(state.Phi,f);Assert.That(state.Model.Key("reciprocal_h2_qz0"),Is.Not.EqualTo(key));
        }
        [Test] public void VisualPhiSignMovesCandidatesPositiveAndLeavesWindowFixed()
        {
            EditorSceneManager.OpenScene(LabSetup.ScenePath,OpenSceneMode.Single);
            try
            {
                var lab=UnityEngine.Object.FindFirstObjectByType<LabController>();lab.SetDataset(File.ReadAllBytes(Path.Combine(Folder(),"canonical.qc5d")),File.ReadAllBytes(Path.Combine(Folder(),"metadata.json")),"canonical_small_v1/canonical.qc5d");lab.Select(0);var id=lab.State.Selection;var internalPoint=lab.View.PointPosition(0,true);var physical=lab.View.PointPosition(0,false);var window=new Vector3[lab.View.WindowLine.positionCount];lab.View.WindowLine.GetPositions(window);
                lab.SetPhi(.181,.047);Assert.That(lab.View.PointPosition(0,true)-internalPoint,Is.EqualTo(new Vector3(.181f,.047f,0)*lab.View.InternalScale).Using(Vector3Comparer()));for(int i=0;i<window.Length;i++) Assert.That(lab.View.WindowLine.GetPosition(i),Is.EqualTo(window[i]));Assert.That(lab.View.PointPosition(0,false),Is.EqualTo(physical));Assert.That(lab.State.Selection,Is.EqualTo(id));Assert.That(lab.State.SelectedCount,Is.EqualTo(15));
                int row=lab.View.PickNearest(lab.View.PointPosition(0,true),true,1e-4f);lab.Select(row);Assert.That(lab.State.Selection,Is.EqualTo(id));row=lab.View.PickNearest(lab.View.PointPosition(0,false),false,1e-4f);lab.Select(row);Assert.That(lab.State.Selection,Is.EqualTo(id));
            }
            finally {EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        static System.Collections.Generic.IEqualityComparer<Vector3> Vector3Comparer()=>new ApproximateVector();
        sealed class ApproximateVector : System.Collections.Generic.IEqualityComparer<Vector3> {public bool Equals(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-12f;public int GetHashCode(Vector3 v)=>0;}
        [Test] public void InstrumentSceneAndFivePresetsArePersisted()
        {
            EditorSceneManager.OpenScene(InstrumentSetup.ScenePath,OpenSceneMode.Single);
            try {var instrument=UnityEngine.Object.FindFirstObjectByType<LinkedInstrument>();Assert.That(instrument,Is.Not.Null);Assert.That(instrument.Presets.Length,Is.EqualTo(5));Assert.That(instrument.Presets.All(x=>x!=null),Is.True);Assert.That(instrument.Lab.View.InstrumentMode,Is.True);Assert.That(instrument.Lab.View.PointMaterial.shader.name,Is.EqualTo("XRSuperspace/TransitionPoint"));Assert.That(instrument.Reciprocal.Origin.GetComponent<ReciprocalViewInteractable>(),Is.Not.Null);Assert.Throws<IOException>(()=>InstrumentSetup.Create(false));}
            finally {EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
