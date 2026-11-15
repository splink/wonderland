// Storybook water for ponds, lakes and streams: shallow turquoise to deep blue (vertex r = depth 0..1), drifting ripples,
// sun glints, a soft sky reflection, and foam along the shore (vertex g = shore 0..1). Vertex b = flow speed along +UV.y
// for streams (0 for still water); UV = flow coordinates. Alpha-blended over the terrain bed and the passthrough room.
Shader "Wonderland/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.36, 0.78, 0.76, 1)
        _Deep ("Deep", Color) = (0.1, 0.36, 0.56, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+480" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "WonderlandCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                p.y += sin(_Time.y * 1.3 + p.x * 30.0 + p.z * 21.0) * 0.0004 * v.color.r;   // a gentle swell, none at the shore
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                half depth = i.color.r, shore = i.color.g, flow = i.color.b;
                float2 p = i.positionWS.xz * 38.0;
                float2 drift = flow > 0.01 ? float2(0.0, -t * flow * 12.0) : float2(t * 0.25, t * 0.18);
                float2 q = flow > 0.01 ? float2(i.uv.x * 38.0, i.uv.y * 38.0) + drift : p + drift;
                float n1 = WL_Noise(q), n2 = WL_Noise(q * 2.3 - drift * 1.7 + 13.0);
                float2 slope = float2(WL_Noise(q + float2(0.1, 0.0)) - n1, WL_Noise(q + float2(0.0, 0.1)) - n1) * 2.2
                             + float2(WL_Noise(q * 2.3 + float2(0.1, 0.0) + 13.0) - n2, WL_Noise(q * 2.3 + float2(0.0, 0.1) + 13.0) - n2) * 1.2;
                half3 normal = normalize(half3(-slope.x, 1.0, -slope.y));
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 colour = lerp(SRGBToLinear(_Shallow.rgb), SRGBToLinear(_Deep.rgb), smoothstep(0.0, 1.0, depth));
                colour *= lerp(0.75h, 1.05h, light.shadowAttenuation) * (0.85h + 0.15h * n1);
                half fresnel = pow(1.0h - saturate(dot(normal, viewDir)), 4.0h);
                colour = lerp(colour, _WL_SkyColor.rgb * 1.25h, fresnel * 0.55h);
                half3 h = normalize(light.direction + viewDir);
                half glint = pow(saturate(dot(normal, h)), 180.0h) * 2.5h * light.shadowAttenuation;
                colour += light.color * glint;
                // Foam: a broken white band along the shore, plus streaks on running water.
                half foamBand = smoothstep(0.55h, 0.95h, shore + (n2 - 0.5h) * 0.5h);
                half foamFlow = flow > 0.01 ? smoothstep(0.78h, 0.92h, n2) * 0.6h : 0.0h;
                half foam = saturate(foamBand + foamFlow);
                colour = lerp(colour, half3(0.95h, 0.97h, 1.0h), foam * 0.85h);
                half alpha = lerp(0.6h, 0.9h, depth);
                alpha = max(alpha, foam * 0.9h);
                alpha *= saturate(depth * 40.0h + shore);   // feather the very edge
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
