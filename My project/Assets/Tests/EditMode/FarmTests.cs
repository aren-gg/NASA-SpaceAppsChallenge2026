using System;
using System.IO;
using MarsTrainer.Data;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class FarmTests
    {
        private static FarmCrop Crop(double days = 2, double kcal = 20) =>
            new FarmCrop("test", "Space greens", days, kcal, 400, 1, 1000, 16);

        private static FarmSystem Farm(Simulation sim, FarmCrop crop, FarmConditions conditions,
            Func<double, double> power = null, int units = 1)
        {
            var farm = new FarmSystem(sim.Clock, new[] { crop }, new Inventory(), sim.Events,
                _ => conditions, power ?? (requested => requested), unitsPerPlot: units);
            sim.RegisterSystem(farm);
            farm.Plant(0, crop.Id);
            return farm;
        }

        [Test]
        public void GrowthMultipliesLightWaterCo2AndTemperatureFactors()
        {
            var sim = new Simulation();
            var farm = Farm(sim, Crop(), new FarmConditions(200, 0, 500, 15));
            farm.Water(0, 0.5);
            sim.Tick(sim.Clock.SecondsPerSol);
            // base .5 * PAR .5 * water .5 * CO2 .5 * temperature .5.
            Assert.That(farm.Plots[0].Growth, Is.EqualTo(0.03125).Within(1e-10));
            Assert.That(farm.Plots[0].WaterLitres, Is.Zero);
        }

        [TestCase(0, 0, 0)]
        [TestCase(400, 0, 0.5)]
        [TestCase(400, 2, 0.06766764161830635)]
        public void UnlitPlotsRespondToLightStarvationAndDust(double par, double dust, double growth)
        {
            var sim = new Simulation();
            var farm = Farm(sim, Crop(), new FarmConditions(par, dust, 1000, 20));
            farm.Water(0, 1);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(farm.Plots[0].Growth, Is.EqualTo(growth).Within(1e-10));
        }

        [TestCase(true)] [TestCase(false)]
        public void LampsPayForEnergyAndBlackoutStopsArtificialLight(bool powered)
        {
            var sim = new Simulation();
            double spent = 0;
            var farm = Farm(sim, Crop(), new FarmConditions(0, 0, 1000, 20),
                requested => { if (!powered) return 0; spent += requested; return requested; });
            farm.SetLamps(0, true);
            farm.Water(0, 1);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(farm.Plots[0].Growth, Is.EqualTo(powered ? 0.5 : 0).Within(1e-10));
            Assert.That(spent, Is.EqualTo(powered ? GameConstants.FarmLampKWPerUnit * 16 : 0).Within(1e-10));
        }

        [Test]
        public void HarvestAddsNamedSnacksOnceAndPlotCanBeReplantedAndUpgraded()
        {
            var sim = new Simulation();
            var farm = Farm(sim, Crop(), new FarmConditions(400, 0, 1000, 20), units: 3);
            Assert.That(farm.Plots.Count, Is.EqualTo(2));
            Assert.That(farm.Harvest(0), Is.False);
            Assert.That(farm.Plant(0, "test"), Is.False);
            farm.Water(0, 6);
            sim.Tick(sim.Clock.SecondsPerSol * 2);
            int harvests = 0;
            sim.Events.FarmHarvested += (plot, snack) => harvests++;
            Assert.That(farm.Harvest(0), Is.True);
            Assert.That(farm.Harvest(0), Is.False);
            Assert.That(farm.Inventory.FoodKcal, Is.EqualTo(60));
            Assert.That(farm.Inventory.Snacks[0].Name, Is.EqualTo("Space greens"));
            Assert.That(farm.Inventory.Snacks[0].Kcal, Is.EqualTo(60));
            Assert.That(harvests, Is.EqualTo(1));
            Assert.That(farm.Plant(0, "test"), Is.True);
            Assert.That(farm.AddPlot(), Is.EqualTo(2));
            Assert.That(farm.Plots.Count, Is.EqualTo(3));
        }

        [Test]
        public void WaterLimitedGrowthAndLampCostDoNotDependOnTickPartition()
        {
            double Run(int parts, out double cost)
            {
                var sim = new Simulation();
                double spent = 0;
                var farm = Farm(sim, Crop(), new FarmConditions(0, 0, 1000, 20),
                    requested => { spent += requested; return requested; });
                farm.SetLamps(0, true);
                farm.Water(0, 0.5);
                for (int i = 0; i < parts; i++) sim.Tick(sim.Clock.SecondsPerSol / parts);
                cost = spent;
                return farm.Plots[0].Growth;
            }
            Assert.That(Run(1, out var coarseCost), Is.EqualTo(Run(64, out var fineCost)).Within(1e-10));
            Assert.That(coarseCost, Is.EqualTo(fineCost).Within(1e-10));
        }

        [Test]
        public void ThirtySolScriptFromBundledCropDataFeedsOneAstronaut()
        {
            var repository = new DataRepository();
            repository.LoadCropsJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "crop_parameters.json")));
            var dto = repository.Crops[1];
            var crop = new FarmCrop(dto.id, dto.name, dto.daysToHarvest, dto.kcalPerHarvest,
                dto.parSat, dto.waterLPerDay, dto.co2Ppm, dto.lightHours);
            var sim = new Simulation();
            // Finite, precharged store: the farm must debit every lamp kWh.
            const double initialEnergy = 2000;
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 20),
                new SolarPanel(0, 1, 1), new Battery(initialEnergy, initialEnergy, initialEnergy, 1, initialEnergy),
                new EnergyLoads(0, 0, 0, 0, 0, 20), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            var farm = new FarmSystem(sim.Clock, new[] { crop }, new Inventory(), sim.Events,
                _ => new FarmConditions(0, 2, crop.Co2Ppm, GameConstants.FarmOptimalTemperature),
                requested => energy.TrySpend(sim.State, requested) ? requested : 0);
            sim.RegisterSystem(farm);
            for (int plot = 0; plot < farm.Plots.Count; plot++)
            {
                farm.Plant(plot, crop.Id);
                farm.SetLamps(plot, true);
            }
            for (int sol = 0; sol < 30; sol++)
            {
                for (int plot = 0; plot < farm.Plots.Count; plot++)
                    farm.Water(plot, crop.WaterLitresPerSol * farm.UnitsPerPlot);
                sim.Tick(sim.Clock.SecondsPerSol);
                for (int plot = 0; plot < farm.Plots.Count; plot++) farm.Harvest(plot);
            }
            Assert.That(farm.Inventory.FoodKcal, Is.GreaterThanOrEqualTo(30 * GameConstants.AstronautKcalPerSol));
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(initialEnergy - farm.LampEnergyKWh).Within(1e-8));
            TestContext.WriteLine("30-sol harvest: " + farm.Inventory.FoodKcal + " kcal; lamp energy: " + farm.LampEnergyKWh + " kWh");
        }
    }
}
