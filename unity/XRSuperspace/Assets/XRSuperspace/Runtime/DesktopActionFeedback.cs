using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Stable tint on code-native surfaces; a single existing outline handles
    // focus. Avoid restarting uGUI's color tween during live style updates.
    public sealed class DesktopActionFeedback : MonoBehaviour,IPointerDownHandler,IPointerUpHandler
    {
        Button button;
        Text label;
        bool pressed;
        void Awake() {button=GetComponent<Button>();label=GetComponentInChildren<Text>();}
        void Update()
        {
            bool active=button.IsInteractable();float tint=!active?.6f:pressed?.88f:1;
            button.targetGraphic.canvasRenderer.SetColor(new Color(tint,tint,tint,active?1:.7f));
            if(label==null) label=GetComponentInChildren<Text>();
            if(label!=null) label.color=active?DesktopTheme.Text:DesktopTheme.Muted;
        }
        public void OnPointerDown(PointerEventData data) {if(data.button==PointerEventData.InputButton.Left) pressed=true;}
        public void OnPointerUp(PointerEventData data)=>pressed=false;
        void OnDisable()=>pressed=false;
    }
}
