using System;
using MarsSurvival.Simulation;

public static class SimulationChecks
{
    private static int checks;
    private static PlayerContext Home = new PlayerContext { zone = PlayerZone.Habitat, station = "habitat", roverAtBase = true };
    private static EnvironmentSample Clear = new EnvironmentSample { solarFraction = 1, temperatureC = -65,
        radiationMicrograyPerHour = 210.0 / 24, signal = 1, localHour = 12, label = "Test clear conditions" };

    public static int Main(string[] args)
    {
        try
        {
            AllocationAndConservation();
            ResourcesAndNeeds();
            TasksResearchAndReports();
            TerminalAndReplay();
            if (args.Length == 0 || args[0] != "--quick")
            {
                foreach (MissionLength length in Enum.GetValues(typeof(MissionLength)))
                {
                    Campaign(length, true);
                    Campaign(length, false);
                }
            }
            Console.WriteLine("PASS: " + checks + " simulation assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL after " + checks + " assertions: " + error);
            return 1;
        }
    }

    private static MissionSimulation Fresh()
    {
        MissionState state = MissionSimulation.NewMission(MissionLength.Month, 2026);
        state.routineEnabled = false;
        return new MissionSimulation(new MissionSettings(), state);
    }

    private static void AllocationAndConservation()
    {
        MissionSimulation sim = Fresh();
        double before = Stored(sim);
        sim.SetPowerRequest(PowerChannel.LifeControl, 1);
        sim.SetPowerRequest(PowerChannel.Temperature, 1);
        sim.SetPowerRequest(PowerChannel.RadiationShielding, 1);
        sim.State.resources.roverKwh = 10;
        sim.State.resources.fieldKwh = 0;
        sim.State.chargeRover = true;
        before = Stored(sim);
        sim.Step(3600, Clear, Home);
        Near(sim.Power.requestedKw[0], 3, 1e-9, "life request");
        Near(sim.Power.requestedKw[1], 5, 1e-9, "thermal request");
        Near(sim.Power.requestedKw[2], 4, 1e-9, "shield request");
        Near(before + sim.State.totalGeneratedKwh, Stored(sim) + sim.State.totalConsumedKwh + sim.State.totalLossKwh + sim.State.totalCurtailedKwh, 1e-7, "energy ledger with charging");
        Assert(sim.State.resources.roverKwh > 10, "rover charger transfers energy");
        Assert(sim.State.resources.fieldKwh > 0, "field charger transfers energy");

        MissionSimulation limited = Fresh();
        limited.State.resources.batteryKwh = 0;
        limited.SetPowerRequest(PowerChannel.LifeControl, 1);
        limited.SetPowerRequest(PowerChannel.Temperature, 1);
        limited.SetPowerRequest(PowerChannel.RadiationShielding, 1);
        EnvironmentSample weak = Clear; weak.solarFraction = 0.1;
        limited.Step(1, weak, Home);
        Near(limited.Power.deliveredKw[0] / 3, limited.Power.deliveredKw[1] / 5, 1e-8, "proportional shortage");
        Near(limited.Power.deliveredKw[1] / 5, limited.Power.deliveredKw[2] / 4, 1e-8, "all three shortage factors");
        Near(limited.State.knobs[0], 1, 0, "shortage preserves request");
        MissionSimulation normal = Fresh();
        EnvironmentSample night = Clear; night.solarFraction = 0;
        normal.Step(3600, night, Home);
        sim = Fresh();
        for (int i = 0; i < 3; i++) sim.SetPowerRequest((PowerChannel)i, 1);
        sim.Step(3600, night, Home);
        Assert(sim.State.resources.batteryKwh < normal.State.resources.batteryKwh - 5, "excess settings spend real energy");

        // Overdraw: every kW beyond the useful need costs overdrawCostMultiplier kW in total.
        MissionSimulation plain = Fresh();
        plain.Settings.overdrawCostMultiplier = 1;
        for (int i = 0; i < 3; i++) plain.SetPowerRequest((PowerChannel)i, 0.6);
        plain.Step(3600, night, Home);
        MissionSimulation penalised = Fresh();
        for (int i = 0; i < 3; i++) penalised.SetPowerRequest((PowerChannel)i, 0.6);
        double ledgerBefore = Stored(penalised);
        penalised.Step(3600, night, Home);
        double excessKw = 0;
        for (int i = 0; i < 3; i++) excessKw += Math.Max(0, penalised.Power.deliveredKw[i] - penalised.Power.neededKw[i]);
        Near(penalised.Power.overdrawLossKw, excessKw * 2, 1e-6, "overdraw waste is twice the excess (3x total cost)");
        Near(plain.State.resources.batteryKwh - penalised.State.resources.batteryKwh, excessKw * 2, 1e-3, "overdraw drains the battery faster");
        Near(ledgerBefore + penalised.State.totalGeneratedKwh, Stored(penalised) + penalised.State.totalConsumedKwh + penalised.State.totalLossKwh + penalised.State.totalCurtailedKwh, 1e-7, "energy ledger with overdraw");
        MissionSimulation balanced = Fresh();
        balanced.Step(1, night, Home);
        for (int i = 0; i < 3; i++) balanced.SetPowerRequest((PowerChannel)i, balanced.Power.neededKw[i] / new[] { 3.0, 5.0, 4.0 }[i]);
        balanced.Step(3600, night, Home);
        Near(balanced.Power.overdrawLossKw, 0, 1e-9, "no overdraw waste when supply matches need");
        Assert(!sim.SetPowerRequest(PowerChannel.LifeControl, double.NaN), "invalid knob rejected");
    }

