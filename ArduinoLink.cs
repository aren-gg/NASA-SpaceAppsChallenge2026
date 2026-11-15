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

    readonly float[] knobs = new float[3] { 0.5f, 0.5f, 0.5f };
    readonly float[] knobsRaw = new float[3] { 0.5f, 0.5f, 0.5f };

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
    }

    void OnDestroy()
    {
        if (Connected)
        {
            // hand the lights back to the knobs and silence the buzzer
            outgoing.Enqueue("B0");
            outgoing.Enqueue("M1");
            Thread.Sleep(100);
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
    [Tooltip("Steps the needle travels from empty (0) to full (1). Measure this on your gauge. 28BYJ-48 = 4096 steps per full turn.")]
    public int gaugeFullSteps = 1024;
    int lastGaugeSteps = int.MinValue;

    /// <summary>Move the needle. value 0 = empty end, 1 = full end.</summary>
    public void SetGauge(float value01)
    {
        if (!Connected) return;
        int steps = Mathf.RoundToInt(Mathf.Clamp01(value01) * gaugeFullSteps);
        if (Mathf.Abs(steps - lastGaugeSteps) < 4) return;   // ignore tiny changes
        lastGaugeSteps = steps;
        outgoing.Enqueue("S" + steps);
    }

    /// <summary>Declare the needle's CURRENT position as zero (call once at start, with the needle at empty).</summary>
    public void ZeroGauge()
    {
        if (!Connected) return;
        lastGaugeSteps = 0;
        outgoing.Enqueue("Z");
    }

    // ---------- Internals ----------

    void ParseLine(string line)
    {
        line = line.Trim();
        if (line.Length < 2 || line[0] != 'P') return;
        string[] p = line.Split(',');
        if (p.Length != 4) return;                       // half-received line
        for (int i = 0; i < 3; i++)
        {
            int v;
            if (int.TryParse(p[i + 1], out v))
                knobsRaw[i] = Mathf.Clamp01(v / 1023f);
        }
    }

    void IoLoop()
    {
        // An Arduino Uno resets when the port opens. Wait for it to boot.
        Thread.Sleep(2000);
        try { port.DiscardInBuffer(); } catch { }
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
