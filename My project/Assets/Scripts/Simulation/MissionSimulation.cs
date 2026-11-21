using System;
using System.Globalization;

namespace MarsSurvival.Simulation
{
    /// <summary>
    /// The whole game rule set: three vitals (oxygen, food, water), a greenhouse of plant spots,
    /// a seed shop and credits. Buy seeds, sow them, water them, pick the crops, then eat or sell them;
    /// spend credits on more plots. The astronaut is lost when any vital reaches zero.
    /// </summary>
    public sealed class MissionSimulation
    {
        public MissionState State { get; private set; }
        public EnvironmentSample Environment;
        public string LastResult { get; private set; } = "";
        public bool IsRunning { get { return State.phase == MissionPhase.Running; } }
        public bool Asleep { get { return State.sleepUntil > State.elapsedSeconds; } }
        public int ExpansionPrice { get { return 25 * State.unlockedPlots; } }
        public bool CanExpand { get { return State.unlockedPlots < MissionSettings.MaxPlots; } }

        public MissionSimulation(MissionState state)
        {
            State = state ?? throw new ArgumentNullException("state");
            while (State.plots.Count < MissionSettings.MaxPlots) State.plots.Add(new PlotState());
            if (State.seeds == null || State.seeds.Length != SeedKind.All.Length) State.seeds = new int[SeedKind.All.Length];
            if (State.basket == null || State.basket.Length != SeedKind.All.Length) State.basket = new int[SeedKind.All.Length];
        }

        public static MissionState NewMission(int seed)
        {
            var state = new MissionState { seed = seed, phase = MissionPhase.Running };
            state.seeds[0] = 3; // a first packet of lettuce, so the loop starts at once
            state.basket[0] = 2; // and two lettuces to eat while it grows
            return state;
        }

        public PlotState Plot(int index) { return index >= 0 && index < State.plots.Count ? State.plots[index] : null; }
        public bool Unlocked(int index) { return index >= 0 && index < State.unlockedPlots; }

        /// <summary>Runs one action; returns false with the reason in <paramref name="reason"/> when it cannot.</summary>
        public bool Command(string action, string target, PlayerContext ctx, out string reason)
        {
            if (!IsRunning) return Fail("The mission is not running.", out reason);
            // Targets: a plot index ("5"), a seed id ("lettuce"), "seed:plot" for sowing, or "plots".
            string[] parts = (target ?? "").Split(':');
            int spot; if (!int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out spot)) spot = -1;
            PlotState plot = Plot(spot);
            int kind = Array.FindIndex(SeedKind.All, s => s.id == parts[0]);
            switch (action)
            {
                case "eat":
                {
                    if (State.astronaut.food > 95) return Fail("You are not hungry.", out reason);
                    int best = -1;
                    for (int i = 0; i < SeedKind.All.Length; i++) if (State.basket[i] > 0 && (best < 0 || SeedKind.All[i].sellPrice < SeedKind.All[best].sellPrice)) best = i;
                    if (best < 0) return Fail("Nothing to eat. Harvest a crop first.", out reason);
                    State.basket[best]--;
                    State.astronaut.food = Math.Min(100, State.astronaut.food + SeedKind.All[best].food);
                    reason = "Ate a " + SeedKind.All[best].name.ToLowerInvariant() + ".";
                    break;
                }
                case "drink":
                    if (State.astronaut.water > 95) return Fail("You are not thirsty.", out reason);
                    State.astronaut.water = 100;
                    reason = "Drank water.";
                    break;
                case "sleep":
                    if (ctx.zone == PlayerZone.Outdoors) return Fail("Sleep inside the habitat.", out reason);
                    State.sleepUntil = State.elapsedSeconds + SecondsUntilMorning();
                    reason = "Sleeping until 07:00.";
                    break;
                case "plant":
                {
                    if (plot == null || !Unlocked(spot)) return Fail("This spot is locked. Buy more plots at the seed shop.", out reason);
                    if (plot.seed != "") return Fail("Something is already growing here.", out reason);
                    int pick = kind >= 0 ? kind : Array.FindIndex(State.seeds, n => n > 0);
                    if (pick < 0 || State.seeds[pick] <= 0) return Fail("No seeds. Buy some at the seed shop.", out reason);
                    State.seeds[pick]--;
                    plot.seed = SeedKind.All[pick].id; plot.growth = 0; plot.wetUntil = 0;
                    reason = "Sowed " + SeedKind.All[pick].name.ToLowerInvariant() + ". Water it to make it grow.";
                    break;
                }
                case "water":
                    if (plot == null || plot.seed == "") return Fail("Nothing is sown here.", out reason);
                    if (plot.Ripe) return Fail("Ripe. Pick it with E.", out reason);
                    // One watering carries the crop to ripe: it stays moist for the rest of its grow time.
                    SeedKind grown = SeedKind.Find(plot.seed);
                    plot.wetUntil = State.elapsedSeconds + grown.GameSeconds * (1 - plot.growth);
                    reason = "Watered. Ripe in " + Math.Ceiling(grown.seconds * (1 - plot.growth)) + " s.";
                    break;
                case "pick":
                {
                    if (plot == null || !plot.Ripe) return Fail("Not ripe yet.", out reason);
                    int index = Array.FindIndex(SeedKind.All, s => s.id == plot.seed);
                    State.basket[index]++;
                    plot.seed = ""; plot.growth = 0; plot.wetUntil = 0;
                    reason = "Picked a " + SeedKind.All[index].name.ToLowerInvariant() + ".";
                    break;
                }
                case "buy":
                    if (target == "plots")
                    {
                        if (!CanExpand) return Fail("The greenhouse is fully planted out.", out reason);
                        if (State.credits < ExpansionPrice) return Fail("Not enough credits.", out reason);
                        State.credits -= ExpansionPrice;
                        State.unlockedPlots = Math.Min(MissionSettings.MaxPlots, State.unlockedPlots + MissionSettings.PlotsPerExpansion);
                        reason = "Greenhouse expanded to " + State.unlockedPlots + " plots.";
                        break;
                    }
                    if (kind < 0) return Fail("Unknown seed.", out reason);
                    if (State.credits < SeedKind.All[kind].price) return Fail("Not enough credits.", out reason);
                    State.credits -= SeedKind.All[kind].price;
                    State.seeds[kind]++;
                    reason = "Bought " + SeedKind.All[kind].name.ToLowerInvariant() + " seeds.";
                    break;
                case "sell":
                    if (kind < 0 || State.basket[kind] <= 0) return Fail("Nothing of that to sell.", out reason);
                    int earned = State.basket[kind] * SeedKind.All[kind].sellPrice;
                    reason = "Sold " + State.basket[kind] + " " + SeedKind.All[kind].name.ToLowerInvariant() + " for " + earned + " credits.";
                    State.credits += earned;
                    State.basket[kind] = 0;
                    break;
                default: return Fail("Unknown action.", out reason);
            }
            LastResult = reason;
            return true;
        }

