namespace MarsTrainer.Sim
{
    public sealed class FarmPlot
    {
        public FarmCrop Crop { get; internal set; }
        public double Growth { get; internal set; }
        public double WaterLitres { get; internal set; }
        public bool LampsEnabled { get; internal set; }
        public bool Ready => Crop != null && Growth >= 1 - GameConstants.FarmGrowthTolerance;
    }
}
