using System;
using System.IO;
using MarsSurvival.Base;
using MarsSurvival.Hardware;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.TimeAndWeather;
using MarsSurvival.UI;
using MarsSurvival.Vehicles;
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
        public MarsDashboardAdapter Dashboard { get; private set; }
        public MarsArduinoBridge Arduino { get; private set; }
        public MissionWorld World { get; private set; }
        public MarsToolBelt Tools { get; private set; }
        public bool Ready { get; private set; }
        public bool Paused { get; private set; }
        public bool PanelOpen { get; private set; }
        public bool ReducedMotion;
        public string Notice = "Choose a mission length to begin.";
        public string SelectedSiteId = "";
        public double AdvanceRemaining { get; private set; }
        public bool IsRunning { get { return Simulation != null && Simulation.State.phase == MissionPhase.Running; } }
        // Tasks do not block walking: the simulation already pauses task progress while the astronaut moves.
        public bool InputBlocked { get { return !IsRunning || Paused || PanelOpen || AdvanceRemaining > 0; } }
        private MarsTerrain terrain;
        private MarsOutpost outpost;
        private MarsAstronautPlayer player;
        private MarsRover rover;
        private MarsDayNightCycle cycle;
        private WeatherController weather;
        private MissionEnvironment environmentModel;
        private double pendingTime, lastOdometer, lastSaveSol;
        private WeatherEvent shownWeather;
        private float shownStrength;
        private string lastWarning = "";
        private bool initialPoseStored;
        private Vector3 initialRoverPosition;
        private float initialRoverYaw;
        private PlayerZone lastZone = PlayerZone.Habitat;
        private bool legacyHudHidden;
        private string SavePath { get { return Path.Combine(Application.persistentDataPath,
            Array.IndexOf(System.Environment.GetCommandLineArgs(), "--mission-smoke") >= 0 ? "mission-smoke.json" : "mission.json"); } }

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
            Dashboard = gameObject.AddComponent<MarsDashboardAdapter>();
            // Physical control panel: ArduinoLink opens the serial port in Start; the bridge picks the port first.
            gameObject.AddComponent<ArduinoLink>();
            Arduino = gameObject.AddComponent<MarsArduinoBridge>();
            Dashboard.Bind(() => Simulation == null ? new double[3] : Simulation.State.knobs,
                (channel, value) => SetPowerRequest(channel, value), StopAdvance);
            World = gameObject.AddComponent<MissionWorld>();
            Tools = gameObject.AddComponent<MarsToolBelt>();
            gameObject.AddComponent<MarsMissionHud>();
            gameObject.AddComponent<MissionSmokeCheck>();
            gameObject.AddComponent<MissionQaCheck>();
        }
        private void Update()
        {
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (rover == null) rover = FindObjectOfType<MarsRover>();
            if (cycle == null) cycle = FindObjectOfType<MarsDayNightCycle>();
            if (weather == null) weather = FindObjectOfType<WeatherController>();
            Ready = terrain != null && terrain.IsReady && outpost != null && outpost.IsPlaced && player != null && player.IsReady && rover != null && rover.IsReady;
            if (Ready && !initialPoseStored) { initialRoverPosition = rover.transform.position; initialRoverYaw = rover.transform.eulerAngles.y; initialPoseStored = true; }
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
            Context = World.GetContext(player, rover, outpost, terrain, Simulation.State);
            if (lastZone != PlayerZone.Outdoors && Context.zone == PlayerZone.Outdoors)
            {
                string cycleReason;
                var indoors = Context; indoors.zone = PlayerZone.Airlock;
                if (!Simulation.Command("airlock_exit", "", indoors, out cycleReason))
                {
                    Notice = cycleReason;
                    player.RestoreMissionPose(outpost.transform.TransformPoint(new Vector3(0, 0.05f, 15)), outpost.transform.eulerAngles.y);
                    Context = World.GetContext(player, rover, outpost, terrain, Simulation.State);
                }
            }
            lastZone = Context.zone;
            var position = player.transform.position;
            Simulation.State.playerX = position.x; Simulation.State.playerY = position.y; Simulation.State.playerZ = position.z;
            Simulation.State.playerYaw = player.transform.eulerAngles.y;
            if (!IsRunning || Paused) return;
            if (environmentModel == null) environmentModel = MissionEnvironment.LoadBundled(MissionDataSources.Seasons(), MissionDataSources.ScheduleSourceLabel);
            if (AdvanceRemaining <= 0) pendingTime += Time.unscaledDeltaTime;
            int budget = AdvanceRemaining > 0 ? 1000 : 10;
            for (int i = 0; i < budget && IsRunning; i++)
            {
                double dt = AdvanceRemaining > 0 ? Math.Min(10, AdvanceRemaining) : Math.Min(1, pendingTime);
                if (dt < 0.999 && AdvanceRemaining <= 0) break;
                var loc = terrain.GetLatLon(player.transform.position);
                Environment = environmentModel.Sample(Simulation.State.elapsedSeconds, Simulation.State.startUnix,
                    Simulation.State.seed, loc.x, loc.y);
                var ctx = Context;
                if (rover != null) { ctx.roverDistanceKm = Math.Max(0, rover.OdometerKm - lastOdometer); lastOdometer = rover.OdometerKm; }
                TaskKind before = Simulation.State.task.kind;
                Simulation.Step(dt, Environment, ctx);
                if (AdvanceRemaining > 0) AdvanceRemaining = Math.Max(0, AdvanceRemaining - dt); else pendingTime = Math.Max(0, pendingTime - dt);
                string warning = Simulation.Warnings.Count > 0 ? Simulation.Warnings[0] : "";
                if (AdvanceRemaining > 0 && ((warning != "" && warning != lastWarning) || (before != TaskKind.None && Simulation.State.task.kind == TaskKind.None)))
                    StopAdvance(warning == "" ? "Task complete." : warning);
                lastWarning = warning;
            }
            if (cycle != null) cycle.SetTime(Simulation.State.startUnix + Simulation.State.elapsedSeconds);
            UpdateTutorial();
            UpdateWeather();
            if (rover != null) rover.ApplyMissionBattery((float)Simulation.State.resources.roverKwh);
            if (IsRunning && Simulation.State.elapsedSeconds / MissionSettings.SolSeconds > lastSaveSol + 1)
            { SaveMission(); lastSaveSol = Simulation.State.elapsedSeconds / MissionSettings.SolSeconds; }
            if (!IsRunning) { StopAdvance(Simulation.State.endingReason); SetPanelOpen(true); }
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
        private void UpdateTutorial()
        {
            var s = Simulation.State;
            if (s.tutorialStep < 0 || s.tutorialStep >= 10) return;
            bool completed = false;
            switch (s.tutorialStep)
            {
                case 0: completed = PowerIsSufficient(); break;
                case 1: completed = s.crops.Exists(c => c.planted); break;
                case 2: completed = s.totalFoodHarvested > 0; break;
                case 3: completed = s.sites.Exists(site => site.sampled); break;
                case 4: completed = s.sites.Exists(site => site.analyzed); break;
                case 5: completed = s.researched.Count > 0; break;
                case 6: completed = s.installed.Count > 0; break;
                case 7: completed = s.sites.Exists(site => site.photographed); break;
                case 8: completed = s.reports.Exists(report => report.acknowledged); break;
                case 9: completed = s.repairsCompleted > 0 && s.panelsCleaned > 0; break;
            }
            if (completed) { s.tutorialStep++; StopAdvance("Guided objective complete. Review the next step in Learning."); }
        }
        private bool PowerIsSufficient()
        {
            for (int i = 0; i < 3; i++) if (Simulation.Power.neededKw[i] <= 0 || Simulation.Power.deliveredKw[i] < Simulation.Power.neededKw[i] * 0.95) return false;
            return true;
        }
        public void NewMission(MissionLength length)
        {
            if (!Ready) { Notice = "Waiting for the astronaut and outpost."; return; }
            Simulation = new MissionSimulation(MissionDataSources.CreateSettings(), MissionSimulation.NewMission(length, 2026));
            rover.RestoreMissionPose(initialRoverPosition, initialRoverYaw, 0);
            World.CreateSites(terrain, outpost, Simulation.State);
            player.RestoreMissionPose(outpost.transform.TransformPoint(new Vector3(0, 0.05f, 0)), outpost.transform.eulerAngles.y);
            pendingTime = 0; AdvanceRemaining = 0; Paused = false; lastSaveSol = 0;
            lastOdometer = rover == null ? 0 : rover.OdometerKm;
            lastZone = PlayerZone.Habitat;
            Dashboard.RequireTakeover(); SetPanelOpen(true); Notice = "Mission started. Balance the three power controls, then plant the greenhouse.";
        }
        public bool SetPowerRequest(PowerChannel channel, double value) { return Simulation != null && Simulation.SetPowerRequest(channel, value); }
        public void SetPanelOpen(bool value) { PanelOpen = value; Cursor.visible = value || Paused; Cursor.lockState = Cursor.visible ? CursorLockMode.None : CursorLockMode.Locked; }
        public void SetPaused(bool value) { Paused = value; if (value) StopAdvance("Paused."); SetPanelOpen(PanelOpen); }
        public void StopAdvance(string reason) { AdvanceRemaining = 0; Notice = reason; }
        public void Advance(double seconds)
        {
            if (!IsRunning || Context.zone == PlayerZone.Outdoors || Context.driving) { Notice = "Advance time while safely inside the habitat."; return; }
            Paused = false; AdvanceRemaining = Math.Max(0, seconds); lastWarning = Simulation.DecisionReason;
        }
        public void Execute(string action, string target = "")
        {
            if (Simulation == null) return;
            string reason;
            if (!Tools.HasAll(MarsToolBelt.ToolsFor(action, target ?? ""), out reason)) { Notice = reason; return; }
            Simulation.Command(action, target, Context, out reason); Notice = reason;
        }
        public void StartTask(TaskKind kind, string target = "")
        {
            if (Simulation == null) return;
            string station = kind == TaskKind.Plant || kind == TaskKind.Harvest ? "greenhouse" : kind == TaskKind.Treat ? "medical" : kind == TaskKind.Analyze ? "lab" : kind == TaskKind.Install ? "energy" : kind == TaskKind.DustPanels ? "array" : kind == TaskKind.RepairRover ? "rover" : "";
            if (station != "" && Context.station != station) { Notice = "Go to the " + station.Replace('_', ' ') + " to start this task."; return; }
            string reason;
            if (!Tools.HasAll(MarsToolBelt.ToolsFor(kind), out reason)) { Notice = reason; return; }
            foreach (string tool in MarsToolBelt.ToolsFor(kind)) { Tools.Hold(tool); break; }
            Simulation.StartTask(kind, target, Context, out reason); Notice = reason;
        }
        public void Research(string id) { if (Simulation == null) return; string reason; Simulation.Research(id, out reason); Notice = reason; }
        public void SelectSite(string id) { SelectedSiteId = id; }
        public void Restart() { NewMission(Simulation == null ? MissionLength.Month : Simulation.State.length); }
        public void CapturePhoto(string id)
        {
            if (Simulation == null) return;
            SiteState site = Simulation.FindSite(id); Camera cam = player.ViewCamera;
            if (site == null || !site.markerPlaced || Context.nearbySiteId != id) { Notice = "Place a marker and stand near the site first."; return; }
            string missing;
            if (!Tools.HasAll(MarsToolBelt.ToolsFor(TaskKind.Photograph), out missing)) { Notice = missing; return; }
            Vector3 screen = cam.WorldToViewportPoint(new Vector3((float)site.x, (float)site.y + 1, (float)site.z));
            if (screen.z <= 0 || screen.x < 0.1f || screen.x > 0.9f || screen.y < 0.1f || screen.y > 0.9f) { Notice = "Frame the marker in the centre of your view."; return; }
            string folder = Path.Combine(Application.persistentDataPath, "photos"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, id + ".png");
            var target = RenderTexture.GetTemporary(640, 360, 16); var old = cam.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(640, 360, TextureFormat.RGB24, false);
            try { cam.targetTexture = target; cam.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { cam.targetTexture = old; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Destroy(texture); }
            var ctx = Context; ctx.photoFramingValid = true; ctx.photoEvidencePath = path;
            string reason; Simulation.StartTask(TaskKind.Photograph, id, ctx, out reason); Notice = reason;
        }
        public void SaveMission()
        {
            if (Simulation == null || !IsRunning) return;
            try
            {
                var s = Simulation.State; var p = player.transform.position; s.playerX = p.x; s.playerY = p.y; s.playerZ = p.z; s.playerYaw = player.transform.eulerAngles.y;
                s.playerDriving = player.IsDriving;
                if (rover != null) { var rp = rover.transform.position; s.roverX = rp.x; s.roverY = rp.y; s.roverZ = rp.z; s.roverYaw = rover.transform.eulerAngles.y; s.roverOdometer = rover.OdometerKm; }
                Directory.CreateDirectory(Application.persistentDataPath); File.WriteAllText(SavePath + ".tmp", JsonUtility.ToJson(s, true));
                if (File.Exists(SavePath)) File.Replace(SavePath + ".tmp", SavePath, SavePath + ".bak"); else File.Move(SavePath + ".tmp", SavePath);
                Notice = "Mission saved.";
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
                Simulation = new MissionSimulation(MissionDataSources.CreateSettings(), s); World.CreateSites(terrain, outpost, s);
                rover.RestoreMissionPose(new Vector3((float)s.roverX, (float)s.roverY, (float)s.roverZ), (float)s.roverYaw, s.roverOdometer);
                lastOdometer = rover.OdometerKm;
                player.RestoreMissionPose(new Vector3((float)s.playerX, (float)s.playerY, (float)s.playerZ), (float)s.playerYaw);
                if (s.playerDriving) rover.RestoreDriving();
                Dashboard.RequireTakeover(); Paused = true; pendingTime = 0; StopAdvance("Loaded. Review your controls, then resume."); SetPanelOpen(true);
            }
            catch (Exception e) { Notice = "Load failed: " + e.Message; }
        }
        private static void ValidateSave(MissionState state)
        {
            if (state == null || state.schemaVersion != 1 || state.resources == null || state.astronaut == null || state.equipment == null || state.task == null || state.knobs == null || state.knobs.Length != 3 || !Enum.IsDefined(typeof(MissionLength), state.length))
                throw new InvalidDataException("Invalid or incompatible save.");
            foreach (object group in new object[] { state, state.resources, state.astronaut, state.equipment, state.task })
                foreach (var field in group.GetType().GetFields())
                    if (field.FieldType == typeof(double))
                    {
                        double value = (double)field.GetValue(group);
                        if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Save contains a non-finite value: " + field.Name);
                    }
            if (state.elapsedSeconds < 0 || state.elapsedSeconds > MissionSettings.Duration(state.length) || state.astronaut.health < 0 || state.astronaut.health > 100)
                throw new InvalidDataException("Save values are outside mission bounds.");
            foreach (double knob in state.knobs) if (double.IsNaN(knob) || knob < 0 || knob > 1) throw new InvalidDataException("Invalid dashboard setting.");
        }
        private static MissionState ReadSave(string path)
        {
            MissionState state = JsonUtility.FromJson<MissionState>(File.ReadAllText(path));
            ValidateSave(state);
            return state;
        }
    }
}
