#ifndef MARS_TRAINER_SKY_INCLUDED
#define MARS_TRAINER_SKY_INCLUDED
float4 _MarsHorizon, _MarsMiddle, _MarsZenith, _MarsSunDirection, _MarsGradient;
float _MarsSun, _MarsNight, _MarsAurora, _MarsFlash;
void MarsSky_float(float3 Direction, out float3 Colour)
{
    float3 d = normalize(Direction);
    float h = saturate(d.y);
    float3 low = lerp(_MarsHorizon.rgb, _MarsMiddle.rgb, smoothstep(0, _MarsGradient.x, h));
    Colour = lerp(low, _MarsZenith.rgb, smoothstep(_MarsGradient.x * .7, _MarsGradient.y, h));
    float sun = saturate(dot(d, normalize(_MarsSunDirection.xyz)));
    Colour += float3(1, .67, .38) * _MarsSun * (smoothstep(.9994, .99975, sun) * 2 + pow(sun, 100) * .18);
    float azimuth = atan2(d.x, d.z);
    float wave = .26 + .075 * sin(azimuth * 3 + _Time.y * .13);
    float curtain = exp2(-abs(h - wave) * 95) + .6 * exp2(-abs(h - wave - .13) * 72);
    float folds = .65 + .35 * sin(azimuth * 72 + sin(azimuth * 9) * 4 + _Time.y * .4);
    Colour += _MarsAurora * curtain * folds * float3(1.7, .85, 1.3) * smoothstep(.02, .1, h);
    float3 cell = floor(d * 360);
    float star = step(.9992, frac(sin(dot(cell, float3(12.98, 78.23, 37.71))) * 43758.54));
    Colour += star * _MarsNight * smoothstep(.08, .3, h) * float3(.7, .6, .8);
    Colour += _MarsFlash * float3(1, .65, .53);
}
void MarsSky_half(half3 Direction, out half3 Colour)
{
    float3 result; MarsSky_float(Direction, result); Colour = result;
}
#endif
