using System;
using System.Globalization;

namespace MarsSurvival.Simulation
{
    public sealed partial class MissionSimulation
    {
        private double TaskEfficiency()
        {
            return State.task.kind == TaskKind.Rest || State.task.kind == TaskKind.Transmit ? 1 : WorkEfficiency;
        }

        private bool TaskLocationValid(PlayerContext ctx)
        {
            TaskState task = State.task;
            if (task.kind == TaskKind.None) return true;
            if (ctx.moving || ctx.driving) return false;
            if (!task.fieldPower && !IsIndoors(ctx)) return false;
            if (task.kind == TaskKind.SampleSoil || task.kind == TaskKind.PlaceMarker || task.kind == TaskKind.Photograph)
                return ctx.nearbySiteId == task.targetId;
            if (task.kind == TaskKind.DustPanels) return ctx.distanceFromBase <= 100;
            return true;
        }

        /// <summary>
        /// True when the astronaut has left the place a task needs (gone outside during an inside task, boarded
        /// the rover, walked away from the site or the solar array). Moving about in place only pauses a task.
        /// </summary>
        private bool LeftTaskPlace(PlayerContext ctx)
        {
            TaskState task = State.task;
            if (task.kind == TaskKind.None || task.kind == TaskKind.Transmit) return false;
            if (ctx.driving) return true;
            if (!task.fieldPower && !IsIndoors(ctx)) return true;
            if (task.kind == TaskKind.SampleSoil || task.kind == TaskKind.PlaceMarker || task.kind == TaskKind.Photograph)
                return ctx.nearbySiteId != task.targetId;
            if (task.kind == TaskKind.DustPanels) return ctx.distanceFromBase > 100;
            return false;
        }

        private void TickTask(double seconds, PlayerContext ctx)
        {
            TaskState task = State.task;
            if (LeftTaskPlace(ctx))
            {
                // A task left behind would otherwise wait forever and keep the tools busy.
                CancelTask();
                Log("Stopped " + task.kind + " because you left its place. Unused supplies were returned.");
                return;
            }
            if (task.kind == TaskKind.None || !TaskLocationValid(ctx)) return;
            if (task.kind == TaskKind.Transmit)
            {
                ReportState report = State.reports.Find(r => r.id == task.targetId);
                if (report == null) { CancelTask(); return; }
                task.progressSeconds = report.progressSeconds;
                if (report.acknowledged) CompleteTask();
                return;
            }
            task.progressSeconds = Math.Min(task.durationSeconds, task.progressSeconds + seconds * TaskEfficiency() * taskPowerFraction);
            if (task.progressSeconds >= task.durationSeconds - 0.0000001) CompleteTask();
        }

