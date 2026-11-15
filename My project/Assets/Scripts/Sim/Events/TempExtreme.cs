namespace MarsTrainer.Sim.Events
{
    public sealed class TempExtreme : WeatherEvent
    {
        public bool IsCold { get; }
        public override string Type => IsCold ? "cold-night" : "heat-storm";
        internal TempExtreme(double start, double peak, double end, int severity, bool isCold)
            : base(isCold ? "cold-night" : "heat-storm", start, peak, end, severity) { IsCold = isCold; }
    }
}
