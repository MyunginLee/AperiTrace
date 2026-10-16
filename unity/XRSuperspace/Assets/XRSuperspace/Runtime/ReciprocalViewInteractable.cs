using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace
{
    public sealed class ReciprocalViewInteractable : XRSimpleInteractable
    {
        public ReciprocalInstrument Instrument;
        public override bool IsSelectableBy(IXRSelectInteractor interactor)=>
            (Instrument.Lab.GetComponent<ScientificDemo>() is not ScientificDemo demo||!demo.Menu.InputBlocked&&QuestProductInput.Allows(interactor.transform,demo))&&base.IsSelectableBy(interactor);
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);if(Instrument.Lab.GetComponent<ScientificDemo>() is ScientificDemo demo&&demo.Menu.InputBlocked) return;
            Transform source=args.interactorObject.transform;if(args.interactorObject is XRRayInteractor ray) source=ray.rayOriginTransform;
            int row=-1;if(args.interactorObject is XRDirectInteractor) {float distance=.12f;for(int i=0;i<Instrument.Peaks.Length;i++) {float d=Vector3.Distance(source.position,Instrument.PeakPosition(i));if(d<distance) {distance=d;row=i;}}}
            else row=Instrument.Pick(new Ray(source.position,source.forward));if(row>=0) Instrument.SelectedPeak=row;
        }
    }
}
