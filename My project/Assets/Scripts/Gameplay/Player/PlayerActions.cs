using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using UnityEngine;

namespace MarsTrainer.Gameplay.Player
{
    // Input/UI facade. All resource and physiology rules live in the pure sim.
    public sealed class PlayerActions : MonoBehaviour
    {
        private Simulation sim;
        private SurvivalSystem survival;
        private bool busy;

        public void Initialize(Simulation simulation, Inventory inventory, EnergySystem energy = null)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            if (sim != null && !ReferenceEquals(sim, simulation))
                throw new InvalidOperationException("Player actions already belong to another simulation.");
            survival = simulation.EnableSurvival(inventory, energy);
            sim = simulation;
        }

        private bool CanAct => sim != null && sim.State.Alive && sim.State.Health > 0 &&
            !sim.Clock.Paused && !busy && sim.Clock.ElapsedSols < GameConstants.MissionDurationDays;

        public bool Eat(double kcal)
        {
            Validate(kcal);
            return CanAct && survival.Eat(sim.State, kcal);
        }

        public bool Drink(double litres)
        {
            Validate(litres);
            return CanAct && survival.Drink(sim.State, litres);
        }

        public bool Medbay(double kWh)
        {
            Validate(kWh);
            return CanAct && survival.Medbay(sim.State, kWh);
        }

        public bool Sleep(double sols) { return PassTime(sols, false); }
        public bool Exercise(double sols) { return PassTime(sols, true); }

        private bool PassTime(double sols, bool exercise)
        {
            Validate(sols);
            if (!CanAct || sols <= 0) return false;
            sols = Math.Min(sols, GameConstants.MissionDurationDays - sim.Clock.ElapsedSols);
            if (exercise && !survival.PayForExercise(sim.State, sols)) return false;
            busy = true;
            survival.Sleeping = !exercise;
            survival.Exercising = exercise;
            try { sim.Tick(sols * sim.Clock.SecondsPerSol / sim.Clock.Speed); }
            finally
            {
                survival.Sleeping = false;
                survival.Exercising = false;
                busy = false;
            }
            return true;
        }

        private static void Validate(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
