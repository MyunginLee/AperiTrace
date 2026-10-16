using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace.Tests
{
    public sealed class QuestResearchTests
    {
        [Test] public void CorruptAndLegacyPreferencesRestoreSafeDisplayDefaults()
        {
            var p=new QuestPreferences {Diffraction=null};p.Validate();Assert.That(p.Diffraction.Palette,Is.EqualTo("viridis"));
            p.Diffraction.Minimum=-2;p.Validate();Assert.That(p.Diffraction.Minimum,Is.Zero);
        }
        [Test] public void VrAndDesktopUseTheSamePresentationSettingsAndUnits()
        {
            var s=new DiffractionDisplay {Palette="cividis",VisibleMinimum=.1,HideCenter=true};var p=new QuestPreferences {Diffraction=s};p.Validate();
            Assert.That(p.Diffraction.Visible(.2,new Double3(1,0,0)),Is.True);
            Assert.That(p.Diffraction.Visible(.2,new Double3(0,0,0)),Is.False);
            Assert.That(p.Diffraction.Visible(.01,new Double3(1,0,0)),Is.False);
            Assert.That(QuestUiTheme.Background,Is.EqualTo(DesktopTheme.Panel));
        }
        [Test] public void LinkedZoomCannotCreateAngularOverlapByEnlargement()
        {
            Assert.That(QuestViewTools.ClampZoom(10,true),Is.EqualTo(1));Assert.That(QuestViewTools.ClampZoom(.01f,true),Is.EqualTo(.25f));
            Assert.That(QuestViewTools.ClampZoom(10,false),Is.EqualTo(2));
        }
        [Test] public void GridSharesAdaptiveSpacingAndHasNoRayTarget()
        {
            var go=new GameObject("Grid",typeof(RectTransform),typeof(Canvas));
            try {var grid=go.AddComponent<QuestPlaneGrid>();grid.raycastTarget=false;grid.Configure(.7f,.3,2.4f,false,true,"dimensionless u/v",1);
                double first=grid.Minor;grid.Configure(.7f,.6,2.4f,false,true,"dimensionless u/v",2);
                Assert.That(grid.Minor,Is.LessThan(first));Assert.That(grid.raycastTarget,Is.False);
                Assert.That(grid.GetComponent<CanvasRenderer>(),Is.Not.Null);
                Assert.That(grid.GetComponentInChildren<Text>().supportRichText,Is.True);}
            finally {Object.DestroyImmediate(go);}
        }
    }
}
