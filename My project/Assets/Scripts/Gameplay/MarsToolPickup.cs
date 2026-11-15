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
    /// Fills the tool hotbar from the outpost: look at one of the 10 hand tools on its rack and
    /// press E to put it in its hotbar slot (keys 1-9, 0). Q puts the selected tool back and
    /// B returns every tool, both inside the base. The carried set is stored in the mission
    /// save. Also says which tool each daily task needs.
    /// </summary>
    public sealed class MarsToolPickup : MonoBehaviour
    {
        public float reachMeters = 2.4f;
        public float aimDegrees = 18f;

        private MissionState syncedState;
        private MarsAstronautPlayer player;

        /// <summary>The tool rack item under the crosshair that E would pick up, or null.</summary>
        public MarsBaseTool Aimed { get; private set; }

        // ------------------------------------------------------------------ requirements

        /// <summary>Hand tools (hotbar ids) a mission task needs.</summary>
        public static string[] ToolsFor(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.Plant: return new[] { "regolith_shovel" };
                case TaskKind.Harvest: return new[] { "hydro_sprayer" };
                case TaskKind.RepairBase:
                case TaskKind.RepairRover:
                case TaskKind.Install: return new[] { "power_wrench" };
                case TaskKind.DustPanels: return new[] { "panel_brush" };
                case TaskKind.SampleSoil:
                case TaskKind.Analyze: return new[] { "core_drill" };
                case TaskKind.PlaceMarker: return new[] { "rock_hammer" };
                case TaskKind.Photograph: return new[] { "survey_camera" };
                case TaskKind.Transmit: return new[] { "comm_radio" };
                default: return new string[0];
            }
        }

        public static string[] ToolsFor(string command, string target)
        {
            switch (command)
            {
                case "survey": return new[] { "rock_hammer" };
                case "canister_fill":
                case "canister_use": return new[] { "oxygen_canister" };
                case "water_crop": return new[] { "hydro_sprayer" };
                case "radio": return target == "off" ? new string[0] : new[] { "comm_radio" };
                default: return new string[0];
            }
        }

        /// <summary>True for the 10 hand tools that live in the hotbar (the other rack items are fixed equipment).</summary>
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
            reason = "You need: " + string.Join(", ", parts.ToArray()) + ". Look at it on its rack and press E to add it to your hotbar.";
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
            if (belt != null && index >= 0 && Has(toolId)) belt.Select(index);
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
            var mission = MarsMissionController.Instance;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (state == null || mission.InputBlocked || MarsHomeScreen.IsOpen || player == null || !player.IsReady || player.IsDriving || player.ViewCamera == null) return;

            Aimed = FindAimed(player.ViewCamera.transform);
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.E) && Aimed != null) mission.Notice = TryPickUp(Aimed);
            bool inside = mission.Context.zone != PlayerZone.Outdoors;
            if (Input.GetKeyDown(KeyCode.Q) && state.carriedTools.Count > 0)
                mission.Notice = inside ? PutBackSelected() : "Put tools back inside the base.";
            if (Input.GetKeyDown(KeyCode.B) && state.carriedTools.Count > 0)
                mission.Notice = inside ? PutAllBack() : "Put tools back inside the base.";
#endif
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
            return "Returned all tools to their racks.";
        }

        public string PutBackSelected()
        {
            MissionState state = State;
            MarsToolBelt belt = MarsExpedition.Instance == null ? null : MarsExpedition.Instance.Belt;
            if (state == null || belt == null) return "";
            string id = belt.Current.Id;
            int removed = state.carriedTools.RemoveAll(key => key.Contains("/" + id + "#"));
            if (removed == 0) return "That hotbar slot is empty.";
            Sync(state);
            return "Put the " + MarsBaseTool.NameOf(id) + " back on its rack.";
        }

        private List<string> CarriedIds()
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
                case "rock_hammer": return "Survey a science site and hammer in its marker.";
                case "core_drill": return "Drill a core at a site; analyze it at the lab bench.";
                case "survey_camera": return "Photograph a site's survey marker. R zooms.";
                case "comm_radio": return "Queue and transmit Houston reports. R finds the nearest site.";
                case "oxygen_canister": return "Top up your suit on EVA; refill it inside.";
                case "work_lamp": return "Click to switch the light on or off.";
                case "panel_brush": return "Dust the solar array outside the energy room.";
                case "power_wrench": return "Repair the base and rover; install upgrades in the energy room.";
                case "regolith_shovel": return "Plant a greenhouse bed. R picks the crop.";
                case "hydro_sprayer": return "Harvest ready beds or water a stressed one.";
                default: return "Fixed outpost equipment.";
            }
        }
    }
}
