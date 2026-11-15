using System;
using MarsTrainer.Core;
using MarsTrainer.Data;
using MarsTrainer.Sim;
using MarsTrainer.Sim.World;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class OpenWorldPacingTests
    {
        [Test]
        public void ExplorationDurationDoesNotChangeTheDefaultMissionClock()
        {
            var custom = (MissionSession)Activator.CreateInstance(typeof(MissionSession),
                new object[] { new DataRepository(), 42, null, 7200.0 });
            Assert.That(custom.Simulation.Clock.SecondsPerSol, Is.EqualTo(7200.0 / GameConstants.MarsYearSols));
            var standard = new MissionSession(new DataRepository(), 42);
            Assert.That(standard.Simulation.Clock.SecondsPerSol,
                Is.EqualTo(GameConstants.RunDurationSeconds / GameConstants.MarsYearSols));
        }

        [Test]
        public void ExplorationTankUsesItsOwnCapacityAndRefillsOnlyAtBase()
        {
            var sim = new Simulation(GameConstants.MarsYearSols * 300);
            var expedition = (ExpeditionSystem)Activator.CreateInstance(typeof(ExpeditionSystem),
                new object[] { sim.Clock, null, 300.0 });
            sim.RegisterSystem(expedition); expedition.AtBase = false;
            sim.Tick(30);
            Assert.That(expedition.TankSeconds, Is.EqualTo(270).Within(1e-6));
            Assert.That(sim.State.Oxygen, Is.EqualTo(90).Within(1e-6));
            expedition.AtBase = true; sim.Tick(1);
            Assert.That(expedition.TankSeconds, Is.EqualTo(300));
            Assert.That(sim.State.Oxygen, Is.EqualTo(100));
        }
    }
}
