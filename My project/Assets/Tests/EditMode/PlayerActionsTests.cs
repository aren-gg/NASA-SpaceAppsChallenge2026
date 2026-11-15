using System;
using MarsTrainer.Gameplay.Player;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class PlayerActionsTests
    {
        private GameObject owner;
        private Simulation sim;
        private Inventory inventory;
        private PlayerActions actions;

        [SetUp]
        public void SetUp()
        {
            sim = new Simulation();
            inventory = StockFood(4000000);
            inventory.AddWater(10000);
            sim.State.EnergyKWh = 10000;
            owner = new GameObject("Player actions");
            actions = owner.AddComponent<PlayerActions>();
            actions.Initialize(sim, inventory);
        }

        [TearDown]
        public void TearDown() { UnityEngine.Object.DestroyImmediate(owner); }

        [Test]
        public void EatAndDrinkSpendOnlyWhatFitsInReserves()
        {
            sim.State.Hunger = 50; sim.State.Water = 50;
            double food = inventory.FoodKcal, water = inventory.WaterLitres;
            Assert.That(actions.Eat(5000), Is.True);
            Assert.That(actions.Drink(3), Is.True);
            Assert.That(sim.State.Hunger, Is.Zero);
            Assert.That(sim.State.Water, Is.EqualTo(100));
            Assert.That(inventory.FoodKcal, Is.EqualTo(food - 2500));
            Assert.That(inventory.WaterLitres, Is.EqualTo(water - 1.5));
        }

        [Test]
        public void SleepAdvancesSimulationAndRecoversTiredness()
        {
            sim.State.Tiredness = 100;
            Assert.That(actions.Sleep(0.25), Is.True);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(sim.State.Tiredness, Is.EqualTo(50).Within(1e-8));
            Assert.That(sim.State.Hunger, Is.GreaterThan(0));
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(9999.75).Within(1e-8));
            Assert.That(sim.Survival.Sleeping, Is.False);
        }

        [Test]
        public void ExerciseCostsFoodWaterPowerAndTimeAndMaintainsFitness()
        {
            sim.State.Fitness = 50;
            double food = inventory.FoodKcal, water = inventory.WaterLitres;
            Assert.That(actions.Exercise(0.25), Is.True);
            Assert.That(sim.State.Fitness, Is.GreaterThan(50));
            Assert.That(sim.State.Fitness, Is.LessThanOrEqualTo(100));
            Assert.That(inventory.FoodKcal, Is.EqualTo(food - 250));
            Assert.That(inventory.WaterLitres, Is.EqualTo(water - 0.5));
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(9999.25).Within(1e-8));
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(0.25).Within(1e-9));
        }

        [Test]
        public void MedbayConvertsPaidPowerToHpUpToFitnessMaximum()
        {
            sim.State.Fitness = 0; sim.State.Health = 80;
            Assert.That(actions.Medbay(10), Is.True);
            Assert.That(sim.State.Health, Is.EqualTo(90));
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(9998));
        }

        [Test]
        public void FailedExerciseIsAtomicAndPausedActionsDoNotSpendOrPassTime()
        {
            sim.State.EnergyKWh = 0;
            double food = inventory.FoodKcal, water = inventory.WaterLitres;
            Assert.That(actions.Exercise(1), Is.False);
            Assert.That(inventory.FoodKcal, Is.EqualTo(food));
            Assert.That(inventory.WaterLitres, Is.EqualTo(water));
            sim.Clock.Paused = true;
            Assert.That(actions.Sleep(1), Is.False);
            Assert.That(sim.Clock.ElapsedSols, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => actions.Eat(double.NaN));
        }

        [Test]
        public void DeadPlayerCannotHealOrConsumeSupplies()
        {
            sim.State.Health = 0; sim.Tick(0);
            double food = inventory.FoodKcal;
            Assert.That(actions.Eat(100), Is.False);
            Assert.That(actions.Medbay(1), Is.False);
            Assert.That(sim.State.Health, Is.Zero);
            Assert.That(inventory.FoodKcal, Is.EqualTo(food));
        }

        [Test]
        public void SensibleDailyRoutineSurvivesEntireMission()
        {
            for (int sol = 0; sol < GameConstants.MissionDurationDays; sol++)
            {
                actions.Eat(5000); actions.Drink(3);
                Assert.That(actions.Exercise(0.1), Is.True);
                Assert.That(actions.Sleep(0.25), Is.True);
                sim.Tick(sim.Clock.SecondsPerSol * 0.65);
            }
            Assert.That(sim.State.Alive, Is.True);
            Assert.That(sim.State.Health, Is.GreaterThan(95));
            Assert.That(sim.State.Fitness, Is.GreaterThan(99));
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(GameConstants.MissionDurationDays).Within(1e-8));
            Assert.That(actions.Exercise(0.1), Is.False, "Actions must stop at the 99-day finish.");
        }

        // Use actual farm harvests so eating proves integration with the food producer.
        private static Inventory StockFood(double kcal)
        {
            var sim = new Simulation();
            var food = new Inventory();
            var crop = new FarmCrop("ration", "Rations", 1, kcal, 400, 1, 1000, 16);
            var farm = new FarmSystem(sim.Clock, new[] { crop }, food, sim.Events,
                _ => new FarmConditions(400, 0, 1000, 20), requested => requested, unitsPerPlot: 1);
            sim.RegisterSystem(farm);
            farm.Plant(0, crop.Id); farm.Water(0, 2);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(farm.Harvest(0), Is.True);
            return food;
        }
    }
}
