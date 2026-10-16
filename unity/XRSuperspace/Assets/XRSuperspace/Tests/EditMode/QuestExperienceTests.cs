using NUnit.Framework;
using UnityEngine;

namespace XRSuperspace.Tests
{
    public sealed class QuestExperienceTests
    {
        [Test] public void DepthMotionCannotMoveTheTwoDimensionalControl()
        {
            var frame=new GameObject("Control plane");
            try
            {
                var start=new Vector3(1,2,3);
                Assert.That(PhasonHandleControl.PlaneDelta(frame.transform,start,start+Vector3.forward*200,.55f),Is.EqualTo(Vector2.zero));
                var delta=PhasonHandleControl.PlaneDelta(frame.transform,start,start+new Vector3(.11f,-.055f,200),.55f);
                Assert.That(delta.x,Is.EqualTo(.2f).Within(1e-6));Assert.That(delta.y,Is.EqualTo(-.1f).Within(1e-6));
            }
            finally {Object.DestroyImmediate(frame);}
        }
        [Test] public void ControlPlaneFollowsPresentationYawWithoutChangingItsCoordinates()
        {
            var frame=new GameObject("Rotated control plane");
            try
            {
                frame.transform.rotation=Quaternion.Euler(0,73,0);var start=frame.transform.position;
                var current=start+frame.transform.TransformDirection(new Vector3(.1f,.2f,5));
                var delta=PhasonHandleControl.PlaneDelta(frame.transform,start,current,.5f);
                Assert.That(delta.x,Is.EqualTo(.2f).Within(1e-6));Assert.That(delta.y,Is.EqualTo(.4f).Within(1e-6));
                Assert.That(PhasonHandleControl.PlaneDelta(frame.transform,start,new Vector3(float.NaN,0,0),.5f),Is.EqualTo(Vector2.zero));
                Assert.That(PhasonHandleControl.PlaneDelta(frame.transform,start,current,0),Is.EqualTo(Vector2.zero));
            }
            finally {Object.DestroyImmediate(frame);}
        }
        [Test] public void ControllerRaysUseAimNotGripBindings()
        {
            Assert.That(ControllerAimRay.PositionBinding(true),Is.EqualTo("<XRController>{LeftHand}/pointerPosition"));
            Assert.That(ControllerAimRay.RotationBinding(false),Is.EqualTo("<XRController>{RightHand}/pointerRotation"));
            Assert.That(ControllerAimRay.RotationBinding(true),Does.Not.Contain("deviceRotation"));
        }
        [Test] public void CardAnchorDoesNotRollOrFollowVerticalHeadTilt()
        {
            var yaw=QuestExperienceUI.HorizontalFacing(new Vector3(1,4,1));
            Assert.That(Vector3.Angle(yaw*Vector3.up,Vector3.up),Is.LessThan(.001f));
            Assert.That((yaw*Vector3.forward).y,Is.EqualTo(0).Within(1e-6));
            Assert.That(QuestExperienceUI.HorizontalFacing(Vector3.up),Is.EqualTo(Quaternion.identity));
        }
        [Test] public void DefaultCoachIsBelowEyesAndHasTimeForReading()
        {
            var settings=ScriptableObject.CreateInstance<DemoSettings>();
            try
            {
                Assert.That(settings.QuestCardBelowEyes,Is.InRange(.1f,.6f));
                Assert.That(settings.QuestCardDistance,Is.InRange(1.3f,2.5f));
                Assert.That(settings.QuestReadSeconds,Is.GreaterThanOrEqualTo(6));
                Assert.That(settings.QuestStageSeconds,Is.GreaterThan(settings.QuestReadSeconds+2*settings.QuestFadeSeconds));
                float horizontalAngle=2*Mathf.Atan(.55f/settings.QuestCardDistance)*Mathf.Rad2Deg;
                Assert.That(horizontalAngle,Is.LessThan(40));
                float cardTopAngle=Mathf.Atan((settings.QuestCardBelowEyes-.528f/2)/settings.QuestCardDistance);
                float diagramBottomAngle=Mathf.Atan((.65f-settings.QuestViewAboveEyes)/settings.QuestViewDistance);
                Assert.That(cardTopAngle,Is.GreaterThan(diagramBottomAngle),"Coach overlaps the diagram");
            }
            finally {Object.DestroyImmediate(settings);}
        }
        [Test] public void EveryEnglishInstructionFitsTheCoachCard()
        {
            var go=new GameObject("Text measurement",typeof(RectTransform));
            try
            {
                var label=go.AddComponent<UnityEngine.UI.Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize=32;label.horizontalOverflow=HorizontalWrapMode.Wrap;label.rectTransform.sizeDelta=new Vector2(944,210);
                for(int step=0;step<10;step++)
                {
                    label.text=QuestExperienceUI.GuideInstruction(step);
                    Assert.That(System.Text.RegularExpressions.Regex.IsMatch(label.text,"[가-힣]"),Is.False);
                    float height=label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text,label.GetGenerationSettings(label.rectTransform.rect.size))/label.pixelsPerUnit;
                    Assert.That(height,Is.LessThanOrEqualTo(210),"Instruction clips at step "+step);
                }
            }
            finally {Object.DestroyImmediate(go);}
        }
    }
}
