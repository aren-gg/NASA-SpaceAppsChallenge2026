using System;
using System.Collections.Generic;
using MarsTrainer.Sim.Events;

namespace MarsTrainer.Sim.Systems
{
    internal static class WeatherSchedule
    {
        internal static List<WeatherEvent> Map(WeatherConditions[] rows, int seed)
        {
            var result = new List<WeatherEvent>();
            var random = new Random(seed);
            for (int kind = 0; kind < 4; kind++)
            {
                int sol = 0;
                while (sol < rows.Length)
                {
                    if (!Hazard(rows[sol], kind)) { sol++; continue; }
                    int start = sol, peak = sol;
                    bool aurora = false;
                    while (sol < rows.Length && Hazard(rows[sol], kind))
                    {
                        if (Strength(rows[sol], kind) > Strength(rows[peak], kind)) peak = sol;
                        aurora |= rows[sol].AuroraAlert;
                        sol++;
                    }
                    double strength = Strength(rows[peak], kind);
                    int severity = kind == 0 ? (strength >= GameConstants.DustHighTau ? 3
                        : strength >= GameConstants.DustMediumTau ? 2 : 1)
                        : kind == 1 ? (int)strength
                        : Math.Max(1, Math.Min(GameConstants.MaxFlareLevel,
                            (int)Math.Ceiling(strength / GameConstants.TemperatureSeverityStep)));
                    double peakTime = peak + 0.5;
                    if (kind == 0)
                    {
                        double variation = 1 + (random.NextDouble() * 2 - 1) * GameConstants.WeatherMagnitudeVariation;
                        result.Add(new DustStorm(start, peakTime, sol, severity, strength * variation));
                    }
                    else if (kind == 1) result.Add(new SolarFlare(start, peakTime, sol, severity, aurora));
                    else result.Add(new TempExtreme(start, peakTime, sol, severity, kind == 2));
                }
            }
            return result;
        }

        private static bool Hazard(WeatherConditions row, int kind)
        {
            if (kind == 0) return row.Tau >= GameConstants.DustStormTau;
            if (kind == 1) return row.FlareLevel > 0 || row.AuroraAlert;
            return kind == 2 ? row.MinTemperature <= GameConstants.ColdNightTemperature
                : row.MaxTemperature >= GameConstants.HeatStormTemperature;
        }

        private static double Strength(WeatherConditions row, int kind)
        {
            if (kind == 0) return row.Tau;
            if (kind == 1) return Math.Max(row.FlareLevel, row.AuroraAlert ? GameConstants.AuroraFlareThreshold : 0);
            return kind == 2 ? GameConstants.ColdNightTemperature - row.MinTemperature
                : row.MaxTemperature - GameConstants.HeatStormTemperature;
        }
    }
}
