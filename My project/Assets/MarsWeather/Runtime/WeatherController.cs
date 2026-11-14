using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MarsWeather
{
    public enum WeatherEvent { Clear, SolarFlare, DustStorm, HeatStorm, ColdNight }

    /// <summary>Standalone visual weather. Owns atmosphere only while enabled; no terrain or survival dependencies.</summary>
    public sealed class WeatherController : MonoBehaviour
    {
        public Light sun;
        public Material skyTemplate;
        public Material particleMaterial;
        [Range(0, 1)] public float intensity = 1f;
        [Min(0)] public float transitionSeconds = 3f;
        public bool controlAtmosphere = true;
        public event Action<WeatherEvent> WeatherChanged;
        public WeatherEvent Current { get; private set; }
        public float DustAmount { get; private set; }
        public float SolarAmount { get; private set; }
        public float HeatAmount { get; private set; }
        public float ColdAmount { get; private set; }
        public float RemainingSeconds { get; private set; }

        Material sky;
        ParticleSystem dust, haze, sparks, heatWisps, stars;
        Volume volume;
        VolumeProfile profile;
        ColorAdjustments grading;
        Bloom bloom;
        Vignette vignette;
        float dustWeight, solarWeight, heatWeight, coldWeight;
        bool saved;
        Material previousSky;
        bool previousFog;
        FogMode previousFogMode;
        float previousFogDensity;
        Color previousFogColor, previousAmbient, previousSunColor;
        AmbientMode previousAmbientMode;
        float previousSunIntensity;
        Quaternion previousSunRotation;
        GameObject effects;
        MaterialPropertyBlock opacityBlock;

        public void Activate(WeatherEvent weather, float strength = 1f, float durationSeconds = 0f)
        {
            Current = weather;
            intensity = Mathf.Clamp01(strength);
            RemainingSeconds = Mathf.Max(0, durationSeconds); // zero stays active until stopped
            WeatherChanged?.Invoke(Current);
        }
        public void StopWeather() { Activate(WeatherEvent.Clear); }

        void OnEnable()
        {
            opacityBlock = new MaterialPropertyBlock();
            if (controlAtmosphere)
            {
                previousSky = RenderSettings.skybox;
                previousFog = RenderSettings.fog;
                previousFogMode = RenderSettings.fogMode;
                previousFogDensity = RenderSettings.fogDensity;
                previousFogColor = RenderSettings.fogColor;
                previousAmbient = RenderSettings.ambientLight;
                previousAmbientMode = RenderSettings.ambientMode;
                if (sun != null)
                {
                    previousSunColor = sun.color;
                    previousSunIntensity = sun.intensity;
                    previousSunRotation = sun.transform.rotation;
                }
                saved = true;
                if (skyTemplate != null) { sky = new Material(skyTemplate); RenderSettings.skybox = sky; }
            }
            effects = new GameObject("Weather effects");
            effects.transform.SetParent(transform, false);
            dust = Particles("Windblown fine dust", 1800, 8f, 0.16f, new Color(.72f,.39f,.25f,.48f), new Vector3(42,12,38), new Vector3(-14,0,2));
            haze = Particles("Rolling dust banks", 120, 12f, 10f, new Color(.71f,.40f,.29f,.065f), new Vector3(48,5,42), new Vector3(-3,0,0));
            sparks = Particles("Thermal air flecks", 900, 6f, .13f, new Color(.98f,.72f,.47f,.6f), new Vector3(36,2,30), new Vector3(.3f,1.5f,0));
            sparks.transform.localPosition = Vector3.up * 1.2f;
            heatWisps = Particles("Rising heat wisps", 64, 6f, 2.2f, new Color(.92f,.65f,.43f,.075f), new Vector3(34,1,28), new Vector3(.2f,.9f,0));
            heatWisps.transform.localPosition = Vector3.up * .7f;
            var wispSize = heatWisps.main;
            wispSize.startSize3D = true;
            wispSize.startSizeX = new ParticleSystem.MinMaxCurve(1.2f,2.3f);
            wispSize.startSizeY = new ParticleSystem.MinMaxCurve(3.5f,6f);
            wispSize.startSizeZ = 1f;
            stars = Particles("Distant night stars", 350, 120f, .13f, new Color(.76f,.78f,.94f,.85f), new Vector3(100,35,80), Vector3.zero);
            stars.transform.localPosition = new Vector3(0,30,30);
            var main = stars.main; main.startSpeed = 0; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            volume = effects.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 20; volume.weight = 1;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
            grading = profile.Add<ColorAdjustments>(true);
            bloom = profile.Add<Bloom>(true); bloom.intensity.value = .12f; bloom.threshold.value = 1.1f;
            vignette = profile.Add<Vignette>(true); vignette.intensity.value = .13f; vignette.smoothness.value = .5f;
        }

        ParticleSystem Particles(string title, int max, float life, float size, Color color, Vector3 area, Vector3 velocity)
        {
            var go = new GameObject(title); go.transform.SetParent(effects.transform, false); go.transform.localPosition = Vector3.up * 5;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.maxParticles = max; main.startLifetime = life; main.startSize = new ParticleSystem.MinMaxCurve(size*.55f,size*1.4f);
            main.startColor = color; main.startSpeed = 0; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = area;
            var motion = ps.velocityOverLifetime; motion.enabled = true; motion.space = ParticleSystemSimulationSpace.World;
            motion.x = velocity.x; motion.y = velocity.y; motion.z = velocity.z;
            var noise = ps.noise; noise.enabled = true; noise.strength = .45f; noise.frequency = .18f; noise.scrollSpeed = .25f;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.16f),new GradientAlphaKey(.7f,.75f),new GradientAlphaKey(0,1)}); fade.color = gradient;
            var emission = ps.emission; emission.rateOverTime = 0;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(max + 17); ps.Play();
            return ps;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (RemainingSeconds > 0) { RemainingSeconds -= dt; if (RemainingSeconds <= 0) StopWeather(); }
            float step = transitionSeconds <= 0 ? 1f : dt / transitionSeconds;
            dustWeight = Mathf.MoveTowards(dustWeight, Current == WeatherEvent.DustStorm ? intensity : 0, step);
            solarWeight = Mathf.MoveTowards(solarWeight, Current == WeatherEvent.SolarFlare ? intensity : 0, step);
            heatWeight = Mathf.MoveTowards(heatWeight, Current == WeatherEvent.HeatStorm ? intensity : 0, step);
            coldWeight = Mathf.MoveTowards(coldWeight, Current == WeatherEvent.ColdNight ? intensity : 0, step);
            if (coldWeight > 0 && stars.particleCount < 250) stars.Emit(250 - stars.particleCount);
            DustAmount = dustWeight; SolarAmount = solarWeight; HeatAmount = heatWeight; ColdAmount = coldWeight;
            Emit(dust, dustWeight*210); Emit(haze,dustWeight*7); Emit(sparks,heatWeight*140); Emit(heatWisps,heatWeight*9); Emit(stars,coldWeight*8);
            FadeParticles(dust,dustWeight,new Color(.72f,.39f,.25f,.48f));
            FadeParticles(haze,dustWeight,new Color(.71f,.40f,.29f,.065f));
            FadeParticles(sparks,heatWeight,new Color(.98f,.72f,.47f,.6f));
            FadeParticles(heatWisps,heatWeight,Color.white);
            FadeParticles(stars,coldWeight,new Color(.76f,.78f,.94f,.85f));
            float pulse = .5f + .5f*Mathf.Sin(Time.time*1.2f);
            if (controlAtmosphere)
            {
                Color horizon = Color.Lerp(new Color(.64f,.58f,.55f), new Color(.09f,.08f,.18f),coldWeight);
                horizon = Color.Lerp(horizon,new Color(.64f,.36f,.25f),dustWeight*.8f);
                horizon = Color.Lerp(horizon,new Color(1f,.54f,.30f),heatWeight);
                horizon = Color.Lerp(horizon,new Color(.88f,.43f,.35f),solarWeight*(.7f+.2f*pulse));
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = horizon; RenderSettings.fogDensity = .006f + dustWeight*.027f + heatWeight*.007f + coldWeight*.003f;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.Lerp(new Color(.48f,.44f,.46f),new Color(.23f,.27f,.40f),coldWeight);
                if (sky != null)
                {
                    sky.SetColor("_Horizon",horizon);
                    sky.SetColor("_Zenith",Color.Lerp(new Color(.40f,.40f,.48f),new Color(.018f,.025f,.065f),coldWeight));
                    sky.SetFloat("_SunStrength",(1-coldWeight)*(1-dustWeight*.8f)*(.45f+heatWeight*.55f+solarWeight*(1.5f+pulse)));
                    sky.SetFloat("_Heat",heatWeight);
                }
                if (sun != null)
                {
                    Color daylight = Color.Lerp(new Color(.96f,.88f,.79f),new Color(1f,.69f,.48f),Mathf.Max(heatWeight,solarWeight));
                    sun.color = Color.Lerp(daylight,new Color(.44f,.53f,.8f),coldWeight);
                    sun.intensity = Mathf.Lerp(1.2f,.28f,coldWeight)*(1-dustWeight*.7f)+solarWeight*.4f*pulse;
                }
            }
            grading.colorFilter.value = Color.Lerp(Color.white,new Color(.82f,.88f,1f),coldWeight*.45f);
            grading.saturation.value = -16f * (1f-Mathf.Max(Mathf.Max(heatWeight,solarWeight),Mathf.Max(dustWeight,coldWeight)));
            grading.postExposure.value = -coldWeight*.35f - dustWeight*.15f;
            bloom.intensity.value = .12f + solarWeight*.35f + heatWeight*.15f;
            volume.weight = controlAtmosphere ? 1f : 0f;
        }
        static void Emit(ParticleSystem ps,float rate) { var emission=ps.emission; emission.rateOverTime=rate; }
        void FadeParticles(ParticleSystem ps,float amount,Color color)
        {
            // Renderer opacity also fades living particles; switching events never leaves stale fog banks.
            var renderer=ps.GetComponent<ParticleSystemRenderer>();
            opacityBlock.SetFloat("_Opacity",amount); renderer.SetPropertyBlock(opacityBlock);
        }
        void OnDisable()
        {
            if (saved)
            {
                RenderSettings.skybox=previousSky; RenderSettings.fog=previousFog; RenderSettings.fogMode=previousFogMode;
                RenderSettings.fogDensity=previousFogDensity; RenderSettings.fogColor=previousFogColor;
                RenderSettings.ambientLight=previousAmbient; RenderSettings.ambientMode=previousAmbientMode;
                if(sun!=null) { sun.color=previousSunColor; sun.intensity=previousSunIntensity; sun.transform.rotation=previousSunRotation; }
                saved=false;
            }
            if(effects!=null) Destroy(effects);
            if(profile!=null) { foreach(var component in profile.components) Destroy(component); Destroy(profile); }
            if(sky!=null) Destroy(sky);
            dustWeight=solarWeight=heatWeight=coldWeight=0;
        }
    }
}
