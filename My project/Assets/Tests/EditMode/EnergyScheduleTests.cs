using System.Collections.Generic;
using MarsTrainer.Data;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class EnergyScheduleTests
    {
        [Test]
        public void ScheduleRowsDriveDustOrbitAndHeatingPerSol()
        {
            var rows = new[]
            {
                new ScheduleDto { ls = 251, dustOpacity = 0, minTemp = -20, maxTemp = 0 },
                new ScheduleDto { ls = 251, dustOpacity = 5, minTemp = 10, maxTemp = 30 },
                new ScheduleDto { ls = 71, dustOpacity = 0, minTemp = 10, maxTemp = 30 }
            };
            var sim = new Simulation();
            var reports = new List<EnergyReport>();
            sim.Events.EnergyReported += report =>
            {
                reports.Add(report);
                Assert.That(sim.State.EnergyKWh / sim.State.BatteryCapKWh * 100,
                    Is.EqualTo(report.BatteryPercent).Within(1e-9));
            };
            var energy = new EnergySystem(sim.Clock, sol =>
            {
                var row = rows[sol];
                return new EnergyConditions(row.ls, row.dustOpacity, (row.minTemp + row.maxTemp) / 2.0);
            }, new SolarPanel(1, 1, 1), new Battery(100, 100, 100, 1),
                new EnergyLoads(0, 0, 0, 0, 0.1, 10), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            sim.Tick(sim.Clock.SecondsPerSol * rows.Length);
            Assert.That(reports.Count, Is.EqualTo(3));
            Assert.That(reports[0].LoadKWh, Is.EqualTo(2).Within(1e-9));
            Assert.That(reports[1].LoadKWh, Is.Zero);
            Assert.That(reports[1].GenerationKWh, Is.LessThan(reports[0].GenerationKWh * 0.1));
            Assert.That(reports[2].GenerationKWh, Is.LessThan(reports[0].GenerationKWh));
        }
    }
}
