using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Simulation;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Plain-language description shown next to the crosshair: the outpost tool rack item you
    /// are looking at (press E to put it in its hotbar slot), or the science site you are
    /// standing at, with which tool key does each step of its fieldwork.
    /// </summary>
    public static class MarsLookInfo
    {
        public static bool Describe(ToolTarget target, MarsExpedition x, out string title, out string body)
        {
            title = body = null;
            MarsMissionController mission = MarsMissionController.Instance;
            if (mission == null || mission.Simulation == null) return false;

            MarsBaseTool rack = mission.Tools == null ? null : mission.Tools.Aimed;
            if (rack != null)
            {
                title = rack.DisplayName;
                body = "Press E to put it in hotbar slot " + MarsTool.KeyOf(rack.toolId) + ".\n" + MarsToolPickup.UseOf(rack.toolId);
                return true;
            }

            if (mission.Tools != null && mission.Tools.AimedUse != null)
            {
                title = mission.Tools.AimedUse.name;
                body = "Press E to " + mission.Tools.UseLabel + ".";
                ResourceState r = mission.Simulation.State.resources;
                switch (mission.Tools.AimedUse.kind)
                {
                    case MarsTaskTarget.Kind.Food: body += "\nFood store: " + r.food.ToString("0") + " portions"; break;
                    case MarsTaskTarget.Kind.Water: body += "\nClean water: " + r.cleanWaterLiters.ToString("0") + " L"; break;
                    case MarsTaskTarget.Kind.Bunk: body += "\nThe night passes quickly; you wake up hungry and thirsty."; break;
                    case MarsTaskTarget.Kind.MedicalBed: body += "\nUses one medical supply (" + r.medicine.ToString("0") + " left)."; break;
                }
                return true;
            }

            SiteState site = target == null || string.IsNullOrEmpty(target.SiteId) ? null : mission.Simulation.FindSite(target.SiteId);
            if (site != null)
            {
                title = site.name;
                body = Step("Survey", "rock_hammer", site.surveyed) + "\n" +
                       Step("Marker", "rock_hammer", site.markerPlaced) + "\n" +
                       Step("Photo", "survey_camera", site.photographed) + "\n" +
                       Step("Core", "core_drill", site.sampled) + "\n" +
                       (site.analyzed ? "Lab analysis: done!" : site.sampled ? "Lab analysis: Core Drill (2) at the lab bench" : "Lab analysis: after the core");
                return true;
            }
            return false;
        }

        private static string Step(string what, string id, bool done)
        {
            return done ? what + ": done!" : what + ": " + MarsBaseTool.NameOf(id) + " (" + MarsTool.KeyOf(id) + ")";
        }
    }
}
