using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Plain-language description of whatever the crosshair is on (a camp machine, a crop
    /// bed, a survey site), with what it needs and which tool key to press. Shown next to
    /// the crosshair so young players always know what something is and what to do.
    /// </summary>
    public static class MarsLookInfo
    {
        public static bool Describe(ToolTarget target, MarsExpedition x, out string title, out string body)
        {
            title = body = null;
            if (target == null || !target.Hit) return false;

            MarsSolarPanel panel = target.Get<MarsSolarPanel>();
            if (panel != null)
            {
                title = panel.name;
                if (panel.Damaged) body = "Broken! Fix it with " + MarsTool.Named("power_wrench", "Field Wrench") + ".";
                else if (panel.Dust > 0.3f) body = "Dusty, so it makes less power.\nClean it with " + MarsTool.Named("panel_brush", "Panel Duster") + ".";
                else if (x.Daylight < 0.1f) body = "Clean and working.\nIt's night, so there's no sunlight to use.";
                else body = "Clean and working: " + Mathf.RoundToInt(panel.OutputWatts) + " watts from sunlight.";
                return true;
            }

            MarsBatteryCabinet battery = target.Get<MarsBatteryCabinet>();
            if (battery != null)
            {
                title = "Battery";
                body = "Charged " + Mathf.RoundToInt(battery.Fraction * 100f) + "%. The orange bars show how full it is.";
                if (battery.Damaged) body += "\nBroken! Fix it with " + MarsTool.Named("power_wrench", "Field Wrench") + ".";
                return true;
            }

            MarsCropBed bed = target.Get<MarsCropBed>();
            if (bed != null)
            {
                title = bed.name;
                if (bed.NeedsSoil)
                    body = "Needs soil (" + Mathf.RoundToInt(bed.Soil * 2f) + " of 2 loads).\nDig soil with " + MarsTool.Named("regolith_shovel", "Shovel") + ", then aim here.";
                else if (bed.Grown) body = "Ready to harvest!\nUse " + MarsTool.Named("hydro_sprayer", "Sprayer") + ".";
                else if (bed.Water <= 0f) body = "Thirsty! Water it with " + MarsTool.Named("hydro_sprayer", "Sprayer") + ".";
                else body = "Growing: " + Mathf.RoundToInt(bed.Growth * 100f) + "%  ·  Water " + Mathf.RoundToInt(bed.Water * 100f) + "%";
                return true;
            }

            if (target.Get<MarsSupplyCrate>() != null)
            {
                title = "Supply Crate";
                body = "Walk up close to hand in your samples.\nRefill " + MarsTool.Named("oxygen_canister", "Oxygen Canister") + " here.";
                return true;
            }

            MarsSurveySite site = target.Site;
            if (site != null)
            {
                title = site.DisplayName;
                body = Task("Rock", "rock_hammer", "Rock Hammer", site.RockTaken) + "\n" +
                       Task("Core", "core_drill", "Core Drill", site.CoreTaken) + "\n" +
                       Task("Photo", "survey_camera", "Survey Camera", site.Photographed);
                return true;
            }
            return false;
        }

        private static string Task(string what, string id, string tool, bool done)
        {
            return done ? what + ": done!" : what + ": use the " + tool + " (" + MarsTool.KeyOf(id) + ")";
        }
    }
}
