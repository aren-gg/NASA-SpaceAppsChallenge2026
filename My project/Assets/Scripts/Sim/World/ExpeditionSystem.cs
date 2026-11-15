using System;
using MarsTrainer.Sim.Systems;

namespace MarsTrainer.Sim.World
{
    // Transform/input adapters supply location and intent; only SimClock advances costs.
    public sealed class ExpeditionSystem : ISimSystem
    {
        private readonly SimClock clock;
        private readonly EnergySystem energy;
        private readonly double tankCapacitySeconds;
        public int Order => GameConstants.ExpeditionSystemOrder;
        public bool AtBase { get; set; } = true;
        public bool SprintRequested { get; set; }
        public double TankSeconds { get; private set; }

        public ExpeditionSystem(SimClock clock, EnergySystem energy = null, double tankSeconds = GameConstants.OxygenTankSeconds)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (double.IsNaN(tankSeconds) || double.IsInfinity(tankSeconds) || tankSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(tankSeconds));
            this.energy = energy;
            TankSeconds = tankCapacitySeconds = tankSeconds;
        }

        public bool CanSprint(GameState state)
        {
            return state.Alive && state.Health > 0 && !clock.Paused &&
                state.Tiredness < GameConstants.SprintTirednessLimit &&
                state.EnergyKWh > 0 && clock.ElapsedSols < GameConstants.MissionDurationDays;
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (double.IsNaN(dtSol) || double.IsInfinity(dtSol) || dtSol < 0)
                throw new ArgumentOutOfRangeException(nameof(dtSol));
            if (dtSol == 0 || !state.Alive || clock.Paused) return;
            double seconds = dtSol * clock.SecondsPerSol;
            state.UsingOxygenTank = !AtBase;
            TankSeconds = AtBase ? tankCapacitySeconds : Math.Max(0, TankSeconds - seconds);
            if (!AtBase) state.Oxygen = TankSeconds / tankCapacitySeconds * GameConstants.StatMaximum;
            else state.Oxygen = GameConstants.StatMaximum;
            if (!SprintRequested || !CanSprint(state)) return;
            double cost = seconds * GameConstants.SprintKWhPerSecond;
            if (energy != null)
            {
                if (!energy.TrySpend(state, cost)) return;
            }
            else
            {
                if (state.EnergyKWh + GameConstants.EnergyTimeTolerance < cost) return;
                state.EnergyKWh = Math.Max(0, state.EnergyKWh - cost);
            }
            state.Tiredness += seconds * GameConstants.SprintTirednessPerSecond;
        }
    }
}
