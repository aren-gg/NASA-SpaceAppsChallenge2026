using System;
using System.Globalization;

namespace MarsSurvival.Simulation
{
    /// <summary>
    /// The daily loop: sols that start at 07:00 and end in a bunk, hand-sown and hand-picked
    /// greenhouse plants, panel-by-panel dusting and the base fault fixed with the wrench.
    /// </summary>
    public sealed partial class MissionSimulation
    {
        public const int PanelCount = 6;
        /// <summary>Number of places in the outpost where a fault can appear (see MissionWorld.FaultSpots).</summary>
        public const int FaultSpotCount = 7;

        /// <summary>True before 14:00 local time: meals count as breakfast.</summary>
        private bool Morning { get { return Environment.localHour < 14; } }

        /// <summary>Mars-local hour (0-24) from the latest environment sample.</summary>
        public double LocalHour { get { return Environment.localHour; } }

        private static bool AtGalley(PlayerContext ctx) { return ctx.station == "galley"; }

        public double SecondsUntilMorning()
        {
            double hours = MissionSettings.MorningHour - Environment.localHour;
            while (hours < 0.25) hours += 24;
            return hours * MissionSettings.HourSeconds;
        }

        private bool StartSleep(PlayerContext ctx, out string reason)
        {
            if (ctx.station != "quarters") return Fail("Sleep in a bunk in the crew quarters.", out reason);
            if (State.task.kind != TaskKind.None) return Fail("Finish the current task first.", out reason);
            if (Environment.localHour >= MissionSettings.MorningHour && Environment.localHour < 17 && State.astronaut.fatigue < 60)
                return Fail("It is still daytime. Finish today's plan and sleep after 17:00.", out reason);
            if (!StartTask(TaskKind.Rest, "sleep", ctx, out reason)) return false;
            reason = "Good night. Sleeping until 07:00.";
            LastResult = reason;
            return true;
        }

        /// <summary>Starts a new day: resets the plan, records yesterday and maybe breaks something.</summary>
        public void NewDay(bool slept)
        {
            DayState old = State.day ?? new DayState();
            DayState day = new DayState
            {
                number = old.number + 1,
                startSeconds = State.elapsedSeconds,
                summaryPending = slept && old.number > 0,
                foodAtStart = State.resources.food,
                waterAtStart = State.resources.cleanWaterLiters,
                researchAtStart = State.resources.researchPoints,
            };
            if (old.number > 0)
                day.yesterday = string.Format(CultureInfo.InvariantCulture,
                    "Harvested {0:0} food, picked {1} plants, sowed {2}, {3} repairs, {4} panels swept, +{5} research{6}{7}",
                    old.foodHarvested, old.plantsPicked, old.plantsSown, old.repairs, old.panelsSwept,
                    Math.Max(0, State.resources.researchPoints - old.researchAtStart),
                    old.fieldDone ? ", field trip done" : "", old.reportDone ? ", Houston report sent" : "");
            State.day = day;
            // Something in the base breaks on most days, and always when it is worn.
            Random random = new Random(State.seed * 31 + day.number * 7919);
            if (!State.faultActive && day.number > 1 && (random.NextDouble() < 0.6 || State.equipment.baseCondition < 0.8))
            {
                State.faultActive = true;
                State.faultSpot = random.Next(FaultSpotCount);
                Log("A fault is sparking somewhere in the base. Find it and fix it with the Field Wrench.");
            }
        }

        private void TickDay(PlayerContext ctx)
        {
            if (State.day == null) State.day = new DayState();
            if (State.day.number == 0 || State.elapsedSeconds - State.day.startSeconds > 1.25 * MissionSettings.SolSeconds) NewDay(false);
            bool ok = true;
            for (int i = 0; i < 3; i++)
                if (Power.neededKw[i] > 0.001 && Power.deliveredKw[i] < Power.neededKw[i] * 0.95) ok = false;
            if (ok && Power.neededKw[0] > 0.001) State.day.powerChecked = true;
            if (!State.faultActive && State.equipment.baseCondition < 0.7)
            {
                State.faultActive = true;
                State.faultSpot = (State.day.number * 3 + 1) % FaultSpotCount;
                Log("The base is worn and a fault is sparking. Fix it with the Field Wrench.");
            }
        }

        // ------------------------------------------------------------------ greenhouse by hand

        private static int Slot(string[] parts, int index)
        {
            int value;
            return parts.Length > index && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
        }

        /// <summary>"bed:slot[:crop]": sow one plant spot. The bed starts growing when all 12 are sown.</summary>
        private bool PlantSlot(string target, PlayerContext ctx, out string reason)
        {
            string[] parts = (target ?? "").Split(':');
            CropState crop = FindCrop(target);
            int slot = Slot(parts, 1);
            if (ctx.station != "greenhouse" || crop == null || slot < 0 || slot >= CropState.Slots) return Fail("Sow the greenhouse beds.", out reason);
            if (crop.planted) return Fail("This bed is already growing.", out reason);
            if ((crop.sownMask & (1 << slot)) != 0) return Fail("This spot is already sown.", out reason);
            if (crop.sownMask == 0)
            {
                string kind = parts.Length > 2 ? parts[2].ToLowerInvariant() : crop.crop;
                if (kind != "leafy" && kind != "staple") kind = "leafy";
                if (State.resources.seeds < 1) return Fail("No seeds left. Harvesting a bed saves one.", out reason);
                State.resources.seeds -= 1;
                crop.crop = kind;
            }
            crop.sownMask |= 1 << slot;
            State.day.plantsSown++;
            int sown = CropState.Count(crop.sownMask);
            if (crop.sownMask == CropState.AllSlots)
            {
                crop.planted = true; crop.ready = false; crop.growth = 0; crop.stress = 0; crop.pickedMask = 0;
                Log("Bed " + (crop.bed + 1) + " is sown with " + crop.crop + " crops. Water it every day with the Sprayer.");
                reason = LastResult;
            }
            else reason = "Sowed " + sown + " of " + CropState.Slots + " spots in bed " + (crop.bed + 1) + ".";
            LastResult = reason;
            return true;
        }

