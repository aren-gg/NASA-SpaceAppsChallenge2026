using System;

namespace MarsTrainer.Sim.Systems
{
    public sealed class EnergySystem : ISimSystem
    {
        private readonly SimClock clock;
        private readonly Func<int, EnergyConditions> schedule;
        private readonly SolarPanel panels;
        private readonly Battery battery;
        private readonly EnergyLoads loads;
        private readonly SimModifiers modifiers;
        private readonly SimEvents events;
        private double generation, demand, served;
        private EnergyLoad shed;
        private bool empty;
        public int Order => GameConstants.EnergySystemOrder;
        public double LifeSupportPowerFraction { get; private set; } = 1;
        public double ShieldPowerFraction { get; private set; } = 1;
        public EnergyReport LastReport { get; private set; }

        public EnergySystem(SimClock clock, Func<int, EnergyConditions> schedule, SolarPanel panels,
            Battery battery, EnergyLoads loads, SimModifiers modifiers, SimEvents events)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            this.panels = panels ?? throw new ArgumentNullException(nameof(panels));
            this.battery = battery ?? throw new ArgumentNullException(nameof(battery));
            this.loads = loads ?? throw new ArgumentNullException(nameof(loads));
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Battery.ValidateNonnegative(dtSol, nameof(dtSol));
            double poweredShieldSols = 0, poweredLifeSupportSols = 0;
            double time = SnapToBin(clock.ElapsedSols);
            double end = SnapToBin(clock.ElapsedSols + dtSol);
            while (time < end)
            {
                int sol = (int)Math.Floor(time);
                var conditions = schedule(sol);
                if (double.IsNaN(conditions.OutdoorTemperature) || double.IsInfinity(conditions.OutdoorTemperature))
                    throw new ArgumentOutOfRangeException(nameof(schedule));
                // Fixed solar-time bins give consistent generation across arbitrary render ticks.
                int bin = (int)Math.Floor((time - sol) * GameConstants.EnergySamplesPerSol
                    + GameConstants.EnergyTimeTolerance);
                double next = Math.Min(end, sol + (bin + 1.0) / GameConstants.EnergySamplesPerSol);
                double delta = next - time;
                double midpoint = (bin + 0.5) / GameConstants.EnergySamplesPerSol;
                double hours = delta * GameConstants.MarsSolHours;
                double generated = panels.PowerKW(midpoint, conditions.Tau, conditions.Ls, modifiers) * hours;
                double heating = loads.HeatingKWhPerDegreeSol * Math.Max(0, loads.SetTemperature - conditions.OutdoorTemperature);
                double requested = (loads.LifeSupport + loads.Shield + loads.Medbay + loads.Lights + heating) * delta;
                double deficit = requested - generated;
                double missing = 0;
                if (deficit > 0) missing = Math.Max(0, deficit - battery.Discharge(deficit, hours));
                else battery.Charge(-deficit, hours);
                generation += generated;
                demand += requested;
                served += requested - missing;
                Shed(ref missing, loads.Lights * delta, EnergyLoad.Lights);
                Shed(ref missing, heating * delta, EnergyLoad.Heating);
                Shed(ref missing, loads.Medbay * delta, EnergyLoad.Medbay);
                poweredShieldSols += loads.Shield > 0
                    ? delta * (1 - Math.Min(1, missing / (loads.Shield * delta))) : 0;
                Shed(ref missing, loads.Shield * delta, EnergyLoad.Shield);
                poweredLifeSupportSols += loads.LifeSupport > 0
                    ? delta * (1 - Math.Min(1, missing / (loads.LifeSupport * delta))) : 0;
                Shed(ref missing, loads.LifeSupport * delta, EnergyLoad.LifeSupport);
                state.EnergyKWh = battery.StoredKWh;
                state.BatteryCapKWh = battery.CapacityKWh;
                bool nowEmpty = battery.StoredKWh <= GameConstants.EnergyTimeTolerance;
                if (nowEmpty && !empty) events.ReportBrownout();
                empty = nowEmpty;
                time = next;
                if (time == sol + 1)
                {
                    LastReport = new EnergyReport(sol, generation, demand, served, battery.Percent, shed);
                    generation = demand = served = 0;
                    shed = EnergyLoad.None;
                    events.ReportEnergy(LastReport);
                }
            }
            if (dtSol > 0)
            {
                ShieldPowerFraction = poweredShieldSols / dtSol;
                LifeSupportPowerFraction = poweredLifeSupportSols / dtSol;
            }
            state.EnergyKWh = battery.StoredKWh;
            state.BatteryCapKWh = battery.CapacityKWh;
        }

        public bool TrySpend(GameState state, double costKWh)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            bool paid = battery.TrySpend(costKWh);
            state.EnergyKWh = battery.StoredKWh;
            return paid;
        }

        private static double SnapToBin(double time)
        {
            double boundary = Math.Round(time * GameConstants.EnergySamplesPerSol) / GameConstants.EnergySamplesPerSol;
            return Math.Abs(time - boundary) <= GameConstants.EnergyTimeTolerance ? boundary : time;
        }

        private void Shed(ref double missing, double load, EnergyLoad kind)
        {
            if (missing <= GameConstants.EnergyTimeTolerance || load <= 0) return;
            shed |= kind;
            missing = Math.Max(0, missing - load);
        }
    }
}