    private static void ResourcesAndNeeds()
    {
        MissionSimulation sim = Fresh();
        string reason;
        sim.State.resources.suitOxygenGrams = 200;
        double oxygen = sim.State.resources.suitOxygenGrams + sim.State.resources.oxygenGrams;
        Assert(sim.Command("refill", "", Home, out reason), "refill succeeds");
        Near(sim.State.resources.suitOxygenGrams + sim.State.resources.oxygenGrams, oxygen, 1e-8, "oxygen transfer conservation");
        PlayerContext field = new PlayerContext { zone = PlayerZone.Outdoors };
        Assert(!sim.Command("refill", "", field, out reason), "remote refill rejected");
        // Oxygen Canister tool: filled from habitat oxygen inside, emptied into the suit on EVA.
        Func<double> allOxygen = () => sim.State.resources.oxygenGrams + sim.State.resources.suitOxygenGrams + sim.State.resources.canisterOxygenGrams;
        sim.State.resources.canisterOxygenGrams = 0;
        oxygen = allOxygen();
        Assert(!sim.Command("canister_fill", "", field, out reason), "canister fill needs the habitat");
        Assert(sim.Command("canister_fill", "", Home, out reason), "canister fill succeeds");
        Near(sim.State.resources.canisterOxygenGrams, sim.Settings.canisterCapacityGrams, 1e-8, "canister filled");
        Near(allOxygen(), oxygen, 1e-8, "canister fill conservation");
        sim.State.resources.suitOxygenGrams = 100;
        oxygen = allOxygen();
        Assert(sim.Command("canister_use", "", field, out reason), "canister field top-up");
        Near(sim.State.resources.suitOxygenGrams, 100 + sim.Settings.canisterDoseGrams, 1e-8, "canister dose delivered");
        Near(allOxygen(), oxygen, 1e-8, "canister use conservation");
        sim.State.resources.suitOxygenGrams = sim.Settings.suitCapacityGrams;
        Assert(!sim.Command("canister_use", "", field, out reason), "full suit rejects canister");
        // Sprayer tool: watering a stressed growing bed.
        sim.State.crops[0].planted = true; sim.State.crops[0].ready = false; sim.State.crops[0].stress = 0.5;
        double clean = sim.State.resources.cleanWaterLiters;
        Assert(sim.Command("water_crop", "0", Home, out reason), "water stressed crop");
        Near(sim.State.crops[0].stress, 0.25, 1e-9, "watering lowers stress");
        Near(sim.State.resources.cleanWaterLiters, clean - 1, 1e-9, "watering uses 1 L");
        sim.State.crops[0].stress = 0;
        Assert(!sim.Command("water_crop", "0", Home, out reason), "unstressed crop needs no water");
        sim.State.crops[0].planted = false;
        sim.State.astronaut.hydration = 40;
        double water = sim.State.resources.cleanWaterLiters + sim.State.resources.wasteWaterLiters;
        Assert(sim.Command("drink", "", Home, out reason), "drink succeeds");
        Near(water - (sim.State.resources.cleanWaterLiters + sim.State.resources.wasteWaterLiters), 0.1, 1e-7, "drinking net water loss");
        sim.State.resources.cleanWaterLiters = 0; sim.State.resources.wasteWaterLiters = 0;
        sim.State.resources.oxygenGrams = 1000;
        sim.Step(10, Clear, Home);
        Assert(sim.State.totalOxygenProducedGrams == 0, "oxygen requires water");
        sim = Fresh();
        sim.State.resources.food = 0;
        sim.State.astronaut.satiety = 0;
        sim.Step(10000, Clear, Home);
        Assert(sim.State.astronaut.health < 100 && sim.State.astronaut.lastDamageCause == "Prolonged starvation", "explained hunger damage");

        sim = Fresh();
        Assert(sim.StartTask(TaskKind.Plant, "0:leafy", Home, out reason), "plant task");
        Finish(sim, Home, Clear);
        double growth = sim.State.crops[0].growth;
        sim.State.greenhouseEnabled = false;
        sim.Step(3600, Clear, Home);
        Near(sim.State.crops[0].growth, growth, 1e-8, "no crop growth with no greenhouse power");
        sim.State.greenhouseEnabled = true;
        sim.State.routineEnabled = true;
        Advance(sim, 6 * MissionSettings.SolSeconds, Home, Clear);
        Assert(sim.State.totalFoodHarvested >= 24, "routine harvest produces food");
        Assert(sim.State.resources.seeds > 0, "seed sustainability");
    }

