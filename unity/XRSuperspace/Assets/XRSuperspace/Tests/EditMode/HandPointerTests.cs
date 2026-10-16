using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace XRSuperspace.Tests
{
    public sealed class HandPointerTests
    {
        [Test] public void InputPublishesBeforeUiAndInteractionProcessing()
        {
            var order=(DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(HandPointer),typeof(DefaultExecutionOrder));
            Assert.That(order.order,Is.LessThan(XRInteractionUpdateOrder.k_UIInputModule));
            Assert.That(order.order,Is.LessThan(XRInteractionUpdateOrder.k_InteractionManager));
        }
        [Test] public void ReaderHasExactPressAndReleaseEdgesInTheCurrentFrame()
        {
            var reader=new XRInputButtonReader();HandPointer.Write(reader,false);HandPointer.Write(reader,true);
            Assert.That(reader.ReadIsPerformed(),Is.True);Assert.That(reader.ReadWasPerformedThisFrame(),Is.True);
            Assert.That(reader.manualValue,Is.EqualTo(1));HandPointer.Write(reader,false);
            Assert.That(reader.ReadIsPerformed(),Is.False);Assert.That(reader.ReadWasCompletedThisFrame(),Is.True);
            Assert.That(reader.manualValue,Is.Zero);
        }
        [Test] public void LostTrackingAndRegainedHeldPinchCannotAccidentallySelect()
        {
            var gate=new HandPinchGate();Assert.That(gate.Update(true,true),Is.False);
            Assert.That(gate.Update(true,false),Is.False);Assert.That(gate.Update(true,true),Is.True);
            Assert.That(gate.Update(false,true),Is.False);Assert.That(gate.Update(true,true),Is.False);
            Assert.That(gate.Update(true,false),Is.False);Assert.That(gate.Update(true,true),Is.True);
            gate.Reset();Assert.That(gate.Update(true,true),Is.False);
        }
        [Test] public void GeometricFallbackHasHysteresisAndRejectsNonFiniteDistances()
        {
            Assert.That(HandPointer.DistancePinch(.024f,false),Is.True);Assert.That(HandPointer.DistancePinch(.03f,false),Is.False);
            Assert.That(HandPointer.DistancePinch(.03f,true),Is.True);Assert.That(HandPointer.DistancePinch(.036f,true),Is.False);
            Assert.That(HandPointer.DistancePinch(float.NaN,true),Is.False);Assert.That(HandPointer.DistancePinch(-1,true),Is.False);
        }
        [Test] public void NativeAimExcludesSystemGesturesAndInvalidTracking()
        {
            var valid=MetaAimFlags.Computed|MetaAimFlags.Valid;
            Assert.That(HandPointer.NativeAimAllowed(valid),Is.True);
            Assert.That(HandPointer.NativeAimAllowed(valid|MetaAimFlags.SystemGesture),Is.False);
            Assert.That(HandPointer.NativeAimAllowed(MetaAimFlags.Computed),Is.False);
            Assert.That(HandPointer.NativeAimAllowed(MetaAimFlags.None),Is.False);
        }
        [Test] public void VisibleHandMeshHasNoColliderOrUiTargetAndHidesOnLoss()
        {
            var go=new GameObject("Synthetic tracked hand");
            try
            {
                var visual=go.AddComponent<TrackedHandVisual>();visual.PreviewSegment(Vector3.zero,new Vector3(0,.1f,0));
                Assert.That(visual.Visible,Is.True);Assert.That(visual.ValidSegments,Is.EqualTo(1));
                Assert.That(go.GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(0));
                Assert.That(go.GetComponentsInChildren<Collider>(),Is.Empty);
                Assert.That(go.GetComponentsInChildren<UnityEngine.UI.Graphic>(),Is.Empty);
                visual.Hide();Assert.That(visual.Visible,Is.False);
            }
            finally {UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
