using System;
using System.IO.Ports;
using MarsSurvival.Gameplay;
using MarsSurvival.Simulation;
using UnityEngine;

namespace MarsSurvival.Hardware
{
    /// <summary>
    /// Connects the physical control panel (through <see cref="ArduinoLink"/>) to the mission:
    /// <list type="bullet">
    /// <item>Knobs left / middle / right set the Life control, Temperature and Radiation
    /// shielding power requests. They are always fully counter-clockwise (zero power) when the
    /// game starts, and turning one clockwise sends more power to that system.</item>
    /// <item>Each LED group shows its channel: green when the power put in meets or exceeds the
    /// useful need, yellow when it is 1-15% short, red when more than 15% short.</item>
    /// <item>The stepper needle shows total stored battery energy (kWh / capacity). It always
    /// sits at energy 0 when the game starts; energy 100% is half a turn (180 degrees)
    /// clockwise from there.</item>
    /// <item>The buzzer sounds when something goes wrong (the sketch also buzzes on any red LED).</item>
    /// </list>
    /// If no Arduino is found the game runs normally with the on-screen sliders.
    /// </summary>
    [RequireComponent(typeof(ArduinoLink))]
    public sealed class MarsArduinoBridge : MonoBehaviour
    {
        [Tooltip("Serial port of the Arduino. Used when present; otherwise the first Arduino-like port is tried.")]
        public string preferredPort = "COM7";
        [Tooltip("Shortfall (percent of need) up to which the light is yellow; beyond it the light is red.")]
        public float yellowShortPercent = 15f;
        [Tooltip("Shortfall (percent of need) still shown green, so rounding never flickers yellow at exactly the need.")]
        public float greenTolerancePercent = 1f;
        [Tooltip("Off: turning a knob clockwise increases that system's power. Each knob's wiring direction is also checked from its first reading, since the knobs start fully counter-clockwise.")]
        public bool counterClockwiseIncreases = false;
        [Tooltip("Tick if the needle turns counter-clockwise as energy rises; it then steps the other way from the same zero.")]
        public bool invertGauge;
        [Tooltip("The needle only moves once the battery has changed by at least this many percent.")]
        [Min(0.1f)] public float gaugeStepPercent = 1f;
        [Tooltip("Seconds between repeated alarm beeps while a problem lasts.")]
        public float alarmRepeatSeconds = 4f;

        public ArduinoLink Link { get; private set; }
        public string Status { get { return Link == null ? "" : Link.Status; } }

        private MarsMissionController mission;
        private bool attached, zeroed;
        private float connectedAt = -1, nextAlarm;
        private string lastWarning = "";
        private MissionPhase lastPhase = MissionPhase.Running;
        private readonly float[] lastKnob = { -1, -1, -1 };
        // Per knob: true when its potentiometer reads high at the counter-clockwise end.
        private readonly bool[] reversedWiring = new bool[3];
        private bool wiringChecked;
        // Battery share the needle currently shows; it only moves once the battery is 1% away.
        private float shownFraction = -1f;
        private float lastFeed;

        private void Awake()
        {
            Link = GetComponent<ArduinoLink>();
            // ArduinoLink opens its port in Start, so choose it now.
            if (string.IsNullOrEmpty(Link.portName))
            {
                string[] ports = new string[0];
                try { ports = SerialPort.GetPortNames(); } catch (Exception) { }
                if (Array.IndexOf(ports, preferredPort) >= 0) Link.portName = preferredPort;
            }
        }

        private void Update()
        {
            if (mission == null) mission = MarsMissionController.Instance;
            if (Link == null || !Link.Connected || mission == null || mission.Dashboard == null) return;
            if (connectedAt < 0) connectedAt = Time.unscaledTime;
            // The Uno resets when the port opens; its IoLoop waits 2 s before talking.
            if (Time.unscaledTime - connectedAt < 2.5f) return;

            if (!attached)
            {
                mission.Dashboard.AttachPhysicalSource(Link.portName == "" ? "Arduino" : Link.portName);
                attached = true;
            }
            if (!zeroed)
            {
                // The needle is parked at empty when the game quits, so call that zero.
                Link.ZeroGauge();
                zeroed = true;
            }

            FeedKnobs();
            DriveLights();
            DriveGauge();
            DriveAlarm();
        }

