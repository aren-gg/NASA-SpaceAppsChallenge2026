using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using UnityEngine;

namespace MarsTrainer.Gameplay.Radiation
{
    // Composition root calls Initialize with the player's shelter list; no scene search or singleton.
    public sealed class RecallDrone : MonoBehaviour
    {
        private Simulation simulation;
        private EnergySystem energy;
        private Transform player;
        private ShelterZone[] shelters;
        private RecallDroneModel model;
        private ShelterZone destination;
        private Action<bool> flightChanged;
        public bool Flying => destination != null;

        public void Initialize(Simulation simulation, EnergySystem energy, Transform player, ShelterZone[] shelters, Action<bool> flightChanged = null)
        {
            Unbind();
            this.flightChanged = flightChanged;
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.energy = energy ?? throw new ArgumentNullException(nameof(energy));
            this.player = player != null ? player : throw new ArgumentNullException(nameof(player));
            this.shelters = shelters ?? throw new ArgumentNullException(nameof(shelters));
            if (isActiveAndEnabled) Bind();
        }
        private void Bind()
        {
            model = new RecallDroneModel(simulation.Clock, simulation.Events, TryLaunch);
            simulation.RegisterSystem(model);
        }
        private bool TryLaunch()
        {
            if (Flying || player == null) return false;
            ShelterZone nearest = null;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < shelters.Length; i++)
            {
                var shelter = shelters[i];
                if (shelter == null || !shelter.isActiveAndEnabled) continue;
                float candidate = (shelter.ArrivalPosition - player.position).sqrMagnitude;
                if (candidate < distance) { nearest = shelter; distance = candidate; }
            }
            if (nearest == null || !energy.TrySpend(simulation.State, GameConstants.RecallEnergyKWh)) return false;
            destination = nearest;
            flightChanged?.Invoke(true);
            return true;
        }
        private void Update() { Fly(Time.deltaTime); }
        public void Fly(float deltaSeconds)
        {
            if (!Flying || player == null || simulation.Clock.Paused || !simulation.State.Alive) return;
            Vector3 arrival = destination.ArrivalPosition;
            player.position = Vector3.MoveTowards(player.position, arrival,
                GameConstants.RecallFlightSpeed * deltaSeconds * simulation.Clock.Speed);
            if (player.position == arrival) { destination = null; flightChanged?.Invoke(false); }
        }
        private void OnEnable() { if (simulation != null && model == null) Bind(); }
        private void OnDisable() { Unbind(); }
        private void Unbind()
        {
            if (model != null) { model.Dispose(); simulation.UnregisterSystem(model); model = null; }
            if (destination != null) flightChanged?.Invoke(false);
            destination = null;
        }
    }
}
