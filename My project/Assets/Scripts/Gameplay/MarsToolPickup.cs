using System;
using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using MarsSurvival.UI;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>
    /// Fills the tool hotbar from the outpost: look at a hand tool on its rack and press E to
    /// put it in its hotbar slot (keys 1-3). Q puts the selected tool back and B returns every
    /// tool, both inside the base. The carried set is stored in the mission save. E also uses
    /// the food locker, water dispenser, bunk, ripe plants and the seed shop.
    /// </summary>
    public sealed class MarsToolPickup : MonoBehaviour
    {
        public float reachMeters = 2.4f;
        [Tooltip("How far E reaches for stations and plants (picking up tools uses reachMeters).")]
        public float useReachMeters = 4f;
        public float aimDegrees = 18f;

        private MissionState syncedState;
        private MarsAstronautPlayer player;

        /// <summary>The tool rack item under the crosshair that E would pick up, or null.</summary>
        public MarsBaseTool Aimed { get; private set; }
        /// <summary>The food locker, water dispenser, bunk, shop or ripe plant E would use, or null.</summary>
        public MarsTaskTarget AimedUse { get; private set; }
        /// <summary>What E does to <see cref="AimedUse"/> ("eat", "sleep until morning"...), or null.</summary>
        public string UseLabel { get; private set; }

        // ------------------------------------------------------------------ requirements

        /// <summary>Hand tools (hotbar ids) a mission command needs.</summary>
        public static string[] ToolsFor(string command)
        {
            switch (command)
            {
                case "water": return new[] { "hydro_sprayer" };
                case "plant": return new[] { "regolith_shovel" };
                default: return new string[0];
            }
        }

        /// <summary>True for the hand tools that live in the hotbar (the other rack items are fixed equipment).</summary>
        public static bool IsHandTool(string toolId) { return Array.IndexOf(MarsTool.Order, toolId) >= 0; }

        public bool Has(string toolId)
        {
            MissionState s = State;
            if (s == null || s.carriedTools == null) return false;
            string prefix = "/" + toolId + "#";
            foreach (string key in s.carriedTools) if (key.Contains(prefix)) return true;
            return false;
        }

        /// <summary>False, with a hint naming what to fetch and where, when a tool is missing.</summary>
        public bool HasAll(string[] tools, out string reason)
        {
            var missing = new List<string>();
            foreach (string id in tools) if (!Has(id)) missing.Add(id);
            reason = "";
            if (missing.Count == 0) return true;
            var parts = new List<string>();
            foreach (string id in missing) parts.Add(MarsBaseTool.NameOf(id) + " (" + MarsBaseTool.WhereIs(id) + ")");
            reason = "You need: " + string.Join(", ", parts.ToArray()) + ". Look at it on the tool shed and press E to add it to your hotbar.";
            return false;
        }

        public string CarriedSummary()
        {
            MissionState s = State;
            if (s == null || s.carriedTools == null || s.carriedTools.Count == 0) return "nothing";
            var names = new List<string>();
            foreach (string id in CarriedIds())
            {
                string name = MarsBaseTool.NameOf(id) + " (" + MarsTool.KeyOf(id) + ")";
                if (!names.Contains(name)) names.Add(name);
            }
            return string.Join(", ", names.ToArray());
        }

        /// <summary>Selects the tool's hotbar slot, for example the tool a console task uses.</summary>
        public void Hold(string toolId)
        {
            MarsToolBelt belt = MarsExpedition.Instance == null ? null : MarsExpedition.Instance.Belt;
            int index = Array.IndexOf(MarsTool.Order, toolId);
            if (belt != null && index >= 0 && Has(toolId) && !(belt.Selected == index && belt.Equipped)) belt.Select(index);
        }

        // ------------------------------------------------------------------ pickup

        private static MissionState State
        {
            get
            {
                var m = MarsMissionController.Instance;
                return m == null || m.Simulation == null ? null : m.Simulation.State;
            }
        }

        private void Update()
        {
            MissionState state = State;
            if (state != null && state.carriedTools == null) state.carriedTools = new List<string>();
            if (state != syncedState) Sync(state);

            Aimed = null;
            AimedUse = null;
            UseLabel = null;
            var mission = MarsMissionController.Instance;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (state == null || mission.InputBlocked || MarsHomeScreen.IsOpen || player == null || !player.IsReady || player.ViewCamera == null) return;

            Aimed = FindAimed(player.AimTransform);
            string label = null;
            if (Aimed == null) AimedUse = FindUse(player.AimTransform, state, out label);
            UseLabel = AimedUse == null ? null : label;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.E) && Aimed != null) mission.Notice = TryPickUp(Aimed);
            else if (Input.GetKeyDown(KeyCode.E) && AimedUse != null) Use(AimedUse, mission);
            bool inside = mission.Context.zone != PlayerZone.Outdoors;
            if (Input.GetKeyDown(KeyCode.Q) && state.carriedTools.Count > 0)
                mission.Notice = inside ? PutBackSelected() : "Put tools back inside the base.";
            if (Input.GetKeyDown(KeyCode.B) && state.carriedTools.Count > 0)
                mission.Notice = inside ? PutAllBack() : "Put tools back inside the base.";
