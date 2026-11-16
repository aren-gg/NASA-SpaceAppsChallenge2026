using System;

namespace MarsSurvival.Hardware
{
    [Serializable]
    public sealed class DashboardCalibration
    {
        public int minimum;
        public int maximum = 65535;
        public bool inverted;
        public bool configured;

        public bool IsValid { get { return minimum >= 0 && maximum > minimum && maximum <= 65535; } }

        public double Normalize(int raw, int advertisedMaximum)
        {
            double low = configured ? minimum : 0;
            double high = configured ? maximum : advertisedMaximum;
            if (high <= low) return 0;
            double value = DashboardProtocol.Clamp((raw - low) / (high - low), 0, 1);
            return inverted ? 1 - value : value;
        }
    }

    public sealed class DashboardKnobController
    {
        public readonly DashboardCalibration[] Calibration =
        {
            new DashboardCalibration(), new DashboardCalibration(), new DashboardCalibration()
        };
        public readonly int[] RawValues = new int[3];
        public readonly double[] NormalizedValues = new double[3];
        public readonly bool[] AwaitingTakeover = { true, true, true };
        public double takeoverTolerance = 0.025;
        public double deadband = 0.004;
        public double smoothingSeconds = 0.08;

        private readonly double[] previousPhysical = new double[3], lastSent = new double[3];
        private readonly bool[] hasPhysical = new bool[3];

        public void RequireTakeover(int channel = -1)
        {
            for (int i = 0; i < 3; i++)
            {
                if (channel >= 0 && i != channel) continue;
                AwaitingTakeover[i] = true;
                hasPhysical[i] = false;
            }
        }

        /// <summary>Lets every knob take control at once, from wherever it is turned.</summary>
        public void Release()
        {
            for (int i = 0; i < 3; i++) AwaitingTakeover[i] = false;
        }

        public bool Apply(int channel, int raw, int advertisedMaximum, double logical, double elapsedSeconds, out double request, bool normalizedInput = false)
        {
            request = logical;
            if (channel < 0 || channel > 2 || advertisedMaximum < 1 || raw < 0 || raw > advertisedMaximum) return false;
            DashboardCalibration calibration = Calibration[channel];
            if (!normalizedInput && !calibration.IsValid) return false;
            double physical = normalizedInput ? (double)raw / advertisedMaximum : calibration.Normalize(raw, advertisedMaximum);
            RawValues[channel] = raw;
            bool hadPhysical = hasPhysical[channel];
            double previous = previousPhysical[channel];
            double smoothing = smoothingSeconds <= 0 ? 1 : 1 - Math.Exp(-Math.Max(0, elapsedSeconds) / smoothingSeconds);
            NormalizedValues[channel] = hadPhysical ? NormalizedValues[channel] + smoothing * (physical - NormalizedValues[channel]) : physical;
            previousPhysical[channel] = physical;
            hasPhysical[channel] = true;

            if (AwaitingTakeover[channel])
            {
                bool near = Math.Abs(physical - logical) <= takeoverTolerance;
                bool crossed = hadPhysical && (previous - logical) * (physical - logical) <= 0;
                if (!near && !crossed) return false;
                AwaitingTakeover[channel] = false;
                // Start from the saved setting; smoothing then follows the physical knob.
                NormalizedValues[channel] = logical;
                lastSent[channel] = logical;
                return false;
            }

            request = DashboardProtocol.Clamp(NormalizedValues[channel], 0, 1);
            if (physical == 0 || physical == 1) request = physical;
            // Skip jitter, but always pull the game onto the knob when they disagree (for
            // example the first reading of a knob parked at zero, or a freshly loaded save).
            if (Math.Abs(request - lastSent[channel]) < deadband && Math.Abs(request - logical) < deadband) return false;
            lastSent[channel] = request;
            return true;
        }
    }
}
