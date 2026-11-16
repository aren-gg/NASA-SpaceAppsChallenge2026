using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Serial bridge to the Arduino control panel (115200 baud).
///
/// Arduino -> Unity:  "P,left,middle,right"  (0-1023, only sent when a knob moves)
/// Unity -> Arduino:  "M0"        game controls the LEDs (instead of the knobs)
///                    "T<g><c>"   group 1-3, colour R/Y/G/O   e.g. "T2R"
///                    "B<f>,<ms>" beep                         e.g. "B880,200"
///
/// Requires: Project Settings > Player > Api Compatibility Level = .NET Framework
/// Desktop builds only (does not work in WebGL).
/// </summary>
public class ArduinoLink : MonoBehaviour
{
    [Tooltip("Leave empty to auto-pick. Windows: COM3. Mac: /dev/cu.usbmodem1101")]
    public string portName = "";
    public int baudRate = 115200;

    // Knob positions, 0..1. Index 0 = left, 1 = middle, 2 = right.
    public float Left   => knobs[0];
    public float Middle => knobs[1];
    public float Right  => knobs[2];
    public float GetKnob(int i) => knobs[i];

    public bool Connected { get; private set; }
    public string Status  { get; private set; } = "Not connected";

    /// <summary>True once the sketch has sent at least one knob reading.</summary>
    public bool HasKnobReading { get; private set; }
    /// <summary>The first raw reading of each knob (0..1); the knobs are fully counter-clockwise at start.</summary>
    public float GetFirstKnob(int i) => firstKnobs[i];

    // The knobs are always turned fully counter-clockwise when the game starts, and the sketch
    // only reports a knob once it moves, so until then assume that end (raw 0).
    readonly float[] knobs = new float[3];
    readonly float[] knobsRaw = new float[3];
    readonly float[] firstKnobs = new float[3];

    SerialPort port;
    Thread thread;
    volatile bool running;
    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
    readonly char[] lastColor = new char[3];   // so we only send a light command when it changes

    // ---------- Unity lifecycle ----------

    void Start()
    {
        try
        {
            string name = string.IsNullOrEmpty(portName) ? AutoPickPort() : portName;
            if (name == null) { Status = "No serial port found - using virtual dashboard"; return; }

            port = new SerialPort(name, baudRate)
            {
                ReadTimeout = 20,
                WriteTimeout = 200,
                NewLine = "\n",
                DtrEnable = true
            };
            port.Open();
            running = true;
            thread = new Thread(IoLoop) { IsBackground = true };
            thread.Start();
            Connected = true;
            Status = "Connected on " + name;
        }
        catch (Exception e)
        {
            Connected = false;
            Status = "No Arduino (" + e.Message + ") - using virtual dashboard";
            Debug.LogWarning(Status);
        }
    }

    void Update()
    {
        string line;
        while (incoming.TryDequeue(out line))
            ParseLine(line);

        // light smoothing so knob jitter doesn't make the game flicker
        for (int i = 0; i < 3; i++)
            knobs[i] = Mathf.Lerp(knobs[i], knobsRaw[i], 0.3f);

        FlushGauge();
    }

    void OnDestroy()
    {
        if (Connected)
        {
            // park the needle at empty (so ZeroGauge is right next time), hand the
            // lights back to the knobs and silence the buzzer
            outgoing.Enqueue("S0");
            outgoing.Enqueue("B0");
            outgoing.Enqueue("M1");
            // The sketch takes one half-step every 1.2 ms, so a full 2048-step return takes
            // about 2.5 s. Keep the port open until the needle is home, so the next game's
            // zero really is energy 0.
            int parkMs = Mathf.Abs(GaugeSentSteps) * 12 / 10 + 200;
            Thread.Sleep(Mathf.Clamp(parkMs, 100, 3000));
        }
        running = false;
        if (thread != null) thread.Join(300);
        if (port != null && port.IsOpen) port.Close();
    }

    // ---------- Public commands (game -> Arduino) ----------

    /// <summary>group 1..3 (left, middle, right); colour 'R','Y','G' or 'O' (off).</summary>
    public void SetLight(int group, char color)
    {
        if (!Connected || group < 1 || group > 3) return;
        if (lastColor[group - 1] == color) return;
        lastColor[group - 1] = color;
        outgoing.Enqueue("T" + group + color);
    }

    /// <summary>Single beep. Note: the Arduino also beeps by itself whenever any light is red.</summary>
    public void Beep(int freqHz, int ms)
    {
        if (Connected) outgoing.Enqueue("B" + freqHz + "," + ms);
    }

    public void StopBeep() { if (Connected) outgoing.Enqueue("B0"); }

