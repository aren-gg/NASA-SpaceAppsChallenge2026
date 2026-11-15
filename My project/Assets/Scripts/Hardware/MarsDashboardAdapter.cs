using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using MarsSurvival.Simulation;
using UnityEngine;

namespace MarsSurvival.Hardware
{
    public sealed class MarsDashboardAdapter : MonoBehaviour
    {
        [Min(1)] public float heartbeatTimeoutSeconds = 4;
        public bool Connected { get { return physicalSource && receivedInput; } }
        public bool EmulatorActive { get; private set; }
        public string Status { get; private set; } = "No Arduino panel attached; on-screen controls are available.";
        public string PortName { get; private set; } = "";
        public int RejectedPackets { get; private set; }
        public int[] RawValues { get { return knobs.RawValues; } }
        public double[] NormalizedValues { get { return knobs.NormalizedValues; } }
        public bool[] AwaitingTakeover { get { return knobs.AwaitingTakeover; } }
        public DashboardCalibration[] Calibration { get { return knobs.Calibration; } }

        public event Action<double[], double[], string> FeedbackReady;

        private readonly DashboardKnobController knobs = new DashboardKnobController();
        private readonly DashboardSession emulatorSession = new DashboardSession();
        private readonly ConcurrentQueue<Reading> inbox = new ConcurrentQueue<Reading>();
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private Func<double[]> logicalSettings;
        private Action<PowerChannel, double> apply;
        private Action<string> lostConnection;
        private volatile bool physicalSource;
        private bool receivedInput, hasSequence;
        private int epoch, queuedCount, generatedSequence;
        private uint lastSequence;
        private double lastReceipt, previousReceipt, feedbackTime;
        private readonly double[] delivered = new double[3];
        private string missionPhase = "Ready";
        private const int MaximumQueuedReadings = 128;
        private const string CalibrationKey = "MarsDashboard.Calibration.v1";

        private struct Reading
        {
            public int epoch, life, temperature, shielding, maximum;
            public uint sequence;
            public double receivedAt;
            public bool normalized;
        }

        [Serializable]
        private sealed class SavedCalibration
        {
            public DashboardCalibration[] channels;
        }

        private void Awake()
        {
            LoadCalibration();
        }

        public void Bind(Func<double[]> settings, Action<PowerChannel, double> command, Action<string> disconnect)
        {
            logicalSettings = settings;
            apply = command;
            lostConnection = disconnect;
            RequireTakeover();
        }

        // The existing Arduino reader calls this only after opening its real connection.
        public void AttachPhysicalSource(string connectionName)
        {
            ResetSource();
            physicalSource = true;
            PortName = string.IsNullOrWhiteSpace(connectionName) ? "Arduino Uno" : connectionName;
            lastReceipt = timer.Elapsed.TotalSeconds;
            // The physical knobs are the controls: they take over straight away.
            knobs.Release();
            Status = "Waiting for Arduino readings: " + PortName;
        }

        public void BeginEmulator()
        {
            bool wasPhysical = physicalSource;
            ResetSource();
            EmulatorActive = true;
            Status = "Software emulator; no physical dashboard connected.";
            if (wasPhysical && lostConnection != null) lostConnection("Switched to the software dashboard emulator.");
        }

        public void Disconnect()
        {
            bool wasPhysical = physicalSource;
            ResetSource();
            Status = "Dashboard disconnected. Last settings retained; use the on-screen controls.";
            if (wasPhysical && lostConnection != null) lostConnection(Status);
        }

        public void RequireTakeover()
        {
            // With the panel attached the knobs stay in charge (for example across a new or
            // loaded mission); only an on-screen slider change makes a knob catch up first.
            if (physicalSource) return;
            knobs.RequireTakeover();
        }

        public void NotifySoftwareOverride(PowerChannel channel)
        {
            knobs.RequireTakeover((int)channel);
        }

        public bool SetCalibration(PowerChannel channel, int minimum, int maximum, bool inverted = false)
        {
            int index = (int)channel;
            if (index < 0 || index > 2 || minimum < 0 || maximum <= minimum || maximum > 65535) return false;
            DashboardCalibration calibration = knobs.Calibration[index];
            calibration.minimum = minimum;
            calibration.maximum = maximum;
            calibration.inverted = inverted;
            calibration.configured = true;
            knobs.RequireTakeover(index);
            PlayerPrefs.SetString(CalibrationKey, JsonUtility.ToJson(new SavedCalibration { channels = knobs.Calibration }));
            PlayerPrefs.Save();
            return true;
        }

        // These entry points are safe for the future serial reader's worker thread.
        public bool FeedNormalized(double life, double temperature, double shielding)
        {
            if (!Normalized(life) || !Normalized(temperature) || !Normalized(shielding)) return false;
            uint sequence = unchecked((uint)Interlocked.Increment(ref generatedSequence));
            return QueueReading((int)Math.Round(life * 65535), (int)Math.Round(temperature * 65535),
                (int)Math.Round(shielding * 65535), 65535, sequence, true);
        }

        public bool FeedRawReadings(int life, int temperature, int shielding, int maximum = 1023)
        {
            uint sequence = unchecked((uint)Interlocked.Increment(ref generatedSequence));
            return FeedRawReadings(life, temperature, shielding, maximum, sequence);
        }

