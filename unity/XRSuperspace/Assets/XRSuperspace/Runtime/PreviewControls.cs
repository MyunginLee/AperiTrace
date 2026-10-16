using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRSuperspace
{
    public sealed class PreviewControls : MonoBehaviour
    {
        public GpuPreview Preview;
        public PreviewProfiler Profiler;
        Text status;
        LabState shownState;
        int shownRevision=-1,shownTier=-1;
        PrecisionMode shownMode;
        string shownMessage,shownBackend;
        string error;
        public string LastFailure {get;private set;}="none";
        void Awake()
        {
            var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Preview.Lab.GetComponent<LabXrMode>().DesktopCamera;gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();var rect=(RectTransform)transform;rect.sizeDelta=new Vector2(1800,230);rect.localScale=Vector3.one*.0023f;
            var text=new GameObject("Precision and backend",typeof(RectTransform)).AddComponent<Text>();text.transform.SetParent(transform,false);status=text;status.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");status.fontSize=22;status.color=Color.white;status.alignment=TextAnchor.UpperCenter;status.rectTransform.sizeDelta=new Vector2(1800,140);status.rectTransform.anchoredPosition=new Vector2(0,40);
            string[] titles={"Validate GPU","Replace with CPU","Python fixture","Diagnostic","Desktop","Quest","Capture","Force fallback","Save profile"};
            for(int i=0;i<titles.Length;i++)
            {
                int index=i;var button=new GameObject(titles[i],typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(transform,false);var r=(RectTransform)button.transform;r.sizeDelta=new Vector2(190,46);r.anchoredPosition=new Vector2((i-4)*198,-75);button.GetComponent<Image>().color=new Color(.08f,.15f,.22f,.95f);
                var label=new GameObject("Label",typeof(RectTransform)).AddComponent<Text>();label.transform.SetParent(button.transform,false);label.rectTransform.sizeDelta=r.sizeDelta;label.font=status.font;label.fontSize=18;label.alignment=TextAnchor.MiddleCenter;label.color=Color.white;label.text=titles[i];button.GetComponent<Button>().onClick.AddListener(()=>Run(index));
            }
        }
        void Run(int index)
        {
            try {error=null;if(index==0) Preview.Validate(false);else if(index==1) Preview.Validate(true);else if(index==2) Preview.UsePythonFixture();else if(index<7) Preview.SetTier(index-3);else if(index==7) Preview.SetFallback(!Preview.ForceFallback);else {string folder=Path.Combine(Application.persistentDataPath,"SuperspaceProfiles");Directory.CreateDirectory(folder);string path=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"_"+Guid.NewGuid().ToString("N")+".json");using var file=new StreamWriter(new FileStream(path,FileMode.CreateNew));file.Write(JsonConvert.SerializeObject(Profiler.Snapshot(),Formatting.Indented));error="Saved local profile: "+path;}}
            catch(Exception e) {LastFailure=e.GetType().Name+": "+e.Message;error=e.Message;}Refresh();
        }
        public void Refresh() {var s=Preview.Lab.State;shownState=s;shownRevision=s?.Model.Revision??-1;shownMode=Preview.Mode;shownTier=Preview.TierIndex;shownMessage=Preview.Message;shownBackend=Preview.Backend;status.text=$"{Preview.Mode} · {Preview.Backend} · {Preview.Tier.Title}\nScientific candidates {s?.Dataset.Candidates.Count} / selected {s?.SelectedCount}; display stride {Preview.DrawStride}, ≤{Preview.VisibleCandidateBudget} per view · edges ≤{Preview.Tier.EdgeBudget}\n{error??Preview.Message}\nGPU ε {s?.Dataset.Metadata.PreviewEpsilon:G3} / CPU ε {s?.Dataset.Metadata.AuthorityEpsilon:G3} · profile = Editor/player observation; Quest not tested";}
        void Update() {var s=Preview.Lab.State;if(shownState!=s||shownRevision!=(s?.Model.Revision??-1)||shownMode!=Preview.Mode||shownTier!=Preview.TierIndex||shownMessage!=Preview.Message||shownBackend!=Preview.Backend) Refresh();}
    }
}
