using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRSuperspace
{
    // Local development-player diagnostics, bounded to one record per second.
    // Loop observations are NOT compositor FPS or input-to-photon latency.
    public sealed class QuestDeviceDiagnostics : MonoBehaviour
    {
        public ScientificDemo Demo;
        double next,last;
        int frames;
        double elapsed,maxFrame;
        XRRayInteractor[] rays;
        HandPointer[] hands;
        bool leftTrigger,rightTrigger;
        int leftEdges,rightEdges;
        // Unity's Android logger truncates long messages. Keep every chunk below
        // its limit and identify parts so the host can reconstruct exact JSON.
        public static string[] LogParts(string json,int frame)
        {
            const int size=800;
            int count=(json.Length+size-1)/size;
            var parts=new string[count];
            for(int i=0;i<count;i++) parts[i]="[AperiTraceDiagPart] "+frame+" "+(i+1)+"/"+count+" "+json.Substring(i*size,System.Math.Min(size,json.Length-i*size));
            return parts;
        }
        void Start() {var endpoints=Demo.Lab.GetComponent<LabXrMode>().XrControllers;rays=endpoints.GetComponentsInChildren<XRRayInteractor>(true);hands=endpoints.GetComponentsInChildren<HandPointer>(true);last=Time.realtimeSinceStartupAsDouble;next=last+1;}
        void LateUpdate()
        {
            if(Application.platform!=RuntimePlatform.Android||!Debug.isDebugBuild||Demo==null) return;
            double now=Time.realtimeSinceStartupAsDouble,delta=now-last;last=now;
            if(Demo.Lifecycle.Suspended||!XRSettings.isDeviceActive) {frames=0;elapsed=maxFrame=0;next=now+1;return;}
            frames++;elapsed+=delta;maxFrame=System.Math.Max(maxFrame,delta*1000);
            bool a=false,b=false,leftTracked=false,rightTracked=false;
            foreach(var ray in rays)
            {
                if(ray==null||ray.GetComponent<ControllerAimRay>() is not ControllerAimRay aim) continue;
                bool pressed=ray.enabled&&ray.selectInput.ReadIsPerformed();
                if(aim.LeftHand) {a|=pressed;leftTracked|=aim.Tracked;} else {b|=pressed;rightTracked|=aim.Tracked;}
            }
            if(a&&!leftTrigger) leftEdges++;if(b&&!rightTrigger) rightEdges++;leftTrigger=a;rightTrigger=b;
            if(now<next||Demo.Lab.State==null) return;next=now+1;
            var model=Demo.Lab.State.Model;var reciprocal=Demo.Instrument.Reciprocal;
            var input=new JArray();
            foreach(var ray in rays)
            {
                if(ray==null) continue;
                string hit="none";
                if(ray.isActiveAndEnabled&&ray.TryGetCurrentUIRaycastResult(out var ui)&&ui.gameObject!=null) hit=ui.gameObject.name;
                var aim=ray.GetComponent<ControllerAimRay>();
                string role=aim!=null?(aim.LeftHand?"left-controller":"right-controller"):ray.transform.parent.name+"/hand";
                input.Add(new JObject { ["ray"]=role,["enabled"]=ray.isActiveAndEnabled,["select_active"]=ray.isSelectActive,
                    ["select_pressed"]=ray.isActiveAndEnabled&&ray.selectInput.ReadIsPerformed(),["ui_pressed"]=ray.isActiveAndEnabled&&ray.uiPressInput.ReadIsPerformed(),["ui_hit"]=hit });
            }
            bool measured=reciprocal.Current&&reciprocal.MeasuredResultRevision==model.Revision;
            var handRecords=new JArray();
            foreach(var hand in hands) if(hand!=null) handRecords.Add(new JObject {
                ["side"]=hand.Left?"left":"right",["tracked"]=hand.Available,["visible"]=hand.HandVisible,
                ["native_aim"]=hand.UsingNativeAim,["pinching"]=hand.Pinching,["press_edges"]=hand.PressEdges,["release_edges"]=hand.ReleaseEdges });
            var record=new JObject {
                ["schema"]="aperitrace-device-diag/2",["utc"]=System.DateTimeOffset.UtcNow.ToString("O"),
                ["frame"]=Time.frameCount,["loop_samples"]=frames,["loop_mean_ms"]=frames==0?0:elapsed*1000/frames,["loop_max_ms"]=maxFrame,
                ["menu"]=Demo.Menu.IsOpen,["guided"]=Demo.Guided,["held"]=Demo.Lab.PhasonHandle.GetComponent<PhasonHandleControl>().Held,
                ["phi"]=new JArray(model.Phi.X,model.Phi.Y),["selected"]=model.SelectedCount,["revision"]=model.Revision,
                ["membership_cpu_ms"]=model.LastMembershipMilliseconds,["diffraction_current"]=reciprocal.Current,
                ["completed_revision"]=reciprocal.MeasuredResultRevision,
                ["diffraction_cpu_ms"]=measured?JToken.FromObject(reciprocal.LastCalculationCpuMilliseconds):JValue.CreateNull(),
                ["settle_to_current_ms"]=measured?JToken.FromObject(reciprocal.LastUpdateLatencyMilliseconds):JValue.CreateNull(),
                ["left_tracked"]=leftTracked,["right_tracked"]=rightTracked,["input_source"]="Input System aim tracking and XRI select readers",
                ["trigger_pressed_edges"]=new JArray(leftEdges,rightEdges),["rays"]=input,["hands"]=handRecords
            };
            var json=JsonConvert.SerializeObject(record,new JsonSerializerSettings {StringEscapeHandling=StringEscapeHandling.EscapeNonAscii});
            foreach(var part in LogParts(json,Time.frameCount)) Debug.Log(part);
            frames=0;elapsed=maxFrame=0;
        }
    }
}
