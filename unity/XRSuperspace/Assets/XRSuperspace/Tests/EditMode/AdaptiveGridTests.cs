using System;
using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class AdaptiveGridTests
    {
        [TestCase(1,true,1.5f)] [TestCase(-1,true,2f/3)] [TestCase(2,true,2.25f)]
        [TestCase(120,false,1.5f)] [TestCase(-120,false,2f/3)] [TestCase(240,false,2.25f)]
        public void NormalizedAndNativeWindowsWheelNotchesHaveTheSameResponsiveZoom(float delta,bool normalized,float expected)
        {Assert.That(DesktopWorkspace.WheelZoomFromInput(1,delta,normalized),Is.EqualTo(expected).Within(1e-6f));}
        [TestCase(24,1)] [TestCase(120,.2)] [TestCase(240,.1)] [TestCase(480,.05)] [TestCase(2400,.01)]
        public void FineSpacingGetsSmallerAsScientificUnitsGrowOnScreen(double pixels,double expected)
        {Assert.That(DesktopViewGrid.AdaptiveMinor(pixels),Is.EqualTo(expected).Within(1e-12));}
        [Test] public void ScreenSpacingIsBoundedAndMonotonicAcrossSixOrdersOfZoom()
        {
            double previous=double.PositiveInfinity;
            for(int i=0;i<=1000;i++)
            {
                double pixels=Math.Pow(10,-2+i*.006),step=DesktopViewGrid.AdaptiveMinor(pixels);
                Assert.That(step,Is.GreaterThan(0).And.LessThanOrEqualTo(previous));
                Assert.That(step*pixels,Is.InRange(15.0,38.0));previous=step;
            }
        }
        [Test] public void DisplayDpiScalingDoesNotChangeLogicalDensity()
        {Assert.That(DesktopViewGrid.AdaptiveMinor(120,1),Is.EqualTo(DesktopViewGrid.AdaptiveMinor(240,2)));}
        [TestCase(0)] [TestCase(-1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void InvalidScaleDoesNotGenerateNonFiniteGeometry(double pixels)
        {Assert.That(DesktopViewGrid.AdaptiveMinor(pixels),Is.EqualTo(1));}
        [Test] public void ThreePaneBoundsAreDisjointClippedAndSeparated()
        {
            var viewport=new Rect(20,40,900,400);
            var a=DesktopViewGrid.Pane(viewport,320,620,1,12);
            var b=DesktopViewGrid.Pane(viewport,320,620,2,12);
            var c=DesktopViewGrid.Pane(viewport,320,620,3,12);
            Assert.That(a.xMin,Is.EqualTo(20));Assert.That(c.xMax,Is.EqualTo(920));
            Assert.That(b.xMin-a.xMax,Is.EqualTo(12));Assert.That(c.xMin-b.xMax,Is.EqualTo(12));
            Assert.That(DesktopViewGrid.Pane(viewport,-100,-50,1,12).width,Is.EqualTo(0));
        }
        [Test] public void LinesCannotBleedIntoAnotherScientificSpace()
        {
            var rect=new Rect(0,0,100,80);var a=new Vector2(-100,40);var b=new Vector2(200,40);
            Assert.That(DesktopViewGrid.Clip(rect,ref a,ref b),Is.True);
            Assert.That(a.x,Is.EqualTo(0).Within(1e-5));Assert.That(b.x,Is.EqualTo(100).Within(1e-5));
            a=new Vector2(-10,-10);b=new Vector2(-10,100);Assert.That(DesktopViewGrid.Clip(rect,ref a,ref b),Is.False);
        }
        [Test] public void TiltedGridSegmentsAreClippedAtBothEdges()
        {var a=new Vector2(-50,-50);var b=new Vector2(150,150);Assert.That(DesktopViewGrid.Clip(new Rect(0,0,100,80),ref a,ref b),Is.True);Assert.That(a,Is.EqualTo(Vector2.zero));Assert.That(b,Is.EqualTo(new Vector2(80,80)));}
    }
}