        /// <summary>"bed:slot": pick one ripe plant into the food store. The bed empties when every plant is picked.</summary>
        private bool PickSlot(string target, PlayerContext ctx, out string reason)
        {
            string[] parts = (target ?? "").Split(':');
            CropState crop = FindCrop(target);
            int slot = Slot(parts, 1);
            if (ctx.station != "greenhouse" || crop == null || slot < 0 || slot >= CropState.Slots) return Fail("Pick ripe plants in the greenhouse.", out reason);
            if (!crop.ready) return Fail("Not ripe yet.", out reason);
            if ((crop.pickedMask & (1 << slot)) != 0) return Fail("Already picked.", out reason);
            if (State.resources.food >= Settings.foodCapacity) return Fail("Food storage is full.", out reason);
            double yield = (crop.crop == "staple" ? 50 : 24) * (Installed("H1") ? 1.25 : 1) * (1 - crop.stress * 0.4) / CropState.Slots;
            double stored = Math.Min(yield, Settings.foodCapacity - State.resources.food);
            State.resources.food += stored;
            State.totalFoodHarvested += stored;
            State.day.foodHarvested += stored;
            State.day.plantsPicked++;
            crop.pickedMask |= 1 << slot;
            if (crop.pickedMask == CropState.AllSlots)
            {
                crop.planted = false; crop.ready = false; crop.growth = 0; crop.harvests++;
                crop.sownMask = 0; crop.pickedMask = 0; crop.moisture = 0;
                State.resources.seeds += 1;
                Log("Bed " + (crop.bed + 1) + " fully harvested and one seed saved. Sow it again with the Shovel.");
                reason = LastResult;
            }
            else reason = string.Format(CultureInfo.InvariantCulture, "+{0:0.0} food. {1} plants left in bed {2}.",
                stored, CropState.Slots - CropState.Count(crop.pickedMask), crop.bed + 1);
            LastResult = reason;
            return true;
        }

        /// <summary>"bed:level": the sprayer soaked the bed to this moisture (0-1.4). Above 1.15 the roots drown.</summary>
        private bool WaterCrop(string target, PlayerContext ctx, out string reason)
        {
            string[] parts = (target ?? "").Split(':');
            CropState crop = FindCrop(target);
            if (!IsIndoors(ctx) || crop == null) return Fail("Water the greenhouse beds.", out reason);
            if (!crop.planted || crop.ready) return Fail("Only a growing bed needs water.", out reason);
            double level = 1;
            if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out level);
            level = Clamp(level, 0, 1.4);
            double added = Math.Max(0, level - crop.moisture);
            double litres = added * Settings.leafyWaterLitersPerSol * (crop.crop == "staple" ? 1.5 : 1) * (Installed("H0") ? 0.8 : 1);
            if (State.resources.cleanWaterLiters < litres) return Fail("Not enough clean water.", out reason);
            State.resources.cleanWaterLiters -= litres;
            State.resources.wasteWaterLiters += litres * 0.8;
            State.totalWaterLostLiters += litres * 0.2;
            crop.moisture = Math.Max(crop.moisture, level);
            if (level > 1.15)
            {
                crop.stress = Math.Min(1, crop.stress + 0.1);
                reason = "Too much water: bed " + (crop.bed + 1) + " is waterlogged and the roots are stressed.";
            }
            else if (level < 0.7) reason = "Bed " + (crop.bed + 1) + " is only partly watered; it will dry out early.";
            else
            {
                crop.stress = Math.Max(0, crop.stress - 0.15);
                reason = string.Format(CultureInfo.InvariantCulture, "Bed {0} watered ({1:0.0} L). It will stay moist for about a sol.", crop.bed + 1, litres);
            }
            LastResult = reason;
            return true;
        }

        // ------------------------------------------------------------------ solar panels, one at a time

        private void AddPanelDust(double amount)
        {
            if (State.panelDustEach == null || State.panelDustEach.Length != PanelCount) EnsureCollections();
            for (int i = 0; i < PanelCount; i++) State.panelDustEach[i] = Clamp(State.panelDustEach[i] + amount * (0.8 + 0.08 * i), 0, 0.85);
            UpdatePanelDust();
        }

        private void UpdatePanelDust()
        {
            double sum = 0;
            for (int i = 0; i < State.panelDustEach.Length; i++) sum += State.panelDustEach[i];
            State.equipment.panelDust = State.panelDustEach.Length == 0 ? 0 : sum / State.panelDustEach.Length;
        }

        /// <summary>"index": the duster swept one array panel clean.</summary>
        private bool DustPanel(string target, PlayerContext ctx, out string reason)
        {
            int index;
            if (!int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0 || index >= PanelCount)
                return Fail("Sweep one of the array's panels.", out reason);
            if (ctx.station != "array") return Fail("Go out to the solar array beside the energy room.", out reason);
            if (State.panelDustEach[index] < 0.01) return Fail("This panel is already clean.", out reason);
            State.panelDustEach[index] = 0;
            UpdatePanelDust();
            State.day.panelsSwept++;
            int dirty = 0;
            foreach (double d in State.panelDustEach) if (d >= 0.01) dirty++;
            if (dirty == 0) { State.panelsCleaned++; Log("All six panels are clean. Solar output is back up."); reason = LastResult; }
            else reason = "Panel swept. " + dirty + " still dusty.";
            LastResult = reason;
            return true;
        }
    }
}
