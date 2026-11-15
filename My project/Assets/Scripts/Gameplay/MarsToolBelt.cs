using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.UI;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>
    /// Lets the astronaut pick up the tools standing in the outpost (look at one and press E;
    /// B inside puts everything back) and says which tools each daily task needs.
    /// The carried set is stored in the mission save.
    /// </summary>
    public sealed class MarsToolBelt : MonoBehaviour
    {
        public float reachMeters = 2.4f;
        public float aimDegrees = 18f;

        private MissionState syncedState;
        private MarsBaseTool aimed;
        private GUIStyle promptStyle, listStyle;
        private Light lamp;

        // ------------------------------------------------------------------ requirements

        public static string[] ToolsFor(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.Plant: return new[] { "regolith_shovel", "hydro_sprayer" };
                case TaskKind.Harvest: return new[] { "hydro_sprayer" };
                case TaskKind.Treat: return new[] { "dosimeter" };
                case TaskKind.RepairBase: return new[] { "power_wrench", "multimeter" };
                case TaskKind.RepairRover: return new[] { "power_wrench" };
                case TaskKind.DustPanels: return new[] { "panel_brush" };
                case TaskKind.Install: return new[] { "power_wrench", "multimeter" };
                case TaskKind.SampleSoil: return new[] { "sample_scoop", "sample_canisters" };
                case TaskKind.Analyze: return new[] { "core_drill" };
                case TaskKind.PlaceMarker: return new[] { "survey_flag" };
                case TaskKind.Photograph: return new[] { "survey_camera" };
                default: return new string[0];
            }
        }

        public static string[] ToolsFor(string command, string target)
        {
            switch (command)
            {
                case "refill": return new[] { "oxygen_canister" };
                case "survey": return new[] { "rock_hammer" };
                case "measure": return new[] { "dosimeter" };
                case "radio": return target == "off" ? new string[0] : new[] { "comm_radio" };
                default: return new string[0];
            }
        }

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
            reason = "You need: " + string.Join(", ", parts.ToArray()) + ". Look at a tool and press E to pick it up.";
            return false;
        }

        public string CarriedSummary()
        {
            MissionState s = State;
            if (s == null || s.carriedTools == null || s.carriedTools.Count == 0) return "nothing";
            var names = new List<string>();
            foreach (string key in s.carriedTools)
            {
                int a = key.IndexOf('/'), b = key.LastIndexOf('#');
                string id = a >= 0 && b > a ? key.Substring(a + 1, b - a - 1) : key;
                string name = MarsBaseTool.NameOf(id);
                if (!names.Contains(name)) names.Add(name);
            }
            return string.Join(", ", names.ToArray());
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

            aimed = null;
            var mission = MarsMissionController.Instance;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            UpdateLamp(player);
            UpdateHeld(player);
            if (state == null || mission.InputBlocked || MarsHomeScreen.IsOpen || player == null || !player.IsReady || player.IsDriving || player.ViewCamera == null) return;

            aimed = FindAimed(player.ViewCamera.transform);
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.E) && aimed != null) mission.Notice = TryPickUp(aimed);
            if (Input.GetKeyDown(KeyCode.Q)) CycleHeld();
            if (Input.GetKeyDown(KeyCode.B) && state.carriedTools.Count > 0)
                mission.Notice = mission.Context.zone == PlayerZone.Outdoors ? "Put tools back inside the base." : PutAllBack();
