Shader "XRSuperspace/GpuPoint"
{
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:Setup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Candidate {float4 physical;float2 uv;uint allowed;uint padding;};
            StructuredBuffer<Candidate> _Candidates;
            StructuredBuffer<uint> _Rows,_States,_Active,_Previous;
            float4 _Origin,_Scales,_Phi,_SelectedColor,_RejectedColor,_BoundaryColor;
            float _Radius,_Fade;
            int _Internal,_Context,_SelectedRow,_BoundaryPoints;
            void Setup() {} // Centers are explicit world-space buffer data.
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION;float4 color:TEXCOORD0;float3 normal:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
            V Vert(A i)
            {
                V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                uint row=_Rows[unity_InstanceID];Candidate c=_Candidates[row];uint state=_States[row];
                float3 center=_Internal!=0?float3((c.uv+_Phi.xy)*_Scales.w,0):c.physical.xyz*_Scales.xyz;
                o.color=state==2?_BoundaryColor:state==1?_SelectedColor:_RejectedColor;
                if(_Internal==0&&_Context==0) o.color.a*=lerp((float)_Previous[row],(float)_Active[row],_Fade);
                if(_BoundaryPoints!=0&&state==2) o.color.a=1;
                bool highlight=(int)row==_SelectedRow;if(highlight) o.color=float4(1,1,1,1);
                o.positionCS=TransformWorldToHClip(_Origin.xyz+center+i.positionOS.xyz*_Radius*(highlight?1.7:1));o.normal=i.normalOS;return o;
            }
            half4 Frag(V i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return half4(i.color.rgb*(.5+.5*saturate(dot(normalize(i.normal),normalize(float3(-.3,.6,-1))))),i.color.a);}
            ENDHLSL
        }
    }
}