        public double SecondsUntilMorning()
        {
            double hour = Environment.localHour;
            double wait = MissionSettings.MorningHour - hour;
            if (wait <= 0.25) wait += 24;
            return wait * MissionSettings.HourSeconds;
        }

        public void Step(double seconds, EnvironmentSample environment, PlayerContext ctx)
        {
            if (!IsRunning || double.IsNaN(seconds) || seconds <= 0) return;
            Environment = environment;
            double sols = seconds / MissionSettings.SolSeconds, hours = seconds / MissionSettings.HourSeconds;
            AstronautState a = State.astronaut;
            double rest = Asleep ? 0.5 : 1;
            a.food = Math.Max(0, a.food - MissionSettings.FoodPerSol * sols * rest);
            a.water = Math.Max(0, a.water - MissionSettings.WaterPerSol * sols * rest);
            a.oxygen = ctx.zone == PlayerZone.Outdoors
                ? Math.Max(0, a.oxygen - MissionSettings.OxygenPerHourOutside * hours * (ctx.sprinting ? 1.5 : 1))
                : Math.Min(100, a.oxygen + MissionSettings.OxygenRefillPerHour * hours);
            foreach (PlotState plot in State.plots)
            {
                if (plot.seed == "" || plot.Ripe) continue;
                // Growth only counts while the soil is moist.
                double wet = Math.Max(0, Math.Min(seconds, plot.wetUntil - State.elapsedSeconds));
                plot.growth = Math.Min(1, plot.growth + wet / SeedKind.Find(plot.seed).GameSeconds);
            }
            State.elapsedSeconds += seconds;
            if (!Asleep) State.sleepUntil = 0;
            string cause = a.oxygen <= 0 ? "You ran out of oxygen outside." : a.food <= 0 ? "You starved." : a.water <= 0 ? "You died of thirst." : null;
            if (cause != null) { State.phase = MissionPhase.Lost; State.endingReason = cause; LastResult = cause; }
        }

        private bool Fail(string text, out string reason) { reason = text; LastResult = text; return false; }
    }
}
