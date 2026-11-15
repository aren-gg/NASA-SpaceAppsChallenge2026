using System;

namespace MarsSurvival.Simulation
{
    public sealed partial class MissionSimulation
    {
        private void TickLifeSupport(double seconds, PlayerContext context)
        {
            double sols = seconds / MissionSettings.SolSeconds, hours = seconds / 3600;
            ResourceState r = State.resources;
            EquipmentState e = State.equipment;
            double support = Clamp(Power.deliveredKw[0] / Math.Max(0.01, Power.neededKw[0]), 0, 1);
            double wastewater = Math.Min(r.wasteWaterLiters, hours * 1.2 * support);
            double recovery = Installed("E2") ? 0.95 : 0.9;
            wastewater = Math.Min(wastewater, Math.Max(0, Settings.waterCapacityLiters - r.cleanWaterLiters) / recovery);
            r.wasteWaterLiters -= wastewater;
            r.cleanWaterLiters += wastewater * recovery;
            State.totalWaterLostLiters += wastewater * (1 - recovery);

            double oxygen = Math.Min(Settings.oxygenCapacityGrams - r.oxygenGrams, 1600 * sols * support);
            // Electrolysis mass balance: oxygen mass is approximately 8/9 of input water mass.
            oxygen = Math.Min(oxygen, r.cleanWaterLiters * 1000 * (8.0 / 9));
            double water = Math.Max(0, oxygen) / (1000 * (8.0 / 9));
            r.cleanWaterLiters -= water;
            r.oxygenGrams += Math.Max(0, oxygen);
            State.totalOxygenProducedGrams += Math.Max(0, oxygen);
            State.totalWaterLostLiters += water;
            e.airQuality = Clamp(e.airQuality + hours * (support >= 0.9 ? 12 : -(1 - support) * 18), 0, 100);

            double insulation = Settings.thermalLossKwPerDegree * (Installed("P2") ? 0.7 : 1);
            double thermalOutput = Power.deliveredKw[1] * Math.Max(0.1, e.baseCondition);
            double achievable = Environment.temperatureC < 21
                ? Math.Min(21, Environment.temperatureC + thermalOutput / Math.Max(0.001, insulation))
                : Math.Max(21, Environment.temperatureC - thermalOutput / Math.Max(0.001, insulation));
            e.habitatTemperatureC += (achievable - e.habitatTemperatureC) * (1 - Math.Exp(-hours / 3));
        }

        private void TickCrops(double seconds)
        {
            double sols = seconds / MissionSettings.SolSeconds;
            ResourceState r = State.resources;
            double temperature = State.equipment.habitatTemperatureC;
            double temperatureFactor = temperature >= 12 && temperature <= 30 ? 1 : temperature >= 3 && temperature <= 38 ? 0.4 : 0;
            foreach (CropState crop in State.crops)
            {
                if (!crop.planted || crop.ready) continue;
                double conservation = Installed("H0") ? 0.8 : 1;
                double waterNeed = sols * (crop.crop == "staple" ? 1.5 : 1) * conservation;
                double nutrientNeed = sols * 0.35 * conservation;
                double resourceFraction = Math.Min(1, Math.Min(waterNeed > 0 ? r.cleanWaterLiters / waterNeed : 1,
                    nutrientNeed > 0 ? r.nutrients / nutrientNeed : 1));
                double supported = Math.Min(greenhousePowerFraction, resourceFraction) * temperatureFactor;
                double usedWater = waterNeed * supported;
                r.cleanWaterLiters -= usedWater;
                r.wasteWaterLiters += usedWater * 0.8;
                State.totalWaterLostLiters += usedWater * 0.2;
                r.nutrients -= nutrientNeed * supported;
                crop.stress = Clamp(crop.stress + sols * (supported < 0.5 ? (Installed("H0") ? 0.08 : 0.15) : -0.1), 0, 1);
                double cycle = crop.crop == "staple" ? Settings.cropStapleSols : Settings.cropLeafySols;
                crop.growth = Math.Min(1, crop.growth + sols / Math.Max(0.1, cycle) * supported * (1 - crop.stress * 0.75));
                if (crop.growth >= 1 - 0.0000001)
                {
                    crop.growth = 1; crop.ready = true;
                    Log("Greenhouse bed " + (crop.bed + 1) + " is ready to harvest.");
                }
            }
        }

