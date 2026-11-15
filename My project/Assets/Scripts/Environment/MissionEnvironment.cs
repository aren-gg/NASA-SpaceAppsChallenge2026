using System;
using MarsSurvival.TimeAndWeather;
using UnityEngine;

namespace MarsSurvival.Simulation
{
    /// <summary>Physical inputs are independent of weather shaders and display strengths.</summary>
    public sealed class MissionEnvironment
    {
        public const string ProfileResource = "mission_environment_profile";
        private readonly EnvironmentProfile profile;
        private readonly MarsWeatherArchive archive;

        [Serializable]
        public sealed class TemperaturePoint
        {
            public double hour, temperatureC;
        }

        [Serializable]
        public sealed class EnvironmentProfile
        {
            public string id = "gale-representative-model-v1";
            public string temperatureSource = "Representative Gale temperature model; not REMS observations";
            public double backgroundMicrograyPerEarthDay = 210;
            public double simulatedParticlePeakMicrograyPerHour = 500;
            public TemperaturePoint[] temperatures;
        }

        public MissionEnvironment() : this(null, null) { }

        public MissionEnvironment(EnvironmentProfile temperatureProfile, MarsWeatherArchive solarArchive = null)
        {
            profile = Valid(temperatureProfile) ? temperatureProfile : DefaultProfile();
            archive = solarArchive;
        }

        public static MissionEnvironment LoadBundled()
        {
            EnvironmentProfile loaded = null;
            TextAsset asset = Resources.Load<TextAsset>(ProfileResource);
            if (asset != null)
            {
                try { loaded = JsonUtility.FromJson<EnvironmentProfile>(asset.text); }
                catch (Exception ex) { Debug.LogWarning("Environment profile: " + ex.Message); }
            }
            if (!Valid(loaded)) Debug.LogWarning("Using the labeled representative Gale temperature model; measured REMS profile is unavailable.");
            return new MissionEnvironment(loaded, MarsWeatherArchive.Load());
        }

        public EnvironmentSample Sample(double elapsedSeconds, double startUnix, int seed, double latitude, double longitude)
        {
            if (!Finite(elapsedSeconds) || !Finite(startUnix) || !Finite(latitude) || !Finite(longitude))
                throw new ArgumentException("Environment time and coordinates must be finite.");
            elapsedSeconds = Math.Max(0, elapsedSeconds);
            latitude = Math.Max(-90, Math.Min(90, latitude));
            longitude = MarsTime.Wrap(longitude, 360);
            double unix = startUnix + elapsedSeconds;
            MarsTime.State astronomy = MarsTime.Compute(unix, latitude, longitude);
            double sol = elapsedSeconds / MarsTime.SolSeconds;
            double nextDust, nextSolar;
            double dust = Hazard(sol, seed, 30, 0, out nextDust);
            double solarEvent = Hazard(sol, seed, 42, 1, out nextSolar);
            double hour = astronomy.localMeanSolarTimeHours;
            long heatBlock = (long)Math.Floor(sol / 20);
            double hotSol = heatBlock * 20 + 3 + Math.Floor(Hash(heatBlock, seed, 71) * 14);
            double heat = Math.Floor(sol) == hotSol && hour > 11 && hour < 17
                ? Math.Sin((hour - 11) / 6 * Math.PI) * 0.75 : 0;
            double cold = Clamp01(-astronomy.sunElevation / 12);
            double seasonalOffset = 5 * Math.Sin((astronomy.solarLongitude - 180) * Math.PI / 180);
            double outsideC = TemperatureAt(hour) + seasonalOffset + heat * 9;
            double distanceScale = 1.523679 / astronomy.sunDistanceAu;
            double sunlight = Math.Max(0, astronomy.sunUp) * distanceScale * distanceScale;
            double solarFraction = sunlight * (1 - 0.65 * dust);
            double nextWindow;
            double signal = RadioSignal(elapsedSeconds, seed, latitude, longitude, out nextWindow);
            bool solarWarning = nextSolar > 0 && nextSolar <= 0.3;
            bool dustWarning = nextDust > 0 && nextDust <= 0.3;
            string label = solarEvent > 0.001 ? "Solar particle event (simulated)"
                : dust > 0.001 ? "Dust storm (simulated)"
                : solarWarning ? "Solar particle warning - prepare shelter"
                : dustWarning ? "Dust warning - charge reserves"
                : heat > 0.01 ? "Thermal stress (simulated)"
                : cold > 0.01 ? "Cold night" : "Clear";
            string context = ArchivedContext(unix);
            return new EnvironmentSample
            {
                temperatureC = outsideC,
                solarFraction = Math.Max(0, solarFraction),
                radiationMicrograyPerHour = profile.backgroundMicrograyPerEarthDay / 24.0 +
                    solarEvent * profile.simulatedParticlePeakMicrograyPerHour,
                dust = dust, heat = heat, cold = cold, solarEvent = solarEvent,
                localHour = hour, solarLongitude = astronomy.solarLongitude,
                signal = signal, nextWindowSeconds = nextWindow,
                warning = solarWarning || dustWarning,
                label = label,
                temperatureSource = profile.temperatureSource,
                radiationSource = "RAD 2012-2013 background reference; event increment simulated (microgray/hour)",
                eventSource = string.IsNullOrEmpty(context) ? "Seeded teaching scenario; not a live forecast" :
                    "Seeded teaching scenario; independent DONKI context: " + context
            };
        }