#endif
        }

        /// <summary>The usable thing nearest the centre of view within reach, and what E does to it.</summary>
        public MarsTaskTarget FindUse(Transform eye, MissionState state, out string label)
        {
            label = null;
            MarsTaskTarget found = null;
            float best = aimDegrees;
            foreach (MarsTaskTarget t in MarsTaskTarget.Usables)
            {
                if (t == null) continue;
                // Aim at the body of the thing (its collider), not its pivot on the floor.
                Collider body = t.GetComponent<Collider>();
                Vector3 to = (body != null ? body.bounds.center : t.transform.position) - eye.position;
                if (to.magnitude > useReachMeters) continue;
                string what = UseLabelFor(t, state);
                if (what == null) continue;
                float angle = Vector3.Angle(eye.forward, to);
                if (angle < best) { best = angle; found = t; label = what; }
            }
            return found;
        }

        private static string UseLabelFor(MarsTaskTarget t, MissionState state)
        {
            switch (t.kind)
            {
                case MarsTaskTarget.Kind.Food: return "eat a crop";
                case MarsTaskTarget.Kind.Water: return "drink";
                case MarsTaskTarget.Kind.Garage: return MarsWorldTasks.Instance != null && MarsWorldTasks.Instance.GarageOpen ? "close the garage door" : "open the garage door";
                case MarsTaskTarget.Kind.Bunk: return "sleep until 07:00";
                case MarsTaskTarget.Kind.Seat: return UnityEngine.Object.FindObjectOfType<MarsSurvival.Interaction.MarsAstronautPlayer>().IsSeated ? "stand up" : "sit down";
                case MarsTaskTarget.Kind.Shop: return "open the seed shop";
                case MarsTaskTarget.Kind.Shed:
                    string bayTool = MarsTool.Order[t.index];
                    return MarsMissionController.Instance.Tools.Has(bayTool) ? "put the " + MarsBaseTool.NameOf(bayTool) + " back" : null;
                case MarsTaskTarget.Kind.Plant:
                    var sim = MarsMissionController.Instance.Simulation;
                    PlotState plot = sim.Plot(t.index);
                    return plot != null && plot.Ripe ? "pick this " + SeedKind.Find(plot.seed).name.ToLowerInvariant() : null;
                default: return null;
            }
        }

        private static void Use(MarsTaskTarget t, MarsMissionController mission)
        {
            switch (t.kind)
            {
                case MarsTaskTarget.Kind.Food: mission.Execute("eat"); break;
                case MarsTaskTarget.Kind.Water: mission.Execute("drink"); break;
                case MarsTaskTarget.Kind.Garage: MarsWorldTasks.Instance.ToggleGarage(); MarsSfx.Play(MarsSfx.Sound.Select); break;
                case MarsTaskTarget.Kind.Bunk: mission.Sleep(); break;
                case MarsTaskTarget.Kind.Seat: UnityEngine.Object.FindObjectOfType<MarsSurvival.Interaction.MarsAstronautPlayer>().Sit(); break;
                case MarsTaskTarget.Kind.Shop: mission.SetShopOpen(true); break;
                case MarsTaskTarget.Kind.Shed: mission.Notice = mission.Tools.PutBack(MarsTool.Order[t.index]); MarsSfx.Play(MarsSfx.Sound.Select); break;
                case MarsTaskTarget.Kind.Plant:
                    mission.Execute("pick", t.index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (MarsExpedition.Instance != null) MarsExpedition.Instance.Pop("+1 crop", t.transform.position + Vector3.up * 0.5f);
                    MarsSfx.Play(MarsSfx.Sound.Select);
                    break;
            }
        }

        /// <summary>The closest hand tool roughly in the centre of view and within reach, or null.</summary>
        public MarsBaseTool FindAimed(Transform eye)
        {
            // A tool the view ray passes through wins (nearest first); otherwise the one
            // closest to the centre of view, so small tools are still easy to pick.
            MarsBaseTool found = null, hit = null;
            float best = aimDegrees, hitDistance = float.MaxValue;
            var ray = new Ray(eye.position, eye.forward);
            foreach (MarsBaseTool tool in MarsBaseTool.All)
            {
                if (tool == null || tool.Carried || !IsHandTool(tool.toolId)) continue;
                Vector3 to = tool.Centre - eye.position;
                if (to.magnitude > reachMeters) continue;
                Bounds b = tool.WorldBounds;
                b.Expand(0.06f);
                float distance;
                if (b.IntersectRay(ray, out distance) && distance < hitDistance) { hitDistance = distance; hit = tool; }
                float angle = Vector3.Angle(eye.forward, to);
                if (angle < best) { best = angle; found = tool; }
            }
            return hit != null ? hit : found;
        }

        /// <summary>Takes one tool off its rack into its hotbar slot. Returns a message for the player.</summary>
        public string TryPickUp(MarsBaseTool tool)
        {
            MissionState state = State;
            if (state == null || tool == null || tool.Carried || !IsHandTool(tool.toolId)) return "";
            if (Has(tool.toolId)) { Hold(tool.toolId); return "The " + tool.DisplayName + " is already in hotbar slot " + MarsTool.KeyOf(tool.toolId) + "."; }
            tool.SetCarried(true);
            state.carriedTools.Add(tool.key);
            Hold(tool.toolId);
            MarsSfx.Play(MarsSfx.Sound.Select);
            return "Picked up the " + tool.DisplayName + " (slot " + MarsTool.KeyOf(tool.toolId) + "). " + UseOf(tool.toolId);
        }

        public string PutAllBack()
        {
            MissionState state = State;
            if (state == null) return "";
            state.carriedTools.Clear();
            Sync(state);
            return "Put your tools back on the shed.";
        }

        public string PutBackSelected()
        {
            MissionState state = State;
            MarsToolBelt belt = MarsExpedition.Instance == null ? null : MarsExpedition.Instance.Belt;
            if (state == null || belt == null) return "";
            if (!belt.Equipped) return "Nothing in your hand.";
            return PutBack(belt.Current.Id);
        }

        /// <summary>Returns one tool to its bay on the shed.</summary>
        public string PutBack(string id)
        {
            MissionState state = State;
            if (state == null) return "";
            int removed = state.carriedTools.RemoveAll(key => key.Contains("/" + id + "#"));
            if (removed == 0) return "You aren't carrying the " + MarsBaseTool.NameOf(id) + ".";
            Sync(state);
            return "Put the " + MarsBaseTool.NameOf(id) + " back on the shed.";
        }

        /// <summary>Carried hand tool ids in pickup order.</summary>
        public List<string> CarriedIds()
        {
            var ids = new List<string>();
            MissionState s = State;
            if (s == null || s.carriedTools == null) return ids;
            foreach (string key in s.carriedTools)
            {
                int a = key.IndexOf('/'), b = key.LastIndexOf('#');
                string id = a >= 0 && b > a ? key.Substring(a + 1, b - a - 1) : key;
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        private void Sync(MissionState state)
        {
            syncedState = state;
            foreach (MarsBaseTool tool in MarsBaseTool.All)
                if (tool != null) tool.SetCarried(state != null && state.carriedTools != null && state.carriedTools.Contains(tool.key));
        }

        public static string UseOf(string id)
        {
            switch (id)
            {
                case "work_lamp": return "Click to switch the light on or off.";
                case "regolith_shovel": return "Sow seeds in empty plots. R picks the seed.";
                case "hydro_sprayer": return "Water growing plots; they grow while the soil is moist.";
                default: return "Fixed outpost equipment.";
            }
        }
    }
}
