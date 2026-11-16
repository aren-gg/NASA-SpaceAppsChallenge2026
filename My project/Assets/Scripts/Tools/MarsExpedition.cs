using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.TimeAndWeather;
using MarsSurvival.UI;
using MarsSurvival.World;
using MarsWeather;
using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Connects the 10 hand tools and their HUD to the outpost mission. The tools act on the
    /// mission's daily tasks (see <see cref="MarsTool"/>); this class supplies the world (terrain,
    /// units, weather), the astronaut's suit and site guidance from the mission state, and the
    /// floating "+research" pop-ups and waypoints the tool HUD draws.
    /// </summary>
    public sealed class MarsExpedition : MonoBehaviour
    {
        public enum Tone { Info, Good, Warning, Radio }

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

        [Tooltip("How far the tools reach, in real metres.")]
        public float reachMeters = 3.5f;

        public MarsTerrain Terrain { get; private set; }
        public MarsDayNightCycle Cycle { get; private set; }
        public MarsWeatherSchedule Schedule { get; private set; }
        public WeatherController Weather { get; private set; }
        public MarsToolBelt Belt { get; private set; }

        /// <summary>The outpost science site the radio is guiding to, or null.</summary>
        public SiteState GuidedSite { get; private set; }
        public readonly List<Popup> Popups = new List<Popup>();
        public readonly List<Waypoint> Waypoints = new List<Waypoint>();

        private MarsAstronautPlayer player;
        private MarsOutpost outpost;
        private MissionState watchedState;
        private int lastResearch;

        public static MarsMissionController Mission { get { return MarsMissionController.Instance; } }
        public static MissionSimulation Sim { get { return Mission == null ? null : Mission.Simulation; } }
        public static MissionState State { get { return Sim == null ? null : Sim.State; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindObjectOfType<MarsExpedition>() != null) return;
            if (FindObjectOfType<MarsTerrain>() == null) return;
            new GameObject("Mars Expedition Tools").AddComponent<MarsExpedition>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
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
            Belt = gameObject.AddComponent<MarsToolBelt>();
            gameObject.AddComponent<MarsToolHud>();
        }

        // ------------------------------------------------------------------ world helpers

        public float Units(float meters)
        {
            return Terrain != null && Terrain.config != null ? Terrain.config.ToUnits(meters) : meters;
        }

        public float Meters(float units)
        {
            return Terrain != null && Terrain.IsReady ? Terrain.UnitsToRealMeters(units) : units;
        }

        public float Reach { get { return Units(reachMeters); } }

        public Vector3 PlayerPosition
        {
            get
            {
                if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
                if (player != null) return player.transform.position;
                return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            }
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public float Daylight { get { return Cycle != null ? Cycle.Daylight : 1f; } }

        /// <summary>Suit oxygen as a percentage of the suit tank (mission resources).</summary>
        public float SuitOxygen
        {
            get
            {
                MissionSimulation sim = Sim;
                return sim == null ? 100f : (float)(100.0 * sim.State.resources.suitOxygenGrams / sim.Settings.suitCapacityGrams);
            }
        }

        public bool Outdoors { get { return Mission != null && Mission.Context.zone == PlayerZone.Outdoors; } }

        public string RadiationWord
        {
            get
            {
                if (Mission == null) return "normal";
                double rate = Mission.Environment.radiationMicrograyPerHour;
                return rate >= 100 ? "HIGH (solar particle event)" : rate >= 20 ? "a little high" : "normal";
            }
        }

        /// <summary>A short message for the player, shown in the mission HUD's message line.</summary>
        public void Say(string text, Tone tone = Tone.Info)
        {
            if (Mission != null) Mission.Notice = text;
            if (tone == Tone.Radio) MarsSfx.Play(MarsSfx.Sound.Radio);
        }

        public void Pop(string text, Vector3? where)
        {
            Popups.Add(new Popup { text = text, world = where ?? Vector3.zero, hasWorld = where.HasValue, time = Time.unscaledTime });
        }

        /// <summary>Points the player at the nearest science site with work left, and selects it in the field log.</summary>
        public void GuideToNearestSite(bool announce)
        {
            MissionState state = State;
            GuidedSite = null;
            if (state == null) return;
            Vector3 here = PlayerPosition;
            float best = float.MaxValue;
            foreach (SiteState site in state.sites)
            {
                if (site.analyzed && site.photographed && site.surveyed) continue;
                float d = FlatDistance(here, new Vector3((float)site.x, 0f, (float)site.z));
                if (d < best) { best = d; GuidedSite = site; }
            }
            if (GuidedSite != null && Mission != null) Mission.SelectSite(GuidedSite.id);
            if (!announce) return;
            if (GuidedSite == null) Say("Houston: every science site is finished. Outstanding work!", Tone.Radio);
            else Say("Houston: the nearest site with work left is " + GuidedSite.name + ", " + Mathf.RoundToInt(Meters(best)) +
                     " m away. Follow its marker.", Tone.Radio);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (MarsHomeScreen.IsOpen) return;
            MissionState state = State;
            if (state != watchedState)
            {
                watchedState = state;
                lastResearch = state == null ? 0 : state.resources.researchPoints;
                GuidedSite = null;
            }
            if (state != null)
            {
                // Research is earned by the mission's tasks; celebrate it where the astronaut is.
                int research = state.resources.researchPoints;
                if (research > lastResearch)
                {
                    Pop("+" + (research - lastResearch) + " research", null);
                    MarsSfx.Play(MarsSfx.Sound.Research);
                }
                lastResearch = research;
                if (GuidedSite == null && Mission != null && !string.IsNullOrEmpty(Mission.SelectedSiteId))
                    GuidedSite = state.sites.Find(s => s.id == Mission.SelectedSiteId);
            }
            UpdateWaypoints();
        }

        private void UpdateWaypoints()
        {
            Waypoints.Clear();
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (Outdoors && outpost != null && outpost.IsPlaced)
                Waypoints.Add(new Waypoint { position = outpost.DoorPosition + Vector3.up * Units(2.4f), label = "Airlock", paint = Paint.WarmOrange });
            // Today's plan: a marker on where the current step happens.
            string planLabel = null;
            if (Mission != null && Mission.Simulation != null)
            {
                List<PlanStep> steps = MissionDailyPlan.Steps(Mission);
                int current = MissionDailyPlan.Current(steps, Mission.Simulation.LocalHour);
                if (current >= 0 && steps[current].waypoint.HasValue)
                {
                    planLabel = steps[current].waypointLabel;
                    Waypoints.Add(new Waypoint { position = steps[current].waypoint.Value, label = steps[current].title + ": " + planLabel, paint = Paint.WarmOrange });
                }
            }
            if (GuidedSite != null && GuidedSite.name != planLabel)
                Waypoints.Add(new Waypoint { position = new Vector3((float)GuidedSite.x, (float)GuidedSite.y + Units(2.2f), (float)GuidedSite.z),
                                             label = GuidedSite.name, paint = Paint.PalePink });
            for (int i = Popups.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - Popups[i].time > 2f) Popups.RemoveAt(i);
        }
    }
}