        public double TemperatureAt(double localHour)
        {
            double hour = MarsTime.Wrap(localHour, 24);
            TemperaturePoint[] points = profile.temperatures;
            for (int i = 1; i < points.Length; i++)
            {
                if (hour > points[i].hour) continue;
                double fraction = (hour - points[i - 1].hour) / (points[i].hour - points[i - 1].hour);
                return points[i - 1].temperatureC + fraction * (points[i].temperatureC - points[i - 1].temperatureC);
            }
            return points[points.Length - 1].temperatureC;
        }

        // A local relay model, deliberately separate from actual Earth-Mars communications.
        public static double RadioSignal(double elapsedSeconds, int seed, double latitude, double longitude, out double nextWindowSeconds)
        {
            const double period = 7200, openDuration = 5400;
            double phase = MarsTime.Wrap(elapsedSeconds + (seed & 255) * 7, period);
            if (phase >= openDuration)
            {
                nextWindowSeconds = period - phase;
                return 0;
            }
            nextWindowSeconds = 0;
            double spatial = 0.67 + 0.28 * Math.Sin(latitude * 17.1 + longitude * 11.7 + seed * 0.01);
            double edge = Math.Min(1, Math.Min(phase + 120, openDuration - phase) / 240);
            return Clamp01(spatial * Math.Max(0.1, edge));
        }

        private static double Hazard(double sol, int seed, double period, int kind, out double nextStartSols)
        {
            long block = (long)Math.Floor(sol / period);
            nextStartSols = double.MaxValue;
            double value = 0;
            for (long b = Math.Max(0, block - 1); b <= block + 1; b++)
            {
                bool dust = kind == 0;
                double start = b * period + (dust ? 5 : 11) + Hash(b, seed, kind + 1) * (dust ? 5 : 8);
                double duration = dust ? (b == 0 ? 0.85 : 1.2 + Hash(b, seed, 13) * 0.8) : 0.35 + Hash(b, seed, 14) * 0.25;
                double peak = dust ? (b == 0 ? 0.45 : 0.5 + Hash(b, seed, 15) * 0.25) : 0.55 + Hash(b, seed, 16) * 0.45;
                if (sol < start) nextStartSols = Math.Min(nextStartSols, start - sol);
                double progress = sol - start;
                if (progress < 0 || progress >= duration) continue;
                double ramp = dust ? 0.15 : 0.05;
                double envelope = Math.Min(1, Math.Min(progress, duration - progress) / ramp);
                value = Math.Max(value, peak * Clamp01(envelope));
            }
            return value;
        }

        private string ArchivedContext(double unix)
        {
            if (archive == null || archive.events == null || unix < archive.windowStartUnix || unix > archive.windowEndUnix) return "";
            for (int i = 0; i < archive.events.Length; i++)
            {
                MarsWeatherArchive.SolarEvent item = archive.events[i];
                if (item == null || item.startUnix > unix) continue;
                if (unix > item.startUnix + Math.Max(0, item.durationHours) * 3600) continue;
                return item.kind == "cme" ? "modeled CME arrival " + item.id : item.kind + " " + item.id;
            }
            return "";
        }

        private static bool Valid(EnvironmentProfile value)
        {
            if (value == null || value.temperatures == null || value.temperatures.Length < 2 ||
                string.IsNullOrEmpty(value.temperatureSource) || !Finite(value.backgroundMicrograyPerEarthDay) ||
                value.backgroundMicrograyPerEarthDay < 0 || !Finite(value.simulatedParticlePeakMicrograyPerHour) ||
                value.simulatedParticlePeakMicrograyPerHour < 0) return false;
            double previous = -1;
            foreach (TemperaturePoint point in value.temperatures)
            {
                if (point == null || !Finite(point.hour) || point.hour <= previous || point.hour > 24 ||
                    !Finite(point.temperatureC) || point.temperatureC < -150 || point.temperatureC > 50) return false;
                previous = point.hour;
            }
            return value.temperatures[0].hour == 0 && previous == 24;
        }

        private static EnvironmentProfile DefaultProfile()
        {
            double[] hours = { 0, 3, 5, 7, 9, 12, 14, 16, 18, 21, 24 };
            double[] temperatures = { -70, -78, -82, -72, -48, -25, -18, -27, -44, -60, -70 };
            EnvironmentProfile result = new EnvironmentProfile { temperatures = new TemperaturePoint[hours.Length] };
            for (int i = 0; i < hours.Length; i++) result.temperatures[i] = new TemperaturePoint { hour = hours[i], temperatureC = temperatures[i] };
            return result;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Clamp01(double value) { return Math.Max(0, Math.Min(1, value)); }

        private static double Hash(long block, int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(block * 374761393L) ^ (uint)(seed * 668265263) ^ ((uint)salt * 2246822519u);
                h = (h ^ (h >> 15)) * 2246822519u;
                h = (h ^ (h >> 13)) * 3266489917u;
                return (h ^ (h >> 16)) / (double)uint.MaxValue;
            }
        }
    }
}