    // ----- Stepper gauge (needle) -----
    [Header("Stepper gauge")]
    [Tooltip("Steps the needle travels from empty (0) to full (1), i.e. half a turn (180 degrees) clockwise. Measured on the dashboard: 2048 steps is half a turn (about 20 steps per 1% of battery).")]
    public int gaugeFullSteps = 2048;
    [Tooltip("Tick if the stepper turns counter-clockwise for positive steps: positions are then sent negative.")]
    public bool reverseGaugeDirection;
    [Tooltip("Seconds between needle commands. Sending one every frame overflows the Uno's 64-byte serial buffer while it is stepping, and lost commands left the needle short of empty.")]
    public float gaugeSendInterval = 0.2f;
    [Tooltip("Seconds between repeats of the current needle target, so a lost or garbled command corrects itself.")]
    public float gaugeResendInterval = 1f;
    int lastGaugeSteps = int.MinValue, gaugeTarget;
    bool gaugeHasTarget;
    int gaugeCommandsSent;

    /// <summary>Diagnostics: the last needle position sent (steps) and how many needle commands have gone out.</summary>
    public int GaugeSentSteps => lastGaugeSteps == int.MinValue ? 0 : lastGaugeSteps;
    public int GaugeCommandsSent => gaugeCommandsSent;
    float lastGaugeSend = -100f;

    /// <summary>Move the needle. value 0 = empty end, 1 = full end. Sent at a limited rate from Update.</summary>
    public void SetGauge(float value01)
    {
        if (!Connected) return;
        gaugeTarget = Mathf.RoundToInt(Mathf.Clamp01(value01) * gaugeFullSteps) * (reverseGaugeDirection ? -1 : 1);
        gaugeHasTarget = true;
    }

    /// <summary>Declare the needle's CURRENT position as zero (call once at start, with the needle at empty).</summary>
    public void ZeroGauge()
    {
        if (!Connected) return;
        lastGaugeSteps = 0;
        gaugeTarget = 0;
        gaugeHasTarget = false;
        lastGaugeSend = Time.unscaledTime;
        outgoing.Enqueue("Z");
    }

    // Targets are absolute step positions, so sending the latest one again is always safe.
    void FlushGauge()
    {
        if (!Connected || !gaugeHasTarget) return;
        float since = Time.unscaledTime - lastGaugeSend;
        bool changed = gaugeTarget != lastGaugeSteps;
        if (changed ? since < gaugeSendInterval : since < gaugeResendInterval) return;
        lastGaugeSteps = gaugeTarget;
        lastGaugeSend = Time.unscaledTime;
        gaugeCommandsSent++;
        outgoing.Enqueue("S" + gaugeTarget);
    }

    // ---------- Internals ----------

    void ParseLine(string line)
    {
        line = line.Trim();
        if (line.Length < 2 || line[0] != 'P') return;
        string[] p = line.Split(',');
        if (p.Length != 4) return;                       // half-received line
        float[] values = new float[3];
        for (int i = 0; i < 3; i++)
        {
            int v;
            if (!int.TryParse(p[i + 1], out v)) return;
            values[i] = Mathf.Clamp01(v / 1023f);
        }
        bool first = !HasKnobReading;
        for (int i = 0; i < 3; i++)
        {
            knobsRaw[i] = values[i];
            if (first) { firstKnobs[i] = values[i]; knobs[i] = values[i]; }
        }
        HasKnobReading = true;
    }

    void IoLoop()
    {
        // An Arduino Uno resets when the port opens. Wait for it to boot.
        Thread.Sleep(2000);
        // Keep what arrived during the wait: the sketch reports every knob once at boot (and then
        // only when one moves), and that first reading is where the knobs really start.
        // A half line from the reset is rejected by ParseLine.
        outgoing.Enqueue("M0");   // game now drives the LEDs

        while (running)
        {
            try
            {
                string cmd;
                while (outgoing.TryDequeue(out cmd))
                    port.WriteLine(cmd);

                incoming.Enqueue(port.ReadLine());
            }
            catch (TimeoutException) { }   // normal: nothing arrived in 20 ms
            catch (Exception) { Thread.Sleep(10); }
        }
    }

    static string AutoPickPort()
    {
        foreach (string n in SerialPort.GetPortNames())
        {
            // Mac/Linux Arduino ports; on Windows just take the first COM port
            if (n.Contains("usbmodem") || n.Contains("usbserial") || n.Contains("ttyACM") || n.Contains("ttyUSB"))
                return n;
        }
        string[] all = SerialPort.GetPortNames();
        return (all.Length > 0 && all[0].StartsWith("COM")) ? all[0] : null;
    }
}
