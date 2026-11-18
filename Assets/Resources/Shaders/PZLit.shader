Shader "PeopleZ/Lit"
{
    Properties
    {
        _Team ("Team", Color) = (1,1,1,1)
        _Glow ("Glow", Float) = 2
        _Sway ("Sway", Float) = 0
        _Walk ("Walk", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "PZCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _Team;
        float _Glow;
        float _Sway;
        float _Walk;
        CBUFFER_END
        struct A
        {
            float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 PZWorldPos(A v)
        {
            float3 p = TransformObjectToWorld(v.positionOS.xyz);
            float w = v.color.a * _Sway;
            float gust = 0.6 + 0.4 * sin(_Time.y * 0.5 + p.x * 0.08 + p.z * 0.05);
            p.xz += w * gust * float2(sin(_Time.y * 1.7 + p.x * 0.9 + p.z * 0.6), cos(_Time.y * 1.3 + p.z * 0.8)) * 0.075;
            // walking waddle: lean the body from the feet up, phase per instance
            float3 org = TransformObjectToWorld(float3(0, 0, 0));
            float3 right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
            p += right * (v.positionOS.y * sin(_Time.y * 11.0 + org.x * 7.3 + org.z * 5.1) * 0.10 * _Walk);
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            struct V2F { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; float4 color : COLOR; float2 uv : TEXCOORD2; };
            V2F vert(A v)
            {
                V2F o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.posWS = PZWorldPos(v);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float3 N = normalize(i.nWS);
                float3 albedo = lerp(i.color.rgb, i.color.rgb * _Team.rgb, saturate(i.uv.x));
                float3 em = i.color.rgb * i.uv.y * _Glow * _PZGlow;
                float foliage = saturate(i.color.a * 4.0) * _Sway;
                albedo *= 0.9 + 0.2 * pzNoise(i.posWS.xz * 7.0 + i.posWS.y * 5.0);
                float3 c = PZShade(albedo, N, i.posWS, i.positionCS, lerp(0.22, 0.1, foliage), em, foliage);
                c = PZFog(c, i.posWS);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float4 vert(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return PZShadowClip(PZWorldPos(v), TransformObjectToWorldNormal(v.normalOS));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float4 vert(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformWorldToHClip(PZWorldPos(v));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
