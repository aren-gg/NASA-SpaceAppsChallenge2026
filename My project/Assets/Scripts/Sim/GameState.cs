using System;

namespace MarsTrainer.Sim
{
    public sealed class GameState
    {
        private double health = GameConstants.StatMaximum;
        private double hunger;
        private double water = GameConstants.StatMaximum;
        private double tiredness;
        private double fitness = GameConstants.StatMaximum;
        private double oxygen = GameConstants.StatMaximum;

        public double Health { get => health; set => health = Clamp(value); }
        public double Hunger { get => hunger; set => hunger = Clamp(value); }
        public double Water { get => water; set => water = Clamp(value); }
        public double Tiredness { get => tiredness; set => tiredness = Clamp(value); }
        public double Oxygen { get => oxygen; set => oxygen = Clamp(value); }
        public double Fitness { get => fitness; set => fitness = Clamp(value); }
        public double MaxHealth => GameConstants.StatMaximum -
            (GameConstants.StatMaximum - Fitness) * GameConstants.FitnessMaxHpPenalty;
        public double WorkEfficiency => 1 - GameConstants.WorkFatiguePenalty * Tiredness / GameConstants.StatMaximum;
        public bool UsingOxygenTank { get; set; }
        public bool TemperatureProtected { get; set; } = true;
        public double TemperatureC { get; set; }
        public string LastDamageCause { get; internal set; }
        public string DeathCause { get; internal set; }
        internal bool HealthManaged { get; set; }
        public double EnergyKWh { get; set; }
        public double BatteryCapKWh { get; set; } = GameConstants.DefaultBatteryCapKWh;
        public double RadiationDoseMSv { get; set; }
        public int Sol { get; internal set; }
        public bool Alive { get; internal set; } = true;

        private static double Clamp(double value)
        {
            if (double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return Math.Max(GameConstants.StatMinimum, Math.Min(GameConstants.StatMaximum, value));
        }
    }
}
