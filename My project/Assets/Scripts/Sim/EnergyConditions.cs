namespace MarsTrainer.Sim
{
    // Project a schedule row's ls, dustOpacity and (minTemp + maxTemp) / 2 here.
    public readonly struct EnergyConditions
    {
        public double Ls { get; }
        public double Tau { get; }
        public double OutdoorTemperature { get; }
        public EnergyConditions(double ls, double tau, double outdoorTemperature)
        {
            Ls = ls;
            Tau = tau;
            OutdoorTemperature = outdoorTemperature;
        }
    }
}
