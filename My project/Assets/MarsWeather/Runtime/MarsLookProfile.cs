using UnityEngine;

namespace MarsTrainer.Look
{
    [CreateAssetMenu(menuName = "Mars/P11 Look Profile")]
    public sealed class MarsLookProfile : ScriptableObject
    {
        [Header("Eight shared matte surface colours")]
        public Color terracotta = new Color32(185, 109, 85, 255);
        public Color rose = new Color32(199, 141, 135, 255);
        public Color olive = new Color32(125, 136, 101, 255);
        public Color orange = new Color32(230, 160, 110, 255);
        public Color purple = new Color32(64, 51, 79, 255);
        public Color pink = new Color32(233, 185, 188, 255);
        public Color mauve = new Color32(128, 104, 134, 255);
        public Color sand = new Color32(216, 172, 143, 255);
        [Header("Atmosphere")]
        public float skyMiddleHeight = .14f;
        public float skyZenithHeight = .45f;
        public float skyDomeScale = 1.6f;
        public float previewHour = 16f;
        public float stormSunDimming = .65f;
        public float stormAuroraDimming = .8f;
        public float nightFogBrightness = .6f;
        public float nightGradeBlend = .25f;
        public float hazeAlpha = .09f;
        public float dustAlpha = .45f;
        public float dustFadeIn = .2f;
        public float hazeSpeedScale = .25f;
        public float dustMinimumSizeScale = .5f;
        public int volumePriority = 20;
        public int edgeSortOrder = -10;
        public float heatSkyBlend = .5f;
        public float clearFog = .006f;
        public float stormFog = .038f;
        public float nightFog = .009f;
        public float transitionSeconds = 3f;
        public float sunPitch = 16f;
        public float sunYaw = -35f;
        public Color sunlight = new Color32(255, 225, 196, 255);
        public float warmGradeStrength = .15f;
        public float sunIntensity = 1.05f;
        public float nightIntensity = .12f;
        public float ambientIntensity = 1f;
        public float nightSkyBrightness = .13f;
        public float nightAmbientBrightness = .35f;
        public float hoursPerSol = 24f;
        public float dawnHour = 6f;
        public float duskHour = 18f;
        public float twilightHours = 1.5f;
        [Header("Grade and overlays")]
        public float bloom = .18f;
        public float flareBloom = .5f;
        public float bloomThreshold = 1.1f;
        public float vignette = .14f;
        public float nightExposure = -.65f;
        public float stormExposure = -.2f;
        public float saturation = -7f;
        public float warningFrequency = 1.5f;
        public float flareFlashSeconds = 1.1f;
        public float flareFlash = .55f;
        public float frostOpacity = .7f;
        [Header("Dust: bounded particle budget")]
        public int dustCapacity = 800;
        public float dustRate = 95f;
        public float dustLife = 7f;
        public float dustSize = .12f;
        public Vector3 dustArea = new Vector3(44, 12, 40);
        public Vector3 dustVelocity = new Vector3(-10, -.3f, 2);
        public int hazeCapacity = 32;
        public float hazeRate = 2f;
        public float hazeSize = 6f;
        [Header("Existing heat preview")]
        public int heatCapacity = 180;
        public float heatRate = 30f;
        public float heatSize = .13f;
        public float heatWispSize = 2.2f;
        public float heatWispRate = 3f;
        public Vector3 heatArea = new Vector3(34, 2, 28);
        public Vector3 heatVelocity = new Vector3(.2f, 1.5f, 0);
        [Header("Shared assets; no textures")]
        public Material skyMaterial;
        public Material dustMaterial;
        public Material edgeMaterial;
        public Material safeShell;
        public Material warnShell;
        public Material dangerShell;
        public Material criticalShell;
    }
}
