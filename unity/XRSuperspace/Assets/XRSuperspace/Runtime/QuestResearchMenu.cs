using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    // The same scientific readers and diffraction display settings as desktop.
    // A controller-operated keypad avoids requiring a physical/native keyboard.
    public sealed partial class QuestAppMenu
    {
        RectTransform fixedFooter;
        Text inspectorLive;
        float nextInspector;
        int inspectorPage,palettePage;
        string editKey,editValue,editReturn;
        readonly Dictionary<string,string> drafts=new();
        DiffractionDisplay Display=>Demo.Preferences.Diffraction;
        QuestViewTools ViewTools=>Demo.GetComponent<QuestViewTools>();
        string Number(double value)=>value.ToString("G17",CultureInfo.InvariantCulture);
        void Paragraph(string name,string plain,int size=26)
        {
            string rich=DesktopSemanticText.Present(plain,Demo.Preferences.HighContrast);
            float height=Height(rich,size,944);var label=Label(name,content,28,cursor,944,height,size);
            label.supportRichText=true;label.text=rich;cursor+=height+16;
        }
        void BeginDisplayPage(string screen)
        {
            drafts.Clear();ShowScreen(screen);
        }
        void Numeric(string key,string title,string value)
        {
            if(!drafts.ContainsKey(key)) drafts[key]=value;
            Row(title+": "+drafts[key]+"  / Edit",()=> {editKey=key;editValue=drafts[key];editReturn=Screen;ShowScreen("Keypad");});
        }
        double Draft(string key)=>LinkedInstrument.Number(drafts[key]);
        public void ApplyDisplay(DiffractionDisplay settings)
        {
            settings.Validate(); // Validate the entire draft before mutating preferences.
            Demo.Preferences.Diffraction=settings.Copy();Demo.Preferences.Save();
            Demo.Instrument.Reciprocal.SetDisplay(settings);
            Feedback="Display updated. Scientific values and sampling are unchanged.";
        }
        void BuildFixedFooter(float height)
        {
            if(fixedFooter!=null) {QuestUiTheme.ClearSelectionWithin(fixedFooter);fixedFooter.gameObject.SetActive(false);Destroy(fixedFooter.gameObject);}
            fixedFooter=Node("Always reachable navigation",transform,0,height,1000,92);
            DesktopTheme.Paint(fixedFooter,QuestUiTheme.Background);
            var previous=content;content=fixedFooter;
            var scroll=scrollHost.GetComponent<ScrollRect>();bool overflow=scroll.content.rect.height>scroll.viewport.rect.height+1;
            MakeButton("Up",28,10,160,72,()=>scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+.45f),enabled:overflow);
            MakeButton("Down",200,10,160,72,()=>scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-.45f),enabled:overflow);
            MakeButton("Menu",372,10,224,72,()=>ShowScreen("Home"));
            MakeButton(Paused?"Resume safely":"Close menu",608,10,364,72,()=> {if(Paused) SetPaused(false);Close();},true);
            content=previous;((RectTransform)transform).sizeDelta=new Vector2(1000,height+92);
        }
        public void RefreshResearchValues()
        {
            if(!IsOpen||Screen!="Inspector"||inspectorLive==null||Time.unscaledTime<nextInspector) return;
            nextInspector=Time.unscaledTime+.2f;
            DesktopSemanticText.Apply(inspectorLive,InspectorValue(),Demo.Preferences.HighContrast);
            inspectorLive.rectTransform.sizeDelta=new Vector2(944,Mathf.Max(Height(inspectorLive.text,26,944),900));
        }
        string InspectorValue()=>inspectorPage==0?DesktopResearchTools.Overview(Demo):inspectorPage==1?DesktopResearchTools.Point(Demo):DesktopResearchTools.Diffraction(Demo);
        bool BuildResearchScreen(string screen)
        {
            if(screen=="Inspector")
            {
                Paragraph("semantic-legend","Blue: variables / amber: numbers / violet: identities. Labels and units distinguish each role without relying on color alone.",24);
                Rail("Previous",()=> {inspectorPage=Math.Max(0,inspectorPage-1);Refresh();},"Next",()=> {inspectorPage=Math.Min(2,inspectorPage+1);Refresh();},inspectorPage>0,inspectorPage<2);
                Paragraph("inspector-page",new[]{"Overview / current state","Exact lattice identity / selected point","Finite diffraction / selected peak"}[inspectorPage],26);
                Row("Select another point or peak",()=> {Explore();Close();});
                if(inspectorPage==2) Row("Color and filters",()=>ShowScreen("Diffraction"));
                string value=DesktopSemanticText.Present(InspectorValue(),Demo.Preferences.HighContrast);
                float h=Mathf.Max(900,Height(value,26,944));inspectorLive=Label("Live typed numerical inspector",content,28,cursor,944,h,26);
                inspectorLive.supportRichText=true;inspectorLive.text=value;cursor+=h+16;
                Row("Export all numerical results (CSV + state)",()=> {string folder=DesktopResearchTools.Export(Demo,Path.Combine(Application.persistentDataPath,"AperiTraceNumericalResults"),CanSave);Feedback="Saved CSV + state in AperiTraceNumericalResults / "+Path.GetFileName(folder);},enabled:CanSave&&Demo.Instrument.Reciprocal.Current);
            }
            else if(screen=="ViewTools")
            {
                Paragraph("view-context","Presentation only. Each space keeps its own scientific units. Focus a view for closer inspection; Use knob returns to Selection.");
                foreach(var choice in new[]{("All views",0),("Structure",1),("Selection window",2),("Finite diffraction",3)})
                {
                    var item=choice;Row(item.Item1+(Demo.Preferences.View==item.Item2?" / selected":""),()=> {Demo.Preferences.View=item.Item2;ApplyPreferences();},Demo.Preferences.View==item.Item2);
                }
                Row("Grid: "+(Demo.Preferences.GridVisible?"on":"off"),()=> {Demo.Preferences.GridVisible=!Demo.Preferences.GridVisible;Demo.Preferences.Save();});
                Rail("Zoom out",()=>ViewTools.Zoom(.8f),"Zoom in",()=>ViewTools.Zoom(1.25f));
                Row("Reset view / zoom",()=>ViewTools.ResetView());
                Row("Exact offset x / y",()=>BeginDisplayPage("Offset"));
                Row("Diffraction colors and filters",()=>ShowScreen("Diffraction"));
                Paragraph("zoom-help","Focused view: right thumbstick up/down zooms smoothly; left thumbstick pans. Inputs stop during menus, knob dragging, playback, tracking loss and pause. In All views zoom is bounded to preserve separation. View settings do not change coordinates or intensities.",24);
            }
            else if(screen=="Offset")
            {
                Paragraph("offset-help","Enter the internal selection offset. This changes the scientific selection and records a new sampled state. It does not move physical coordinates.");
                Numeric("offset-x","Offset x",Number(Demo.Lab.State.Phi.X));Numeric("offset-y","Offset y",Number(Demo.Lab.State.Phi.Y));
                Row("Apply offset and explore",()=> {double x=Draft("offset-x"),y=Draft("offset-y");if(x<Demo.Lab.PhiMin.X||x>Demo.Lab.PhiMax.X||y<Demo.Lab.PhiMin.Y||y>Demo.Lab.PhiMax.Y) throw new ArgumentException("Offset outside the loaded support domain.");Explore();Demo.Lab.SetPhi(x,y);Feedback="Offset applied. Wait for diffraction Ready.";Close();},true,enabled:CanSave);
            }
            else if(screen=="Diffraction")
            {
                Paragraph("display-contract","Color and visibility only. All numerical samples and CSV exports remain unchanged. Selected peaks are white. Updating is not a current result.");
                var range=Demo.Instrument.Reciprocal.ColorRange;
                var rect=Node("Diffraction colorbar",content,28,cursor,944,24);var bar=rect.gameObject.AddComponent<DiffractionColorbar>();bar.Settings=Display.Copy();bar.raycastTarget=false;cursor+=40;
                Paragraph("color-range","I / N squared  "+Number(range.min)+" to "+Number(range.max)+"\n"+Display.Palette+" / "+Display.Scale+"\n"+(Demo.Instrument.Reciprocal.Current?Demo.Instrument.Reciprocal.VisibleCount+" of "+Demo.Instrument.Reciprocal.Peaks.Length+" shown":"Updating / stale display"));
                Row("Colormap: "+Display.Palette,()=> {palettePage=Math.Max(0,Array.IndexOf(DiffractionColors.Names,Display.Palette)/4);ShowScreen("Colormaps");},true);
                Row("Color range and scale",()=>BeginDisplayPage("ColorRange"));Row("Intensity and reciprocal-radius filters",()=>BeginDisplayPage("Filters"));
                Row(Display.Invert?"Direction: reversed":"Direction: normal",()=> {var s=Display.Copy();s.Invert=!s.Invert;ApplyDisplay(s);});
                Row("Restore display defaults",()=>ApplyDisplay(new DiffractionDisplay()));
            }
            else if(screen=="Colormaps")
            {
                Paragraph("palette-guide","Viridis and Cividis support ordered intensity. Diverging/cyclic maps are exploration options, not evidence of signed intensity or phase.",24);
                for(int i=palettePage*4;i<Math.Min(DiffractionColors.Names.Length,palettePage*4+4);i++)
                {
                    string name=DiffractionColors.Names[i];Row(name+(name==Display.Palette?" / selected":""),()=> {if(name=="Custom") BeginDisplayPage("CustomColors");else {var s=Display.Copy();s.Palette=name;ApplyDisplay(s);ShowScreen("Diffraction");}},name==Display.Palette);
                    var rect=Node("Swatch "+name,content,28,cursor,944,18);var bar=rect.gameObject.AddComponent<DiffractionColorbar>();bar.Settings=Display.Copy();bar.Settings.Palette=name;bar.Settings.Invert=false;bar.raycastTarget=false;cursor+=30;
                }
                Rail("Previous",()=> {palettePage=Math.Max(0,palettePage-1);Refresh();},"Next",()=> {palettePage=Math.Min(4,palettePage+1);Refresh();},palettePage>0,palettePage<4);
                Paragraph("palette-page","Page "+(palettePage+1)+" of 5 / 18 palettes + custom",24);
            }
            else if(screen=="ColorRange")
            {
                Paragraph("range-help","Color limits saturate, not hide, peaks. Log10 needs a positive minimum. Auto range follows visible data; empty/constant data use manual limits.",24);
                foreach(string scale in new[]{"linear","sqrt","log"}) {string mode=scale;Row(mode=="log"?"Log10":mode=="sqrt"?"Square root":"Linear",()=> {var s=Display.Copy();s.Scale=mode;if(mode=="log"&&s.Minimum<=0) s.Minimum=1e-6;ApplyDisplay(s);drafts.Clear();},Display.Scale==scale);}
                Numeric("min","Color minimum",Number(Display.Minimum));Numeric("max","Color maximum",Number(Display.Maximum));
                Row("Apply manual range",()=> {var s=Display.Copy();s.Minimum=Draft("min");s.Maximum=Draft("max");s.AutoRange=false;ApplyDisplay(s);ShowScreen("Diffraction");},true);
                Row("Auto range: "+(Display.AutoRange?"on":"off"),()=> {var s=Display.Copy();s.AutoRange=!s.AutoRange;ApplyDisplay(s);});
            }
            else if(screen=="Filters")
            {
                Paragraph("filter-help","Inclusive bounds affect display and picking only. |q| uses model reciprocal units, not measured inverse angstroms. An empty display is valid; Show all peaks restores it.",24);
                Numeric("imin","Intensity from",Number(Display.VisibleMinimum));Numeric("imax","Intensity to",Number(Display.VisibleMaximum));Numeric("qmin","|q| from",Number(Display.QMinimum));Numeric("qmax","|q| to",Number(Display.QMaximum));
                Row("Apply filters",()=> {var s=Display.Copy();s.VisibleMinimum=Draft("imin");s.VisibleMaximum=Draft("imax");s.QMinimum=Draft("qmin");s.QMaximum=Draft("qmax");ApplyDisplay(s);ShowScreen("Diffraction");},true);
                Row("Central q = 0: "+(Display.HideCenter?"hidden":"shown"),()=> {var s=Display.Copy();s.HideCenter=!s.HideCenter;ApplyDisplay(s);});
                Row("Show all peaks",()=> {var s=Display.Copy();s.VisibleMinimum=s.QMinimum=0;s.VisibleMaximum=1;s.QMaximum=100;s.HideCenter=false;ApplyDisplay(s);drafts.Clear();});
            }
            else if(screen=="CustomColors")
            {
                Paragraph("custom-help","Enter three #RRGGBB colors. This is a custom exploration gradient, not a perceptually uniform scale.",24);
                Numeric("low","Low color",Display.Low);Numeric("middle","Middle color",Display.Middle);Numeric("high","High color",Display.High);
                Row("Apply custom gradient",()=> {var s=Display.Copy();s.Palette="Custom";s.Low=drafts["low"];s.Middle=drafts["middle"];s.High=drafts["high"];ApplyDisplay(s);ShowScreen("Diffraction");},true);
            }
            else if(screen=="Keypad")
            {
                Paragraph("keypad-value",editKey+" = "+editValue+"\nEdits are drafts until Apply. Cancel preserves the previous value.",30);
                string[] keys={"7","8","9","4","5","6","1","2","3","-","0",".","e","+","#","A","B","C","D","E","F"};
                for(int i=0;i<keys.Length;i+=3) {for(int j=0;j<3&&i+j<keys.Length;j++) {string key=keys[i+j];MakeButton(key,28+j*320,cursor,304,70,()=> {if(editValue.Length<48) editValue+=key;});}cursor+=82;}
                Rail("Backspace",()=> {if(editValue.Length>0) editValue=editValue.Substring(0,editValue.Length-1);},"Clear",()=>editValue="");
                Rail("Keep draft",()=> {drafts[editKey]=editValue;ShowScreen(editReturn);},"Cancel",()=>ShowScreen(editReturn),primaryFirst:true);
            }
            else return false;
            return true;
        }
    }
}
