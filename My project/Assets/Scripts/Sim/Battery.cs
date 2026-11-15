using System;

namespace MarsTrainer.Sim
{
    public sealed class Battery
    {
        private readonly double efficiency;
        public double CapacityKWh { get; }
        public double ChargeLimitKW { get; }
        public double DischargeLimitKW { get; }
        public double StoredKWh { get; private set; }
        public double Percent => CapacityKWh > 0 ? StoredKWh / CapacityKWh * GameConstants.StatMaximum : 0;

        // Limits and transfer amounts are measured on the external bus; loss is symmetric.
        public Battery(double capacityKWh, double chargeLimitKW, double dischargeLimitKW,
            double roundTripEfficiency, double initialKWh = 0)
        {
            ValidateNonnegative(capacityKWh, nameof(capacityKWh));
            ValidateNonnegative(chargeLimitKW, nameof(chargeLimitKW));
            ValidateNonnegative(dischargeLimitKW, nameof(dischargeLimitKW));
            ValidateNonnegative(roundTripEfficiency, nameof(roundTripEfficiency));
            if (roundTripEfficiency <= 0 || roundTripEfficiency > 1)
                throw new ArgumentOutOfRangeException(nameof(roundTripEfficiency));
            if (double.IsNaN(initialKWh) || double.IsInfinity(initialKWh))
                throw new ArgumentOutOfRangeException(nameof(initialKWh));
            CapacityKWh = capacityKWh;
            ChargeLimitKW = chargeLimitKW;
            DischargeLimitKW = dischargeLimitKW;
            efficiency = Math.Sqrt(roundTripEfficiency);
            StoredKWh = Math.Max(0, Math.Min(capacityKWh, initialKWh));
        }

        // Atomic one-off bus cost; preserves round-trip loss and leaves storage unchanged on failure.
        public bool TrySpend(double requestedKWh)
        {
            ValidateNonnegative(requestedKWh, nameof(requestedKWh));
            if (StoredKWh * efficiency < requestedKWh) return false;
            StoredKWh = Math.Max(0, StoredKWh - requestedKWh / efficiency);
            return true;
        }

        public double Charge(double availableKWh, double hours)
        {
            ValidateNonnegative(availableKWh, nameof(availableKWh));
            ValidateNonnegative(hours, nameof(hours));
            double accepted = Math.Min(availableKWh, Math.Min(ChargeLimitKW * hours,
                (CapacityKWh - StoredKWh) / efficiency));
            StoredKWh = Math.Min(CapacityKWh, StoredKWh + accepted * efficiency);
            return accepted;
        }

        public double Discharge(double requestedKWh, double hours)
        {
            ValidateNonnegative(requestedKWh, nameof(requestedKWh));
            ValidateNonnegative(hours, nameof(hours));
            double delivered = Math.Min(requestedKWh, Math.Min(DischargeLimitKW * hours, StoredKWh * efficiency));
            StoredKWh = Math.Max(0, StoredKWh - delivered / efficiency);
            return delivered;
        }

        internal static void ValidateNonnegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
