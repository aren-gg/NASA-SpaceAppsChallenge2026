using System;
using System.Collections.Generic;
using MarsTrainer.Sim.Events;

namespace MarsTrainer.Sim.Systems
{
    public sealed class WeatherSystem : ISimSystem
    {
        private readonly SimClock clock;
        private readonly WeatherConditions[] rows;
        private readonly SimModifiers modifiers;
        private readonly SimEvents bus;
        private readonly Func<bool> sheltered;
        public int Order => GameConstants.WeatherPreparationOrder;
        public IReadOnlyList<WeatherEvent> Events { get; }
        public bool LowVisibility { get; private set; }
        public double FreezingRisk { get; private set; }

        // Project DataRepository.Schedule into WeatherConditions at this boundary.
        // Wire EnergyAt/RadiationAt into P3/P4 to avoid applying the original schedule twice.
        public WeatherSystem(SimClock clock, Func<int, WeatherConditions> schedule, int solCount,
            SimModifiers modifiers, SimEvents events, int seed = GameConstants.DataSeed, Func<bool> sheltered = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (solCount <= 0 || solCount > GameConstants.ScheduleSols) throw new ArgumentOutOfRangeException(nameof(solCount));
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            bus = events ?? throw new ArgumentNullException(nameof(events));
            this.sheltered = sheltered;
            rows = new WeatherConditions[solCount];
            for (int i = 0; i < rows.Length; i++) rows[i] = schedule(i);
            Events = WeatherSchedule.Map(rows, seed).AsReadOnly();
            Refresh();
        }

        // These queries are independent of system execution order and never consume events.
        public EnergyConditions EnergyAt(int sol)
        {
            var row = Row(sol);
            Refresh();
            double low = row.MinTemperature, high = row.MaxTemperature;
            for (int i = 0; i < Events.Count; i++)
            {
                var item = Events[i];
                if (!Active(item, sol + 0.5) || !(item is TempExtreme extreme)) continue;
                double protection = 1 - (double)item.Severity / item.BaseSeverity;
                if (extreme.IsCold) low += (GameConstants.ColdNightTemperature - low) * protection;
                else high += (GameConstants.HeatStormTemperature - high) * protection;
            }
            return new EnergyConditions(row.Ls, TauAt(sol + 0.5), (low + high) / 2);
        }

        public RadiationConditions RadiationAt(int sol)
        {
            var row = Row(sol);
            // Aurora alone remains an alert: it must not invent a dose absent from the schedule.
            int severity = row.FlareLevel == 0 ? 0 : Severity(row.FlareLevel);
            return new RadiationConditions(row.GcrDoseUGy, severity);
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Battery.ValidateNonnegative(dtSol, nameof(dtSol));
            if (dtSol == 0) return;
            Refresh();
            double time = clock.ElapsedSols, end = time + dtSol;
            Dispatch(time);
            while (time < end)
            {
                double next = end;
                for (int i = 0; i < Events.Count; i++)
                {
                    var item = Events[i];
                    if (!item.Warned) next = Boundary(next, time, item.WarningSol);
                    if (!item.Started) next = Boundary(next, time, item.StartSol);
                    if (!item.Peaked) next = Boundary(next, time, item.PeakSol);
                    if (!item.Ended) next = Boundary(next, time, item.EndSol);
                }
                ApplyExposure(state, time, next);
                time = next;
                Dispatch(time);
            }
            LowVisibility = time < rows.Length && TauAt(time) >= GameConstants.LowVisibilityTau;
            FreezingRisk = 0;
            if (!IsSheltered() && NightDuration(time, time + GameConstants.EnergyTimeTolerance) > 0)
                for (int i = 0; i < Events.Count; i++)
                    if (Events[i] is TempExtreme cold && cold.IsCold && Active(cold, time))
                        FreezingRisk = (double)cold.Severity / GameConstants.MaxFlareLevel;
        }

        private void ApplyExposure(GameState state, double start, double end)
        {
            if (IsSheltered()) return;
            for (int i = 0; i < Events.Count; i++)
            {
                var item = Events[i];
                if (!Active(item, start)) continue;
                double delta = end - start;
                if (item is SolarFlare)
                {
                    // The dose itself belongs to RadiationSystem; only acute exposure HP is applied here.
                    int sol = (int)start;
                    if (sol < rows.Length && rows[sol].FlareLevel > 0)
                        state.Health -= Severity(rows[sol].FlareLevel) * delta * GameConstants.FlareHpPerSeveritySol;
                }
                else if (item is TempExtreme extreme)
                {
                    double night = NightDuration(start, end);
                    double exposed = extreme.IsCold ? night : delta - night;
                    state.Tiredness += item.Severity * exposed * GameConstants.WeatherFatiguePerSeveritySol;
                    if (extreme.IsCold) state.Health -= item.Severity * exposed * GameConstants.FreezingHpPerSeveritySol;
                }
            }
        }

        private void Dispatch(double time)
        {
            for (int i = 0; i < Events.Count; i++)
            {
                var item = Events[i];
                if (!item.Warned && time >= item.WarningSol) { item.Warned = true; bus.ReportWeatherWarning(item); }
                if (!item.Started && time >= item.StartSol) { item.Started = true; bus.ReportWeatherStarted(item); }
                if (!item.Peaked && time >= item.PeakSol) { item.Peaked = true; bus.ReportWeatherPeak(item); }
                if (!item.Ended && time >= item.EndSol) { item.Ended = true; bus.ReportWeatherEnded(item); }
            }
        }

        private void Refresh()
        {
            for (int i = 0; i < Events.Count; i++)
            {
                var item = Events[i];
                item.Severity = Severity(item.BaseSeverity);
                // Once sent, a warning keeps its announced timestamp; severity can still improve.
                if (!item.Warned) item.WarningTimeSols = Math.Max(0, Math.Min(rows.Length,
                    modifiers.Apply(GameConstants.WeatherWarningStat, GameConstants.WeatherWarningSols)));
            }
        }

        private int Severity(int value) => (int)Math.Max(1, Math.Min(GameConstants.MaxFlareLevel,
            Math.Round(modifiers.Apply(GameConstants.WeatherSeverityStat, value), MidpointRounding.AwayFromZero)));

        private double TauAt(double time)
        {
            for (int i = 0; i < Events.Count; i++)
                if (Events[i] is DustStorm dust && Active(dust, time)) return dust.TauAt(time);
            return time >= rows.Length ? GameConstants.DefaultClearTau : rows[(int)time].Tau;
        }

        private WeatherConditions Row(int sol)
        {
            if (sol < 0 || sol >= rows.Length) throw new ArgumentOutOfRangeException(nameof(sol));
            return rows[sol];
        }

        private bool IsSheltered() => sheltered != null && sheltered();
        private static bool Active(WeatherEvent item, double time) => time >= item.StartSol && time < item.EndSol;
        private static double Boundary(double next, double now, double boundary) => boundary > now ? Math.Min(next, boundary) : next;

        private static double NightDuration(double start, double end)
        {
            double total = 0;
            while (start < end)
            {
                double sol = Math.Floor(start), next = Math.Min(end, sol + 1);
                total += Math.Max(0, Math.Min(next, sol + GameConstants.SunriseSol) - start);
                total += Math.Max(0, next - Math.Max(start, sol + GameConstants.SunsetSol));
                start = next;
            }
            return total;
        }
    }
}
