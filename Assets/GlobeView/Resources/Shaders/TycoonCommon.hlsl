#ifndef TYCOON_COMMON_INCLUDED
#define TYCOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Set globally by SkyRenderer (Earth-fixed frame, Earth sits at the origin).
float4 _TycoonSunDir;
float4 _TycoonMoonDir;
float4x4 _TycoonStarRot;   // stars are fixed in space; rotates them into the Earth-fixed frame
float _TycoonTime;         // seconds of the twinkle clock (fixed pace in real time)
float _TycoonCalm;         // 0..1: at high game speed day and night blend toward one calm light

float TyHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float TyNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(TyHash(i + float3(0, 0, 0)), TyHash(i + float3(1, 0, 0)), f.x),
             lerp(TyHash(i + float3(0, 1, 0)), TyHash(i + float3(1, 1, 0)), f.x), f.y),
        lerp(lerp(TyHash(i + float3(0, 0, 1)), TyHash(i + float3(1, 0, 1)), f.x),
             lerp(TyHash(i + float3(0, 1, 1)), TyHash(i + float3(1, 1, 1)), f.x), f.y),
        f.z);
}

#endif