#endif
        }

        private MarsAstronautPlayer player;

        /// <summary>The closest tool roughly in the centre of view and within reach, or null.</summary>
        public MarsBaseTool FindAimed(Transform eye)
        {
            // A tool the view ray passes through wins (nearest first); otherwise the one
            // closest to the centre of view, so small tools are still easy to pick.
            MarsBaseTool found = null, hit = null;
            float best = aimDegrees, hitDistance = float.MaxValue;
            var ray = new Ray(eye.position, eye.forward);
            foreach (MarsBaseTool tool in MarsBaseTool.All)
            {
                if (tool == null || tool.Carried) continue;
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

        /// <summary>Takes one tool off its shelf into the astronaut's hands. Returns a message for the player.</summary>
        public string TryPickUp(MarsBaseTool tool)
        {
            MissionState state = State;
            if (state == null || tool == null || tool.Carried) return "";
            if (Has(tool.toolId)) { HeldId = tool.toolId; return "You already carry a " + tool.DisplayName + "; it is now in your hands."; }
            tool.SetCarried(true);
            state.carriedTools.Add(tool.key);
            HeldId = tool.toolId;
            return "Picked up the " + tool.DisplayName + ". " + UseOf(tool.toolId) + (state.carriedTools.Count > 1 ? " [Q] switches the tool in your hands." : "");
        }

        public string PutAllBack()
        {
            MissionState state = State;
            if (state == null) return "";
            state.carriedTools.Clear();
            Sync(state);
            return "Returned all tools to their places.";
        }

        // ------------------------------------------------------------------ tool in hand

        /// <summary>The tool shown in the astronaut's hands (one of the carried tools), or "".</summary>
        public string HeldId { get; private set; } = "";
        /// <summary>The model currently shown in front of the camera, for checks.</summary>
        public GameObject HeldModel { get { return held; } }

        private GameObject held;
        private string heldShown = "";
        private Transform heldCamera;

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

        public void CycleHeld()
        {
            List<string> ids = CarriedIds();
            if (ids.Count == 0) return;
            int i = ids.IndexOf(HeldId);
            HeldId = ids[(i + 1) % ids.Count];
        }

        /// <summary>Selects which carried tool is in the astronaut's hands, for example the one a task uses.</summary>
        public void Hold(string toolId) { if (Has(toolId)) HeldId = toolId; }

        private void UpdateHeld(MarsAstronautPlayer astronaut)
        {
            List<string> ids = CarriedIds();
            if (!ids.Contains(HeldId)) HeldId = ids.Count > 0 ? ids[ids.Count - 1] : "";
            Transform cam = astronaut != null && astronaut.ViewCamera != null ? astronaut.ViewCamera.transform : null;
            bool show = HeldId != "" && cam != null && !astronaut.IsDriving;
            if (!show || HeldId != heldShown || cam != heldCamera)
            {
                if (held != null) Destroy(held);
                held = null;
                heldShown = "";
                heldCamera = null;
            }
            if (!show || held != null) return;
            MarsBaseTool source = null;
            foreach (MarsBaseTool tool in MarsBaseTool.All) if (tool != null && tool.toolId == HeldId) { source = tool; break; }
            if (source == null) return;
            held = BuildHeldModel(source, cam);
            heldShown = HeldId;
            heldCamera = cam;
        }

        /// <summary>
        /// A copy of the tool's model in the lower right of the view, as if held in the right hand:
        /// scaled to about 35 cm, without colliders or shadows.
        /// </summary>
        private static GameObject BuildHeldModel(MarsBaseTool source, Transform cam)
        {
            GameObject copy = Instantiate(source.gameObject);
            DestroyImmediate(copy.GetComponent<MarsBaseTool>());
            copy.name = "Held " + source.toolId;
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            copy.transform.SetParent(cam, false);
            copy.transform.localScale = Vector3.one;
            copy.transform.localRotation = Quaternion.Euler(-12f, -35f, 8f);
            copy.transform.localPosition = Vector3.zero;

            Renderer[] renderers = copy.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return copy;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float lossy = cam.lossyScale.x;
            float scale = size > 1e-4f ? Mathf.Min(1f, 0.26f * lossy / size) : 1f;
            copy.transform.localScale = Vector3.one * scale / Mathf.Max(1e-4f, lossy);
            // Put the model's centre at the hand position, lower right and 60 cm in front of the eye.
            Vector3 centreLocal = cam.InverseTransformPoint(b.center) * scale;
            copy.transform.localPosition = new Vector3(0.2f, -0.19f, 0.6f) - centreLocal;
            return copy;
        }

        private void Sync(MissionState state)
        {
            syncedState = state;
            foreach (MarsBaseTool tool in MarsBaseTool.All)
                if (tool != null) tool.SetCarried(state != null && state.carriedTools != null && state.carriedTools.Contains(tool.key));
        }

        private void UpdateLamp(MarsAstronautPlayer player)
        {
            bool on = player != null && player.ViewCamera != null && Has("work_lamp");
            if (on && lamp == null)
            {
                lamp = new GameObject("Carried work lamp").AddComponent<Light>();
                lamp.type = LightType.Spot;
                lamp.range = 25f;
                lamp.spotAngle = 70f;
                lamp.intensity = 3f;
                lamp.color = new Color(1f, 0.93f, 0.82f);
                lamp.shadows = LightShadows.None;
            }
            if (lamp == null) return;
            lamp.enabled = on;
            if (on)
            {
                Transform eye = player.ViewCamera.transform;
                lamp.transform.SetPositionAndRotation(eye.position + eye.right * 0.2f, eye.rotation);
            }
        }

        public static string UseOf(string id)
        {
            switch (id)
            {
                case "regolith_shovel": return "Needed to plant a greenhouse bed.";
                case "hydro_sprayer": return "Needed to plant and to harvest crops.";
                case "dosimeter": return "Needed to measure a site and for medical treatment.";
                case "power_wrench": return "Needed for repairs and installing upgrades.";
                case "multimeter": return "Needed to repair the base and install upgrades.";
                case "panel_brush": return "Needed to dust the solar panels.";
                case "sample_scoop":
                case "sample_canisters": return "Scoop and canisters together collect a soil sample.";
                case "core_drill": return "Needed to analyze a sample at the lab bench.";
                case "rock_hammer": return "Needed to survey a science site.";
                case "survey_flag": return "Needed to place a survey marker.";
                case "survey_camera": return "Needed to photograph a marker.";
                case "comm_radio": return "Needed to listen or transmit to Houston.";
                case "oxygen_canister": return "Needed to refill your suit oxygen.";
                case "work_lamp": return "Lights your way while carried.";
                default: return "";
            }
        }

        private void OnGUI()
        {
            MissionState state = State;
            var mission = MarsMissionController.Instance;
            if (state == null || mission == null || mission.PanelOpen || MarsHomeScreen.IsOpen) return;
            float scale = Screen.height / 1080f;
            if (promptStyle == null)
            {
                promptStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                listStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, wordWrap = true };
            }
            promptStyle.fontSize = Mathf.RoundToInt(24 * scale);
            listStyle.fontSize = Mathf.RoundToInt(19 * scale);
            listStyle.normal.textColor = MarsPalette.PalePink;
            if (aimed != null)
            {
                string text = Has(aimed.toolId) ? aimed.DisplayName + " (already carried)" : "[E] Pick up " + aimed.DisplayName + "\n" + UseOf(aimed.toolId);
                float w = 620 * scale, h = 80 * scale;
                GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f + 40 * scale, w, h), text, promptStyle);
            }
            string carried = "Carrying: " + CarriedSummary() + (state.carriedTools.Count > 0 ? "   [B] put back (inside)" : "");
            GUI.Label(new Rect(Screen.width * 0.4f, 150 * scale, Screen.width * 0.6f - 30 * scale, 60 * scale), carried, listStyle);
        }
    }
}
