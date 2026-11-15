using System;

namespace MarsTrainer.Sim
{
    // Pure projection of a validated DataRepository schedule row; no JSON or Unity dependency.
    public readonly struct WeatherConditions
    {
        public double Ls { get; }
        public double Tau { get; }
        public double MinTemperature { get; }
        public double MaxTemperature { get; }
        public double GcrDoseUGy { get; }
        public int FlareLevel { get; }
        public bool AuroraAlert { get; }

        public WeatherConditions(double ls, double tau, double minTemperature, double maxTemperature,
            double gcrDoseUGy, int flareLevel, bool auroraAlert)
        {
            Battery.ValidateNonnegative(tau, nameof(tau));
            Battery.ValidateNonnegative(gcrDoseUGy, nameof(gcrDoseUGy));
            if (double.IsNaN(ls) || double.IsInfinity(ls) ||
                double.IsNaN(minTemperature) || double.IsInfinity(minTemperature) ||
                double.IsNaN(maxTemperature) || double.IsInfinity(maxTemperature) || minTemperature > maxTemperature)
                throw new ArgumentOutOfRangeException(nameof(minTemperature));
            if (flareLevel < 0 || flareLevel > GameConstants.MaxFlareLevel)
                throw new ArgumentOutOfRangeException(nameof(flareLevel));
            Ls = ls;
            Tau = tau;
            MinTemperature = minTemperature;
            MaxTemperature = maxTemperature;
            GcrDoseUGy = gcrDoseUGy;
            FlareLevel = flareLevel;
            AuroraAlert = auroraAlert;
        }
    }
}
