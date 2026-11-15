// Vertex-coloured storybook material shared by every generated asset (trees, houses, train, track, icons).
// Vertex colour = sRGB albedo; vertex alpha = emission mask (0 glows: windows, lamps).
Shader "Wonderland/Lit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _EmissionColor ("Emission Color", Color) = (1, 0.78, 0.45, 1)
        _EmissionStrength ("Emission Strength", Float) = 1.3
        _Highlight ("Highlight", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "WonderlandCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half4 _EmissionColor;
            half _EmissionStrength;
            half4 _Highlight;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = WL_Wind(TransformObjectToWorld(v.positionOS.xyz), v.uv.x);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 albedo = SRGBToLinear(i.color.rgb) * _Tint.rgb;
                half3 normalWS = normalize(i.normalWS);
                half3 colour = WL_Shade(albedo, i.positionWS, normalWS);
                half glow = 1.0h - i.color.a;
                colour += albedo * _EmissionColor.rgb * (_EmissionStrength * glow);
                colour = lerp(colour, _Highlight.rgb, _Highlight.a);
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex WLShadowVert
            #pragma fragment WLShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "WonderlandShadow.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex WLDepthVert
            #pragma fragment WLShadowFrag
            #pragma multi_compile_instancing
            #include "WonderlandShadow.hlsl"
            ENDHLSL
        }
    }
}
