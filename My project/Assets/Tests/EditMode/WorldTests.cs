using MarsTrainer.Sim;
using MarsTrainer.Sim.World;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class WorldTests
    {
        [Test]
        public void PoweredSurvivalCannotRefillAnOutdoorTank()
        {
            var sim = new Simulation(GameConstants.MarsYearSols * GameConstants.OxygenTankSeconds);
            sim.State.EnergyKWh = 100;
            sim.EnableSurvival(new Inventory());
            var expedition = new ExpeditionSystem(sim.Clock);
            sim.RegisterSystem(expedition);
            expedition.AtBase = false;
            sim.Tick(GameConstants.OxygenTankSeconds / 2);
            Assert.That(expedition.TankSeconds, Is.EqualTo(GameConstants.OxygenTankSeconds / 2).Within(1e-7));
            Assert.That(sim.State.Oxygen, Is.EqualTo(50).Within(1e-7));
        }

        [Test]
        public void BaseRefillsTankAndPausedClockDoesNotSpendSprintResources()
        {
            var sim = new Simulation(GameConstants.MarsYearSols * GameConstants.OxygenTankSeconds);
            var expedition = new ExpeditionSystem(sim.Clock);
            sim.RegisterSystem(expedition);
            expedition.AtBase = false;
            sim.Tick(10);
            expedition.AtBase = true;
            sim.Tick(0.1);
            Assert.That(expedition.TankSeconds, Is.EqualTo(GameConstants.OxygenTankSeconds));
            sim.State.EnergyKWh = 10;
            expedition.SprintRequested = true;
            sim.Clock.Paused = true;
            sim.Tick(1);
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(10));
            Assert.That(sim.State.Tiredness, Is.Zero);
        }

        [Test]
        public void SprintCostsPowerAndFatigueAndCannotOverdraw()
        {
            var sim = new Simulation(GameConstants.MarsYearSols * GameConstants.OxygenTankSeconds);
            sim.State.EnergyKWh = GameConstants.SprintKWhPerSecond;
            var expedition = new ExpeditionSystem(sim.Clock) { SprintRequested = true };
            sim.RegisterSystem(expedition);
            sim.Tick(1);
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(0).Within(1e-9));
            Assert.That(sim.State.Tiredness, Is.EqualTo(GameConstants.SprintTirednessPerSecond).Within(1e-9));
            Assert.That(expedition.CanSprint(sim.State), Is.False);
            sim.Tick(1);
            Assert.That(sim.State.EnergyKWh, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void SprintPaymentPersistsInTheSharedBattery()
        {
            var sim = new Simulation();
            var battery = new Battery(10, 10, 10, 1, 10);
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 20),
                new SolarPanel(0, 1, 1), battery, new EnergyLoads(0, 0, 0, 0, 0, 20), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            var expedition = new ExpeditionSystem(sim.Clock, energy) { SprintRequested = true };
            sim.RegisterSystem(expedition);
            sim.Tick(1);
            expedition.SprintRequested = false;
            sim.Tick(1);
            Assert.That(battery.StoredKWh, Is.EqualTo(10 - GameConstants.SprintKWhPerSecond).Within(1e-9));
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(battery.StoredKWh));
        }

        [Test]
        public void ExhaustedTankCausesHypoxiaAndClockSpeedScalesExposure()
        {
            var sim = new Simulation(GameConstants.MarsYearSols * GameConstants.OxygenTankSeconds);
            sim.State.EnergyKWh = 1000;
            sim.EnableSurvival(new Inventory());
            var expedition = new ExpeditionSystem(sim.Clock) { AtBase = false };
            sim.RegisterSystem(expedition);
            sim.Clock.Speed = 2;
            sim.Tick(GameConstants.OxygenTankSeconds / 2);
            Assert.That(expedition.TankSeconds, Is.Zero.Within(1e-7));
            Assert.That(sim.State.Oxygen, Is.Zero.Within(1e-7));
            double hp = sim.State.Health;
            sim.Tick(1);
            Assert.That(sim.State.Health, Is.LessThan(hp));
        }
    }
}
