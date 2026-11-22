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
    /// Connects the hand tools and their HUD to the outpost mission (see <see cref="MarsTool"/>):
    /// supplies the world (terrain, units, weather) and the floating pop-ups and airlock waypoint
    /// the tool HUD draws.
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
        public float reachMeters = 5f;

        public MarsTerrain Terrain { get; private set; }
        public MarsDayNightCycle Cycle { get; private set; }
        public MarsWeatherSchedule Schedule { get; private set; }
        public WeatherController Weather { get; private set; }
        public MarsToolBelt Belt { get; private set; }

        public readonly List<Popup> Popups = new List<Popup>();
        public readonly List<Waypoint> Waypoints = new List<Waypoint>();

        private MarsAstronautPlayer player;
        private MarsOutpost outpost;

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

        public bool Outdoors { get { return Mission != null && Mission.Context.zone == PlayerZone.Outdoors; } }

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

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (MarsHomeScreen.IsOpen) return;
            UpdateWaypoints();
        }

        private void UpdateWaypoints()
        {
            Waypoints.Clear();
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (Outdoors && outpost != null && outpost.IsPlaced)
                Waypoints.Add(new Waypoint { position = outpost.DoorPosition + Vector3.up * Units(2.4f), label = "Airlock", paint = Paint.WarmOrange });
            if (State != null && State.astronaut.water < MissionSettings.WaterLow && outpost != null && outpost.IsPlaced)
                Waypoints.Add(new Waypoint { position = MarsWorldTasks.World(MarsWorldTasks.PumpPoint + Vector3.up * 2.6f), label = "Water pump", paint = Paint.PalePink });
            for (int i = Popups.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - Popups[i].time > 2f) Popups.RemoveAt(i);
        }
    }
}
