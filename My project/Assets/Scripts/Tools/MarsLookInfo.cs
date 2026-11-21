using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Simulation;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Plain-language description shown next to the crosshair: the outpost tool rack item you
    /// are looking at (press E to put it in its hotbar slot), or the thing E would use.
    /// </summary>
    public static class MarsLookInfo
    {
        public static bool Describe(ToolTarget target, MarsExpedition x, out string title, out string body)
        {
            title = body = null;
            MarsMissionController mission = MarsMissionController.Instance;
            if (mission == null || mission.Simulation == null || mission.Tools == null) return false;

            MarsBaseTool rack = mission.Tools.Aimed;
            if (rack != null)
            {
                title = rack.DisplayName;
                body = "Press E to add it to your hotbar.\n" + MarsToolPickup.UseOf(rack.toolId);
                return true;
            }

            MarsTaskTarget use = mission.Tools.AimedUse;
            if (use == null) return false;
            title = use.name;
            body = "Press E to " + mission.Tools.UseLabel + ".";
            if (use.kind == MarsTaskTarget.Kind.Bunk) body += "\nTime runs fast until morning; your plants keep growing.";
            if (use.kind == MarsTaskTarget.Kind.Food)
            {
                int crops = 0;
                foreach (int n in mission.Simulation.State.basket) crops += n;
                body += "\nCrops in your basket: " + crops;
            }
            return true;
        }
    }
}
