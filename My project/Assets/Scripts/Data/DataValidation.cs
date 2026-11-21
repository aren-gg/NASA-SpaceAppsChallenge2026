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

        private static bool Range(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }
    }
}
