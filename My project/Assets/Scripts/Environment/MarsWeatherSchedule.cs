using MarsWeather;
using UnityEngine;

namespace MarsSurvival.TimeAndWeather
{
    /// <summary>
    /// Decides which weather event is happening at the current Mars time and tells the
    /// weather module to show it. It only calls the module's public API.
    ///
    /// Where each event comes from:
    /// - Solar events (Solar Flare visuals): NASA CCMC DONKI, replayed from the bundled
    ///   archive at the time each one reached Mars. Flares are light, so they show only
    ///   while the Sun is up where the viewer stands; CME arrivals and particle events
    ///   show day or night.
    /// - Dust storms: DONKI has none, so they are simulated. About one every 30 sols in
    ///   the dust-storm season (Ls 180-360) and one every 90 sols otherwise, each lasting
    ///   1-4 sols. Seeded, so the same sol always has the same weather.
    /// - Heat storms: not a recorded Mars phenomenon and not in DONKI. Simulated as one
    ///   hot afternoon about every 30 sols, around the early-afternoon temperature peak.
    /// - Cold nights: every night. Curiosity's REMS station records nights far below
    ///   freezing all year at Gale Crater, so this follows the Sun rather than a schedule.
    ///
    /// One event shows at a time: solar, then dust, then heat, then the cold night.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class MarsWeatherSchedule : MonoBehaviour
    {
        public enum Source { None, Donki, Simulated, DayNight }

        public MarsDayNightCycle cycle;
        public WeatherController weather;

        [Tooltip("Turn off to drive the weather by hand.")]
        public bool automatic = true;
        [Tooltip("After something else changes the weather (keys F5-F9), wait this long before taking over again.")]
        public float manualHoldSeconds = 45f;

        [Header("DONKI solar events")]
        [Tooltip("Events are shown for at least this many hours of Mars time, so a short flare is not over " +
                 "in a few seconds at 74x speed.")]
        public float minimumEventHours = 1.5f;

        [Header("Simulated events (not NASA data)")]
        public int seed = 2026;
        [Tooltip("Average sols between dust storms in the dust-storm season, Ls 180-360.")]
        public float dustIntervalSols = 30f;
        [Tooltip("Dust storms outside the season are this many times rarer.")]
        public float offSeasonRarity = 3f;
        public Vector2 dustDurationSols = new Vector2(1f, 4f);
        [Tooltip("Average sols between heat storms.")]
        public float heatIntervalSols = 30f;

        /// <summary>The event being shown.</summary>
        public WeatherEvent Current { get; private set; }
        public float CurrentStrength { get; private set; }
        public string CurrentLabel { get; private set; }
        public Source CurrentSource { get; private set; }
        /// <summary>Strength 0-1 of each cause right now, whether or not it is the one shown.</summary>
        public float SolarStrength { get; private set; }
        public float DustStrength { get; private set; }
        public float HeatStrength { get; private set; }
        public float NightAmount { get; private set; }
        /// <summary>The DONKI event in progress, or null.</summary>
        public MarsWeatherArchive.SolarEvent ActiveSolarEvent { get; private set; }
        /// <summary>The next DONKI event to reach Mars, or null.</summary>
        public MarsWeatherArchive.SolarEvent NextSolarEvent { get; private set; }
        /// <summary>Mars hours until <see cref="NextSolarEvent"/>.</summary>
        public double NextSolarEventMarsHours { get; private set; }
        public bool HasArchive { get { return archive != null; } }
        /// <summary>False once the clock has run past the end of the bundled archive.</summary>
        public bool InsideArchiveWindow { get; private set; }

        private const double MarsHourSeconds = MarsTime.SolSeconds / 24.0;

        private MarsWeatherArchive archive;
        private WeatherEvent lastApplied;
        private bool hasApplied;
        private float holdUntil;

        private struct DustStorm { public bool exists; public double startSol, durationSols; public float peak; }
        private long dustBlockCached = long.MinValue;
        private DustStorm dustHere, dustBefore;

        private void Start()
        {
            if (cycle == null) cycle = FindObjectOfType<MarsDayNightCycle>();
            if (weather == null) weather = FindObjectOfType<WeatherController>();
            archive = MarsWeatherArchive.Load();
            CurrentLabel = "Clear";
        }

        private void Update()
        {
            if (cycle == null) return;
            MarsTime.State now = cycle.Now;
            double unix = cycle.UnixNow;

            EvaluateSolar(unix, now.sunElevation > 0.0);
            DustStrength = EvaluateDust(unix, now);
            HeatStrength = EvaluateHeat(now);
            NightAmount = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-9f, 1f, (float)now.sunElevation));

