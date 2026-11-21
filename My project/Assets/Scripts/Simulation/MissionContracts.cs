using System;
using System.Collections.Generic;

namespace MarsSurvival.Simulation
{
    public enum MissionPhase { Ready, Running, Lost }
    public enum PlayerZone { Outdoors, Habitat, Shelter, Airlock }

    public static class MissionSettings
    {
        public const double SolSeconds = 88775.244;
        /// <summary>Game seconds per real second: one sol lasts about 15 real minutes.</summary>
        public const double TimeScale = SolSeconds / 900.0;
        /// <summary>Mars-local hour the astronaut wakes up.</summary>
        public const double MorningHour = 7;
        public const double HourSeconds = SolSeconds / 24.0;
        public const double FixedStepSeconds = 10;
        /// <summary>Plant spots in each of the two greenhouse beds.</summary>
        public const int SpotsPerBed = 12, MaxPlots = 24, StartPlots = 6, PlotsPerExpansion = 2;
        /// <summary>Vital points lost per sol (oxygen only drains outdoors; indoors it refills).</summary>
        public const double FoodPerSol = 60, WaterPerSol = 80, OxygenPerHourOutside = 25, OxygenRefillPerHour = 200;
        public const int StartCredits = 30;
    }

    /// <summary>A seed the shop sells: what it costs, how many real seconds it grows once watered, what the crop sells for and how filling it is.</summary>
    public sealed class SeedKind
    {
        public string id, name;
        public int price, sellPrice;
        public double seconds, food;
        /// <summary>Mission (game) seconds to ripen once watered.</summary>
        public double GameSeconds { get { return seconds * MissionSettings.TimeScale; } }

        public static readonly SeedKind[] All =
        {
            new SeedKind { id = "lettuce", name = "Lettuce", price = 10, sellPrice = 15, seconds = 30, food = 25 },
            new SeedKind { id = "tomato", name = "Tomato", price = 25, sellPrice = 45, seconds = 60, food = 35 },
            new SeedKind { id = "watermelon", name = "Watermelon", price = 60, sellPrice = 130, seconds = 120, food = 60 },
        };

        public static SeedKind Find(string id) { return Array.Find(All, s => s.id == id); }
    }

    [Serializable]
    public sealed class AstronautState
    {
        public double oxygen = 100, food = 100, water = 100;
    }

    /// <summary>One greenhouse plant spot (bed = index / 12). Empty when seed is "".</summary>
    [Serializable]
    public sealed class PlotState
    {
        public string seed = "";
        /// <summary>0..1; ripe at 1.</summary>
        public double growth;
        /// <summary>Mission time until which the soil stays moist (plants only grow while moist).</summary>
        public double wetUntil;
        // The moisture from one watering ends just as growth reaches 1; allow for rounding over many steps.
        public bool Ripe { get { return seed != "" && growth >= 0.9999; } }
        /// <summary>Real seconds until ripe while moist (the plot timer).</summary>
        public double SecondsLeft { get { SeedKind k = SeedKind.Find(seed); return k == null ? 0 : Math.Max(0, (1 - growth) * k.seconds); } }
    }

    [Serializable]
    public sealed class MissionState
    {
        public int schemaVersion = 3, seed = 2026;
        public MissionPhase phase;
        public double startUnix = 1763985600, elapsedSeconds;
        public AstronautState astronaut = new AstronautState();
        public int credits = MissionSettings.StartCredits, unlockedPlots = MissionSettings.StartPlots;
        public List<PlotState> plots = new List<PlotState>();
        /// <summary>Counts per SeedKind.All index: seeds in the pack, and crops in the basket.</summary>
        public int[] seeds = new int[3], basket = new int[3];
        public double playerX, playerY, playerZ, playerYaw;
        /// <summary>Outpost tool copies the astronaut is carrying (see MarsBaseTool.key).</summary>
        public List<string> carriedTools = new List<string>();
        public string endingReason = "";
        /// <summary>Mission time the astronaut sleeps until (0 when awake).</summary>
        public double sleepUntil;
    }

    public struct EnvironmentSample
    {
        public double temperatureC, solarFraction, radiationMicrograyPerHour;
        public double dust, heat, cold, solarEvent, localHour, solarLongitude;
        public double signal, nextWindowSeconds;
        public string label, temperatureSource, radiationSource, eventSource;
        public bool warning;
    }

    public struct PlayerContext
    {
        public PlayerZone zone;
        public bool moving, sprinting;
        public double distanceFromBase;
        public string station;
    }
}