        private void TickAstronaut(double seconds, PlayerContext ctx)
        {
            AstronautState a = State.astronaut;
            ResourceState r = State.resources;
            double sols = seconds / MissionSettings.SolSeconds, hours = seconds / 3600;
            bool sleeping = State.task.kind == TaskKind.Rest && TaskLocationValid(ctx);
            bool exercise = State.task.kind == TaskKind.Exercise && taskPowerFraction > 0;
            double activity = sleeping ? 0.7 : ctx.sprinting ? 1.8 : ctx.moving || exercise ? 1.3 : 1;
            double oxygenNeed = Settings.oxygenGramsPerSol * sols * activity;
            bool indoor = IsIndoors(ctx);
            double oxygenAvailable = indoor ? r.oxygenGrams : r.suitOxygenGrams;
            double usedOxygen = Math.Min(oxygenNeed, oxygenAvailable);
            if (indoor) r.oxygenGrams -= usedOxygen; else r.suitOxygenGrams -= usedOxygen;
            // Fraction of breathing demand actually met; unsafe cabin air counts as none.
            double oxygenSupply = oxygenNeed > 0 ? usedOxygen / oxygenNeed : 1;
            if (indoor && State.equipment.airQuality < 15) oxygenSupply = 0;
            bool hypoxia = oxygenSupply < 0.999;

            double temperatureTarget = State.equipment.habitatTemperatureC;
            if (!indoor)
            {
                double request = 0.06 + Math.Abs(21 - Environment.temperatureC) * 0.001;
                double delivered = DrawField(request, hours, ctx.driving);
                temperatureTarget = delivered >= request * 0.99 ? 21 : Environment.temperatureC;
            }
            a.temperatureC += (temperatureTarget - a.temperatureC) * (1 - Math.Exp(-hours / (indoor ? 0.5 : 1.5)));
            double passive = indoor ? ctx.zone == PlayerZone.Shelter ? 0.18 * (Installed("P1") ? 0.5 : 1) : 0.65 : 1;
            if (Installed("P3") && indoor) passive *= 0.8;
            double shield = indoor ? 1 - 0.65 * Clamp(Power.deliveredKw[2] / Math.Max(0.01, Power.neededKw[2]), 0, 1) : 1;
            double doseRate = Environment.radiationMicrograyPerHour * passive * shield;
            a.doseMicrogray += doseRate * hours;

            // Hunger, water, tiredness, oxygen, fitness and their health damage (starvation, dehydration,
            // hypoxia, cumulative dose against the NASA-STD-3001 budget) come from the MarsTrainer model.
            string cause = Needs.Step(a, seconds, oxygenSupply, sleeping, exercise);
            if (cause != null) NoteDamage(cause);
            if (sleeping && Installed("H3")) a.fatigue = Math.Max(0, a.fatigue - hours * 3);

            if (a.temperatureC < 5) Damage(Math.Min(18, (5 - a.temperatureC) * 0.4) * hours, "Unsafe suit or habitat temperature");
            if (a.temperatureC > 35) Damage(Math.Min(18, (a.temperatureC - 35) * 0.5) * hours, "Unsafe suit or habitat temperature");
            // Both threshold and acute-rate condition are teaching rules, not medical limits.
            if (a.doseMicrogray > Settings.backgroundDamageThresholdMicrogray && doseRate > Settings.harmfulDoseRateMicrograyPerHour)
                Damage(Math.Min(20, (doseRate - Settings.harmfulDoseRateMicrograyPerHour) * 0.003) * hours, "Severe radiation scenario");
            bool stable = !hypoxia && a.oxygen > 0 && a.hydration > 35 && a.satiety > 35 && a.temperatureC >= 12 && a.temperatureC <= 30;
            if (sleeping && stable) a.health = Math.Min(Needs.Body.MaxHealth, a.health + hours * (Installed("H3") ? 0.8 : 0.4));
        }

        private void Damage(double amount, string cause)
        {
            if (amount <= 0) return;
            State.astronaut.health = Math.Max(0, State.astronaut.health - amount);
            NoteDamage(cause);
        }

        private void NoteDamage(string cause)
        {
            if (State.astronaut.lastDamageCause != cause) Log("Health falling: " + cause + ".");
            State.astronaut.lastDamageCause = cause;
        }

