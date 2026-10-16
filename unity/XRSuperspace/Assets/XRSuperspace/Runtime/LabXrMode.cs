using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.UI;

namespace XRSuperspace
{
    public sealed class LabXrMode : MonoBehaviour
    {
        public Camera DesktopCamera,XrCamera;
        public GameObject XrControllers;
        public InputActionAsset Actions;
        public string TrackingStatus { get; private set; }="desktop fallback";
        bool starting;
        Canvas[] panels;
        void Awake()
        {
            if(XrControllers==null) return;
            foreach(var ray in XrControllers.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true))
            {
                var controller=ray.transform.parent;
                if(controller==null||(controller.name!="Left Controller"&&controller.name!="Right Controller")) continue;
                var aim=ray.GetComponent<ControllerAimRay>();if(aim==null) aim=ray.gameObject.AddComponent<ControllerAimRay>();
                aim.Configure(ray,controller.parent,controller.name=="Left Controller");
            }
        }
        void Start() { panels=GetComponentsInChildren<Canvas>();foreach(var panel in panels) panel.worldCamera=XRSettings.isDeviceActive?XrCamera:DesktopCamera; }
        void OnEnable() { if(Actions!=null) Actions.Enable(); }
        void OnDisable() { if(Actions!=null) Actions.Disable(); }
        void Update()
        {
            bool active=XRSettings.isDeviceActive;DesktopCamera.enabled=!active;XrCamera.enabled=active;XrControllers.SetActive(active);
            if(panels!=null) foreach(var panel in panels) panel.worldCamera=active?XrCamera:DesktopCamera;
            TrackingStatus=active?"OpenXR tracked display":"desktop fallback (no active HMD)";
            if(Keyboard.current!=null&&Keyboard.current.xKey.wasPressedThisFrame&&!starting&&!active) StartCoroutine(StartXr());
        }
        IEnumerator StartXr()
        {
            var manager=XRGeneralSettings.Instance?.Manager;if(manager==null) { TrackingStatus="XR manager unavailable; desktop retained";yield break; }
            starting=true;
            if(manager.activeLoader==null) yield return manager.InitializeLoader();
            if(manager.activeLoader!=null) manager.StartSubsystems();
            else Debug.LogWarning("[XRSuperspace] XR runtime unavailable; desktop retained");
            starting=false;
        }
    }
}
