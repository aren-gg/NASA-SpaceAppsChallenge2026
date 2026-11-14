Shader "MarsWeather/Atmosphere" {
 Properties { _Horizon("Horizon",Color)=(.64,.58,.55,1) _Zenith("Zenith",Color)=(.4,.4,.48,1) _SunStrength("Sun",Float)=0.45 _Heat("Heat",Float)=0 }
 SubShader { Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" } Cull Off ZWrite Off
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION; }; struct V { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
 float4 _Horizon,_Zenith; float _SunStrength,_Heat;
 V vert(A i) { V o; o.pos=TransformObjectToHClip(i.positionOS.xyz); o.dir=i.positionOS.xyz; return o; }
 half4 frag(V i):SV_Target {
 float3 d=normalize(i.dir); float h=saturate(d.y);
 h+=sin(d.x*110+_Time.y*1.8)*sin(d.z*63+_Time.y)*.002*_Heat;
 float3 c=lerp(_Horizon.rgb,_Zenith.rgb,pow(saturate(h),.6));
 float sun=dot(d,normalize(float3(-.45,.26,.8)));
 c+=float3(1,.57,.24)*(_SunStrength*(smoothstep(.9991,.9997,sun)*1.8+pow(saturate(sun),90)*.27));
 return half4(c,1); }
 ENDHLSL }
 }
}
