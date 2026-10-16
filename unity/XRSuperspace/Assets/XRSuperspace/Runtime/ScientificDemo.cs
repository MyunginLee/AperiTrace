using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace XRSuperspace
{
    public sealed class ScientificDemo : MonoBehaviour
    {
        public LinkedInstrument Instrument;
        public GpuPreview Preview;
        public DemoSettings Settings;
        public DemoUI UI;
        public DemoCapture Capture;
        public QuestExperienceUI QuestUI {get;private set;}
        public SelectionComparisonOverlay Comparison {get;private set;}
        public QuestAppMenu Menu {get;private set;}
        public QuestLifecycle Lifecycle {get;private set;}
        public QuestPreferences Preferences {get;private set;}
        [NonSerialized] public DesktopWorkspace Desktop;
        public bool PlaybackBlocked=>(Lifecycle!=null&&Lifecycle.Suspended)||(Menu!=null&&Menu.InputBlocked)||Capture.Busy||(Desktop!=null&&Desktop.BlocksInput);
        string savedStamp;
        string WorkStamp=>Lab.State==null?"":Lab.State.Model.Key(Instrument.Reciprocal.Config)+"|"+Lab.State.Model.Journal.Count+"|"+Instrument.Trace.Count+"|"+Instrument.Path.Count+"|"+Guided+"|"+Step+"|"+Lab.State.Selection+"|"+Lab.View.LayerSeparation+"|"+Lab.View.PointSize+"|"+Instrument.Reciprocal.SelectedPeak;
        public bool UnsavedChanges=>Lab.State!=null&&savedStamp!=WorkStamp;
#if UNITY_EDITOR
        public string SessionOutputRoot;
#endif
        public string SessionFolder
        {
            get
            {
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(SessionOutputRoot)) return SessionOutputRoot;
#endif
                if(Desktop!=null&&!string.IsNullOrEmpty(Desktop.SessionRootOverride)) return Desktop.SessionRootOverride;
                return Path.Combine(Application.persistentDataPath,"SuperspaceDemoSessions");
            }
        }
        public bool Guided {get;private set;}
        public bool Exhibition {get;private set;}
        public bool AccessiblePalette {get;private set;}
        public bool PerformanceOverlay {get;set;}
        public bool AutoAdvance {get;set;}
        public int Step {get;private set;}
        public int PathIndex {get;private set;}
        public bool PathRunning {get;private set;}
        public string LastFailure {get;private set;}="none";
        public string Notice {get;private set;}="A scripted scientific tour, not evidence of learning.";
        public JObject FrozenPerformance;
        public LabController Lab=>Instrument.Lab;
        LabSettings originalSettings,ownedSettings;
        LabState initialized;
        MeshRenderer physicalFocus,internalFocus;
        LineRenderer link;
        Material focusMaterial;
        float nextAdvance;
        string lastSession;
        public string LastSessionPath=>lastSession;
        void Awake()
        {
            Preferences=QuestPreferences.Load();Lifecycle=gameObject.AddComponent<QuestLifecycle>();Lifecycle.Demo=this;
            originalSettings=Lab.Settings;ownedSettings=Instantiate(originalSettings);Lab.Settings=Lab.View.Settings=ownedSettings;
            Instrument.Camera.clearFlags=CameraClearFlags.SolidColor;Instrument.Camera.backgroundColor=Settings.Background;
            focusMaterial=new Material(Lab.View.LineMaterial);focusMaterial.SetColor("_BaseColor",Color.white);
            MeshRenderer Marker(string name) {var go=new GameObject(name);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=Lab.View.PointMesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=focusMaterial;go.transform.localScale=Vector3.one*.035f;return r;}
            physicalFocus=Marker("Exact n physical focus");internalFocus=Marker("Exact n internal focus");
            link=new GameObject("Display-only exact-n correspondence").AddComponent<LineRenderer>();link.transform.SetParent(transform,false);link.sharedMaterial=focusMaterial;link.startWidth=link.endWidth=.004f;link.positionCount=2;
            SetPalette(Preferences.AlternativePalette);
            var coach=new GameObject("Quest eye-level guide",typeof(RectTransform));coach.transform.SetParent(transform,false);
            QuestUI=coach.AddComponent<QuestExperienceUI>();QuestUI.Demo=this;
            var comparison=new GameObject("Local before / now comparison");comparison.transform.SetParent(transform,false);
            Comparison=comparison.AddComponent<SelectionComparisonOverlay>();Comparison.Demo=this;
            var menu=new GameObject("Quest application menu",typeof(RectTransform));menu.transform.SetParent(transform,false);
            Menu=menu.AddComponent<QuestAppMenu>();Menu.Demo=this;
            gameObject.AddComponent<QuestViewTools>().Demo=this;
            if(Application.platform==RuntimePlatform.Android&&Debug.isDebugBuild) gameObject.AddComponent<QuestDeviceDiagnostics>().Demo=this;
            if(Application.platform==RuntimePlatform.WindowsPlayer) DesktopWorkspace.Install(this);
        }
        public void Guard(Action action)
        {
            try {action();}catch(Exception e) {LastFailure=e.GetType().Name+": "+e.Message;Notice="Refused; failure remains visible.";Debug.LogWarning("[XRSuperspace] demo refusal: "+LastFailure);}
            UI?.Refresh();
        }
        public void ReportFailure(Exception error)=>Guard(()=>throw error);
        public void SetPalette(bool alternative)
        {
            AccessiblePalette=alternative;ownedSettings.SelectedColor=alternative?Settings.AccessibleSelected:Settings.Selected;
            ownedSettings.BoundaryColor=alternative?Settings.AccessibleBoundary:Settings.Boundary;ownedSettings.RejectedColor=alternative?Settings.AccessibleRejected:Settings.Rejected;
            if(Preferences.HighContrast) ownedSettings.RejectedColor=new Color(.6f,.63f,.68f);
            if(Lab.State!=null) Lab.View.Rebuild(Lab.State);
        }
        void CanonicalReset()
        {
            if(Lab.State==null||Lab.State.Dataset.Metadata.BinarySha256!=InstrumentCapturePreset.Checksum)
                throw new InvalidOperationException("guided/capture presets require canonical_small_v1; Reset canonical first");
            Instrument.Preset(2);Lab.View.LayerSeparation=1;Lab.View.BoundaryBand=0;
            Lab.View.ShowCandidates=false;Lab.View.FadeSeconds=0;Preview.SetFallback(true);Lab.View.PointSize=1;
            Lab.Select(Instrument.Resolve(Settings.FocusId));Lab.State.Model.ClearJournal();Instrument.Reciprocal.SetIntensityThreshold(0);
        }
        public void StartGuide(bool automatic=false)
        {
            Menu?.ClearExample();
            Comparison.EndComparison();
            StopAllCoroutines();PathRunning=false;CanonicalReset();Guided=true;AutoAdvance=automatic;Step=0;PathIndex=0;ApplyReveal();
            Lab.PhasonHandle?.GetComponent<PhasonHandleControl>()?.CancelGrab();
            Notice="Deterministic tour baseline; event journal explicitly reset.";nextAdvance=Time.unscaledTime+GuideSeconds;
            Instrument.Camera.transform.SetPositionAndRotation(new Vector3(0,2,-6.1f),Quaternion.identity);Instrument.Camera.fieldOfView=42;Lab.GetComponent<DesktopInput>().SyncCamera();
        }
        public void FreeMode()
        {
            Comparison.EndComparison();
            StopAllCoroutines();PathRunning=false;Guided=false;AutoAdvance=false;Exhibition=false;
            Lab.View.PresentationPhysical=Lab.View.PresentationInternal=true;Lab.View.WindowLine.enabled=true;
            Instrument.Reciprocal.PresentationVisible=true;Lab.View.ShowEdges=true;Lab.View.Rebuild(Lab.State);ApplyPanels();
            Notice="Free exploration: exact n, phi, scientific subset, threshold, sessions and CPU validation.";
        }
        public void SetExhibition(bool value) {Exhibition=value;ApplyPanels();}
        void ApplyPanels()
        {
            Instrument.UI.gameObject.SetActive(!Guided&&!Exhibition);
            foreach(var control in Lab.GetComponentsInChildren<PreviewControls>(true)) control.gameObject.SetActive(!Guided&&!Exhibition);
            if(UI?.Controls!=null) UI.Controls.SetActive(!Exhibition&&!Capture.Busy);
            UI?.Refresh();
        }
        public bool Advance()
        {
            if(!Guided||Step>=9||PathRunning||Capture.Busy) return false;
            if(Step==7&&!Instrument.Reciprocal.Current) {Notice="Waiting for CURRENT diffraction; stale result is not presented as current.";return false;}
            Step++;ApplyReveal();nextAdvance=Time.unscaledTime+GuideSeconds;
            if(Step==6) StartCoroutine(PhasonPath());return true;
        }
        IEnumerator PhasonPath()
        {
            Comparison.BeginComparison();
            PathRunning=true;
            for(int i=1;i<=16;i++)
            {
                while(PlaybackBlocked) yield return null;
                PathIndex=i;var phi=DemoNarrative.PathSample(i);Lab.SetPhi(phi.X,phi.Y);Preview.Validate(true);
                for(float remaining=Settings.SampleSeconds;remaining>0;) {yield return null;if(!PlaybackBlocked) remaining-=Time.unscaledDeltaTime;}
            }
            Comparison.EndComparison();PathRunning=false;Notice="17 exact path samples complete; event IDs and diffraction use this same state.";
        }
        float GuideSeconds=>QuestUI!=null&&QuestUI.Active?Settings.QuestStageSeconds:Settings.StageSeconds;
        void ApplyReveal()
        {
            var view=Lab.View;view.PresentationPhysical=Step>=4;view.PresentationInternal=Step>=3;view.WindowLine.enabled=Step>=2;
            view.ShowEdges=Step>=5;Instrument.Reciprocal.PresentationVisible=Step>=8;
            view.Rebuild(Lab.State);ApplyPanels();UI?.Refresh();
        }
        public void ValidateCpu()
        {
            if(Lab.State==null) throw new InvalidOperationException("no dataset loaded");
            var model=Lab.State.Model;var cpu=Membership.Evaluate(model.Dataset,model.Phi);
            if(!cpu.SequenceEqual(model.States)) throw new InvalidOperationException("C# membership state disagrees with independent recomputation");
            Preview.Validate(true);var c=Preview.Comparison;
            var check=new InteractionModel(model.Dataset);check.Apply(model.Phi,model.Filter);
            if(!check.Active.SequenceEqual(model.Active)||!check.Edges.SequenceEqual(model.Edges)) throw new InvalidOperationException("CPU active subset/graph disagrees with independent recomputation");
            if(c!=null&&c.DisagreementIds.Length>0&&c.MaximumAuthoritativeDistanceMagnitude>model.Dataset.Metadata.PreviewEpsilon)
                throw new InvalidOperationException("GPU disagreement outside declared preview boundary band");
            if(!Capture.Busy) Notice="CPU membership validated; "+(c==null?"GPU comparison unavailable (CPU fallback).":c.DisagreementIds.Length+" GPU boundary-band disagreements disclosed.");
        }
        public void ResetCanonical()
        {
            if(Lab.Busy) throw new InvalidOperationException("dataset load is busy");
            if(Lab.State?.Dataset.Metadata.BinarySha256==InstrumentCapturePreset.Checksum) {StartGuide(false);return;}
            initialized=null;Lab.ResetFirstDataset();
        }
        public void ApplyPreset(DemoCapturePreset preset)
        {
            preset.Validate();StopAllCoroutines();PathRunning=false;CanonicalReset();Guided=false;AutoAdvance=false;SetExhibition(true);
            Lab.SetPhi(preset.PhiX,preset.PhiY);Preview.Validate(true);Lab.View.PresentationPhysical=preset.Physical;Lab.View.PresentationInternal=preset.Internal;
            Lab.View.WindowLine.enabled=preset.Internal;Lab.View.ShowEdges=preset.Physical;Instrument.Reciprocal.PresentationVisible=preset.Reciprocal;
            PerformanceOverlay=preset.Performance;Instrument.Camera.transform.SetPositionAndRotation(preset.CameraPosition,Quaternion.Euler(preset.CameraEuler));Instrument.Camera.fieldOfView=preset.Fov;
            Lab.GetComponent<DesktopInput>().Pivot=new Vector3(0,1.4f,1.3f);Lab.GetComponent<DesktopInput>().SyncCamera();Lab.View.Rebuild(Lab.State);
            Notice="Preset: "+preset.Title+" · finite canonical model; display separation only.";
            if(preset.Id=="linked_point") {Guided=true;Step=1;PathIndex=0;ApplyReveal();}
        }
        public string SessionJson()
        {
            if(PathRunning||Instrument.Playing) throw new InvalidOperationException("stop/finish deterministic playback before saving a session");
            var captured=Instrument.Capture();if(Desktop!=null&&Desktop.Active) DesktopWorkspace.NormalizeSessionPresentation(captured);
            return new JObject { ["schema"]="xr-demo-session/1",["instrument"]=JObject.Parse(InstrumentSession.Encode(captured)),
                ["journal"]=JArray.FromObject(Lab.State.Model.Journal),
                ["notice"]=Notice,
                ["guided"]=Guided,["step"]=Step,["path_index"]=PathIndex,["exhibition"]=Exhibition,["accessible_palette"]=AccessiblePalette,
                ["performance_overlay"]=PerformanceOverlay,["threshold"]=Instrument.Reciprocal.IntensityThreshold,["last_failure"]=LastFailure,
                ["physical_visible"]=Lab.View.PresentationPhysical,["internal_visible"]=Lab.View.PresentationInternal,
                ["window_visible"]=Lab.View.WindowLine.enabled,["reciprocal_visible"]=Instrument.Reciprocal.PresentationVisible }.ToString(Formatting.Indented);
        }
        public void LoadSessionJson(string json)
        {
            if(json==null||json.Length>16*1024*1024) throw new FormatException("demo session size invalid");
            var root=JObject.Parse(json);string[] required={"schema","instrument","journal","notice","guided","step","path_index","exhibition","accessible_palette","performance_overlay","threshold","last_failure","physical_visible","internal_visible","window_visible","reciprocal_visible"};
            if(root.Properties().Count()!=required.Length||required.Any(n=>root[n]==null)||(string)root["schema"]!="xr-demo-session/1") throw new FormatException("demo session fields/schema invalid");
            foreach(string n in new[]{"guided","exhibition","accessible_palette","performance_overlay","physical_visible","internal_visible","window_visible","reciprocal_visible"}) if(root[n].Type!=JTokenType.Boolean) throw new FormatException("demo bool field invalid");
            if(root["step"].Type!=JTokenType.Integer||root["path_index"].Type!=JTokenType.Integer||root["last_failure"].Type!=JTokenType.String) throw new FormatException("demo exact field types invalid");
            if(root["threshold"].Type!=JTokenType.Float&&root["threshold"].Type!=JTokenType.Integer) throw new FormatException("threshold must be numeric");
            if(root["notice"].Type!=JTokenType.String||((string)root["notice"]).Length>4096) throw new FormatException("caption invalid");
            int step=(int)root["step"],index=(int)root["path_index"];DemoNarrative.ValidateStep(step);
            double threshold=(double)root["threshold"];if(index<0||index>16||double.IsNaN(threshold)||double.IsInfinity(threshold)||threshold<0||threshold>1||((string)root["last_failure"]).Length>4096) throw new FormatException("demo state bounds invalid");
            var session=InstrumentSession.Decode(root["instrument"].ToString());
            if((bool)root["guided"])
            {
                if((bool)root["physical_visible"]!=(step>=4)||(bool)root["internal_visible"]!=(step>=3)||(bool)root["window_visible"]!=(step>=2)||(bool)root["reciprocal_visible"]!=(step>=8)) throw new FormatException("guided reveal does not match narrative step");
                var expected=DemoNarrative.PathSample(step<6?0:16);
                if(index!=(step<6?0:16)||session.Phi[0]!=expected.X||session.Phi[1]!=expected.Y||session.Filter.CropEnabled||session.Filter.SliceEnabled) throw new FormatException("guided model is not a completed deterministic step");
            }
            if(root["journal"].Type!=JTokenType.Array) throw new FormatException("journal must be an array");
            var frames=JsonConvert.DeserializeObject<ChangeFrame[]>(root["journal"].ToString(),new JsonSerializerSettings {MissingMemberHandling=MissingMemberHandling.Error});
            var replay=new InteractionModel(Lab.State.Dataset);replay.Apply(new Double2(session.Phi[0],session.Phi[1]),session.Filter);replay.RestoreJournal(frames);
            foreach(var frame in frames) if(frame.Phi[0]<Lab.PhiMin.X||frame.Phi[0]>Lab.PhiMax.X||frame.Phi[1]<Lab.PhiMin.Y||frame.Phi[1]>Lab.PhiMax.Y) throw new FormatException("journal phi outside exported domain");
            if(Lifecycle.Suspended) throw new InvalidOperationException("Resume the app before loading a session.");
            // Instrument validates all checksum/coverage/camera/tuple inputs before mutation.
            Instrument.ApplySession(session);Lab.State.Model.RestoreJournal(frames);StopAllCoroutines();PathRunning=false;AutoAdvance=false;
            Step=step;PathIndex=index;Guided=(bool)root["guided"];Exhibition=(bool)root["exhibition"];PerformanceOverlay=(bool)root["performance_overlay"];
            if((string)root["last_failure"]!="none") LastFailure=(string)root["last_failure"];
            // Quest comfort preferences are device-specific, not scientific session state.
            SetPalette(Desktop!=null&&Desktop.Active?Desktop.Preferences.AlternativePalette:QuestUI!=null&&QuestUI.Active?Preferences.AlternativePalette:(bool)root["accessible_palette"]);Instrument.Reciprocal.SetIntensityThreshold(threshold);
            Lab.View.PresentationPhysical=(bool)root["physical_visible"];Lab.View.PresentationInternal=(bool)root["internal_visible"];Lab.View.WindowLine.enabled=(bool)root["window_visible"];Instrument.Reciprocal.PresentationVisible=(bool)root["reciprocal_visible"];
            Lab.View.Rebuild(Lab.State);ApplyPanels();Notice=(string)root["notice"];
            Menu?.ClearExample();Comparison.EndComparison();savedStamp=WorkStamp;
        }
        public void SaveSession()
        {
            if(Lifecycle.Suspended||Lab.State==null||Lab.Busy||Capture.Busy) throw new InvalidOperationException("Wait for the app to be ready before saving.");
            string json=SessionJson();InstrumentSession.Decode(JObject.Parse(json)["instrument"].ToString());
            string path=LocalSessionStore.Save(SessionFolder,json);
            lastSession=path;savedStamp=WorkStamp;Notice="Session saved locally. Open Menu / Sessions to restore it.";
            Menu?.SetFeedback(Notice);QuestUI?.RefreshStory();
        }
        public void LoadSession(string path)
        {
            if(PathRunning||Instrument.Playing||Instrument.Recording||Capture.Busy||Menu.Screenshot.Busy||Lifecycle.Suspended)
                throw new InvalidOperationException("Finish playback or capture, then load the session.");
            string json=LocalSessionStore.Read(SessionFolder,path);
            LoadSessionJson(json);lastSession=path;LastFailure="none";QuestUI?.Recenter();
        }
        public void LoadLastSession()
        {
            if(lastSession==null) lastSession=LocalSessionStore.List(SessionFolder).FirstOrDefault();
            if(lastSession==null) throw new FileNotFoundException("no local demo session; Save first");
            LoadSession(lastSession);
        }
        public string ProvenanceText()
        {
            if(Lab.State==null) return DatasetLabels.Unvalidated+" · "+Lab.Status+"\nFAILED / last refusal: "+LastFailure;
            var m=Lab.State.Dataset.Metadata;var f=Lab.State.Model.Filter;
            return DatasetLabels.Canonical+" · schema "+RuntimeMetadata.SchemaVersion+" · "+Preview.Mode+" / "+Preview.Backend+
                "\nDataset SHA-256 "+m.BinarySha256+"\nAuthority SHA-256 "+m.AuthoritativeSha256+
                $"\nphi=({Lab.State.Phi.X:G17},{Lab.State.Phi.Y:G17}) · CPU epsilon {m.AuthorityEpsilon:G4} / GPU epsilon {m.PreviewEpsilon:G4}"+
                $" · crop {f.CropKind} {(f.CropEnabled?"ON":"OFF")} R={f.Radius:G5} |z|<={f.ZMax:G5} · slice {(f.SliceEnabled?"ON":"OFF")} z={f.SliceZ:G4} +/- {f.SliceHalfWidth:G4}"+
                "\nDiffraction "+Instrument.Reciprocal.Status+" · display threshold "+Instrument.Reciprocal.IntensityThreshold.ToString("G5",CultureInfo.InvariantCulture)+
                $"\nDisplay only: separated views; layer x{Lab.View.LayerSeparation:G3}; size x{Lab.View.PointSize:G3}. Selected / boundary / rejected; white = exact-n focus."+
                "\nFAILED / last refusal: "+LastFailure+" · free controls: "+Instrument.LastFailure+" · precision controls: "+string.Join("; ",Lab.GetComponentsInChildren<PreviewControls>(true).Select(c=>c.LastFailure))+" · "+Lab.Status;
        }
        public string StoryText()
        {
            if(Lab.State==null) return "Waiting for checksum-verified local canonical data.";
            string story=Guided?DemoNarrative.Titles[Step]+"\n"+DemoNarrative.Explanations[Step]:"FREE / EXHIBITION · "+Notice;
            var id=Lab.State.Selection;if(id.HasValue) {var n=id.Value;var data=Lab.State.Dataset;story+=$"\nn=[{n}] · r=({data.Project(n,0):G7},{data.Project(n,1):G7},{data.Project(n,2):G7}) · u=({data.Project(n,3):G7},{data.Project(n,4):G7})";}
            if(Guided&&Step==7) {var frames=Lab.State.Model.Journal;story+="\nEntered: "+string.Join(" | ",frames.SelectMany(f=>f.Entered).Distinct())+"\nExited: "+string.Join(" | ",frames.SelectMany(f=>f.Exited).Distinct());}
            return story;
        }
        void Update()
        {
            if(Lab.State!=null&&initialized!=Lab.State&&!Lab.Busy) {initialized=Lab.State;if(Settings.StartGuided&&Lab.State.Dataset.Metadata.BinarySha256==InstrumentCapturePreset.Checksum) Guard(()=>StartGuide(false));else Guard(FreeMode);savedStamp=WorkStamp;}
            var desktop=Lab.GetComponent<DesktopInput>();if(desktop!=null) desktop.enabled=Desktop!=null&&Desktop.Active?!Desktop.BlocksInput:!Guided&&!Capture.Busy&&!(Menu!=null&&Menu.InputBlocked);
            if(Lab.PhasonHandle!=null) Lab.PhasonHandle.gameObject.SetActive(!Guided||Step>=6);
            if(Instrument.LastFailure!="none"&&LastFailure=="none") LastFailure=Instrument.LastFailure;
            if(Instrument.Reciprocal.Status.StartsWith("ERROR")) LastFailure=Instrument.Reciprocal.Status;
            if(Guided&&AutoAdvance&&!PathRunning&&!Capture.Busy&&!(Menu!=null&&Menu.InputBlocked)&&Time.unscaledTime>=nextAdvance) Guard(()=>Advance());
            if(Lab.Status.Contains("ERROR")) LastFailure=Lab.Status;
            var keyboard=Keyboard.current;
            if(Desktop==null&&keyboard!=null&&!Capture.Busy&&!(Menu!=null&&Menu.InputBlocked)&&!QuestUiTheme.HasTextInputFocus())
            {if(keyboard.gKey.wasPressedThisFrame) Guard(()=>StartGuide(false));if(keyboard.fKey.wasPressedThisFrame) Guard(FreeMode);if(keyboard.nKey.wasPressedThisFrame) Guard(()=>Advance());}
            if(Lab.State!=null&&Lab.State.SelectedRow>=0)
            {
                bool focus=Guided&&Step<4;physicalFocus.gameObject.SetActive(focus);internalFocus.gameObject.SetActive(focus&&Step>=1);link.gameObject.SetActive(focus&&Step>=1);
                int row=Lab.State.SelectedRow;physicalFocus.transform.position=Lab.View.PointPosition(row,false);internalFocus.transform.position=Lab.View.PointPosition(row,true);
                link.SetPosition(0,physicalFocus.transform.position);link.SetPosition(1,internalFocus.transform.position);
            }
            else {physicalFocus.gameObject.SetActive(false);internalFocus.gameObject.SetActive(false);link.gameObject.SetActive(false);}
        }
        void OnDestroy() {if(ownedSettings!=null) Destroy(ownedSettings);if(focusMaterial!=null) Destroy(focusMaterial);if(Lab!=null&&originalSettings!=null) Lab.Settings=Lab.View.Settings=originalSettings;}
    }
}
