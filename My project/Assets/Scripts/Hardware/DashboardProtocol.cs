using System;
using System.Globalization;
using System.Text;

namespace MarsSurvival.Hardware
{
    public enum DashboardPacketKind { Hello, Knobs, Heartbeat }

    public struct DashboardPacket
    {
        public DashboardPacketKind kind;
        public string session;
        public uint sequence, uptimeMilliseconds;
        public int adcMaximum;
        public int life, temperature, shielding;
    }

    public static class DashboardProtocol
    {
        public const int Version = 1;
        public const int MaximumLineLength = 192;

        public static bool TryParse(string line, out DashboardPacket packet, out string error)
        {
            packet = new DashboardPacket();
            error = "Invalid dashboard packet.";
            if (string.IsNullOrEmpty(line) || line.Length > MaximumLineLength) return false;
            string[] values = line.Trim().Split(',');
            if (values.Length < 5 || values[0] != "MARS" || values[1] != "1")
            {
                error = "Expected MARS protocol version 1.";
                return false;
            }
            if (!ValidSession(values[3])) return false;
            packet.session = values[3];
            if (values[2] == "HELLO")
            {
                packet.kind = DashboardPacketKind.Hello;
                if (values.Length != 5 || !TryInteger(values[4], out packet.adcMaximum) ||
                    packet.adcMaximum < 1 || packet.adcMaximum > 65535) return false;
                error = "";
                return true;
            }
            if (values.Length < 6 ||
                !uint.TryParse(values[4], NumberStyles.None, CultureInfo.InvariantCulture, out packet.sequence) ||
                !uint.TryParse(values[5], NumberStyles.None, CultureInfo.InvariantCulture, out packet.uptimeMilliseconds)) return false;
            if (values[2] == "HEARTBEAT" && values.Length == 6)
            {
                packet.kind = DashboardPacketKind.Heartbeat;
                error = "";
                return true;
            }
            if (values[2] != "KNOBS" || values.Length != 9) return false;
            packet.kind = DashboardPacketKind.Knobs;
            if (!TryInteger(values[6], out packet.life) || !TryInteger(values[7], out packet.temperature) ||
                !TryInteger(values[8], out packet.shielding)) return false;
            if (packet.life > 65535 || packet.temperature > 65535 || packet.shielding > 65535) return false;
            error = "";
            return true;
        }

        public static string Feedback(uint sequence, double[] knobs, double[] deliveredKw, string phase)
        {
            if (knobs == null || knobs.Length < 3) return "";
            if (deliveredKw == null || deliveredKw.Length < 3) deliveredKw = new double[3];
            string safePhase = phase == "Running" || phase == "Won" || phase == "Lost" ? phase : "Ready";
            return string.Format(CultureInfo.InvariantCulture,
                "MARS,1,STATUS,{0},{1:0.000},{2:0.000},{3:0.000},{4:0.000},{5:0.000},{6:0.000},{7}\n",
                sequence, Clamp(knobs[0], 0, 1), Clamp(knobs[1], 0, 1), Clamp(knobs[2], 0, 1),
                Clamp(deliveredKw[0], 0, 100000), Clamp(deliveredKw[1], 0, 100000), Clamp(deliveredKw[2], 0, 100000), safePhase);
        }

        public static bool IsNewer(uint candidate, uint previous)
        {
            return unchecked((int)(candidate - previous)) > 0;
        }

        internal static double Clamp(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return minimum;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool TryInteger(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static bool ValidSession(string session)
        {
            if (string.IsNullOrEmpty(session) || session.Length > 40) return false;
            foreach (char c in session)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }
    }

    public sealed class DashboardSession
    {
        public string Id { get; private set; }
        public int AdcMaximum { get; private set; }
        private uint lastSequence, lastUptime;
        private bool hasSequence;

        public void Reset()
        {
            Id = null;
            AdcMaximum = 0;
            hasSequence = false;
        }

        public bool Accept(DashboardPacket packet, out bool newSession)
        {
            newSession = false;
            if (packet.kind == DashboardPacketKind.Hello)
            {
                if (packet.session == Id) return packet.adcMaximum == AdcMaximum;
                Id = packet.session;
                AdcMaximum = packet.adcMaximum;
                hasSequence = false;
                newSession = true;
                return true;
            }
            if (Id == null || Id != packet.session) return false;
            if (hasSequence && (!DashboardProtocol.IsNewer(packet.sequence, lastSequence) ||
                unchecked((int)(packet.uptimeMilliseconds - lastUptime)) < 0)) return false;
            if (packet.kind == DashboardPacketKind.Knobs &&
                (packet.life > AdcMaximum || packet.temperature > AdcMaximum || packet.shielding > AdcMaximum)) return false;
            lastSequence = packet.sequence;
            lastUptime = packet.uptimeMilliseconds;
            hasSequence = true;
            return true;
        }
    }

    public sealed class DashboardLineFramer
    {
        private readonly StringBuilder line = new StringBuilder(DashboardProtocol.MaximumLineLength);
        private bool discarding;
        public int RejectedLines { get; private set; }

        public void Push(byte[] bytes, int count, Action<string> completed)
        {
            for (int i = 0; i < count; i++)
            {
                byte value = bytes[i];
                if (value == 10)
                {
                    if (!discarding && line.Length != 0) completed(line.ToString());
                    line.Length = 0;
                    discarding = false;
                }
                else if (value != 13 && !discarding)
                {
                    if (value < 32 || value > 126 || line.Length == DashboardProtocol.MaximumLineLength)
                    {
                        discarding = true;
                        line.Length = 0;
                        RejectedLines++;
                    }
                    else line.Append((char)value);
                }
            }
        }
    }
}
