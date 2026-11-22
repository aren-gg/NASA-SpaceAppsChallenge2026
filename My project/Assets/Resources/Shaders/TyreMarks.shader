// The rover's tyre marks (MarsRoverTracks): unlit, coloured and faded by vertex colour. No fog: the dust fog
// starts close and washed the marks out to a light tan stripe.
Shader "Mars/TyreMarks"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 position : POSITION; half4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; half4 color : COLOR; };
            Varyings vert(Attributes a)
            {
                Varyings v;
                v.position = TransformObjectToHClip(a.position.xyz);
                v.color = a.color;
                return v;
            }
            half4 frag(Varyings v) : SV_Target
            {
                return v.color;
            }
            ENDHLSL
        }
    }
}
