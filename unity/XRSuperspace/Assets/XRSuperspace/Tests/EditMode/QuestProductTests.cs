using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class QuestProductTests
    {
        string folder;
        // EditMode does not run EventSystem MonoBehaviour lifecycle. Invoke its
        // managed lifecycle directly; SendMessage asserts outside PlayMode.
        static void EventLifecycle(UnityEngine.EventSystems.EventSystem events,string method)=>
            typeof(UnityEngine.EventSystems.EventSystem).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(events,null);
        [SetUp] public void Setup() {folder=Path.Combine(Path.GetTempPath(),"AperiTrace-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);}
        [TearDown] public void Cleanup()
        {
            string full=Path.GetFullPath(folder);
            if(Path.GetDirectoryName(full)==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(full).StartsWith("AperiTrace-test-"))
                Directory.Delete(full,true);
        }
        [Test] public void CompletedSaveIsUniqueValidAndListed()
        {
            string a=LocalSessionStore.Save(folder,"{\"schema\":\"test\",\"value\":1}");
            string b=LocalSessionStore.Save(folder,"{\"schema\":\"test\",\"value\":2}");
            Assert.That(a,Is.Not.EqualTo(b));Assert.That(LocalSessionStore.Read(folder,a),Does.Contain("\"value\":1"));
            Assert.That(LocalSessionStore.List(folder),Has.Length.EqualTo(2));Assert.That(Directory.GetFiles(folder,"*.part"),Is.Empty);
        }
        [Test] public void InvalidSerializationDoesNotCreateAnyFile()
        {
            Assert.Throws<Newtonsoft.Json.JsonReaderException>(()=>LocalSessionStore.Save(folder,"not json"));
            Assert.That(Directory.GetFiles(folder),Is.Empty);
        }
        [Test] public void InterruptedPublishPreservesPreviousSaveAndCleansTemporaryFile()
        {
            string first=LocalSessionStore.Save(folder,"{\"value\":1}");
            Assert.Throws<IOException>(()=>LocalSessionStore.Save(folder,"{\"value\":2}",_=>throw new IOException("Injected storage failure")));
            Assert.That(LocalSessionStore.List(folder),Is.EqualTo(new[]{first}));Assert.That(Directory.GetFiles(folder,"*.part"),Is.Empty);
            Assert.That(File.ReadAllText(first),Is.EqualTo("{\"value\":1}"));
        }
        [Test] public void MissingAndEscapingSessionsAreRejected()
        {
            Assert.Throws<FileNotFoundException>(()=>LocalSessionStore.Read(folder,Path.Combine(folder,"demo-missing.json")));
            Assert.Throws<IOException>(()=>LocalSessionStore.Read(folder,Path.Combine(folder,"..","demo-other.json")));
        }
        [Test] public void PauseAndFocusAreIndependent()
        {
            var go=new GameObject("Lifecycle test");var lifecycle=go.AddComponent<QuestLifecycle>();
            try
            {
                lifecycle.SetPaused(true);lifecycle.SetFocused(false);lifecycle.SetPaused(false);Assert.That(lifecycle.Suspended,Is.True);
                lifecycle.SetFocused(true);Assert.That(lifecycle.Suspended,Is.False);
                lifecycle.SetPaused(true);lifecycle.SetFocused(true);Assert.That(lifecycle.Suspended,Is.True);
            }
            finally {UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void CorruptPreferenceBoundsAreClamped()
        {
            var p=new QuestPreferences {TextSize=999,UiSize=-1,View=100,PanelHeight=-3,Controllers=42};p.Validate();
            Assert.That(p.TextScale,Is.EqualTo(1.35f));Assert.That(p.UiScale,Is.EqualTo(1));Assert.That(p.View,Is.EqualTo(3));
            Assert.That(p.Controllers,Is.EqualTo(2));Assert.That(p.PanelHeight,Is.Zero);
        }
        [Test] public void InterFacesAndLicenseAreBundled()
        {
            foreach(int weight in new[]{0,1,2}) Assert.That(QuestUiTheme.Font(weight).name,Does.StartWith("Inter"));
            Assert.That(Resources.Load<TextAsset>("QuestFonts/LICENSE").text,Does.Contain("SIL OPEN FONT LICENSE"));
        }
        [Test] public void TextStylesUseActualFacesNotSyntheticBold()
        {
            var go=new GameObject("Type style",typeof(RectTransform));var text=go.AddComponent<UnityEngine.UI.Text>();
            try {QuestUiTheme.Style(text,34,2);Assert.That(text.font.name,Does.Contain("SemiBold"));Assert.That(text.fontStyle,Is.EqualTo(FontStyle.Normal));Assert.That(text.supportRichText,Is.False);}
            finally {UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void ControlFeedbackStatesAreDistinct()
        {
            var go=new GameObject("Button",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));
            try {var button=go.GetComponent<UnityEngine.UI.Button>();QuestUiTheme.Button(button);Assert.That(button.colors.highlightedColor,Is.Not.EqualTo(button.colors.selectedColor));Assert.That(button.colors.normalColor,Is.Not.EqualTo(button.colors.disabledColor));}
            finally {UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void ControllerPointerDisablesInheritedElasticVisuals()
        {
            var root=new GameObject("Inactive pointer");root.SetActive(false);
            var origin=new GameObject("Aim pose");origin.transform.SetParent(root.transform);
            try
            {
                var ray=root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
                var line=root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
                ray.blendVisualLinePoints=true;line.overrideInteractorLineOrigin=true;
                line.smoothMovement=true;line.snapEndpointIfAvailable=true;line.bendingEnabledInteractionLayers=-1;line.lineBendRatio=.5f;
                ControllerAimRay.ConfigureStraightPointer(ray,line,origin.transform);
                Assert.That(ray.rayOriginTransform,Is.SameAs(origin.transform));Assert.That(line.lineOriginTransform,Is.SameAs(origin.transform));
                Assert.That(ray.lineType,Is.EqualTo(UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor.LineType.StraightLine));
                Assert.That(ray.blendVisualLinePoints||line.overrideInteractorLineOrigin||line.smoothMovement||line.snapEndpointIfAvailable,Is.False);
                Assert.That(line.bendingEnabledInteractionLayers.value,Is.Zero);Assert.That(line.lineBendRatio,Is.EqualTo(1f));
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void AndroidDiagnosticChunksPreserveLongJsonWithoutTruncation()
        {
            string json="{\"long\":\""+new string('x',2600)+"\"}";
            var parts=QuestDeviceDiagnostics.LogParts(json,123456);
            var restored=new System.Text.StringBuilder();
            for(int i=0;i<parts.Length;i++)
            {
                string prefix="[AperiTraceDiagPart] 123456 "+(i+1)+"/"+parts.Length+" ";
                Assert.That(parts[i],Does.StartWith(prefix));Assert.That(parts[i].Length,Is.LessThan(900));
                restored.Append(parts[i].Substring(prefix.Length));
            }
            Assert.That(restored.ToString(),Is.EqualTo(json));
        }
        [Test] public void DestroyedSelectedUiObjectDoesNotThrowInKeyboardGuard()
        {
            var previous=UnityEngine.EventSystems.EventSystem.current;
            var root=new GameObject("Selection lifetime",typeof(UnityEngine.EventSystems.EventSystem));
            var selected=new GameObject("Transient menu button",typeof(RectTransform));
            try
            {
                var events=root.GetComponent<UnityEngine.EventSystems.EventSystem>();EventLifecycle(events,"OnEnable");UnityEngine.EventSystems.EventSystem.current=events;
                events.SetSelectedGameObject(selected);UnityEngine.Object.DestroyImmediate(selected);
                Assert.DoesNotThrow(()=>QuestUiTheme.HasTextInputFocus());Assert.That(QuestUiTheme.HasTextInputFocus(),Is.False);
            }
            finally {EventLifecycle(root.GetComponent<UnityEngine.EventSystems.EventSystem>(),"OnDisable");UnityEngine.Object.DestroyImmediate(root);if(selected!=null) UnityEngine.Object.DestroyImmediate(selected);if(previous!=null) UnityEngine.EventSystems.EventSystem.current=previous;}
        }
        [Test] public void RetiredMenuClearsSelectionBeforeItsObjectsAreDestroyed()
        {
            var previous=UnityEngine.EventSystems.EventSystem.current;
            var root=new GameObject("Events",typeof(UnityEngine.EventSystems.EventSystem));var menu=new GameObject("Retired menu");
            var selected=new GameObject("Button",typeof(RectTransform));selected.transform.SetParent(menu.transform);
            try
            {
                var events=root.GetComponent<UnityEngine.EventSystems.EventSystem>();EventLifecycle(events,"OnEnable");UnityEngine.EventSystems.EventSystem.current=events;
                events.SetSelectedGameObject(selected);QuestUiTheme.ClearSelectionWithin(menu.transform);
                Assert.That(ReferenceEquals(events.currentSelectedGameObject,null),Is.True);
                UnityEngine.Object.DestroyImmediate(selected);Assert.DoesNotThrow(()=>QuestUiTheme.HasTextInputFocus());
            }
            finally {EventLifecycle(root.GetComponent<UnityEngine.EventSystems.EventSystem>(),"OnDisable");UnityEngine.Object.DestroyImmediate(menu);UnityEngine.Object.DestroyImmediate(root);if(previous!=null) UnityEngine.EventSystems.EventSystem.current=previous;}
        }
        [TestCase(.73f,.85f,.63f,2.4f)]
        [TestCase(.73f,1.2f,.63f,3.2f)]
        public void LinkedAngularSpacingIncludesBothEnvelopesAndFiveDegreeGap(float a,float b,float c,float distance)
        {
            float angle=QuestExperienceUI.LinkedAngle(a,b,c,distance);
            Assert.That(angle-Mathf.Atan2(a,distance)*Mathf.Rad2Deg-Mathf.Atan2(b,distance)*Mathf.Rad2Deg,Is.GreaterThanOrEqualTo(4.99f));
            Assert.That(angle-Mathf.Atan2(c,distance)*Mathf.Rad2Deg-Mathf.Atan2(b,distance)*Mathf.Rad2Deg,Is.GreaterThanOrEqualTo(4.99f));
        }
    }
}