        public bool FeedRawReadings(int life, int temperature, int shielding, int maximum, uint sequence)
        {
            return QueueReading(life, temperature, shielding, maximum, sequence, false);
        }

        private bool QueueReading(int life, int temperature, int shielding, int maximum, uint sequence, bool normalized)
        {
            if ((!physicalSource && !EmulatorActive) || maximum < 1 || maximum > 65535 ||
                life < 0 || temperature < 0 || shielding < 0 || life > maximum || temperature > maximum || shielding > maximum) return false;
            Reading reading = new Reading
            {
                epoch = epoch, life = life, temperature = temperature, shielding = shielding,
                maximum = maximum, sequence = sequence, receivedAt = timer.Elapsed.TotalSeconds, normalized = normalized
            };
            inbox.Enqueue(reading);
            Interlocked.Increment(ref queuedCount);
            Reading discarded;
            while (Volatile.Read(ref queuedCount) > MaximumQueuedReadings && inbox.TryDequeue(out discarded))
                Interlocked.Decrement(ref queuedCount);
            return true;
        }

        // Optional test format only. The user's existing sketch does not have to emit it.
        public bool FeedEmulatorLine(string line)
        {
            if (!EmulatorActive) return false;
            DashboardPacket packet;
            string error;
            bool newSession;
            if (!DashboardProtocol.TryParse(line, out packet, out error) || !emulatorSession.Accept(packet, out newSession))
            {
                RejectedPackets++;
                return false;
            }
            if (newSession)
            {
                Interlocked.Increment(ref epoch);
                Reading discarded;
                while (inbox.TryDequeue(out discarded)) Interlocked.Decrement(ref queuedCount);
                hasSequence = false;
                RequireTakeover();
            }
            if (packet.kind != DashboardPacketKind.Knobs) return true;
            return FeedRawReadings(packet.life, packet.temperature, packet.shielding, emulatorSession.AdcMaximum, packet.sequence);
        }

        public void SetFeedback(double[] deliveredKw, string phase)
        {
            if (deliveredKw != null && deliveredKw.Length >= 3)
                for (int i = 0; i < 3; i++) delivered[i] = Math.Max(0, deliveredKw[i]);
            missionPhase = phase;
        }

        private void Update()
        {
            double now = timer.Elapsed.TotalSeconds;
            Reading reading;
            int processed = 0;
            while (processed++ < MaximumQueuedReadings && inbox.TryDequeue(out reading))
            {
                Interlocked.Decrement(ref queuedCount);
                if (reading.epoch != epoch || now - reading.receivedAt > heartbeatTimeoutSeconds ||
                    (hasSequence && !DashboardProtocol.IsNewer(reading.sequence, lastSequence)))
                {
                    RejectedPackets++;
                    continue;
                }
                hasSequence = true;
                lastSequence = reading.sequence;
                receivedInput = true;
                lastReceipt = reading.receivedAt;
                double dt = previousReceipt == 0 ? 0.1 : Math.Max(0.001, reading.receivedAt - previousReceipt);
                previousReceipt = reading.receivedAt;
                double[] logical = logicalSettings == null ? null : logicalSettings();
                if (logical == null || logical.Length < 3 || apply == null) continue;
                ApplyReading(0, reading.life, reading.maximum, logical[0], dt, reading.normalized);
                ApplyReading(1, reading.temperature, reading.maximum, logical[1], dt, reading.normalized);
                ApplyReading(2, reading.shielding, reading.maximum, logical[2], dt, reading.normalized);
                Status = physicalSource ? "Arduino connected: " + PortName : "Software emulator active.";
            }
            if (physicalSource && now - lastReceipt > heartbeatTimeoutSeconds)
            {
                Disconnect();
                Status = "Arduino readings stopped. Settings retained; fast-forward stopped.";
            }
            if (now - feedbackTime >= 0.25 && FeedbackReady != null && logicalSettings != null)
            {
                double[] logical = logicalSettings();
                if (logical != null && logical.Length >= 3)
                    FeedbackReady((double[])logical.Clone(), (double[])delivered.Clone(), missionPhase);
                feedbackTime = now;
            }
        }

        private void ApplyReading(int channel, int raw, int maximum, double logical, double seconds, bool normalized)
        {
            double value;
            if (knobs.Apply(channel, raw, maximum, logical, seconds, out value, normalized)) apply((PowerChannel)channel, value);
        }

        private void ResetSource()
        {
            Interlocked.Increment(ref epoch);
            physicalSource = false;
            EmulatorActive = false;
            receivedInput = false;
            hasSequence = false;
            previousReceipt = 0;
            PortName = "";
            emulatorSession.Reset();
            RequireTakeover();
            Reading discarded;
            while (inbox.TryDequeue(out discarded)) Interlocked.Decrement(ref queuedCount);
        }

        private void LoadCalibration()
        {
            string json = PlayerPrefs.GetString(CalibrationKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                SavedCalibration saved = JsonUtility.FromJson<SavedCalibration>(json);
                if (saved == null || saved.channels == null || saved.channels.Length != 3) return;
                for (int i = 0; i < 3; i++)
                    if (saved.channels[i] != null && saved.channels[i].IsValid) knobs.Calibration[i] = saved.channels[i];
            }
            catch (Exception)
            {
                Status = "Stored dashboard calibration was invalid; using device ranges.";
            }
        }

        private static bool Normalized(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
        }

        private void OnDisable()
        {
            Disconnect();
        }
    }
}
