Shader "Hidden/AperiTrace/DesktopCaptureComposite"
{
    Properties { _MainTex ("UI", 2D) = "black" {} _SceneTex ("Scene", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _SceneTex;
            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 ui=tex2D(_MainTex,input.uv);
                fixed4 scene=tex2D(_SceneTex,input.uv);
                return fixed4(ui.rgb+scene.rgb*(1-ui.a),1);
            }
            ENDCG
        }
    }
}
