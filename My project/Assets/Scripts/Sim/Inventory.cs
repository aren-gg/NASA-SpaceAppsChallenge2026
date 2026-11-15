using System;
using System.Collections.Generic;

namespace MarsTrainer.Sim
{
    public sealed class Inventory
    {
        private double consumedFromFirst;
        public double WaterLitres { get; private set; }
        private readonly List<SpaceSnack> snacks = new List<SpaceSnack>();
        public IReadOnlyList<SpaceSnack> Snacks { get; }
        public double FoodKcal { get; private set; }
        public Inventory() { Snacks = snacks.AsReadOnly(); }
        public void AddWater(double litres)
        {
            ValidateAmount(litres);
            WaterLitres += litres;
        }
        public bool TryConsumeWater(double litres)
        {
            ValidateAmount(litres);
            if (WaterLitres < litres) return false;
            WaterLitres -= litres;
            return true;
        }
        // Snacks retain their harvest identity; FoodKcal is the remaining edible balance.
        public bool TryConsumeFood(double kcal)
        {
            ValidateAmount(kcal);
            if (FoodKcal < kcal) return false;
            FoodKcal -= kcal;
            while (kcal > 0 && snacks.Count > 0)
            {
                double available = snacks[0].Kcal - consumedFromFirst;
                double taken = Math.Min(kcal, available);
                kcal -= taken;
                consumedFromFirst += taken;
                if (consumedFromFirst >= snacks[0].Kcal)
                {
                    snacks.RemoveAt(0);
                    consumedFromFirst = 0;
                }
            }
            return true;
        }
        private static void ValidateAmount(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        public void Add(SpaceSnack snack)
        {
            if (snack == null) throw new ArgumentNullException(nameof(snack));
            snacks.Add(snack);
            FoodKcal += snack.Kcal;
        }
    }
}
