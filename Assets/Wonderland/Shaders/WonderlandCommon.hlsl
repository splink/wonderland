#ifndef WONDERLAND_COMMON_INCLUDED
#define WONDERLAND_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Storybook ambient, set once by WonderlandLighting (C#).
half4 _WL_SkyColor;
half4 _WL_GroundColor;
half4 _WL_ShadowTint;
half _WL_Night;   // 0 by day, 1 at night: lamps and lit windows glow brighter
float4 _WL_Haze;  // distance haze: x = where it starts (m from the eyes), y = over how far it builds up, z = how strong (0: off)

// The world window (set by WonderlandWorld, C#): only the part of the world inside it is drawn, cut off cleanly at its
// edges. _WL_WorldToWindow takes world space to the world root's local space, where the window is the rectangle centred
// at _WL_Window.xy with half size _WL_Window.zw (metres); _WL_WindowOn is 0 when the whole world is drawn.
float4x4 _WL_WorldToWindow;
float4 _WL_Window;
float _WL_WindowOn;

void WL_Clip(float3 positionWS)
{
    if (_WL_WindowOn > 0.5)
    {
        float3 p = mul(_WL_WorldToWindow, float4(positionWS, 1.0)).xyz;
        float2 d = abs(p.xz - _WL_Window.xy) - _WL_Window.zw;
        clip(-max(d.x, d.y));
    }
}

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
    // Distance haze: the further parts of the world take on a soft tint of the sky, like hills on a summer day.
    if (_WL_Haze.z > 0.001)
    {
        float far = saturate((distance(positionWS, GetCameraPositionWS()) - _WL_Haze.x) / _WL_Haze.y);
        half3 air = lerp(_WL_SkyColor.rgb, half3(0.85h, 0.9h, 0.97h), 0.5h) * 0.9h;
        colour = lerp(colour, air, far * far * _WL_Haze.z);
    }
    return colour;
}

#endif
