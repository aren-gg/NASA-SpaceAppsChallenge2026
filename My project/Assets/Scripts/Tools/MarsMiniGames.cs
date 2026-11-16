using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>How the tool HUD draws a mini-game.</summary>
    public enum MiniGameView { Dial, Heat, Soak, Scan }

    /// <summary>
    /// A short hands-on challenge that replaces "stand still while a bar fills". The tool belt
    /// feeds it the mouse button each frame; when it is <see cref="Done"/> the task happens.
    /// </summary>
    public abstract class MiniGame
    {
        public bool Done, Failed;
        /// <summary>One line for the prompt under the crosshair.</summary>
        public string Status = "";
        /// <summary>Values the HUD draws (0-1 unless noted).</summary>
        public float Needle, ZoneStart, ZoneEnd, Fill, Heat;
        public int Count, Total;
        public bool Jammed;
        /// <summary>Time of the last miss or success, for HUD flashes.</summary>
        public float MissedAt = -10f, HitAt = -10f;

        public abstract MiniGameView View { get; }
        public virtual float Progress { get { return Total > 0 ? (float)Count / Total : Fill; } }

        /// <summary>Advances one frame. <paramref name="pressed"/>: clicked this frame; <paramref name="held"/>: button down.</summary>
        public abstract void Tick(float dt, bool pressed, bool held, bool released);
    }

    /// <summary>
    /// A needle sweeps back and forth; click while it is inside the green zone. Each hit tightens
    /// a bolt (or scores a reading) and moves the zone; a miss is shown and costs nothing but time.
    /// </summary>
    public sealed class TimingGame : MiniGame
    {
        private readonly float speed, width;
        private readonly MiniGameView view;
        private readonly string noun;
        private float phase;

        public TimingGame(int total, float speed, float width, MiniGameView view, string noun)
        {
            Total = total;
            this.speed = speed;
            this.width = width;
            this.view = view;
            this.noun = noun;
            NewZone();
            Status = "Click when the needle is in the green  (" + noun + " 1/" + total + ")";
        }

        public override MiniGameView View { get { return view; } }

        private void NewZone()
        {
            ZoneStart = Random.Range(0.08f, 0.92f - width);
            ZoneEnd = ZoneStart + width;
        }

        public override void Tick(float dt, bool pressed, bool held, bool released)
        {
            phase += dt * speed * (1f + 0.15f * Count);
            Needle = Mathf.PingPong(phase, 1f);
            if (!pressed) return;
            if (Needle >= ZoneStart && Needle <= ZoneEnd)
            {
                Count++;
                HitAt = Time.unscaledTime;
                MarsSfx.Play(MarsSfx.Sound.Success, 0.7f);
                if (Count >= Total) { Done = true; Status = "Done!"; return; }
                NewZone();
                Status = "Good!  " + noun + " " + (Count + 1) + "/" + Total;
            }
            else
            {
                MissedAt = Time.unscaledTime;
                MarsSfx.Play(MarsSfx.Sound.Deny, 0.6f);
                Status = "Missed: wait for the green  (" + noun + " " + (Count + 1) + "/" + Total + ")";
            }
        }
    }

    /// <summary>
    /// Core drilling: hold to drill deeper, but the bit heats up. Let go before it reaches the
    /// red or it jams for two seconds and loses some depth.
    /// </summary>
    public sealed class HeatGame : MiniGame
    {
        private float jamTimer;
        public override MiniGameView View { get { return MiniGameView.Heat; } }

        public HeatGame() { Status = "Hold to drill. Let go before the heat reaches the red"; }

        public override void Tick(float dt, bool pressed, bool held, bool released)
        {
            if (Jammed)
            {
                jamTimer -= dt;
                Heat = Mathf.Max(0f, Heat - dt * 0.2f);
                if (jamTimer <= 0f) { Jammed = false; Status = "Bit free. Hold to drill again"; }
                return;
            }
            if (held)
            {
                Fill = Mathf.Min(1f, Fill + dt / 3.5f);
                Heat += dt * 0.5f;
                Status = "Drilling  " + Mathf.RoundToInt(Fill * 100f) + "%";
            }
            else Heat = Mathf.Max(0f, Heat - dt * 0.7f);
            if (Heat >= 1f)
            {
                Jammed = true;
                jamTimer = 2f;
                Heat = 0.9f;
                Fill = Mathf.Max(0f, Fill - 0.12f);
                MissedAt = Time.unscaledTime;
                MarsSfx.Play(MarsSfx.Sound.Deny, 0.8f);
                Status = "Overheated! The bit is jammed";
            }
            if (Fill >= 1f) { Done = true; Status = "Core extracted!"; }
        }
    }

    /// <summary>
    /// Watering: hold to soak the bed and let go inside the green band. Too little dries out
    /// early; too much waterlogs the roots.
    /// </summary>
    public sealed class SoakGame : MiniGame
    {
        public const float BandLow = 0.7f, BandHigh = 1.15f, Max = 1.4f;
        private bool sprayed;
        public override MiniGameView View { get { return MiniGameView.Soak; } }

        public SoakGame(float start)
        {
            Fill = Mathf.Clamp(start, 0f, Max);
            ZoneStart = BandLow / Max;
            ZoneEnd = BandHigh / Max;
            Status = "Hold to spray. Let go in the green band";
        }

        public override float Progress { get { return Fill / Max; } }

        public override void Tick(float dt, bool pressed, bool held, bool released)
        {
            if (held)
            {
                sprayed = true;
                Fill = Mathf.Min(Max, Fill + dt * 0.45f);
                Status = Fill > BandHigh ? "Too much! Let go" : Fill >= BandLow ? "Good, let go now" : "Soaking...";
                if (Fill >= Max) Done = true;
            }
            else if (sprayed && released)
            {
                if (Fill >= BandLow) Done = true;
                else Status = "Not enough yet. Keep spraying";
            }
        }
    }
}
