using System;
using System.Collections.Generic;

namespace MarsSurvival.Simulation
{
    public enum MissionLength { Month, HalfYear, Year }
    public enum MissionPhase { Ready, Running, Won, Lost }
    public enum PowerChannel { LifeControl, Temperature, RadiationShielding }
    public enum PlayerZone { Outdoors, Habitat, Shelter, Airlock }
    public enum TaskKind { None, Rest, Exercise, Treat, Plant, Harvest, RepairBase, RepairRover, SampleSoil, Analyze, PlaceMarker, Photograph, DustPanels, Install, Transmit }
    public enum RadioMode { Off, Listen, Transmit }

    [Serializable]
    public sealed class MissionSettings
    {
        public const double SolSeconds = 88775.244;
        /// <summary>Game seconds per real second: one sol lasts about 15 real minutes.</summary>
        public const double TimeScale = SolSeconds / 900.0;
        /// <summary>Mars-local hour the astronaut wakes up and the daily plan starts.</summary>
        public const double MorningHour = 7;
        /// <summary>Game seconds in one Mars-local hour.</summary>
        public const double HourSeconds = SolSeconds / 24.0;
        public const double SolsPerYear = 668.5921;
        public double fixedStepSeconds = 10;
        public double batteryCapacityKwh = 180;
        public double maximumDischargeKw = 24;
        public double maximumChargeKw = 24;
        public double solarRatedKw = 16;
        public double lifeRatedKw = 3;
        public double thermalRatedKw = 5;
        public double shieldingRatedKw = 4;
        /// <summary>
        /// Each kW a system draws beyond its useful need costs this many kW from the battery
        /// (the excess is lost as heat and strain), so overdrawing drains energy faster.
        /// </summary>
        public double overdrawCostMultiplier = 3;
        public double roverCapacityKwh = 60;
        public double fieldCapacityKwh = 4;
        public double oxygenCapacityGrams = 12000;
        public double suitCapacityGrams = 1000;
        /// <summary>Portable O2 canister (hand tool): filled from habitat oxygen, emptied into the suit on EVA.</summary>
        public double canisterCapacityGrams = 800, canisterDoseGrams = 400;
        public double waterCapacityLiters = 5000;
        public double foodCapacity = 300;
        public double thermalLossKwPerDegree = 0.02;
        public double greenhouseKwPerBed = 0.6;
        public double oxygenGramsPerSol = 850;
        public double backgroundDamageThresholdMicrogray = 10000;
        public double harmfulDoseRateMicrograyPerHour = 150;
        public double chargeEfficiency = 0.95;
        public double portableChargeEfficiency = 0.9;
        public double cropLeafySols = 3, cropStapleSols = 6;
        /// <summary>Water one leafy bed uses per sol; set from the KSC Veggie crop estimates (MissionDataSources).</summary>
        public double leafyWaterLitersPerSol = 1;
        public static double Duration(MissionLength length)
        {
            return SolSeconds * SolsPerYear / (length == MissionLength.Month ? 12 : length == MissionLength.HalfYear ? 2 : 1);
        }
    }

    [Serializable]
    public sealed class AstronautState
    {
        // Mirrors of the MarsTrainer survival state (see SurvivalNeeds): satiety = 100 - hunger,
        // fatigue = tiredness, oxygen = breathing reserve. Stored here so saves keep them.
        public double health = 100, satiety = 100, hydration = 100, fatigue = 10, fitness = 75, oxygen = 100;
        public double doseMicrogray, temperatureC = 21;
        public double starvationSeconds, dehydrationSeconds, hypoxiaSeconds, lastExerciseSeconds;
        public string lastDamageCause = "";
    }

    [Serializable]
    public sealed class ResourceState
    {
        public double batteryKwh = 140, roverKwh = 60, fieldKwh = 4;
        public double cleanWaterLiters = 3000, wasteWaterLiters;
        public double oxygenGrams = 10000, suitOxygenGrams = 1000, canisterOxygenGrams = 800;
        public double food = 50, parts = 80, seeds = 30, nutrients = 1000, medicine = 20;
        public int researchPoints;
    }

    [Serializable]
    public sealed class EquipmentState
    {
        public double baseCondition = 1, roverCondition = 1, panelCondition = 1, panelDust;
        public int extraPanels, extraBatteries;
        public double habitatTemperatureC = 21;
        public double airQuality = 100;
    }

    [Serializable]
    public sealed class CropState
    {
        /// <summary>Plant spots in each greenhouse bed; each is sown and picked by hand.</summary>
        public const int Slots = 12;
        public const int AllSlots = (1 << Slots) - 1;
        public int bed;
        public string crop = "leafy";
        public bool planted, ready;
        public double growth, stress;
        public int harvests;
        /// <summary>Bit per plant spot: sown with the shovel, picked when ripe.</summary>
        public int sownMask, pickedMask;
        /// <summary>Soil moisture 0-1 (above 1 is waterlogged). Dries out over about one sol; plants only grow while moist.</summary>
        public double moisture;

