using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class SurvivalTests
    {
        [TestCase(0, 1)] [TestCase(50, 0.8)] [TestCase(100, 0.6)]
        public void TirednessControlsWorkEfficiency(double tired, double efficiency)
        {
            Assert.That(new GameState { Tiredness = tired }.WorkEfficiency, Is.EqualTo(efficiency));
        }

        [Test]
        public void NeedsConsumeReservesAndLifeSupportPaysFromEnergy()
        {
            var sim = new Simulation();
            sim.State.EnergyKWh = 10;
            sim.EnableSurvival(new Inventory());
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            Assert.That(sim.State.Hunger, Is.EqualTo(25).Within(1e-8));
            Assert.That(sim.State.Water, Is.EqualTo(50).Within(1e-8));
            Assert.That(sim.State.Oxygen, Is.EqualTo(100).Within(1e-8));
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(9.5).Within(1e-8));
            Assert.That(sim.State.Tiredness, Is.EqualTo(25).Within(1e-8));
        }

        [TestCase("starvation", 8)] [TestCase("dehydration", 20)]
        [TestCase("hypoxia", 40)] [TestCase("temperature", 15)] [TestCase("radiation", 4)]
        public void EachHazardDrainsHealth(string cause, double damage)
        {
            var state = new GameState();
            var health = new HealthSystem(state);
            if (cause == "starvation") state.Hunger = 100;
            if (cause == "dehydration") state.Water = 0;
            if (cause == "hypoxia") state.Oxygen = 0;
            if (cause == "temperature") { state.TemperatureProtected = false; state.TemperatureC = -50; }
            if (cause == "radiation") state.RadiationDoseMSv = GameConstants.H_limit;
            health.Step(state, 1);
            Assert.That(state.Health, Is.EqualTo(100 - damage).Within(1e-8));
            Assert.That(state.LastDamageCause, Is.EqualTo(cause));
        }

        [Test]
        public void CombinedDrainsAreCappedAndZeroTimeDoesNothing()
        {
            var state = new GameState { Hunger = 100, Water = 0, Oxygen = 0,
                RadiationDoseMSv = 10000, TemperatureProtected = false, TemperatureC = -100 };
            var health = new HealthSystem(state);
            health.Step(state, 0);
            Assert.That(state.Health, Is.EqualTo(100));
            health.Step(state, 0.5);
            Assert.That(state.Health, Is.EqualTo(80));
        }

        [Test]
        public void NeglectKillsAtExpectedTimeAndReportsCauseOnce()
        {
            var sim = new Simulation();
            sim.EnableSurvival(new Inventory());
            int deaths = 0;
            sim.Events.PlayerDied += () => deaths++;
            sim.Tick(sim.Clock.SecondsPerSol * 10);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(sim.State.DeathCause, Is.EqualTo("hypoxia"));
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(3.5).Within(0.02));
            sim.Tick(1);
            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void CriticalEventsReportBothBandsAndRearmOnRecovery()
        {
            var sim = new Simulation();
            int water = 0;
            sim.Events.StatCritical += (id, value) => { if (id == "water") water++; };
            sim.State.Water = 25; sim.Tick(0);
            sim.State.Water = 10; sim.Tick(0); sim.Tick(0);
            Assert.That(water, Is.EqualTo(2));
            sim.State.Water = 100; sim.Tick(0);
            sim.State.Water = 10; sim.Tick(0);
            Assert.That(water, Is.EqualTo(3), "A jump across both bands emits one current-value notification.");
        }

        [Test]
        public void InactivityLowersFitnessAndMaximumHealth()
        {
            var state = new GameState();
            var health = new HealthSystem(state);
            health.Step(state, 10);
            Assert.That(state.Fitness, Is.EqualTo(90));
            Assert.That(state.MaxHealth, Is.EqualTo(99));
            Assert.That(state.Health, Is.EqualTo(99));
        }

        [Test]
        public void ExistingSystemsShareTheTotalHealthDrainCap()
        {
            var sim = new Simulation();
            sim.EnableSurvival(new Inventory());
            sim.RegisterSystem(new ExtremeExposure());
            sim.Tick(sim.Clock.SecondsPerSol * 0.5);
            Assert.That(sim.State.Health, Is.EqualTo(80).Within(1e-8));
        }

        private sealed class ExtremeExposure : ISimSystem
        {
            public int Order => GameConstants.WeatherSystemOrder;
            public void Step(GameState state, double dtSol) { state.Health -= 1000 * dtSol; }
        }

        [Test]
        public void LifeSupportUsesTheRealBatteryAndDoesNotDoubleCharge()
        {
            var sim = new Simulation();
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 20),
                new SolarPanel(0, 1, 1), new Battery(10, 100, 100, 1, 10),
                new EnergyLoads(1, 0, 0, 0, 0, 20), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            sim.EnableSurvival(new Inventory(), energy);
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(9.5).Within(1e-8));
            Assert.That(sim.State.Oxygen, Is.EqualTo(100).Within(1e-8));
        }

        [Test]
        public void PartialLifeSupportAndPowerRecoveryAffectOxygenWithoutFalseHypoxia()
        {
            var state = new GameState { Oxygen = 0, EnergyKWh = 1 };
            var survival = new SurvivalSystem(new Inventory());
            var health = new HealthSystem(state, survival);
            survival.Step(state, 0.1); health.Step(state, 0.1);
            Assert.That(state.Oxygen, Is.EqualTo(10).Within(1e-8));
            Assert.That(state.Health, Is.GreaterThan(99));
        }

        [Test]
        public void RadiationDamageIsCountedOnceByHealthSystem()
        {
            var sim = new Simulation();
            sim.State.EnergyKWh = 100;
            sim.State.RadiationDoseMSv = GameConstants.H_limit;
            var radiation = new RadiationSystem(sim.Clock, _ => new RadiationConditions(0, 0), sim.Modifiers, sim.Events);
            sim.RegisterSystem(radiation);
            sim.EnableSurvival(new Inventory());
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            Assert.That(sim.State.Health, Is.EqualTo(98).Within(1e-8));
        }

        [Test]
        public void NeglectTimingIsIndependentOfRenderTickPartition()
        {
            var coarse = new Simulation(); coarse.EnableSurvival(new Inventory());
            var fine = new Simulation(); fine.EnableSurvival(new Inventory());
            coarse.Tick(coarse.Clock.SecondsPerSol * 10);
            for (int i = 0; i < 10000 && fine.State.Alive; i++)
                fine.Tick(fine.Clock.SecondsPerSol / 1000);
            Assert.That(fine.Clock.ElapsedSols, Is.EqualTo(coarse.Clock.ElapsedSols).Within(0.02));
            Assert.That(fine.State.DeathCause, Is.EqualTo(coarse.State.DeathCause));
        }

        [TestCase("starvation")] [TestCase("dehydration")] [TestCase("hypoxia")]
        [TestCase("radiation")] [TestCase("temperature")]
        public void LethalHazardReportsItsCause(string cause)
        {
            var sim = new Simulation();
            var health = new HealthSystem(sim.State);
            sim.RegisterSystem(health);
            sim.State.Health = 1;
            if (cause == "starvation") sim.State.Hunger = 100;
            if (cause == "dehydration") sim.State.Water = 0;
            if (cause == "hypoxia") sim.State.Oxygen = 0;
            if (cause == "radiation") sim.State.RadiationDoseMSv = GameConstants.H_limit;
            if (cause == "temperature") { sim.State.TemperatureProtected = false; sim.State.TemperatureC = 50; }
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(sim.State.DeathCause, Is.EqualTo(cause));
        }

        [Test]
        public void InvalidDeltaDoesNotMutateHealth()
        {
            var state = new GameState();
            var health = new HealthSystem(state);
            Assert.Throws<ArgumentOutOfRangeException>(() => health.Step(state, double.NaN));
            Assert.That(state.Health, Is.EqualTo(100));
        }
    }
}
