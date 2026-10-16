using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Presentation only. Never modifies Values, selection, crop, sampling or sums.
    [Serializable]
    public sealed class DiffractionDisplay
    {
        public string Palette="viridis",Scale="sqrt";
        public bool Invert,AutoRange,HideCenter;
        public double Minimum=0,Maximum=1,VisibleMinimum=0,VisibleMaximum=1,QMinimum=0,QMaximum=100;
        public string Low="#14213d",Middle="#2ec4b6",High="#fff3b0";
        public DiffractionDisplay Copy()=>(DiffractionDisplay)MemberwiseClone();
        public void Validate()
        {
            if(!DiffractionColors.Contains(Palette)) throw new ArgumentException("Choose an available colormap.");
            if(Scale!="linear"&&Scale!="sqrt"&&Scale!="log") throw new ArgumentException("Choose Linear, Square root or Log10.");
            foreach(double n in new[]{Minimum,Maximum,VisibleMinimum,VisibleMaximum,QMinimum,QMaximum})
                if(double.IsNaN(n)||double.IsInfinity(n)) throw new ArgumentException("Display ranges must be finite.");
            if(Minimum<0||Maximum>1||Minimum>=Maximum||Scale=="log"&&Minimum<=0) throw new ArgumentException("Color range requires 0 <= min < max <= 1. Log10 needs a positive minimum.");
            if(VisibleMinimum<0||VisibleMaximum>1||VisibleMinimum>VisibleMaximum||QMinimum<0||QMinimum>QMaximum||QMaximum>100)
                throw new ArgumentException("Filters require 0 <= intensity min <= max <= 1 and 0 <= |q| min <= max <= 100.");
            foreach(string hex in new[]{Low,Middle,High}) if(!ValidHex(hex)) throw new ArgumentException("Custom colors require #RRGGBB.");
        }
        static bool ValidHex(string value)=>value!=null&&value.Length==7&&value[0]=='#'&&uint.TryParse(value.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _);
        public bool Visible(double intensity,Double3 q)
        {
            double r=Math.Sqrt(q.X*q.X+q.Y*q.Y+q.Z*q.Z);
            return !double.IsNaN(intensity)&&!double.IsInfinity(intensity)&&intensity>=VisibleMinimum&&intensity<=VisibleMaximum&&r>=QMinimum&&r<=QMaximum&&!(HideCenter&&r<1e-10);
        }
        public (double min,double max) Range(double[][] values,ReciprocalPeak[] peaks,double threshold=0)
        {
            double low=Minimum,high=Maximum;
            if(AutoRange&&values!=null)
            {
                low=double.PositiveInfinity;high=double.NegativeInfinity;
                for(int i=0;i<Math.Min(values.Length,peaks.Length);i++)
                    if(values[i][3]>=threshold&&Visible(values[i][3],peaks[i].Q)&&(Scale!="log"||values[i][3]>0)) {low=Math.Min(low,values[i][3]);high=Math.Max(high,values[i][3]);}
                // Empty/constant data have no inferable range. Use the explicit range.
                if(!(low<high)) {low=Minimum;high=Maximum;}
            }
            return (low,high);
        }
        public double Normalize(double value,double low,double high)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||!(low<high)) throw new ArgumentException("Invalid color normalization.");
            value=Math.Max(low,Math.Min(high,value));
            double t=Scale=="log"?(Math.Log10(value)-Math.Log10(low))/(Math.Log10(high)-Math.Log10(low)):Scale=="sqrt"?(Math.Sqrt(value)-Math.Sqrt(low))/(Math.Sqrt(high)-Math.Sqrt(low)):(value-low)/(high-low);
            t=Math.Max(0,Math.Min(1,t));return Invert?1-t:t;
        }
        public Color ColorAt(double value,double low,double high)=>Ramp(Normalize(value,low,high));
        public Color Ramp(double t)
        {
            if(Palette!="Custom") return DiffractionColors.Sample(Palette,(float)t);
            ColorUtility.TryParseHtmlString(Low,out var a);ColorUtility.TryParseHtmlString(Middle,out var b);ColorUtility.TryParseHtmlString(High,out var c);
            return t<.5?Color.Lerp(a,b,(float)t*2):Color.Lerp(b,c,(float)t*2-1);
        }
    }
    public static class DiffractionColors
    {
        public static readonly string[] Names={"viridis","cividis","plasma","inferno","magma","gray","Blues","YlGnBu","YlOrRd","hot","copper","bone","turbo","Spectral","coolwarm","RdBu","twilight","hsv","Custom"};
        static Dictionary<string,Color[]> tables;
        public static bool Contains(string name)=>Array.IndexOf(Names,name)>=0;
        public static Color Sample(string name,float value)
        {
            if(tables==null)
            {
                tables=new Dictionary<string,Color[]>();
                var asset=Resources.Load<TextAsset>("DiffractionPalettes");
                if(asset==null) throw new InvalidOperationException("Bundled colormap tables missing.");
                foreach(var entry in (JObject)JObject.Parse(asset.text)["palettes"])
                {
                    var rows=(JArray)entry.Value;var colors=new Color[rows.Count];
                    for(int i=0;i<colors.Length;i++) if(!ColorUtility.TryParseHtmlString((string)rows[i],out colors[i])) throw new FormatException("Invalid bundled RGB.");
                    if(colors.Length!=256) throw new FormatException("Colormap requires 256 samples.");tables.Add(entry.Key,colors);
                }
            }
            if(!tables.TryGetValue(name,out var table)) throw new ArgumentException("Unknown colormap.");
            float row=Mathf.Clamp01(value)*255;int lo=Mathf.FloorToInt(row);return Color.Lerp(table[lo],table[Mathf.Min(lo+1,255)],row-lo);
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DiffractionColorbar : MaskableGraphic
    {
        public DiffractionDisplay Settings;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(Settings==null) return;var r=rectTransform.rect;
            for(int i=0;i<128;i++)
            {
                float a=i/128f,b=(i+1)/128f;int n=mesh.currentVertCount;
                Color ca=Settings.Ramp(Settings.Invert?1-a:a),cb=Settings.Ramp(Settings.Invert?1-b:b);
                mesh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,a),r.yMin),ca,Vector2.zero);
                mesh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,a),r.yMax),ca,Vector2.zero);
                mesh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,b),r.yMax),cb,Vector2.zero);
                mesh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,b),r.yMin),cb,Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
            }
        }
    }
}
