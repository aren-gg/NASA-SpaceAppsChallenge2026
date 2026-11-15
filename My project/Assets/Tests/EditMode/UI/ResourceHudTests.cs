using MarsTrainer.Core;
using MarsTrainer.Data;
using MarsTrainer.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.Tests
{
    public sealed class ResourceHudTests
    {
        [Test]
        public void QuickSuppliesConsumeSharedInventoryOnEvaAndExplainFullMeters()
        {
            var root = HudView.Create();
            try
            {
                var mission = new MissionSession(new DataRepository(),42) {AtBase=false,Sheltered=false};
                var hud=root.GetComponent<HudView>(); hud.Bind(mission.Simulation,mission.Research);
                root.GetComponentInChildren<MissionControls>(true).Bind(mission);
                typeof(HudView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                var actions=root.transform.Find("Safe area/Mission HUD/Quick supplies").GetComponentsInChildren<Button>();
                Assert.That(actions[0].interactable,Is.False,"A full food meter should explain why eating is unavailable.");
                Assert.That(actions[1].interactable,Is.False);
                Assert.That(actions[0].GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("Full [F]"));
                Assert.That(actions[1].GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("Full [G]"));
                mission.Simulation.State.Hunger=50; mission.Simulation.State.Water=50; typeof(HudView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                double food=mission.Inventory.FoodKcal, water=mission.Inventory.WaterLitres;
                foreach(var button in actions) {Assert.That(button.interactable,Is.True);button.onClick.Invoke();}
                Assert.That(mission.Simulation.State.Hunger,Is.LessThan(50));
                Assert.That(mission.Simulation.State.Water,Is.GreaterThan(50));
                Assert.That(mission.Inventory.FoodKcal,Is.LessThan(food));
                Assert.That(mission.Inventory.WaterLitres,Is.LessThan(water));
                mission.Simulation.Clock.Paused=true; typeof(HudView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                foreach(var button in actions) Assert.That(button.interactable,Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void OxygenIsFirstAndFoodWaterAreDirectHudActions()
        {
            var root = HudView.Create();
            try
            {
                var dayLabel = root.transform.Find("Safe area/Mission HUD/Mission date").GetComponentInChildren<TMPro.TMP_Text>();
                Assert.That(dayLabel.text, Is.EqualTo("Martian Days 00.0 / 99"));
                var vitals = root.transform.Find("Safe area/Mission HUD/Suit vitals");
                Assert.That(vitals.GetChild(0).name, Is.EqualTo("Air"), "Oxygen needs the clearest place in the survival cluster.");
                foreach (var glyph in root.GetComponentsInChildren<ResourceGlyph>(true))
                    Assert.That(glyph.GetComponent<CanvasRenderer>(), Is.Not.Null, glyph.name + " must render its procedural mesh.");
                var actions = root.transform.Find("Safe area/Mission HUD/Quick supplies");
                Assert.That(actions, Is.Not.Null, "Eating and drinking should not require opening a menu.");
                Assert.That(actions.GetComponentsInChildren<Button>().Length, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
