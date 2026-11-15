using System;

namespace MarsTrainer.Sim
{
    // Absorbed dose in microgray per sol. Project schedule gcrDose_uGy and flareLevel here.
    public readonly struct RadiationConditions
    {
        public double GcrDoseUGy { get; }
        public int FlareLevel { get; }
        public RadiationConditions(double gcrDoseUGy, int flareLevel)
        {
            Battery.ValidateNonnegative(gcrDoseUGy, nameof(gcrDoseUGy));
            if (flareLevel < 0 || flareLevel > GameConstants.MaxFlareLevel)
                throw new ArgumentOutOfRangeException(nameof(flareLevel));
            GcrDoseUGy = gcrDoseUGy;
            FlareLevel = flareLevel;
        }
    }
}
