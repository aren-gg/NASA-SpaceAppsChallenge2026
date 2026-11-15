using System;

namespace MarsTrainer.Sim
{
    public sealed class SolarPanel
    {
        public int Count { get; }
        public double AreaM2 { get; }
        public double Efficiency { get; }

        public SolarPanel(int count, double areaM2, double efficiency)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Battery.ValidateNonnegative(areaM2, nameof(areaM2));
            Battery.ValidateNonnegative(efficiency, nameof(efficiency));
            if (efficiency > 1) throw new ArgumentOutOfRangeException(nameof(efficiency));
            Count = count;
            AreaM2 = areaM2;
            Efficiency = efficiency;
        }

        // solFraction is local solar time: sunrise 0.25, noon 0.5, sunset 0.75.
        public double PowerKW(double solFraction, double tau, double ls, SimModifiers modifiers)
        {
            Battery.ValidateNonnegative(tau, nameof(tau));
            Battery.ValidateNonnegative(solFraction, nameof(solFraction));
            if (double.IsNaN(ls) || double.IsInfinity(ls)) throw new ArgumentOutOfRangeException(nameof(ls));
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));
            double phase = solFraction - Math.Floor(solFraction);
            if (phase <= GameConstants.SunriseSol || phase >= GameConstants.SunsetSol) return 0;
            double cosine = Math.Sin((phase - GameConstants.SunriseSol) * 2 * Math.PI);
            double anomaly = (ls - GameConstants.MarsPerihelionLs) * Math.PI / (GameConstants.FullOrbitDegrees / 2);
            double e = GameConstants.MarsOrbitEccentricity;
            double r = GameConstants.MarsSemiMajorAxisAU * (1 - e * e) / (1 + e * Math.Cos(anomaly));
            double count = Math.Max(0, modifiers.Apply(GameConstants.PanelCountStat, Count));
            double area = Math.Max(0, modifiers.Apply(GameConstants.PanelAreaStat, AreaM2));
            double efficiency = Math.Max(0, Math.Min(1, modifiers.Apply(GameConstants.PanelEfficiencyStat, Efficiency)));
            return count * area * efficiency * GameConstants.SolarIrradianceWm2 / (r * r)
                * cosine * Math.Exp(-tau / cosine) / GameConstants.WattsPerKW;
        }
    }
}