    private static void TasksResearchAndReports()
    {
        MissionSimulation sim = Fresh();
        string reason;
        sim.State.resources.researchPoints = 1000;
        sim.State.resources.parts = 1000;
        Assert(!sim.Research("S3", out reason), "capstone prerequisites enforced");
        foreach (ResearchNode node in ResearchCatalog.Nodes)
        {
            Assert(sim.Research(node.id, out reason), "research " + node.id);
            double parts = sim.State.resources.parts;
            TaskQuote quote = sim.GetTaskQuote(TaskKind.Install, node.id, Home);
            Assert(quote.available && sim.State.resources.parts == parts, "quote has no side effects " + node.id);
            Assert(sim.StartTask(TaskKind.Install, node.id, Home, out reason), "install " + node.id);
            Assert(!sim.Installed(node.id), "installation not immediate " + node.id);
            Finish(sim, Home, Clear);
            Assert(sim.Installed(node.id), "installed effect " + node.id);
            Assert(!sim.Research(node.id, out reason), "no double purchase " + node.id);
        }
        Assert(sim.StartTask(TaskKind.Install, "panel", Home, out reason), "panel investment");
        sim.CancelTask();
        Assert(sim.State.equipment.extraPanels == 0, "cancelled panel not installed");
        sim.State.equipment.baseCondition = 0.5;
        Assert(sim.StartTask(TaskKind.RepairBase, "", Home, out reason), "base repair"); Finish(sim, Home, Clear);
        Assert(sim.State.equipment.baseCondition > 0.8, "base repaired");
        sim.State.equipment.roverCondition = 0.5;
        Assert(sim.StartTask(TaskKind.RepairRover, "", Home, out reason), "rover repair"); Finish(sim, Home, Clear);
        Assert(sim.State.equipment.roverCondition > 0.8, "rover repaired");
        sim.State.equipment.panelDust = 0.5;
        Assert(sim.StartTask(TaskKind.DustPanels, "", Home, out reason), "dust task"); Finish(sim, Home, Clear);
        Assert(sim.State.equipment.panelDust < 0.01, "deposits removed");
        sim.State.astronaut.health = 50;
        Assert(sim.StartTask(TaskKind.Treat, "", Home, out reason), "treatment"); Finish(sim, Home, Clear);
        Assert(sim.State.astronaut.health > 75, "medical upgrade effective");
        double fitness = sim.State.astronaut.fitness;
        Assert(sim.StartTask(TaskKind.Exercise, "", Home, out reason), "exercise"); Finish(sim, Home, Clear);
        Assert(sim.State.astronaut.fitness > fitness, "exercise fitness benefit");

        SiteState site = new SiteState { id = "site-1", name = "Survey ridge", reward = 16 };
        sim.State.sites.Add(site);
        PlayerContext field = new PlayerContext { zone = PlayerZone.Outdoors, nearbySiteId = site.id,
            photoFramingValid = true, photoEvidencePath = "unit-test-evidence.png" };
        foreach (TaskKind kind in new[] { TaskKind.SampleSoil, TaskKind.PlaceMarker, TaskKind.Photograph })
        {
            Assert(sim.StartTask(kind, site.id, field, out reason), "field " + kind); Finish(sim, field, Clear);
        }
        Assert(site.sampled && site.markerPlaced && site.photographed && site.photoPath.Length > 0, "persistent science evidence");
        Assert(!sim.StartTask(TaskKind.SampleSoil, site.id, field, out reason), "sample cannot duplicate");
        Assert(sim.StartTask(TaskKind.Analyze, site.id, Home, out reason), "analysis"); Finish(sim, Home, Clear);
        Assert(site.analyzed, "analysis complete");
        Assert(!sim.StartTask(TaskKind.Analyze, site.id, Home, out reason), "analysis cannot duplicate");
        Assert(sim.Command("report", "", Home, out reason), "queue report");
        Assert(!sim.Command("report", "", Home, out reason), "no identical report spam");
        Assert(sim.StartTask(TaskKind.Transmit, "", Home, out reason), "transmit task");
        EnvironmentSample blocked = Clear; blocked.signal = 0;
        sim.Step(120, blocked, Home);
        Assert(!sim.State.reports[0].acknowledged && sim.State.reports[0].progressSeconds == 0, "no signal keeps report queued");
        EnvironmentSample weak = Clear; weak.signal = 0.25;
        sim.Step(120, weak, Home);
        Assert(!sim.State.reports[0].acknowledged && sim.State.reports[0].progressSeconds > 0, "weak link slower");
        Finish(sim, Home, Clear);
        Assert(sim.State.reports[0].acknowledged, "Houston acknowledges once");
    }

