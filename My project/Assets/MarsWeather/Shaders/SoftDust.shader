Shader "MarsWeather/SoftDust" {
 Properties { _Opacity("Opacity",Range(0,1))=1 }
 SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
 Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
 struct V { float4 pos:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
 float _Opacity;
 V vert(A i) { V o; o.pos=TransformObjectToHClip(i.positionOS.xyz); o.color=i.color; o.uv=i.uv; return o; }
 half4 frag(V i):SV_Target { float2 p=i.uv*2-1; float edge=saturate(1-dot(p,p));
 float wisps=.8+.2*sin(p.x*7+sin(p.y*9)); return half4(i.color.rgb,i.color.a*edge*edge*wisps*_Opacity); }
 ENDHLSL }
 }
}
