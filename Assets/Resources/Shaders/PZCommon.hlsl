#ifndef PZ_COMMON_INCLUDED
#define PZ_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float3 _PZSunDir;      // direction towards the active light (sun or moon)
float3 _PZSunColor;
float3 _PZZenith;
float3 _PZHorizon;
float3 _PZGround;
float _PZFogDensity;
float _PZWaterY;
float _PZNight;        // 0 day .. 1 night
float _PZGlow;         // window / emissive strength
TEXTURE2D(_PZMask);    // r,b farmland  g swamp  a scorch

float pzHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}
float pzNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(pzHash(i), pzHash(i + float2(1, 0)), f.x),
                lerp(pzHash(i + float2(0, 1)), pzHash(i + float2(1, 1)), f.x), f.y);
}
float pzFbm(float2 p)
{
    float a = 0.5, s = 0.0;
    for (int i = 0; i < 4; i++) { s += a * pzNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return s;
}
float pzFbm5(float2 p)
{
    float a = 0.5, s = 0.0;
    for (int i = 0; i < 5; i++) { s += a * pzNoise(p); p = p * 2.11 + 9.7; a *= 0.5; }
    return s;
}

float3 PZSkyColor(float3 d, float detail)
{
    float up = d.y;
    float3 col = lerp(_PZHorizon, _PZZenith, pow(saturate(up), 0.45));
    col = lerp(col, _PZGround, saturate(-up * 6.0));
    float s = saturate(dot(d, _PZSunDir));
    if (detail > 0.5 && up > 0.0 && _PZNight > 0.01)
    {
        float3 q = d * 170.0;
        float3 ip = floor(q);
        float hs = pzHash(ip.xy + ip.z * 17.3);
        float st = step(0.991, hs) * saturate(1.0 - length(frac(q) - 0.5) * 2.6);
        col += st * _PZNight * (1.0 + 3.0 * frac(hs * 91.7)) * saturate(up * 4.0);
    }
    col += _PZSunColor * (pow(s, 1800.0) * 30.0 + pow(s, 60.0) * 0.4 + pow(s, 7.0) * 0.14);
    if (detail > 0.5 && up > 0.0)
    {
        float2 cp = d.xz / (up + 0.10) * 1.2 + _Time.y * float2(0.012, 0.006);
        float c = pzFbm5(cp);
        float c2 = pzFbm5(cp + normalize(_PZSunDir.xz + 1e-4) * 0.09);
        float m = smoothstep(0.46, 0.74, c) * saturate(up * 7.0);
        float lit = saturate((c - c2) * 5.0 + 0.55);
        float3 dark = lerp(_PZZenith, _PZHorizon, 0.55) * 0.75;
        float3 bright = _PZHorizon * 0.5 + _PZSunColor * 0.95 + 0.12 * (1.0 - _PZNight);
        float3 cc = lerp(dark, bright, lit) + _PZSunColor * pow(s, 10.0) * 0.6 * (1.0 - smoothstep(0.5, 0.8, c));
        col = lerp(col, cc, m * 0.92);
    }
    return col;
}

float3 PZFog(float3 col, float3 posWS)
{
    float3 v = posWS - _WorldSpaceCameraPos;
    float d = length(v);
    float hf = exp(-max(posWS.y, 0.0) * 0.12);
    float f = 1.0 - exp(-max(d - 10.0, 0.0) * _PZFogDensity * (0.55 + hf));
    float3 dir = v / max(d, 1e-4);
    float3 fc = PZSkyColor(normalize(float3(dir.x, 0.04, dir.z)), 0.0);
    return lerp(col, fc, f);
}

// drifting cloud shadows on the ground
float PZCloudShadow(float3 posWS)
{
    float2 cp = posWS.xz * 0.016 + _Time.y * float2(0.020, 0.011);
    float c = pzFbm(cp);
    return 1.0 - smoothstep(0.50, 0.72, c) * 0.55 * (1.0 - _PZNight * 0.6);
}

float PZSpec(float3 N, float3 V, float3 L, float rough)
{
    float3 H = normalize(V + L);
    float ndh = saturate(dot(N, H));
    float a = max(rough * rough, 0.003);
    float a2 = a * a;
    float d = ndh * ndh * (a2 - 1.0) + 1.0;
    float D = a2 / (PI * d * d);
    float F = 0.04 + 0.96 * pow(1.0 - saturate(dot(V, H)), 5.0);
    return min(D * F * 0.25, 24.0);
}

float3 PZShade(float3 albedo, float3 N, float3 posWS, float4 posCS, float smooth, float3 emission, float trans)
{
    float4 sc = TransformWorldToShadowCoord(posWS);
    Light L = GetMainLight(sc, posWS, half4(1, 1, 1, 1));
    float ndl = saturate(dot(N, L.direction));
    float sh = L.shadowAttenuation * PZCloudShadow(posWS);
    float ao = 1.0;
#if defined(_SCREEN_SPACE_OCCLUSION)
    float2 suv = GetNormalizedScreenSpaceUV(posCS);
    AmbientOcclusionFactor aof = GetScreenSpaceAmbientOcclusion(suv);
    ao = aof.indirectAmbientOcclusion;
#endif
    float3 V = normalize(_WorldSpaceCameraPos - posWS);
    float3 sky = lerp(_PZHorizon, _PZZenith, saturate(N.y));
    float3 amb = lerp(_PZGround * 0.7 + albedo * _PZSunColor * 0.12, sky, N.y * 0.5 + 0.5) * 0.62;
    float fres = pow(1.0 - saturate(dot(N, V)), 5.0);
    float3 c = albedo * (L.color * ndl * sh + amb * ao);
    c += albedo * L.color * saturate(dot(-N, L.direction) * 0.6 + 0.4) * trans * sh * 0.5;
    c += L.color * PZSpec(N, V, L.direction, 1.0 - smooth) * sh * ndl;
    c += sky * fres * (0.04 + smooth * 0.5) * ao;
    return c + emission;
}

float3 _LightDirection;
float4 PZShadowClip(float3 posWS, float3 nWS)
{
    float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
#if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return cs;
}
#endif