        private void ClampState()
        {
            AstronautState a = State.astronaut;
            a.health = Clamp(a.health, 0, 100); a.satiety = Clamp(a.satiety, 0, 100);
            a.hydration = Clamp(a.hydration, 0, 100); a.fatigue = Clamp(a.fatigue, 0, 100); a.oxygen = Clamp(a.oxygen, 0, 100);
            ResourceState r = State.resources;
            r.batteryKwh = Clamp(r.batteryKwh, 0, BatteryCapacity);
            r.roverKwh = Clamp(r.roverKwh, 0, Settings.roverCapacityKwh);
            r.fieldKwh = Clamp(r.fieldKwh, 0, Settings.fieldCapacityKwh);
            r.cleanWaterLiters = Clamp(r.cleanWaterLiters, 0, Settings.waterCapacityLiters);
            r.wasteWaterLiters = Clamp(r.wasteWaterLiters, 0, Settings.waterCapacityLiters);
            r.oxygenGrams = Clamp(r.oxygenGrams, 0, Settings.oxygenCapacityGrams);
            r.suitOxygenGrams = Clamp(r.suitOxygenGrams, 0, Settings.suitCapacityGrams);
            r.food = Clamp(r.food, 0, Settings.foodCapacity);
            r.seeds = Math.Max(0, r.seeds); r.nutrients = Math.Max(0, r.nutrients);
        }

        private void RefreshWarnings(PlayerContext ctx)
        {
            Warnings.Clear();
            if (!Active) return;
            ResourceState r = State.resources;
            AstronautState a = State.astronaut;
            if (a.health < 50) Warnings.Add("Health is low. Stabilize supplies and use medical care.");
            if (Environment.warning) Warnings.Add("Weather warning: check protection and return supplies before advancing.");
            if (Environment.solarEvent > 0.1 && ctx.zone != PlayerZone.Shelter) Warnings.Add("Radiation event: return to the storm shelter and check shielding power.");
            if (r.batteryKwh < BatteryCapacity * 0.15) Warnings.Add("Battery reserve is low. Reduce avoidable loads or improve solar collection.");
            if (Power.deliveredKw[0] < Power.neededKw[0] * 0.9) Warnings.Add("Life control is underpowered. Oxygen, air processing and recycling are reduced.");
            if (State.equipment.habitatTemperatureC < 12 || State.equipment.habitatTemperatureC > 30) Warnings.Add("Habitat temperature is unsafe. Adjust Temperature power or repair the base.");
            if (r.food < 6) Warnings.Add("Food reserve is low. Harvest a ready crop and keep greenhouse power available.");
            if (r.cleanWaterLiters < 10) Warnings.Add("Clean water is low. Keep recycling powered and check water use.");
            if (a.satiety < 25) Warnings.Add("Eat a food portion before hunger affects work.");
            if (a.hydration < 25) Warnings.Add("Drink clean water before dehydration causes harm.");
            if (a.fatigue > 75) Warnings.Add("The astronaut is tired. Rest improves task efficiency.");
            if (a.oxygen < 40) Warnings.Add("Breathing oxygen is low. Restore suit or habitat oxygen supply now.");
            if (SurvivalNeeds.DoseBudgetFraction(a) >= MarsTrainer.Sim.GameConstants.ShieldWarnFraction)
                Warnings.Add("Radiation dose has used over half of the NASA-STD-3001 budget. Raise shielding and limit EVA.");
            if (!IsIndoors(ctx) && r.suitOxygenGrams < 150) Warnings.Add("Suit oxygen is low. Return to the habitat refill station.");
            if (!IsIndoors(ctx) && r.fieldKwh < 0.25) Warnings.Add("Field battery is low. Return before suit heating and tools lose power.");
            if (IsIndoors(ctx) && r.oxygenGrams < 1000) Warnings.Add("Habitat oxygen is low. Increase Life control and check water input.");
            if (State.equipment.baseCondition < 0.6 || State.equipment.roverCondition < 0.6) Warnings.Add("Equipment condition is poor. Plan a repair with parts and power.");
            if (State.equipment.panelDust > 0.25) Warnings.Add("Panel deposits are reducing generation. Dust the solar array.");
            if (State.task.kind != TaskKind.None && taskPowerFraction < 0.05 && State.task.powerKw > 0) Warnings.Add("The current task has no power. Free capacity or recharge its battery.");
            if (State.radioMode == RadioMode.Transmit && Environment.signal <= 0.02 && State.reports.Exists(rp => !rp.acknowledged)) Warnings.Add("No radio link. Reports remain queued; move or wait for a relay window.");
        }
    }
}
