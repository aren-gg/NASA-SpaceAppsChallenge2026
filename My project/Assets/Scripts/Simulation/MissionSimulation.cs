using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarsSurvival.Simulation
{
    // Tuned teaching model; source-derived environmental inputs arrive through EnvironmentSample.
    public sealed partial class MissionSimulation
    {
        public MissionState State { get; private set; }
        public MissionSettings Settings { get; private set; }
        public PowerSnapshot Power { get; private set; } = new PowerSnapshot();
        public EnvironmentSample Environment;
        public List<string> Warnings { get; private set; } = new List<string>();
        public bool DecisionRequired { get { return Warnings.Count > 0; } }
        public bool NeedsDecision { get { return DecisionRequired; } }
        public string DecisionReason { get { return Warnings.Count > 0 ? Warnings[0] : ""; } }
        public string LastResult { get; private set; } = "";
        public double DurationSeconds { get { return MissionSettings.Duration(State.length); } }
        public double WorkEfficiency { get { return Clamp(1 - State.astronaut.fatigue / 160, 0.35, 1) * (State.astronaut.satiety < 20 ? 0.7 : 1) * (0.7 + 0.3 * State.astronaut.fitness / 100); } }
        public double BatteryCapacity { get { return (Settings.batteryCapacityKwh + State.equipment.extraBatteries * 90) * (Installed("E1") ? 1.5 : 1); } }
        public double SuitHoursRemaining { get { return State.resources.suitOxygenGrams / (Settings.oxygenGramsPerSol / (MissionSettings.SolSeconds / 3600)); } }

        public MissionSimulation(MissionSettings settings, MissionState state)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (state == null) throw new ArgumentNullException("state");
            Settings = settings;
            State = state;
            EnsureCollections();
            Power.batteryCapacityKwh = BatteryCapacity;
        }

        public static MissionState NewMission(MissionLength length, int seed)
        {
            MissionState state = new MissionState { length = length, seed = seed, phase = MissionPhase.Running };
            state.crops.Add(new CropState { bed = 0 });
            state.crops.Add(new CropState { bed = 1, crop = "staple" });
            double sols = MissionSettings.Duration(length) / MissionSettings.SolSeconds;
            state.resources.cleanWaterLiters = 300 + sols * 4;
            state.resources.nutrients = 60 + sols;
            state.resources.food = 50;
            state.resources.parts = 80;
            state.history.Add("Mission started. Solar panels generate electricity; the three knobs decide how it is spent.");
            return state;
        }

        public bool Installed(string id) { return State.installed.Contains(id); }
        public SiteState FindSite(string id) { return State.sites.Find(s => s.id == id); }
        public CropState FindCrop(string target)
        {
            int index;
            string part = (target ?? "0").Split(':')[0];
            return int.TryParse(part, out index) ? State.crops.Find(c => c.bed == index) : null;
        }

        public bool SetPowerRequest(PowerChannel channel, double value)
        {
            if (!Active || !Finite(value) || (int)channel < 0 || (int)channel > 2) return false;
            State.knobs[(int)channel] = Clamp(value, 0, 1);
            return true;
        }

        public bool Research(string id, out string reason)
        {
            ResearchNode node = ResearchCatalog.Find(id);
            if (!Active) return Fail("The mission is not running.", out reason);
            if (node == null) return Fail("Unknown research.", out reason);
            if (State.researched.Contains(node.id)) return Fail("Already researched.", out reason);
            foreach (string prerequisite in node.prerequisites)
                if (!State.researched.Contains(prerequisite)) return Fail("Research " + prerequisite + " first.", out reason);
            if (State.resources.researchPoints < node.points) return Fail("Not enough research points.", out reason);
            State.resources.researchPoints -= node.points;
            State.researched.Add(node.id);
            Log("Researched " + node.title + ". Install the equipment to activate it.");
            reason = LastResult;
            return true;
        }

        public bool Command(string action, string target, PlayerContext ctx, out string reason)
        {
            reason = "";
            if (!Active) return Fail("The mission is not running.", out reason);
            string command = (action ?? "").ToLowerInvariant();
            bool indoor = IsIndoors(ctx);
            switch (command)
            {
                case "eat":
                    if (!indoor) return Fail("Eat from the galley inside the habitat.", out reason);
                    if (State.astronaut.satiety > 95) return Fail("You do not need another meal yet.", out reason);
                    if (State.resources.food < 1) return Fail("No food portions remain.", out reason);
                    State.resources.food -= 1;
                    State.astronaut.satiety = Math.Min(100, State.astronaut.satiety + (Installed("H1") ? 35 : 30));
                    reason = "Ate one food portion.";
                    break;
                case "drink":
                    if (State.astronaut.hydration > 95) return Fail("Hydration is already sufficient.", out reason);
                    if (State.resources.cleanWaterLiters < 0.5) return Fail("No clean water is available.", out reason);
                    if (!indoor) return Fail("Return to a connected drinking-water station.", out reason);
                    State.resources.cleanWaterLiters -= 0.5;
                    State.resources.wasteWaterLiters += 0.4;
                    State.totalWaterLostLiters += 0.1;
                    State.astronaut.hydration = Math.Min(100, State.astronaut.hydration + 18);
                    reason = "Drank 0.5 L of water.";
                    break;
                case "refill":
                    if (!indoor) return Fail("Suit refill requires a habitat connection.", out reason);
                    double transfer = Math.Min(Settings.suitCapacityGrams - State.resources.suitOxygenGrams, State.resources.oxygenGrams);
                    if (transfer <= 0) return Fail("Suit is full or habitat oxygen is empty.", out reason);
                    State.resources.suitOxygenGrams += transfer;
                    State.resources.oxygenGrams -= transfer;
                    reason = "Refilled suit from habitat oxygen.";
                    break;
                case "airlock_exit":
                    if (!indoor) return Fail("Use the habitat airlock to begin EVA.", out reason);
                    if (State.resources.suitOxygenGrams < 100 || State.resources.fieldKwh < 0.1) return Fail("Refill suit oxygen and field battery before EVA.", out reason);
                    if (State.resources.batteryKwh < 0.02 || State.resources.oxygenGrams < 10) return Fail("Airlock needs 0.02 kWh and 10 g of habitat oxygen.", out reason);
                    State.resources.batteryKwh -= 0.02;
                    State.resources.oxygenGrams -= 10;
                    State.totalConsumedKwh += 0.02;
                    reason = "Airlock cycled; suit supplies now support the EVA.";
                    break;
                case "routine": State.routineEnabled = Toggle(State.routineEnabled, target); reason = "Daily routine " + (State.routineEnabled ? "enabled." : "disabled."); break;
                case "greenhouse": State.greenhouseEnabled = Toggle(State.greenhouseEnabled, target); reason = "Greenhouse power " + (State.greenhouseEnabled ? "enabled." : "disabled."); break;
                case "charge_rover": State.chargeRover = Toggle(State.chargeRover, target); reason = "Rover charging " + (State.chargeRover ? "requested." : "disabled."); break;
                case "charge_field": State.chargeField = Toggle(State.chargeField, target); reason = "Field battery charging " + (State.chargeField ? "requested." : "disabled."); break;
                case "radio":
                    RadioMode mode;
                    if (!Enum.TryParse<RadioMode>(target, true, out mode)) return Fail("Choose Off, Listen or Transmit.", out reason);
                    State.radioMode = mode;
                    reason = "Radio " + mode.ToString().ToLowerInvariant() + ".";
                    break;
                case "report":
                    string fingerprint = ReportFingerprint();
                    if (State.reports.Exists(r => r.id == fingerprint)) return Fail("This status report is already queued or acknowledged.", out reason);
                    if (State.reports.Count >= 64) State.reports.RemoveAll(r => r.acknowledged);
                    if (State.reports.Count >= 64) return Fail("Transmit queued reports before creating more.", out reason);
                    State.reports.Add(new ReportState { id = fingerprint, createdSeconds = State.elapsedSeconds,
                        summary = string.Format(CultureInfo.InvariantCulture, "Sol {0:0.0}: health {1:0}, battery {2:0.0} kWh; {3} analyzed samples, {4} marker photos; {5} repairs. {6}",
                            State.elapsedSeconds / MissionSettings.SolSeconds + 1, State.astronaut.health, State.resources.batteryKwh,
                            State.sites.FindAll(s => s.analyzed).Count, State.sites.FindAll(s => s.photographed).Count, State.repairsCompleted, Environment.label ?? "") });
                    State.reportsCreated++;
                    reason = "Report queued for Houston. Select Transmit when a link is available.";
                    break;
                case "survey":
                case "measure":
                    SiteState site = FindSite(target);
                    if (site == null || ctx.nearbySiteId != site.id) return Fail("Stand beside the selected science site.", out reason);
                    if ((command == "survey" && site.surveyed) || (command == "measure" && site.measured)) return Fail("This observation is already recorded.", out reason);
                    if (State.task.kind != TaskKind.None) return Fail("Finish the current task first.", out reason);
                    if (!SpendFieldEnergy(0.01, ctx.driving)) return Fail("The connected field battery is empty.", out reason);
                    if (command == "survey") site.surveyed = true; else site.measured = true;
                    site.discovered = true;
                    State.resources.researchPoints += 4;
                    reason = command == "survey" ? "Terrain survey recorded (+4 research). Heights are derived from the DEM." : "Environmental reading recorded (+4 research). See source labels for model assumptions.";
                    break;
                case "cancel": CancelTask(); reason = LastResult; return true;
                default: return Fail("Unknown action.", out reason);
            }
            LastResult = reason;
            return true;
        }

        public bool StartTask(TaskKind kind, string targetId, PlayerContext ctx, out string reason)
        {
            TaskState task;
            if (!BuildTask(kind, targetId, ctx, out task, out reason)) return false;
            State.resources.parts -= task.reservedParts;
            State.resources.seeds -= task.reservedSeeds;
            State.resources.medicine -= task.reservedMedicine;
            State.task = task;
            if (kind == TaskKind.Transmit) State.radioMode = RadioMode.Transmit;
            reason = "Started " + kind + ".";
            LastResult = reason;
            return true;
        }

        public TaskQuote GetTaskQuote(TaskKind kind, string targetId, PlayerContext ctx)
        {
            string previousResult = LastResult, reason;
            TaskState task;
            bool available = BuildTask(kind, targetId, ctx, out task, out reason);
            LastResult = previousResult;
            TaskQuote quote = new TaskQuote { available = available, reason = reason ?? "" };
            if (task == null) return quote;
            quote.durationSeconds = task.durationSeconds / (kind == TaskKind.Rest || kind == TaskKind.Transmit ? 1 : WorkEfficiency);
            quote.powerKw = kind == TaskKind.Transmit ? 0.18 : task.powerKw;
            quote.estimatedKwh = quote.powerKw * quote.durationSeconds / 3600;
            quote.parts = task.reservedParts; quote.seeds = task.reservedSeeds; quote.medicine = task.reservedMedicine;
            quote.fieldPower = task.fieldPower;
            quote.energySource = task.fieldPower ? task.roverPower ? "Rover battery" : "Field battery" : "Habitat grid";
            return quote;
        }

        private bool BuildTask(TaskKind kind, string targetId, PlayerContext ctx, out TaskState task, out string reason)
        {
            reason = "";
            task = null;
            if (!Active) return Fail("The mission is not running.", out reason);
            if (State.task.kind != TaskKind.None) return Fail("Finish or cancel the current task first.", out reason);
            if (ctx.moving || ctx.driving) return Fail("Stop moving before starting a task.", out reason);
            bool indoor = IsIndoors(ctx);
            task = new TaskState { kind = kind, targetId = targetId ?? "", fieldPower = !indoor,
                roverPower = ctx.driving, station = ctx.station ?? "", durationSeconds = 60, powerKw = 0.25 };
            SiteState site = FindSite(targetId);
            CropState crop = FindCrop(targetId);
            switch (kind)
            {
                case TaskKind.Rest:
                    if (!indoor) return Fail("Rest inside the habitat.", out reason);
                    task.durationSeconds = 6 * 3600; task.powerKw = 0; break;
                case TaskKind.Exercise:
                    if (!indoor) return Fail("Use the exercise equipment inside the habitat.", out reason);
                    task.durationSeconds = 1800; task.powerKw = 0.08; break;
                case TaskKind.Treat:
                    if (!indoor) return Fail("Medical treatment requires the habitat medical station.", out reason);
                    if (State.astronaut.health >= 99) return Fail("Medical treatment is not needed.", out reason);
                    if (State.resources.medicine < 1) return Fail("No medical supplies remain.", out reason);
                    task.reservedMedicine = 1; task.durationSeconds = 120; task.powerKw = 0.6; break;
                case TaskKind.Plant:
                    if (!indoor || crop == null) return Fail("Choose a greenhouse bed inside the habitat.", out reason);
                    if (crop.planted) return Fail("This bed is already planted.", out reason);
                    string[] pieces = task.targetId.Split(':');
                    task.cropId = pieces.Length > 1 ? pieces[1].ToLowerInvariant() : crop.crop;
                    if (task.cropId != "leafy" && task.cropId != "staple") return Fail("Choose leafy or staple crops.", out reason);
                    if (State.resources.seeds < 1) return Fail("No seeds remain.", out reason);
                    task.reservedSeeds = 1; task.durationSeconds = 30; task.powerKw = 0.15; break;
                case TaskKind.Harvest:
                    if (!indoor || crop == null || !crop.ready) return Fail("Choose a ready greenhouse crop.", out reason);
                    if (State.resources.food >= Settings.foodCapacity) return Fail("Food storage is full.", out reason);
                    task.durationSeconds = 30; task.powerKw = 0.15; break;
                case TaskKind.RepairBase:
                    if (!indoor) return Fail("Base repairs require access inside the habitat.", out reason);
                    if (State.equipment.baseCondition >= 0.995) return Fail("The base does not need repair.", out reason);
                    task.reservedParts = 2; task.durationSeconds = 120; task.powerKw = 0.5; break;
                case TaskKind.RepairRover:
                    if (ctx.distanceFromBase > 80 && !string.Equals(ctx.station, "rover", StringComparison.OrdinalIgnoreCase)) return Fail("Stand beside the rover to repair it.", out reason);
                    if (State.equipment.roverCondition >= 0.995) return Fail("The rover does not need repair.", out reason);
                    task.reservedParts = 2; task.durationSeconds = 120; task.powerKw = 0.5; break;
                case TaskKind.SampleSoil:
                case TaskKind.PlaceMarker:
                case TaskKind.Photograph:
                    if (site == null || ctx.nearbySiteId != site.id) return Fail("Stand beside the selected science site.", out reason);
                    if (kind == TaskKind.SampleSoil && site.sampled) return Fail("This sample is already collected.", out reason);
                    if (kind == TaskKind.PlaceMarker && site.markerPlaced) return Fail("This site already has a marker.", out reason);
                    if (kind == TaskKind.Photograph && site.photographed) return Fail("This marker is already documented.", out reason);
                    if (kind == TaskKind.Photograph && (!site.markerPlaced || !ctx.photoFramingValid || string.IsNullOrEmpty(ctx.photoEvidencePath))) return Fail("Frame the survey marker and target, then capture the photo.", out reason);
                    task.durationSeconds = kind == TaskKind.SampleSoil ? 30 : kind == TaskKind.PlaceMarker ? 12 : 5;
                    task.powerKw = kind == TaskKind.SampleSoil ? 0.4 : 0.08;
                    task.evidencePath = ctx.photoEvidencePath ?? "";
                    break;
                case TaskKind.Analyze:
                    if (!indoor) return Fail("Analyze samples at the habitat laboratory.", out reason);
                    if (site == null || !site.sampled || site.analyzed) return Fail("Choose an unanalyzed soil sample.", out reason);
                    task.durationSeconds = 60; task.powerKw = 0.5; break;
                case TaskKind.DustPanels:
                    if (ctx.distanceFromBase > 100) return Fail("Return to the outpost solar array.", out reason);
                    if (State.equipment.panelDust < 0.005) return Fail("The panels are already clean.", out reason);
                    task.durationSeconds = 60; task.powerKw = 0.3; break;
                case TaskKind.Install:
                    if (!indoor) return Fail("Install equipment from the habitat energy/work station.", out reason);
                    ResearchNode node = ResearchCatalog.Find(task.targetId);
                    if (task.targetId == "panel")
                    {
                        if (!Installed("S1") || State.equipment.extraPanels >= 2) return Fail("Install Array expansion first; two additional panel slots are available.", out reason);
                        task.reservedParts = 8; task.durationSeconds = 180;
                    }
                    else if (task.targetId == "battery")
                    {
                        if (!Installed("E0") || State.equipment.extraBatteries >= 2) return Fail("Install Battery modules first; two additional battery slots are available.", out reason);
                        task.reservedParts = 6; task.durationSeconds = 180;
                    }
                    else
                    {
                        if (node == null || !State.researched.Contains(node.id)) return Fail("Research this equipment first.", out reason);
                        if (Installed(node.id)) return Fail("This upgrade is already installed.", out reason);
                        task.targetId = node.id; task.reservedParts = node.parts; task.durationSeconds = node.installationSeconds;
                    }
                    task.powerKw = 0.6; break;
                case TaskKind.Transmit:
                    if (!State.reports.Exists(r => !r.acknowledged)) return Fail("Compose a report first.", out reason);
                    task.targetId = State.reports.Find(r => !r.acknowledged).id;
                    task.durationSeconds = 120; task.powerKw = 0; break;
                default: return Fail("Choose an available task.", out reason);
            }
            if (State.resources.parts < task.reservedParts) return Fail("Not enough parts.", out reason);
            if (task.fieldPower && (task.roverPower ? State.resources.roverKwh : State.resources.fieldKwh) <= 0 && task.powerKw > 0) return Fail("Charge the field equipment before starting.", out reason);
            reason = "Ready.";
            return true;
        }

        public void CancelTask()
        {
            if (!Active) return;
            TaskState task = State.task;
            if (task.kind == TaskKind.None) { LastResult = "No task is active."; return; }
            // Materials remain reserved until completion. Work energy is never refunded.
            State.resources.parts += task.reservedParts;
            State.resources.seeds += task.reservedSeeds;
            State.resources.medicine += task.reservedMedicine;
            State.task = new TaskState();
            Log("Cancelled " + task.kind + "; unused supplies returned, spent energy retained.");
        }

        public void Step(double seconds, EnvironmentSample environment, PlayerContext context)
        {
            if (!Active || !Finite(seconds) || seconds <= 0) return;
            Environment = Sanitize(environment);
            double remaining = Math.Min(seconds, DurationSeconds - State.elapsedSeconds);
            bool distanceApplied = false;
            while (remaining > 0.0000001 && Active)
            {
                double dt = Math.Min(Math.Max(0.1, Settings.fixedStepSeconds), remaining);
                if (State.task.kind != TaskKind.None && State.task.kind != TaskKind.Transmit)
                    dt = Math.Min(dt, Math.Max(0.001, (State.task.durationSeconds - State.task.progressSeconds) / Math.Max(0.1, TaskEfficiency())));
                PlayerContext ctx = context;
                if (distanceApplied) ctx.roverDistanceKm = 0;
                Tick(dt, ctx);
                distanceApplied = true;
                remaining -= dt;
            }
            RefreshWarnings(context);
        }

        private void Tick(double seconds, PlayerContext context)
        {
            double hours = seconds / 3600, sols = seconds / MissionSettings.SolSeconds;
            if (State.routineEnabled && IsIndoors(context) && !context.moving && !context.driving) RunRoutine(context);
            ApplyPower(seconds, context);
            TickLifeSupport(seconds, context);
            TickCrops(seconds);
            TickAstronaut(seconds, context);
            if (State.astronaut.health > 0)
            {
                TickRadio(seconds, context);
                TickTask(seconds, context);
            }
            double wearFactor = Installed("P3") ? 0.5 : 1;
            State.equipment.baseCondition = Clamp(State.equipment.baseCondition - sols * (0.00025 + Environment.dust * 0.001) * wearFactor, 0, 1);
            State.equipment.panelCondition = Clamp(State.equipment.panelCondition - sols * 0.0001 * wearFactor, 0.25, 1);
            State.equipment.panelDust = Clamp(State.equipment.panelDust + sols * (0.0006 + Environment.dust * 0.015), 0, 0.85);
            if (context.roverDistanceKm > 0)
            {
                double km = Math.Max(0, context.roverDistanceKm);
                double driveUse = Math.Min(State.resources.roverKwh, km * 0.5 * (1 + (1 - State.equipment.roverCondition) * 0.5));
                State.resources.roverKwh -= driveUse;
                State.totalConsumedKwh += driveUse;
                State.equipment.roverCondition = Clamp(State.equipment.roverCondition - km * 0.0008, 0, 1);
                State.roverOdometer += km;
            }
            State.elapsedSeconds = Math.Min(DurationSeconds, State.elapsedSeconds + seconds);
            ClampState();
            if (State.astronaut.health <= 0)
            {
                State.phase = MissionPhase.Lost;
                State.endingReason = string.IsNullOrEmpty(State.astronaut.lastDamageCause) ? "The astronaut's health reached zero." : State.astronaut.lastDamageCause;
                Log("Mission ended: " + State.endingReason);
            }
            else if (State.elapsedSeconds >= DurationSeconds - 0.000001)
            {
                State.elapsedSeconds = DurationSeconds;
                State.phase = MissionPhase.Won;
                State.endingReason = "You kept the astronaut alive for the selected Martian mission.";
                Log(State.endingReason);
            }
        }

        private bool Active { get { return State.phase == MissionPhase.Running; } }
        private static bool IsIndoors(PlayerContext ctx) { return ctx.zone != PlayerZone.Outdoors && !ctx.driving; }
        private static bool Toggle(bool current, string value) { return string.IsNullOrEmpty(value) ? !current : value == "on" || value == "true"; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private bool Fail(string text, out string reason) { reason = text; LastResult = text; return false; }
        private void Log(string text)
        {
            LastResult = text;
            State.history.Add("Sol " + (State.elapsedSeconds / MissionSettings.SolSeconds + 1).ToString("0.0", CultureInfo.InvariantCulture) + ": " + text);
            if (State.history.Count > 100) State.history.RemoveRange(0, State.history.Count - 100);
        }

        private void EnsureCollections()
        {
            if (State.knobs == null || State.knobs.Length != 3) State.knobs = new[] { 0.42, 0.45, 0.05 };
            if (State.crops == null) State.crops = new List<CropState>();
            if (State.sites == null) State.sites = new List<SiteState>();
            if (State.researched == null) State.researched = new List<string>();
            if (State.installed == null) State.installed = new List<string>();
            if (State.reports == null) State.reports = new List<ReportState>();
            if (State.history == null) State.history = new List<string>();
            if (State.task == null) State.task = new TaskState();
        }

        private EnvironmentSample Sanitize(EnvironmentSample value)
        {
            value.solarFraction = Finite(value.solarFraction) ? Clamp(value.solarFraction, 0, 2) : 0;
            value.radiationMicrograyPerHour = Finite(value.radiationMicrograyPerHour) ? Math.Max(0, value.radiationMicrograyPerHour) : 0;
            value.temperatureC = Finite(value.temperatureC) ? Clamp(value.temperatureC, -150, 80) : -65;
            value.dust = Finite(value.dust) ? Clamp(value.dust, 0, 1) : 0;
            value.solarEvent = Finite(value.solarEvent) ? Clamp(value.solarEvent, 0, 1) : 0;
            value.signal = Finite(value.signal) ? Clamp(value.signal, 0, 1) : 0;
            return value;
        }
    }
}
