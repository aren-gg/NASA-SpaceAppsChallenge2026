using System;
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
        /// <summary>Survey site under the crosshair, or whose working area contains the hit point.</summary>
        public MarsSurveySite Site;

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
    /// action on R, and an optional live readout.
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
    }

    // ---------------------------------------------------------------- 1-3: science

    public sealed class RockHammer : MarsTool
    {
        // Handle along +Y with the head on top; the pick points along OBJ +X (Unity -X).
        public RockHammer() : base("rock_hammer", "Rock Hammer", "Break a rock sample off a survey site.",
            ToolPose.Upright, Vector3.left, Vector3.up, Vector3.zero) { }

        public override ToolMotion Motion { get { return ToolMotion.Swing; } }

        public override ToolAction Plan(ToolTarget target)
        {
            MarsSurveySite site = target.Get<MarsSurveySite>();
            if (site == null) return NotHere(target, "Aim at the rocks of a survey site");
            if (site.RockTaken) return ToolAction.No("You already have a rock from here");
            if (X.BagFull) return ToolAction.No("Your sample bag is full. Take it to the supply crate");
            return ToolAction.Do("break off a rock sample", 1.3f, site, () =>
            {
                site.RockTaken = true;
                X.AddSample(MarsExpedition.SampleKind.Rock, target.Point);
                Belt.Debris(target, Paint.Mauve, 10);
            });
        }
    }

    public sealed class CoreDrill : MarsTool
    {
        // Pistol drill: the bit points along OBJ +X (Unity -X), grip at the handle.
        public CoreDrill() : base("core_drill", "Core Drill", "Drill a core sample at a survey site. Cores are worth the most!",
            ToolPose.Point, Vector3.left, Vector3.up, new Vector3(0.04f, 0.04f, 0f)) { }

        public override ToolMotion Motion { get { return ToolMotion.Buzz; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Drill; } }

        public override ToolAction Plan(ToolTarget target)
        {
            MarsSurveySite site = target.Hit ? target.Site : null;
            if (site == null) return NotHere(target, "Drill at a survey site. The radio (4) can find one");
            if (site.CoreTaken) return ToolAction.No("You already drilled a core here");
            if (X.BagFull) return ToolAction.No("Your sample bag is full. Take it to the supply crate");
            return ToolAction.Do("drill a core sample", 2f, site, () =>
            {
                site.CoreTaken = true;
                X.AddSample(MarsExpedition.SampleKind.Core, target.Point);
                Belt.Debris(target, Paint.ClaySand, 14);
            });
        }
    }

    public sealed class SurveyCamera : MarsTool
    {
        // Aeolis Mons (Mount Sharp) summit, about 5.08 S 137.85 E.
        private const double SharpLatitude = -5.08;
        private const double SharpLongitude = 137.85;
        private bool sharpPhotographed;
        private bool campPhotographed;

        // Lens on +Z.
        public SurveyCamera() : base("survey_camera", "Survey Camera", "Take photos of survey sites and Mount Sharp. R zooms in.",
            ToolPose.Camera, Vector3.forward, Vector3.up, new Vector3(0.075f, 0.06f, 0f)) { } // its hand grip, on the right side

        public override ToolAction Plan(ToolTarget target)
        {
            return ToolAction.Do("take a photo", 0f, "photo", Snap);
        }

        public override string SecondaryLabel(ToolTarget target) { return Belt.Zoomed ? "zoom out" : "zoom in"; }
        public override void Secondary(ToolTarget target) { Belt.Zoomed = !Belt.Zoomed; MarsSfx.Play(MarsSfx.Sound.Click); }
        public override void OnUnequip() { Belt.Zoomed = false; }

        private void Snap()
        {
            Belt.Flash();
            MarsSfx.Play(MarsSfx.Sound.Shutter);
            Transform view = Belt.ViewCamera.transform;
            bool anything = false;

            foreach (MarsSurveySite site in MarsSurveySite.All)
            {
                if (site.Photographed) continue;
                Vector3 to = site.transform.position + Vector3.up * X.Units(0.5f) - view.position;
                float limit = Belt.Zoomed ? 25f : 14f;
                if (X.Meters(to.magnitude) > 90f || Vector3.Angle(view.forward, to) > limit) continue;
                site.Photographed = true;
                site.Discovered = true;
                X.AddResearch(2, "Great photo of " + site.DisplayName + "!", site.transform.position + Vector3.up * X.Units(1f));
                anything = true;
            }

            if (!sharpPhotographed && X.Terrain != null && X.Terrain.IsReady)
            {
                Vector3 summit = X.Terrain.LatLonToWorld(SharpLatitude, SharpLongitude);
                if (X.Terrain.IsInsideTile(summit) && Vector3.Angle(view.forward, summit - view.position) < 10f)
                {
                    sharpPhotographed = true;
                    X.AddResearch(3, "You photographed Mount Sharp, a 5 km tall mountain in the middle of Gale Crater!", null);
                    anything = true;
                }
            }

            if (!campPhotographed && X.Camp != null)
            {
                Vector3 to = X.Camp.transform.position - view.position;
                if (X.Meters(to.magnitude) > 8f && Vector3.Angle(view.forward, to) < 15f)
                {
                    campPhotographed = true;
                    X.AddResearch(1, "A photo of your camp for the mission log.", null);
                    anything = true;
                }
            }

            if (!anything) X.Say("Nice photo! Point the camera at a survey site or at Mount Sharp to earn research.");
        }
    }

    // ---------------------------------------------------------------- 4-6: survival

    public sealed class CommRadio : MarsTool
    {
        // Upright handset; its screen faces OBJ +Z, so the back (-Z) points away from you.
        public CommRadio() : base("comm_radio", "Radio", "Click to call the base. R: \"Where is the nearest survey site?\"",
            ToolPose.Upright, Vector3.back, Vector3.up, new Vector3(0f, 0.07f, 0f)) { }

        public override ToolAction Plan(ToolTarget target)
        {
            return ToolAction.Do("call the base", 0f, "radio", () =>
            {
                MarsSfx.Play(MarsSfx.Sound.Radio);
                X.Say("Base: " + WeatherLine() + "  ·  Your oxygen: " + Mathf.RoundToInt(X.SuitOxygen) + "%", MarsExpedition.Tone.Radio);
                if (X.Camp != null) X.Say("Base: battery " + Percent(X.Camp.Battery.Fraction) + " charged.", MarsExpedition.Tone.Radio);
            });
        }

        public override string SecondaryLabel(ToolTarget target) { return "find the nearest survey site"; }

        public override void Secondary(ToolTarget target)
        {
            MarsSfx.Play(MarsSfx.Sound.Radio);
            X.GuideToNearestSite(true);
        }

        public override string Readout(ToolTarget target)
        {
            string text = WeatherLine();
            if (X.Schedule != null && X.Schedule.NextSolarEvent != null)
            {
                double hours = X.Schedule.NextSolarEventMarsHours;
                text += "\nNext: " + X.Schedule.NextSolarEvent.label + " in " +
                        (hours >= 48.0 ? (hours / 24.0).ToString("F0") + " sols" : hours.ToString("F0") + " hours");
            }
            text += "\nRadiation: " + X.RadiationWord;
            return text;
        }

        private static string WeatherLine()
        {
            if (X.Schedule != null && !string.IsNullOrEmpty(X.Schedule.CurrentLabel)) return "Weather: " + X.Schedule.CurrentLabel;
            return "Weather: " + (X.Weather != null ? X.Weather.Current.ToString() : "unknown");
        }
    }

    public sealed class OxygenCanister : MarsTool
    {
        // Upright bottle; the outlet points along OBJ +X (Unity -X).
        public OxygenCanister() : base("oxygen_canister", "Oxygen Canister", "Top up your suit's oxygen when you are far from camp.",
            ToolPose.Upright, Vector3.left, Vector3.up, new Vector3(0f, 0.24f, 0f))
        {
            ViewOffset = new Vector3(0f, 0.06f, 0f);
        }

        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Spray; } }

        public override ToolAction Plan(ToolTarget target)
        {
            if (X.NearCrate)
            {
                if (X.CanisterCharges >= MarsExpedition.MaxCanisterCharges) return ToolAction.No("The canister is already full");
                return ToolAction.Do("refill the canister", 1f, "refill", () =>
                {
                    X.CanisterCharges = MarsExpedition.MaxCanisterCharges;
                    X.Say("Oxygen canister refilled.", MarsExpedition.Tone.Good);
                });
            }
            if (X.SuitOxygen >= 99f) return ToolAction.No("Your suit oxygen is already full");
            if (X.CanisterCharges <= 0) return ToolAction.No("The canister is empty. Refill it at the supply crate");
            return ToolAction.Do("breathe from the canister", 0.8f, "breathe", () =>
            {
                X.CanisterCharges--;
                X.ChangeOxygen(X.canisterRefillPercent);
                X.Say("Oxygen topped up to " + Mathf.RoundToInt(X.SuitOxygen) + "%.", MarsExpedition.Tone.Good);
            });
        }

        public override string Readout(ToolTarget target)
        {
            return "Suit oxygen: " + Mathf.RoundToInt(X.SuitOxygen) + "%\nCanister: " + X.CanisterCharges + " of " +
                   MarsExpedition.MaxCanisterCharges + " top-ups left";
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

    // ---------------------------------------------------------------- 7-0: base

    public sealed class PanelDuster : MarsTool
    {
        // Broom: the brush head is at the -Y end, the handle runs up to +Y.
        public PanelDuster() : base("panel_brush", "Panel Duster", "Sweep dust off the solar panels so they make power again.",
            ToolPose.Pole, Vector3.down, Vector3.forward, new Vector3(0f, 0.95f, 0f)) { } // held near the top of the handle

        public override ToolMotion Motion { get { return ToolMotion.Sweep; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Sweep; } }

        public override ToolAction Plan(ToolTarget target)
        {
            MarsSolarPanel panel = target.Get<MarsSolarPanel>();
            if (panel == null) return NotHere(target, "Aim at a solar panel at camp");
            if (panel.Dust < 0.05f) return ToolAction.No("This panel is already clean");
            return ToolAction.Do("sweep the dust off", 1.4f, panel, () =>
            {
                panel.Clean();
                Belt.Debris(target, Paint.ClaySand, 12);
                X.Say(panel.name + " is shiny and clean!", MarsExpedition.Tone.Good);
            });
        }
    }

    public sealed class FieldWrench : MarsTool
    {
        // Flat spanner standing up, jaws at +Y.
        public FieldWrench() : base("power_wrench", "Field Wrench", "Fix solar panels and the battery when storms break them.",
            ToolPose.Upright, Vector3.forward, Vector3.up, new Vector3(0f, -0.07f, 0f)) { }

        public override ToolMotion Motion { get { return ToolMotion.Buzz; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Ratchet; } }

        public override ToolAction Plan(ToolTarget target)
        {
            MarsSolarPanel panel = target.Get<MarsSolarPanel>();
            if (panel != null)
            {
                if (!panel.Damaged) return ToolAction.No("This panel is working fine");
                return ToolAction.Do("fix the solar panel", 1.6f, panel, () =>
                {
                    panel.Damaged = false;
                    X.Say(panel.name + " is fixed!", MarsExpedition.Tone.Good);
                });
            }
            MarsBatteryCabinet battery = target.Get<MarsBatteryCabinet>();
            if (battery != null)
            {
                if (!battery.Damaged) return ToolAction.No("The battery is working fine");
                return ToolAction.Do("fix the battery", 1.8f, battery, () =>
                {
                    battery.Damaged = false;
                    X.Say("The battery is fixed! It can hold a full charge again.", MarsExpedition.Tone.Good);
                });
            }
            return NotHere(target, "Aim at a solar panel or the battery at camp");
        }
    }

    public sealed class RegolithShovel : MarsTool
    {
        // The blade is at the -Y end, the D-handle at +Y.
        public RegolithShovel() : base("regolith_shovel", "Shovel", "Dig up Mars soil (regolith) and put it in the crop beds.",
            ToolPose.Pole, Vector3.down, Vector3.forward, new Vector3(0f, 0.85f, 0f)) { } // held by the D-handle

        public override ToolMotion Motion { get { return ToolMotion.Dig; } }

        public override ToolAction Plan(ToolTarget target)
        {
            MarsCropBed bed = target.Get<MarsCropBed>();
            if (bed != null)
            {
                if (!bed.NeedsSoil) return ToolAction.No("This bed already has enough soil");
                if (X.RegolithBags <= 0) return ToolAction.No("Dig some soil from the ground first");
                return ToolAction.Do("put soil in the bed", 0.8f, bed, () =>
                {
                    X.RegolithBags--;
                    bed.AddSoil(0.5f);
                    Belt.Debris(target, Paint.Terracotta, 10);
                    X.Say(bed.NeedsSoil ? bed.name + " needs one more load of soil."
                                        : bed.name + " is full of soil. Now water it with " + Named("hydro_sprayer", "Sprayer") + "!",
                          MarsExpedition.Tone.Good);
                });
            }
            if (!target.Hit || !target.Ground) return NotHere(target, "Aim at the ground or a crop bed");
            if (X.RegolithBags >= MarsExpedition.MaxRegolithBags)
                return ToolAction.No("Your soil bags are full. Put the soil in a crop bed at camp");
            return ToolAction.Do("dig up soil", 0.9f, "ground", () =>
            {
                X.RegolithBags++;
                Belt.Debris(target, Paint.Terracotta, 12);
                X.Say("You dug up a bag of soil (" + X.RegolithBags + "/" + MarsExpedition.MaxRegolithBags + "). Crop beds need 2.");
            });
        }

        public override string Readout(ToolTarget target)
        {
            return "Soil bags: " + X.RegolithBags + " / " + MarsExpedition.MaxRegolithBags;
        }
    }

    public sealed class HydroSprayer : MarsTool
    {
        // Watering can: the spout points along OBJ +X (Unity -X).
        public HydroSprayer() : base("hydro_sprayer", "Sprayer", "Water the crop beds so plants grow. Harvest them when they're ready!",
            ToolPose.Upright, Vector3.left, Vector3.up, new Vector3(0f, 0.16f, 0.11f))
        {
            // Carried by the side of the tank, like a watering can, so the tank stays in view.
            ViewOffset = new Vector3(0f, 0.02f, 0.02f);
        }

        public override ToolMotion Motion { get { return ToolMotion.Spray; } }
        public override MarsSfx.Loop LoopSound { get { return MarsSfx.Loop.Spray; } }

        public override ToolAction Plan(ToolTarget target)
        {
            MarsCropBed bed = target.Get<MarsCropBed>();
            if (bed == null) return NotHere(target, "Aim at a crop bed at camp");
            if (bed.NeedsSoil) return ToolAction.No("Put soil in this bed first with " + Named("regolith_shovel", "Shovel"));
            if (bed.Grown)
            {
                return ToolAction.Do("harvest the plants", 0.9f, bed, () =>
                {
                    bed.Harvest();
                    X.AddResearch(2, "You harvested fresh food from " + bed.name + "!", bed.transform.position + Vector3.up * X.Units(0.8f));
                });
            }
            if (bed.Water > 0.9f) return ToolAction.No("Already watered. The plants are growing");
            return ToolAction.Do("water the plants", 0.8f, bed, () =>
            {
                bed.WaterUp();
                X.Say(bed.name + " watered. Watch the plants grow!", MarsExpedition.Tone.Good);
            });
        }
    }
}
