using System;
using System.IO;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.TimeAndWeather;
using MarsSurvival.UI;
using MarsSurvival.World;
using MarsWeather;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    [DefaultExecutionOrder(-100)]
    public sealed class MarsMissionController : MonoBehaviour
    {
        public static MarsMissionController Instance { get; private set; }
        public MissionSimulation Simulation { get; private set; }
        public PlayerContext Context { get; private set; }
        public EnvironmentSample Environment { get; private set; }
        public MissionWorld World { get; private set; }
        public MarsToolPickup Tools { get; private set; }
        public bool Ready { get; private set; }
        public bool Paused { get; private set; }
        public bool ShopOpen { get; private set; }
        public bool ReducedMotion;
        public string Notice = "";
        /// <summary>Game seconds per real second (MissionSettings.TimeScale; the QA run slows it to 1).</summary>
        public double ClockScale { get; set; } = MissionSettings.TimeScale;
        public bool IsRunning { get { return Simulation != null && Simulation.IsRunning; } }
        public bool InputBlocked { get { return !IsRunning || Paused || ShopOpen || Simulation.Asleep; } }
        private MarsTerrain terrain;
        private MarsOutpost outpost;
        private MarsAstronautPlayer player;
        private MarsDayNightCycle cycle;
        private WeatherController weather;
        private MissionEnvironment environmentModel;
        private double pendingTime, lastSaveSol;
        private WeatherEvent shownWeather;
        private float shownStrength;
        private bool legacyHudHidden;
        private string SavePath { get { return Path.Combine(Application.persistentDataPath, "garden.json"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<MarsTerrain>() != null && FindObjectOfType<MarsMissionController>() == null)
                new GameObject("Mission Control").AddComponent<MarsMissionController>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (!Application.isEditor && !Application.isBatchMode && Screen.width < 1024)
                Screen.SetResolution(1280, 800, FullScreenMode.Windowed);
            World = gameObject.AddComponent<MissionWorld>();
            Tools = gameObject.AddComponent<MarsToolPickup>();
            gameObject.AddComponent<MarsMissionHud>();
            gameObject.AddComponent<MissionQaCheck>();
        }
        private void Update()
        {
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (cycle == null) cycle = FindObjectOfType<MarsDayNightCycle>();
            if (weather == null) weather = FindObjectOfType<WeatherController>();
            Ready = terrain != null && terrain.IsReady && outpost != null && outpost.IsPlaced && player != null && player.IsReady;
            if (cycle != null) { cycle.paused = true; cycle.debugKeys = false; }
            var schedule = FindObjectOfType<MarsWeatherSchedule>();
            if (schedule != null) schedule.automatic = false;
            var link = FindObjectOfType<MarsSurvival.Integration.MarsWorldWeatherLink>();
            if (link != null) link.debugKeys = false;
            if (Ready && !MarsHomeScreen.IsOpen && !legacyHudHidden)
            {
                var dataOverlay = FindObjectOfType<MarsTerrainDataOverlay>();
                if (dataOverlay != null) dataOverlay.visible = false;
                var clockHud = FindObjectOfType<MarsClockHud>();
                if (clockHud != null) clockHud.visible = false;
                outpost.showRoomLabel = false;
                legacyHudHidden = true;
            }
            if (!Ready || Simulation == null || MarsHomeScreen.IsOpen) return;
            Context = World.GetContext(player, outpost);
            var s = Simulation.State;
            var position = player.transform.position;
            s.playerX = position.x; s.playerY = position.y; s.playerZ = position.z;
            s.playerYaw = player.transform.eulerAngles.y;
            if (!IsRunning || Paused) return;
            if (environmentModel == null) environmentModel = MissionEnvironment.LoadBundled(MissionDataSources.Seasons(), MissionDataSources.ScheduleSourceLabel);
            // One sol lasts about 15 real minutes; asleep, time runs fast until 07:00.
            bool asleep = Simulation.Asleep;
            // Asleep: run to just past the wake time (steps under 1 s are skipped, so overshoot by one).
            pendingTime = asleep ? s.sleepUntil - s.elapsedSeconds + 1 : pendingTime + Time.unscaledDeltaTime * ClockScale;
            int budget = asleep ? 1000 : 10;
            for (int i = 0; i < budget && IsRunning && pendingTime >= 1; i++)
            {
                double dt = Math.Min(MissionSettings.FixedStepSeconds, pendingTime);
                Simulation.Step(dt, SampleEnvironment(), Context);
                pendingTime -= dt;
            }
            if (asleep && !Simulation.Asleep) { pendingTime = 0; Notice = "Good morning. Your plants grew overnight."; }
            if (cycle != null) cycle.SetTime(s.startUnix + s.elapsedSeconds);
            UpdateWeather();
            if (IsRunning && s.elapsedSeconds / MissionSettings.SolSeconds > lastSaveSol + 1)
            { SaveMission(); lastSaveSol = s.elapsedSeconds / MissionSettings.SolSeconds; }
            if (!IsRunning) { Notice = s.endingReason; SetShopOpen(false); }
        }
        private EnvironmentSample SampleEnvironment()
        {
            var s = Simulation.State;
            var loc = terrain.GetLatLon(player.transform.position);
            Environment = environmentModel.Sample(s.elapsedSeconds, s.startUnix, s.seed, loc.x, loc.y);
            return Environment;
        }
        private void UpdateWeather()
        {
            if (weather == null) return;
            WeatherEvent next = Environment.solarEvent > 0 ? WeatherEvent.SolarFlare : Environment.dust > 0 ? WeatherEvent.DustStorm : Environment.heat > 0 ? WeatherEvent.HeatStorm : Environment.cold > 0 ? WeatherEvent.ColdNight : WeatherEvent.Clear;
            float strength = (float)Math.Max(Environment.solarEvent, Math.Max(Environment.dust, Math.Max(Environment.heat, Environment.cold)));
            if (ReducedMotion) strength *= 0.3f;
            if (next != shownWeather || Math.Abs(strength - shownStrength) > 0.15f)
            { weather.Activate(next, strength); shownWeather = next; shownStrength = strength; }
        }
        public void NewMission()
        {
            if (!Ready) { Notice = "Waiting for the astronaut and outpost."; return; }
            Simulation = new MissionSimulation(MissionSimulation.NewMission(2026));
            StartAtMorning();
            player.RestoreMissionPose(outpost.transform.TransformPoint(new Vector3(0, 0.05f, 0)), outpost.transform.eulerAngles.y);
            pendingTime = 0; Paused = false; lastSaveSol = 0;
            SetShopOpen(false);
            Notice = "Sol 1, 07:00. Take the shovel and sprayer from the greenhouse, then sow your lettuce seeds.";
        }
        public void SetShopOpen(bool value) { ShopOpen = value && IsRunning; UpdateCursor(); }
        public void SetPaused(bool value) { Paused = value; UpdateCursor(); }
        private void UpdateCursor()
        {
            Cursor.visible = ShopOpen || Paused || !IsRunning;
            Cursor.lockState = Cursor.visible ? CursorLockMode.None : CursorLockMode.Locked;
        }
        public void Execute(string action, string target = "")
        {
            if (Simulation == null) return;
            string reason;
            if (!Tools.HasAll(MarsToolPickup.ToolsFor(action), out reason)) { Notice = reason; return; }
            Simulation.Command(action, target, Context, out reason); Notice = reason;
        }
        /// <summary>Sleep in a bunk: time runs fast to 07:00 next morning (needs keep ticking, plants keep growing).</summary>
        public void Sleep() { Execute("sleep"); pendingTime = 0; }
        public void Restart() { NewMission(); }
        /// <summary>Moves the mission start so sol 1 begins at 07:00 local time at the outpost.</summary>
        private void StartAtMorning()
        {
            if (environmentModel == null) environmentModel = MissionEnvironment.LoadBundled(MissionDataSources.Seasons(), MissionDataSources.ScheduleSourceLabel);
            var s = Simulation.State;
            for (int pass = 0; pass < 2; pass++)
            {
                double hour = SampleEnvironment().localHour;
                double shift = MissionSettings.MorningHour - hour;
                while (shift < 0) shift += 24;
                s.startUnix += shift * MissionSettings.HourSeconds;
            }
            Simulation.Environment = SampleEnvironment();
        }
        public void SaveMission()
        {
            if (Simulation == null || !IsRunning) return;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath); File.WriteAllText(SavePath + ".tmp", JsonUtility.ToJson(Simulation.State, true));
                if (File.Exists(SavePath)) File.Replace(SavePath + ".tmp", SavePath, SavePath + ".bak"); else File.Move(SavePath + ".tmp", SavePath);
                Notice = "Saved.";
            }
            catch (Exception e) { Notice = "Save failed: " + e.Message; }
        }
        public void LoadMission()
        {
            if (!Ready) return;
            try
            {
                MissionState s;
                try { s = ReadSave(SavePath); }
                catch { s = ReadSave(SavePath + ".bak"); Notice = "Recovered the previous backup save."; }
                Simulation = new MissionSimulation(s);
                player.RestoreMissionPose(new Vector3((float)s.playerX, (float)s.playerY, (float)s.playerZ), (float)s.playerYaw);
                pendingTime = 0; Notice = "Loaded."; SetShopOpen(false);
            }
            catch (Exception e) { Notice = "Load failed: " + e.Message; }
        }
        private static MissionState ReadSave(string path)
        {
            MissionState state = JsonUtility.FromJson<MissionState>(File.ReadAllText(path));
            if (state == null || state.schemaVersion != 3 || state.astronaut == null || state.phase != MissionPhase.Running || state.unlockedPlots < 0 || state.unlockedPlots > MissionSettings.MaxPlots || state.credits < 0)
                throw new InvalidDataException("Invalid or incompatible save.");
            return state;
        }
    }
}
