using System.Collections.Generic;
using MarsTrainer.Sim;

namespace MarsTrainer.Data
{
    internal static class DataValidation
    {
        internal static bool Schedule(ScheduleDto[] rows)
        {
            if (rows == null || rows.Length != GameConstants.ScheduleSols) return false;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null || row.sol != i || !Range(row.ls, 0f, GameConstants.FullOrbitDegrees) ||
                    row.ls >= GameConstants.FullOrbitDegrees ||
                    !Range(row.minTemp, GameConstants.MinAirTemperature, GameConstants.MaxAirTemperature) ||
                    !Range(row.maxTemp, row.minTemp, GameConstants.MaxAirTemperature) ||
                    !Range(row.dustOpacity, 0f, GameConstants.MaxDustOpacity) ||
                    !Range(row.gcrDose_uGy, 0f, GameConstants.MaxGcrDose) ||
                    row.flareLevel < 0 || row.flareLevel > GameConstants.MaxFlareLevel ||
                    string.IsNullOrWhiteSpace(row.source)) return false;
            }
            return true;
        }

        internal static bool Crops(CropDto[] rows)
        {
            if (rows == null || rows.Length == 0) return false;
            var ids = new HashSet<string>();
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.id) || !ids.Add(row.id) ||
                    string.IsNullOrWhiteSpace(row.name) || row.daysToHarvest <= 0 ||
                    row.daysToHarvest > GameConstants.MaxCropDays ||
                    !Positive(row.kcalPerHarvest, GameConstants.MaxHarvestKcal) ||
                    !Positive(row.parSat, GameConstants.MaxPar) ||
                    !Positive(row.waterLPerDay, GameConstants.MaxWaterLitres) ||
                    !Positive(row.co2Ppm, GameConstants.MaxCo2Ppm) ||
                    !Positive(row.lightHours, GameConstants.EarthDayHours)) return false;
            }
            return true;
        }

        private static bool Positive(float value, float max) { return value > 0f && Range(value, 0f, max); }
        private static bool Range(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }
    }
}
