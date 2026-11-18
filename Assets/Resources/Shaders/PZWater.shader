Shader "PeopleZ/Water"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "PZCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; };
            V2F vert(A v)
            {
                V2F o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                return o;
            }
            float wh(float2 p, float t)
            {
                return pzNoise(p * 0.55 + t * float2(0.35, 0.22)) + 0.5 * pzNoise(p * 1.4 - t * float2(0.30, 0.42)) + 0.25 * pzNoise(p * 3.1 + t * 0.5);
            }
            float3 SSR(float3 posWS, float3 R, float surfZ, out float hit)
            {
                hit = 0.0;
                float3 p = posWS;
                float stepLen = 0.22;
                for (int k = 0; k < 30; k++)
                {
                    p += R * stepLen;
                    stepLen *= 1.2;
                    float3 ndc = ComputeNormalizedDeviceCoordinatesWithZ(p, GetWorldToHClipMatrix());
                    if (ndc.x < 0.0 || ndc.x > 1.0 || ndc.y < 0.0 || ndc.y > 1.0) break;
                    float rayZ = -TransformWorldToView(p).z;
                    if (rayZ <= 0.0) break;
                    float sceneZ = LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, ndc.xy, 0).r, _ZBufferParams);
                    if (rayZ > sceneZ && rayZ - sceneZ < 1.0 + stepLen * 3.0 && sceneZ < 600.0)
                    {
                        float2 edge = smoothstep(0.0, 0.08, ndc.xy) * smoothstep(1.0, 0.92, ndc.xy);
                        hit = edge.x * edge.y;
                        return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_LinearClamp, ndc.xy, 0).rgb;
                    }
                }
                return 0;
            }
            half4 frag(V2F i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.posWS.xz;
                float3 toCam = _WorldSpaceCameraPos - i.posWS;
                float dist = length(toCam);
                float3 V = toCam / dist;

                float e = 0.08;
                float h0 = wh(p, t), hx = wh(p + float2(e, 0), t), hz = wh(p + float2(0, e), t);
                float ns = 0.17 * saturate(50.0 / dist);
                float3 N = normalize(float3(-(hx - h0) / e * ns, 1.0, -(hz - h0) / e * ns));

                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float surfZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float depth = max(sceneZ - surfZ, 0.0);

                float2 uv2 = uv + N.xz * 0.04 * saturate(depth);
                float sceneZ2 = LinearEyeDepth(SampleSceneDepth(uv2), _ZBufferParams);
                if (sceneZ2 < surfZ) { uv2 = uv; sceneZ2 = sceneZ; }
                float depth2 = max(sceneZ2 - surfZ, 0.0);
                float3 scene = SampleSceneColor(uv2);

                float3 light = _PZSunColor * (1.0 - _PZNight * 0.5) + _PZZenith * 0.6;
                float3 shallow = float3(0.30, 0.92, 0.80);
                float3 deep = float3(0.010, 0.10, 0.22) * light;
                float k = 1.0 - exp(-depth2 * 0.7);
                float3 water = lerp(scene * lerp(float3(1, 1, 1), shallow, saturate(depth2 * 2.0)), deep, k);
                // light scattering through wave crests
                water += float3(0.02, 0.22, 0.20) * light * saturate(h0 - 0.95) * k * 0.6;

                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.posWS));
                sh = lerp(1.0, sh, saturate(1.0 - dist / 140.0)) * PZCloudShadow(i.posWS);

                float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 R = reflect(-V, normalize(lerp(float3(0, 1, 0), N, 0.45)));
                R.y = abs(R.y);
                float3 refl = PZSkyColor(R, 1.0);
                float hit;
                float3 ssr = SSR(i.posWS + float3(0, 0.02, 0), R, surfZ, hit);
                refl = lerp(refl, ssr, hit * 0.9);
                float3 col = lerp(water * lerp(0.72, 1.0, sh), refl, saturate(fres * 0.9 + hit * 0.06));

                float3 H = normalize(V + _PZSunDir);
                float ndh = saturate(dot(N, H));
                col += _PZSunColor * (pow(ndh, 700.0) * 18.0 + pow(ndh, 90.0) * 0.5) * sh;
                float glint = step(0.93, pzHash(floor(p * 22.0) + floor(t * 7.0))) * pow(ndh, 40.0);
                col += _PZSunColor * glint * 6.0 * sh * saturate(40.0 / dist);

                float fn = pzNoise(p * 4.0 + t * 0.4) * 0.7 + pzNoise(p * 11.0 - t * 0.6) * 0.3;
                float shore = saturate(1.0 - depth / 0.45);
                float band = smoothstep(0.6, 0.97, sin(depth * 13.0 - t * 2.0) * 0.5 + 0.5) * saturate(1.0 - depth / 1.4);
                float foam = saturate(shore * shore * 1.3 + band * 0.5) * smoothstep(0.25, 0.7, fn + shore * 0.5);
                float3 foamCol = (_PZSunColor * 0.75 * sh + lerp(_PZHorizon, _PZZenith, 0.5) * 0.55);
                col = lerp(col, foamCol, foam * 0.9);

                col = PZFog(col, i.posWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
