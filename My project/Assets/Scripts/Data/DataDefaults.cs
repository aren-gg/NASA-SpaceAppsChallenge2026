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
    }
}
