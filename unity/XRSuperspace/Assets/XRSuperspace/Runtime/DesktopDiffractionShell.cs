using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    public sealed partial class DesktopShell
    {
        RectTransform inputNotice;
        Text pauseReason;
        readonly List<(Text label,DiffractionColorbar bar)> diffractionLegends=new();
        int palettePage;
        DiffractionDisplay Display=>Workspace.Preferences.Diffraction;
        void BuildInputNotice()
        {
            inputNotice=Panel("input-pause-notice",DiagramArea,DesktopTheme.Panel);
            inputNotice.anchorMin=inputNotice.anchorMax=inputNotice.pivot=new Vector2(.5f,.5f);
            inputNotice.sizeDelta=new Vector2(520,Workspace.Preferences.TextSize==2?220:174);
            pauseReason=TextAt("pause-reason",inputNotice,"",20,16,480,90,18);
            ActionAt("resume-input",inputNotice,"Resume",20,inputNotice.sizeDelta.y-64,480,48,()=>Workspace.SetPaused(false),true);
            inputNotice.gameObject.SetActive(Workspace.Paused);
        }
        void RefreshInputNotice()
        {
            if(inputNotice==null) return;
            inputNotice.gameObject.SetActive(Workspace.Paused);
            pauseReason.text=Workspace.ManipulationNotice+"\nYour scientific state is preserved.";
        }
        void BuildDiffractionLegend(Transform body,float width,bool controls=true)
        {
            var instrument=Demo.Instrument.Reciprocal;
            var node=DesktopTheme.Node("diffraction-colorbar",body);node.gameObject.AddComponent<LayoutElement>().preferredHeight=18;
            var bar=node.gameObject.AddComponent<DiffractionColorbar>();bar.Settings=Display.Copy();bar.raycastTarget=false;
            var text=AddParagraph(body,"diffraction-legend","",16,0,DesktopTheme.Muted,width);
            diffractionLegends.Add((text,bar));RefreshDiffractionLegend();
            if(controls) AddButton(body,"inspector-colors","Color & filters...",()=>ShowModal("Diffraction"));
        }
        void RefreshDiffractionLegend()
        {
            diffractionLegends.RemoveAll(pair=>pair.label==null||pair.bar==null);
            var instrument=Demo.Instrument.Reciprocal;var range=instrument.ColorRange;
            string value=$"I / N squared   {range.min:G5} to {range.max:G5}\n{Display.Palette} / {Display.Scale}{(Display.Invert?" / reversed":"")}\n{(instrument.Current?instrument.VisibleCount+" of "+instrument.Peaks.Length+" shown":"Updating / stale display")}\nSelected peak is white. Size uses square-root intensity.";
            foreach(var pair in diffractionLegends)
            {
                DesktopSemanticText.Apply(pair.label,value,Workspace.Preferences.HighContrast,DesktopTheme.Muted);
                pair.label.GetComponent<LayoutElement>().preferredHeight=DesktopTheme.Measure(pair.label.text,pair.label.fontSize,pair.label.rectTransform.rect.width>0?pair.label.rectTransform.rect.width:300,true);
            }
        }
        bool BuildDiffractionModal(string screen,float width)
        {
            void Copy(string id,string text,int size=18)=>AddParagraph(modalBody,id,text,size,0,DesktopTheme.Muted,width);
            void Button(string id,string text,Action action,bool primary=false)=>AddButton(modalBody,id,text,action,primary);
            void Apply(DiffractionDisplay settings,string destination="Diffraction") {Workspace.ApplyDiffraction(settings);ShowModal(destination);}
            string N(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
            if(screen=="Diffraction")
            {
                Copy("display-science","Color and visibility only. Membership, all 381 numerical samples, and CSV exports stay unchanged.");
                BuildDiffractionLegend(modalBody,width,false);
                Button("choose-colormap","Colormap: "+Display.Palette+"...",()=> {palettePage=Math.Max(0,Array.IndexOf(DiffractionColors.Names,Display.Palette)/5);ShowModal("Colormaps");},true);
                Button("choose-range","Color range & scale...",()=>ShowModal("DiffractionRange"));
                Button("choose-filters","Intensity & reciprocal-radius filters...",()=>ShowModal("DiffractionFilters"));
                Button("invert-colormap",Display.Invert?"Direction: reversed":"Direction: normal",()=> {var s=Display.Copy();s.Invert=!s.Invert;Apply(s);});
                Button("reset-diffraction","Restore display defaults",()=>Apply(new DiffractionDisplay()));
                Copy("display-legend-note","Values outside the color range saturate at its endpoints; they are not hidden. Only filters hide peaks. Zero maps to the low endpoint in Log10. Amber means Updating, not current intensity. Filters also affect clicking peaks. Settings are local display preferences, not scientific session parameters.",16);
            }
            else if(screen=="Colormaps")
            {
                Copy("palette-guidance","Viridis and Cividis are recommended for ordered intensity. Diverging and cyclic maps are available for visual exploration, not signed intensity or phase data.",17);
                for(int i=palettePage*5;i<Math.Min(DiffractionColors.Names.Length,palettePage*5+5);i++)
                {
                    string name=DiffractionColors.Names[i];var row=Horizontal(modalBody,"palette-row-"+i,ButtonHeight);
                    var swatch=DesktopTheme.Node("palette-swatch-"+i,row);var element=swatch.gameObject.AddComponent<LayoutElement>();element.preferredWidth=140;element.flexibleWidth=0;
                    var bar=swatch.gameObject.AddComponent<DiffractionColorbar>();bar.Settings=Display.Copy();bar.Settings.Palette=name;bar.Settings.Invert=false;bar.raycastTarget=false;
                    AddButton(row,"colormap-"+i,name+(name==Display.Palette?" / selected":""),()=> {if(name=="Custom") ShowModal("CustomColors");else {var s=Display.Copy();s.Palette=name;Apply(s);}},name==Display.Palette);
                }
                var pages=Horizontal(modalBody,"palette-pages",ButtonHeight);
                var prev=AddButton(pages,"palette-previous","Previous",()=> {palettePage=Math.Max(0,palettePage-1);RefreshModal();});prev.interactable=palettePage>0;
                var next=AddButton(pages,"palette-next","Next",()=> {palettePage=Math.Min(3,palettePage+1);RefreshModal();});next.interactable=palettePage<3;
                Copy("palette-page","Page "+(palettePage+1)+" of 4 / 18 palettes + custom",16);
            }
            else if(screen=="DiffractionRange")
            {
                Copy("range-explain","Normalized intensity I / N squared. A manual color range saturates, but does not discard, values outside it. Auto range follows visible peaks. If empty or constant, it uses the manual range.",17);
                var draft=Display.Copy();
                var row=Horizontal(modalBody,"scale-options",ButtonHeight);
                foreach(string scale in new[]{"linear","sqrt","log"})
                {
                    string selected=scale;
                    AddButton(row,"scale-"+scale,scale=="log"?"Log10":scale=="sqrt"?"Square root":"Linear",()=> {var s=Display.Copy();s.Scale=selected;if(s.Scale=="log"&&s.Minimum<=0) s.Minimum=1e-6;Apply(s,"DiffractionRange");},Display.Scale==scale);
                }
                Copy("log-zero","Log10 requires a positive minimum. Selecting it starts at 1e-6 if the current minimum is zero. Zero is colored at the low endpoint; it is not replaced in the data.",16);
                var min=NumberField("color-min","Color minimum",N(draft.Minimum),width);
                var max=NumberField("color-max","Color maximum",N(draft.Maximum),width);
                Button("apply-color-range","Apply manual range",()=> {draft.Minimum=LinkedInstrument.Number(min.text);draft.Maximum=LinkedInstrument.Number(max.text);draft.AutoRange=false;Apply(draft);},true);
                Button("auto-color-range",Display.AutoRange?"Auto range: on / switch to manual":"Auto range: off / enable",()=> {var s=Display.Copy();s.AutoRange=!s.AutoRange;Apply(s);});
            }
            else if(screen=="DiffractionFilters")
            {
                Copy("filter-explain","Hide displayed peaks without removing their calculated values. Bounds are inclusive. |q| is in model reciprocal units, not measured inverse angstroms. Existing scientific display threshold still applies.",17);
                var draft=Display.Copy();
                var imin=NumberField("filter-intensity-min","Show intensity from",N(draft.VisibleMinimum),width);
                var imax=NumberField("filter-intensity-max","Show intensity to",N(draft.VisibleMaximum),width);
                var qmin=NumberField("filter-q-min","Show |q| from",N(draft.QMinimum),width);
                var qmax=NumberField("filter-q-max","Show |q| to",N(draft.QMaximum),width);
                Button("apply-peak-filters","Apply filters",()=> {draft.VisibleMinimum=LinkedInstrument.Number(imin.text);draft.VisibleMaximum=LinkedInstrument.Number(imax.text);draft.QMinimum=LinkedInstrument.Number(qmin.text);draft.QMaximum=LinkedInstrument.Number(qmax.text);Apply(draft);},true);
                Button("hide-center",Display.HideCenter?"Central q = 0 peak: hidden":"Central q = 0 peak: shown",()=> {var s=Display.Copy();s.HideCenter=!s.HideCenter;Apply(s,"DiffractionFilters");});
                Button("clear-peak-filters","Show all peaks",()=> {var s=Display.Copy();s.VisibleMinimum=s.QMinimum=0;s.VisibleMaximum=1;s.QMaximum=100;s.HideCenter=false;Apply(s);});
                Copy("filter-empty","A valid filter may show no peaks. Return here and choose Show all peaks to recover. Exports always contain the full sampling.",16);
            }
            else if(screen=="CustomColors")
            {
                Copy("custom-explain","Three sRGB colors, interpolated at the low, middle and high positions. Enter #RRGGBB. This is an exploration palette; no perceptual-uniformity guarantee.",17);
                var low=NumberField("custom-low","Low color",Display.Low,width);
                var mid=NumberField("custom-middle","Middle color",Display.Middle,width);
                var high=NumberField("custom-high","High color",Display.High,width);
                Button("apply-custom-colors","Apply custom gradient",()=> {var s=Display.Copy();s.Palette="Custom";s.Low=low.text;s.Middle=mid.text;s.High=high.text;Apply(s);},true);
            }
            else return false;
            return true;
        }
    }
}
