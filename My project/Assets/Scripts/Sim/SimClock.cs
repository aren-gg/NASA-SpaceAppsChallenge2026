using System;

namespace MarsTrainer.Sim
{
    public sealed class SimClock
    {
        private int speed = 1;
        public double SecondsPerSol { get; }
        public double ElapsedSols { get; private set; }
        public int Sol => (int)ElapsedSols;
        public bool Paused { get; set; }
        public int Speed
        {
            get => speed;
            set
            {
                if (value != 1 && value != 2 && value != 4)
                    throw new ArgumentOutOfRangeException(nameof(value));
                speed = value;
            }
        }

        public SimClock(double runDurationSeconds = GameConstants.RunDurationSeconds)
        {
            if (double.IsNaN(runDurationSeconds) || double.IsInfinity(runDurationSeconds) || runDurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(runDurationSeconds));
            SecondsPerSol = runDurationSeconds / GameConstants.MarsYearSols;
        }

        public double Tick(double dtReal)
        {
            ValidateDelta(dtReal);
            if (Paused) return 0;
            double before = ElapsedSols;
            ElapsedSols = Math.Min(GameConstants.MissionDurationDays, before + dtReal / SecondsPerSol * speed);
            return ElapsedSols - before;
        }

        internal static void ValidateDelta(double dtReal)
        {
            if (double.IsNaN(dtReal) || double.IsInfinity(dtReal) || dtReal < 0)
                throw new ArgumentOutOfRangeException(nameof(dtReal));
        }

        // Simulation advances this clock only after stepping the same interval.
        internal void AdvanceTo(double sol) { ElapsedSols = sol; }
    }
}
