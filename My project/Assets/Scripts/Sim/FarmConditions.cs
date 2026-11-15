using System;

namespace MarsTrainer.Sim
{
    public readonly struct FarmConditions
    {
        // PAR is the daily effective natural light before dust attenuation.
        public double Par { get; }
        public double DustOpacity { get; }
        public double Co2Ppm { get; }
        public double Temperature { get; }

        public FarmConditions(double par, double dustOpacity, double co2Ppm, double temperature)
        {
            Battery.ValidateNonnegative(par, nameof(par));
            Battery.ValidateNonnegative(dustOpacity, nameof(dustOpacity));
            Battery.ValidateNonnegative(co2Ppm, nameof(co2Ppm));
            if (double.IsNaN(temperature) || double.IsInfinity(temperature))
                throw new ArgumentOutOfRangeException(nameof(temperature));
            Par = par; DustOpacity = dustOpacity; Co2Ppm = co2Ppm; Temperature = temperature;
        }
    }
}
