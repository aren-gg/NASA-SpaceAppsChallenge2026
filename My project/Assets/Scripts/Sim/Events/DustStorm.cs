namespace MarsTrainer.Sim.Events
{
    public sealed class DustStorm : WeatherEvent
    {
        public override string Type => "dust-storm";
        public double PeakTau { get; }
        internal DustStorm(double start, double peak, double end, int severity, double peakTau)
            : base("dust-storm", start, peak, end, severity) { PeakTau = peakTau; }

        internal double TauAt(double sol)
        {
            if (sol < StartSol || sol >= EndSol) return GameConstants.DefaultClearTau;
            double ramp = sol <= PeakSol ? (sol - StartSol) / (PeakSol - StartSol)
                : (EndSol - sol) / (EndSol - PeakSol);
            return GameConstants.DefaultClearTau + (PeakTau - GameConstants.DefaultClearTau)
                * ramp * Severity / BaseSeverity;
        }
    }
}
