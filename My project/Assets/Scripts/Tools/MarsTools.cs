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
    /// One of the hand tools (models from Task_assets). Subclasses say what clicking does
    /// for the current target (<see cref="Plan"/>), an optional second action on R, and an
    /// optional live readout. The click plays the tool's motion, then runs a mission command.
    /// </summary>
    public abstract class MarsTool
    {
        /// <summary>The hand tools (the shed's three); the hotbar fills in the order you pick things up.</summary>
        public static readonly string[] Order = { "regolith_shovel", "hydro_sprayer", "work_lamp" };

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
            // The hand tools, then one hotbar item per crop (in your hand while you have one harvested).
            var all = new System.Collections.Generic.List<MarsTool> { new RegolithShovel(), new HydroSprayer(), new WorkLamp() };
            for (int i = 0; i < SeedKind.All.Length; i++) all.Add(new CropItem(i));
            return all.ToArray();
        }

        /// <summary>Key that selects a tool or crop now: its place in the hotbar ("1", "2", ...), or "?".</summary>
        public static string KeyOf(string id)
        {
            MarsToolBelt belt = MarsExpedition.Instance == null ? null : MarsExpedition.Instance.Belt;
            int slot = belt == null ? -1 : belt.SlotOf(id);
            return slot < 0 ? "?" : (slot + 1).ToString();
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

        protected static MarsTaskTarget Aimed(ToolTarget target, MarsTaskTarget.Kind kind)
        {
            MarsTaskTarget t = target == null ? null : target.Get<MarsTaskTarget>();
            return t != null && t.kind == kind ? t : null;
        }

        /// <summary>Common refusals: no mission, or not in the greenhouse.</summary>
        protected static ToolAction Unavailable()
        {
            if (S == null || !M.IsRunning) return ToolAction.No("Start a mission first");
            if (M.Context.station != "greenhouse") return ToolAction.No("Use it on the greenhouse beds");
            return null;
        }

        /// <summary>Plays the tool, then runs a mission command.</summary>
        protected static ToolAction Command(string label, float seconds, string command, string targetId)
        {
            return ToolAction.Do(label, seconds, command + "/" + targetId, () => M.Execute(command, targetId));
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

    public sealed class RegolithShovel : MarsTool
    {
        private int seed;

        // The blade is at the -Y end, the D-handle at +Y.
        public RegolithShovel() : base("regolith_shovel", "Shovel", "Sow a seed in an empty greenhouse plot. R picks which seed.",
            ToolPose.Pole, Vector3.down, Vector3.forward, new Vector3(0f, 0.85f, 0f)) { } // held by the D-handle

        public override ToolMotion Motion { get { return ToolMotion.Dig; } }

        public override ToolAction Plan(ToolTarget target)
        {
            ToolAction busy = Unavailable();
            if (busy != null) return busy;
            MarsTaskTarget spot = Aimed(target, MarsTaskTarget.Kind.Plant);
            if (spot == null) return ToolAction.No("Aim at an empty plot in a bed");
            if (!Sim.Unlocked(spot.index)) return ToolAction.No("Locked plot. Buy more plots at the seed shop");
            PlotState plot = Sim.Plot(spot.index);
            if (plot.Ripe) return ToolAction.No("Ripe! Pick it with E");
            if (plot.seed != "") return ToolAction.No("Growing. Water it with the Sprayer");
            // Out of the chosen seed: fall back to the next kind you have.
            for (int i = 0; i < SeedKind.All.Length && S.seeds[seed] <= 0; i++) seed = (seed + 1) % SeedKind.All.Length;
            SeedKind kind = SeedKind.All[seed];
            if (S.seeds[seed] <= 0) return ToolAction.No("No seeds. Buy some at the seed shop");
            string id = spot.index.ToString(CultureInfo.InvariantCulture);
            return ToolAction.Do("sow " + kind.name.ToLowerInvariant(), 0.45f, "sow/" + id, () =>
            {
                Belt.Debris(Belt.Target, Paint.Terracotta, 8);
                M.Execute("plant", kind.id + ":" + id);
            });
        }

        public override string SecondaryLabel(ToolTarget target) { return "next seed"; }
        public override void Secondary(ToolTarget target) { seed = (seed + 1) % SeedKind.All.Length; MarsSfx.Play(MarsSfx.Sound.Click); }

        public override string Readout(ToolTarget target)
        {
            if (S == null) return null;
            string text = "";
            for (int i = 0; i < SeedKind.All.Length; i++)
                text += (i == seed ? "> " : "   ") + SeedKind.All[i].name + " seeds: " + S.seeds[i] + "\n";
            return text.TrimEnd();
        }
    }

    public sealed class HydroSprayer : MarsTool
    {
        // Watering can: the spout points along OBJ +X (Unity -X).
        public HydroSprayer() : base("hydro_sprayer", "Sprayer", "Water a sown plot: it grows until ripe (lettuce 30 s, tomato 1 min, watermelon 2 min).",
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
            MarsTaskTarget spot = Aimed(target, MarsTaskTarget.Kind.Plant);
            if (spot == null) return ToolAction.No("Aim at a growing plot");
            PlotState plot = Sim.Plot(spot.index);
            if (plot == null || plot.seed == "") return ToolAction.No("Sow it with the Shovel first");
            if (plot.Ripe) return ToolAction.No("Ripe! Pick it with E");
            if (plot.wetUntil - S.elapsedSeconds > MissionSettings.SolSeconds * 0.5) return ToolAction.No("Moist enough for now");
            return Command("water this plot", 0.8f, "water", spot.index.ToString(CultureInfo.InvariantCulture));
        }

        public override string Readout(ToolTarget target)
        {
            MarsTaskTarget spot = Aimed(target, MarsTaskTarget.Kind.Plant);
            PlotState plot = spot == null || Sim == null ? null : Sim.Plot(spot.index);
            if (plot == null || plot.seed == "") return null;
            bool wet = plot.wetUntil > S.elapsedSeconds;
            return SeedKind.Find(plot.seed).name + " " + Mathf.RoundToInt((float)plot.growth * 100f) + "%" + (plot.Ripe ? ", ripe" : wet ? ", moist" : ", DRY");
        }
    }

    /// <summary>
    /// A harvested crop in hand (the Garden "&lt;Crop&gt;Item" model): click to eat one. It sits in the
    /// hotbar while the basket holds any of that crop.
    /// </summary>
    public sealed class CropItem : MarsTool
    {
        public readonly int Kind;

        public CropItem(int kind) : base("crop_" + SeedKind.All[kind].id, SeedKind.All[kind].name,
            "Click to eat one: +" + SeedKind.All[kind].food + " food. Sell the rest at the seed shop.",
            ToolPose.Upright, Vector3.forward, Vector3.up, Vector3.zero)
        {
            Kind = kind;
        }

        public SeedKind Seed { get { return SeedKind.All[Kind]; } }

        public override ToolAction Plan(ToolTarget target)
        {
            if (S == null || !M.IsRunning) return ToolAction.No("Start a mission first");
            if (S.astronaut.food > 95) return ToolAction.No("You are not hungry");
            return Command("eat the " + Seed.name.ToLowerInvariant(), 0.6f, "eat", Seed.id);
        }

        public override string Readout(ToolTarget target) { return S == null ? null : Seed.name + " x" + S.basket[Kind]; }
    }
}
