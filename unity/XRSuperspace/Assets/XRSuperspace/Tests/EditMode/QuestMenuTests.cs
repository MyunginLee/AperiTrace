using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace XRSuperspace.Tests
{
    public sealed class QuestMenuTests
    {
        static LabState State()=>new LabState(Qc5dParser.Load(Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1/canonical.qc5d")));
        [Test] public void TwentyExamplesAreUniqueBoundedAndDefensivelyCopied()
        {
            Assert.That(QuestExampleLibrary.All.Count,Is.EqualTo(20));Assert.That(QuestExampleLibrary.All.Select(x=>x.Id).Distinct().Count(),Is.EqualTo(20));
            foreach(var example in QuestExampleLibrary.All) {example.Validate();var filter=example.AfterFilter;filter.Radius=100;Assert.That(example.AfterFilter.Radius,Is.LessThanOrEqualTo(.9));}
        }
        [Test] public void EveryExampleUsesTheSameScientificEngineAndLeavesTheReferenceUntouched()
        {
            var original=State();string key=original.Model.Key("");int frames=original.Model.Journal.Count;
            foreach(var example in QuestExampleLibrary.All)
            {
                QuestExampleLibrary.EnsureBudget(original,(example.Before,example.BeforeFilter),(example.After,example.AfterFilter));
                var model=new InteractionModel(original.Dataset);model.Apply(example.Before,example.BeforeFilter);model.Apply(example.After,example.AfterFilter);
                Assert.That(model.SelectedCount,Is.GreaterThanOrEqualTo(0));foreach(var edge in model.Edges) Assert.That(model.Active[edge.Source]&&model.Active[edge.Target],Is.True);
            }
            Assert.That(original.Model.Key(""),Is.EqualTo(key));Assert.That(original.Model.Journal.Count,Is.EqualTo(frames));
        }
        [Test] public void NamedClaimsMatchTheCanonicalFinitePatch()
        {
            var state=State();var add=QuestExampleLibrary.All.Single(x=>x.Id=="add_points");state.Model.Apply(add.Before,add.BeforeFilter);var comparison=new SelectionComparison(state);state.Model.Apply(add.After,add.AfterFilter);
            var counts=comparison.Counts(state.Model.Active);Assert.That(counts.added,Is.EqualTo(4));Assert.That(counts.removed,Is.Zero);
            var swap=QuestExampleLibrary.All.Single(x=>x.Id=="same_count");state.Model.Apply(swap.Before,swap.BeforeFilter);comparison=new SelectionComparison(state);state.Model.Apply(swap.After,swap.AfterFilter);
            counts=comparison.Counts(state.Model.Active);Assert.That(counts.added,Is.EqualTo(4));Assert.That(counts.removed,Is.EqualTo(4));Assert.That(state.SelectedCount,Is.EqualTo(15));Assert.That(comparison.RemovedEdges(state.Edges).Length,Is.GreaterThan(0));
            var tiny=QuestExampleLibrary.All.Single(x=>x.Id=="tiny_motion");state.Model.Apply(tiny.Before,tiny.BeforeFilter);var active=state.Model.Active.ToArray();state.Model.Apply(tiny.After,tiny.AfterFilter);Assert.That(state.Model.Active,Is.EqualTo(active));
        }
        [Test] public void DiffractionRespondsToTheSetButNotTinyMotionOrDisplay()
        {
            var state=State();var peaks=ReciprocalInstrument.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"generated/canonical_small_v1/interaction_reciprocal.json")),state.Dataset);
            double[] Values()=>peaks.Select(p=>ReciprocalCalculation.Sample(state.Model,p.Q)[3]).ToArray();
            var before=Values();state.SetPhi(new Double2(.181,.047));var after=Values();Assert.That(before.Zip(after,(a,b)=>Math.Abs(a-b)).Max(),Is.GreaterThan(1e-4));
            state.SetPhi(new Double2(.181001,.047));Assert.That(Values(),Is.EqualTo(after));
            var display=QuestExampleLibrary.All.Single(x=>x.Id=="display_only");state.Model.Apply(display.Before,display.BeforeFilter);before=Values();state.Model.Apply(display.After,display.AfterFilter);Assert.That(Values(),Is.EqualTo(before));
        }
        [Test] public void FullTraceRefusesExamplesWithoutAnyMutation()
        {
            var state=State();while(state.Model.Journal.Count<InteractionModel.MaximumFrames) state.SetPhi(new Double2(state.Model.Journal.Count%2==0?.181:-.119,.047));
            string key=state.Model.Key("");var example=QuestExampleLibrary.All[0];
            Assert.Throws<InvalidOperationException>(()=>QuestExampleLibrary.EnsureBudget(state,(example.Before,example.BeforeFilter),(example.After,example.AfterFilter)));
            Assert.That(state.Model.Key(""),Is.EqualTo(key));Assert.That(state.Model.Journal.Count,Is.EqualTo(InteractionModel.MaximumFrames));
        }
        [Test] public void EveryEnglishExampleFitsItsCoachCard()
        {
            var go=new GameObject("Example text bounds",typeof(RectTransform));
            try
            {
                var label=go.AddComponent<UnityEngine.UI.Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=32;label.rectTransform.sizeDelta=new Vector2(944,210);
                foreach(var example in QuestExampleLibrary.All)
                {
                    label.text=example.Instruction;Assert.That(System.Text.RegularExpressions.Regex.IsMatch(label.text,"[가-힣]"),Is.False);
                    float height=label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text,label.GetGenerationSettings(label.rectTransform.rect.size))/label.pixelsPerUnit;
                    Assert.That(height,Is.LessThanOrEqualTo(210),example.Id);
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void LeftMenuBindingMatchesOnlyTheLeftController()
        {
            Assert.That(QuestAppMenu.MenuBinding,Is.EqualTo("<XRController>{LeftHand}/menuButton"));
            InputSystem.RegisterLayout("{\"name\":\"AperiTraceTestMenu\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"menuButton\",\"layout\":\"Button\",\"offset\":128,\"format\":\"FLT\",\"sizeInBits\":32}]}");
            InputDevice left=null,right=null;InputAction action=null;
            try
            {
                left=InputSystem.AddDevice("AperiTraceTestMenu");right=InputSystem.AddDevice("AperiTraceTestMenu");InputSystem.SetDeviceUsage(left,"LeftHand");InputSystem.SetDeviceUsage(right,"RightHand");
                action=new InputAction("Test menu",InputActionType.Button,QuestAppMenu.MenuBinding);action.Enable();
                Assert.That(action.controls.Any(c=>c.device==left&&c.name=="menuButton"),Is.True);
                Assert.That(action.controls.Any(c=>c.device==right),Is.False);
            }
            finally {action?.Dispose();if(left!=null) InputSystem.RemoveDevice(left);if(right!=null) InputSystem.RemoveDevice(right);InputSystem.RemoveLayout("AperiTraceTestMenu");}
        }
    }
}
