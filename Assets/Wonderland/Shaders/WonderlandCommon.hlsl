#ifndef WONDERLAND_COMMON_INCLUDED
#define WONDERLAND_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Storybook ambient, set once by WonderlandLighting (C#).
half4 _WL_SkyColor;
half4 _WL_GroundColor;
half4 _WL_ShadowTint;

float WL_Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float WL_Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(WL_Hash(i), WL_Hash(i + float2(1, 0)), u.x),
                lerp(WL_Hash(i + float2(0, 1)), WL_Hash(i + float2(1, 1)), u.x), u.y);
}

// Gentle wind: foliage and grass tips drift a millimetre or two (weight in UV0.x; 0 = rigid).
float3 WL_Wind(float3 positionWS, float weight)
{
    if (weight <= 0.0) return positionWS;
    float t = _Time.y;
    float phase = dot(positionWS.xz, float2(9.1, 6.7));
    float gust = 0.65 + 0.35 * sin(t * 0.4 + positionWS.x * 2.0);
    float2 offset = float2(sin(t * 1.9 + phase), cos(t * 1.5 + phase * 1.3)) * (0.0016 * weight * gust);
    return positionWS + float3(offset.x, 0.0, offset.y);
}

// Soft wrapped key light, gentle shadows tinted towards a cool shadow colour, hemispheric fill and a faint rim:
// warm, readable volumes that feel painted rather than physically lit.
half3 WL_Shade(half3 albedo, float3 positionWS, half3 normalWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light light = GetMainLight(shadowCoord);
    half ndl = dot(normalWS, light.direction);
    half wrap = saturate(ndl * 0.6h + 0.4h);
    wrap *= wrap;
    half shadow = light.shadowAttenuation;
    half direct = wrap * lerp(0.35h, 1.0h, shadow);
    half3 ambient = lerp(_WL_GroundColor.rgb, _WL_SkyColor.rgb, normalWS.y * 0.5h + 0.5h);
    ambient = lerp(ambient * _WL_ShadowTint.rgb, ambient, saturate(shadow * wrap + 0.4h));
    half3 viewDir = normalize(GetWorldSpaceViewDir(positionWS));
    half rim = pow(1.0h - saturate(dot(normalWS, viewDir)), 3.0h);
    half3 colour = albedo * (ambient + light.color * direct);
    colour += albedo * rim * 0.22h * _WL_SkyColor.rgb;
    return colour;
}

#endif
