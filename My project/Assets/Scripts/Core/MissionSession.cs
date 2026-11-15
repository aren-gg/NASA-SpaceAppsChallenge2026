using System;
using MarsTrainer.Data;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using MarsTrainer.Sim.Systems;

namespace MarsTrainer.Core
{
    // Shared composition for the player and headless bots. No alternate balance model.
    public sealed class MissionSession
    {
        public Simulation Simulation { get; }
        public Inventory Inventory { get; } = new Inventory();
        public EnergySystem Energy { get; }
        public FarmSystem Farm { get; }
        public WeatherSystem Weather { get; }
        public ResearchSystem Research { get; }
        public ResearchNode[] Catalog { get; }
        public bool AtBase { get; set; } = true;
        public bool Sheltered { get; set; } = true;
        private readonly FarmCrop[] crops;

        public MissionSession(DataRepository data, int seed, ResearchNode[] catalog = null, double runDurationSeconds = GameConstants.RunDurationSeconds)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Simulation = new Simulation(runDurationSeconds);
            var sim = Simulation;
            var random = new Random(seed);
            int offset = random.Next(data.Schedule.Count);
            double doseScale = 1 + (random.NextDouble() * 2 - 1) * GameConstants.MissionDoseVariation;
            var rows = new WeatherConditions[data.Schedule.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = data.Schedule[(i + offset) % rows.Length];
                rows[i] = new WeatherConditions(row.ls, row.dustOpacity, row.minTemp, row.maxTemp,
                    row.gcrDose_uGy * doseScale, row.flareLevel, row.auroraAlert);
            }
            Weather = new WeatherSystem(sim.Clock, sol => rows[sol], rows.Length,
                sim.Modifiers, sim.Events, seed, () => Sheltered);
            Energy = new EnergySystem(sim.Clock, Weather.EnergyAt,
                new SolarPanel(GameConstants.MissionPanels, GameConstants.MissionPanelArea, GameConstants.MissionPanelEfficiency),
                new Battery(GameConstants.MissionBatteryKWh, GameConstants.MissionTransferKW, GameConstants.MissionTransferKW,
                    GameConstants.MissionBatteryEfficiency, GameConstants.MissionBatteryKWh),
                new EnergyLoads(GameConstants.LifeSupportKWhPerSol, GameConstants.SuitShieldKWhPerSol,
                    0, 0, GameConstants.MissionHeating, GameConstants.FarmOptimalTemperature), sim.Modifiers, sim.Events);
            sim.RegisterSystem(Weather); sim.RegisterSystem(Energy);
            sim.RegisterSystem(new RadiationSystem(sim.Clock, Weather.RadiationAt, sim.Modifiers, sim.Events, () => Energy.ShieldPowerFraction));
            crops = new FarmCrop[data.Crops.Count];
            for (int i = 0; i < crops.Length; i++)
            {
                var crop = data.Crops[i];
                crops[i] = new FarmCrop(crop.id, crop.name, crop.daysToHarvest, crop.kcalPerHarvest,
                    crop.parSat, crop.waterLPerDay, crop.co2Ppm, crop.lightHours);
            }
            Farm = new FarmSystem(sim.Clock, crops, Inventory, sim.Events,
                sol => new FarmConditions(GameConstants.MissionGreenhousePar,
                    rows[sol].Tau * GameConstants.MissionGreenhouseDustFraction,
                    GameConstants.CropCo2Ppm, GameConstants.FarmOptimalTemperature), DrawPower);
            sim.RegisterSystem(Farm);
            Inventory.Add(new SpaceSnack("Landing provisions", GameConstants.MissionFoodSols * GameConstants.AstronautKcalPerSol));
            Inventory.AddWater(GameConstants.MissionWaterLitres);
            sim.EnableSurvival(Inventory, Energy);
            Catalog = catalog ?? Array.Empty<ResearchNode>();
            Research = new ResearchSystem(Catalog, sim.Modifiers); sim.RegisterSystem(Research);
            sim.State.EnergyKWh = sim.State.BatteryCapKWh = GameConstants.MissionBatteryKWh;
        }

        private double DrawPower(double requested)
        {
            double amount = Math.Min(requested, Math.Max(0, Simulation.State.EnergyKWh));
            return Energy.TrySpend(Simulation.State, amount) ? amount : 0;
        }

        public void TendFarm()
        {
            FarmCrop best = crops[0];
            for (int i = 1; i < crops.Length; i++)
                if (crops[i].KcalPerHarvest / crops[i].DaysToHarvest > best.KcalPerHarvest / best.DaysToHarvest) best = crops[i];
            for (int i = 0; i < Farm.Plots.Count; i++)
            {
                var plot = Farm.Plots[i];
                if (plot.Ready) Farm.Harvest(i);
                if (plot.Crop == null) Farm.Plant(i, best.Id);
                double water = Math.Max(0, best.WaterLitresPerSol * Farm.UnitsPerPlot * GameConstants.MissionFarmWaterSols - plot.WaterLitres);
                if (Inventory.TryConsumeWater(water)) Farm.Water(i, water);
            }
        }
    }
}
