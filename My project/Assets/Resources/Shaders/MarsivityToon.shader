// Anime toon shading for the astronauts, after VRoid's MToon look: a two-tone cel step from
// the main light (with its shadows), a soft lavender shade colour, a gentle rim light, a little
// ambient fill, alpha cut-out for lashes and hair tips, and an inverted-hull ink outline.
// URP forward; shadow casting and depth come from URP Lit.
Shader "Marsivity/Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.78,0.74,0.88,1)
        _ShadeStep ("Shade Step", Range(-1,1)) = 0.05
        _ShadeSoftness ("Shade Softness", Range(0.001,0.5)) = 0.06
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3.5
        _RimStrength ("Rim Strength", Range(0,1)) = 0.25
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _OutlineWidth ("Outline Width (m)", Range(0,0.02)) = 0.0025
        _OutlineColor ("Outline Color", Color) = (0.22,0.18,0.27,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadeColor, _RimColor, _EmissionColor, _OutlineColor;
            half _ShadeStep, _ShadeSoftness, _RimPower, _RimStrength, _Cutoff, _OutlineWidth, _AlphaClip, _Cull;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2; half fog : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                clip(albedo.a - _Cutoff);
                #endif
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, light.direction) * lerp(0.4, 1, light.shadowAttenuation);
                half lit = smoothstep(_ShadeStep - _ShadeSoftness, _ShadeStep + _ShadeSoftness, ndl * light.shadowAttenuation);
                half3 shade = albedo.rgb * _ShadeColor.rgb;
                half3 colour = lerp(shade, albedo.rgb, lit) * lerp(0.75, 1.0, saturate(light.color.r));
                colour += albedo.rgb * SampleSH(n) * 0.35;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(saturate(1 - dot(n, v)), _RimPower) * _RimStrength;
                colour += _RimColor.rgb * rim * (0.4 + 0.6 * lit);
                colour += _EmissionColor.rgb;
                colour = MixFog(colour, i.fog);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fog
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(i.normalOS);
                // Width in metres, thinned up close so the ink never swamps the face.
                float dist = distance(ws, GetCameraPositionWS());
                ws += nws * _OutlineWidth * saturate(dist / 1.5);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return half4(MixFog(_OutlineColor.rgb, i.fog), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Universal Render Pipeline/Lit"
}
