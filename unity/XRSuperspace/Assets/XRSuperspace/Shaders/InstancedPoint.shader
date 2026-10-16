Shader "XRSuperspace/InstancedPoint"
{
    Properties { _InstanceColor("Color",Color)=(0.15,0.85,0.75,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4,_InstanceColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            Varyings Vert(Attributes input) { Varyings output;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.normalWS=TransformObjectToWorldNormal(input.normalOS);return output; }
            half4 Frag(Varyings input):SV_Target { UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);float shade=0.5+0.5*saturate(dot(normalize(input.normalWS),normalize(float3(-0.3,0.6,-1))));return half4(UNITY_ACCESS_INSTANCED_PROP(Props,_InstanceColor).rgb*shade,1); }
            ENDHLSL
        }
    }
}
