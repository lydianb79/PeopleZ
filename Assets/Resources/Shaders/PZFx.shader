Shader "PeopleZ/Fx"
{
    Properties
    {
        _Dst ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha [_Dst]
            ZWrite Off Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Dst;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            V2F vert(A v)
            {
                V2F o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float a = saturate(1.0 - dot(d, d));
                a *= a;
                return half4(i.color.rgb, i.color.a * a);
            }
            ENDHLSL
        }
    }
}
