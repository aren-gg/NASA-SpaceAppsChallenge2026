using System;
using MarsTrainer.Sim;

namespace MarsTrainer.Data
{
    internal static class DataDefaults
    {
        internal static ScheduleDto[] Schedule()
        {
            var random = new Random(GameConstants.DataSeed);
            var rows = new ScheduleDto[GameConstants.ScheduleSols];
            for (int sol = 0; sol < rows.Length; sol++)
            {
                float ls = sol * GameConstants.FullOrbitDegrees / rows.Length;
                float low = GameConstants.DefaultMinTemperature + GameConstants.TemperatureSeasonAmplitude *
                    (float)Math.Sin(ls * Math.PI / GameConstants.DustSeasonStart);
                int level = sol % GameConstants.FlarePeriodSols < GameConstants.FlareClusterSols
                    ? 1 + sol / GameConstants.FlarePeriodSols % GameConstants.MaxFlareLevel : 0;
                rows[sol] = new ScheduleDto
                {
                    sol = sol, ls = ls, minTemp = low, maxTemp = low + GameConstants.DefaultTemperatureSpan,
                    dustOpacity = GameConstants.DefaultClearTau + (ls >= GameConstants.DustSeasonStart
                        ? GameConstants.DefaultDustSeasonTau : 0f) + (float)random.NextDouble() * GameConstants.DefaultTauNoise,
                    gcrDose_uGy = GameConstants.DefaultGcrDose + ((float)random.NextDouble() * 2f - 1f) * GameConstants.DefaultGcrVariation,
                    flareLevel = level, auroraAlert = level >= GameConstants.AuroraFlareThreshold, source = "synthetic"
                };
            }
            return rows;
        }

        internal static CropDto[] Crops()
        {
            return new[]
            {
                Crop("romaine", "Outredgeous red romaine", GameConstants.RomaineHarvestDays,
                    GameConstants.RomaineKcal, GameConstants.LeafyPar, GameConstants.LeafyWater),
                Crop("mizuna", "Mizuna mustard", GameConstants.LeafyHarvestDays,
                    GameConstants.MizunaKcal, GameConstants.MizunaPar, GameConstants.MizunaWater),
                Crop("tokyo_bekana", "Tokyo Bekana cabbage", GameConstants.LeafyHarvestDays,
                    GameConstants.CabbageKcal, GameConstants.LeafyPar, GameConstants.LeafyWater)
            };
        }

        private static CropDto Crop(string id, string name, int days, float kcal, float par, float water)
        {
            return new CropDto
            {
                id = id, name = name, daysToHarvest = days, kcalPerHarvest = kcal, parSat = par,
                waterLPerDay = water, co2Ppm = GameConstants.CropCo2Ppm,
                lightHours = GameConstants.CropLightHours, needsVerification = true
            };
        }
    }
}
