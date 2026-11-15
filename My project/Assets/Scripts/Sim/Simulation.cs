using System;
using System.Collections.Generic;
using MarsTrainer.Sim.Systems;

namespace MarsTrainer.Sim
{
    public sealed class Simulation
    {
        private readonly List<ISimSystem> systems = new List<ISimSystem>();
        private readonly int[] critical = new int[5];
        private readonly double oxygenGraceSols;
        private double oxygenDepletedSols;
        private bool finished;

        public SurvivalSystem Survival { get; private set; }

        public SurvivalSystem EnableSurvival(Inventory inventory, Systems.EnergySystem energy = null)
        {
            if (Survival != null)
            {
                if (!ReferenceEquals(Survival.Inventory, inventory))
                    throw new InvalidOperationException("Survival is already using a different inventory.");
                return Survival;
            }
            Survival = new SurvivalSystem(inventory, energy);
            RegisterSystem(Survival);
            RegisterSystem(new Systems.HealthSystem(State, Survival));
            return Survival;
        }

        public SimClock Clock { get; }
        public GameState State { get; }
        public SimModifiers Modifiers { get; } = new SimModifiers();
        public SimEvents Events { get; } = new SimEvents();
        public IReadOnlyList<ISimSystem> Systems { get; }

        public Simulation(double runDurationSeconds = GameConstants.RunDurationSeconds,
            double oxygenGraceSols = GameConstants.OxygenGraceSols)
        {
            if (double.IsNaN(oxygenGraceSols) || double.IsInfinity(oxygenGraceSols) || oxygenGraceSols <= 0)
                throw new ArgumentOutOfRangeException(nameof(oxygenGraceSols));
            Clock = new SimClock(runDurationSeconds);
            State = new GameState();
            this.oxygenGraceSols = oxygenGraceSols;
            Systems = systems.AsReadOnly();
            RegisterSystem(new EmptySlot("Energy", GameConstants.EnergySystemOrder));
            RegisterSystem(new EmptySlot("Radiation", GameConstants.RadiationSystemOrder));
            RegisterSystem(new EmptySlot("Weather", GameConstants.WeatherSystemOrder));
            RegisterSystem(new EmptySlot("Farm", GameConstants.FarmSystemOrder));
            RegisterSystem(new EmptySlot("Survival", GameConstants.SurvivalSystemOrder));
        }

        // Register before ticking. Equal orders preserve registration order.
        public void RegisterSystem(ISimSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            int index = 0;
            while (index < systems.Count && systems[index].Order <= system.Order) index++;
            systems.Insert(index, system);
        }

        public bool UnregisterSystem(ISimSystem system) { return systems.Remove(system); }

        public void Tick(double dtReal)
        {
            SimClock.ValidateDelta(dtReal);
            if (finished) return;
            ReportCriticalStats();
            if (CheckDeath()) return;
            if (State.Oxygen > 0) oxygenDepletedSols = 0;
            if (Clock.Paused) return;
            double target = Math.Min(GameConstants.MissionDurationDays,
                Clock.ElapsedSols + dtReal / Clock.SecondsPerSol * Clock.Speed);
            while (!finished && Clock.ElapsedSols < target)
            {
                double before = Clock.ElapsedSols;
                double end = Math.Min(target, Math.Floor(before) + 1);
                bool depleted = State.Oxygen <= 0;
                double remainingOxygen = oxygenGraceSols - oxygenDepletedSols;
                double oxygenDeadline = before + remainingOxygen;
                if (Survival != null)
                    end = Math.Min(end, before + GameConstants.SurvivalStepSols);
                else if (depleted) end = Math.Min(end, oxygenDeadline);
                double dtSol = end - before;
                double healthBefore = State.Health;
                for (int i = 0; i < systems.Count; i++) systems[i].Step(State, dtSol);
                // Include damage from existing weather/exposure systems in the same budget.
                if (Survival != null)
                    State.Health = Math.Max(State.Health, healthBefore - GameConstants.HealthDrainCapPerSol * dtSol);
                Clock.AdvanceTo(end);
                int previousSol = State.Sol;
                State.Sol = Clock.Sol;
                // A system's new oxygen value applies from the end of its step.
                if (State.Oxygen > 0) oxygenDepletedSols = 0;
                else if (depleted)
                    oxygenDepletedSols = end >= oxygenDeadline ? oxygenGraceSols : oxygenDepletedSols + dtSol;
                ReportCriticalStats();
                if (CheckDeath()) return;
                if (State.Sol != previousSol) Events.StartSol(State.Sol);
                if (Clock.Sol == GameConstants.MissionDurationDays)
                {
                    finished = true;
                    Events.CompleteMission();
                }
            }
        }

        private bool CheckDeath()
        {
            if (State.Health > 0 && (Survival != null || oxygenDepletedSols < oxygenGraceSols)) return false;
            State.DeathCause = State.Health <= 0 ? State.LastDamageCause ?? "health" : "hypoxia";
            State.Alive = false;
            finished = true;
            Events.ReportDeath();
            return true;
        }

        private void ReportCriticalStats()
        {
            ReportCritical(0, "health", State.Health, false);
            ReportCritical(1, "hunger", State.Hunger, true);
            ReportCritical(2, "water", State.Water, false);
            ReportCritical(3, "tiredness", State.Tiredness, true);
            ReportCritical(4, "oxygen", State.Oxygen, false);
        }

        private void ReportCritical(int index, string id, double value, bool highIsBad)
        {
            double remaining = highIsBad ? GameConstants.StatMaximum - value : value;
            int band = remaining <= GameConstants.StatEmergencyThreshold ? 2
                : remaining <= GameConstants.StatCriticalThreshold ? 1 : 0;
            int was = critical[index];
            critical[index] = band;
            if (band > was) Events.ReportCritical(id, value);
        }

        private sealed class EmptySlot : ISimSystem
        {
            public string Name { get; }
            public int Order { get; }
            public EmptySlot(string name, int order) { Name = name; Order = order; }
            public void Step(GameState state, double dtSol) { }
            public override string ToString() { return Name; }
        }
    }
}
