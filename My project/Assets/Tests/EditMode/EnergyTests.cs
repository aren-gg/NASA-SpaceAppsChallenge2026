using System;
using System.Collections.Generic;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class EnergyTests
    {
        [TestCase(0)] [TestCase(0.2)] [TestCase(0.25)] [TestCase(0.75)] [TestCase(0.9)]
        public void NightGeneratesNothing(double hour)
        {
            Assert.That(new SolarPanel(1, 10, 0.2).PowerKW(hour, 0, 0, new SimModifiers()), Is.Zero);
        }

        [Test]
        public void NoonOrbitDustAndUpgradesMatchFormula()
        {
            var panel = new SolarPanel(2, 10, 0.2);
            var modifiers = new SimModifiers();
            // Perihelion: r = a(1-e), normal incidence.
            double r = 1.523679 * (1 - 0.0934);
            double clear = 4 * 1361 / (r * r) / 1000;
            Assert.That(panel.PowerKW(0.5, 0, 251, modifiers), Is.EqualTo(clear).Within(1e-10));
            Assert.That(panel.PowerKW(0.5, 5, 251, modifiers), Is.LessThan(clear * 0.1));
            modifiers.Set(GameConstants.PanelEfficiencyStat, "upgrade", multiplier: 2);
            Assert.That(panel.PowerKW(0.5, 0, 251, modifiers), Is.EqualTo(clear * 2).Within(1e-10));
            Assert.That(panel.PowerKW(0.5, 0, 71, modifiers), Is.LessThan(clear * 2));
        }

        [Test]
        public void BatteryClampsRatesCapacityAndRoundTripLoss()
        {
            var battery = new Battery(10, 4, 2, 0.81, 100);
            Assert.That(battery.StoredKWh, Is.EqualTo(10));
            Assert.That(battery.Discharge(100, 1), Is.EqualTo(2));
            Assert.That(battery.StoredKWh, Is.EqualTo(10 - 2 / 0.9).Within(1e-10));
            Assert.That(battery.Charge(100, 1), Is.EqualTo((2 / 0.9) / 0.9).Within(1e-10));
            Assert.That(battery.StoredKWh, Is.EqualTo(10).Within(1e-10));
            var empty = new Battery(10, 4, 2, 0.81, -10);
            Assert.That(empty.Discharge(1, 1), Is.Zero);
            Assert.That(empty.Charge(100, 0.5), Is.EqualTo(2));
            Assert.That(empty.Discharge(100, 10), Is.EqualTo(1.62).Within(1e-10));
            Assert.That(empty.StoredKWh, Is.Zero.Within(1e-10));
            Assert.Throws<ArgumentOutOfRangeException>(() => empty.Charge(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Battery(10, 1, 1, 0));
        }

        [TestCase(4.5, EnergyLoad.Lights)]
        [TestCase(3.5, EnergyLoad.Lights | EnergyLoad.Heating)]
        [TestCase(2.5, EnergyLoad.Lights | EnergyLoad.Heating | EnergyLoad.Medbay)]
        [TestCase(1.5, EnergyLoad.Lights | EnergyLoad.Heating | EnergyLoad.Medbay | EnergyLoad.Shield)]
        [TestCase(0.5, EnergyLoad.Lights | EnergyLoad.Heating | EnergyLoad.Medbay | EnergyLoad.Shield | EnergyLoad.LifeSupport)]
        public void ShedsInSpecifiedOrder(double supply, EnergyLoad shed)
        {
            var sim = new Simulation();
            var energy = new EnergySystem(sim.Clock, sol => new EnergyConditions(0, 0, 0),
                new SolarPanel(0, 1, 1), new Battery(supply, 100, supply / 24.6597, 1, supply),
                new EnergyLoads(1, 1, 1, 1, 1, 1), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(energy.LastReport.ShedLoads, Is.EqualTo(shed));
            Assert.That(energy.LastReport.LoadKWh, Is.EqualTo(5).Within(1e-10));
            Assert.That(energy.LastReport.ServedKWh, Is.EqualTo(supply).Within(1e-10));
        }

        [Test]
        public void TenSolScriptMatchesHandLedgerAndEmitsReportsAndBrownouts()
        {
            // Night-independent ledger with zero panels: 10 kWh initially, 2 kWh/sol.
            // Sols 0..4 serve 2, battery 8,6,4,2,0. Sols 5..9 shed all 2.
            var sim = new Simulation();
            var reports = new List<EnergyReport>();
            int brownouts = 0;
            sim.Events.EnergyReported += reports.Add;
            sim.Events.Brownout += () => brownouts++;
            var energy = new EnergySystem(sim.Clock, sol => new EnergyConditions(sol * 30, sol % 2 == 0 ? 0 : 5, -10),
                new SolarPanel(0, 1, 1), new Battery(10, 10, 10, 1, 10),
                new EnergyLoads(1, 0, 0, 0, 0.1, 0), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            for (int i = 0; i < 10; i++)
            {
                sim.Tick(sim.Clock.SecondsPerSol / 2);
                sim.Tick(sim.Clock.SecondsPerSol / 2);
            }
            Assert.That(reports.Count, Is.EqualTo(10));
            for (int i = 0; i < 10; i++)
            {
                Assert.That(reports[i].Sol, Is.EqualTo(i));
                Assert.That(reports[i].GenerationKWh, Is.Zero);
                Assert.That(reports[i].LoadKWh, Is.EqualTo(2).Within(1e-9));
                Assert.That(reports[i].NetKWh, Is.EqualTo(-2).Within(1e-9));
                Assert.That(reports[i].BatteryPercent, Is.EqualTo(Math.Max(0, 80 - 20 * i)).Within(1e-8));
                Assert.That(reports[i].ServedKWh, Is.EqualTo(i < 5 ? 2 : 0).Within(1e-8));
            }
            Assert.That(sim.State.EnergyKWh, Is.Zero.Within(1e-8));
            Assert.That(brownouts, Is.EqualTo(1));
        }

        [Test]
        public void SolarEnergyAndStorageAreIndependentOfTickPartition()
        {
            EnergyReport coarse = RunSolar(1);
            EnergyReport fine = RunSolar(100);
            // Clear sine integral: A*eta*G0 * solHours / pi / 1000.
            double r = 1.523679 * (1 - 0.0934);
            double expected = 1361 / (r * r) * 24.6597 / Math.PI / 1000;
            Assert.That(coarse.GenerationKWh, Is.EqualTo(expected).Within(0.002));
            Assert.That(fine.GenerationKWh, Is.EqualTo(coarse.GenerationKWh).Within(1e-9));
            Assert.That(fine.BatteryPercent, Is.EqualTo(coarse.BatteryPercent).Within(1e-9));
        }

        [Test]
        public void TenClearSolsMatchAnalyticOrbitAndChargingLedger()
        {
            // Integral of the daylight sine is 1/pi sol. Alternate perihelion/aphelion.
            // g = 1361 * 24.6597 / (1000*pi*r^2); stored += 0.9*g.
            double[] daily = { 5.598576901241277, 3.84902682938511 };
            var sim = new Simulation();
            var reports = new List<EnergyReport>();
            sim.Events.EnergyReported += reports.Add;
            var energy = new EnergySystem(sim.Clock,
                sol => new EnergyConditions(sol % 2 == 0 ? 251 : 71, 0, 20),
                new SolarPanel(1, 1, 1), new Battery(100, 100, 100, 0.81),
                new EnergyLoads(0, 0, 0, 0, 1, 10), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            for (int part = 0; part < 70; part++) sim.Tick(sim.Clock.SecondsPerSol / 7);
            Assert.That(reports.Count, Is.EqualTo(10));
            double stored = 0;
            for (int i = 0; i < 10; i++)
            {
                stored += daily[i % 2] * 0.9;
                Assert.That(reports[i].GenerationKWh, Is.EqualTo(daily[i % 2]).Within(0.002));
                Assert.That(reports[i].NetKWh, Is.EqualTo(daily[i % 2]).Within(0.002));
                Assert.That(reports[i].BatteryPercent, Is.EqualTo(stored).Within(0.01));
                Assert.That(reports[i].LoadKWh, Is.Zero);
            }
        }

        private static EnergyReport RunSolar(int parts)
        {
            var sim = new Simulation();
            var energy = new EnergySystem(sim.Clock, sol => new EnergyConditions(251, 0, 0),
                new SolarPanel(1, 1, 1), new Battery(100, 100, 100, 1),
                new EnergyLoads(0, 0, 0, 0, 0, 0), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            for (int i = 0; i < parts; i++) sim.Tick(sim.Clock.SecondsPerSol / parts);
            return energy.LastReport;
        }
    }
}
