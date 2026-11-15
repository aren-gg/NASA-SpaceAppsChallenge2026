namespace MarsTrainer.Sim.Events
{
    public sealed class SolarFlare : WeatherEvent
    {
        public override string Type => "solar-flare";
        public bool AuroraAlert { get; }
        internal SolarFlare(double start, double peak, double end, int severity, bool auroraAlert)
            : base("solar-flare", start, peak, end, severity) { AuroraAlert = auroraAlert; }
    }
}
