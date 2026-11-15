using System;

namespace MarsTrainer.Sim
{
    [Flags]
    public enum EnergyLoad
    {
        None = 0,
        Lights = 1,
        Heating = 2,
        Medbay = 4,
        Shield = 8,
        LifeSupport = 16
    }
}