            if (SolarStrength > 0f)
            {
                Set(WeatherEvent.SolarFlare, SolarStrength, ActiveSolarEvent.label, Source.Donki);
            }
            else if (DustStrength > 0f)
            {
                Set(WeatherEvent.DustStorm, DustStrength, "Dust storm", Source.Simulated);
            }
            else if (HeatStrength > 0f)
            {
                Set(WeatherEvent.HeatStorm, HeatStrength, "Heat storm", Source.Simulated);
            }
            else if (NightAmount > 0.02f)
            {
                Set(WeatherEvent.ColdNight, NightAmount, "Cold night", Source.DayNight);
            }
            else
            {
                Set(WeatherEvent.Clear, 1f, "Clear", Source.None);
            }
            Apply();
        }

        private void Set(WeatherEvent weatherEvent, float strength, string label, Source source)
        {
            Current = weatherEvent;
            CurrentStrength = Mathf.Clamp01(strength);
            CurrentLabel = label;
            CurrentSource = source;
        }

        private void Apply()
        {
            if (weather == null || !automatic) return;
            // Someone else (the F5-F9 test keys, another script) changed the weather: let it stand for a while.
            if (hasApplied && weather.Current != lastApplied) holdUntil = Time.time + manualHoldSeconds;
            if (Time.time < holdUntil)
            {
                lastApplied = weather.Current;
                return;
            }
            if (!hasApplied || weather.Current != Current) weather.Activate(Current, CurrentStrength);
            else weather.intensity = CurrentStrength;
            lastApplied = Current;
            hasApplied = true;
        }

        // ------------------------------------------------------------------ DONKI

        private void EvaluateSolar(double unix, bool sunIsUp)
        {
            SolarStrength = 0f;
            ActiveSolarEvent = null;
            NextSolarEvent = null;
            InsideArchiveWindow = archive != null && unix >= archive.windowStartUnix && unix <= archive.windowEndUnix;
            if (archive == null) return;

            MarsWeatherArchive.SolarEvent[] events = archive.events;
            for (int i = 0; i < events.Length; i++)
            {
                MarsWeatherArchive.SolarEvent e = events[i];
                double duration = Mathf.Max(e.durationHours * 3600f, minimumEventHours * (float)MarsHourSeconds);
                if (unix < e.startUnix)
                {
                    // Events are sorted by start time, so the first one still ahead is the next.
                    NextSolarEvent = e;
                    NextSolarEventMarsHours = (e.startUnix - unix) / MarsHourSeconds;
                    break;
                }
                if (unix > e.startUnix + duration) continue;
                if (e.kind == "flare" && !sunIsUp) continue; // flare light does not reach the night side
                if (e.strength > SolarStrength)
                {
                    SolarStrength = Mathf.Clamp01(e.strength);
                    ActiveSolarEvent = e;
                }
            }
        }

        // ------------------------------------------------------------------ simulated dust

        private float EvaluateDust(double unix, MarsTime.State now)
        {
            float interval = Mathf.Max(1f, dustIntervalSols);
            long block = (long)System.Math.Floor(now.localSolDate / interval);
            if (block != dustBlockCached)
            {
                dustBlockCached = block;
                dustHere = MakeDustStorm(block, interval, unix, now);
                dustBefore = MakeDustStorm(block - 1, interval, unix, now); // a storm can run into the next block
            }
            return Mathf.Max(DustEnvelope(dustHere, now.localSolDate), DustEnvelope(dustBefore, now.localSolDate));
        }

        private DustStorm MakeDustStorm(long block, float interval, double unix, MarsTime.State now)
        {
            DustStorm storm = new DustStorm();
            // Season at the middle of the block decides how likely a storm is.
            double middleSol = (block + 0.5) * interval;
            double middleUnix = unix + (middleSol - now.localSolDate) * MarsTime.SolSeconds;
            double ls = MarsTime.Compute(middleUnix, cycle.LatLon.x, cycle.LatLon.y).solarLongitude;
            bool dustSeason = ls >= 180.0;
            float chance = dustSeason ? 1f : 1f / Mathf.Max(1f, offSeasonRarity);

            if (Hash(block, 1) >= chance) return storm;
            storm.exists = true;
            storm.durationSols = Mathf.Lerp(dustDurationSols.x, dustDurationSols.y, Hash(block, 3));
            storm.startSol = block * (double)interval + Hash(block, 2) * Mathf.Max(0f, interval - (float)storm.durationSols);
            storm.peak = Mathf.Lerp(0.45f, dustSeason ? 0.95f : 0.7f, Hash(block, 4));
            return storm;
        }

        private static float DustEnvelope(DustStorm storm, double solDate)
        {
            if (!storm.exists) return 0f;
            double into = solDate - storm.startSol, left = storm.startSol + storm.durationSols - solDate;
            if (into <= 0 || left <= 0) return 0f;
            // Builds and clears over a quarter of a sol.
            return storm.peak * Mathf.Clamp01((float)System.Math.Min(into, left) / 0.25f);
        }

        // ------------------------------------------------------------------ simulated heat

        private float EvaluateHeat(MarsTime.State now)
        {
            float interval = Mathf.Max(1f, heatIntervalSols);
            long block = (long)System.Math.Floor(now.localSol / (double)interval);
            long hotSol = block * (long)interval + (long)(Hash(block, 11) * interval);
            if (now.localSol != hotSol) return 0f;

            // 11:00 to 17:00 true solar time, peaking at 14:00.
            float t = Mathf.InverseLerp(11f, 17f, (float)now.localTrueSolarTimeHours);
            if (t <= 0f || t >= 1f) return 0f;
            // Warmer around perihelion (southern spring and summer, Ls 180-360).
            float peak = Mathf.Lerp(now.solarLongitude >= 180.0 ? 0.7f : 0.5f, 1f, Hash(block, 12));
            return peak * Mathf.Sin(t * Mathf.PI);
        }

        /// <summary>Repeatable 0-1 value for a block of sols.</summary>
        private float Hash(long block, int salt)
        {
            unchecked
            {
                uint h = (uint)(block * 374761393L) ^ (uint)(seed * 668265263) ^ (uint)(salt * 2246822519u);
                h = (h ^ (h >> 15)) * 2246822519u;
                h = (h ^ (h >> 13)) * 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
