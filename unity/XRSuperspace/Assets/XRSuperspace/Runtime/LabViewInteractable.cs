using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace
{
    public sealed class LabViewInteractable : XRSimpleInteractable
    {
        public LabController Lab;
        public bool InternalView;
        public override bool IsSelectableBy(IXRSelectInteractor interactor)=>
            (Lab.GetComponent<ScientificDemo>() is not ScientificDemo demo||!demo.Menu.InputBlocked&&QuestProductInput.Allows(interactor.transform,demo))&&base.IsSelectableBy(interactor);
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if(Lab==null||Lab.State==null||(Lab.GetComponent<ScientificDemo>() is ScientificDemo demo&&demo.Menu.InputBlocked)) return;
            Transform source=args.interactorObject.transform;
            if(args.interactorObject is XRRayInteractor ray) source=ray.rayOriginTransform;
            int index=args.interactorObject is XRDirectInteractor?Lab.View.PickNearest(source.position,InternalView):Lab.View.Pick(new Ray(source.position,source.forward),InternalView);
            if(index>=0) Lab.Select(index);
        }
    }
}
