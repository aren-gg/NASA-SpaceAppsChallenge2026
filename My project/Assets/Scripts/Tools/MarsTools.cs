using System;
using System.Globalization;
using MarsSurvival.Gameplay;
using MarsSurvival.Simulation;
using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>How a tool is held in the first-person view.</summary>
    public enum ToolPose
    {
        /// <summary>Handle in the fist, head up (hammer, radio, wrench, canister, sprayer).</summary>
        Upright,
        /// <summary>Pointed ahead like a drill or a torch.</summary>
        Point,
        /// <summary>Long handle angled down in front, head near the ground (shovel, duster).</summary>
        Pole,
        /// <summary>Held up in front of the face with both hands.</summary>
        Camera,
    }

    public enum ToolMotion { Raise, Swing, Dig, Buzz, Sweep, Spray }

    /// <summary>What the crosshair is pointing at, within reach.</summary>
    public sealed class ToolTarget
    {
        public bool Hit;
        public Vector3 Point;
        public Vector3 Normal = Vector3.up;
        public float Distance;
        /// <summary>The hit is the terrain itself rather than an object.</summary>
        public bool Ground;
        public Collider Collider;
        /// <summary>Outpost science site the astronaut is standing beside (within 5 m), or "".</summary>
        public string SiteId = "";

        public T Get<T>() where T : Component
        {
            return Collider != null ? Collider.GetComponentInParent<T>() : null;
        }
    }

    /// <summary>
    /// What clicking would do now: an action that takes some seconds, or the reason it
    /// can't be done. <see cref="Key"/> identifies the thing being worked on, so an
    /// action keeps going while the player keeps aiming at the same thing.
    /// </summary>
    public sealed class ToolAction
    {
        public string Label;
        public float Seconds;
        public Action Complete;
        public bool Blocked;
        public object Key;

        public static ToolAction Do(string label, float seconds, object key, Action complete)
        {
            return new ToolAction { Label = label, Seconds = seconds, Key = key, Complete = complete };
        }

        public static ToolAction No(string reason)
        {
            return new ToolAction { Label = reason, Blocked = true };
        }
    }

    /// <summary>
    /// One of the 10 expedition tools (models from Task_assets). Subclasses say what
    /// clicking does for the current target (<see cref="Plan"/>), an optional second
    /// action on R, and an optional live readout. Each tool works on the outpost mission's
    /// daily tasks: the click plays the tool's motion, then starts the mission task (which
    /// runs while the astronaut stands still) or runs the mission command.
    /// </summary>
    public abstract class MarsTool
    {
        /// <summary>Tool ids in hotbar order; keys 1-9 then 0.</summary>
        public static readonly string[] Order =
        {
            "rock_hammer", "core_drill", "survey_camera", "comm_radio", "oxygen_canister",
            "work_lamp", "panel_brush", "power_wrench", "regolith_shovel", "hydro_sprayer",
        };

        public readonly string Id;
        public readonly string Name;
        public readonly string Hint;
        public readonly ToolPose Pose;
        /// <summary>Model-space direction of the working end (blade, bit, lens, head).</summary>
        public readonly Vector3 ModelForward;
        /// <summary>Model-space direction that should face up (or toward the sky side).</summary>
        public readonly Vector3 ModelUp;
        /// <summary>Model-space point the glove closes around. The handle runs along the model's Y axis.</summary>
        public readonly Vector3 Grip;
        /// <summary>Extra shift of the held position (camera space, metres) for bulky tools.</summary>
        public Vector3 ViewOffset;
        public Texture2D Icon;

        protected MarsToolBelt Belt;
        protected static MarsExpedition X { get { return MarsExpedition.Instance; } }

        protected MarsTool(string id, string name, string hint, ToolPose pose, Vector3 modelForward, Vector3 modelUp, Vector3 grip)
        {
            Id = id;
            Name = name;
            Hint = hint;
            Pose = pose;
            ModelForward = modelForward;
            ModelUp = modelUp;
            Grip = grip;
        }

        public void Attach(MarsToolBelt belt)
        {
            Belt = belt;
        }

        public virtual ToolMotion Motion { get { return ToolMotion.Raise; } }
        public virtual MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.None; } }

        /// <summary>The click action for this target, or null when there is nothing to do.</summary>
        public abstract ToolAction Plan(ToolTarget target);

        public virtual string SecondaryLabel(ToolTarget target) { return null; }
        public virtual void Secondary(ToolTarget target) { }
        public virtual string Readout(ToolTarget target) { return null; }
        public virtual void OnEquip() { }
        public virtual void OnUnequip() { }

        public static MarsTool[] CreateAll()
        {
            return new MarsTool[]
            {
                new RockHammer(), new CoreDrill(), new SurveyCamera(), new CommRadio(), new OxygenCanister(),
                new WorkLamp(), new PanelDuster(), new FieldWrench(), new RegolithShovel(), new HydroSprayer(),
            };
        }

        /// <summary>Key that selects a tool: "1".."9", "0".</summary>
        public static string KeyOf(string id)
        {
            int index = Array.IndexOf(Order, id);
            return index < 0 ? "?" : index == 9 ? "0" : (index + 1).ToString();
        }

        /// <summary>"the Field Wrench (8)": a tool name with its key, for hints.</summary>
        public static string Named(string id, string name)
        {
            return "the " + name + " (" + KeyOf(id) + ")";
        }

        protected static string Percent(float fraction)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100f) + "%";
        }

        protected static ToolAction NotHere(ToolTarget target, string what)
        {
            return target.Hit ? ToolAction.No(what) : null;
        }

        // ------------------------------------------------------------ outpost mission helpers

        protected static MarsMissionController M { get { return MarsMissionController.Instance; } }
        protected static MissionSimulation Sim { get { return M == null ? null : M.Simulation; } }
        protected static MissionState S { get { return Sim == null ? null : Sim.State; } }
        protected static PlayerContext Ctx { get { return M.Context; } }
        protected static bool Indoors { get { return Ctx.zone != PlayerZone.Outdoors && !Ctx.driving; } }
        protected static string Station { get { return Ctx.station ?? ""; } }

        protected static SiteState NearbySite
        {
            get { return S == null || string.IsNullOrEmpty(Ctx.nearbySiteId) ? null : Sim.FindSite(Ctx.nearbySiteId); }
        }

        /// <summary>Common refusals: no mission, or a mission task already running.</summary>
        protected static ToolAction Unavailable()
        {
            if (S == null || !M.IsRunning) return ToolAction.No("Start a mission first");
            if (S.task.kind != TaskKind.None)
                return ToolAction.No("Busy: " + TaskName(S.task.kind) + " " + Percent((float)(S.task.progressSeconds / Math.Max(1, S.task.durationSeconds))) +
                                     ". Stand still to finish it");
            return null;
        }

        /// <summary>Plays the tool for <paramref name="seconds"/>, then starts the mission task. Refuses with the mission's reason when it cannot start.</summary>
        protected ToolAction Task(string label, float seconds, TaskKind kind, string targetId)
        {
            TaskQuote quote = Sim.GetTaskQuote(kind, targetId, Ctx);
            if (!quote.available) return ToolAction.No(quote.reason.TrimEnd('.'));
            return ToolAction.Do(label, seconds, kind + "/" + targetId, () =>
            {
                M.StartTask(kind, targetId);
                if (S != null && S.task.kind == kind)
                    X.Say("Started " + TaskName(kind) + ": about " + Minutes(quote.durationSeconds) + ". Stand still until it finishes.");
            });
        }

        /// <summary>Plays the tool, then runs a mission command (eat, refill, survey...).</summary>
        protected static ToolAction Command(string label, float seconds, string command, string targetId = "")
        {
            return ToolAction.Do(label, seconds, command + "/" + targetId, () => M.Execute(command, targetId));
        }

        public static string TaskName(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.SampleSoil: return "core sampling";
                case TaskKind.PlaceMarker: return "placing the marker";
                case TaskKind.DustPanels: return "dusting the panels";
                case TaskKind.RepairBase: return "base repair";
                case TaskKind.RepairRover: return "rover repair";
                case TaskKind.Install: return "installation";
                case TaskKind.Analyze: return "sample analysis";
                case TaskKind.Photograph: return "photo documentation";
                case TaskKind.Transmit: return "transmitting to Houston";
                case TaskKind.Plant: return "planting";
                case TaskKind.Harvest: return "harvesting";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        protected static string Minutes(double seconds)
        {
            return seconds >= 3600 ? (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h"
                 : seconds >= 60 ? Math.Round(seconds / 60).ToString(CultureInfo.InvariantCulture) + " min"
                 : Math.Round(seconds).ToString(CultureInfo.InvariantCulture) + " s";
        }
    }

    // ---------------------------------------------------------------- 1-3: science (Field log sites)

    public sealed class RockHammer : MarsTool
    {
        // Handle along +Y with the head on top; the pick points along OBJ +X (Unity -X).
        public RockHammer() : base("rock_hammer", "Rock Hammer", "Survey a science site's rocks, then hammer in its survey marker.",
            ToolPose.Upright, Vector3.left, Vector3.up, Vector3.zero) { }

        public override ToolMotion Motion { get { return ToolMotion.Swing; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            SiteState site = NearbySite;
            if (site == null) return ToolAction.No("Walk to a science site marker (Tab > Field log) to survey it");
            if (!site.surveyed)
                return Command("survey the rocks of " + site.name, 1.6f, "survey", site.id);
            if (!site.markerPlaced)
                return Task("hammer in the survey marker", 1.2f, TaskKind.PlaceMarker, site.id);
            return ToolAction.No("Surveyed and marked. Photograph the marker with the Survey Camera (3)");
        }

        public override string Readout(ToolTarget target)
        {
            SiteState site = S == null ? null : NearbySite;
            return site == null ? null : SiteProgress(site);
        }

        public static string SiteProgress(SiteState site)
        {
            return site.name + "\nSurvey " + Tick(site.surveyed) + "   Marker " + Tick(site.markerPlaced) + "   Photo " + Tick(site.photographed) +
                   "\nCore " + Tick(site.sampled) + "   Lab analysis " + Tick(site.analyzed);
        }

        private static string Tick(bool done) { return done ? "done" : "to do"; }
    }

    public sealed class CoreDrill : MarsTool
    {
        // Pistol drill: the bit points along OBJ +X (Unity -X), grip at the handle.
        public CoreDrill() : base("core_drill", "Core Drill", "Drill a core sample at a science site, then analyze it at the lab bench.",
            ToolPose.Point, Vector3.left, Vector3.up, new Vector3(0.04f, 0.04f, 0f)) { }

        public override ToolMotion Motion { get { return ToolMotion.Buzz; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Drill; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            if (Station == "lab")
            {
                SiteState waiting = S.sites.Find(s => s.sampled && !s.analyzed);
                if (waiting == null) return ToolAction.No("No core samples are waiting. Drill one at a science site first");
                return Task("analyze the " + waiting.name + " core", 1.5f, TaskKind.Analyze, waiting.id);
            }
            SiteState site = NearbySite;
            if (site == null) return ToolAction.No("Drill at a science site, or analyze cores at the lab bench");
            if (site.sampled) return ToolAction.No(site.analyzed ? "This site's core is already analyzed" : "Core collected. Analyze it at the lab bench");
            return Task("drill a core sample", 2f, TaskKind.SampleSoil, site.id);
        }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            int waiting = S.sites.FindAll(s => s.sampled && !s.analyzed).Count;
            return "Cores waiting for the lab: " + waiting + "\nAnalyzed sites: " + S.sites.FindAll(s => s.analyzed).Count +
                   "\nResearch points: " + S.resources.researchPoints;
        }
    }

    public sealed class SurveyCamera : MarsTool
    {
        // Lens on +Z.
        public SurveyCamera() : base("survey_camera", "Survey Camera", "Photograph a site's survey marker to document it. R zooms in.",
            ToolPose.Camera, Vector3.forward, Vector3.up, new Vector3(0.075f, 0.06f, 0f)) { } // its hand grip, on the right side

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            SiteState site = NearbySite;
            if (site != null && site.markerPlaced && !site.photographed)
                return ToolAction.Do("photograph the " + site.name + " marker", 0f, "photo/" + site.id, () =>
                {
                    Belt.Flash();
                    MarsSfx.Play(MarsSfx.Sound.Shutter);
                    M.CapturePhoto(site.id);
                });
            if (site != null && !site.markerPlaced)
                return ToolAction.No("Place the survey marker first with the Rock Hammer (1)");
            return ToolAction.Do("take a photo", 0f, "photo", () =>
            {
                Belt.Flash();
                MarsSfx.Play(MarsSfx.Sound.Shutter);
                X.Say("Nice photo! At a science site, frame its survey marker to document it for Houston.");
            });
        }

        public override string SecondaryLabel(ToolTarget target) { return Belt.Zoomed ? "zoom out" : "zoom in"; }
        public override void Secondary(ToolTarget target) { Belt.Zoomed = !Belt.Zoomed; MarsSfx.Play(MarsSfx.Sound.Click); }
        public override void OnUnequip() { Belt.Zoomed = false; }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            return "Documented markers: " + S.sites.FindAll(s => s.photographed).Count + " of " + S.sites.Count +
                   "\nCentre the marker in the frame, then click.";
        }
    }

    // ---------------------------------------------------------------- 4-6: survival

    public sealed class CommRadio : MarsTool
    {
        // Upright handset; its screen faces OBJ +Z, so the back (-Z) points away from you.
        public CommRadio() : base("comm_radio", "Radio", "Queue a status report for Houston, then transmit it. R: \"Where is the nearest site?\"",
            ToolPose.Upright, Vector3.back, Vector3.up, new Vector3(0f, 0.07f, 0f)) { }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            bool queued = S.reports.Exists(r => !r.acknowledged);
            if (!queued) return Command("queue a status report for Houston", 0.6f, "report");
            if (M.Environment.signal <= 0.02)
                return ToolAction.No("No relay signal. Next window in " + Minutes(M.Environment.nextWindowSeconds));
            return Task("transmit the report to Houston", 0.6f, TaskKind.Transmit, "");
        }

        public override string SecondaryLabel(ToolTarget target) { return "find the nearest science site"; }

        public override void Secondary(ToolTarget target)
        {
            MarsSfx.Play(MarsSfx.Sound.Radio);
            X.GuideToNearestSite(true);
        }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            EnvironmentSample env = M.Environment;
            return "Signal " + Mathf.RoundToInt((float)env.signal * 100f) + "%" +
                   (env.signal > 0.02 ? "" : "   next window " + Minutes(env.nextWindowSeconds)) +
                   "\nQueued reports: " + S.reports.FindAll(r => !r.acknowledged).Count +
                   "   Acknowledged: " + S.reports.FindAll(r => r.acknowledged).Count +
                   "\nWeather: " + (env.label ?? "unknown") + "\nRadiation: " + X.RadiationWord;
        }
    }

    public sealed class OxygenCanister : MarsTool
    {
        // Upright bottle; the outlet points along OBJ +X (Unity -X).
        public OxygenCanister() : base("oxygen_canister", "Oxygen Canister", "Top up your suit on EVA. Inside the base, refill the canister and your suit.",
            ToolPose.Upright, Vector3.left, Vector3.up, new Vector3(0f, 0.24f, 0f))
        {
            ViewOffset = new Vector3(0f, 0.06f, 0f);
        }

        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Spray; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            ResourceState r = S.resources;
            MissionSettings settings = Sim.Settings;
            if (Indoors)
            {
                if (r.canisterOxygenGrams < settings.canisterCapacityGrams - 1) return Command("refill the canister from habitat oxygen", 1f, "canister_fill");
                if (r.suitOxygenGrams < settings.suitCapacityGrams - 1) return Command("refill your suit from habitat oxygen", 1f, "refill");
                return ToolAction.No("Your suit and the canister are full");
            }
            if (r.suitOxygenGrams >= settings.suitCapacityGrams - 1) return ToolAction.No("Your suit oxygen is already full");
            if (r.canisterOxygenGrams < 1) return ToolAction.No("The canister is empty. Refill it inside the base");
            return Command("top up your suit from the canister", 1f, "canister_use");
        }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            return "Suit oxygen: " + Mathf.RoundToInt(X.SuitOxygen) + "%  (" + S.resources.suitOxygenGrams.ToString("0", CultureInfo.InvariantCulture) + " g)" +
                   "\nCanister: " + S.resources.canisterOxygenGrams.ToString("0", CultureInfo.InvariantCulture) + " of " +
                   Sim.Settings.canisterCapacityGrams.ToString("0", CultureInfo.InvariantCulture) + " g" +
                   "\nAbout " + Sim.SuitHoursRemaining.ToString("0.0", CultureInfo.InvariantCulture) + " h of suit air left";
        }
    }

    public sealed class WorkLamp : MarsTool
    {
        // A torch: the lens is at OBJ +Y, the switch on OBJ +X (Unity -X) faces up.
        public WorkLamp() : base("work_lamp", "Work Lamp", "A bright light for nights and dust storms. It clips to your helmet when you switch tools.",
            ToolPose.Point, Vector3.up, Vector3.left, new Vector3(0f, 0.1f, 0f)) { }

        public override ToolAction Plan(ToolTarget target)
        {
            return ToolAction.Do(Belt.LampOn ? "turn the light off" : "turn the light on", 0f, "lamp", () =>
            {
                Belt.LampOn = !Belt.LampOn;
                MarsSfx.Play(MarsSfx.Sound.Click);
            });
        }
    }

    // ---------------------------------------------------------------- 7-0: base upkeep

    public sealed class PanelDuster : MarsTool
    {
        // Broom: the brush head is at the -Y end, the handle runs up to +Y.
        public PanelDuster() : base("panel_brush", "Panel Duster", "Sweep dust off the solar array (outside the energy room) so it makes power again.",
            ToolPose.Pole, Vector3.down, Vector3.forward, new Vector3(0f, 0.95f, 0f)) { } // held near the top of the handle

        public override ToolMotion Motion { get { return ToolMotion.Sweep; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Sweep; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            if (Station != "array") return ToolAction.No("Go out to the solar array beside the energy room");
            if (S.equipment.panelDust < 0.005) return ToolAction.No("The panels are already clean");
            ToolAction action = Task("sweep the dust off the panels", 1.4f, TaskKind.DustPanels, "");
            if (!action.Blocked)
            {
                Action start = action.Complete;
                action.Complete = () => { start(); Belt.Debris(target, Paint.ClaySand, 12); };
            }
            return action;
        }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            return "Panel dust: " + Percent((float)S.equipment.panelDust) + "\nSolar now: " +
                   Sim.Power.generatedKw.ToString("0.0", CultureInfo.InvariantCulture) + " kW";
        }
    }

    public sealed class FieldWrench : MarsTool
    {
        // Flat spanner standing up, jaws at +Y.
        public FieldWrench() : base("power_wrench", "Field Wrench", "Repair the base and the rover, and install upgrades in the energy room.",
            ToolPose.Upright, Vector3.forward, Vector3.up, new Vector3(0f, -0.07f, 0f)) { }

        public override ToolMotion Motion { get { return ToolMotion.Buzz; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Ratchet; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            if (Station == "rover")
            {
                if (S.equipment.roverCondition >= 0.995) return ToolAction.No("The rover is in good condition");
                return Task("repair the rover", 1.6f, TaskKind.RepairRover, "");
            }
            if (Station == "energy")
            {
                foreach (ResearchNode node in ResearchCatalog.Nodes)
                    if (S.researched.Contains(node.id) && !S.installed.Contains(node.id))
                        return Task("install " + node.title, 1.8f, TaskKind.Install, node.id);
                if (Sim.Installed("S1") && S.equipment.extraPanels < 2) return Task("install an extra solar panel", 1.8f, TaskKind.Install, "panel");
                if (Sim.Installed("E0") && S.equipment.extraBatteries < 2) return Task("install a battery cabinet", 1.8f, TaskKind.Install, "battery");
            }
            if (!Indoors) return ToolAction.No("Repair the base from inside, or stand beside the rover");
            if (S.equipment.baseCondition >= 0.995) return ToolAction.No("The base does not need repair");
            return Task("repair the base", 1.6f, TaskKind.RepairBase, "");
        }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            return "Base " + Percent((float)S.equipment.baseCondition) + "   Rover " + Percent((float)S.equipment.roverCondition) +
                   "\nParts: " + S.resources.parts.ToString("0", CultureInfo.InvariantCulture) + " (a repair uses 2)" +
                   "\nResearched, not installed: " + Math.Max(0, S.researched.Count - S.installed.Count);
        }
    }

    public sealed class RegolithShovel : MarsTool
    {
        private bool staple;

        // The blade is at the -Y end, the D-handle at +Y.
        public RegolithShovel() : base("regolith_shovel", "Shovel", "Plant a greenhouse bed. R switches between a fast leafy crop and a filling staple crop.",
            ToolPose.Pole, Vector3.down, Vector3.forward, new Vector3(0f, 0.85f, 0f)) { } // held by the D-handle

        public override ToolMotion Motion { get { return ToolMotion.Dig; } }

        private string Crop { get { return staple ? "staple" : "leafy"; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            if (Station != "greenhouse") return ToolAction.No("Plant in the greenhouse beds");
            CropState bed = S.crops.Find(c => !c.planted);
            if (bed == null) return ToolAction.No("Both beds are planted. Harvest them with the Sprayer (0) when ready");
            ToolAction action = Task("plant a " + Crop + " crop in bed " + (bed.bed + 1), 1.2f, TaskKind.Plant,
                bed.bed.ToString(CultureInfo.InvariantCulture) + ":" + Crop);
            if (!action.Blocked)
            {
                Action start = action.Complete;
                action.Complete = () => { start(); Belt.Debris(target, Paint.Terracotta, 10); };
            }
            return action;
        }

        public override string SecondaryLabel(ToolTarget target) { return staple ? "plant leafy crops (fast)" : "plant staple crops (filling)"; }
        public override void Secondary(ToolTarget target) { staple = !staple; MarsSfx.Play(MarsSfx.Sound.Click); }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            return "Planting: " + Crop + (staple ? " (10 sols, 50 portions)" : " (5 sols, 24 portions)") +
                   "\nSeeds: " + S.resources.seeds.ToString("0", CultureInfo.InvariantCulture) + "   Nutrients: " + S.resources.nutrients.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    public sealed class HydroSprayer : MarsTool
    {
        // Watering can: the spout points along OBJ +X (Unity -X).
        public HydroSprayer() : base("hydro_sprayer", "Sprayer", "Harvest ready greenhouse beds, or water a stressed bed so it recovers.",
            ToolPose.Upright, Vector3.left, Vector3.up, new Vector3(0f, 0.16f, 0.11f))
        {
            // Carried by the side of the tank, like a watering can, so the tank stays in view.
            ViewOffset = new Vector3(0f, 0.02f, 0.02f);
        }

        public override ToolMotion Motion { get { return ToolMotion.Spray; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Spray; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            if (Station != "greenhouse") return ToolAction.No("Tend the crops in the greenhouse");
            CropState ready = S.crops.Find(c => c.ready);
            if (ready != null) return Task("harvest bed " + (ready.bed + 1), 1f, TaskKind.Harvest, ready.bed.ToString(CultureInfo.InvariantCulture));
            CropState thirsty = S.crops.Find(c => c.planted && !c.ready && c.stress >= 0.05);
            if (thirsty != null) return Command("water bed " + (thirsty.bed + 1), 0.9f, "water_crop", thirsty.bed.ToString(CultureInfo.InvariantCulture));
            if (!S.crops.Exists(c => c.planted)) return ToolAction.No("Plant a bed with the Shovel (9) first");
            return ToolAction.No("The crops are growing well");
        }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            string text = "";
            foreach (CropState crop in S.crops)
                text += (text.Length > 0 ? "\n" : "") + "Bed " + (crop.bed + 1) + ": " +
                        (crop.ready ? "ready to harvest" : crop.planted ? crop.crop + " " + Percent((float)crop.growth) + ", stress " + Percent((float)crop.stress) : "empty");
            return text + "\nClean water: " + S.resources.cleanWaterLiters.ToString("0", CultureInfo.InvariantCulture) + " L";
        }
    }
}
