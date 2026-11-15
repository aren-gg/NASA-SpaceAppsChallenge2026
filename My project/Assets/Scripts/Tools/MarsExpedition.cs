using System.Collections.Generic;
using MarsSurvival.Interaction;
using MarsSurvival.TimeAndWeather;
using MarsSurvival.UI;
using MarsSurvival.World;
using MarsWeather;
using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Shared expedition state the tools act on: research points, the sample bag, suit
    /// oxygen, the O2 canister, soil bags and radiation. Owns the message feed, the
    /// floating "+research" pop-ups, the on-screen waypoints and the "what to do next"
    /// hint shown by <see cref="MarsToolHud"/>.
    ///
    /// Oxygen and radiation are simple stand-ins until the real survival systems exist;
    /// the tools only go through the methods here, so those systems can replace the
    /// internals later.
    ///
    /// Added automatically to any scene with a MarsTerrain, together with the base camp,
    /// survey sites, tool belt and HUD.
    /// </summary>
    public sealed class MarsExpedition : MonoBehaviour
    {
        public enum SampleKind { Rock, Core }
        public enum Tone { Info, Good, Warning, Radio }

        public struct Message
        {
            public string text;
            public Tone tone;
            public float time;
        }

        public struct Popup
        {
            public string text;
            public Vector3 world;
            public bool hasWorld;
            public float time;
        }

        public struct Waypoint
        {
            public Vector3 position;
            public string label;
            public Paint paint;
        }

        public static MarsExpedition Instance { get; private set; }

        public const int BagCapacity = 4;
        public const int MaxRegolithBags = 4;
        public const int MaxCanisterCharges = 2;
        /// <summary>Curiosity RAD measured about 0.21 mGy per day on the surface in Gale Crater.</summary>
        public const float BackgroundDoseMicroGyPerDay = 210f;

        [Header("Stand-in survival numbers (real seconds, real metres)")]
        [Tooltip("Seconds a full suit lasts outside the base camp.")]
        public float suitOxygenSeconds = 420f;
        [Tooltip("Suit oxygen percent regained per second inside the camp.")]
        public float campRefillPerSecond = 10f;
        public float canisterRefillPercent = 40f;
        [Tooltip("Radius around the camp centre where the suit refills.")]
        public float campRadiusMeters = 14f;
        [Tooltip("Walk this close to the supply crate to hand in samples.")]
        public float crateRadiusMeters = 3.5f;
        public float reachMeters = 3.5f;

        public MarsTerrain Terrain { get; private set; }
        public MarsDayNightCycle Cycle { get; private set; }
        public MarsWeatherSchedule Schedule { get; private set; }
        public WeatherController Weather { get; private set; }
        public MarsBaseCamp Camp { get; private set; }
        public MarsToolBelt Belt { get; private set; }

        public int Research { get; private set; }
        public readonly List<SampleKind> Samples = new List<SampleKind>();
        public float SuitOxygen { get; private set; } = 100f;
        public int CanisterCharges { get; set; } = MaxCanisterCharges;
        public int RegolithBags { get; set; }
        public double MissionDoseMicroGy { get; private set; }
        public float DoseRateMicroGyPerDay { get; private set; } = BackgroundDoseMicroGyPerDay;
        /// <summary>Survey site the player is being guided to (shown as a waypoint).</summary>
        public MarsSurveySite GuidedSite { get; private set; }
        /// <summary>The single most useful thing to do next, in plain words.</summary>
        public string NextStep { get; private set; } = "";

        public readonly List<Message> Messages = new List<Message>();
        public readonly List<Popup> Popups = new List<Popup>();
        public readonly List<Waypoint> Waypoints = new List<Waypoint>();

        private static Vector3 spawnPosition;
        private static Quaternion spawnRotation;
        private static bool spawnKnown;
        private MarsAstronautPlayer player;
        private WeatherEvent lastWeather = WeatherEvent.Clear;
        private float oxygenWarnedAt = 101f;
        private float nextStepTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            spawnKnown = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindObjectOfType<MarsExpedition>() != null) return;
            if (FindObjectOfType<MarsTerrain>() == null || Camera.main == null) return;
            // Remember where the astronaut will start before the home screen moves the camera.
            spawnPosition = Camera.main.transform.position;
            spawnRotation = Camera.main.transform.rotation;
            spawnKnown = true;
            new GameObject("Mars Expedition").AddComponent<MarsExpedition>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (!spawnKnown && Camera.main != null)
            {
                // Placed in the scene by hand rather than installed: use the camera now.
                spawnPosition = Camera.main.transform.position;
                spawnRotation = Camera.main.transform.rotation;
                spawnKnown = true;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            Terrain = FindObjectOfType<MarsTerrain>();
            Cycle = FindObjectOfType<MarsDayNightCycle>();
            Schedule = FindObjectOfType<MarsWeatherSchedule>();
            Weather = FindObjectOfType<WeatherController>();
            if (Terrain == null || !Terrain.IsReady)
            {
                enabled = false;
                return;
            }

            Vector3 forward = spawnRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // Camp a short walk ahead and to the right of the start, clear of the outpost's spot.
            Vector3 campCentre = spawnPosition + (forward * 7f + right * 10f) * Units(1f);
            Camp = new GameObject("Base Camp").AddComponent<MarsBaseCamp>();
            Camp.Build(this, campCentre, Quaternion.LookRotation(-right, Vector3.up));

            MarsSurveySite.SpawnAround(this, spawnPosition, forward, 2026);

            Belt = gameObject.AddComponent<MarsToolBelt>();
            gameObject.AddComponent<MarsToolHud>();
        }

        // ------------------------------------------------------------------ helpers

        public float Units(float meters)
        {
            return Terrain != null && Terrain.config != null ? Terrain.config.ToUnits(meters) : meters;
        }

        public float Meters(float units)
        {
            return Terrain != null && Terrain.IsReady ? Terrain.UnitsToRealMeters(units) : units;
        }

        public float Reach { get { return Units(reachMeters); } }

        /// <summary>Feet of the astronaut, or the camera when there is no astronaut yet.</summary>
        public Vector3 PlayerPosition
        {
            get
            {
                if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
                if (player != null) return player.transform.position;
                return Camera.main != null ? Camera.main.transform.position : spawnPosition;
            }
        }

        public bool InsideCamp(Vector3 position)
        {
            return Camp != null && FlatDistance(position, Camp.transform.position) <= Units(campRadiusMeters);
        }

        public bool NearCrate
        {
            get
            {
                return Camp != null && Camp.Crate != null &&
                       FlatDistance(PlayerPosition, Camp.Crate.transform.position) <= Units(crateRadiusMeters);
            }
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Hours of Mars time that passed this frame (the clock runs faster than real time).</summary>
        public float MarsHoursThisFrame
        {
            get
            {
                double scale = Cycle != null ? (Cycle.paused ? 0.0 : Cycle.TimeScale) : 74.0; // 74 = the default 20-minute sol
                return (float)(Time.deltaTime * scale / 3600.0);
            }
        }

        public float Daylight { get { return Cycle != null ? Cycle.Daylight : 1f; } }
        public float DustAmount { get { return Weather != null ? Weather.DustAmount : 0f; } }
        public float SolarAmount { get { return Weather != null ? Weather.SolarAmount : 0f; } }
        public float HeatAmount { get { return Weather != null ? Weather.HeatAmount : 0f; } }
        public float ColdAmount { get { return Weather != null ? Weather.ColdAmount : 0f; } }

        public bool RadiationHigh { get { return DoseRateMicroGyPerDay >= 1000f; } }

        public string RadiationWord
        {
            get
            {
                if (DoseRateMicroGyPerDay < 400f) return "normal";
                return RadiationHigh ? "HIGH (solar flare)" : "a little high";
            }
        }

        public void Say(string text, Tone tone = Tone.Info)
        {
            Messages.Add(new Message { text = text, tone = tone, time = Time.unscaledTime });
            if (Messages.Count > 8) Messages.RemoveAt(0);
        }

        /// <summary>Adds research with a cheerful message and a "+N" pop-up (at a world point if given).</summary>
        public void AddResearch(int points, string reason, Vector3? where)
        {
            Research += points;
            Say(reason + "  +" + points + " research", Tone.Good);
            Popups.Add(new Popup
            {
                text = "+" + points + " research",
                world = where ?? Vector3.zero,
                hasWorld = where.HasValue,
                time = Time.unscaledTime,
            });
            MarsSfx.Play(MarsSfx.Sound.Research);
        }

        public bool BagFull { get { return Samples.Count >= BagCapacity; } }

        public void AddSample(SampleKind kind, Vector3 where)
        {
            Samples.Add(kind);
            MarsSfx.Play(MarsSfx.Sound.Success);
            Popups.Add(new Popup { text = "+1 " + SampleName(kind), world = where, hasWorld = true, time = Time.unscaledTime });
            Say("You got a " + SampleName(kind) + "! (" + Samples.Count + "/" + BagCapacity + " in your bag)", Tone.Good);
        }

        public static int SampleValue(SampleKind kind)
        {
            return kind == SampleKind.Core ? 4 : 3;
        }

        public static string SampleName(SampleKind kind)
        {
            return kind == SampleKind.Core ? "core sample" : "rock sample";
        }

        public void ChangeOxygen(float percent)
        {
            SuitOxygen = Mathf.Clamp(SuitOxygen + percent, 0f, 100f);
            if (SuitOxygen > 30f) oxygenWarnedAt = 101f;
        }

        /// <summary>Points the player at the nearest unfinished survey site.</summary>
        public void GuideToNearestSite(bool announce)
        {
            MarsSurveySite site = MarsSurveySite.Nearest(PlayerPosition, float.MaxValue, true);
            GuidedSite = site;
            if (!announce) return;
            if (site == null)
            {
                Say("Base: You studied every survey site. Amazing work, scientist!", Tone.Radio);
                return;
            }
            Say("Base: The nearest survey site is " + site.DisplayName + ", " +
                Mathf.RoundToInt(Meters(FlatDistance(PlayerPosition, site.transform.position))) +
                " m away. Follow its marker on your screen!", Tone.Radio);
        }

        // ------------------------------------------------------------------ simulation

        private void Update()
        {
            if (MarsHomeScreen.IsOpen) return;
            float dt = Time.deltaTime;
            Vector3 playerPosition = PlayerPosition;

            if (GuidedSite == null && MarsSurveySite.All.Count > 0 && Time.timeSinceLevelLoad > 1f) GuideToNearestSite(false);
            if (GuidedSite != null && GuidedSite.Finished)
            {
                Say("You finished " + GuidedSite.DisplayName + "! On to the next site.", Tone.Good);
                GuideToNearestSite(true);
            }

            // Suit oxygen: refills inside the camp, runs down outside it.
            if (InsideCamp(playerPosition)) ChangeOxygen(campRefillPerSecond * dt);
            else ChangeOxygen(-100f / Mathf.Max(10f, suitOxygenSeconds) * dt);
            WarnOxygen(30f, "Your oxygen is getting low. Head back to camp or use " + MarsTool.Named("oxygen_canister", "Oxygen Canister") + ".");
            WarnOxygen(10f, "Oxygen is very low! Go back to camp now!");
            WarnOxygen(0f, "Out of oxygen! Go back to camp to refill.");

            // Radiation: background plus solar-flare spikes, accumulated over Mars time.
            DoseRateMicroGyPerDay = BackgroundDoseMicroGyPerDay * (1f + 24f * SolarAmount);
            MissionDoseMicroGy += DoseRateMicroGyPerDay * MarsHoursThisFrame / 24.0;

            // Walk up to the supply crate to hand in samples.
            if (Samples.Count > 0 && NearCrate) DeliverSamples();

            // The base radios weather changes.
            WeatherEvent now = Weather != null ? Weather.Current : WeatherEvent.Clear;
            if (now != lastWeather)
            {
                lastWeather = now;
                string alert = WeatherAlert(now);
                if (alert != null)
                {
                    Say("Base: " + alert, now == WeatherEvent.Clear ? Tone.Radio : Tone.Warning);
                    MarsSfx.Play(MarsSfx.Sound.Radio);
                }
            }

            nextStepTimer -= dt;
            if (nextStepTimer <= 0f)
            {
                nextStepTimer = 0.25f;
                NextStep = ChooseNextStep(playerPosition);
            }
            UpdateWaypoints();
        }

        private void DeliverSamples()
        {
            int points = 0;
            foreach (SampleKind kind in Samples) points += SampleValue(kind);
            int count = Samples.Count;
            Samples.Clear();
            AddResearch(points, "You handed in " + count + (count == 1 ? " sample" : " samples") + " for study!",
                        Camp.Crate.transform.position + Vector3.up * Units(1.2f));
        }

        private void WarnOxygen(float level, string text)
        {
            if (SuitOxygen <= level && oxygenWarnedAt > level)
            {
                oxygenWarnedAt = level;
                Say(text, Tone.Warning);
                MarsSfx.Play(MarsSfx.Sound.Deny);
            }
        }

        public static string WeatherAlert(WeatherEvent weather)
        {
            switch (weather)
            {
                case WeatherEvent.SolarFlare: return "Solar flare! Radiation is going up. Keep your trip short.";
                case WeatherEvent.DustStorm: return "Dust storm! The solar panels will get dusty.";
                case WeatherEvent.HeatStorm: return "Heat storm! Things at camp might break.";
                case WeatherEvent.ColdNight: return "Very cold night! Things at camp might break.";
                case WeatherEvent.Clear: return "The weather is clear again.";
                default: return null;
            }
        }

        /// <summary>The most useful thing to do now, for a young player, with the key to press.</summary>
        private string ChooseNextStep(Vector3 playerPosition)
        {
            bool atCamp = InsideCamp(playerPosition);
            if (SuitOxygen < 30f && !atCamp)
            {
                return CanisterCharges > 0
                    ? "Low oxygen! Use " + MarsTool.Named("oxygen_canister", "Oxygen Canister") + " or follow the orange Camp marker home."
                    : "Low oxygen! Follow the orange Camp marker back to camp.";
            }
            if (BagFull) return "Your sample bag is full! Walk to the supply crate at camp.";
            if (Camp != null)
            {
                foreach (MarsSolarPanel panel in Camp.Panels)
                {
                    if (panel.Damaged) return panel.name + " is broken! Fix it with " + MarsTool.Named("power_wrench", "Field Wrench") + ".";
                }
                if (Camp.Battery.Damaged) return "The battery is broken! Fix it with " + MarsTool.Named("power_wrench", "Field Wrench") + ".";
                foreach (MarsSolarPanel panel in Camp.Panels)
                {
                    if (panel.Dust > 0.5f) return panel.name + " is dusty. Clean it with " + MarsTool.Named("panel_brush", "Panel Duster") + ".";
                }
            }
            if (Daylight < 0.15f && Belt != null && !Belt.LampOn)
                return "It's dark! Switch on " + MarsTool.Named("work_lamp", "Work Lamp") + ".";
            if (Camp != null)
            {
                foreach (MarsCropBed bed in Camp.Beds)
                {
                    if (bed.Grown) return bed.name + " is ready! Harvest it with " + MarsTool.Named("hydro_sprayer", "Sprayer") + ".";
                    if (!bed.NeedsSoil && bed.Water <= 0f && bed.Growth > 0f)
                        return bed.name + " is thirsty. Water it with " + MarsTool.Named("hydro_sprayer", "Sprayer") + ".";
                }
            }
            if (Samples.Count >= 2) return "Take your samples to the supply crate at camp for research points.";

            MarsSurveySite site = GuidedSite;
            if (site != null)
            {
                float meters = Meters(FlatDistance(playerPosition, site.transform.position));
                if (meters > 14f) return "Walk to the " + site.DisplayName + " marker (" + Mathf.RoundToInt(meters) + " m). Hold Shift to run!";
                if (!site.RockTaken) return "Break off a rock with " + MarsTool.Named("rock_hammer", "Rock Hammer") + ". Aim at the rocks.";
                if (!site.CoreTaken) return "Drill a core with " + MarsTool.Named("core_drill", "Core Drill") + ". Aim at the ground here.";
                if (!site.Photographed) return "Step back and take a photo with " + MarsTool.Named("survey_camera", "Survey Camera") + ".";
            }

            if (Camp != null)
            {
                foreach (MarsCropBed bed in Camp.Beds)
                {
                    if (bed.NeedsSoil)
                        return "Grow food: dig soil with " + MarsTool.Named("regolith_shovel", "Shovel") + " and put it in a crop bed.";
                    if (bed.Water <= 0f) return "Water " + bed.name + " with " + MarsTool.Named("hydro_sprayer", "Sprayer") + ".";
                }
            }
            if (Samples.Count > 0) return "Take your samples to the supply crate at camp.";
            return "You studied every site! Keep the camp running and try photographing Mount Sharp.";
        }

        private void UpdateWaypoints()
        {
            Waypoints.Clear();
            if (Camp != null)
            {
                Waypoints.Add(new Waypoint { position = Camp.transform.position + Vector3.up * Units(2.4f), label = "Camp", paint = Paint.WarmOrange });
            }
            if (GuidedSite != null)
            {
                Waypoints.Add(new Waypoint { position = GuidedSite.transform.position + Vector3.up * Units(2.2f), label = GuidedSite.DisplayName, paint = Paint.PalePink });
            }
            for (int i = Popups.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime - Popups[i].time > 2f) Popups.RemoveAt(i);
            }
        }
    }
}
