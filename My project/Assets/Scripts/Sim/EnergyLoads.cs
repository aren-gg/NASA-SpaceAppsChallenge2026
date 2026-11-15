using System;

namespace MarsTrainer.Sim
{
    public sealed class EnergyLoads
    {
        public double LifeSupport { get; }
        public double Shield { get; }
        public double Medbay { get; }
        public double Lights { get; }
        public double HeatingKWhPerDegreeSol { get; }
        public double SetTemperature { get; }

        public EnergyLoads(double lifeSupport, double shield, double medbay, double lights,
            double heatingKWhPerDegreeSol, double setTemperature)
        {
            Battery.ValidateNonnegative(lifeSupport, nameof(lifeSupport));
            Battery.ValidateNonnegative(shield, nameof(shield));
            Battery.ValidateNonnegative(medbay, nameof(medbay));
            Battery.ValidateNonnegative(lights, nameof(lights));
            Battery.ValidateNonnegative(heatingKWhPerDegreeSol, nameof(heatingKWhPerDegreeSol));
            if (double.IsNaN(setTemperature) || double.IsInfinity(setTemperature))
                throw new ArgumentOutOfRangeException(nameof(setTemperature));
            LifeSupport = lifeSupport;
            Shield = shield;
            Medbay = medbay;
            Lights = lights;
            HeatingKWhPerDegreeSol = heatingKWhPerDegreeSol;
            SetTemperature = setTemperature;
        }
    }
}