    private static void TerminalAndReplay()
    {
        foreach (MissionLength length in Enum.GetValues(typeof(MissionLength)))
        {
            MissionState state = MissionSimulation.NewMission(length, 1); state.routineEnabled = false;
            state.elapsedSeconds = MissionSettings.Duration(length) - 0.5;
            MissionSimulation sim = new MissionSimulation(new MissionSettings(), state);
            sim.Step(10, Clear, Home);
            Assert(state.phase == MissionPhase.Won, "campaign terminal " + length);
            Near(state.elapsedSeconds, MissionSettings.Duration(length), 0, "exact duration " + length);
            double battery = state.resources.batteryKwh;
            sim.Step(1000, Clear, Home);
            Near(state.resources.batteryKwh, battery, 0, "terminal stops transfers");
        }
        MissionSimulation lethal = Fresh();
        lethal.State.elapsedSeconds = lethal.DurationSeconds - 1;
        lethal.State.astronaut.health = 0.0001; // below one second of hypoxia damage (60 HP per sol)
        lethal.State.astronaut.hypoxiaSeconds = 60;
        lethal.State.astronaut.oxygen = 0;
        lethal.State.resources.oxygenGrams = 0; lethal.State.resources.cleanWaterLiters = 0;
        lethal.State.resources.wasteWaterLiters = 0; lethal.State.knobs[0] = 0;
        lethal.Step(1, Clear, Home);
        Assert(lethal.State.phase == MissionPhase.Lost, "lethal final tick takes precedence");
        MissionSimulation a = Fresh(), b = Fresh();
        for (int i = 0; i < 3600; i++) a.Step(1, Clear, Home);
        for (int i = 0; i < 360; i++) b.Step(10, Clear, Home);
        Near(a.State.resources.batteryKwh, b.State.resources.batteryKwh, 0.02, "time step energy agreement");
        Near(a.State.astronaut.doseMicrogray, b.State.astronaut.doseMicrogray, 1e-7, "dose integration agreement");
        MissionSimulation exposed = Fresh(), sheltered = Fresh();
        EnvironmentSample eventSample = Clear; eventSample.radiationMicrograyPerHour = 2000; eventSample.solarEvent = 1;
        PlayerContext shelter = Home; shelter.zone = PlayerZone.Shelter;
        exposed.Step(3600, eventSample, Home); sheltered.Step(3600, eventSample, shelter);
        Assert(sheltered.State.astronaut.doseMicrogray < exposed.State.astronaut.doseMicrogray, "shelter lowers future dose");
        double previous = sheltered.State.astronaut.doseMicrogray;
        sheltered.Step(10, Clear, shelter);
        Assert(sheltered.State.astronaut.doseMicrogray >= previous, "shelter never erases dose");
    }

