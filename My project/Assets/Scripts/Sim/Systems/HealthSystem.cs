using System;

namespace MarsTrainer.Sim.Systems
{
    public sealed class HealthSystem : ISimSystem
    {
        private readonly SurvivalSystem survival;
        private double previousDose;
        public int Order => GameConstants.HealthSystemOrder;

        public HealthSystem(GameState state, SurvivalSystem survival = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            this.survival = survival;
            previousDose = state.RadiationDoseMSv;
            state.HealthManaged = true;
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            SimClock.ValidateDelta(dtSol);
            if (dtSol == 0 || !state.Alive) return;
            double damage = 0, largest = 0;
            string cause = null;
            Add(ref damage, ref largest, ref cause, "starvation", GameConstants.StarvationHpPerSol *
                (survival != null ? survival.StarvedSols : state.Hunger >= GameConstants.StatMaximum ? dtSol : 0));
            Add(ref damage, ref largest, ref cause, "dehydration", GameConstants.DehydrationHpPerSol *
                (survival != null ? survival.DehydratedSols : state.Water <= 0 ? dtSol : 0));
            Add(ref damage, ref largest, ref cause, "hypoxia", GameConstants.HypoxiaHpPerSol *
                (survival != null ? survival.HypoxicSols : state.Oxygen <= 0 ? dtSol : 0));
            double threshold = GameConstants.H_limit * GameConstants.ShieldDangerFraction;
            // A stationary dose still causes damage. Integrate a rising dose across the danger boundary.
            double before = Math.Min(previousDose, state.RadiationDoseMSv);
            if (survival == null) before = state.RadiationDoseMSv;
            double start = Math.Max(0, before - threshold);
            double end = Math.Max(0, state.RadiationDoseMSv - threshold);
            double exposed = state.RadiationDoseMSv > before
                ? Math.Min(dtSol, end / (state.RadiationDoseMSv - before) * dtSol) : dtSol;
            Add(ref damage, ref largest, ref cause, "radiation", (start + end) * exposed /
                2 / GameConstants.H_limit * GameConstants.RadiationHpDrainPerSol);
            previousDose = state.RadiationDoseMSv;
            if (!state.TemperatureProtected && (state.TemperatureC < GameConstants.SafeTemperatureMinC ||
                state.TemperatureC > GameConstants.SafeTemperatureMaxC))
                Add(ref damage, ref largest, ref cause, "temperature", GameConstants.TemperatureHpPerSol * dtSol);
            if (damage > 0)
            {
                state.LastDamageCause = cause;
                state.Health -= Math.Min(damage, GameConstants.HealthDrainCapPerSol * dtSol);
            }
            state.Fitness -= GameConstants.FitnessDecayPerSol * dtSol;
            state.Health = Math.Min(state.Health, state.MaxHealth);
        }

        private static void Add(ref double total, ref double largest, ref string cause, string candidate, double amount)
        {
            total += amount;
            if (amount > largest) { largest = amount; cause = candidate; }
        }
    }
}
