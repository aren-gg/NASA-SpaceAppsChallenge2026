#if UNITY_EDITOR
using System.Collections;
using MarsTrainer.Gameplay.Farm;
using MarsTrainer.Gameplay.Radiation;
using MarsTrainer.Gameplay.World;
using MarsTrainer.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MarsTrainer.Tests
{
    public sealed class PlayableGameTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            var scene = SceneManager.CreateScene("Empty integration fixture");
            SceneManager.SetActiveScene(scene);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != scene) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
        }

        [UnityTest]
        public IEnumerator MainConnectsTerrainFarmActionsRecallAndReplay()
        {
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            TerrainGenerator world = null; HudView hud = null;
            for (int frame = 0; frame < 600; frame++)
            {
                world = Object.FindObjectOfType<TerrainGenerator>(); hud = Object.FindObjectOfType<HudView>();
                if (world != null && world.gameObject.scene.name == "Main" && world.Mission != null && hud != null && hud.Source != null) break;
                yield return null;
            }
            Assert.That(world, Is.Not.Null); Assert.That(hud, Is.Not.Null);
            var mission = world.Mission; var sim = mission.Simulation;
            Assert.That(world.UsesBundledElevation, Is.True);
            Assert.That(hud.Source, Is.SameAs(sim));
            Assert.That(world.GetComponentInChildren<FarmInteract>().Farm, Is.SameAs(mission.Farm));
            var briefing = hud.GetComponentInChildren<MissionBriefing>(true);
            Assert.That(briefing.IsOpen, Is.True); Assert.That(sim.Clock.Paused, Is.True);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject,
                Is.SameAs(briefing.GetComponentInChildren<Button>().gameObject), "Enter must start the briefing, not select the hidden HUD.");
            briefing.Begin(); world.enabled = false;
            var controls = hud.GetComponentInChildren<MissionControls>(true);
            foreach (var button in controls.GetComponentsInChildren<Button>(true))
                if (button.name == "Tend farm") button.onClick.Invoke();
            Assert.That(mission.Farm.Plots[0].Crop, Is.Not.Null);
            sim.State.Fitness = 40;
            foreach (var button in controls.GetComponentsInChildren<Button>(true))
                if (button.name.StartsWith("Exercise:")) button.onClick.Invoke();
            Assert.That(sim.State.Fitness, Is.GreaterThan(40));
            var drone = world.GetComponentInChildren<RecallDrone>(); Assert.That(drone, Is.Not.Null);
            var player = world.Player; player.enabled = false;
            var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
            player.transform.localPosition = new Vector3(25, world.SampleHeight(25, 0) + .1f, 0);
            controller.enabled = true; Physics.SyncTransforms(); yield return new WaitForFixedUpdate();
            player.RefreshLocation(); mission.AtBase = false;
            int recalls = 0; sim.Events.RecallLaunched += () => recalls++;
            sim.Events.ReportFlareAlert(sim.Clock.Sol, 3); sim.Tick(.01);
            Assert.That(drone.Flying, Is.True); Assert.That(recalls, Is.EqualTo(1));
            var position = player.transform.position; sim.Clock.Paused = true;
            yield return null; yield return null;
            Assert.That(player.transform.position, Is.EqualTo(position));
            sim.Clock.Paused = false;
            for (int frame = 0; frame < 600 && drone.Flying; frame++) { drone.Fly(1f / 60); yield return null; }
            Assert.That(drone.Flying, Is.False);
            Assert.That(player.transform.localPosition.x, Is.EqualTo(0).Within(.5f));
            sim.State.Health = 0; sim.Tick(.01);
            var report = hud.GetComponentInChildren<EndScreen>(true); Assert.That(report.gameObject.activeSelf, Is.True);
            foreach (var button in report.GetComponentsInChildren<Button>(true))
                if (button.name == "Start a new mission") { button.onClick.Invoke(); break; }
            yield return null; yield return null;
            Assert.That(Object.FindObjectOfType<TerrainGenerator>(), Is.Not.SameAs(world));
        }
    }
}

#endif
