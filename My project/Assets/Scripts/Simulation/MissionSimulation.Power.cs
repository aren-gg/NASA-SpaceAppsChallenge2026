using System;

namespace MarsSurvival.Simulation
{
    public sealed partial class MissionSimulation
    {
        private double taskPowerFraction = 1, radioPowerFraction, greenhousePowerFraction;
        private double thermalNeed;

        private void ApplyPower(double seconds, PlayerContext ctx)
        {
            double hours = seconds / 3600;
            ResourceState resource = State.resources;
            EquipmentState equipment = State.equipment;
            double condition = Math.Max(0.1, equipment.baseCondition);
            double efficiency = Installed("E2") ? 0.75 : 1;
            double lossCoefficient = Settings.thermalLossKwPerDegree * (Installed("P2") ? 0.7 : 1);
            thermalNeed = Math.Abs(21 - Environment.temperatureC) * lossCoefficient / condition;
            Power.neededKw[0] = 1.05 * efficiency / condition;
            Power.neededKw[1] = thermalNeed;
            Power.neededKw[2] = 0.2 + Environment.solarEvent * 2.6;
            Power.batteryCapacityKwh = BatteryCapacity;
            double panelRating = Settings.solarRatedKw + equipment.extraPanels * 7;
            Power.generatedKw = panelRating * Environment.solarFraction * equipment.panelCondition * (1 - equipment.panelDust)
                * (Installed("S0") ? 1.2 : 1) * (Installed("S3") ? 1.2 : 1);
            double dischargeLimit = Settings.maximumDischargeKw * (Installed("E1") ? 1.25 : 1);
            double available = Power.generatedKw + Math.Min(dischargeLimit, resource.batteryKwh / hours);
            Power.requestedKw[0] = State.knobs[0] * Settings.lifeRatedKw;
            Power.requestedKw[1] = State.knobs[1] * Settings.thermalRatedKw;
            Power.requestedKw[2] = State.knobs[2] * Settings.shieldingRatedKw;
            double requested = Power.requestedKw[0] + Power.requestedKw[1] + Power.requestedKw[2];
            double factor = requested > 0 ? Math.Min(1, available / requested) : 0;
            double channelLoad = 0;
            for (int i = 0; i < 3; i++)
            {
                Power.deliveredKw[i] = Power.requestedKw[i] * factor;
                channelLoad += Power.deliveredKw[i];
            }
            available = Math.Max(0, available - channelLoad);
            double endUse = channelLoad;

            // Overdraw penalty: power beyond each system's useful need is mostly wasted and
            // costs extra, so the battery drains faster the further the knobs are over.
            double excess = 0;
            for (int i = 0; i < 3; i++) excess += Math.Max(0, Power.deliveredKw[i] - Power.neededKw[i]);
            Power.overdrawLossKw = Take(ref available, excess * Math.Max(0, Settings.overdrawCostMultiplier - 1));
            endUse += Power.overdrawLossKw;   // counted once, as consumed energy

            // Remaining grid power: greenhouse, staffed task, radio, maintenance, charging.
            int growingBeds = State.crops.FindAll(c => c.planted && !c.ready).Count;
            double greenhouseRequest = State.greenhouseEnabled ? growingBeds * Settings.greenhouseKwPerBed : 0;
            Power.greenhouseKw = Take(ref available, greenhouseRequest);
            greenhousePowerFraction = greenhouseRequest > 0 ? Power.greenhouseKw / greenhouseRequest : 0;
            endUse += Power.greenhouseKw;

            TaskState task = State.task;
            bool validTaskLocation = TaskLocationValid(ctx);
            double taskRequest = validTaskLocation && task.kind != TaskKind.None ? task.powerKw : 0;
            Power.taskKw = task.fieldPower ? DrawField(taskRequest, hours, task.roverPower) : Take(ref available, taskRequest);
            taskPowerFraction = !validTaskLocation ? 0 : task.powerKw > 0 ? Power.taskKw / task.powerKw : 1;
            if (!task.fieldPower) endUse += Power.taskKw;

            double radioRequest = State.radioMode == RadioMode.Off ? 0 : State.radioMode == RadioMode.Listen ? 0.03 : Environment.signal > 0.02 && State.reports.Exists(r => !r.acknowledged) ? 0.18 : 0.03;
            bool radioField = !IsIndoors(ctx);
            Power.radioKw = radioField ? DrawField(radioRequest, hours, ctx.driving) : Take(ref available, radioRequest);
            radioPowerFraction = radioRequest > 0 ? Power.radioKw / radioRequest : 0;
            if (!radioField) endUse += Power.radioKw;

            double maintenance = 0;
            if (Installed("S2") && equipment.panelDust > 0)
            {
                double cleaningPower = Take(ref available, 0.1);
                equipment.panelDust = Math.Max(0, equipment.panelDust - hours * cleaningPower * 0.08);
                maintenance += cleaningPower;
            }
            if (Installed("S3") && Power.generatedKw > 0) maintenance += Take(ref available, 0.12);
            endUse += maintenance;

            double chargerEfficiency = Installed("E3") ? 0.96 : Settings.portableChargeEfficiency;
            Power.chargingKw = 0;
            if (State.chargeRover && ctx.roverAtBase && !ctx.driving)
            {
                double request = Math.Min(6, Math.Max(0, Settings.roverCapacityKwh - resource.roverKwh) / (hours * chargerEfficiency));
                double power = Take(ref available, request);
                resource.roverKwh += power * hours * chargerEfficiency;
                State.totalLossKwh += power * hours * (1 - chargerEfficiency);
                Power.chargingKw += power;
            }
            if (State.chargeField && IsIndoors(ctx))
            {
                double request = Math.Min(0.75, Math.Max(0, Settings.fieldCapacityKwh - resource.fieldKwh) / (hours * chargerEfficiency));
                double power = Take(ref available, request);
                resource.fieldKwh += power * hours * chargerEfficiency;
                State.totalLossKwh += power * hours * (1 - chargerEfficiency);
                Power.chargingKw += power;
            }
            Power.loadKw = endUse + Power.chargingKw;
            Power.netKw = Power.generatedKw - Power.loadKw;
            State.totalConsumedKwh += endUse * hours;
            State.totalGeneratedKwh += Power.generatedKw * hours;
            if (Power.netKw < 0) resource.batteryKwh = Math.Max(0, resource.batteryKwh + Power.netKw * hours);
            else
            {
                double chargeEfficiency = Installed("E3") ? 0.98 : Settings.chargeEfficiency;
                double accepted = Math.Min(Power.netKw, Math.Min(Settings.maximumChargeKw,
                    Math.Max(0, BatteryCapacity - resource.batteryKwh) / (hours * chargeEfficiency)));
                resource.batteryKwh += accepted * hours * chargeEfficiency;
                State.totalLossKwh += accepted * hours * (1 - chargeEfficiency);
                State.totalCurtailedKwh += (Power.netKw - accepted) * hours;
            }
            task.spentKwh += Power.taskKw * hours;
        }

        private double DrawField(double requestedKw, double hours, bool rover)
        {
            if (requestedKw <= 0 || hours <= 0) return 0;
            double reserve = rover ? State.resources.roverKwh : State.resources.fieldKwh;
            double power = Math.Min(requestedKw, Math.Max(0, reserve) / hours);
            double energy = power * hours;
            if (rover) State.resources.roverKwh -= energy; else State.resources.fieldKwh -= energy;
            State.totalConsumedKwh += energy;
            return power;
        }

        private bool SpendFieldEnergy(double amount, bool rover)
        {
            if (amount < 0 || (rover ? State.resources.roverKwh : State.resources.fieldKwh) < amount) return false;
            if (rover) State.resources.roverKwh -= amount; else State.resources.fieldKwh -= amount;
            State.totalConsumedKwh += amount;
            return true;
        }

        private static double Take(ref double available, double requested)
        {
            double delivered = Math.Min(Math.Max(0, available), Math.Max(0, requested));
            available -= delivered;
            return delivered;
        }
    }
}
