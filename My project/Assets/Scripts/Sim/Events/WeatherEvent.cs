namespace MarsTrainer.Sim.Events
{
    public abstract class WeatherEvent
    {
        public abstract string Type { get; }
        public string Id { get; }
        public double StartSol { get; }
        public double PeakSol { get; }
        public double EndSol { get; }
        public int BaseSeverity { get; }
        public int Severity { get; internal set; }
        public double WarningTimeSols { get; internal set; } = GameConstants.WeatherWarningSols;
        public double WarningSol => System.Math.Max(0, StartSol - WarningTimeSols);
        internal bool Warned, Started, Peaked, Ended;

        internal WeatherEvent(string type, double start, double peak, double end, int severity)
        {
            Id = type + ":" + start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            StartSol = start;
            PeakSol = peak;
            EndSol = end;
            BaseSeverity = Severity = severity;
        }
    }
}