    private static void Campaign(MissionLength length, bool solarStrategy)
    {
        MissionState state = MissionSimulation.NewMission(length, 2026);
        state.routineEnabled = false;
        MissionSimulation sim = new MissionSimulation(new MissionSettings(), state);
        string reason;
        // Starting research comes only from real command/task rewards, never injected points.
        for (int i = 0; i < 6; i++)
        {
            SiteState site = new SiteState { id = "route-" + i, name = "Test route " + i, reward = 16 };
            state.sites.Add(site);
            PlayerContext field = new PlayerContext { zone = PlayerZone.Outdoors, nearbySiteId = site.id,
                photoFramingValid = true, photoEvidencePath = "test-route-" + i + ".png" };
            Assert(sim.Command("survey", site.id, field, out reason), "campaign terrain survey");
            Assert(sim.Command("measure", site.id, field, out reason), "campaign environmental measure");
            foreach (TaskKind kind in new[] { TaskKind.SampleSoil, TaskKind.PlaceMarker, TaskKind.Photograph })
            { Assert(sim.StartTask(kind, site.id, field, out reason), "campaign science task"); Finish(sim, field, Clear); }
            Assert(sim.StartTask(TaskKind.Analyze, site.id, Home, out reason), "campaign analysis"); Finish(sim, Home, Clear);
        }
        string branch = solarStrategy ? "S" : "P";
        foreach (string id in new[] { branch + "0", branch + "1", branch + "2", branch + "3", "E0", "E1", "E2", "E3" })
        {
            Assert(sim.Research(id, out reason), "campaign research " + id);
            Assert(sim.StartTask(TaskKind.Install, id, Home, out reason), "campaign install " + id); Finish(sim, Home, Clear);
        }
        for (int i = 0; i < 2; i++)
        { Assert(sim.StartTask(TaskKind.Install, "battery", Home, out reason), "campaign battery"); Finish(sim, Home, Clear); }
        if (solarStrategy)
            for (int i = 0; i < 2; i++)
            { Assert(sim.StartTask(TaskKind.Install, "panel", Home, out reason), "campaign panel"); Finish(sim, Home, Clear); }
        state.routineEnabled = true;
        double minBattery = state.resources.batteryKwh, minWater = state.resources.cleanWaterLiters;
        while (state.phase == MissionPhase.Running)
        {
            double sol = state.elapsedSeconds / MissionSettings.SolSeconds;
            double hour = ((sol % 1) * 24 + 7) % 24;
            double dust = sol % 61 > 40 && sol % 61 < 41.5 ? 0.7 : 0;
            bool particle = sol % 97 > 20 && sol % 97 < 20.25;
            EnvironmentSample env = Clear;
            env.localHour = hour;
            env.temperatureC = -65 + 20 * Math.Sin((hour - 8) / 24 * Math.PI * 2);
            env.solarFraction = Math.Max(0, Math.Sin((hour - 6) / 12 * Math.PI)) * (1 - dust * 0.75);
            env.dust = dust; env.solarEvent = particle ? 1 : 0;
            env.radiationMicrograyPerHour = particle ? 2000 : 210.0 / 24;
            PlayerContext ctx = Home; if (particle) ctx.zone = PlayerZone.Shelter;
            double condition = Math.Max(0.1, state.equipment.baseCondition);
            sim.SetPowerRequest(PowerChannel.LifeControl, 1.05 * 0.75 / condition / 3 * 1.03);
            sim.SetPowerRequest(PowerChannel.Temperature, Math.Abs(21 - env.temperatureC) * 0.02 * (sim.Installed("P2") ? 0.7 : 1) / condition / 5 * 1.03);
            sim.SetPowerRequest(PowerChannel.RadiationShielding, (0.2 + env.solarEvent * 2.6) / 4);
            if (!solarStrategy && state.equipment.panelDust > 0.12 && state.task.kind == TaskKind.None)
            { Assert(sim.StartTask(TaskKind.DustPanels, "", Home, out reason), "campaign cleaning"); }
            sim.Step(60, env, ctx);
            minBattery = Math.Min(minBattery, state.resources.batteryKwh);
            minWater = Math.Min(minWater, state.resources.cleanWaterLiters);
        }
        Assert(state.phase == MissionPhase.Won, "full " + length + " " + branch + " campaign survived: " + state.endingReason);
        Assert(minWater > 0 && state.resources.nutrients > 0 && state.resources.seeds > 0, "finite consumables sufficient " + length + " " + branch);
        Console.WriteLine(string.Format("Campaign {0}/{1}: health={2:0.0}, battery min={3:0.0} kWh, water={4:0.0} L, harvest={5:0} portions", length, branch,
            state.astronaut.health, minBattery, state.resources.cleanWaterLiters, state.totalFoodHarvested));
    }

    private static void Finish(MissionSimulation sim, PlayerContext ctx, EnvironmentSample env)
    {
        int attempts = 0;
        while (sim.State.task.kind != TaskKind.None && sim.State.phase == MissionPhase.Running && attempts++ < 10000) sim.Step(10, env, ctx);
        Assert(sim.State.task.kind == TaskKind.None, "task completed within budget");
    }
    private static void Advance(MissionSimulation sim, double seconds, PlayerContext ctx, EnvironmentSample env)
    {
        while (seconds > 0 && sim.State.phase == MissionPhase.Running) { double dt = Math.Min(60, seconds); sim.Step(dt, env, ctx); seconds -= dt; }
    }
    private static double Stored(MissionSimulation sim) { return sim.State.resources.batteryKwh + sim.State.resources.roverKwh + sim.State.resources.fieldKwh; }
    private static void Assert(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected, double tolerance, string message) { Assert(Math.Abs(actual - expected) <= tolerance, message + ": " + actual + " vs " + expected); }
}
