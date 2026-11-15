Shader "MarsTrainer/Look/VisorEdges"
{
    Properties
    {
        _Frost("Frost", Range(0,1)) = 0
        _Warning("Warning", Range(0,1)) = 0
        _FrostColor("Frost colour", Color) = (.914,.725,.737,1)
        _WarningColor("Warning colour", Color) = (.902,.627,.431,1)
        _Frequency("Warning frequency", Float) = 1.5
        _EdgeStart("Clear centre", Range(0,1)) = .62
        _BranchScale("Crystal scale", Float) = 28
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float _Frost, _Warning, _Frequency, _EdgeStart, _BranchScale;
            float4 _FrostColor, _WarningColor;
            CBUFFER_END
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 frag(V i):SV_Target
            {
                float2 p=abs(i.uv*2-1);
                float border=max(p.x,p.y);
                float edge=smoothstep(_EdgeStart,1,border);
                float2 cell=floor(i.uv*_BranchScale);
                float seed=frac(sin(dot(cell,float2(127.1,311.7)))*43758.5453);
                float2 q=frac(i.uv*_BranchScale)-.5;
                float angle=seed*6.283185;
                q=mul(float2x2(cos(angle),-sin(angle),sin(angle),cos(angle)),q);
                float radius=length(q);
                float spokes=min(abs(q.x),min(abs(q.x*.5+q.y*.866),abs(q.x*.5-q.y*.866)));
                float crystal=(1-smoothstep(.008,.025,spokes))*(1-smoothstep(.2,.42,radius));
                crystal*=smoothstep(.35,.7,seed);
                float grain=frac(sin(dot(floor(i.uv*_BranchScale*4),float2(12.98,78.23)))*43758.54);
                float frost=_Frost*edge*(.16+.18*grain+.66*crystal);
                float pulse=.5+.5*sin(_Time.y*_Frequency*6.283185);
                float alert=_Warning*smoothstep(.87,1,border)*(.08+.14*pulse);
                float alpha=saturate(frost+alert);
                return half4(lerp(_FrostColor.rgb,_WarningColor.rgb,alert/max(alpha,.001)),alpha);
            }
            ENDHLSL
        }
    }
}
