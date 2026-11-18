Shader "PeopleZ/Terrain"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "PZCommon.hlsl"
        struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            struct V2F { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; float4 color : COLOR; };
            V2F vert(A v)
            {
                V2F o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float3 Ng = normalize(i.nWS);
                float2 p = i.posWS.xz;
                float h = i.posWS.y;
                float t = _Time.y;
                float dist = distance(_WorldSpaceCameraPos, i.posWS);
                float near = saturate(1.0 - dist / 60.0);
                float4 mk = SAMPLE_TEXTURE2D(_PZMask, sampler_LinearClamp, (p + 0.5) / 129.0);

                float n = pzFbm(p * 0.35);
                float n2 = pzNoise(p * 3.1);
                float nb = pzFbm(p * 0.06 + 11.0);
                float slope = 1.0 - Ng.y;

                float3 sand = float3(0.80, 0.70, 0.47);
                float3 grass = lerp(float3(0.10, 0.30, 0.06), float3(0.30, 0.47, 0.11), n);
                grass = lerp(grass, float3(0.47, 0.46, 0.16), smoothstep(0.5, 0.75, nb) * 0.55);
                grass *= 0.86 + 0.28 * pzNoise(p * float2(23.0, 31.0));
                float3 dirt = float3(0.36, 0.27, 0.17);
                float strata = pzNoise(float2(h * 9.0 + n2 * 0.8, p.x * 0.3 + p.y * 0.2));
                float3 rock = lerp(float3(0.30, 0.28, 0.27), float3(0.52, 0.49, 0.45), strata);
                float3 snow = float3(0.93, 0.95, 1.0);

                float hh = h + (n - 0.5) * 0.35;
                float grassM = smoothstep(_PZWaterY + 0.14, _PZWaterY + 0.5, hh);
                float3 col = lerp(sand, grass, grassM);
                float dirtM = smoothstep(0.035, 0.11, slope + (n2 - 0.5) * 0.04);
                col = lerp(col, dirt * (0.8 + 0.4 * n2), dirtM * 0.6);
                float rockM = saturate(smoothstep(0.13, 0.24, slope + (n2 - 0.5) * 0.05) + smoothstep(3.6, 4.5, hh + slope * 3.0));
                col = lerp(col, rock, rockM);
                float snowM = smoothstep(4.9, 5.4, hh) * smoothstep(0.2, 0.05, slope);
                col = lerp(col, snow, snowM);
                float smooth = lerp(0.08, 0.3, rockM);
                smooth = lerp(smooth, 0.55, snowM);

                // wild flowers
                float2 fq = p * 11.0;
                float fh = pzHash(floor(fq));
                float fl = step(0.972, fh) * smoothstep(0.34, 0.14, length(frac(fq) - 0.5)) * grassM * (1.0 - dirtM) * (1.0 - rockM) * near;
                float3 fcol = fh > 0.992 ? float3(0.95, 0.35, 0.55) : (fh > 0.982 ? float3(1.0, 0.82, 0.15) : float3(0.97, 0.97, 0.92));

                // farmland
                float2 cell = floor(p);
                float ch = pzHash(cell * 0.37 + 3.1);
                float rowPhase = ((ch > 0.5) ? p.x : p.y) * 25.13;
                float rows = sin(rowPhase);
                float cropK = frac(ch * 7.3);
                float3 crop = cropK < 0.4 ? float3(0.72, 0.58, 0.16) : (cropK < 0.75 ? float3(0.26, 0.48, 0.10) : float3(0.50, 0.56, 0.14));
                crop *= 0.80 + 0.3 * pzNoise(p * 14.0);
                crop = lerp(crop, float3(0.30, 0.21, 0.12), 0.6 * smoothstep(0.0, 0.9, rows));
                float2 gc = abs(frac(p) - 0.5);
                float path = smoothstep(0.43, 0.49, max(gc.x, gc.y));
                crop = lerp(crop, float3(0.46, 0.40, 0.26), path * 0.45);
                float fm = smoothstep(0.4, 0.8, max(mk.r, mk.b)) * (1.0 - rockM);
                col = lerp(col, crop, fm * 0.95);
                col = lerp(col, fcol, fl * (1.0 - fm));

                // swamp
                float sw = smoothstep(0.3, 0.7, mk.g + (n2 - 0.5) * 0.3);
                float3 swc = lerp(float3(0.07, 0.10, 0.04), float3(0.17, 0.20, 0.06), pzNoise(p * 5.0 + t * 0.1));
                col = lerp(col, swc, sw);
                smooth = lerp(smooth, 0.9, sw);

                // scorch + embers
                float sc = saturate(mk.a * (0.7 + n2 * 0.6));
                col = lerp(col, float3(0.035, 0.03, 0.028), sc * 0.92);
                float3 em = float3(2.5, 0.6, 0.08) * smoothstep(0.86, 0.98, mk.a) * smoothstep(0.55, 0.8, pzNoise(p * 6.0 + t * 0.3)) * 0.6;

                // wet sand at the waterline, subtle build grid
                float wet = smoothstep(_PZWaterY + 0.16, _PZWaterY + 0.02, h) * (1.0 - grassM);
                col *= 1.0 - wet * 0.35;
                smooth = lerp(smooth, 0.75, wet);
                float gl = smoothstep(0.47, 0.5, max(gc.x, gc.y));
                col *= 1.0 - gl * 0.07 * step(_PZWaterY, h) * (1.0 - fm);

                // detail normal
                float e = 0.11;
                float2 bp = p * 3.3;
                float b0 = pzNoise(bp) + 0.5 * pzNoise(bp * 2.7);
                float bx = pzNoise(bp + float2(e, 0)) + 0.5 * pzNoise((bp + float2(e, 0)) * 2.7);
                float bz = pzNoise(bp + float2(0, e)) + 0.5 * pzNoise((bp + float2(0, e)) * 2.7);
                float bump = lerp(0.05, 0.22, rockM) * (0.35 + 0.65 * near) * (1.0 - snowM * 0.7);
                float3 N = normalize(Ng + float3(b0 - bx, 0, b0 - bz) / e * bump);
                N = normalize(N + float3((ch > 0.5) ? 1 : 0, 0, (ch > 0.5) ? 0 : 1) * cos(rowPhase) * 0.22 * fm * near);

                // underwater
                float uw = smoothstep(_PZWaterY + 0.02, _PZWaterY - 0.25, h);
                float ca = pzNoise(p * 1.9 + t * 0.55) * pzNoise(p * 1.4 - t * 0.45 + 7.0);
                col = lerp(col, sand * float3(0.60, 0.80, 0.78), uw);
                col += uw * smoothstep(0.22, 0.5, ca) * 0.4 * saturate(1.0 + h * 0.4) * (1.0 - _PZNight);
                smooth *= 1.0 - uw;

                float3 c = PZShade(col, N, i.posWS, i.positionCS, smooth, em, 0.0);
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
            float4 vert(A v) : SV_POSITION
            {
                return PZShadowClip(TransformObjectToWorld(v.positionOS.xyz), TransformObjectToWorldNormal(v.normalOS));
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
            float4 vert(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
