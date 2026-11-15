using System;
using MarsTrainer.Data;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using UnityEngine;

namespace MarsTrainer.Gameplay.Farm
{
    // Composition/input adapter. The Simulation clock owns all growth updates.
    public sealed class FarmInteract : MonoBehaviour
    {
        private Simulation simulation;
        private EnergySystem energy;
        private bool registered;
        private bool ownsFarm;
        public FarmSystem Farm { get; private set; }
        public SimEvents Events => simulation?.Events;

        // Repository.Load() should complete before binding, to use bundled JSON or its defaults.
        public void Initialize(Simulation simulation, DataRepository repository,
            Func<int, FarmConditions> conditions, EnergySystem energy)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            if (conditions == null) throw new ArgumentNullException(nameof(conditions));
            Unregister();
            this.simulation = simulation;
            ownsFarm = true;
            this.energy = energy;
            var crops = new FarmCrop[repository.Crops.Count];
            for (int i = 0; i < crops.Length; i++)
            {
                var dto = repository.Crops[i];
                crops[i] = new FarmCrop(dto.id, dto.name, dto.daysToHarvest, dto.kcalPerHarvest,
                    dto.parSat, dto.waterLPerDay, dto.co2Ppm, dto.lightHours);
            }
            Farm = new FarmSystem(simulation.Clock, crops, new Inventory(), simulation.Events, conditions, DrawPower);
            if (isActiveAndEnabled) Register();
        }

        // Scene views borrow the mission farm; its composition root owns registration/inventory.
        public void Bind(Simulation source, FarmSystem sharedFarm)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sharedFarm == null) throw new ArgumentNullException(nameof(sharedFarm));
            Unregister();
            simulation = source; Farm = sharedFarm; ownsFarm = false;
        }

        public bool Plant(int plot, string cropId) { return Farm.Plant(plot, cropId); }
        public void Water(int plot, double litres) { Farm.Water(plot, litres); }
        public bool Harvest(int plot) { return Farm.Harvest(plot); }
        public void SetLamps(int plot, bool enabled) { Farm.SetLamps(plot, enabled); }
        public int AddPlot() { return Farm.AddPlot(); }

        private double DrawPower(double requested)
        {
            double available = Math.Min(requested, Math.Max(0, simulation.State.EnergyKWh));
            return energy.TrySpend(simulation.State, available) ? available : 0;
        }

        private void OnEnable() { Register(); }
        private void OnDisable() { Unregister(); }
        private void Register()
        {
            if (!ownsFarm || Farm == null || registered) return;
            simulation.RegisterSystem(Farm);
            registered = true;
        }
        private void Unregister()
        {
            if (!registered) return;
            simulation.UnregisterSystem(Farm);
            registered = false;
        }
    }
}
