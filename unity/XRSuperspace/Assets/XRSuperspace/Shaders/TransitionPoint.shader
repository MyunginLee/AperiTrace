Shader "XRSuperspace/TransitionPoint"
{
    Properties { _InstanceColor("Color",Color)=(.15,.85,.75,1) _PresentationAlpha("Presentation opacity",Range(0,1))=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
            UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4,_InstanceColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            float _PresentationAlpha;
            V Vert(A i) {V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.normalWS=TransformObjectToWorldNormal(i.normalOS);return o;}
            half4 Frag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float4 c=UNITY_ACCESS_INSTANCED_PROP(Props,_InstanceColor);return half4(c.rgb*(.5+.5*saturate(dot(normalize(i.normalWS),normalize(float3(-.3,.6,-1))))),c.a*_PresentationAlpha);}
            ENDHLSL
        }
    }
}
