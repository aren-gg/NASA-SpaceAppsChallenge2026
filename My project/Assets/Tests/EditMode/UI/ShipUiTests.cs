using MarsTrainer.Sim;
using MarsTrainer.UI;
using MarsTrainer.Core;
using MarsTrainer.Data;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.Tests
{
    public sealed class ShipUiTests
    {
        private GameObject root;
        private float volume;
        [SetUp] public void Setup() { volume = AudioListener.volume; }
        [TearDown] public void Cleanup()
        {
            if (root != null) Object.DestroyImmediate(root);
            AudioListener.volume = volume;
        }

        // EditMode does not dispatch player lifecycle callbacks, including on inactive modal children.
        private static void RunAwake(MonoBehaviour component)
        {
            component.GetType().GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(component, null);
        }

        [Test]
        public void TutorialGuidesFiveSolsAndSkipHidesItImmediately()
        {
            root = HudView.Create();
            var panel = root.GetComponentInChildren<TutorialPanel>(true);
            Assert.That(panel, Is.Not.Null);
            string[] topics = { "Move", "Farm", "Energy", "Shelter", "Research" };
            for (int sol = 0; sol < GameConstants.TutorialSols; sol++)
            {
                panel.ShowSol(sol);
                StringAssert.Contains(topics[sol], panel.GetComponentInChildren<TMP_Text>().text);
            }
            panel.ShowSol(GameConstants.TutorialSols);
            Assert.That(panel.gameObject.activeSelf, Is.False);
            panel.ShowSol(0); panel.Skip(); panel.ShowSol(1);
            Assert.That(panel.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void SavedPrefabActionsUseTheBoundInventoryAndBattery()
        {
            root = Object.Instantiate(Resources.Load<GameObject>("MarsTrainerHUD"));
            var mission = new MissionSession(new DataRepository(), 42);
            var controls = root.GetComponentInChildren<MissionControls>(true);
            RunAwake(controls);
            controls.Bind(mission);
            mission.Simulation.State.Hunger = 50;
            mission.Simulation.State.Water = 50;
            mission.Simulation.State.Health = 40;
            foreach (var button in controls.GetComponentsInChildren<Button>(true)) button.onClick.Invoke();
            Assert.That(mission.Simulation.State.Hunger, Is.LessThan(50));
            Assert.That(mission.Simulation.State.Water, Is.GreaterThan(50));
            Assert.That(mission.Simulation.State.Health, Is.GreaterThan(40));
            Assert.That(mission.Simulation.Survival.Sleeping, Is.True);
            Assert.That(mission.Farm.Plots[0].Crop, Is.Not.Null);
        }

        [Test]
        public void EvaCanUseCarriedFoodAndWaterButCannotWorkAtBaseRemotely()
        {
            root = HudView.Create();
            var mission = new MissionSession(new DataRepository(), 42) { AtBase = false, Sheltered = false };
            var controls = root.GetComponentInChildren<MissionControls>(true); controls.Bind(mission);
            mission.Simulation.State.Hunger = 50; mission.Simulation.State.Water = 50;
            controls.Refresh();
            foreach (var button in controls.GetComponentsInChildren<Button>(true))
            {
                bool portable = button.name == "Eat" || button.name == "Drink";
                Assert.That(button.interactable, Is.EqualTo(portable), button.name);
                button.onClick.Invoke();
            }
            Assert.That(mission.Simulation.State.Hunger, Is.LessThan(50));
            Assert.That(mission.Simulation.State.Water, Is.GreaterThan(50));
            Assert.That(mission.Farm.Plots[0].Crop, Is.Null);
            Assert.That(mission.Simulation.Survival.Sleeping, Is.False);
        }

        [Test]
        public void BriefingStopsTheClockUntilBegin()
        {
            root = HudView.Create();
            var briefing = root.GetComponentInChildren<MissionBriefing>(true);
            var clock = new SimClock(); bool closed = false;
            briefing.Closed = () => closed = true; briefing.Open(clock);
            Assert.That(clock.Paused, Is.True);
            briefing.Begin();
            Assert.That(clock.Paused, Is.False);
            Assert.That(briefing.IsOpen, Is.False);
            Assert.That(closed, Is.True);
        }

        [Test]
        public void SavedPrefabWiresSkipAndSettingsButtons()
        {
            root = Object.Instantiate(Resources.Load<GameObject>("MarsTrainerHUD"));
            var tutorial = root.GetComponentInChildren<TutorialPanel>(true);
            RunAwake(tutorial);
            tutorial.ShowSol(0);
            tutorial.GetComponentInChildren<Button>(true).onClick.Invoke();
            Assert.That(tutorial.gameObject.activeSelf, Is.False);
            AudioListener.volume = 1;
            var settings = root.GetComponentInChildren<MissionSettings>(true);
            RunAwake(settings);
            settings.GetComponentsInChildren<Button>(true)[0].onClick.Invoke();
            Assert.That(AudioListener.volume, Is.EqualTo(0.5f));
        }

        [Test]
        public void SettingsChangeActualAudioAndTextWithoutCompounding()
        {
            root = HudView.Create();
            var settings = root.GetComponentInChildren<MissionSettings>(true);
            Assert.That(settings, Is.Not.Null);
            TMP_Text text = null;
            foreach (var candidate in root.GetComponentsInChildren<TMP_Text>(true))
                if (candidate.fontSize == GameConstants.UiBody) { text = candidate; break; }
            Assert.That(text, Is.Not.Null);
            float original = text.fontSize;
            settings.SetVolume(0.25f);
            Assert.That(AudioListener.volume, Is.EqualTo(0.25f));
            settings.SetLargeText(true); settings.SetLargeText(true);
            Assert.That(text.fontSize, Is.EqualTo(original * GameConstants.SettingsLargeTextScale).Within(0.01));
            settings.SetLargeText(false);
            Assert.That(text.fontSize, Is.EqualTo(original).Within(0.01));
            Assert.That(settings.GetComponentsInChildren<Button>(true).Length, Is.GreaterThanOrEqualTo(2));
        }
    }
}
