using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace XRSuperspace
{
    public sealed class PreviewProfiler : MonoBehaviour
    {
        public GpuPreview Preview;
        readonly float[] samples=new float[16384];
        int cursor,count;
        ProfilerRecorder drawCalls,memory,gc;
        void OnEnable() {drawCalls=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count");memory=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"Total Reserved Memory");gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");}
        void OnDisable() {drawCalls.Dispose();memory.Dispose();gc.Dispose();}
        public void ResetSamples() {cursor=count=0;}
        void LateUpdate() {samples[cursor]=Time.unscaledDeltaTime*1000;cursor=(cursor+1)%samples.Length;count=Math.Min(count+1,samples.Length);}
        public Dictionary<string,object> Snapshot()
        {
            var ordered=new float[count];Array.Copy(samples,ordered,count);Array.Sort(ordered);float Percentile(double p)=>count==0?0:ordered[(int)Math.Ceiling((count-1)*p)];var state=Preview.Lab.State;double elapsed=0;foreach(float ms in ordered) elapsed+=ms/1000;
            return new Dictionary<string,object> {
                ["environment"]=Application.isEditor?"Unity Editor PlayMode":"player",
                ["unity"]=Application.unityVersion,["platform"]=Application.platform.ToString(),["development_build"]=Debug.isDebugBuild,["device"]=SystemInfo.deviceModel,["os"]=SystemInfo.operatingSystem,["cpu"]=SystemInfo.processorType,["gpu"]=SystemInfo.graphicsDeviceName,["graphics_api"]=SystemInfo.graphicsDeviceType.ToString(),
                ["game_view_resolution"]=new[]{Screen.width,Screen.height},["desktop_camera_target_resolution"]=new[]{Preview.Lab.GetComponent<LabXrMode>().DesktopCamera.pixelWidth,Preview.Lab.GetComponent<LabXrMode>().DesktopCamera.pixelHeight},["backend"]=Preview.Backend,["precision"]=Preview.Mode.ToString(),["tier"]=Preview.Tier.Title,["target_fps_not_verified"]=Preview.Tier.FrameTarget,
                ["candidates"]=state?.Dataset.Candidates.Count,["scientific_selected"]=state?.SelectedCount,["boundary"]=state?.BoundaryCount,["display_stride"]=Preview.DrawStride,["visible_candidate_upper_bound"]=Preview.VisibleCandidateBudget,["buffer_bytes_estimate"]=Preview.BufferBytes,
                ["cpu_membership_ms"]=state?.Model.LastMembershipMilliseconds,["validation_cpu_membership_ms"]=Preview.Comparison?.CpuMembershipMilliseconds,["dispatch_cpu_submission_ms_not_gpu_duration"]=Preview.DispatchSubmissionMilliseconds,["gpu_dispatch_duration_ms"]="unavailable: no reliable GPU timestamp measurement",
                ["draw_calls"]=drawCalls.Valid?drawCalls.LastValue:(object)"unavailable",["reserved_memory_bytes"]=memory.Valid?memory.LastValue:(object)"unavailable",["whole_frame_gc_bytes"]=gc.Valid?gc.LastValue:(object)"unavailable",["gpu_draw_owned_max_gc_bytes"]=Preview.OwnedDrawAllocatedBytes,
                ["gpu_point_draw_calls_per_camera"]=Preview.UsingGpu?(Preview.Tier.InternalView?2:1):0,["frames"]=count,["sample_window_seconds"]=elapsed,["frame_ms_p50"]=Percentile(.5),["frame_ms_p95"]=Percentile(.95),["frame_ms_p99"]=Percentile(.99),["comparison"]=Preview.Comparison,["quest_device_test"]="NOT RUN — APK/device validation deferred",["scope"]="Editor loop observation; no GPU timing/fence, not Quest throughput or sustained player performance"
            };
        }
    }
}
