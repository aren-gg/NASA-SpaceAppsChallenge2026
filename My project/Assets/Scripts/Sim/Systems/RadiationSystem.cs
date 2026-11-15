using System;

namespace MarsTrainer.Sim.Systems
{
    public sealed class RadiationSystem : ISimSystem
    {
        private readonly SimClock clock;
        private readonly Func<int, RadiationConditions> schedule;
        private readonly SimModifiers modifiers;
        private readonly SimEvents events;
        private readonly Func<double> shieldPower;
        private int lastFlareSol = -1;
        private double suitShield = GameConstants.SuitShield;
        public int Order => GameConstants.RadiationSystemOrder;
        public double DoseRateMSvPerSol { get; private set; }
        public SuitShieldState ShieldState { get; private set; }
        public double SuitShield
        {
            get => suitShield;
            set { Battery.ValidateNonnegative(value, nameof(value)); suitShield = value; }
        }

        // Register after EnergySystem. Its ShieldPowerFraction supplies the powered suit fraction.
        public RadiationSystem(SimClock clock, Func<int, RadiationConditions> schedule,
            SimModifiers modifiers, SimEvents events, Func<double> shieldPower = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.shieldPower = shieldPower;
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Battery.ValidateNonnegative(dtSol, nameof(dtSol));
            if (dtSol == 0) return;
            double time = clock.ElapsedSols, end = time + dtSol;
            while (time < end)
            {
                int sol = (int)Math.Floor(time);
                var row = schedule(sol);
                double delta = Math.Min(end, sol + 1) - time;
                double powered = shieldPower == null ? 1 : shieldPower();
                Battery.ValidateNonnegative(powered, nameof(shieldPower));
                double shield = Math.Max(0, Math.Min(GameConstants.ShieldMaximum,
                    modifiers.Apply(GameConstants.RadiationShieldStat, SuitShield * Math.Min(1, powered))));
                double flare = row.FlareLevel == 3 ? GameConstants.FlareHighDoseUGy
                    : row.FlareLevel == 2 ? GameConstants.FlareMediumDoseUGy
                    : row.FlareLevel == 1 ? GameConstants.FlareLowDoseUGy : 0;
                DoseRateMSvPerSol = (row.GcrDoseUGy + flare) * (1 - shield)
                    * GameConstants.RadiationQualityFactor / GameConstants.MicroSvPerMilliSv;
                double before = state.RadiationDoseMSv;
                state.RadiationDoseMSv += DoseRateMSvPerSol * delta;
                // Integrate the linear excess-dose drain exactly, including threshold crossings.
                double threshold = GameConstants.H_limit * GameConstants.ShieldDangerFraction;
                double startExcess = Math.Max(0, before - threshold);
                double endExcess = Math.Max(0, state.RadiationDoseMSv - threshold);
                double exposed = DoseRateMSvPerSol > 0 ? Math.Min(delta, endExcess / DoseRateMSvPerSol) : delta;
                if (!state.HealthManaged)
                {
                    state.LastDamageCause = endExcess > 0 ? "radiation" : state.LastDamageCause;
                    state.Health -= (startExcess + endExcess) * exposed / 2
                    / GameConstants.H_limit * GameConstants.RadiationHpDrainPerSol;
                }
                double fraction = state.RadiationDoseMSv / GameConstants.H_limit;
                var next = fraction >= GameConstants.ShieldCriticalFraction ? SuitShieldState.Critical
                    : fraction >= GameConstants.ShieldDangerFraction ? SuitShieldState.Danger
                    : fraction >= GameConstants.ShieldWarnFraction ? SuitShieldState.Warn : SuitShieldState.Safe;
                if (next != ShieldState) { ShieldState = next; events.ReportShieldState(next); }
                if (row.FlareLevel > 0 && lastFlareSol != sol)
                {
                    lastFlareSol = sol;
                    events.ReportFlareAlert(sol, row.FlareLevel);
                }
                events.ReportRadiation(DoseRateMSvPerSol, state.RadiationDoseMSv);
                time += delta;
            }
        }
    }
}