        public static int Count(int mask) { int n = 0; for (int i = 0; i < Slots; i++) if ((mask & (1 << i)) != 0) n++; return n; }
    }

    /// <summary>
    /// One sol of the daily plan: breakfast, power, greenhouse, maintenance, field trip,
    /// report, dinner and sleep. Flags record what the player did today.
    /// </summary>
    [Serializable]
    public sealed class DayState
    {
        public int number;
        public double startSeconds;
        public bool ateMorning, drankMorning, powerChecked, fieldDone, reportDone, ateEvening, drankEvening;
        /// <summary>Set when the astronaut wakes from sleep; the HUD shows the sol summary once.</summary>
        public bool summaryPending;
        public double foodAtStart, waterAtStart, foodHarvested;
        public int researchAtStart, repairs, panelsSwept, plantsPicked, plantsSown;
        public string yesterday = "";
    }

    [Serializable]
    public sealed class SiteState
    {
        public string id, name;
        public double x, y, z, latitude, longitude;
        public double distanceMeters;
        public int reward = 12;
        public bool discovered, sampled, analyzed, markerPlaced, photographed;
        public bool surveyed, measured;
        public string photoPath = "";
        public double sampleTime, photoTime;
    }

    [Serializable]
    public sealed class TaskState
    {
        public TaskKind kind;
        public string targetId = "";
        public double progressSeconds, durationSeconds, powerKw, spentKwh, reservedParts;
        public bool fieldPower;
        public bool roverPower;
        public string station = "", evidencePath = "", cropId = "";
        public double reservedSeeds, reservedMedicine;
    }

    [Serializable]
    public sealed class ReportState
    {
        public string id, summary;
        public double createdSeconds, progressSeconds;
        public bool acknowledged;
        public bool bonusGranted;
    }

    [Serializable]
    public sealed class MissionState
    {
        public int schemaVersion = 1, seed = 2026;
        public MissionLength length;
        public MissionPhase phase;
        public double startUnix = 1763985600, elapsedSeconds;
        public double[] knobs = { 0.42, 0.45, 0.05 };
        public AstronautState astronaut = new AstronautState();
        public ResourceState resources = new ResourceState();
        public EquipmentState equipment = new EquipmentState();
        public List<CropState> crops = new List<CropState>();
        public List<SiteState> sites = new List<SiteState>();
        public List<string> researched = new List<string>(), installed = new List<string>();
        public List<ReportState> reports = new List<ReportState>();
        public List<string> history = new List<string>();
        public TaskState task = new TaskState();
        public RadioMode radioMode;
        public bool routineEnabled = true, chargeRover, chargeField = true, greenhouseEnabled = true;
        public double playerX, playerY, playerZ, playerYaw, roverX, roverY, roverZ, roverYaw, roverOdometer;
        public int tutorialStep;
        public bool playerDriving;
        /// <summary>Outpost tool copies the astronaut is carrying (see MarsBaseTool.key).</summary>
        public List<string> carriedTools = new List<string>();
        public string endingReason = "";
        public double totalGeneratedKwh, totalConsumedKwh, totalLossKwh, totalCurtailedKwh;
        public double totalWaterLostLiters, totalOxygenProducedGrams, totalFoodHarvested;
        public int reportsCreated, repairsCompleted, panelsCleaned;
        public DayState day = new DayState();
        /// <summary>A sparking fault somewhere in the base (index into the outpost's fault spots), fixed with the wrench.</summary>
        public bool faultActive;
        public int faultSpot = -1;
        /// <summary>Dust on each of the six array panels; equipment.panelDust is their average.</summary>
        public double[] panelDustEach;
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
        public bool moving, sprinting, driving, roverAtBase;
        /// <summary>True while the player is fast-forwarding time (+1 hour); the daily routine only runs then.</summary>
        public bool advancing;
        public double distanceFromBase, roverDistanceKm;
        public string nearbySiteId, station;
        public bool photoFramingValid;
        public string photoEvidencePath;
    }

    public sealed class PowerSnapshot
    {
        public double generatedKw, loadKw, netKw, batteryCapacityKwh;
        public double[] requestedKw = new double[3], deliveredKw = new double[3], neededKw = new double[3];
        public double taskKw, greenhouseKw, chargingKw, radioKw;
        /// <summary>Extra kW lost because the three systems are drawing more than they need.</summary>
        public double overdrawLossKw;
    }

    public sealed class ResearchNode
    {
        public string id, title, branch, description;
        public string[] prerequisites;
        public int points;
        public double parts, installationSeconds;
    }

    public sealed class TaskQuote
    {
        public bool available, fieldPower;
        public string reason, energySource;
        public double durationSeconds, powerKw, estimatedKwh, parts, seeds, medicine;
    }
}
