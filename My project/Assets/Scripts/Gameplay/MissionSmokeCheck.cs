using System;
using System.Collections;
using System.IO;
using MarsSurvival.Simulation;
using MarsSurvival.UI;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    public sealed class MissionSmokeCheck : MonoBehaviour
    {
        private IEnumerator Start()
        {
            if (Array.IndexOf(System.Environment.GetCommandLineArgs(), "--mission-smoke") < 0) yield break;
            float deadline = Time.realtimeSinceStartup + 90;
            var home = FindObjectOfType<MarsHomeScreen>();
            while (home == null && Time.realtimeSinceStartup < deadline) { yield return null; home = FindObjectOfType<MarsHomeScreen>(); }
            if (home != null) home.StartMission();
            var controller = MarsMissionController.Instance;
            while (!controller.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            string result;
            if (!controller.Ready) result = "FAIL: mission composition did not become ready.";
            else
            {
                controller.NewMission(MissionLength.Month);
                yield return null;
                controller.Execute("routine", "off");
                controller.SetPowerRequest(PowerChannel.LifeControl, 0.45);
                controller.SetPowerRequest(PowerChannel.Temperature, 0.45);
                controller.SetPowerRequest(PowerChannel.RadiationShielding, 0.05);
                controller.Advance(120);
                while (controller.AdvanceRemaining > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                controller.SaveMission();
                double time = controller.Simulation.State.elapsedSeconds;
                controller.LoadMission();
                result = controller.Simulation.State.elapsedSeconds == time && controller.Simulation.State.sites.Count >= 12 && FindObjectOfType<MarsMissionHud>() != null
                    ? "PASS: scene readiness, mission startup, terrain sites, fixed-step advancement, save/load and HUD composition." : "FAIL: integrated state check. Sites=" + controller.Simulation.State.sites.Count + ", time=" + time + ", loaded=" + controller.Simulation.State.elapsedSeconds + ", notice=" + controller.Notice;
            }
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "smoke-result.txt"), result);
            Debug.Log(result);
            Application.Quit(result.StartsWith("PASS") ? 0 : 1);
        }
    }
}
