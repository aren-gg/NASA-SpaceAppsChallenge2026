using System.Collections;
using MarsTrainer.Gameplay.World;
using MarsTrainer.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MarsTrainer.Tests
{
    public sealed class MissionHudBindingTests
    {
        private GameObject worldOwner;
        private GameObject hudOwner;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (hudOwner != null) Object.Destroy(hudOwner);
            if (worldOwner != null) Object.Destroy(worldOwner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HudLoadedAfterMissionBindsItsSavedActionButtons()
        {
            worldOwner = new GameObject("Late HUD fixture");
            var world = worldOwner.AddComponent<TerrainGenerator>();
            for (int frame = 0; frame < 600 && world.Mission == null; frame++) yield return null;
            Assert.That(world.Mission, Is.Not.Null);
            hudOwner = Object.Instantiate(Resources.Load<GameObject>("MarsTrainerHUD"));
            yield return null; yield return null;
            world.Mission.Simulation.State.Hunger = 50;
            var controls = hudOwner.GetComponentInChildren<MissionControls>(true);
            foreach (var button in controls.GetComponentsInChildren<Button>(true))
                if (button.name == "Eat") { button.onClick.Invoke(); break; }
            Assert.That(world.Mission.Simulation.State.Hunger, Is.LessThan(50));
            var tutorial = hudOwner.GetComponentInChildren<TutorialPanel>(true);
            tutorial.GetComponentInChildren<Button>(true).onClick.Invoke();
            Assert.That(tutorial.gameObject.activeSelf, Is.False);
        }
    }
}