        private void CompleteTask()
        {
            TaskState task = State.task;
            ResourceState r = State.resources;
            SiteState site = FindSite(task.targetId);
            CropState crop = FindCrop(task.targetId);
            string result;
            switch (task.kind)
            {
                case TaskKind.Rest:
                    result = "Rest completed. Fatigue fell; consumed supplies were not restored.";
                    break;
                case TaskKind.Exercise:
                    State.astronaut.fitness = Math.Min(100, State.astronaut.fitness + (Installed("H3") ? 3 : 1.6));
                    State.astronaut.lastExerciseSeconds = State.elapsedSeconds;
                    result = "Exercise completed. Fitness improved; recover through rest.";
                    break;
                case TaskKind.Treat:
                    bool stable = State.astronaut.hydration > 20 && State.astronaut.satiety > 20 && State.astronaut.hypoxiaSeconds == 0;
                    if (stable) State.astronaut.health = Math.Min(100, State.astronaut.health + (Installed("H2") ? 35 : 20));
                    result = stable ? "Medical treatment completed." : "Treatment used supplies, but unstable life support prevented recovery.";
                    break;
                case TaskKind.Plant:
                    crop.crop = task.cropId; crop.planted = true; crop.ready = false; crop.growth = 0; crop.stress = 0;
                    result = "Planted " + crop.crop + " in greenhouse bed " + (crop.bed + 1) + ".";
                    break;
                case TaskKind.Harvest:
                    double yield = (crop.crop == "staple" ? 50 : 24) * (Installed("H1") ? 1.25 : 1) * (1 - crop.stress * 0.4);
                    double stored = Math.Min(yield, Settings.foodCapacity - r.food);
                    r.food += stored;
                    r.seeds += 1;
                    State.totalFoodHarvested += stored;
                    crop.planted = false; crop.ready = false; crop.growth = 0; crop.harvests++;
                    result = "Harvested " + stored.ToString("0", CultureInfo.InvariantCulture) + " food portions and saved one seed.";
                    break;
                case TaskKind.RepairBase:
                    State.equipment.baseCondition = Math.Min(1, State.equipment.baseCondition + 0.35);
                    State.repairsCompleted++;
                    result = "Base equipment repaired.";
                    break;
                case TaskKind.RepairRover:
                    State.equipment.roverCondition = Math.Min(1, State.equipment.roverCondition + 0.35);
                    State.repairsCompleted++;
                    result = "Rover repaired.";
                    break;
                case TaskKind.SampleSoil:
                    site.sampled = true; site.discovered = true; site.sampleTime = State.elapsedSeconds;
                    result = "Soil sample " + site.id + " collected. Return to the lab for analysis.";
                    break;
                case TaskKind.Analyze:
                    if (!site.analyzed)
                    {
                        site.analyzed = true;
                        r.researchPoints += site.reward;
                        r.parts += 3;
                    }
                    result = "Analyzed " + site.name + " (+" + site.reward + " research; +3 mission supply parts). Parts are a fictional supply allotment, not extracted minerals.";
                    break;
                case TaskKind.PlaceMarker:
                    site.markerPlaced = true; site.discovered = true;
                    result = "Survey marker " + site.id + " placed. Frame it with the survey camera.";
                    break;
                case TaskKind.Photograph:
                    if (!site.photographed)
                    {
                        site.photographed = true; site.photoPath = task.evidencePath;
                        site.photoTime = State.elapsedSeconds;
                        r.researchPoints += 4;
                    }
                    result = "Marker photo documented (+4 research). It can be included in a Houston report.";
                    break;
                case TaskKind.DustPanels:
                    State.equipment.panelDust = Math.Max(0, State.equipment.panelDust - 0.7);
                    State.panelsCleaned++;
                    result = "Panel deposits removed. Generation still depends on sunlight and airborne dust.";
                    break;
                case TaskKind.Install:
                    if (task.targetId == "panel") State.equipment.extraPanels++;
                    else if (task.targetId == "battery") State.equipment.extraBatteries++;
                    else if (!State.installed.Contains(task.targetId)) State.installed.Add(task.targetId);
                    result = "Installed " + task.targetId + ".";
                    break;
                case TaskKind.Transmit:
                    result = "Houston acknowledged the report.";
                    break;
                default: result = "Task complete."; break;
            }
            State.task = new TaskState();
            Log(result);
        }

        private void TickRadio(double seconds, PlayerContext ctx)
        {
            if (State.radioMode != RadioMode.Transmit || Environment.signal <= 0.02 || radioPowerFraction <= 0) return;
            ReportState report = State.reports.Find(r => !r.acknowledged);
            if (report == null) return;
            report.progressSeconds = Math.Min(120, report.progressSeconds + seconds * Environment.signal * radioPowerFraction);
            if (report.progressSeconds >= 120 - 0.0000001)
            {
                report.progressSeconds = 120; report.acknowledged = true;
                Log("Houston acknowledged " + report.id + ".");
            }
        }

        private string ReportFingerprint()
        {
            return "houston-" + State.seed + "-" + (int)(State.elapsedSeconds / MissionSettings.SolSeconds) + "-"
                + State.sites.FindAll(s => s.analyzed).Count + "-" + State.sites.FindAll(s => s.photographed).Count + "-" + State.repairsCompleted;
        }

        private void RunRoutine(PlayerContext ctx)
        {
            if (State.task.kind != TaskKind.None) return;
            string ignored;
            if (State.astronaut.satiety < 70 && State.resources.food >= 1) Command("eat", "", ctx, out ignored);
            if (State.astronaut.hydration < 70 && State.resources.cleanWaterLiters >= 0.5) Command("drink", "", ctx, out ignored);
            if (State.astronaut.fatigue > 65) { StartTask(TaskKind.Rest, "", ctx, out ignored); return; }
            if (State.astronaut.health < 60 && State.resources.medicine >= 1) { StartTask(TaskKind.Treat, "", ctx, out ignored); return; }
            foreach (CropState crop in State.crops)
            {
                if (crop.ready && State.resources.food < Settings.foodCapacity * 0.8)
                {
                    StartTask(TaskKind.Harvest, crop.bed.ToString(CultureInfo.InvariantCulture), ctx, out ignored); return;
                }
                if (!crop.planted && State.resources.seeds >= 1 && State.resources.food < Settings.foodCapacity * 0.8)
                {
                    StartTask(TaskKind.Plant, crop.bed.ToString(CultureInfo.InvariantCulture) + ":" + crop.crop, ctx, out ignored); return;
                }
            }
            if (State.astronaut.fitness < 95 && State.elapsedSeconds - State.astronaut.lastExerciseSeconds >= MissionSettings.SolSeconds)
            {
                StartTask(TaskKind.Exercise, "", ctx, out ignored); return;
            }
            if (State.equipment.baseCondition < 0.75 && State.resources.parts >= 2) StartTask(TaskKind.RepairBase, "", ctx, out ignored);
        }
    }
}
