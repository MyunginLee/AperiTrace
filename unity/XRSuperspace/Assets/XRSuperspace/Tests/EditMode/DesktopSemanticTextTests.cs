using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
namespace XRSuperspace.Tests
{
    public sealed class DesktopSemanticTextTests
    {
        static string Strip(string text)=>Regex.Replace(text,@"</?color(?:=#[0-9A-F]{6})?>","");
        [TestCase("qx   -0.12078830000000001\nReal   1.25E-17\nI / N squared   0.0001")]
        [TestCase("n = (-1,0,-1,0,0)\nSigned window distance\n-1.0233345472033855E-10")]
        [TestCase("Ready / sample 190\nUpdating / intensities withheld\nAllowed x: -0.25 to 0.25")]
        public void HighlightingNeverRoundsOrRewritesScientificNumbers(string plain)
        {Assert.That(Strip(DesktopSemanticText.Format(plain)),Is.EqualTo(plain));}
        [Test] public void VariablesNumbersIdentitiesAndStatusesHaveDistinctRoles()
        {
            Assert.That(DesktopSemanticText.Format("qx   1.25"),Does.Contain("#8CC7F5>qx").And.Contain("#E8C088>1.25"));
            Assert.That(DesktopSemanticText.Format("n = (-1,0,0,0,0)"),Does.Contain("#C2B1F2>-1"));
            Assert.That(DesktopSemanticText.Format("Ready / Updating"),Does.Contain("#8CD5B6>Ready").And.Contain("#F1C275>Updating"));
        }
        [Test] public void DatasetOrPathTextCannotInjectRichTextMarkup()
        {var text=DesktopSemanticText.Format("<size=200>9</size><color=red>0</color>");Assert.That(text,Does.Not.Contain("<size").And.Not.Contain("<color=red>"));}
        [Test] public void ChecksumsRemainOneIdentityToken()
        {
            string hash=new string('a',64);var text=DesktopSemanticText.Format("SHA256 "+hash);
            Assert.That(text,Does.Contain("#C2B1F2>"+hash+"</color>"));Assert.That(Strip(text),Is.EqualTo("SHA256 "+hash));
        }
        static double Linear(double v)=>v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);
        static double Light(Color c)=>.2126*Linear(c.r)+.7152*Linear(c.g)+.0722*Linear(c.b);
        [TestCase(false)] [TestCase(true)]
        public void SemanticTextHasBodyContrastOnDataAndDialogSurfaces(bool highContrast)
        {
            foreach(DesktopSemanticText.Role role in Enum.GetValues(typeof(DesktopSemanticText.Role)))
            foreach(var surface in new[]{DesktopTheme.Background,DesktopTheme.Panel,DesktopTheme.Surface,DesktopTheme.Accent})
            {double a=Light(DesktopSemanticText.Tint(role,highContrast)),b=Light(surface);Assert.That((Math.Max(a,b)+.05)/(Math.Min(a,b)+.05),Is.GreaterThanOrEqualTo(4.5),role+" "+surface);}
        }
        [Test] public void SemanticMarkupUsesColorOnlyNotSyntheticBoldSizeOrFontChanges()
        {var text=DesktopSemanticText.Format("Physical coordinates\nx 1\nReady");Assert.That(text,Does.Not.Contain("<b>").And.Not.Contain("<size").And.Not.Contain("<i>"));}
        [Test] public void RichTextDoesNotMultiplyRoleColorsByItsMutedForeground()
        {
            var root=new GameObject("Semantic label test",typeof(RectTransform));
            try
            {
                var label=DesktopTheme.Label("readout",root.transform,"",17);label.color=DesktopTheme.Muted;
                DesktopSemanticText.Apply(label,"qx 1.25",false,DesktopTheme.Muted);
                Assert.That(label.color,Is.EqualTo(Color.white));Assert.That(label.text,Does.Contain("#8CC7F5>qx").And.Contain("#E8C088>1.25"));
                Assert.That(Strip(label.text),Is.EqualTo("qx 1.25"));
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
