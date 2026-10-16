using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class DesktopThemeTests
    {
        static double Linear(double value)=>value<=.04045?value/12.92:System.Math.Pow((value+.055)/1.055,2.4);
        [TestCase(.011,.02)] [TestCase(.21,.5)] [TestCase(3,5)] [TestCase(6,10)] [TestCase(0,1)]
        public void CalibratedGridUsesLegibleOneTwoFiveIntervals(double target,double expected)
        {Assert.That(DesktopViewGrid.NiceInterval(target),Is.EqualTo(expected).Within(1e-12));}
        [TestCase(1,120,1.5f)] [TestCase(1,-120,2f/3)] [TestCase(20,120,20)] [TestCase(.1f,-120,.1f)]
        public void WheelNavigationIsResponsiveReversibleAndBounded(float start,float delta,float expected)
        {Assert.That(DesktopWorkspace.WheelZoom(start,delta),Is.EqualTo(expected).Within(1e-6f));}
        static double Luminance(Color color)=>.2126*Linear(color.r)+.7152*Linear(color.g)+.0722*Linear(color.b);
        static double Contrast(Color a,Color b)=>(System.Math.Max(Luminance(a),Luminance(b))+.05)/(System.Math.Min(Luminance(a),Luminance(b))+.05);
        [Test] public void PrimaryBodyTextHasAtLeastFourPointFiveNominalContrast()
        {
            foreach(var surface in new[]{DesktopTheme.Background,DesktopTheme.Panel,DesktopTheme.Surface,DesktopTheme.Accent,QuestUiTheme.Destructive})
                Assert.That(Contrast(DesktopTheme.Text,surface),Is.GreaterThanOrEqualTo(4.5));
        }
        [Test] public void SecondaryTextDoesNotBecomeLowContrastDecoration()
        {
            foreach(var surface in new[]{DesktopTheme.Background,DesktopTheme.Panel,DesktopTheme.Surface}) Assert.That(Contrast(DesktopTheme.Muted,surface),Is.GreaterThanOrEqualTo(4.5));
        }
        [TestCase(-5,1f)] [TestCase(999,1.3f)]
        public void CorruptDesktopTextPreferenceDoesNotIndexOutsideItsBounds(int size,float expected)
        {Assert.That(new DesktopPreferences {TextSize=size}.TextScale,Is.EqualTo(expected));}
        [Test] public void ActualBundledDesktopCompositorShaderIsAvailable()
        {Assert.That(Resources.Load<Shader>("DesktopCaptureComposite"),Is.Not.Null);}
        [Test] public void DesktopActionsDoNotStackOutlinesOrEmboldenGlyphs()
        {
            var root=new GameObject("Desktop theme test",typeof(RectTransform));
            try
            {
                var button=DesktopTheme.Button("action",root.transform,"Explore",()=>{},19);
                Assert.That(button.GetComponents<UnityEngine.UI.Outline>().Length,Is.LessThanOrEqualTo(1));
                Assert.That(button.GetComponent<QuestFocusFeedback>(),Is.Not.Null);
                var text=button.GetComponentInChildren<UnityEngine.UI.Text>();
                Assert.That(text.font,Is.SameAs(QuestUiTheme.Font(0)));
                Assert.That(text.GetComponents<UnityEngine.UI.BaseMeshEffect>(),Is.Empty);
            }
            finally {Object.DestroyImmediate(root);}
        }
        [TestCase(-1,0)] [TestCase(0,0)] [TestCase(.5f,.5f)] [TestCase(1,1)] [TestCase(2,1)]
        public void PresentationEaseHasExactClampedEndpoints(float input,float expected)
        {Assert.That(DesktopMotion.Ease(input),Is.EqualTo(expected));}
        [Test] public void PresentationEaseIsMonotonicWithoutOvershoot()
        {float last=0;for(int i=0;i<=100;i++) {float current=DesktopMotion.Ease(i/100f);Assert.That(current,Is.InRange(last,1));last=current;}}
    }
}
