using System;

namespace MarsTrainer.Sim.Systems
{
    public sealed class SurvivalSystem : ISimSystem
    {
        private readonly EnergySystem energy;
        public Inventory Inventory { get; }
        public int Order => GameConstants.SurvivalSystemOrder;
        public bool Sleeping { get; set; }
        public bool Exercising { get; set; }
        internal double StarvedSols { get; private set; }
        internal double DehydratedSols { get; private set; }
        internal double HypoxicSols { get; private set; }

        public SurvivalSystem(Inventory inventory, EnergySystem energy = null)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.energy = energy;
        }

        public bool Eat(GameState state, double kcal)
        {
            SimClock.ValidateDelta(kcal);
            double taken = Math.Min(kcal, state.Hunger / GameConstants.StatMaximum *
                GameConstants.AstronautKcalPerSol * GameConstants.FoodReserveSols);
            if (!state.Alive || taken <= 0 || !Inventory.TryConsumeFood(taken)) return false;
            state.Hunger -= taken / (GameConstants.AstronautKcalPerSol * GameConstants.FoodReserveSols) * GameConstants.StatMaximum;
            return true;
        }

        public bool Drink(GameState state, double litres)
        {
            SimClock.ValidateDelta(litres);
            double taken = Math.Min(litres, (GameConstants.StatMaximum - state.Water) /
                GameConstants.StatMaximum * GameConstants.WaterReserveLitres);
            if (!state.Alive || taken <= 0 || !Inventory.TryConsumeWater(taken)) return false;
            state.Water += taken / GameConstants.WaterReserveLitres * GameConstants.StatMaximum;
            return true;
        }

        public bool PayForExercise(GameState state, double sols)
        {
            SimClock.ValidateDelta(sols);
            double food = sols * GameConstants.ExerciseKcalPerSol;
            double water = sols * GameConstants.ExerciseWaterLitresPerSol;
            if (!state.Alive || sols <= 0 || Inventory.FoodKcal < food || Inventory.WaterLitres < water ||
                !SpendPower(state, sols * GameConstants.ExerciseKWhPerSol)) return false;
            Inventory.TryConsumeFood(food);
            Inventory.TryConsumeWater(water);
            return true;
        }

        public bool Medbay(GameState state, double kWh)
        {
            SimClock.ValidateDelta(kWh);
            double paid = Math.Min(kWh, Math.Max(0, state.MaxHealth - state.Health) / GameConstants.MedbayHpPerKWh);
            if (!state.Alive || paid <= 0 || !SpendPower(state, paid)) return false;
            state.Health = Math.Min(state.MaxHealth, state.Health + paid * GameConstants.MedbayHpPerKWh);
            return true;
        }

        private bool SpendPower(GameState state, double cost)
        {
            if (energy != null) return energy.TrySpend(state, cost);
            if (state.EnergyKWh < cost) return false;
            state.EnergyKWh -= cost;
            return true;
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            SimClock.ValidateDelta(dtSol);
            if (dtSol == 0 || !state.Alive) return;
            double hungerRate = GameConstants.StatMaximum / GameConstants.FoodReserveSols;
            double waterRate = GameConstants.StatMaximum * GameConstants.WaterLitresPerSol / GameConstants.WaterReserveLitres;
            StarvedSols = Math.Max(0, dtSol - (GameConstants.StatMaximum - state.Hunger) / hungerRate);
            DehydratedSols = Math.Max(0, dtSol - state.Water / waterRate);
            state.Hunger += hungerRate * dtSol;
            state.Water -= waterRate * dtSol;
            // The energy load already paid for this interval. Never charge it twice.
            double powered;
            if (energy != null) powered = energy.LifeSupportPowerFraction;
            else
            {
                double required = GameConstants.LifeSupportKWhPerSol * dtSol;
                double paid = Math.Min(Math.Max(0, state.EnergyKWh), required);
                state.EnergyKWh -= paid;
                powered = paid / required;
            }
            double oxygenRate = GameConstants.StatMaximum / GameConstants.OxygenReserveSols;
            double netRate = state.UsingOxygenTank ? 0 : oxygenRate * (2 * powered - 1);
            HypoxicSols = netRate < 0 ? Math.Max(0, dtSol - state.Oxygen / -netRate)
                : netRate == 0 && state.Oxygen <= 0 ? dtSol : 0;
            state.Oxygen += netRate * dtSol;
            if (Exercising) state.Fitness += GameConstants.ExerciseFitnessPerSol * dtSol;
            state.Tiredness += (Sleeping ? -GameConstants.SleepRecoveryPerSol : GameConstants.TirednessPerSol) * dtSol;
        }
    }
}
