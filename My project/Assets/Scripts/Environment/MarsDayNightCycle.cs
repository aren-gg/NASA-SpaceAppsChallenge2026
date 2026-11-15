using System;
using System.Globalization;
using MarsSurvival.World;
using MarsWeather;
using UnityEngine;

namespace MarsSurvival.TimeAndWeather
{
    /// <summary>
    /// The game clock and the Sun. Time is real Mars time (<see cref="MarsTime"/>: Mars
    /// Sol Date, Local Mean Solar Time, solar longitude) for a real UTC instant, run
    /// faster so one sol of 24 h 39 m 35 s passes in <see cref="realSecondsPerSol"/>.
    /// The Sun's direction is the real one for that instant at the viewer's latitude
    /// and longitude on the terrain.
    ///
    /// Lighting: the weather module (when present) writes its sky, fog, ambient and
    /// sun values every frame for a fixed midday; this component runs after it and
    /// scales those values for the time of day. Without the weather module it scales
    /// the scene's own values captured at start. It never edits the weather module.
    ///
    /// The weather sky draws its sun disk at a fixed direction, so that disk is
    /// switched off and a disk that follows the real Sun is drawn here instead.
    ///
    /// Test keys: ] and [ step one Mars hour forward and back.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class MarsDayNightCycle : MonoBehaviour
    {
        public MarsTerrain terrain;
        public Light sun;
        public WeatherController weather;

        [Header("Clock")]
        [Tooltip("Real seconds for one sol (88,775.244 s of Mars time). 1200 = a 20 minute day.")]
        [Min(1f)] public float realSecondsPerSol = 1200f;
        [Tooltip("UTC start, e.g. 2026-03-01T00:00:00Z. Empty = the morning of the first event in the bundled " +
                 "DONKI archive, or the present moment if there is no archive.")]
        public string startUtc = "";
        [Tooltip("The start is moved to this local hour (Local Mean Solar Time). Negative = leave as is.")]
        public float startLocalHour = 7f;
        public bool paused;
        public bool debugKeys = true;

        [Header("Night (fractions of the daytime values)")]
        [Range(0f, 1f)] public float nightSkyBrightness = 0.06f;
        [Tooltip("Kept above real darkness so the ground stays readable.")]
        [Range(0f, 1f)] public float nightAmbient = 0.22f;

        [Header("Sun disk")]
        [Tooltip("Apparent diameter in degrees. The real Sun from Mars is about 0.35; larger reads better.")]
        public float sunDiskDegrees = 0.6f;

        /// <summary>Simulated UTC instant as Unix seconds.</summary>
        public double UnixNow { get; private set; }
        /// <summary>Mars time and Sun position for the viewer's location.</summary>
        public MarsTime.State Now { get; private set; }
        public Vector2 LatLon { get; private set; }
        /// <summary>How many times faster than real time the clock runs (about 74 for a 20 minute sol).</summary>
        public double TimeScale { get { return MarsTime.SolSeconds / Mathf.Max(1f, realSecondsPerSol); } }
        /// <summary>Direct sunlight, 0 (Sun down) to 1 (Sun well up).</summary>
        public float Daylight { get; private set; }
        /// <summary>Sky brightness, 0 (night) to 1 (day); twilight is in between.</summary>
        public float SkyLight { get; private set; }
        /// <summary>World-space unit vector toward the Sun.</summary>
        public Vector3 SunDirection { get; private set; }
        public DateTime UtcNow { get { return DateTimeOffset.FromUnixTimeMilliseconds((long)(UnixNow * 1000.0)).UtcDateTime; } }

        private const float GlowQuadScale = 12f; // the quad is this many disk diameters wide, for the glow

        private bool started;
        private Color baseFog, baseAmbientLight, baseAmbientSky, baseAmbientEquator, baseAmbientGround, baseSunColor;
        private float baseSunIntensity;
        private Transform disk;
        private Material diskMaterial;
        private Texture2D diskTexture;
        private Mesh diskMesh;

        private void Start()
        {
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            if (weather == null) weather = FindObjectOfType<WeatherController>();
            if (sun == null) sun = RenderSettings.sun;

            baseFog = RenderSettings.fogColor;
            baseAmbientLight = RenderSettings.ambientLight;
            baseAmbientSky = RenderSettings.ambientSkyColor;
            baseAmbientEquator = RenderSettings.ambientEquatorColor;
            baseAmbientGround = RenderSettings.ambientGroundColor;
            if (sun != null)
            {
                baseSunColor = sun.color;
                baseSunIntensity = sun.intensity;
            }

            // An explicit start moves forward to the wanted hour; the default start moves back to
            // the morning before the archive's first event, so that event is not missed.
            double start;
            bool backward = false;
            if (!TryParseUtc(startUtc, out start))
            {
                MarsWeatherArchive archive = MarsWeatherArchive.Load();
                if (archive != null && archive.events != null && archive.events.Length > 0)
                {
                    start = archive.events[0].startUnix;
                    backward = true;
                }
                else
                {
                    start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                }
            }
            UpdateLocation();
            if (startLocalHour >= 0f)
            {
                double clock = MarsTime.Compute(start, LatLon.x, LatLon.y).localMeanSolarTimeHours;
                double shift = backward ? -MarsTime.Wrap(clock - startLocalHour, 24.0) : MarsTime.Wrap(startLocalHour - clock, 24.0);
                start += shift / 24.0 * MarsTime.SolSeconds;
            }
            UnixNow = start;
            CreateSunDisk();
            started = true;
            Refresh();
        }

        public static bool TryParseUtc(string text, out double unixSeconds)
        {
            unixSeconds = 0;
            DateTimeOffset parsed;
            if (string.IsNullOrWhiteSpace(text) ||
                !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return false;
            }
            unixSeconds = parsed.ToUnixTimeMilliseconds() / 1000.0;
            return true;
        }

        /// <summary>Jumps the clock to a UTC instant (Unix seconds).</summary>
        public void SetTime(double unixSeconds)
        {
            UnixNow = unixSeconds;
            Refresh();
        }

        private void Update()
        {
            if (!started) return;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (debugKeys)
            {
                if (Input.GetKeyDown(KeyCode.RightBracket)) UnixNow += MarsTime.SolSeconds / 24.0;
                if (Input.GetKeyDown(KeyCode.LeftBracket)) UnixNow -= MarsTime.SolSeconds / 24.0;
            }
#endif
            if (!paused) UnixNow += Time.deltaTime * TimeScale;
            Refresh();
        }

        private void UpdateLocation()
        {
            Vector2 latLon = new Vector2((float)MarsTime.CuriosityLatitude, (float)MarsTime.CuriosityLongitudeEast);
            if (terrain != null && terrain.IsReady && terrain.viewer != null)
            {
                latLon = terrain.GetLatLon(terrain.viewer.position);
            }
            LatLon = latLon;
        }

        private void Refresh()
        {
            UpdateLocation();
            MarsTime.State state = MarsTime.Compute(UnixNow, LatLon.x, LatLon.y);
            Now = state;

            // Terrain local axes: +X east, +Y up, +Z north.
            Vector3 direction = new Vector3((float)state.sunEast, (float)state.sunUp, (float)state.sunNorth);
            if (terrain != null) direction = terrain.transform.TransformDirection(direction);
            SunDirection = direction.normalized;
            if (sun != null) sun.transform.rotation = Quaternion.LookRotation(-SunDirection, Vector3.up);

            float elevation = (float)state.sunElevation;
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 10f, elevation));
            SkyLight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-12f, 5f, elevation));
        }

        // After WeatherController.Update (and the terrain link) have written this frame's daytime values.
        private void LateUpdate()
        {
            if (!started) return;
            float elevation = (float)Now.sunElevation;
            bool weatherOwns = weather != null && weather.isActiveAndEnabled && weather.controlAtmosphere;
            float cold = weatherOwns ? weather.ColdAmount : 0f;
            float dust = weather != null ? weather.DustAmount : 0f;
            float solar = weather != null ? weather.SolarAmount : 0f;

            // The weather module's Cold Night look is already dark, so do not darken it twice.
            float skyScale = Mathf.Lerp(nightSkyBrightness, 1f, SkyLight);
            skyScale = Mathf.Lerp(skyScale, Mathf.Max(skyScale, 0.8f), cold);
            float ambientScale = Mathf.Lerp(nightAmbient, 1f, SkyLight);
            ambientScale = Mathf.Lerp(ambientScale, Mathf.Max(ambientScale, 0.6f), cold);

            // Sunlight follows the inverse square of the real Sun distance (about +-19% over a Mars year).
            float distance = 1.523679f / (float)Now.sunDistanceAu;
            float sunScale = Daylight * distance * distance;

            // On Mars the low Sun and the sky around it turn blue: dust scatters blue light forward.
            float lowSun = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 25f, elevation));
            Color sunTint = Color.Lerp(Color.white, new Color(0.80f, 0.88f, 1f), lowSun);
            float twilight = SkyLight * (1f - Daylight);

            if (weatherOwns)
            {
                RenderSettings.fogColor = Twilight(RenderSettings.fogColor, twilight) * skyScale;
                RenderSettings.ambientLight *= ambientScale;
                Material sky = RenderSettings.skybox;
                if (sky != null && sky.HasProperty("_Horizon"))
                {
                    sky.SetColor("_Horizon", Twilight(sky.GetColor("_Horizon"), twilight) * skyScale);
                    sky.SetColor("_Zenith", sky.GetColor("_Zenith") * skyScale);
                    sky.SetFloat("_SunStrength", 0f); // its disk has a fixed direction; ours follows the Sun
                }
            }
            else
            {
                RenderSettings.fogColor = Twilight(baseFog, twilight) * skyScale;
                RenderSettings.ambientLight = baseAmbientLight * ambientScale;
                RenderSettings.ambientSkyColor = baseAmbientSky * ambientScale;
                RenderSettings.ambientEquatorColor = baseAmbientEquator * ambientScale;
                RenderSettings.ambientGroundColor = baseAmbientGround * ambientScale;
            }

            if (sun != null)
            {
                bool weatherSetsSun = weatherOwns && weather.sun == sun;
                sun.intensity = (weatherSetsSun ? sun.intensity : baseSunIntensity) * sunScale;
                sun.color = (weatherSetsSun ? sun.color : baseSunColor) * sunTint;
            }

            UpdateSunDisk(elevation, dust, solar, sunTint);
        }

        private static Color Twilight(Color color, float twilight)
        {
            float luminance = 0.3f * color.r + 0.59f * color.g + 0.11f * color.b;
            Color blue = new Color(0.62f, 0.78f, 1.08f) * luminance;
            return Color.Lerp(color, blue, twilight * 0.45f);
        }

        // ------------------------------------------------------------------ sun disk

        private void CreateSunDisk()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            const int size = 128;
            float coreRadius = 1f / GlowQuadScale; // as a fraction of the half-width
            diskTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            diskTexture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = 1f - Mathf.SmoothStep(coreRadius * 0.8f, coreRadius * 1.2f, r);
                    float glow = Mathf.Exp(-r * 7f) * 0.55f * Mathf.Clamp01(1f - r);
                    byte alpha = (byte)(Mathf.Clamp01(Mathf.Max(core, glow)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            diskTexture.SetPixels32(pixels);
            diskTexture.Apply(false, true);

            diskMaterial = new Material(shader);
            diskMaterial.mainTexture = diskTexture;

            diskMesh = new Mesh();
            diskMesh.name = "Sun Disk";
            diskMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            diskMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            diskMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };

            GameObject go = new GameObject("Sun Disk");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = diskMesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = diskMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            disk = go.transform;
        }

        private void UpdateSunDisk(float elevation, float dust, float solar, Color tint)
        {
            if (disk == null) return;
            Camera cam = Camera.main;
            float visible = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 1f, elevation)) * (1f - dust * 0.85f);
            if (cam == null || visible <= 0.001f)
            {
                if (disk.gameObject.activeSelf) disk.gameObject.SetActive(false);
                return;
            }
            if (!disk.gameObject.activeSelf) disk.gameObject.SetActive(true);

            // Just inside the far plane, so terrain in front of the Sun hides it.
            float distance = cam.farClipPlane * 0.9f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.2f);
            float flare = solar * (0.6f + 0.4f * pulse);
            float width = 2f * distance * Mathf.Tan(sunDiskDegrees * GlowQuadScale * 0.5f * Mathf.Deg2Rad);
            disk.position = cam.transform.position + SunDirection * distance;
            disk.rotation = Quaternion.LookRotation(SunDirection, Vector3.up);
            disk.localScale = Vector3.one * width * (1f + 0.5f * flare);

            Color color = tint * (1.6f + 1.4f * flare);
            color.a = visible;
            diskMaterial.color = color;
        }

        private void OnDestroy()
        {
            if (diskMaterial != null) Destroy(diskMaterial);
            if (diskTexture != null) Destroy(diskTexture);
            if (diskMesh != null) Destroy(diskMesh);
        }
    }
}
