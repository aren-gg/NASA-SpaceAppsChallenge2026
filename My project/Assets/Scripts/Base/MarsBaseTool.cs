using System.Collections.Generic;
using UnityEngine;

namespace MarsSurvival.Base
{
    /// <summary>
    /// One tool model standing in the outpost. The astronaut can pick it up; while carried
    /// the model is hidden here. <see cref="key"/> is unique per copy and stable between
    /// runs, so a save can record which copies are being carried.
    /// </summary>
    public sealed class MarsBaseTool : MonoBehaviour
    {
        public string toolId = "";
        public string key = "";

        private static readonly List<MarsBaseTool> all = new List<MarsBaseTool>();
        public static IReadOnlyList<MarsBaseTool> All { get { return all; } }

        private Renderer[] renderers;
        private Vector3 localCentre;

        public bool Carried { get; private set; }

        public string DisplayName { get { return NameOf(toolId); } }

        private bool centreKnown;

        /// <summary>World centre of the model, for aiming at it.</summary>
        public Vector3 Centre
        {
            get
            {
                // Measured on first use, once the outpost has placed and scaled the model.
                if (!centreKnown && !Carried && renderers != null && renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    localCentre = transform.InverseTransformPoint(bounds.center);
                    centreKnown = true;
                }
                return transform.TransformPoint(localCentre);
            }
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>World bounds of the model (empty when it has no renderers).</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (renderers == null || renderers.Length == 0) return new Bounds(transform.position, Vector3.zero);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }
        }

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDestroy() { all.Remove(this); }

        public void SetCarried(bool carried)
        {
            Carried = carried;
            if (renderers == null) Awake();
            foreach (Renderer r in renderers) if (r != null) r.enabled = !carried;
        }

        public static string NameOf(string id)
        {
            switch (id)
            {
                case "comm_radio": return "Comm Radio";
                case "dosimeter": return "Radiation Dosimeter";
                case "oxygen_canister": return "Portable O2 Canister";
                case "work_lamp": return "Habitat Work Lamp";
                case "multimeter": return "Energy Meter";
                case "power_wrench": return "Field Wrench";
                case "panel_brush": return "Solar Panel Duster";
                case "hydro_sprayer": return "Hydroponic Sprayer";
                case "regolith_shovel": return "Regolith Shovel";
                case "rock_hammer": return "Rock Hammer";
                case "core_drill": return "Core Sample Drill";
                case "sample_scoop": return "Sample Scoop";
                case "sample_canisters": return "Sample Canister Rack";
                case "survey_camera": return "Survey Camera";
                case "survey_flag": return "Survey Marker";
                default: return id;
            }
        }

        /// <summary>Where each tool is kept, for "fetch it from…" hints.</summary>
        public static string WhereIs(string id)
        {
            switch (id)
            {
                case "comm_radio": return "command hub, storm shelter or lab";
                case "dosimeter": return "command hub, medical bay or storm shelter";
                case "oxygen_canister": return "life support, medical bay or lab";
                case "work_lamp": return "crew quarters or storm shelter";
                case "multimeter":
                case "power_wrench":
                case "panel_brush": return "energy room";
                case "hydro_sprayer":
                case "regolith_shovel": return "greenhouse";
                default: return "EVA prep and sample lab";
            }
        }
    }
}
