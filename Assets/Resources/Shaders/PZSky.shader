Shader "PeopleZ/Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PZCommon.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V2F vert(A v)
            {
                V2F o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                return half4(PZSkyColor(normalize(i.dir), 1.0), 1);
            }
            ENDHLSL
        }
    }
}
