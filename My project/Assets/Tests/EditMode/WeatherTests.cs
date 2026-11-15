using System;
using System.Collections.Generic;
using System.IO;
using MarsTrainer.Data;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Events;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class WeatherTests
    {
        private static WeatherConditions Clear(int sol) => new WeatherConditions(0, 0.35, -80, -20, 0, 0, false);

        private static WeatherSystem Add(Simulation sim, Func<int, WeatherConditions> rows,
            int days = 8, int seed = 42, Func<bool> sheltered = null)
        {
            var weather = new WeatherSystem(sim.Clock, rows, days, sim.Modifiers, sim.Events, seed, sheltered);
            sim.RegisterSystem(weather);
            return weather;
        }

        [Test]
        public void MapsAdjacentHazardDaysToTypedEventsWithPeakAndSeverity()
        {
            var sim = new Simulation();
            var weather = Add(sim, sol => sol >= 2 && sol <= 4
                ? new WeatherConditions(0, sol == 3 ? 3 : 1.5, -100, -5, 0, sol == 3 ? 3 : 1, true) : Clear(sol));
            Assert.That(weather.Events.Count, Is.EqualTo(4));
            foreach (var item in weather.Events)
            {
                Assert.That(item.StartSol, Is.EqualTo(2));
                Assert.That(item.EndSol, Is.EqualTo(5));
                Assert.That(item.PeakSol, Is.InRange(item.StartSol, item.EndSol));
                Assert.That(item.Severity, Is.InRange(1, 3));
            }
            Assert.That(weather.Events[0], Is.TypeOf<DustStorm>());
            Assert.That(weather.Events[1], Is.TypeOf<SolarFlare>());
            Assert.That(weather.Events[1].PeakSol, Is.EqualTo(3.5));
            Assert.That(((SolarFlare)weather.Events[1]).AuroraAlert, Is.True);
        }

        [Test]
        public void WarningPrecedesFlareDamageAndDoseAndPhasesFireOnce()
        {
            var sim = new Simulation();
            var trace = new List<string>();
            sim.Events.WeatherWarned += e => trace.Add("warning");
            sim.Events.WeatherStarted += e => trace.Add("start");
            sim.Events.WeatherPeaked += e => trace.Add("peak");
            sim.Events.WeatherEnded += e => trace.Add("end");
            var weather = Add(sim, sol => sol == 2 ? new WeatherConditions(0, 0.35, -80, -20, 0, 3, true) : Clear(sol));
            sim.RegisterSystem(new RadiationSystem(sim.Clock, weather.RadiationAt, sim.Modifiers, sim.Events));
            sim.Tick(sim.Clock.SecondsPerSol);
            CollectionAssert.AreEqual(new[] { "warning" }, trace);
            Assert.That(sim.State.Health, Is.EqualTo(100));
            Assert.That(sim.State.RadiationDoseMSv, Is.Zero);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(sim.State.Health, Is.EqualTo(100));
            sim.Tick(sim.Clock.SecondsPerSol * 2);
            CollectionAssert.AreEqual(new[] { "warning", "start", "peak", "end" }, trace);
            Assert.That(sim.State.Health, Is.LessThan(100));
            Assert.That(sim.State.RadiationDoseMSv, Is.GreaterThan(0));
        }

        [Test]
        public void ResearchReducesSeverityAndExtendsWarningWithoutMovingSchedule()
        {
            var sim = new Simulation();
            var weather = Add(sim, sol => sol == 4 ? new WeatherConditions(0, 0.35, -80, -20, 0, 3, false) : Clear(sol));
            sim.Modifiers.Set(GameConstants.WeatherSeverityStat, "research", -1);
            sim.Modifiers.Set(GameConstants.WeatherWarningStat, "research", 1);
            WeatherEvent warned = null;
            sim.Events.WeatherWarned += e => warned = e;
            sim.Tick(sim.Clock.SecondsPerSol * 2);
            Assert.That(warned, Is.Not.Null);
            Assert.That(warned.Severity, Is.EqualTo(2));
            Assert.That(warned.WarningSol, Is.EqualTo(2));
            Assert.That(warned.StartSol, Is.EqualTo(4));
            Assert.That(weather.RadiationAt(4).FlareLevel, Is.EqualTo(2));
            sim.Modifiers.Set(GameConstants.WeatherSeverityStat, "research", -100);
            Assert.That(weather.RadiationAt(4).FlareLevel, Is.EqualTo(1));
        }

        [Test]
        public void DustRampsOverDaysAndFeedsSolarAndVisibility()
        {
            var sim = new Simulation();
            var weather = Add(sim, sol => sol >= 1 && sol <= 5
                ? new WeatherConditions(0, sol == 3 ? 4 : 2, -80, -20, 0, 0, false) : Clear(sol));
            double first = weather.EnergyAt(1).Tau, peak = weather.EnergyAt(3).Tau;
            Assert.That(peak, Is.GreaterThan(first));
            Assert.That(weather.EnergyAt(5).Tau, Is.LessThan(peak));
            Assert.That(weather.EnergyAt(6).Tau, Is.EqualTo(0.35).Within(1e-9));
            var panel = new SolarPanel(1, 1, 1);
            Assert.That(panel.PowerKW(0.5, peak, 0, sim.Modifiers),
                Is.LessThan(panel.PowerKW(0.5, 0.35, 0, sim.Modifiers)));
            sim.Tick(sim.Clock.SecondsPerSol * 3.5);
            Assert.That(weather.LowVisibility, Is.True);
            sim.Tick(sim.Clock.SecondsPerSol * 3);
            Assert.That(weather.LowVisibility, Is.False);
        }

        [Test]
        public void ShelterPreventsFlareAndColdHealthLossAndColdFeedsHeatingAndFatigue()
        {
            var exposed = new Simulation();
            var sheltered = new Simulation();
            Func<int, WeatherConditions> rows = _ => new WeatherConditions(0, 0.35, -105, -40, 0, 2, false);
            var outside = Add(exposed, rows);
            Add(sheltered, rows, sheltered: () => true);
            exposed.Tick(exposed.Clock.SecondsPerSol / 8);
            sheltered.Tick(sheltered.Clock.SecondsPerSol / 8);
            Assert.That(exposed.State.Health, Is.LessThan(sheltered.State.Health));
            Assert.That(sheltered.State.Health, Is.EqualTo(100));
            Assert.That(outside.FreezingRisk, Is.GreaterThan(0));
            Assert.That(exposed.State.Tiredness, Is.GreaterThan(0));
            Assert.That(outside.EnergyAt(0).OutdoorTemperature, Is.LessThan(-50));
        }

        [Test]
        public void SeedIsRepeatableAndOnlyPerturbsMagnitude()
        {
            Func<int, WeatherConditions> rows = _ => new WeatherConditions(0, 3, -80, -20, 0, 0, false);
            var a = Add(new Simulation(), rows, seed: 1);
            var b = Add(new Simulation(), rows, seed: 1);
            var c = Add(new Simulation(), rows, seed: 2);
            Assert.That(a.EnergyAt(3).Tau, Is.EqualTo(b.EnergyAt(3).Tau));
            Assert.That(a.EnergyAt(3).Tau, Is.Not.EqualTo(c.EnergyAt(3).Tau));
            Assert.That(a.Events[0].StartSol, Is.EqualTo(c.Events[0].StartSol));
        }

        [Test]
        public void MissionRunMapsTheFullScheduleAndOnlyDispatchesHazardsThroughDay99()
        {
            var repository = new DataRepository(Assert.Fail);
            repository.LoadScheduleJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "mars_year_schedule.json")));
            var rows = repository.Schedule;
            var sim = new Simulation();
            var weather = Add(sim, sol =>
            {
                var row = rows[sol];
                return new WeatherConditions(row.ls, row.dustOpacity, row.minTemp, row.maxTemp,
                    row.gcrDose_uGy, row.flareLevel, row.auroraAlert);
            }, rows.Count, sheltered: () => true);
            int expected = 0;
            for (int type = 0; type < 4; type++)
            {
                bool previous = false;
                for (int sol = 0; sol < rows.Count; sol++)
                {
                    var row = rows[sol];
                    bool hazard = type == 0 ? row.dustOpacity >= GameConstants.DustStormTau
                        : type == 1 ? row.flareLevel > 0 || row.auroraAlert
                        : type == 2 ? row.minTemp <= GameConstants.ColdNightTemperature
                        : row.maxTemp >= GameConstants.HeatStormTemperature;
                    bool covered = false;
                    foreach (var item in weather.Events)
                    {
                        string kind = type == 0 ? "dust-storm" : type == 1 ? "solar-flare"
                            : type == 2 ? "cold-night" : "heat-storm";
                        if (item.Type == kind && sol >= item.StartSol && sol < item.EndSol) covered = true;
                    }
                    Assert.That(covered, Is.EqualTo(hazard), $"Hazard {type} at sol {sol}");
                    if (hazard && !previous) expected++;
                    previous = hazard;
                }
            }
            int started = 0, peaked = 0, ended = 0, warned = 0;
            sim.Events.WeatherWarned += e => warned++;
            sim.Events.WeatherStarted += e =>
            {
                started++;
                TestContext.WriteLine($"{e.Type}: start={e.StartSol} peak={e.PeakSol} end={e.EndSol} severity={e.Severity} warning={e.WarningSol}");
            };
            sim.Events.WeatherPeaked += e => peaked++;
            sim.Events.WeatherEnded += e => ended++;
            sim.Tick(GameConstants.RunDurationSeconds);
            Assert.That(sim.State.Alive, Is.True);
            Assert.That(sim.State.Sol, Is.EqualTo(GameConstants.MissionDurationDays));
            Assert.That(weather.Events.Count, Is.EqualTo(expected));
            int expectedStarted = 0, expectedPeaked = 0, expectedEnded = 0, expectedWarned = 0;
            foreach (var item in weather.Events)
            {
                if (item.StartSol <= GameConstants.MissionDurationDays) expectedStarted++;
                if (item.PeakSol <= GameConstants.MissionDurationDays) expectedPeaked++;
                if (item.EndSol <= GameConstants.MissionDurationDays) expectedEnded++;
                if (item.WarningSol <= GameConstants.MissionDurationDays) expectedWarned++;
            }
            Assert.That(started, Is.EqualTo(expectedStarted));
            Assert.That(peaked, Is.EqualTo(expectedPeaked));
            Assert.That(ended, Is.EqualTo(expectedEnded));
            Assert.That(warned, Is.EqualTo(expectedWarned));
        }
    }
}
