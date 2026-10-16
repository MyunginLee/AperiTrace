using System;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    public static class DesktopTheme
    {
        public static readonly Color Background=new Color(.028f,.047f,.067f);
        public static readonly Color Panel=new Color(.05f,.078f,.105f);
        public static readonly Color Surface=new Color(.10f,.15f,.21f);
        public static readonly Color Border=new Color(.31f,.40f,.47f);
        public static readonly Color Text=new Color(.94f,.97f,1);
        public static readonly Color Muted=new Color(.72f,.81f,.88f);
        public static readonly Color Teal=new Color(.06f,.43f,.40f);
        public static readonly Color Accent=new Color(.12f,.29f,.50f);
        public static RectTransform Node(string id,Transform parent)
        {
            var rect=new GameObject(id,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.gameObject.layer=5;return rect;
        }
        public static void Fill(RectTransform rect,float left=0,float bottom=0,float right=0,float top=0)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(-right,-top);}
        public static void Box(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        public static Image Paint(RectTransform node,Color color,bool hit=false,float radius=0)
        {var image=node.gameObject.AddComponent<DesktopSurface>();image.Radius=radius;image.color=color;image.raycastTarget=hit;return image;}
        public static Text Label(string id,Transform parent,string text,int size,int weight=0)
        {
            var label=Node(id,parent).gameObject.AddComponent<Text>();QuestUiTheme.Style(label,size,weight);label.color=Text;label.text=text;return label;
        }
        public static float Measure(string text,int size,float width,bool rich=false)
        {
            var generator=new TextGenerator();var settings=new TextGenerationSettings {font=QuestUiTheme.Font(),fontSize=size,scaleFactor=1,lineSpacing=1.12f,
                richText=rich,textAnchor=TextAnchor.UpperLeft,generationExtents=new Vector2(Mathf.Max(40,width),0),horizontalOverflow=HorizontalWrapMode.Wrap,verticalOverflow=VerticalWrapMode.Overflow};
            return Mathf.Ceil(generator.GetPreferredHeight(text,settings))+6;
        }
        public static Button Button(string id,Transform parent,string text,Action action,int size,bool primary=false,bool destructive=false,bool contrast=false)
        {
            var node=Node(id,parent);Paint(node,Surface,true,9);var button=node.gameObject.AddComponent<Button>();
            StyleButton(button,primary,destructive,contrast);
            // Regular face for ordinary actions; medium only for a primary CTA.
            // QuestFocusFeedback supplies ONE focus ring. Do not stack another
            // Outline on the same image or add effects to glyphs.
            var label=Label(id+"-label",node,text,size,primary?1:0);DesktopSemanticText.Apply(label,text,contrast);Fill(label.rectTransform,16,9,16,9);label.alignment=TextAnchor.MiddleLeft;
            button.onClick.AddListener(()=>action());return button;
        }
        public static void StyleButton(Button button,bool primary=false,bool destructive=false,bool contrast=false)
        {
            QuestUiTheme.Button(button,primary,destructive,contrast);
            button.transition=Selectable.Transition.None;button.targetGraphic.CrossFadeColor(Color.white,0,true,true);
            if(button.GetComponent<DesktopActionFeedback>()==null) button.gameObject.AddComponent<DesktopActionFeedback>();
            if(primary&&!destructive) button.GetComponent<Image>().color=Accent;
        }
    }
}