        private void FeedKnobs()
        {
            // Feed whenever a knob moves, and at least every 0.5 s to keep the dashboard's
            // heartbeat alive (the sketch only reports when a knob turns).
            bool moved = false;
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(Link.GetKnob(i) - lastKnob[i]) > 0.002f) moved = true;
            if (!moved && Time.unscaledTime - lastFeed < 0.5f) return;
            for (int i = 0; i < 3; i++) lastKnob[i] = Link.GetKnob(i);
            lastFeed = Time.unscaledTime;
            if (!wiringChecked && Link.HasKnobReading)
            {
                // Every knob starts fully counter-clockwise, so a first reading near the top of
                // the range means that potentiometer is wired backwards: flip it so it still
                // starts at zero power and gains power clockwise.
                for (int i = 0; i < 3; i++) reversedWiring[i] = Link.GetFirstKnob(i) > 0.5f;
                wiringChecked = true;
            }
            mission.Dashboard.FeedNormalized(Knob(0), Knob(1), Knob(2));
        }

        private float Knob(int index)
        {
            float value = Mathf.Clamp01(Link.GetKnob(index));
            // A potentiometer normally reads higher clockwise, so clockwise = more power.
            bool flip = counterClockwiseIncreases ^ reversedWiring[index];
            return flip ? 1f - value : value;
        }

        /// <summary>
        /// LED colour for one channel: G when supplied >= needed, Y when short by 1-15% of the need,
        /// R when short by more than that.
        /// </summary>
        public static char LightFor(double suppliedKw, double neededKw, double yellowPercent, double greenTolerance)
        {
            if (neededKw <= 0.01) return 'G';
            double shortPercent = (neededKw - suppliedKw) / neededKw * 100;
            if (shortPercent < greenTolerance) return 'G';
            if (shortPercent <= yellowPercent) return 'Y';
            return 'R';
        }

        /// <summary>
        /// Power going into each channel right now, from the current knob settings and the supply
        /// share of the last simulation step, so the lights answer a knob at once (even when paused).
        /// </summary>
        private static double Supplied(MissionSimulation sim, int channel)
        {
            double[] rated = { sim.Settings.lifeRatedKw, sim.Settings.thermalRatedKw, sim.Settings.shieldingRatedKw };
            double requested = sim.Power.requestedKw[0] + sim.Power.requestedKw[1] + sim.Power.requestedKw[2];
            double delivered = sim.Power.deliveredKw[0] + sim.Power.deliveredKw[1] + sim.Power.deliveredKw[2];
            double share = requested > 1e-6 ? delivered / requested : 1;
            return sim.State.knobs[channel] * rated[channel] * share;
        }

        private void DriveLights()
        {
            MissionSimulation sim = mission.Simulation;
            for (int i = 0; i < 3; i++)
            {
                char c = sim == null || !mission.IsRunning ? 'O' : LightFor(Supplied(sim, i), sim.Power.neededKw[i], yellowShortPercent, greenTolerancePercent);
                Link.SetLight(i + 1, c);
            }
        }

        /// <summary>Needle = total stored battery energy as a share of the battery capacity.</summary>
        private void DriveGauge()
        {
            MissionSimulation sim = mission.Simulation;
            float fraction = sim == null ? 0f : Mathf.Clamp01((float)(sim.State.resources.batteryKwh / Math.Max(0.001, sim.BatteryCapacity)));
            // Move the needle only in whole 1% steps of battery, so it ticks visibly instead of
            // creeping a step at a time. Empty and full are always shown exactly.
            bool atEnd = (fraction <= 0f || fraction >= 1f) && fraction != shownFraction;
            if (shownFraction < 0f || atEnd || Mathf.Abs(fraction - shownFraction) >= gaugeStepPercent / 100f)
                shownFraction = fraction;
            // Energy 0 is always the start position (zeroed on connect); full is 180 degrees on.
            Link.reverseGaugeDirection = invertGauge;
            Link.SetGauge(shownFraction);
        }

        private void DriveAlarm()
        {
            MissionSimulation sim = mission.Simulation;
            if (sim == null) return;
            MissionState s = sim.State;
            if (s.phase != lastPhase)
            {
                lastPhase = s.phase;
                if (s.phase == MissionPhase.Lost) { Link.Beep(330, 1500); return; }
                if (s.phase == MissionPhase.Won) { Link.Beep(1320, 400); return; }
            }
            if (!mission.IsRunning) return;

            string warning = sim.Warnings.Count > 0 ? sim.Warnings[0] : "";
            bool outside = mission.Context.zone == PlayerZone.Outdoors;
            bool critical = s.astronaut.health < 35 || (outside && s.resources.suitOxygenGrams < 300) ||
                            s.resources.batteryKwh < sim.BatteryCapacity * 0.1;
            if (warning != "" && warning != lastWarning)
            {
                Link.Beep(988, 250);
                nextAlarm = Time.unscaledTime + alarmRepeatSeconds;
            }
            else if (critical && Time.unscaledTime >= nextAlarm)
            {
                Link.Beep(1400, 400);
                nextAlarm = Time.unscaledTime + alarmRepeatSeconds;
            }
            lastWarning = warning;
        }
    }
}
