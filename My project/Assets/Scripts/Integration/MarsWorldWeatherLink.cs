using MarsSurvival.World;
using MarsWeather;
using UnityEngine;

namespace MarsSurvival.Integration
{
    /// <summary>
    /// Connects the streamed terrain (MarsSurvival.World) to the weather module
    /// (MarsWeather) without editing either.
    ///
    /// - The weather controller owns fog, sky and ambient light while it controls the
    ///   atmosphere; the terrain then only sets the camera far clip.
    /// - The weather module's fog densities were tuned for its 60 m preview stage. At
    ///   1:1 they would limit visibility to about 300 m even in clear weather, so this
    ///   rescales them each frame: clear weather fades the terrain out at the terrain's
    ///   fog end distance, and storms keep their ratio to clear weather.
    /// - The weather effects follow the viewer across the 204 km map, because their
    ///   particles are emitted from boxes a few tens of metres around the prefab.
    ///
    /// All of this is visual. Neither module calculates measured Mars weather here.
    /// </summary>
    public sealed class MarsWorldWeatherLink : MonoBehaviour
    {
        public MarsTerrain terrain;
        public WeatherController weather;

        [Tooltip("Keep the weather effects centred on the terrain's viewer, at ground level.")]
        public bool followViewer = true;

        [Tooltip("Rescale the weather module's fog density from its preview-stage scale to the terrain's render distance.")]
        public bool scaleFogToWorld = true;
        [Tooltip("Fog density the weather module uses for clear weather (WeatherController, clear preset).")]
        [Min(0.0001f)] public float weatherClearFogDensity = 0.006f;

        [Tooltip("Keys 1-5 switch Clear, Solar Flare, Dust Storm, Heat Storm and Cold Night. For testing only.")]
        public bool debugKeys = true;

        // sqrt(-ln 0.02): exponential-squared fog leaves 2% of the terrain colour at this many 1/density.
        private const float Exp2FogFullDistanceFactor = 1.978f;

        private Camera viewerCamera;
        private CameraClearFlags previousClearFlags;
        private bool changedClearFlags;

        private void OnDisable()
        {
            if (terrain != null) terrain.manageFogAndSky = true;
            if (changedClearFlags && viewerCamera != null) viewerCamera.clearFlags = previousClearFlags;
            changedClearFlags = false;
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!debugKeys || weather == null) return;
            for (int i = 0; i < 5; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) weather.Activate((WeatherEvent)i, weather.intensity);
            }
#endif
        }

        // LateUpdate runs after WeatherController.Update has written this frame's atmosphere.
        private void LateUpdate()
        {
            if (terrain == null || weather == null || !terrain.IsReady) return;

            bool weatherOwnsAtmosphere = weather.isActiveAndEnabled && weather.controlAtmosphere;
            terrain.manageFogAndSky = !weatherOwnsAtmosphere;

            Transform viewer = terrain.viewer;
            if (followViewer && viewer != null)
            {
                Vector3 p = viewer.position;
                weather.transform.position = new Vector3(p.x, terrain.GetGroundY(p), p.z);
            }

            if (!weatherOwnsAtmosphere) return;

            // The weather draws its own sky, so the camera must clear to the skybox.
            if (viewer != null && (viewerCamera == null || viewerCamera.transform != viewer))
            {
                viewerCamera = viewer.GetComponent<Camera>();
            }
            if (viewerCamera != null && viewerCamera.clearFlags != CameraClearFlags.Skybox)
            {
                if (!changedClearFlags)
                {
                    previousClearFlags = viewerCamera.clearFlags;
                    changedClearFlags = true;
                }
                viewerCamera.clearFlags = CameraClearFlags.Skybox;
            }

            if (scaleFogToWorld && RenderSettings.fogMode == FogMode.ExponentialSquared)
            {
                float clearDensityForWorld = Exp2FogFullDistanceFactor / Mathf.Max(1f, terrain.FogEndDistance);
                RenderSettings.fogDensity *= clearDensityForWorld / weatherClearFogDensity;
            }
        }
    }
}
