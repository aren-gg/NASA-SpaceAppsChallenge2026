using System;

namespace MarsTrainer.Sim
{
    // Harvested food data only: no behaviours or Unity objects.
    public sealed class SpaceSnack
    {
        public string Name { get; }
        public double Kcal { get; }
        public SpaceSnack(string name, double kcal)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            Battery.ValidateNonnegative(kcal, nameof(kcal));
            Name = name; Kcal = kcal;
        }
    }
}
