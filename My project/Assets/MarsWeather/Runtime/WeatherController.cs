using System;
using MarsTrainer.Look;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MarsWeather
{
    public enum WeatherEvent { Clear, SolarFlare, DustStorm, HeatStorm, ColdNight }

    /// <summary>Presentation only. Gameplay supplies sol hour and independent weather weights.</summary>
    public sealed class WeatherController : MonoBehaviour
    {
        public Light sun;
        public Material skyTemplate;
        public Material particleMaterial;
        public MarsLookProfile look;
        [Range(0, 1)] public float intensity = 1;
        [Min(0)] public float transitionSeconds = 3;
        public bool controlAtmosphere = true;
        [Range(0, 24)] public float solHour = 16;
        public event Action<WeatherEvent> WeatherChanged;
        public WeatherEvent Current { get; private set; }
        public float DustAmount { get; private set; }
        public float SolarAmount { get; private set; }
        public float HeatAmount { get; private set; }
        public float ColdAmount { get; private set; }
        public float RemainingSeconds { get; private set; }
        public float NightAmount { get; private set; }
        public int ParticleCount => dust == null ? 0 : dust.particleCount + haze.particleCount + heatFlecks.particleCount + heatWisps.particleCount;
        private Material sky, edges;
        private ParticleSystem dust, haze, heatFlecks, heatWisps;
        private ParticleSystemRenderer dustRenderer, hazeRenderer, heatRenderer, wispRenderer;
        private VolumeProfile profile;
        private Volume volume;
        private ColorAdjustments grading;
        private Bloom bloom;
        private Vignette vignette;
        private GameObject effects;
        private Transform skyDome;
        private Camera viewerCamera;
        private MaterialPropertyBlock opacity;
        private bool driven, warning, ownsLook, saved;
        private float targetDust, targetSolar, targetHeat, targetCold, elapsed, flashRemaining;
        private Material previousSky;
        private bool previousFog;
        private FogMode previousFogMode;
        private float previousFogDensity, previousSunIntensity;
        private Color previousFogColor, previousAmbient, previousSunColor, previousShadow;
        private AmbientMode previousAmbientMode;
        private Quaternion previousSunRotation;

        public void Activate(WeatherEvent weather, float strength = 1, float durationSeconds = 0)
        {
            driven = false;
            if (weather == WeatherEvent.SolarFlare && Current != weather && look != null) flashRemaining = look.flareFlashSeconds;
            Current = weather;
            intensity = Mathf.Clamp01(strength);
            RemainingSeconds = Mathf.Max(0, durationSeconds);
            solHour = weather == WeatherEvent.ColdNight ? 0 : look == null ? 16 : look.previewHour;
            WeatherChanged?.Invoke(Current);
        }
        public void StopWeather() { Activate(WeatherEvent.Clear); }
        public void SetPresentation(float dust, float solar, float heat, float cold, float hour, bool warned)
        {
            if (solar > targetSolar && targetSolar <= 0 && look != null) flashRemaining = look.flareFlashSeconds;
            driven = true;
            targetDust = Mathf.Clamp01(dust); targetSolar = Mathf.Clamp01(solar);
            targetHeat = Mathf.Clamp01(heat); targetCold = Mathf.Clamp01(cold);
            solHour = Mathf.Repeat(hour, look == null ? 24 : look.hoursPerSol);
            warning = warned;
        }
        public void ReleasePresentation()
        {
            driven = false; warning = false;
            targetDust = targetSolar = targetHeat = targetCold = 0;
            Current = WeatherEvent.Clear;
        }
        private void OnEnable()
        {
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<MarsLookProfile>(); ownsLook = true;
                Debug.LogWarning("P11 look profile missing; using default look settings.", this);
            }
            previousSky = RenderSettings.skybox; previousFog = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode; previousFogDensity = RenderSettings.fogDensity;
            previousFogColor = RenderSettings.fogColor; previousAmbient = RenderSettings.ambientLight;
            previousAmbientMode = RenderSettings.ambientMode; previousShadow = RenderSettings.subtractiveShadowColor;
            if (sun != null)
            {
                previousSunColor = sun.color; previousSunIntensity = sun.intensity; previousSunRotation = sun.transform.rotation;
            }
            saved = controlAtmosphere;
            var template = look.skyMaterial != null ? look.skyMaterial : skyTemplate;
            if (template != null) { sky = new Material(template); sky.renderQueue = (int)RenderQueue.Background; }
            effects = new GameObject("P11 atmosphere"); effects.transform.SetParent(transform, false);
            opacity = new MaterialPropertyBlock();
            dust = CreateDust("Windblown dust", look.dustCapacity, look.dustSize, look.dustArea, look.dustVelocity, look.terracotta);
            haze = CreateDust("Ground haze", look.hazeCapacity, look.hazeSize, look.dustArea, look.dustVelocity * look.hazeSpeedScale, look.rose);
            dustRenderer = dust.GetComponent<ParticleSystemRenderer>(); hazeRenderer = haze.GetComponent<ParticleSystemRenderer>();
            heatFlecks = CreateDust("Thermal flecks", look.heatCapacity, look.heatSize, look.heatArea, look.heatVelocity, look.orange);
            heatWisps = CreateDust("Heat wisps", look.hazeCapacity, look.heatWispSize, look.heatArea, look.heatVelocity, look.sand);
            heatRenderer = heatFlecks.GetComponent<ParticleSystemRenderer>(); wispRenderer = heatWisps.GetComponent<ParticleSystemRenderer>();
            volume = effects.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = look.volumePriority;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
            grading = profile.Add<ColorAdjustments>(true);
            bloom = profile.Add<Bloom>(true); bloom.threshold.value = look.bloomThreshold;
            vignette = profile.Add<Vignette>(true); vignette.intensity.value = look.vignette;
            if (look.edgeMaterial != null)
            {
                edges = new Material(look.edgeMaterial);
                var canvas = new GameObject("Visor edges", typeof(Canvas)); canvas.transform.SetParent(effects.transform, false);
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.GetComponent<Canvas>().sortingOrder = look.edgeSortOrder;
                var image = new GameObject("Frost and flare warning", typeof(RectTransform), typeof(RawImage));
                image.transform.SetParent(canvas.transform, false);
                var rect = (RectTransform)image.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var graphic = image.GetComponent<RawImage>(); graphic.texture = Texture2D.whiteTexture;
                graphic.material = edges; graphic.raycastTarget = false;
            }
            EnablePostProcessing(Camera.main);
            AdvanceVisuals(0);
        }
        private ParticleSystem CreateDust(string label, int capacity, float size, Vector3 area, Vector3 velocity, Color tint)
        {
            var go = new GameObject(label); go.transform.SetParent(effects.transform, false);
            go.transform.localPosition = Vector3.up * area.y / 2;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.maxParticles = capacity; main.startLifetime = look.dustLife;
            main.startSize = new ParticleSystem.MinMaxCurve(size * look.dustMinimumSizeScale, size); main.startSpeed = 0;
            tint.a = size == look.hazeSize ? look.hazeAlpha : look.dustAlpha; main.startColor = tint;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = area;
            var motion = ps.velocityOverLifetime; motion.enabled = true; motion.space = ParticleSystemSimulationSpace.World;
            motion.x = velocity.x; motion.y = velocity.y; motion.z = velocity.z;
            var emission = ps.emission; emission.rateOverTime = 0;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, look.dustFadeIn), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = look.dustMaterial != null ? look.dustMaterial : particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)capacity; ps.Play();
            return ps;
        }
        public void EnablePostProcessing(Camera camera)
        {
            if (camera == null) return;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; camera.allowHDR = true;
            viewerCamera = camera;
            if (!controlAtmosphere || sky == null || skyDome != null) return;
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Procedural Shader Graph sky dome";
            dome.transform.SetParent(effects.transform, false);
            dome.GetComponent<Collider>().enabled = false;
            var renderer = dome.GetComponent<MeshRenderer>(); renderer.sharedMaterial = sky;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            skyDome = dome.transform;
            skyDome.localScale = Vector3.one * camera.farClipPlane * look.skyDomeScale;
            skyDome.position = camera.transform.position;
        }
        private void LateUpdate()
        {
            if (skyDome != null && viewerCamera != null) skyDome.position = viewerCamera.transform.position;
        }
        private void Update() { AdvanceVisuals(Time.deltaTime); }
        public void AdvanceVisuals(float deltaSeconds)
        {
            if (look == null || effects == null) return;
            elapsed += deltaSeconds;
            if (!driven)
            {
                if (RemainingSeconds > 0) { RemainingSeconds -= deltaSeconds; if (RemainingSeconds <= 0) StopWeather(); }
                targetDust = Current == WeatherEvent.DustStorm ? intensity : 0;
                targetSolar = Current == WeatherEvent.SolarFlare ? intensity : 0;
                targetHeat = Current == WeatherEvent.HeatStorm ? intensity : 0;
                targetCold = Current == WeatherEvent.ColdNight ? intensity : 0;
            }
            float step = transitionSeconds <= 0 ? 1 : deltaSeconds / transitionSeconds;
            DustAmount = Mathf.MoveTowards(DustAmount, targetDust, step);
            SolarAmount = Mathf.MoveTowards(SolarAmount, targetSolar, step);
            HeatAmount = Mathf.MoveTowards(HeatAmount, targetHeat, step);
            float daylight = Mathf.SmoothStep(0, 1, Mathf.Min((solHour - look.dawnHour + look.twilightHours) / look.twilightHours,
                (look.duskHour + look.twilightHours - solHour) / look.twilightHours));
            NightAmount = 1 - daylight;
            ColdAmount = Mathf.MoveTowards(ColdAmount, targetCold * NightAmount, step);
            Emit(dust, DustAmount * look.dustRate); Emit(haze, DustAmount * look.hazeRate);
            opacity.SetFloat("_Opacity", DustAmount); dustRenderer.SetPropertyBlock(opacity); hazeRenderer.SetPropertyBlock(opacity);
            Emit(heatFlecks, HeatAmount * look.heatRate); Emit(heatWisps, HeatAmount * look.heatWispRate);
            opacity.SetFloat("_Opacity", HeatAmount); heatRenderer.SetPropertyBlock(opacity); wispRenderer.SetPropertyBlock(opacity);
            flashRemaining = Mathf.Max(0, flashRemaining - deltaSeconds);
            float flash = flashRemaining / Mathf.Max(.001f, look.flareFlashSeconds) * look.flareFlash;
            if (controlAtmosphere)
            {
                if (sky != null) RenderSettings.skybox = null;
                Color horizon = Color.Lerp(look.orange, look.purple * look.nightSkyBrightness, NightAmount);
                horizon = Color.Lerp(horizon, look.terracotta, Mathf.Max(DustAmount, HeatAmount * look.heatSkyBlend));
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(Color.Lerp(look.mauve, look.purple * look.nightFogBrightness, NightAmount), look.rose, DustAmount);
                RenderSettings.fogDensity = Mathf.Lerp(Mathf.Lerp(look.clearFog, look.nightFog, NightAmount), look.stormFog, DustAmount);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = look.mauve * Mathf.Lerp(look.ambientIntensity, look.nightAmbientBrightness, NightAmount);
                RenderSettings.subtractiveShadowColor = look.purple;
                Shader.SetGlobalVector("_MarsGradient", new Vector4(look.skyMiddleHeight, look.skyZenithHeight, 0, 0));
                Shader.SetGlobalColor("_MarsHorizon", horizon);
                Shader.SetGlobalColor("_MarsMiddle", Color.Lerp(look.rose, look.purple * look.nightSkyBrightness, NightAmount));
                Shader.SetGlobalColor("_MarsZenith", Color.Lerp(look.purple, look.purple * look.nightSkyBrightness, NightAmount));
                Shader.SetGlobalFloat("_MarsSun", daylight * (1 - DustAmount));
                Shader.SetGlobalFloat("_MarsNight", NightAmount * (1 - DustAmount));
                Shader.SetGlobalFloat("_MarsAurora", SolarAmount * (1 - DustAmount * look.stormAuroraDimming));
                Shader.SetGlobalFloat("_MarsFlash", flash);
                if (sun != null)
                {
                    float arc = Mathf.Sin((solHour - look.dawnHour) / (look.duskHour - look.dawnHour) * Mathf.PI);
                    sun.transform.rotation = Quaternion.Euler(look.sunPitch * arc, look.sunYaw, 0);
                    sun.color = look.sunlight; sun.intensity = Mathf.Lerp(look.nightIntensity, look.sunIntensity, daylight) * (1 - DustAmount * look.stormSunDimming);
                    Shader.SetGlobalVector("_MarsSunDirection", -sun.transform.forward);
                }
            }
            grading.colorFilter.value = Color.Lerp(Color.Lerp(Color.white, look.sand, look.warmGradeStrength), look.mauve, NightAmount * look.nightGradeBlend);
            grading.saturation.value = look.saturation;
            grading.postExposure.value = look.nightExposure * NightAmount + look.stormExposure * DustAmount;
            bloom.intensity.value = look.bloom + SolarAmount * look.flareBloom;
            volume.weight = controlAtmosphere ? 1 : 0;
            if (edges != null)
            {
                edges.SetFloat("_Frost", ColdAmount * look.frostOpacity);
                edges.SetFloat("_Warning", Mathf.Max(SolarAmount, warning ? 1 : 0));
                edges.SetFloat("_Frequency", look.warningFrequency);
            }
        }
        private static void Emit(ParticleSystem ps, float rate) { var emission = ps.emission; emission.rateOverTime = rate; }
        private static void Dispose(UnityEngine.Object asset) { if (asset == null) return; if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset); }
        private void OnDisable()
        {
            if (saved)
            {
                RenderSettings.skybox = previousSky; RenderSettings.fog = previousFog; RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogDensity = previousFogDensity; RenderSettings.fogColor = previousFogColor;
                RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientLight = previousAmbient;
                RenderSettings.subtractiveShadowColor = previousShadow;
                if (sun != null) { sun.color = previousSunColor; sun.intensity = previousSunIntensity; sun.transform.rotation = previousSunRotation; }
                saved = false;
            }
            Dispose(effects); Dispose(sky); Dispose(edges);
            if (profile != null) { for (int i = 0; i < profile.components.Count; i++) Dispose(profile.components[i]); Dispose(profile); }
            if (ownsLook) { Dispose(look); look = null; ownsLook = false; }
            DustAmount = SolarAmount = HeatAmount = ColdAmount = 0;
        }
    }
}
