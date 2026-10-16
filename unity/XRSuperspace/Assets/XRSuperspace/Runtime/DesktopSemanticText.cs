using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    // IDE-like presentation, not a parser or formatter for scientific data.
    // Input strings, clipboard text, session JSON and CSV remain untouched.
    public static class DesktopSemanticText
    {
        public enum Role { Variable, Number, Identity, Ready, Warning, Unit, Punctuation, Section }
        public static Color Tint(Role role,bool contrast=false)
        {
            string hex=role switch {
                Role.Variable=>contrast?"AEDFFF":"8CC7F5",
                Role.Number=>contrast?"FFE0AD":"E8C088",
                Role.Identity=>contrast?"E3D5FF":"C2B1F2",
                Role.Ready=>contrast?"BCF5D6":"8CD5B6",
                Role.Warning=>contrast?"FFE0AD":"F1C275",
                Role.Punctuation=>contrast?"C8D8E8":"ADC3D8",
                Role.Section=>contrast?"FFFFFF":"CDD7EB",
                _=>contrast?"D2E2F2":"B5C9DC" };
            ColorUtility.TryParseHtmlString("#"+hex,out var color);return color;
        }
        static readonly Regex Tokens=new Regex(@"(?<hash>\b[0-9a-fA-F]{64}\b)|(?<number>(?<![\p{L}\p{N}_])[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?(?![\p{L}\p{N}_]))|(?<word>[\p{L}_][\p{L}\p{N}_²]*)|(?<punct>[=()\[\]{},;/+])",RegexOptions.Compiled);
        static readonly HashSet<string> Variables=new HashSet<string>(StringComparer.Ordinal) {"x","y","z","u","v","n","h","q","qx","qy","qz","A","I","N","H","V","r","i","Real","Imag","Phi","phi","φx","φy","cx","cy","width","height"};
        public static string Legend(bool contrast=false)=>Colorize("variable",Role.Variable,contrast)+"  /  "+Colorize("number",Role.Number,contrast)+"  /  "+Colorize("identity",Role.Identity,contrast);
        static readonly HashSet<string> Sections=new HashSet<string>(StringComparer.Ordinal) {"Exact lattice identity","Physical coordinates","Selection coordinates + offset","Reciprocal vector","Complex amplitude","Signed window distance"};
        static string Colorize(string value,Role role,bool contrast)=>"<color=#"+ColorUtility.ToHtmlStringRGB(Tint(role,contrast))+">"+value+"</color>";
        public static string Format(string plain,bool contrast=false)
        {
            if(string.IsNullOrEmpty(plain)) return "";
            // uGUI rich text has no HTML escape mechanism. Display untrusted
            // angle delimiters literally as angle glyphs, never executable tags.
            string safe=plain.Replace('<','‹').Replace('>','›');var output=new StringBuilder();
            var lines=safe.Split('\n');
            for(int index=0;index<lines.Length;index++)
            {
                if(index>0) output.Append('\n');string line=lines[index];
                if(Sections.Contains(line.Trim())) {output.Append(Colorize(line,Role.Section,contrast));continue;}
                if(line.StartsWith("Coordinates are",StringComparison.Ordinal)||line.StartsWith("Dimensionless model",StringComparison.Ordinal)||line.StartsWith("Calculated",StringComparison.Ordinal)||line.StartsWith("Unit weights;",StringComparison.Ordinal))
                {output.Append(Colorize(line,Role.Unit,contrast));continue;}
                bool identity=line.TrimStart().StartsWith("n =",StringComparison.Ordinal)||line.TrimStart().StartsWith("h =",StringComparison.Ordinal);
                int at=0;
                foreach(Match token in Tokens.Matches(line))
                {
                    output.Append(line,at,token.Index-at);Role? role=null;string value=token.Value;
                    if(token.Groups["hash"].Success) role=Role.Identity;
                    else if(token.Groups["number"].Success) role=identity?Role.Identity:Role.Number;
                    else if(token.Groups["punct"].Success) role=Role.Punctuation;
                    else if(Variables.Contains(value)) role=Role.Variable;
                    else if(value.Equals("Ready",StringComparison.OrdinalIgnoreCase)||value=="CURRENT"||value=="Yes"||value=="Selected") role=Role.Ready;
                    else if(value.Equals("Updating",StringComparison.OrdinalIgnoreCase)||value=="STALE"||value=="Boundary"||value=="Paused") role=Role.Warning;
                    else if(value=="No"||value=="Rejected"||value=="dimensionless"||value=="units"||value=="reciprocal") role=Role.Unit;
                    output.Append(role.HasValue?Colorize(value,role.Value,contrast):value);at=token.Index+token.Length;
                }
                output.Append(line,at,line.Length-at);
            }
            return output.ToString();
        }
        public static string Present(string plain,bool contrast=false,Color? foreground=null)
        {
            var color=foreground??(contrast?Color.white:DesktopTheme.Text);
            return "<color=#"+ColorUtility.ToHtmlStringRGB(color)+">"+Format(plain,contrast)+"</color>";
        }
        public static void Apply(Text text,string plain,bool contrast=false,Color? foreground=null)
        {
            // Keep the component foreground neutral and express ordinary prose
            // as an outer tag. Semantic roles do not depend on a label's tint
            // policy; antialiased edge pixels still blend with the background.
            text.color=Color.white;text.supportRichText=true;text.text=Present(plain,contrast,foreground);
        }
    }
}
