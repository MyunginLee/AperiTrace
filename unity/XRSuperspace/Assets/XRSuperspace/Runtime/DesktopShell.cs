using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Screen-space UI: no world-space debug panels, no obstruction of the knob.
    public sealed partial class DesktopShell : MonoBehaviour
    {
        public DesktopWorkspace Workspace;
        public Canvas Canvas {get;private set;}
        public string Modal {get;private set;}="";
        public bool ModalOpen=>!string.IsNullOrEmpty(Modal);
        public RectTransform DiagramArea {get;private set;}
        public Rect DiagramRect
        {
            get
            {
                if(DiagramArea==null||Screen.width==0||Screen.height==0) return new Rect(.22f,.24f,.75f,.57f);
                DiagramArea.GetWorldCorners(diagramCorners);
                var lo=RectTransformUtility.WorldToScreenPoint(null,diagramCorners[0]);var hi=RectTransformUtility.WorldToScreenPoint(null,diagramCorners[2]);
                return new Rect(lo.x/Screen.width,lo.y/Screen.height,(hi.x-lo.x)/Screen.width,(hi.y-lo.y)/Screen.height);
            }
        }
        RectTransform chrome,modalLayer,modalBody;
        RectTransform lessonPanel;
        RectTransform lessonActions;
        CanvasGroup chromeGroup;
        float lessonBaseHeight;
        ScrollRect modalScroll;
        Text feedback,lessonTitle,lessonText,dirty,viewHint;
        Button primary,before,pause,save;
        readonly List<Button> focus=new();
        readonly List<Text> viewHeadings=new();
        readonly List<Selectable> navigation=new();
        int examplePage,sessionPage,lastWidth,lastHeight;
        float nextLive;
        string error;
        readonly Vector3[] diagramCorners=new Vector3[4];
        Coroutine modalFade;
        Coroutine inspectorFade;
        RectTransform inspector;
        CanvasGroup inspectorGroup;
        Text inspectorReadout,inspectorStatus;
        RectTransform inspectorCard;
        InputField offsetX,offsetY;
        DesktopViewGrid grid;
        public DesktopViewGrid Grid=>grid;
        Text scaleReadout;
        Button gridToggle;
        Button diffractionAppearance;
        Button applyOffset;
        int inspectorSection;
        public bool InspectorOpen {get;private set;}
        public bool Transitioning {get;private set;}
        float InspectorWidth=>Workspace.Preferences.TextSize==2?410:360;
        ScientificDemo Demo=>Workspace.Demo;
        int Size(int value)=>Mathf.RoundToInt(value*Workspace.Preferences.TextScale);
        float ButtonHeight=>Mathf.Max(48,Size(20)*2.4f);
        public void Build()
        {
            gameObject.layer=5;
            Canvas=gameObject.AddComponent<Canvas>();Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Canvas.sortingOrder=100;
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1600,1000);scaler.matchWidthOrHeight=.5f;
            gameObject.AddComponent<GraphicRaycaster>();BuildChrome();
        }
        RectTransform Panel(string id,Transform parent,Color color,bool hit=true)
        {var node=DesktopTheme.Node(id,parent);DesktopTheme.Paint(node,color,hit,id=="lesson"||id=="modal-panel"||id=="inspector"?16:0);return node;}
        Text TextAt(string id,Transform parent,string value,float x,float y,float w,float h,int size,int weight=0,bool muted=false)
        {
            var label=DesktopTheme.Label(id,parent,value,Size(size),weight);DesktopTheme.Box(label.rectTransform,x,y,w,h);if(muted) label.color=Workspace.Preferences.HighContrast?Color.white:DesktopTheme.Muted;return label;
        }
        Button ActionAt(string id,Transform parent,string value,float x,float y,float w,float h,Action action,bool primary=false,bool destructive=false)
        {
            var button=DesktopTheme.Button(id,parent,value,()=>Workspace.Run(action),Size(19),primary,destructive,Workspace.Preferences.HighContrast);
            DesktopTheme.Box(button.GetComponent<RectTransform>(),x,y,w,h);navigation.Add(button);return button;
        }
        void Retire(RectTransform node)
        {
            if(node==null) return;QuestUiTheme.ClearSelectionWithin(node);
            navigation.RemoveAll(s=>s==null||s.transform.IsChildOf(node));
            node.gameObject.SetActive(false);Destroy(node.gameObject);
        }
        void BuildChrome()
        {
            if(inspectorFade!=null) StopCoroutine(inspectorFade);inspectorFade=null;
            Retire(chrome);navigation.Clear();focus.Clear();viewHeadings.Clear();
            chrome=DesktopTheme.Node("desktop-chrome",transform);DesktopTheme.Fill(chrome);
            chromeGroup=chrome.gameObject.AddComponent<CanvasGroup>();chromeGroup.interactable=chromeGroup.blocksRaycasts=!ModalOpen;
            var header=Panel("top-bar",chrome,DesktopTheme.Panel);header.anchorMin=new Vector2(0,1);header.anchorMax=Vector2.one;header.pivot=new Vector2(0,1);header.sizeDelta=new Vector2(0,80);
            TextAt("brand",header,"AperiTrace",28,12,210,56,27,1);
            dirty=TextAt("save-state",header,"",246,27,300,30,16,0,true);
            var toolbar=DesktopTheme.Node("toolbar",header);toolbar.anchorMin=toolbar.anchorMax=toolbar.pivot=new Vector2(1,1);toolbar.anchoredPosition=new Vector2(-20,-12);toolbar.sizeDelta=new Vector2(616,56);
            save=ActionAt("save",toolbar,"Save",0,0,116,56,Workspace.Save);
            ActionAt("inspect",toolbar,"Inspect",126,0,188,56,ToggleInspector);
            pause=ActionAt("pause",toolbar,"Pause",324,0,128,56,()=>Workspace.SetPaused(!Workspace.Paused));
            ActionAt("menu",toolbar,"Menu",462,0,154,56,()=>ShowModal("Menu"));
            var footer=Panel("status-bar",chrome,DesktopTheme.Panel);footer.anchorMin=Vector2.zero;footer.anchorMax=new Vector2(1,0);footer.pivot=Vector2.zero;footer.sizeDelta=new Vector2(0,48);
            feedback=DesktopTheme.Label("feedback",footer,"",Size(16));DesktopTheme.Fill(feedback.rectTransform,24,10,24,8);
            var stage=DesktopTheme.Node("main-stage",chrome);DesktopTheme.Fill(stage,32,64,InspectorOpen?InspectorWidth+48:32,100);
            var viewBar=DesktopTheme.Node("view-bar",stage);viewBar.anchorMin=viewBar.anchorMax=new Vector2(.5f,1);viewBar.pivot=new Vector2(.5f,1);viewBar.sizeDelta=new Vector2(760,48);
            var layout=viewBar.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=10;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;
            string[] names={"All views","Structure","Window","Diffraction"};
            for(int i=0;i<4;i++) {int index=i;focus.Add(AddButton(viewBar,"view-"+i,names[i],()=>Workspace.SetFocus(index)));}
            var headings=DesktopTheme.Node("view-headings",stage);headings.anchorMin=new Vector2(0,1);headings.anchorMax=Vector2.one;headings.pivot=new Vector2(0,1);headings.anchoredPosition=new Vector2(0,-74);headings.sizeDelta=new Vector2(0,44);
            foreach(var pair in new[]{("structure-heading","Structure"),("selection-heading","Selection window"),("diffraction-heading","Finite diffraction")})
            {var text=DesktopTheme.Label(pair.Item1,headings,pair.Item2,Size(18),1);text.color=Workspace.Preferences.HighContrast?Color.white:DesktopTheme.Muted;text.alignment=TextAnchor.MiddleCenter;viewHeadings.Add(text);}
            float lessonHeight=Workspace.Preferences.TextSize==2?292:Workspace.Preferences.TextSize==1?260:236;
            lessonBaseHeight=lessonHeight;
            DiagramArea=DesktopTheme.Node("diagram-area",stage);DesktopTheme.Fill(DiagramArea,4,lessonHeight+44,4,128);
            var gridRoot=DesktopTheme.Node("calibrated-grid",DiagramArea);DesktopTheme.Fill(gridRoot);gridRoot.gameObject.AddComponent<RectMask2D>();
            var lines=DesktopTheme.Node("grid-lines",gridRoot);DesktopTheme.Fill(lines);grid=lines.gameObject.AddComponent<DesktopViewGrid>();grid.Workspace=Workspace;grid.raycastTarget=false;
            grid.TickLabels=new Text[18];grid.Captions=new Text[3];
            for(int i=0;i<18;i++)
            {
                var tick=DesktopTheme.Label("grid-tick-"+i,lines,"",Size(14));tick.color=DesktopSemanticText.Tint(DesktopSemanticText.Role.Number,Workspace.Preferences.HighContrast);tick.alignment=i%6<3?TextAnchor.MiddleCenter:TextAnchor.MiddleLeft;
                tick.rectTransform.anchorMin=tick.rectTransform.anchorMax=tick.rectTransform.pivot=new Vector2(.5f,.5f);tick.rectTransform.sizeDelta=new Vector2(96,28);grid.TickLabels[i]=tick;tick.gameObject.SetActive(false);
            }
            for(int i=0;i<3;i++) {var caption=DesktopTheme.Label("grid-caption-"+i,lines,"",Size(14));caption.alignment=TextAnchor.MiddleCenter;caption.rectTransform.anchorMin=caption.rectTransform.anchorMax=caption.rectTransform.pivot=new Vector2(.5f,.5f);grid.Captions[i]=caption;caption.gameObject.SetActive(false);}
            gridToggle=ActionAt("toggle-grid",DiagramArea,"Grid / G",8,4,124,48,Workspace.ToggleGrid);
            ActionAt("view-controls",DiagramArea,"View...",142,4,128,48,()=>ShowModal("View"));
            diffractionAppearance=ActionAt("diffraction-display",DiagramArea,"Color & filters...",280,4,Workspace.Preferences.TextSize==2?248:Workspace.Preferences.TextSize==1?210:180,Mathf.Max(48,ButtonHeight),()=>ShowModal("Diffraction"));
            diffractionAppearance.gameObject.SetActive(Workspace.FocusView==0||Workspace.FocusView==3);
            BuildInputNotice();
            scaleReadout=DesktopTheme.Label("view-scale",DiagramArea,"",Size(14));scaleReadout.color=DesktopTheme.Muted;scaleReadout.alignment=TextAnchor.LowerRight;
            scaleReadout.rectTransform.anchorMin=new Vector2(0,0);scaleReadout.rectTransform.anchorMax=new Vector2(1,0);scaleReadout.rectTransform.pivot=Vector2.zero;scaleReadout.rectTransform.offsetMin=new Vector2(12,4);scaleReadout.rectTransform.offsetMax=new Vector2(-12,60);
            var lesson=Panel("lesson",stage,DesktopTheme.Panel);lesson.anchorMin=Vector2.zero;lesson.anchorMax=new Vector2(1,0);lesson.pivot=Vector2.zero;lesson.sizeDelta=new Vector2(0,lessonHeight);
            lessonPanel=lesson;
            lessonTitle=DesktopTheme.Label("lesson-title",lesson,"",Size(23),1);lessonTitle.rectTransform.anchorMin=new Vector2(0,1);lessonTitle.rectTransform.anchorMax=Vector2.one;lessonTitle.rectTransform.pivot=new Vector2(0,1);lessonTitle.rectTransform.offsetMin=new Vector2(22,-54);lessonTitle.rectTransform.offsetMax=new Vector2(-22,-16);
            lessonText=DesktopTheme.Label("lesson-copy",lesson,"",Size(19));DesktopTheme.Fill(lessonText.rectTransform,22,80,22,62);
            lessonActions=DesktopTheme.Node("lesson-actions",lesson);lessonActions.anchorMin=lessonActions.anchorMax=lessonActions.pivot=new Vector2(1,0);lessonActions.anchoredPosition=new Vector2(-22,16);lessonActions.sizeDelta=new Vector2(560,48);
            var actionsLayout=lessonActions.gameObject.AddComponent<HorizontalLayoutGroup>();actionsLayout.spacing=10;actionsLayout.childControlWidth=actionsLayout.childControlHeight=true;actionsLayout.childForceExpandWidth=true;
            primary=AddButton(lessonActions,"next","Next step",()=> {if(Demo.Guided&&Demo.Step<9) Workspace.Next();else Workspace.Explore();},true);
            before=AddButton(lessonActions,"before-after","Before / After",Workspace.ToggleExample);
            AddButton(lessonActions,"reset-view","Reset view",Workspace.ResetView);
            AddButton(lessonActions,"worked-examples","Examples",()=>ShowModal("Examples"));
            viewHint=DesktopTheme.Label("view-hint",stage,"",Size(16));viewHint.color=DesktopTheme.Muted;viewHint.rectTransform.anchorMin=Vector2.zero;viewHint.rectTransform.anchorMax=new Vector2(1,0);viewHint.rectTransform.pivot=Vector2.zero;viewHint.rectTransform.offsetMin=new Vector2(12,lessonHeight+4);viewHint.rectTransform.offsetMax=new Vector2(-12,lessonHeight+36);viewHint.alignment=TextAnchor.MiddleCenter;
            BuildInspector();
            lastWidth=Screen.width;lastHeight=Screen.height;nextLive=0;
        }
        static ScrollRect Scroll(Transform parent,string id,out RectTransform content)
        {
            var root=DesktopTheme.Node(id,parent);var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var viewport=DesktopTheme.Node(id+"-viewport",root);DesktopTheme.Fill(viewport,0,0,12,0);DesktopTheme.Paint(viewport,new Color(0,0,0,0),true);viewport.gameObject.AddComponent<RectMask2D>();scroll.viewport=viewport;
            content=DesktopTheme.Node(id+"-content",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;scroll.content=content;
            var bar=DesktopTheme.Node(id+"-scrollbar",root);bar.anchorMin=new Vector2(1,0);bar.anchorMax=Vector2.one;bar.offsetMin=new Vector2(-8,0);bar.offsetMax=Vector2.zero;DesktopTheme.Paint(bar,DesktopTheme.Surface,true);
            var scrollbar=bar.gameObject.AddComponent<Scrollbar>();scrollbar.direction=Scrollbar.Direction.BottomToTop;var handle=DesktopTheme.Node(id+"-handle",bar);DesktopTheme.Fill(handle);var image=DesktopTheme.Paint(handle,DesktopTheme.Border,true);scrollbar.handleRect=handle;scrollbar.targetGraphic=image;scroll.verticalScrollbar=scrollbar;return scroll;
        }
        static void Stack(RectTransform content,float spacing,int padding)
        {
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=spacing;layout.padding=new RectOffset(padding,padding,padding,padding);layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        }
        Text AddParagraph(Transform parent,string id,string value,int size,int weight,Color color,float width)
        {
            var text=DesktopTheme.Label(id,parent,value,Size(size),weight);DesktopSemanticText.Apply(text,value,Workspace.Preferences.HighContrast,Workspace.Preferences.HighContrast?Color.white:color);
            var element=text.gameObject.AddComponent<LayoutElement>();element.preferredHeight=DesktopTheme.Measure(text.text,Size(size),width,true);return text;
        }
        void AddSpace(Transform parent,float height) {DesktopTheme.Node("spacing",parent).gameObject.AddComponent<LayoutElement>().preferredHeight=height;}
        Button AddButton(Transform parent,string id,string value,Action action,bool primary=false,bool destructive=false)
        {
            var button=DesktopTheme.Button(id,parent,value,()=>Workspace.Run(action),Size(19),primary,destructive,Workspace.Preferences.HighContrast);
            var element=button.gameObject.AddComponent<LayoutElement>();element.preferredHeight=ButtonHeight;element.minHeight=ButtonHeight;element.flexibleWidth=1;navigation.Add(button);return button;
        }
        RectTransform Horizontal(Transform parent,string id,float height)
        {
            var row=DesktopTheme.Node(id,parent);row.gameObject.AddComponent<LayoutElement>().preferredHeight=height;
            var group=row.gameObject.AddComponent<HorizontalLayoutGroup>();group.spacing=10;group.childControlWidth=group.childControlHeight=true;group.childForceExpandWidth=true;return row;
        }
        public void Restyle()
        {
            if(Canvas==null) return;if(modalFade!=null) StopCoroutine(modalFade);modalFade=null;Transitioning=false;
            string screen=Modal;Retire(modalLayer);modalLayer=null;BuildChrome();if(!string.IsNullOrEmpty(screen)) ShowModal(screen);Workspace.Reframe();
        }
        public void ToggleInspector()
        {
            Workspace.CancelGesture();
            if(inspectorFade!=null) StopCoroutine(inspectorFade);inspectorFade=null;
            if(InspectorOpen&&!Workspace.Preferences.ReducedMotion) {inspectorFade=StartCoroutine(CloseInspector());return;}
            InspectorOpen=!InspectorOpen;Workspace.AnimateNextFrame();Restyle();
            if(InspectorOpen) inspectorFade=StartCoroutine(RevealInspector());
        }
        public void Inspect(int section)
        {
            inspectorSection=Mathf.Clamp(section,0,2);
            if(!InspectorOpen) ToggleInspector();else BuildInspector();
        }
        IEnumerator RevealInspector()
        {
            var group=inspectorGroup;group.alpha=0;float duration=Workspace.Preferences.ReducedMotion?0:.18f;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime) {group.alpha=DesktopMotion.Ease(t/duration);yield return null;}
            group.alpha=1;inspectorFade=null;
        }
        IEnumerator CloseInspector()
        {
            var group=inspectorGroup;float alpha=group.alpha;group.interactable=group.blocksRaycasts=false;
            for(float t=0;t<.14f;t+=Time.unscaledDeltaTime) {group.alpha=alpha*(1-DesktopMotion.Ease(t/.14f));yield return null;}
            inspectorFade=null;InspectorOpen=false;Workspace.AnimateNextFrame();Restyle();
        }
        void BuildInspector()
        {
            if(inspectorFade!=null) StopCoroutine(inspectorFade);inspectorFade=null;
            Retire(inspector);inspector=null;inspectorReadout=null;if(!InspectorOpen) return;
            inspector=Panel("inspector",chrome,DesktopTheme.Panel);inspector.anchorMin=new Vector2(1,0);inspector.anchorMax=Vector2.one;inspector.pivot=new Vector2(1,.5f);
            inspector.offsetMin=new Vector2(-InspectorWidth-16,64);inspector.offsetMax=new Vector2(-16,-100);inspectorGroup=inspector.gameObject.AddComponent<CanvasGroup>();
            var scroll=Scroll(inspector,"inspector-scroll",out var body);DesktopTheme.Fill(scroll.GetComponent<RectTransform>(),20,20,20,20);Stack(body,12,0);
            AddParagraph(body,"inspector-heading","Inspector",22,1,DesktopTheme.Text,InspectorWidth-52);
            var tabs=Horizontal(body,"inspector-tabs",ButtonHeight);
            string[] names={"Model","Point","Peak"};for(int i=0;i<3;i++)
            {int index=i;var button=AddButton(tabs,"inspect-tab-"+i,names[i],()=>Inspect(index),i==inspectorSection);DesktopTheme.Fill(button.GetComponentInChildren<Text>().rectTransform,8,9,8,9);}
            inspectorStatus=AddParagraph(body,"inspector-status","",16,0,DesktopTheme.Muted,InspectorWidth-52);
            var legend=AddParagraph(body,"syntax-legend","",14,0,DesktopTheme.Muted,InspectorWidth-52);legend.text=DesktopSemanticText.Legend(Workspace.Preferences.HighContrast);
            legend.GetComponent<LayoutElement>().preferredHeight=DesktopTheme.Measure(legend.text,legend.fontSize,InspectorWidth-52,true);
            inspectorCard=DesktopTheme.Node("inspector-data-card",body);DesktopTheme.Paint(inspectorCard,DesktopTheme.Surface,false,12);inspectorCard.gameObject.AddComponent<LayoutElement>();
            inspectorReadout=DesktopTheme.Label("inspector-values",inspectorCard,"",Size(17));DesktopTheme.Fill(inspectorReadout.rectTransform,16,16,16,16);
            if(inspectorSection==0)
            {
                AddButton(body,"precision","Precision controls...",()=>ShowModal("Precision"));
                AddButton(body,"reference",Workspace.ReferencePinned?"Unpin reference":"Pin current reference",()=> {Workspace.PinComparison();BuildInspector();});
            }
            if(inspectorSection==2) BuildDiffractionLegend(body,InspectorWidth-52);
            AddButton(body,"copy-values","Copy values",()=> {GUIUtility.systemCopyBuffer=InspectorValues();Workspace.Notify("Numerical summary copied.");});
            var export=AddButton(body,"export-values","Export CSV...",Workspace.ExportNumbers);export.interactable=Workspace.CanSave&&Demo.Instrument.Reciprocal.Current;
            AddButton(body,"close-inspector","Hide inspector",ToggleInspector);
            RefreshInspector();
        }
        string InspectorValues()=>inspectorSection==1?DesktopResearchTools.Point(Demo):inspectorSection==2?DesktopResearchTools.Diffraction(Demo):DesktopResearchTools.Overview(Demo);
        void RefreshInspector()
        {
            if(inspectorReadout==null) return;DesktopSemanticText.Apply(inspectorReadout,InspectorValues(),Workspace.Preferences.HighContrast);
            inspectorCard.GetComponent<LayoutElement>().preferredHeight=DesktopTheme.Measure(inspectorReadout.text,inspectorReadout.fontSize,InspectorWidth-84,true)+32;
            DesktopSemanticText.Apply(inspectorStatus,Demo.Instrument.Reciprocal.Current?"Ready / current numerical state":"Updating / intensities withheld",Workspace.Preferences.HighContrast,Workspace.Preferences.HighContrast?Color.white:DesktopTheme.Muted);
            inspectorStatus.GetComponent<LayoutElement>().preferredHeight=DesktopTheme.Measure(inspectorStatus.text,inspectorStatus.fontSize,InspectorWidth-52,true);
            var export=inspector.GetComponentsInChildren<Button>().First(b=>b.name=="export-values");export.interactable=Workspace.CanSave&&Demo.Instrument.Reciprocal.Current;
        }
        InputField NumberField(string id,string caption,string value,float width)
        {
            string symbol=id switch {"offset-x"=>"φx","offset-y"=>"φy","view-center-x"=>"cx","view-center-y"=>"cy","view-width"=>"width","view-height"=>"height",_=>""};
            if(symbol.Length>0) caption=symbol+"  /  "+caption;
            AddParagraph(modalBody,id+"-caption",caption,18,1,DesktopTheme.Text,width);
            var node=DesktopTheme.Node(id,modalBody);DesktopTheme.Paint(node,DesktopTheme.Surface,true,8);node.gameObject.AddComponent<LayoutElement>().preferredHeight=ButtonHeight;
            var field=node.gameObject.AddComponent<InputField>();var text=DesktopTheme.Label(id+"-text",node,"",Size(20));text.color=DesktopSemanticText.Tint(DesktopSemanticText.Role.Number,Workspace.Preferences.HighContrast);DesktopTheme.Fill(text.rectTransform,16,8,16,8);text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Overflow;
            field.textComponent=text;field.text=value;field.characterLimit=32;field.contentType=InputField.ContentType.Standard;field.lineType=InputField.LineType.SingleLine;
            navigation.Add(field);return field;
        }
        public void ShowError(string message) {error=message;ShowModal("Error");}
        public void ShowModal(string screen)
        {
            Workspace.CancelGesture();Workspace.ClearDiagramFocus();if(modalFade!=null) StopCoroutine(modalFade);modalFade=null;Transitioning=false;Retire(modalLayer);Modal=screen;
            if(chromeGroup!=null) {chromeGroup.interactable=chromeGroup.blocksRaycasts=false;chromeGroup.alpha=screen=="Welcome"?0:1;}
            modalLayer=Panel("modal-dimmer",transform,new Color(.005f,.012f,.022f,.87f));DesktopTheme.Fill(modalLayer);
            var panel=Panel("modal-panel",modalLayer,DesktopTheme.Panel);panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
            var root=(RectTransform)transform;Canvas.ForceUpdateCanvases();panel.sizeDelta=new Vector2(Mathf.Min(780,root.rect.width-48),Mathf.Min(860,root.rect.height-48));
            if(screen=="Welcome") panel.sizeDelta=new Vector2(Mathf.Min(720,root.rect.width-48),Mathf.Min(Workspace.Preferences.TextSize==2?720:620,root.rect.height-48));
            string title=screen=="Diffraction"?"Diffraction display":screen=="Colormaps"?"Choose a colormap":screen=="DiffractionRange"?"Color range & scale":screen=="DiffractionFilters"?"Peak display filters":screen=="CustomColors"?"Custom gradient":screen=="Welcome"?"AperiTrace":screen=="View"?"Grid, scale & exact view":screen=="Exported"?"Results exported":screen=="Precision"?"Precision controls":screen=="Examples"?"Worked examples":screen=="Sessions"?"Your saved sessions":screen=="Settings"?"Display & accessibility":screen=="Help"?"How to use AperiTrace":screen=="About"?"Data & about":screen=="Quit"?"Quit AperiTrace?":screen=="Confirm"?Workspace.PendingTitle:screen=="Error"?"Action not completed":"Workspace menu";
            var heading=DesktopTheme.Label("modal-title",panel,title,Size(screen=="Welcome"?38:27),1);heading.rectTransform.anchorMin=new Vector2(0,1);heading.rectTransform.anchorMax=Vector2.one;heading.rectTransform.pivot=new Vector2(0,1);heading.rectTransform.offsetMin=new Vector2(28,-84);heading.rectTransform.offsetMax=new Vector2(-28,-24);
            var scroll=Scroll(panel,"modal-scroll",out modalBody);DesktopTheme.Fill(scroll.GetComponent<RectTransform>(),28,100,28,102);modalScroll=scroll;Stack(modalBody,14,0);
            BuildModal(screen,Mathf.Max(200,panel.sizeDelta.x-70));
            var close=DesktopTheme.Button("modal-close",panel,screen=="Welcome"?"Maybe later / open workspace":"Back to workspace  /  Esc",Workspace.CancelModal,Size(19),false,false,Workspace.Preferences.HighContrast);
            var closeRect=close.GetComponent<RectTransform>();closeRect.anchorMin=Vector2.zero;closeRect.anchorMax=new Vector2(1,0);closeRect.pivot=Vector2.zero;closeRect.offsetMin=new Vector2(28,24);closeRect.offsetMax=new Vector2(-28,78);navigation.Add(close);
            if(screen=="Welcome") {close.gameObject.SetActive(false);DesktopTheme.Fill(scroll.GetComponent<RectTransform>(),28,28,28,102);}
            var group=modalLayer.gameObject.AddComponent<CanvasGroup>();modalFade=StartCoroutine(Fade(group,panel));
            Canvas.ForceUpdateCanvases();SelectFirstModal();
        }
        IEnumerator Fade(CanvasGroup group,RectTransform panel)
        {
            float duration=Workspace.Preferences.ReducedMotion?0:.18f;group.alpha=0;
            for(float time=0;time<duration;time+=Time.unscaledDeltaTime) {float eased=DesktopMotion.Ease(time/duration);group.alpha=eased;panel.localScale=Vector3.one*Mathf.Lerp(.98f,1,eased);yield return null;}
            group.alpha=1;panel.localScale=Vector3.one;modalFade=null;
        }
        void BuildModal(string screen,float width)
        {
            void Copy(string id,string text,int size=20)=>AddParagraph(modalBody,id,text,size,0,DesktopTheme.Muted,width);
            void Button(string id,string text,Action action,bool primary=false,bool destructive=false)=>AddButton(modalBody,id,text,action,primary,destructive);
            if(BuildDiffractionModal(screen,width)) return;
            if(screen=="Welcome")
            {
                Copy("welcome-purpose","See how a selection rule shapes a structure and its calculated diffraction.",22);
                Copy("welcome-plan","Start with a short guided tour, or open the workspace. Exact numbers are one click away in Inspect.",18);
                Button("welcome-explore","Open workspace",Workspace.Explore,true);
                Button("welcome-tour","Take the guided tour",Workspace.StartGuide);
                Button("welcome-sessions","Open a saved session",()=>ShowModal("Sessions"));
            }
            else if(screen=="Menu")
            {
                Copy("menu-purpose","Everything stays on this computer. Saving creates a new file; previous sessions are kept.");
                Button("menu-home","Start screen",()=>ShowModal("Welcome"));
                Button("menu-tour","Guided tour",Workspace.StartGuide);
                Button("menu-explore","Free exploration",Workspace.Explore);
                Button("menu-view","Grid, scale & exact view...",()=>ShowModal("View"));
                Button("menu-diffraction","Diffraction colors & filters...",()=>ShowModal("Diffraction"));
                Button("menu-examples","Worked examples",()=>ShowModal("Examples"),true);Button("menu-sessions","Saved sessions",()=>ShowModal("Sessions"));
                var screenshot=AddButton(modalBody,"menu-screenshot","Screenshot + scientific state",()=>Workspace.CaptureScreenshot());screenshot.interactable=Workspace.CanSave&&Demo.Instrument.Reciprocal.Current;
                Button("menu-settings","Display & accessibility",()=>ShowModal("Settings"));Button("menu-help","Help & controls",()=>ShowModal("Help"));
                Button("menu-about","Data & about",()=>ShowModal("About"));Button("menu-quit","Quit...",Workspace.RequestQuit,false,true);
            }
            else if(screen=="Examples")
            {
                Copy("examples-copy","Start with foundations, then compare membership, diffraction, and scientific subsets. Examples preserve your trace.");
                Button("examples-tour","Start the guided tour",Workspace.StartGuide,true);
                Copy("example-section",new[]{"FOUNDATIONS  /  01-04","MEMBERSHIP  /  05-08","BOUNDARIES  /  09-12","COMPARISONS  /  13-16","DIFFRACTION & SUBSETS  /  17-20"}[examplePage],16);
                for(int i=examplePage*4;i<Mathf.Min(examplePage*4+4,QuestExampleLibrary.All.Count);i++) {int index=i;Button("example-"+index,$"{index+1:00}   {QuestExampleLibrary.All[index].Title}",()=>Workspace.ChooseExample(index));}
                var row=Horizontal(modalBody,"example-pages",ButtonHeight);var previous=AddButton(row,"examples-previous","Previous",()=> {examplePage=Mathf.Max(0,examplePage-1);RefreshModal();});previous.interactable=examplePage>0;
                var next=AddButton(row,"examples-next","Next page",()=> {examplePage=Mathf.Min(4,examplePage+1);RefreshModal();});next.interactable=examplePage<4;
                Copy("example-page","Page "+(examplePage+1)+" of 5",16);
            }
            else if(screen=="Sessions")
            {
                Copy("sessions-copy","Save the exact scientific state and trace, then restore it later. Screenshots are saved separately.");
                var saveButton=AddButton(modalBody,"sessions-save","Save a new session  /  Ctrl+S",Workspace.Save,true);saveButton.interactable=Workspace.CanSave;
                var files=LocalSessionStore.List(Demo.SessionFolder);sessionPage=Mathf.Clamp(sessionPage,0,Mathf.Max(0,(files.Length-1)/5));
                if(files.Length==0) Copy("sessions-empty","No saved sessions yet. Choose Save a new session to create your first.");
                for(int i=sessionPage*5;i<Mathf.Min(files.Length,sessionPage*5+5);i++) {string path=files[i],stem=Path.GetFileNameWithoutExtension(path);Button("session-"+i,"Load  "+File.GetLastWriteTime(path).ToString("yyyy-MM-dd  HH:mm:ss")+"  /  "+stem.Substring(stem.Length-6),()=>Workspace.Load(path));}
                if(files.Length>5) {var row=Horizontal(modalBody,"session-pages",ButtonHeight);AddButton(row,"sessions-previous","Previous",()=> {sessionPage=Mathf.Max(0,sessionPage-1);RefreshModal();});AddButton(row,"sessions-next","Next page",()=> {sessionPage=Mathf.Min((files.Length-1)/5,sessionPage+1);RefreshModal();});}
                Button("open-folder","Open local data folder",()=> {Directory.CreateDirectory(Application.persistentDataPath);Application.OpenURL(new Uri(Application.persistentDataPath+Path.DirectorySeparatorChar).AbsoluteUri);});
                Button("clear-trace","Clear trace and reset knob...",Workspace.ClearTrace,false,true);
                Copy("sessions-location",Demo.SessionFolder,16);
            }
            else if(screen=="Settings")
            {
                var p=Workspace.Preferences;Copy("settings-copy","Preferences affect display only. They do not change coordinates, membership or finite diffraction.");
                Button("text-size","Text size: "+new[]{"Standard","Large","Extra large"}[p.TextSize],()=> {p.TextSize=(p.TextSize+1)%3;Workspace.ApplyPreferences();});
                Button("high-contrast","High contrast: "+(p.HighContrast?"On":"Off"),()=> {p.HighContrast=!p.HighContrast;Workspace.ApplyPreferences();});
                Button("palette","Scientific palette: "+(p.AlternativePalette?"Blue / amber":"Teal / amber"),()=> {p.AlternativePalette=!p.AlternativePalette;Workspace.ApplyPreferences();});
                Button("reduced-motion","Reduced motion: "+(p.ReducedMotion?"On":"Off"),()=> {p.ReducedMotion=!p.ReducedMotion;Workspace.ApplyPreferences();});
                Button("welcome-preference",p.SkipWelcome?"Start screen: skipped next time":"Start screen: show next time",()=> {p.SkipWelcome=!p.SkipWelcome;Workspace.ApplyPreferences();});
                Copy("keyboard-copy","Tab / Shift+Tab moves focus. Enter activates. Controls have visible focus outlines and do not rely on color alone.");
            }
            else if(screen=="View")
            {
                Copy("view-explanation","Wheel: 1.5x zoom per notch. Middle-drag: direct pan. G: independent grids in every view. Fine 1/2/5 spacing adapts to screen scale; major lines occur every 5 small cells. Right-drag: rotate Structure. Home: restore all views. Navigation changes display only.",18);
                Button("view-grid",Workspace.GridVisible?"Hide grid / G":"Show grid / G",()=> {Workspace.ToggleGrid();RefreshModal();});
                if(Workspace.FocusView==0) Copy("view-focus-first","All views shows three independently clipped grids with their own origin, units and spacing. There is no common scientific scale across the spaces. Choose an individual view for exact camera center/span input. Structure and Window are dimensionless; Diffraction uses model reciprocal units.",18);
                else
                {
                    Copy("view-units",Workspace.FocusView==1?"Structure x / y: dimensionless model units. Applying an exact view resets rotation to the x/y plane.":Workspace.FocusView==2?"Window u / v: dimensionless internal coordinates after the selection offset.":"Diffraction qx / qy: model reciprocal units, not measured inverse angstroms.",18);
                    var culture=System.Globalization.CultureInfo.InvariantCulture;var center=Workspace.ViewCenter;var span=Workspace.ViewSpan;
                    var x=NumberField("view-center-x","Horizontal center",center.x.ToString("R",culture),width);
                    var y=NumberField("view-center-y","Vertical center",center.y.ToString("R",culture),width);
                    var w=NumberField("view-width","Horizontal span",span.x.ToString("R",culture),width);
                    var h=NumberField("view-height","Vertical span",span.y.ToString("R",culture),width);
                    Copy("view-aspect","Aspect ratio stays locked. Apply width derives height; apply height derives width. Zoom range: 10% to 2000%. Camera positions are display precision, not research outputs.",17);
                    Button("apply-view-width","Apply center + width",()=>Workspace.ApplyView(x.text,y.text,w.text,true),true);
                    Button("apply-view-height","Apply center + height",()=>Workspace.ApplyView(x.text,y.text,h.text,false));
                }
            }
            else if(screen=="Precision")
            {
                Copy("precision-explanation","Set the selection offset with exact numerical input. Applied values are recorded as a discrete scientific state; animation never interpolates membership.",18);
                var culture=System.Globalization.CultureInfo.InvariantCulture;
                Copy("precision-domain",$"Allowed x: {Demo.Lab.PhiMin.X.ToString("R",culture)} to {Demo.Lab.PhiMax.X.ToString("R",culture)}\nAllowed y: {Demo.Lab.PhiMin.Y.ToString("R",culture)} to {Demo.Lab.PhiMax.Y.ToString("R",culture)}",17);
                offsetX=NumberField("offset-x","Offset x",Demo.Lab.State.Phi.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture),width);
                offsetY=NumberField("offset-y","Offset y",Demo.Lab.State.Phi.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),width);
                applyOffset=AddButton(modalBody,"apply-offset","Apply offsets",()=>Workspace.ApplyOffsets(offsetX.text,offsetY.text),true);applyOffset.interactable=Workspace.CanAdjustOffsets;
                if(!Workspace.CanAdjustOffsets) Copy("precision-locked","Return to Free exploration and resume the app before editing offsets.",17);
                Copy("precision-step","Step the current applied state by 0.01. Each step updates the fields and remains in this dialog.",17);
                var row=Horizontal(modalBody,"precision-nudges",ButtonHeight);
                foreach(var item in new[]{("X -",-.01,0.0),("X +",.01,0.0),("Y -",0.0,-.01),("Y +",0.0,.01)})
                {var delta=item;var button=AddButton(row,"nudge-"+item.Item1,item.Item1,()=> {Workspace.ApplyOffsets((Demo.Lab.State.Phi.X+delta.Item2).ToString("R",System.Globalization.CultureInfo.InvariantCulture),(Demo.Lab.State.Phi.Y+delta.Item3).ToString("R",System.Globalization.CultureInfo.InvariantCulture),false);offsetX.text=Demo.Lab.State.Phi.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture);offsetY.text=Demo.Lab.State.Phi.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture);});button.interactable=Workspace.CanAdjustOffsets;}
                Button("toggle-lines",Demo.Lab.View.ShowEdges?"Lattice connections: shown":"Lattice connections: hidden",()=> {Demo.Lab.ToggleEdges();RefreshModal();});
            }
            else if(screen=="Exported")
            {
                Copy("exported-success","Numerical results saved",22);
                Copy("exported-files","selected_points.csv\nfinite_diffraction.csv\nstate.json\nREADME.txt\n\nState and tables describe the same Ready revision. Previous exports are kept.",18);
                Copy("exported-folder",Workspace.LastNumericalExport,16);
                Button("open-export","Open export folder",()=>Application.OpenURL(new Uri(Workspace.LastNumericalExport+Path.DirectorySeparatorChar).AbsoluteUri),true);
            }
            else if(screen=="Quit"||screen=="Confirm")
            {
                Copy("confirm-copy",screen=="Quit"?"Keep your work before leaving. Saved sessions and screenshots will remain on this computer.":"This action replaces the current unsaved state or clears its trace. Save first, discard, or go back.");
                if(!Workspace.CanSave) Copy("confirm-wait","A guided example or capture is in progress. Return to the workspace and let it finish before saving.");
                var yes=AddButton(modalBody,"confirm-save",screen=="Quit"?"Save and quit":"Save and continue",()=> {if(screen=="Quit") Workspace.ConfirmQuit(true);else Workspace.ConfirmChange(true);},true);yes.interactable=Workspace.CanSave;
                var discard=AddButton(modalBody,"confirm-discard",screen=="Quit"?"Quit without saving":"Discard and continue",()=> {if(screen=="Quit") Workspace.ConfirmQuit(false);else Workspace.ConfirmChange(false);},false,true);discard.interactable=!Workspace.Capturing&&!Demo.Capture.Busy;
                Button("confirm-cancel","Cancel",Workspace.CancelModal);
            }
            else if(screen=="Error") {Copy("error-message",error??"The action was refused without replacing your current state.");Copy("error-recovery","Return to the workspace, wait for Ready, and try again. Existing saved files are kept.");}
            else if(screen=="About")
            {
                Copy("about-brand","AperiTrace 0.1.0\nImplemented as the Python package qc5d, with this Unity inspection interface.\nSupport: myungin@umd.edu");
                Copy("about-science","Canonical cut-and-project model. The structure is a finite mathematical point set, not a refined material. Lines are lattice neighbours, not chemical bonds. Diffraction is calculated, not measured.");
                Copy("about-data",Demo.ProvenanceText(),16);Copy("about-license","Runs locally without an internet connection. No cloud sync or telemetry. Project software: MIT. Inter font: SIL Open Font License. Colormap tables: Matplotlib license, included with the application. Unity and package licenses retain their own terms.",16);
            }
            else
            {
                Copy("help-start","Start with Guided tour and Next step. Then choose Explore freely. Worked examples let you compare meaningful Before / After states without guessing what to do.");
                Copy("help-views","LEFT  Structure: the selected physical points.\nMIDDLE  Selection window: where membership is decided.\nRIGHT  Finite diffraction: the calculated pattern from the selected structure.");
                Copy("help-controls","Drag the cyan knob, or use arrow keys after clicking a diagram. Inspect opens Model / Point / Peak numerical values; Precision controls edits scientific offsets. View... instead edits camera center and span, with aspect ratio locked. Wheel zoom works in every view; middle-drag pans directly; right-drag rotates Structure. Grid axes use dimensionless model coordinates or model reciprocal units. Home restores all views. Export CSV saves checksum-linked scientific tables locally.");
                Copy("help-shortcuts","Ctrl+S  Save session\nCtrl+O  Saved sessions\nCtrl+Shift+S  Screenshot + state\nG  Grid   /   F  Explore   /   N  Next step\nP  Pause or resume   /   Esc  Menu or close dialog\nTab / Shift+Tab  Move focus   /   Enter  Activate\nGuided tour: Start screen or Menu");
                Copy("help-colors","Teal or blue: selected. Grey: outside. White: the same focused identity. Solid rings: added; dashed rings: removed. IN / OUT means selected / not selected, not correct / incorrect.");
                Copy("help-computation","After a change, the pattern is Updating until the current revision is Ready. Never treat an updating display as a completed result. Pausing or opening a dialog stops input and guided motion; a calculation already in flight may finish.");
            }
        }
        public void RefreshModal() {if(ModalOpen) ShowModal(Modal);}
        public void AlignViewHeadings(Camera camera)
        {
            if(viewHeadings.Count!=3) return;
            var origins=new[]{Demo.Lab.View.PhysicalOrigin,Demo.Lab.View.InternalOrigin,Demo.Instrument.Reciprocal.Origin};
            for(int i=0;i<viewHeadings.Count;i++)
            {
                var label=viewHeadings[i];var parent=(RectTransform)label.transform.parent;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,camera.WorldToScreenPoint(origins[i].position),null,out var point);
                label.rectTransform.anchorMin=label.rectTransform.anchorMax=label.rectTransform.pivot=new Vector2(.5f,.5f);
                label.rectTransform.anchoredPosition=new Vector2(point.x-parent.rect.center.x,0);
                if(Workspace.FocusView!=0) label.rectTransform.anchoredPosition=Vector2.zero;
                label.rectTransform.sizeDelta=new Vector2(parent.rect.width/3-12,44);
            }
        }
        public void CloseModal()
        {
            if(modalFade!=null) StopCoroutine(modalFade);modalFade=null;Modal="";
            if(chromeGroup!=null) chromeGroup.alpha=1;
            if(EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(null);nextLive=0;
            if(modalLayer!=null&&!Workspace.Preferences.ReducedMotion)
            {Transitioning=true;modalLayer.GetComponent<CanvasGroup>().interactable=false;modalFade=StartCoroutine(Dismiss(modalLayer));}
            else FinishDismiss();
        }
        IEnumerator Dismiss(RectTransform retiring)
        {
            var group=retiring.GetComponent<CanvasGroup>();float alpha=group.alpha;
            for(float t=0;t<.14f;t+=Time.unscaledDeltaTime) {group.alpha=alpha*(1-DesktopMotion.Ease(t/.14f));yield return null;}
            FinishDismiss();
        }
        void FinishDismiss()
        {
            Retire(modalLayer);modalLayer=null;modalFade=null;Transitioning=false;
            if(chromeGroup!=null) chromeGroup.interactable=chromeGroup.blocksRaycasts=true;
        }
        void SelectFirstModal()
        {
            var buttons=modalLayer.GetComponentsInChildren<Button>();
            var first=(Modal=="Quit"||Modal=="Confirm")?buttons.FirstOrDefault(b=>b.name=="confirm-cancel"):buttons.FirstOrDefault(b=>b.interactable&&b.name!="confirm-discard");
            if(first!=null&&EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }
        public void FocusNext(int direction)
        {
            var choices=(ModalOpen?modalLayer.GetComponentsInChildren<Selectable>():navigation.ToArray()).Where(s=>s!=null&&s.gameObject.activeInHierarchy&&s.IsInteractable()).ToArray();
            if(choices.Length==0||EventSystem.current==null) return;var selected=EventSystem.current.currentSelectedGameObject;
            int index=Array.FindIndex(choices,s=>s.gameObject==selected);index=(index+direction+choices.Length)%choices.Length;
            EventSystem.current.SetSelectedGameObject(choices[index].gameObject);EnsureVisible(choices[index].GetComponent<RectTransform>());
        }
        static void EnsureVisible(RectTransform target)
        {
            var scroll=target.GetComponentInParent<ScrollRect>();if(scroll==null) return;Canvas.ForceUpdateCanvases();
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,target);var view=scroll.viewport.rect;
            float delta=bounds.max.y>view.yMax?bounds.max.y-view.yMax:bounds.min.y<view.yMin?bounds.min.y-view.yMin:0;
            var position=scroll.content.anchoredPosition;position.y=Mathf.Clamp(position.y+delta,0,Mathf.Max(0,scroll.content.rect.height-view.height));scroll.content.anchoredPosition=position;
        }
        public void RefreshLive()
        {
            if(Canvas==null) return;if(lastWidth!=Screen.width||lastHeight!=Screen.height) {Restyle();return;}
            if(scaleReadout!=null&&Demo.Lab.State!=null)
            {
                grid.Refresh();
                var camera=Demo.Instrument.Camera;
                float zoom=Workspace.ViewTransitioning?Workspace.Zoom*Workspace.ViewSpan.y/(2*camera.orthographicSize/Workspace.ViewUnitScale):Workspace.Zoom;
                bool overview=Workspace.FocusView==0&&Workspace.GridVisible;
                scaleReadout.alignment=overview?TextAnchor.UpperRight:TextAnchor.LowerRight;
                scaleReadout.rectTransform.anchorMin=overview?new Vector2(1,1):Vector2.zero;scaleReadout.rectTransform.anchorMax=overview?Vector2.one:new Vector2(1,0);
                scaleReadout.rectTransform.pivot=overview?Vector2.one:Vector2.zero;
                scaleReadout.rectTransform.offsetMin=overview?new Vector2(-120,-36):new Vector2(12,4);scaleReadout.rectTransform.offsetMax=overview?new Vector2(-12,-4):new Vector2(-12,76);
                DesktopSemanticText.Apply(scaleReadout,Workspace.FocusView==0?(overview?$"{zoom*100:F0}%":$"{zoom*100:F0}% / Independent units when grid is on"):$"{zoom*100:F0}% / H {2*camera.orthographicSize*camera.aspect/Workspace.ViewUnitScale:G5} / V {2*camera.orthographicSize/Workspace.ViewUnitScale:G5}\n{(Workspace.FocusView==1?"x/y projection, unitless":Workspace.FocusView==2?"u/v, unitless":"qx/qy, model reciprocal")}{(Workspace.GridVisible?$"\nminor {grid.MinorInterval:G4} / major {grid.Interval:G4}":"")}",Workspace.Preferences.HighContrast,Workspace.Preferences.HighContrast?Color.white:DesktopTheme.Muted);
                DesktopTheme.StyleButton(gridToggle,Workspace.GridVisible,false,Workspace.Preferences.HighContrast);
            }
            if(Time.unscaledTime<nextLive) return;nextLive=Time.unscaledTime+.10f;
            RefreshInputNotice();RefreshDiffractionLegend();
            diffractionAppearance.gameObject.SetActive(Workspace.FocusView==0||Workspace.FocusView==3);
            var model=Demo.Lab.State?.Model;bool ready=Demo.Instrument.Reciprocal.Current;
            dirty.text=Demo.UnsavedChanges?"Unsaved changes":"All changes saved";dirty.color=Demo.UnsavedChanges?new Color(1,.77f,.43f):DesktopTheme.Muted;
            feedback.text=Demo.Lifecycle.Suspended?"Paused while this window is inactive.":Workspace.Paused?"Paused. Resume when you are ready.":Workspace.Feedback;
            var generator=feedback.cachedTextGeneratorForLayout;var settings=feedback.GetGenerationSettings(new Vector2(10000,feedback.rectTransform.rect.height));
            string message=feedback.text;
            while(message.Length>12&&generator.GetPreferredWidth(message,settings)/feedback.pixelsPerUnit>feedback.rectTransform.rect.width)
                message=message.Substring(0,message.Length-4)+"...";
            feedback.text=message;
            pause.GetComponentInChildren<Text>().text=Workspace.Paused?"Resume":"Pause";save.interactable=Workspace.CanSave;
            for(int i=0;i<focus.Count;i++) DesktopTheme.StyleButton(focus[i],Workspace.FocusView==i,false,Workspace.Preferences.HighContrast);
            var headingOrigins=new[]{Demo.Lab.View.PhysicalOrigin,Demo.Lab.View.InternalOrigin,Demo.Instrument.Reciprocal.Origin};
            for(int i=0;i<viewHeadings.Count;i++)
            {
                float x=Demo.Instrument.Camera.WorldToScreenPoint(headingOrigins[i].position).x;var bounds=Demo.Instrument.Camera.pixelRect;
                viewHeadings[i].gameObject.SetActive(Workspace.FocusView==i+1||Workspace.FocusView==0&&x>=bounds.xMin+60&&x<=bounds.xMax-60);
            }
            before.gameObject.SetActive(Demo.Menu.HasExample);before.interactable=ready&&!Workspace.BlocksInput;
            if(Demo.Menu.HasExample) before.GetComponentInChildren<Text>().text=Demo.Menu.PrimaryText;
            primary.GetComponentInChildren<Text>().text=Demo.Guided&&Demo.Step<9?"Next step":"Explore freely";
            primary.gameObject.SetActive(Demo.Guided||Demo.Menu.HasExample);
            primary.interactable=!Workspace.BlocksInput&&!Demo.PathRunning&&(!Demo.Guided||Demo.Step!=7||ready);
            lessonTitle.text=Demo.Guided?$"{Demo.Step+1:00} / 10   {TourTitles[Demo.Step]}":Demo.Menu.HasExample?Demo.Menu.ExampleHeading:"Your selection, your pace";
            bool explaining=Demo.Guided||Demo.Menu.HasExample;lessonTitle.gameObject.SetActive(explaining);
            lessonText.text=Workspace.Paused?Workspace.ManipulationNotice:Demo.Guided?TourCopy[Demo.Step]:Demo.Menu.HasExample?Demo.Menu.ExampleInstruction:Workspace.ManipulationNotice.Length>0?Workspace.ManipulationNotice:"Drag the cyan control. Click a point or peak to inspect its values.";
            float actionWidth=(Demo.Menu.HasExample?4:Demo.Guided?3:2)*(Workspace.Preferences.TextSize==2?210:180);
            lessonActions.sizeDelta=new Vector2(actionWidth,Mathf.Max(48,ButtonHeight));
            var textOffset=lessonText.rectTransform.offsetMax;textOffset.y=explaining?-62:-20;textOffset.x=explaining?-22:-(actionWidth+44);lessonText.rectTransform.offsetMax=textOffset;
            var textBottom=lessonText.rectTransform.offsetMin;textBottom.y=explaining?80:20;lessonText.rectTransform.offsetMin=textBottom;
            float needed=Mathf.Max(explaining?lessonBaseHeight:88,DesktopTheme.Measure(lessonText.text,lessonText.fontSize,lessonText.rectTransform.rect.width)+(explaining?150:40));
            lessonPanel.sizeDelta=new Vector2(0,needed);var offset=DiagramArea.offsetMin;offset.y=needed+44;DiagramArea.offsetMin=offset;
            viewHint.rectTransform.offsetMin=new Vector2(12,needed+4);viewHint.rectTransform.offsetMax=new Vector2(-12,needed+36);
            DesktopSemanticText.Apply(viewHint,$"{model?.SelectedCount??0} selected  /  {model?.Edges.Length??0} connections  /  {(ready?"Diffraction ready":"Updating diffraction")}",Workspace.Preferences.HighContrast,Workspace.Preferences.HighContrast?Color.white:DesktopTheme.Muted);
            RefreshInspector();
        }
        static readonly string[] TourTitles={"Three views, one model","One identity in two views","A window decides membership","Which points belong?","The selected structure","Connections follow selection","Watch a short change","Read the before / after","The calculated diffraction","Now make your own change"};
        static readonly string[] TourCopy={
            "A selection rule decides which points form a structure. This tour links that rule to the structure and its calculated diffraction. Choose Next step at your own pace.",
            "The white marker is the same exact point in both views. It has a physical position and separate selection coordinates. These are linked views, not separate objects.",
            "The polygon is a fixed selection window. It decides which candidates are included. Nothing needs to be dragged yet.",
            "Teal or blue points are selected; grey points are outside. This is a canonical mathematical model, not a refined material.",
            "Selected points form the physical structure. Their positions are fixed; changing the selection rule can make points enter or leave.",
            "A line joins selected neighbours of the parent lattice. When a point leaves, its connections disappear. These are not chemical bonds.",
            "Watch the cyan knob shift selection coordinates while the window stays fixed. The next step becomes available when this short example finishes.",
            "Solid rings mark additions; dashed rings mark removals. The same comparison appears in both views. This compares sampled states, not every intervening crossing.",
            "Finite diffraction is calculated from the selected structure. Wait for Ready after a change. This is not a measured X-ray image.",
            "Choose Explore freely. Drag the cyan knob, use X / Y controls, or try a worked example. Save a session or take a screenshot when the pattern is Ready."
        };
    }
}
