using System;
using System.Collections.Generic;
using System.Text;

namespace MarsSurvival.Hardware
{
    public static class DashboardProtocolChecks
    {
        public static string[] Run()
        {
            List<string> failures = new List<string>();
            DashboardPacket packet;
            string error;
            Check(DashboardProtocol.TryParse("MARS,1,HELLO,boot-1,1023", out packet, out error), "Parse hello", failures);
            DashboardSession session = new DashboardSession();
            bool fresh;
            Check(session.Accept(packet, out fresh) && fresh, "New session", failures);
            Check(!DashboardProtocol.TryParse("MARS,2,HELLO,boot-1,1023", out packet, out error), "Reject unsupported version", failures);
            Check(!DashboardProtocol.TryParse("MARS,1,HELLO,boot-1,0", out packet, out error), "Reject empty ADC range", failures);
            Check(!DashboardProtocol.TryParse("MARS,1,KNOBS,boot-1,1,10,NaN,10,10", out packet, out error), "Reject NaN", failures);
            Check(!DashboardProtocol.TryParse("MARS,1,KNOBS,boot-1,1,10,-1,10,10", out packet, out error), "Reject negative input", failures);
            DashboardProtocol.TryParse("MARS,1,KNOBS,boot-1,1,100,512,100,1023", out packet, out error);
            Check(session.Accept(packet, out fresh), "Accept first reading", failures);
            Check(!session.Accept(packet, out fresh), "Reject duplicate sequence", failures);
            DashboardProtocol.TryParse("MARS,1,HELLO,boot-1,1023", out packet, out error);
            Check(session.Accept(packet, out fresh) && !fresh, "Repeated hello cannot reset sequence", failures);
            DashboardProtocol.TryParse("MARS,1,KNOBS,boot-1,1,100,512,100,1023", out packet, out error);
            Check(!session.Accept(packet, out fresh), "Repeated hello preserves duplicate protection", failures);
            DashboardProtocol.TryParse("MARS,1,KNOBS,boot-1,2,90,512,100,1023", out packet, out error);
            Check(!session.Accept(packet, out fresh), "Reject old uptime", failures);
            DashboardProtocol.TryParse("MARS,1,KNOBS,boot-1,2,110,1024,100,1023", out packet, out error);
            Check(!session.Accept(packet, out fresh), "Reject input above advertised range", failures);
            DashboardProtocol.TryParse("MARS,1,KNOBS,boot-2,2,110,500,100,1023", out packet, out error);
            Check(!session.Accept(packet, out fresh), "Reject missing boot handshake", failures);
            Check(DashboardProtocol.IsNewer(0, uint.MaxValue), "Sequence rollover", failures);

            DashboardLineFramer framer = new DashboardLineFramer();
            List<string> lines = new List<string>();
            byte[] part = Encoding.ASCII.GetBytes("MARS,1,HE");
            framer.Push(part, part.Length, lines.Add);
            part = Encoding.ASCII.GetBytes("LLO,boot-1,1023\r\n");
            framer.Push(part, part.Length, lines.Add);
            Check(lines.Count == 1 && lines[0] == "MARS,1,HELLO,boot-1,1023", "Partial-line framing", failures);
            part = Encoding.ASCII.GetBytes(new string('x', 300) + "\nMARS,1,HELLO,boot-2,1023\n");
            framer.Push(part, part.Length, lines.Add);
            Check(framer.RejectedLines == 1 && lines.Count == 2, "Bounded framing recovers after oversize line", failures);

            DashboardKnobController knobs = new DashboardKnobController();
            double request;
            Check(!knobs.Apply(0, 900, 1000, 0.4, 0.1, out request), "Mismatched knob cannot jump loaded setting", failures);
            Check(!knobs.Apply(0, 350, 1000, 0.4, 0.1, out request) && !knobs.AwaitingTakeover[0], "Crossing picks up saved setting", failures);
            Check(knobs.Apply(0, 250, 1000, 0.4, 0.1, out request) && request < 0.4, "Picked-up knob follows movement", failures);
            knobs.RequireTakeover(0);
            Check(!knobs.Apply(0, 900, 1000, 0.3, 0.1, out request) && knobs.AwaitingTakeover[0], "Software override requires pickup again", failures);
            knobs.Calibration[1].minimum = 100;
            knobs.Calibration[1].maximum = 900;
            knobs.Calibration[1].configured = true;
            knobs.Calibration[1].inverted = true;
            Check(Math.Abs(knobs.Calibration[1].Normalize(300, 1023) - 0.75) < 0.000001, "Calibrated inversion", failures);
            Check(!knobs.Apply(1, 32768, 65535, 0.5, 0.1, out request, true) && !knobs.AwaitingTakeover[1], "Normalized bridge bypasses raw calibration", failures);
            return failures.ToArray();
        }

        public static void RunOrThrow()
        {
            string[] failures = Run();
            if (failures.Length != 0) throw new InvalidOperationException(string.Join("\n", failures));
        }

        private static void Check(bool condition, string description, List<string> failures)
        {
            if (!condition) failures.Add(description);
        }
    }
}
