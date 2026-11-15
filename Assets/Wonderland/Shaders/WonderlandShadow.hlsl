#ifndef WONDERLAND_SHADOW_INCLUDED
#define WONDERLAND_SHADOW_INCLUDED
// Shared ShadowCaster / DepthOnly vertex programs. Include after the shader's UnityPerMaterial CBUFFER.

float3 _LightDirection;
float3 _LightPosition;

struct WLShadowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct WLShadowVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

WLShadowVaryings WLShadowVert(WLShadowAttributes v)
{
    WLShadowVaryings o = (WLShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = positionCS;
    return o;
}

WLShadowVaryings WLDepthVert(WLShadowAttributes v)
{
    WLShadowVaryings o = (WLShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    return o;
}

half4 WLShadowFrag(WLShadowVaryings i) : SV_Target { return 0; }

#endif
