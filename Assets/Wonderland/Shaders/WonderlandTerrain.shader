// The miniature landscape's ground. Colour is procedural (no textures): layered grass greens, sunny meadows, slope and
// height tints, tiny flowers, soil under the railway, and an organic edge that fades into the real floor through
// passthrough (alpha < 1 lets the camera feed show). Also draws the glowing brush ring.
// Vertex colour: r = railway soil mask, g = edge factor (0 at the rim), b = cavity (0.5 flat, < 0.5 hollows), a = 1 - beach sand.
// High ground turns alpine: rock bands on cliffs, scree, then snow on the gentler tops.
Shader "Wonderland/Terrain"
{
    Properties
    {
        _BrushPos ("Brush (local xyz, radius)", Vector) = (0, 0, 0, 0)
        _BrushColor ("Brush Color (a = intensity)", Color) = (0.6, 1, 0.4, 0)
        _FlowerDensity ("Flower Density", Float) = 0.06
    }
    SubShader
    {
        // Drawn after the opaque props with premultiplied blending: on the headset the eye buffer is cleared to
        // transparent, so this equals an opaque write that carries the rim's alpha to the passthrough compositor; in
        // editor renders it blends the rim over the stand-in floor the same way.
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+450" }

        HLSLINCLUDE
        #include "WonderlandCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BrushPos;
            half4 _BrushColor;
            half _FlowerDensity;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _WL_TERRAIN_LIGHT _WL_TERRAIN_SIMPLE

            TEXTURE2D(_WLNoiseTex); SAMPLER(sampler_WLNoiseTex);
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 normalOS : TEXCOORD3;
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
                o.positionOS = v.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                o.color = v.color;
                return o;
            }

            half3 FlowerColour(float h)
            {
                if (h < 0.3) return half3(1.0, 0.97, 0.9);
                if (h < 0.55) return half3(1.0, 0.83, 0.3);
                if (h < 0.8) return half3(0.96, 0.55, 0.72);
                return half3(0.72, 0.58, 0.95);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.positionOS.xz;
                float height = i.positionOS.y;
                half3 nOS = normalize(i.normalOS);
                half slope = 1.0h - saturate(nOS.y);
                half soil = i.color.r;
                half edge = i.color.g;
                half cavity = i.color.b;

                // The noise: computed per pixel (Full), read from a texture (Light: same look, a fraction of the
                // cost), or none at all (Simple).
            #if defined(_WL_TERRAIN_LIGHT)
                half4 coarse = SAMPLE_TEXTURE2D(_WLNoiseTex, sampler_WLNoiseTex, p * 0.5);
                half fine = SAMPLE_TEXTURE2D(_WLNoiseTex, sampler_WLNoiseTex, p * 4.0).a;
                half finer = SAMPLE_TEXTURE2D(_WLNoiseTex, sampler_WLNoiseTex, p * 13.3 + 0.37).a;
                float n1 = coarse.g, n2 = coarse.b, n3 = fine, n4 = finer;
                float meadow = smoothstep(0.5, 0.78, coarse.r * 0.75 + n2 * 0.25);
            #elif defined(_WL_TERRAIN_SIMPLE)
                float n1 = 0.5, n2 = 0.5, n3 = 0.5, n4 = 0.5;
                float meadow = 0.0;
            #else
                float n1 = WL_Noise(p * 5.0);
                float n2 = WL_Noise(p * 14.0 + 7.3);
                float n3 = WL_Noise(p * 48.0 + 3.1);
                float n4 = WL_Noise(p * 160.0 + 1.7);
                float meadow = smoothstep(0.5, 0.78, WL_Noise(p * 2.6 + 11.0) * 0.75 + n2 * 0.25);
            #endif

                half3 grassDeep = half3(0.25, 0.45, 0.16);
                half3 grassMid = half3(0.40, 0.59, 0.22);
                half3 grassSun = half3(0.62, 0.70, 0.31);
                half3 moss = half3(0.30, 0.45, 0.17);
                half3 earth = half3(0.50, 0.36, 0.22);
                half3 rock = half3(0.60, 0.57, 0.52);
                half3 path = half3(0.58, 0.47, 0.33);

                half3 albedo = lerp(grassDeep, grassMid, smoothstep(0.15, 0.85, n1 * 0.5 + n2 * 0.5));
                albedo = lerp(albedo, grassSun, meadow * 0.5);
                albedo = lerp(albedo, grassSun, saturate((height - 0.07) / 0.25) * 0.45);
                albedo *= 0.88 + 0.16 * n3 + 0.1 * (n4 - 0.5);
                half steep = saturate((slope - 0.18) / 0.3);
                albedo = lerp(albedo, moss, steep * 0.7);
                half bare = saturate((slope - 0.42) / 0.2) * smoothstep(0.35, 0.65, n2);
                albedo = lerp(albedo, lerp(earth, rock, n3), bare);
                // Mountains: above the tree line grass thins to scree and rock; cliffs show layered rock bands; snow caps
                // the gentler tops. (Heights match GroundCover.AlpineHeight / SnowHeight.)
                half alpine = smoothstep(0.12, 0.17, height + (n1 - 0.5) * 0.03);
                half cliff = saturate((slope - 0.3) / 0.25) * smoothstep(0.06, 0.12, height);
                half strata = 0.82 + 0.18 * sin(height * 260.0 + n2 * 4.0);
                half3 rockFace = lerp(half3(0.52, 0.49, 0.46), half3(0.68, 0.64, 0.58), n3) * strata;
                albedo = lerp(albedo, lerp(albedo, rockFace, 0.55 + 0.35 * n2), alpine * 0.75);
                albedo = lerp(albedo, rockFace, max(cliff, alpine * steep) * 0.85);
                half snow = smoothstep(0.19, 0.215, height + (n2 - 0.5) * 0.025) * saturate(1.0 - (slope - 0.25) / 0.2);
                albedo = lerp(albedo, half3(0.95, 0.96, 0.98) * (0.94 + 0.06 * n4), snow);
                // Beaches: warm sand along the pond shores.
                half sand = saturate(1.0 - i.color.a) * (1.0 - snow);
                albedo = lerp(albedo, half3(0.86, 0.77, 0.56) * (0.9 + 0.12 * n4 + 0.06 * n3), smoothstep(0.2, 0.6, sand + (n3 - 0.5) * 0.25));
                albedo *= lerp(0.72, 1.08, saturate(cavity * 1.6 - 0.3));

                // Tiny flowers in sunny, flat grass.
                float2 cellP = p * 95.0;
                float2 cell = floor(cellP);
                float r = WL_Hash(cell);
                half density = _FlowerDensity * (0.35 + meadow * 1.6) * (1.0 - steep) * (1.0 - soil) * saturate(edge * 3.0 - 0.6) * (1.0 - alpine * 0.8) * (1.0 - snow) * (1.0 - sand);
            #if !defined(_WL_TERRAIN_SIMPLE)
                if (r < density)
                {
                    float2 centre = float2(WL_Hash(cell + 17.0), WL_Hash(cell + 31.0)) * 0.5 + 0.25;
                    float d = length(frac(cellP) - centre);
                    half petal = smoothstep(0.26, 0.14, d);
                    half3 flower = FlowerColour(WL_Hash(cell + 5.0));
                    flower = lerp(flower, half3(1.0, 0.9, 0.45), smoothstep(0.08, 0.03, d));
                    albedo = lerp(albedo, flower, petal);
                }

                // Railway: trodden soil and ballast spill.
            #endif

                half soilMask = smoothstep(0.15, 0.85, soil + (n3 - 0.5) * 0.35);
                albedo = lerp(albedo, path * (0.85 + 0.3 * n4), soilMask);

                // Rim: a little darker and earthier, then dissolving into the real floor.
                half rimMix = smoothstep(0.32, 0.06, edge + (n2 - 0.5) * 0.12);
                albedo = lerp(albedo, lerp(moss, earth, 0.4), rimMix * 0.55);
                // Fully transparent before the mesh's own stepped boundary, so only the soft noisy fade is seen.
                half alpha = smoothstep(0.05, 0.2, edge + (n3 - 0.5) * 0.08);

                half3 linearAlbedo = SRGBToLinear(albedo);
                half3 colour = WL_Shade(linearAlbedo, i.positionWS, normalize(i.normalWS));

                // Brush: glowing ring with a soft inner fill and halo.
                if (_BrushColor.a > 0.001)
                {
                    float d = distance(p, _BrushPos.xz);
                    float radius = _BrushPos.w;
                    float width = 0.0035 + radius * 0.015;
                    float ring = exp(-pow((d - radius) / width, 2.0));
                    float halo = exp(-pow((d - radius) / (width * 5.0), 2.0)) * 0.35;
                    float fill = d < radius ? 0.12 * (1.0 - d / radius) + 0.05 : 0.0;
                    colour += SRGBToLinear(_BrushColor.rgb) * (ring * 1.4 + halo + fill) * _BrushColor.a;
                }

                return half4(colour * alpha, alpha);
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
