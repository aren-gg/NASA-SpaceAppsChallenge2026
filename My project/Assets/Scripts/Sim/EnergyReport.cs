namespace MarsTrainer.Sim
{
    public readonly struct EnergyReport
    {
        public int Sol { get; }
        public double GenerationKWh { get; }
        // Load is requested demand; ServedKWh excludes shed demand. Net is generation minus demand.
        public double LoadKWh { get; }
        public double ServedKWh { get; }
        public double NetKWh => GenerationKWh - LoadKWh;
        public double BatteryPercent { get; }
        public EnergyLoad ShedLoads { get; }
        public EnergyReport(int sol, double generation, double load, double served, double batteryPercent, EnergyLoad shed)
        {
            Sol = sol;
            GenerationKWh = generation;
            LoadKWh = load;
            ServedKWh = served;
            BatteryPercent = batteryPercent;
            ShedLoads = shed;
        }
    }
}
