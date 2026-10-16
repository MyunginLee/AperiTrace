using System;
using System.IO;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace XRSuperspace
{
    public static class DesktopFrameCapture
    {
        // Render real application content, not a reconstructed illustration.
        public static string Save(DesktopWorkspace workspace,string folder,string name)
        {
            if(string.IsNullOrWhiteSpace(name)||Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0) throw new ArgumentException("Invalid screenshot filename.");
            folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);string path=Path.Combine(folder,name+".png"),jsonPath=Path.Combine(folder,name+".json");
            if(File.Exists(path)||File.Exists(jsonPath)) throw new IOException("Preserve existing screenshot.");
            var demo=workspace.Demo;var shell=workspace.Shell;int width=Mathf.Clamp(Screen.width,640,4096),height=Mathf.Clamp(Screen.height,480,4096);
            if((long)width*height>9000000) throw new InvalidOperationException("Screenshot exceeds the size budget.");
            string key=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config);int revision=demo.Lab.State.Model.Revision;
            var state=demo.Capture.State(width,height);var target=new RenderTexture(width,height,24);target.Create();
            var uiTarget=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);uiTarget.Create();
            var combined=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);combined.Create();
            var shader=Resources.Load<Shader>("DesktopCaptureComposite");if(shader==null) throw new InvalidOperationException("Desktop compositor shader is unavailable.");
            var composite=new Material(shader);
            var title=shell.GetComponentsInChildren<UnityEngine.UI.Text>().First(t=>t.name==(shell.ModalOpen?"modal-title":"brand")&&t.gameObject.activeInHierarchy);
            var titleCorners=new Vector3[4];title.rectTransform.GetWorldCorners(titleCorners);
            var titleMin=RectTransformUtility.WorldToScreenPoint(null,titleCorners[0]);var titleMax=RectTransformUtility.WorldToScreenPoint(null,titleCorners[2]);
            var previous=RenderTexture.active;var canvas=shell.Canvas;var mode=canvas.renderMode;var originalCamera=canvas.worldCamera;float distance=canvas.planeDistance;
            var modelGo=new GameObject("Temporary desktop model capture");var model=modelGo.AddComponent<Camera>();model.CopyFrom(demo.Instrument.Camera);model.enabled=false;
            model.transform.SetPositionAndRotation(demo.Instrument.Camera.transform.position,demo.Instrument.Camera.transform.rotation);model.cullingMask&=~(1<<5);model.targetTexture=target;model.aspect=demo.Instrument.Camera.aspect;
            var modelData=modelGo.AddComponent<UniversalAdditionalCameraData>();modelData.allowXRRendering=false;
            // Map the real scientific projection into its reserved viewport.
            // UI is rendered to a separate transparent target, then composed.
            var rect=demo.Instrument.Camera.rect;var mapping=Matrix4x4.identity;
            mapping.m00=rect.width;mapping.m03=2*rect.x+rect.width-1;mapping.m11=rect.height;mapping.m13=2*rect.y+rect.height-1;
            model.rect=new Rect(0,0,1,1);model.projectionMatrix=mapping*demo.Instrument.Camera.projectionMatrix;
            var uiGo=new GameObject("Temporary desktop UI capture");var ui=uiGo.AddComponent<Camera>();ui.enabled=false;ui.orthographic=true;ui.nearClipPlane=.01f;ui.farClipPlane=100;
            ui.cullingMask=1<<5;ui.clearFlags=CameraClearFlags.SolidColor;ui.backgroundColor=Color.clear;ui.targetTexture=uiTarget;
            var uiData=uiGo.AddComponent<UniversalAdditionalCameraData>();uiData.allowXRRendering=false;uiData.renderPostProcessing=false;
            Texture2D pixels=null;
            try
            {
                RenderTexture.active=target;GL.Clear(true,true,DesktopTheme.Background);
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=ui;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();
                demo.Lab.View.DrawForCamera(model);demo.Instrument.Reciprocal.DrawForCamera(model);
                model.Render();ui.Render();composite.SetTexture("_SceneTex",target);Graphics.Blit(uiTarget,combined,composite);
                RenderTexture.active=combined;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                if(revision!=demo.Lab.State.Model.Revision||key!=demo.Lab.State.Model.Key(demo.Instrument.Reciprocal.Config)||!demo.Instrument.Reciprocal.Current) throw new InvalidOperationException("Scientific state changed; capture refused.");
                float titleProbe=0;
                for(int x=Mathf.Max(0,(int)titleMin.x);x<Mathf.Min(width,(int)titleMax.x);x++) for(int y=Mathf.Max(0,(int)titleMin.y);y<Mathf.Min(height,(int)titleMax.y);y++)
                    titleProbe=Mathf.Max(titleProbe,pixels.GetPixel(x,y).maxColorComponent);
                if(!shell.ModalOpen&&titleProbe<.7f) throw new InvalidOperationException("Desktop title is missing from the composed image; capture refused.");
                float probe=-1;
                if(!shell.ModalOpen&&(workspace.FocusView==0||workspace.FocusView==3)&&demo.Instrument.Reciprocal.PresentationVisible&&demo.Lab.State.SelectedCount>0)
                {
                    int peak=Array.FindIndex(demo.Instrument.Reciprocal.Peaks,p=>p.Q.X==0&&p.Q.Y==0&&p.Q.Z==0);
                    if(peak>=0)
                    {
                        var center=demo.Instrument.Camera.WorldToScreenPoint(demo.Instrument.Reciprocal.PeakPosition(peak));probe=0;
                        // Navigation may deliberately put q=0 outside the view.
                        // Do not clamp it to an unrelated border/UI pixel or refuse
                        // an otherwise valid screenshot of that view.
                        // White-peak probe is only meaningful for an unfiltered,
                        // bright endpoint. Dark/inverted/custom palettes legitimately
                        // have no white peak. Preserve the baseline probe unchanged.
                        bool visible=demo.Instrument.Reciprocal.VisiblePeak(peak)&&demo.Instrument.Reciprocal.PeakColor(peak).maxColorComponent>=.9f&&demo.Instrument.Camera.pixelRect.Contains(new Vector2(center.x,center.y))&&center.z>=demo.Instrument.Camera.nearClipPlane&&center.z<=demo.Instrument.Camera.farClipPlane;
                        if(!visible) probe=-1;
                        for(int x=-5;visible&&x<=5;x++) for(int y=-5;y<=5;y++)
                        {
                            var color=pixels.GetPixel(Mathf.Clamp((int)center.x+x,0,width-1),Mathf.Clamp((int)center.y+y,0,height-1));probe=Mathf.Max(probe,color.maxColorComponent);
                        }
                        if(visible&&probe<.70f) throw new InvalidOperationException("Diffraction peak is missing from the composed image; capture refused.");
                    }
                }
                byte[] png=pixels.EncodeToPNG();string stateJson=state.ToString(Formatting.None);
                var sidecar=new JObject { ["schema"]="xr-demo-capture/1",["name"]=name,["captured_utc"]=DateTimeOffset.UtcNow.ToString("O"),
                    ["image"]=name+".png",["image_sha256"]=Qc5dParser.Sha256(png),["state_json"]=stateJson,["state_sha256"]=DemoCapture.Hash(stateJson),["state"]=state,
                    ["capture_method"]="Actual Unity URP model-camera and desktop-Canvas composition; not a reconstructed illustration",
                    ["desktop_ui"]=new JObject { ["modal"]=shell.Modal,["focus_view"]=workspace.FocusView,["text_size"]=workspace.Preferences.TextSize,["high_contrast"]=workspace.Preferences.HighContrast,
                        ["reduced_motion"]=workspace.Preferences.ReducedMotion,["paused"]=workspace.Paused,["platform"]=Application.platform.ToString(),["diffraction_peak_pixel_max"]=probe,["title_pixel_max"]=titleProbe,
                        ["camera_rect"]=JArray.FromObject(new[]{rect.x,rect.y,rect.width,rect.height}),["orthographic_size"]=demo.Instrument.Camera.orthographicSize,
                        ["grid_visible"]=workspace.GridVisible,["zoom"]=workspace.Zoom,["view_unit_scale"]=workspace.FocusView==0?JValue.CreateNull():new JValue(workspace.ViewUnitScale),
                        ["grid_details"]=new JArray(shell.Grid.Regions.Where(r=>r.Visible).Select(r=>new JObject {
                            ["view"]=r.View,["units"]=r.Units,["unit_scale"]=r.UnitScale,["pixels_per_unit"]=r.PixelsPerUnit,
                            ["minor"]=r.Minor,["major"]=r.Major,["old_minor"]=r.OldMinor,["blend"]=r.Blend,["projected"]=r.Projected,
                            ["pane_pixels"]=JArray.FromObject(new[]{r.PixelRect.x,r.PixelRect.y,r.PixelRect.width,r.PixelRect.height}),
                            ["plot_pixels"]=JArray.FromObject(new[]{r.PlotRect.x,r.PlotRect.y,r.PlotRect.width,r.PlotRect.height}),
                            ["clipped_segments"]=JArray.FromObject(r.Segments) })),
                        ["canvas_scale_factor"]=shell.Canvas.scaleFactor,
                        ["diffraction_display"]=JObject.FromObject(workspace.Preferences.Diffraction),["visible_peak_count"]=demo.Instrument.Reciprocal.VisibleCount,
                        ["color_range"]=JArray.FromObject(new[]{demo.Instrument.Reciprocal.ColorRange.min,demo.Instrument.Reciprocal.ColorRange.max}),
                        ["q0_probe_policy"]="Brightness probe applies only to an in-view, unfiltered, bright q=0 endpoint; -1 means not applicable.",
                        ["session_layout"]="Canonical cross-platform pose; actual desktop projection is recorded separately here.",
                        ["camera_position"]=JArray.FromObject(InstrumentSession.V(demo.Instrument.Camera.transform.position)),
                        ["camera_rotation"]=JArray.FromObject(InstrumentSession.V(demo.Instrument.Camera.transform.eulerAngles)),
                        ["physical_origin"]=JArray.FromObject(InstrumentSession.V(demo.Lab.View.PhysicalOrigin.position)),
                        ["internal_origin"]=JArray.FromObject(InstrumentSession.V(demo.Lab.View.InternalOrigin.position)),
                        ["reciprocal_origin"]=JArray.FromObject(InstrumentSession.V(demo.Instrument.Reciprocal.Origin.position)),
                        ["view_center"]=workspace.FocusView==0?JValue.CreateNull():JArray.FromObject(new[]{workspace.ViewCenter.x,workspace.ViewCenter.y}),
                        ["view_span"]=workspace.FocusView==0?JValue.CreateNull():JArray.FromObject(new[]{workspace.ViewSpan.x,workspace.ViewSpan.y}),
                        ["theme_primary_srgb"]=new JArray(((Color32)DesktopTheme.Accent).r,((Color32)DesktopTheme.Accent).g,((Color32)DesktopTheme.Accent).b) },
                    ["pixel_reproducibility"]="Not guaranteed across drivers; linked scientific state is independently checked." };
                using(var file=new FileStream(path,FileMode.CreateNew)) file.Write(png,0,png.Length);
                using(var file=new FileStream(jsonPath,FileMode.CreateNew)) using(var writer=new StreamWriter(file,new UTF8Encoding(false))) writer.Write(sidecar.ToString());
                return path;
            }
            finally
            {
                canvas.renderMode=mode;canvas.worldCamera=originalCamera;canvas.planeDistance=distance;Canvas.ForceUpdateCanvases();RenderTexture.active=previous;
                model.targetTexture=ui.targetTexture=null;if(pixels!=null) UnityEngine.Object.Destroy(pixels);target.Release();uiTarget.Release();combined.Release();
                UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(uiTarget);UnityEngine.Object.Destroy(combined);UnityEngine.Object.Destroy(composite);UnityEngine.Object.Destroy(modelGo);UnityEngine.Object.Destroy(uiGo);
            }
        }
    }
}
