using System;
using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class DiffractionDisplayTests
    {
        [TestCase("linear",0,1,.5,.5)]
        [TestCase("sqrt",0,1,.25,.5)]
        [TestCase("log",.001,1,.01,1.0/3)]
        [TestCase("log",.001,1,0,0)]
        public void NormalizationMatchesDefinedTransform(string scale,double min,double max,double value,double expected)
        {var s=new DiffractionDisplay {Scale=scale,Minimum=min,Maximum=max};s.Validate();Assert.That(s.Normalize(value,min,max),Is.EqualTo(expected).Within(1e-12));}
        [TestCase("viridis")] [TestCase("cividis")] [TestCase("plasma")] [TestCase("inferno")] [TestCase("magma")]
        [TestCase("gray")] [TestCase("Blues")] [TestCase("YlGnBu")] [TestCase("YlOrRd")] [TestCase("hot")]
        [TestCase("copper")] [TestCase("bone")] [TestCase("turbo")] [TestCase("Spectral")] [TestCase("coolwarm")]
        [TestCase("RdBu")] [TestCase("twilight")] [TestCase("hsv")] [TestCase("Custom")]
        public void EveryBundledOrCustomPaletteIsFinite(string name)
        {var s=new DiffractionDisplay {Palette=name};s.Validate();for(int i=0;i<=256;i++) {var c=s.Ramp(i/256.0);Assert.That(c.r,Is.InRange(0,1));Assert.That(c.g,Is.InRange(0,1));Assert.That(c.b,Is.InRange(0,1));Assert.That(c.a,Is.EqualTo(1));}}
        [Test] public void CanonicalViridisRgbEndpointsAreNotAnApproximateInventedPalette()
        {Assert.That((Color32)DiffractionColors.Sample("viridis",0),Is.EqualTo(new Color32(68,1,84,255)));Assert.That((Color32)DiffractionColors.Sample("viridis",1),Is.EqualTo(new Color32(253,231,37,255)));}
        [Test] public void ReversedAndSaturatedEndpointsAreDefined()
        {var s=new DiffractionDisplay {Invert=true};Assert.That(s.Normalize(-100,0,1),Is.EqualTo(1));Assert.That(s.Normalize(100,0,1),Is.EqualTo(0));}
        [Test] public void EmptyAndConstantAutoRangeUseTheExplicitRange()
        {
            var s=new DiffractionDisplay {AutoRange=true,Minimum=.01,Maximum=.8};
            Assert.That(s.Range(Array.Empty<double[]>(),Array.Empty<ReciprocalPeak>()),Is.EqualTo((.01,.8)));
            Assert.That(s.Range(new[]{new[]{0.0,0,1,.5}},new[]{new ReciprocalPeak {Q=new Double3(1,0,0)}}),Is.EqualTo((.01,.8)));
        }
        [Test] public void AutoLogRangeExcludesZeroAndFilteredPeaks()
        {
            var s=new DiffractionDisplay {AutoRange=true,Scale="log",Minimum=.001,HideCenter=true};
            var values=new[]{new[]{0.0,0,0,0},new[]{0.0,0,1,.01},new[]{0.0,0,1,.5}};
            var peaks=new[]{new ReciprocalPeak {Q=new Double3(0,0,0)},new ReciprocalPeak {Q=new Double3(1,0,0)},new ReciprocalPeak {Q=new Double3(2,0,0)}};
            Assert.That(s.Range(values,peaks),Is.EqualTo((.01,.5)));
        }
        [Test] public void FiltersAreInclusiveAndDoNotEditTheirInputs()
        {var s=new DiffractionDisplay {VisibleMinimum=.2,VisibleMaximum=.6,QMinimum=1,QMaximum=2,HideCenter=true};Assert.That(s.Visible(.2,new Double3(1,0,0)),Is.True);Assert.That(s.Visible(.6,new Double3(2,0,0)),Is.True);Assert.That(s.Visible(.6,new Double3(0,0,0)),Is.False);Assert.That(s.Visible(.1,new Double3(1,0,0)),Is.False);}
        [Test] public void InvalidSettingsAreRejectedBeforeApplication()
        {
            foreach(var s in new[]{new DiffractionDisplay {Minimum=double.NaN},new DiffractionDisplay {Maximum=0},new DiffractionDisplay {Scale="log"},new DiffractionDisplay {QMaximum=double.PositiveInfinity},new DiffractionDisplay {Low="red"},new DiffractionDisplay {Palette="missing"},new DiffractionDisplay {VisibleMinimum=.9,VisibleMaximum=.1}})
                Assert.Throws<ArgumentException>(()=>s.Validate());
        }
        [Test] public void ColorbarIncludesItsRequiredCanvasRenderer()
        {var go=new GameObject("Colorbar fixture",typeof(RectTransform));try {go.AddComponent<DiffractionColorbar>();Assert.That(go.GetComponent<CanvasRenderer>(),Is.Not.Null);}finally {UnityEngine.Object.DestroyImmediate(go);}}
    }
}
