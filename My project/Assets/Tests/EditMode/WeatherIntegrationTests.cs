using System;
using System.Collections.Generic;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class WeatherIntegrationTests
    {
        [Test]
        public void EnergyConsumesWeatherDustAndProtectedTemperature()
        {
            var unprotected = EnergyAfter(false);
            var protectedReport = EnergyAfter(true);
            Assert.That(protectedReport.GenerationKWh, Is.GreaterThan(unprotected.GenerationKWh));
            Assert.That(protectedReport.LoadKWh, Is.LessThan(unprotected.LoadKWh));
        }

        private static EnergyReport EnergyAfter(bool protection)
        {
            var sim = new Simulation();
            if (protection) sim.Modifiers.Set(GameConstants.WeatherSeverityStat, "research", -2);
            var weather = new WeatherSystem(sim.Clock,
                _ => new WeatherConditions(0, 3, -110, -30, 0, 0, false),
                3, sim.Modifiers, sim.Events, sheltered: () => true);
            var energy = new EnergySystem(sim.Clock, weather.EnergyAt, new SolarPanel(1, 10, 1),
                new Battery(100, 100, 100, 1, 100), new EnergyLoads(0, 0, 0, 0, 0.1, 20),
                sim.Modifiers, sim.Events);
            sim.RegisterSystem(weather);
            sim.RegisterSystem(energy);
            sim.Tick(sim.Clock.SecondsPerSol);
            return energy.LastReport;
        }

        [Test]
        public void CoarseAndFineTicksProduceSameExposureAndOrderedSignals()
        {
            var coarse = Run(1, out var coarseSignals);
            var fine = Run(128, out var fineSignals);
            Assert.That(fine.State.Health, Is.EqualTo(coarse.State.Health).Within(1e-8));
            Assert.That(fine.State.Tiredness, Is.EqualTo(coarse.State.Tiredness).Within(1e-8));
            CollectionAssert.AreEqual(coarseSignals, fineSignals);
        }

        private static Simulation Run(int parts, out List<string> signals)
        {
            var sim = new Simulation();
            var weather = new WeatherSystem(sim.Clock, sol => sol == 1
                ? new WeatherConditions(0, 2, -110, -5, 0, 3, true)
                : new WeatherConditions(0, 0.35, -80, -20, 0, 0, false), 3, sim.Modifiers, sim.Events);
            var trace = new List<string>();
            sim.Events.WeatherWarned += e => trace.Add("warn:" + e.Id);
            sim.Events.WeatherStarted += e => trace.Add("start:" + e.Id);
            sim.Events.WeatherPeaked += e => trace.Add("peak:" + e.Id);
            sim.Events.WeatherEnded += e => trace.Add("end:" + e.Id);
            sim.RegisterSystem(weather);
            for (int i = 0; i < parts; i++) sim.Tick(sim.Clock.SecondsPerSol * 3 / parts);
            signals = trace;
            return sim;
        }

        [Test]
        public void PlayerCanTakeShelterAtWarningBeforeFlareStarts()
        {
            var sim = new Simulation();
            bool sheltered = false;
            var weather = new WeatherSystem(sim.Clock, sol => new WeatherConditions(0, 0.35,
                -80, -20, 0, sol == 2 ? 3 : 0, false), 4, sim.Modifiers, sim.Events,
                sheltered: () => sheltered);
            sim.Events.WeatherWarned += e => sheltered = true;
            sim.RegisterSystem(weather);
            sim.Tick(sim.Clock.SecondsPerSol * 4);
            Assert.That(sheltered, Is.True);
            Assert.That(sim.State.Health, Is.EqualTo(100));
        }

        [Test]
        public void AuroraOnlyWarnsWithoutInventingScheduledRadiationOrHealthDamage()
        {
            var sim = new Simulation();
            var weather = new WeatherSystem(sim.Clock,
                _ => new WeatherConditions(0, 0.35, -80, -20, 0, 0, true), 2, sim.Modifiers, sim.Events);
            int warned = 0;
            sim.Events.WeatherWarned += e => warned++;
            sim.RegisterSystem(weather);
            sim.RegisterSystem(new RadiationSystem(sim.Clock, weather.RadiationAt, sim.Modifiers, sim.Events));
            sim.Tick(sim.Clock.SecondsPerSol * 2);
            Assert.That(warned, Is.EqualTo(1));
            Assert.That(sim.State.Health, Is.EqualTo(100));
            Assert.That(sim.State.RadiationDoseMSv, Is.Zero);
        }

        [Test]
        public void PauseDoesNotConsumeWeatherTransitions()
        {
            var sim = new Simulation();
            var weather = new WeatherSystem(sim.Clock,
                _ => new WeatherConditions(0, 0.35, -80, -20, 0, 2, false), 2, sim.Modifiers, sim.Events);
            int started = 0;
            sim.Events.WeatherStarted += e => started++;
            sim.RegisterSystem(weather);
            sim.Clock.Paused = true;
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(started, Is.Zero);
            sim.Clock.Paused = false;
            sim.Tick(sim.Clock.SecondsPerSol / 4);
            Assert.That(started, Is.EqualTo(1));
        }
    }
}
