using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using MarsTrainer.Gameplay.Radiation;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class RadiationTests
    {
        private static RadiationSystem AddRadiation(Simulation sim, double gcr = 210, int flare = 0)
        {
            var radiation = new RadiationSystem(sim.Clock, _ => new RadiationConditions(gcr, flare),
                sim.Modifiers, sim.Events);
            sim.RegisterSystem(radiation);
            return radiation;
        }

        [Test]
        public void DoseMathStacksShieldingAndClampsAtNinetyPercent()
        {
            var sim = new Simulation();
            var radiation = AddRadiation(sim, 210, 2);
            radiation.SuitShield = 0.2;
            sim.Modifiers.Set(GameConstants.RadiationShieldStat, "research", 0.1);
            sim.Modifiers.Set(GameConstants.RadiationShieldStat, "cave", 0.3);
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            Assert.That(sim.State.RadiationDoseMSv, Is.EqualTo((210 + GameConstants.FlareMediumDoseUGy) * 0.4 * 3.1 / 1000 * 0.5).Within(1e-10));
            sim.Modifiers.Set(GameConstants.RadiationShieldStat, "cave", 2);
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            Assert.That(radiation.DoseRateMSvPerSol, Is.EqualTo((210 + GameConstants.FlareMediumDoseUGy) * 0.1 * 3.1 / 1000).Within(1e-10));
        }

        [TestCase(0.499, SuitShieldState.Safe)]
        [TestCase(0.5, SuitShieldState.Warn)]
        [TestCase(0.8, SuitShieldState.Danger)]
        [TestCase(1, SuitShieldState.Critical)]
        public void ThresholdEventsFireOnlyOnChange(double fraction, SuitShieldState expected)
        {
            var sim = new Simulation();
            var radiation = AddRadiation(sim, 0);
            radiation.SuitShield = 0;
            int changes = 0;
            sim.Events.SuitShieldChanged += _ => changes++;
            sim.State.RadiationDoseMSv = fraction * GameConstants.H_limit;
            sim.Tick(sim.Clock.SecondsPerSol / 4);
            sim.Tick(sim.Clock.SecondsPerSol / 4);
            Assert.That(radiation.ShieldState, Is.EqualTo(expected));
            Assert.That(changes, Is.EqualTo(expected == SuitShieldState.Safe ? 0 : 1));
        }

        [Test]
        public void HealthDrainIncreasesWithDoseAndIsTickIndependent()
        {
            double danger = HealthAfter(0.8, 1);
            double critical = HealthAfter(1.2, 1);
            Assert.That(danger, Is.EqualTo(100));
            Assert.That(critical, Is.LessThan(danger));
            Assert.That(HealthAfter(0.79, 1), Is.EqualTo(HealthAfter(0.79, 100)).Within(1e-8));
        }

        private static double HealthAfter(double fraction, int parts)
        {
            var sim = new Simulation();
            var radiation = AddRadiation(sim, fraction == 0.8 ? 0 : 210, fraction == 0.8 ? 0 : 3);
            radiation.SuitShield = 0;
            sim.State.RadiationDoseMSv = fraction * GameConstants.H_limit;
            for (int i = 0; i < parts; i++) sim.Tick(sim.Clock.SecondsPerSol / parts);
            return sim.State.Health;
        }

        [Test]
        public void ShelterBonusEndsOnDisableAndOverlappingZonesKeepTheirBonus()
        {
            var sim = new Simulation();
            var radiation = AddRadiation(sim, 100);
            radiation.SuitShield = 0;
            var player = new GameObject("player");
            var a = new GameObject("cave");
            var b = new GameObject("base");
            try
            {
                var collider = player.AddComponent<BoxCollider>();
                var first = a.AddComponent<ShelterZone>();
                var second = b.AddComponent<ShelterZone>();
                first.Initialize(sim.Modifiers, player.transform);
                second.Initialize(sim.Modifiers, player.transform);
                Invoke(first, "OnTriggerEnter", collider);
                Invoke(first, "OnTriggerEnter", collider);
                Invoke(second, "OnTriggerEnter", collider);
                sim.Tick(sim.Clock.SecondsPerSol / 4);
                Assert.That(radiation.DoseRateMSvPerSol, Is.EqualTo(0.031).Within(1e-10));
                // EditMode does not dispatch Play Mode lifecycle callbacks.
                first.enabled = false;
                Invoke(first, "OnDisable");
                sim.Tick(sim.Clock.SecondsPerSol / 4);
                Assert.That(radiation.DoseRateMSvPerSol, Is.EqualTo(0.31 * (1 - GameConstants.ShelterShield)).Within(1e-10));
                Invoke(second, "OnTriggerExit", collider);
                sim.Tick(sim.Clock.SecondsPerSol / 4);
                Assert.That(radiation.DoseRateMSvPerSol, Is.EqualTo(0.31).Within(1e-10));
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(player); }
        }

        // Dispatch physics/lifecycle callbacks directly: EditMode has no running player loop.
        private static void Invoke(object target, string method, params object[] args)
        {
            target.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(target, args);
        }

        [Test]
        public void ScriptedFlareSolCalculatesDoseAndFiresDroneOnce()
        {
            var sim = new Simulation();
            var radiation = AddRadiation(sim, 210, 3);
            radiation.SuitShield = 0.2;
            sim.State.RadiationDoseMSv = GameConstants.H_limit * 0.79;
            int flights = 0, events = 0;
            sim.Events.RecallLaunched += () => events++;
            using (var drone = new RecallDroneModel(sim.Clock, sim.Events, () => { flights++; return true; }))
            using (var meter = new SunZapMeterModel(sim.State, sim.Events))
            {
                sim.RegisterSystem(drone);
                for (int i = 0; i < 100; i++) sim.Tick(sim.Clock.SecondsPerSol / 100);
                Assert.That(sim.State.RadiationDoseMSv, Is.EqualTo(GameConstants.H_limit * 0.79 + (210 + GameConstants.FlareHighDoseUGy) * 0.8 * 3.1 / 1000).Within(1e-8));
                Assert.That(meter.CumulativeMSv, Is.EqualTo(sim.State.RadiationDoseMSv));
                Assert.That(meter.DoseRateMSvPerSol, Is.EqualTo((210 + GameConstants.FlareHighDoseUGy) * 0.8 * 3.1 / 1000).Within(1e-10));
                Assert.That(flights, Is.EqualTo(1));
                Assert.That(events, Is.EqualTo(1));
            }
        }

        [Test]
        public void DroneRetriesUnavailableLaunchAndRespectsCooldownAcrossAlerts()
        {
            var sim = new Simulation();
            bool available = false;
            int flights = 0;
            using (var drone = new RecallDroneModel(sim.Clock, sim.Events, () => { if (!available) return false; flights++; return true; }))
            {
                sim.RegisterSystem(drone);
                sim.Events.ReportFlareAlert(0, 1);
                sim.Tick(sim.Clock.SecondsPerSol / 10);
                Assert.That(flights, Is.Zero);
                available = true;
                sim.Tick(sim.Clock.SecondsPerSol / 10);
                sim.Events.ReportFlareAlert(0, 1);
                sim.Tick(sim.Clock.SecondsPerSol / 10);
                Assert.That(flights, Is.EqualTo(1));
                sim.Events.ReportFlareAlert(1, 1);
                sim.Tick(sim.Clock.SecondsPerSol * GameConstants.RecallCooldownSols);
                sim.Tick(sim.Clock.SecondsPerSol / 10);
                Assert.That(flights, Is.EqualTo(2));
            }
        }

        [TestCase(0, 0)]
        [TestCase(0.25, 0.5)]
        public void PowerLossRemovesOnlyTheUnpoweredSuitBonus(double stored, double powered)
        {
            var sim = new Simulation();
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 0),
                new SolarPanel(0, 1, 1), new Battery(10, 100, 100, 1, stored),
                new EnergyLoads(0, GameConstants.SuitShieldKWhPerSol, 0, 0, 0, 0), sim.Modifiers, sim.Events);
            var radiation = new RadiationSystem(sim.Clock, _ => new RadiationConditions(210, 0),
                sim.Modifiers, sim.Events, () => energy.ShieldPowerFraction);
            sim.Modifiers.Set(GameConstants.RadiationShieldStat, "cave", 0.3);
            sim.RegisterSystem(energy);
            sim.RegisterSystem(radiation);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(energy.ShieldPowerFraction, Is.EqualTo(powered).Within(1e-9));
            Assert.That(sim.State.RadiationDoseMSv,
                Is.EqualTo(210 * (1 - 0.3 - 0.2 * powered) * 3.1 / 1000).Within(1e-9));
        }

        [Test]
        public void DroneComponentChoosesNearestShelterPaysOnceAndPausesFlight()
        {
            var sim = new Simulation();
            var battery = new Battery(10, 100, 100, 1, 10);
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 0),
                new SolarPanel(0, 1, 1), battery, new EnergyLoads(0, 0, 0, 0, 0, 0), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            AddRadiation(sim, 210, 1);
            var player = new GameObject("player");
            var near = new GameObject("near");
            var far = new GameObject("far");
            var host = new GameObject("drone");
            var drone = host.AddComponent<RecallDrone>();
            try
            {
                far.transform.position = Vector3.right * 100;
                var nearest = near.AddComponent<ShelterZone>();
                var distant = far.AddComponent<ShelterZone>();
                drone.Initialize(sim, energy, player.transform, new[] { distant, nearest });
                int launches = 0;
                sim.Events.RecallLaunched += () => launches++;
                sim.Tick(sim.Clock.SecondsPerSol / 4);
                Assert.That(drone.Flying, Is.True);
                Assert.That(battery.StoredKWh, Is.EqualTo(10 - GameConstants.RecallEnergyKWh));
                sim.Clock.Paused = true;
                Invoke(drone, "Update");
                Assert.That(drone.Flying, Is.True);
                sim.Clock.Paused = false;
                Invoke(drone, "Update");
                Assert.That(drone.Flying, Is.False, "Nearest arrival is the player's position; far shelter would still be in flight.");
                sim.Tick(sim.Clock.SecondsPerSol / 4);
                Assert.That(launches, Is.EqualTo(1));
                int count = sim.Systems.Count;
                Invoke(drone, "OnDisable");
                Assert.That(sim.Systems.Count, Is.EqualTo(count - 1));
            }
            finally
            {
                Invoke(drone, "OnDisable");
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(near);
                UnityEngine.Object.DestroyImmediate(far);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void ShieldDrawsEnergyAndRecallCostUsesTheSameBattery()
        {
            var sim = new Simulation();
            var battery = new Battery(10, 100, 100, 1, 10);
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 0),
                new SolarPanel(0, 1, 1), battery, new EnergyLoads(0, GameConstants.SuitShieldKWhPerSol, 0, 0, 0, 0), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(battery.StoredKWh, Is.EqualTo(10 - GameConstants.SuitShieldKWhPerSol).Within(1e-9));
            Assert.That(energy.TrySpend(sim.State, GameConstants.RecallEnergyKWh), Is.True);
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(battery.StoredKWh));
            Assert.That(energy.TrySpend(sim.State, 100), Is.False);
        }
    }
}
