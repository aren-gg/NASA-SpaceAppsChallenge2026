using System.Collections.Generic;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>One line of the daily plan.</summary>
    public sealed class PlanStep
    {
        public string title, detail;
        public bool done, skipped;
        /// <summary>Where to go, in world space, or null when the step happens in the console.</summary>
        public Vector3? waypoint;
        public string waypointLabel;
    }

    /// <summary>
    /// The same order of jobs every sol: breakfast, power, greenhouse, maintenance, field trip,
    /// report to Houston, dinner, sleep. Built fresh from the mission state, so it always says
    /// what is left today, where to go and which tool to carry.
    /// </summary>
    public static class MissionDailyPlan
    {
        public const int Breakfast = 0, Power = 1, Greenhouse = 2, Maintenance = 3, Field = 4, Report = 5, Dinner = 6, Sleep = 7;

        public static List<PlanStep> Steps(MarsMissionController m)
        {
            var steps = new List<PlanStep>();
            if (m == null || m.Simulation == null) return steps;
            MissionState s = m.Simulation.State;
            DayState day = s.day ?? new DayState();
            AstronautState a = s.astronaut;
            double hour = m.Simulation.LocalHour;
            bool evening = hour >= 14 || hour < MissionSettings.MorningHour;

            // 1. Breakfast
            bool ate = day.ateMorning || a.satiety >= 92, drank = day.drankMorning || a.hydration >= 92;
            steps.Add(new PlanStep
            {
                title = "Breakfast",
                done = ate && drank,
                skipped = !(ate && drank) && evening,
                detail = "Galley (command hub): press E at the " + (ate ? "water dispenser" : drank ? "food locker" : "food locker and the water dispenser") + ".",
                waypoint = MarsWorldTasks.World(MarsWorldTasks.GalleyPoint), waypointLabel = "Galley",
            });

            // 2. Power
            steps.Add(new PlanStep
            {
                title = "Check the power",
                done = day.powerChecked,
                detail = "Tab > Power: drag each knob until its bar is full, without extra draw.",
            });

            // 3. Greenhouse
            var jobs = new List<string>();
            foreach (CropState crop in s.crops)
            {
                if (crop.ready) jobs.Add("pick bed " + (crop.bed + 1) + " (E on each ripe plant)");
                else if (crop.planted && crop.moisture < SoakGame.BandLow) jobs.Add("water bed " + (crop.bed + 1) + " (Sprayer 0)");
                else if (!crop.planted && s.resources.seeds >= 1)
                    jobs.Add("sow bed " + (crop.bed + 1) + " (Shovel 9, " + (CropState.Slots - CropState.Count(crop.sownMask)) + " spots left)");
            }
            steps.Add(new PlanStep
            {
                title = "Greenhouse",
                done = jobs.Count == 0,
                detail = jobs.Count == 0 ? "Beds watered and growing." : Sentence(string.Join(", ", jobs.ToArray())) + ". Tools hang by the potting bench.",
                waypoint = MarsWorldTasks.World(MarsWorldTasks.GreenhousePoint), waypointLabel = "Greenhouse",
            });

            // 4. Maintenance
            var fix = new List<string>();
            Vector3? fixAt = null;
            string fixLabel = null;
            if (s.faultActive)
            {
                int spot = Mathf.Clamp(s.faultSpot, 0, MarsWorldTasks.FaultSpots.Length - 1);
                fix.Add("fix the sparking fault on the " + MarsWorldTasks.FaultRooms[spot] + " ceiling (Field Wrench 8, from the energy room)");
                fixAt = MarsWorldTasks.World(MarsWorldTasks.FaultSpots[spot]);
                fixLabel = "Fault";
            }
            if (s.equipment.panelDust > 0.15)
            {
                fix.Add("sweep the dusty solar panels outside (Panel Duster 7)");
                if (fixAt == null) { fixAt = MarsWorldTasks.World(MarsWorldTasks.ArrayPoint); fixLabel = "Solar array"; }
            }
            steps.Add(new PlanStep
            {
                title = "Maintenance",
                done = fix.Count == 0,
                detail = fix.Count == 0 ? "Nothing is broken today." : Sentence(string.Join(", then ", fix.ToArray())) + ".",
                waypoint = fixAt, waypointLabel = fixLabel,
            });

            // 5. Field trip
            SiteState site = TodaysSite(m);
            PlanStep field = new PlanStep { title = "Field trip", done = day.fieldDone };
            if (day.fieldDone) field.detail = "Sample analysed. Well done!";
            else if (site == null) { field.skipped = true; field.detail = "Every science site is finished."; }
            else if (site.sampled)
            {
                field.detail = "Analyse the " + site.name + " core at the lab bench (Core Drill 2).";
                field.waypoint = MarsWorldTasks.World(MarsWorldTasks.LabPoint);
                field.waypointLabel = "Lab bench";
            }
            else
            {
                field.detail = "Take the Hammer (1) and Core Drill (2) from the lab, exit through the airlock and go to " + site.name +
                               ": strike its 3 glowing rocks, then drill a core.";
                field.waypoint = new Vector3((float)site.x, (float)site.y + 2f, (float)site.z);
                field.waypointLabel = site.name;
            }
            steps.Add(field);

            // 6. Report
            bool queued = s.reports.Exists(r => !r.acknowledged);
            steps.Add(new PlanStep
            {
                title = "Report to Houston",
                done = day.reportDone,
                detail = queued ? "Radio (4): click to transmit the queued report (needs a signal)." : "Radio (4): click to queue today's report, then click again to send it.",
            });

            // 7. Dinner and 8. sleep
            bool supper = hour >= 14 && (day.ateEvening || a.satiety >= 92) && (day.drankEvening || a.hydration >= 92);
            steps.Add(new PlanStep
            {
                title = "Dinner",
                done = supper,
                detail = "Back to the galley after 14:00: eat and drink before bed.",
                waypoint = MarsWorldTasks.World(MarsWorldTasks.GalleyPoint), waypointLabel = "Galley",
            });
            steps.Add(new PlanStep
            {
                title = "Sleep",
                detail = hour >= 17 || hour < MissionSettings.MorningHour ? "Press E at a bunk in the crew quarters. You wake at 07:00." : "After 17:00, sleep in a bunk in the crew quarters (E).",
                waypoint = MarsWorldTasks.World(MarsWorldTasks.BunkPoint), waypointLabel = "Bunk",
            });
            return steps;
        }

        /// <summary>The step to do now: the first one left, but dinner and bed once evening comes.</summary>
        public static int Current(List<PlanStep> steps, double hour)
        {
            if (steps.Count == 0) return -1;
            bool late = hour >= 18 || hour < MissionSettings.MorningHour;
            if (late) return steps[Dinner].done ? Sleep : Dinner;
            for (int i = 0; i < steps.Count; i++)
                if (!steps[i].done && !steps[i].skipped) return i;
            return Sleep;
        }

        /// <summary>The site the field trip goes to today: the selected one, else the nearest unfinished.</summary>
        public static SiteState TodaysSite(MarsMissionController m)
        {
            MissionState s = m.Simulation.State;
            SiteState selected = s.sites.Find(x => x.id == m.SelectedSiteId);
            if (selected != null && !selected.analyzed) return selected;
            SiteState waiting = s.sites.Find(x => x.sampled && !x.analyzed);
            if (waiting != null) return waiting;
            SiteState best = null;
            double bestDistance = double.MaxValue;
            foreach (SiteState site in s.sites)
            {
                if (site.analyzed) continue;
                if (site.distanceMeters < bestDistance) { bestDistance = site.distanceMeters; best = site; }
            }
            return best;
        }

        private static string Sentence(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
