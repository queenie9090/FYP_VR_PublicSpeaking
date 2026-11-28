Shader "FYP/NPC Speaker Border"
{
    Properties { _Tint ("Tint", Color) = (0,0,0,0) }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END
            struct Input { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 positionCS : SV_POSITION; float2 edge : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Output vert(Input v)
            {
                Output o = (Output)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = float4(v.positionOS.xy, .5, 1);
                o.edge = v.positionOS.xy;
                return o;
            }
            half4 frag(Output i) : SV_Target
            {
                half fade = smoothstep(.92, 1., max(abs(i.edge.x),abs(i.edge.y)));
                return half4(_Tint.rgb, _Tint.a * fade);
            }
            ENDHLSL
        }
    }
}
