using System;

namespace MarsTrainer.Sim
{
    // Snapshot of crop_parameters.json; the sim has no dependency on the JSON loader.
    public sealed class FarmCrop
    {
        public string Id { get; }
        public string Name { get; }
        public double DaysToHarvest { get; }
        public double KcalPerHarvest { get; }
        public double ParSaturation { get; }
        public double WaterLitresPerSol { get; }
        public double Co2Ppm { get; }
        public double LightHours { get; }

        public FarmCrop(string id, string name, double daysToHarvest, double kcalPerHarvest,
            double parSaturation, double waterLitresPerSol, double co2Ppm, double lightHours)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException(nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            Positive(daysToHarvest); Positive(kcalPerHarvest); Positive(parSaturation);
            Positive(waterLitresPerSol); Positive(co2Ppm); Positive(lightHours);
            if (lightHours > GameConstants.EarthDayHours) throw new ArgumentOutOfRangeException(nameof(lightHours));
            Id = id; Name = name; DaysToHarvest = daysToHarvest; KcalPerHarvest = kcalPerHarvest;
            ParSaturation = parSaturation; WaterLitresPerSol = waterLitresPerSol;
            Co2Ppm = co2Ppm; LightHours = lightHours;
        }

        private static void Positive(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
