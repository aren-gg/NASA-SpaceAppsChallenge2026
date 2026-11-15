using System;
using System.Collections.Generic;

namespace MarsTrainer.Sim.Systems
{
    public sealed class FarmSystem : ISimSystem
    {
        private readonly SimClock clock;
        private readonly Dictionary<string, FarmCrop> crops = new Dictionary<string, FarmCrop>(StringComparer.Ordinal);
        private readonly List<FarmPlot> plots = new List<FarmPlot>();
        private readonly SimEvents events;
        private readonly Func<int, FarmConditions> conditions;
        private readonly Func<double, double> drawPower;
        public int Order => GameConstants.FarmSystemOrder;
        public IReadOnlyList<FarmPlot> Plots { get; }
        public Inventory Inventory { get; }
        public int UnitsPerPlot { get; }
        public double LampEnergyKWh { get; private set; }

        // drawPower must debit the power store and return delivered kWh (zero during blackout).
        public FarmSystem(SimClock clock, IReadOnlyList<FarmCrop> crops, Inventory inventory,
            SimEvents events, Func<int, FarmConditions> conditions, Func<double, double> drawPower,
            int initialPlots = GameConstants.FarmStartingPlots, int unitsPerPlot = GameConstants.FarmUnitsPerPlot)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.drawPower = drawPower ?? throw new ArgumentNullException(nameof(drawPower));
            if (crops == null) throw new ArgumentNullException(nameof(crops));
            if (initialPlots < 0 || unitsPerPlot <= 0) throw new ArgumentOutOfRangeException(nameof(initialPlots));
            for (int i = 0; i < crops.Count; i++) this.crops.Add(crops[i].Id, crops[i]);
            UnitsPerPlot = unitsPerPlot;
            Plots = plots.AsReadOnly();
            for (int i = 0; i < initialPlots; i++) plots.Add(new FarmPlot());
        }

        public int AddPlot()
        {
            int index = plots.Count;
            plots.Add(new FarmPlot());
            events.ReportFarmPlot(index);
            return index;
        }

        public bool Plant(int index, string cropId)
        {
            var plot = plots[index];
            if (plot.Crop != null || cropId == null || !crops.TryGetValue(cropId, out var crop)) return false;
            plot.Crop = crop;
            plot.Growth = 0;
            events.ReportFarmPlot(index);
            return true;
        }

        public void Water(int index, double litres)
        {
            Battery.ValidateNonnegative(litres, nameof(litres));
            plots[index].WaterLitres += litres;
            events.ReportFarmPlot(index);
        }

        public void SetLamps(int index, bool enabled)
        {
            plots[index].LampsEnabled = enabled;
            events.ReportFarmPlot(index);
        }

        public bool Harvest(int index)
        {
            var plot = plots[index];
            if (!plot.Ready) return false;
            var snack = new SpaceSnack(plot.Crop.Name, plot.Crop.KcalPerHarvest * UnitsPerPlot);
            Inventory.Add(snack);
            plot.Crop = null;
            plot.Growth = 0;
            events.ReportFarmPlot(index);
            events.ReportHarvest(index, snack);
            return true;
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Battery.ValidateNonnegative(dtSol, nameof(dtSol));
            if (dtSol == 0) return;
            var environment = conditions(clock.Sol);
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot.Crop == null || plot.Ready) continue;
                var crop = plot.Crop;
                double lampPar = 0;
                if (plot.LampsEnabled)
                {
                    double requested = GameConstants.FarmLampKWPerUnit * UnitsPerPlot * crop.LightHours * dtSol;
                    double delivered = drawPower(requested);
                    Battery.ValidateNonnegative(delivered, nameof(drawPower));
                    if (delivered > requested) throw new InvalidOperationException("Lamp power exceeds demand.");
                    LampEnergyKWh += delivered;
                    lampPar = crop.ParSaturation * delivered / requested;
                }
                double waterDemand = crop.WaterLitresPerSol * UnitsPerPlot * dtSol;
                double waterUsed = Math.Min(plot.WaterLitres, waterDemand);
                double light = Math.Min(1, (environment.Par * Math.Exp(-environment.DustOpacity) + lampPar) / crop.ParSaturation);
                double co2 = Math.Min(1, environment.Co2Ppm / crop.Co2Ppm);
                double temperature = Math.Max(0, 1 - Math.Abs(environment.Temperature - GameConstants.FarmOptimalTemperature)
                    / GameConstants.FarmTemperatureTolerance);
                plot.Growth = Math.Min(1, plot.Growth + dtSol / crop.DaysToHarvest * light
                    * (waterUsed / waterDemand) * co2 * temperature);
                plot.WaterLitres -= waterUsed;
                events.ReportFarmPlot(i);
            }
        }
    }
}
