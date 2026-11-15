using System;

namespace MarsTrainer.Sim
{
    // One successful launch per alert sol; simultaneous danger/flare alerts coalesce.
    public sealed class RecallDroneModel : ISimSystem, IDisposable
    {
        private readonly SimClock clock;
        private readonly SimEvents events;
        private readonly Func<bool> tryLaunch;
        private int pendingSol = -1, handledSol = -1;
        private double nextLaunchSol;
        private bool danger;
        public int Order => GameConstants.RecallSystemOrder;
        public RecallDroneModel(SimClock clock, SimEvents events, Func<bool> tryLaunch)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.tryLaunch = tryLaunch ?? throw new ArgumentNullException(nameof(tryLaunch));
            events.FlareAlert += OnFlare;
            events.SuitShieldChanged += OnShield;
        }
        private void OnFlare(int sol, int level) { if (level > 0 && sol > handledSol) pendingSol = sol; }
        private void OnShield(SuitShieldState state)
        {
            bool now = state >= SuitShieldState.Danger;
            if (now && !danger && clock.Sol > handledSol) pendingSol = clock.Sol;
            danger = now;
        }
        public void Step(GameState state, double dtSol)
        {
            Battery.ValidateNonnegative(dtSol, nameof(dtSol));
            if (dtSol == 0 || pendingSol < 0 || clock.ElapsedSols < nextLaunchSol || !tryLaunch()) return;
            handledSol = pendingSol;
            pendingSol = -1;
            nextLaunchSol = clock.ElapsedSols + GameConstants.RecallCooldownSols;
            events.ReportRecall();
        }
        public void Dispose()
        {
            events.FlareAlert -= OnFlare;
            events.SuitShieldChanged -= OnShield;
        }
    }
}
