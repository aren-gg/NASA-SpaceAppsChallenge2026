using System;
using System.Collections.Generic;
using System.Globalization;
using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Hardware;
using MarsSurvival.Simulation;
using MarsSurvival.World;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsSurvival.UI
{
    public sealed class MarsMissionHud : MonoBehaviour
    {
        private static readonly string[] TabNames = { "Overview", "Base tasks", "Research", "Field log", "Radio", "Learning", "Help", "Settings" };
        private const int HelpTab = 6;
        private readonly List<Action> live = new List<Action>();
        private readonly List<Action> shellLive = new List<Action>();
        private readonly List<Selectable> controls = new List<Selectable>();
        private MarsMissionController mission;
        private MissionState drawnState;
        private Canvas canvas;
        private CanvasScaler scaler;
        private Font font;
        private RectTransform panel, content, selector;
        private Text status, needs, alert, guide, location;
        private ScrollRect scroll;
        private int tab, drawnPhase = -1, drawnReports = -1;
        private bool built, lastOpen, suppressSliders;
        private float nextRefresh, uiScale = 1;
        private Color ink, paper, accent, muted;

        private void Start()
        {
            BuildShell();
        }

        private void Update()
        {
            if (!built) return;
            mission = MarsMissionController.Instance;
            bool visible = mission != null && mission.Ready && !MarsHomeScreen.IsOpen;
            canvas.enabled = visible;
            if (!visible) return;

            HandleKeys();
            MissionState state = mission.Simulation == null ? null : mission.Simulation.State;
            if (state == null && !mission.PanelOpen) mission.SetPanelOpen(true);
            int phase = state == null ? -1 : (int)state.phase;
            if (state != drawnState || phase != drawnPhase)
            {
                drawnState = state;
                drawnPhase = phase;
                if (state == null || state.phase != MissionPhase.Running) mission.SetPanelOpen(true);
                RebuildPanel();
            }
            if (mission.PanelOpen != lastOpen)
            {
                lastOpen = mission.PanelOpen;
                panel.gameObject.SetActive(lastOpen);
                if (lastOpen)
                {
                    RebuildPanel();
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                RefreshShell();
                if (mission.PanelOpen)
                {
                    if (state != null && tab == 4 && drawnReports != state.reports.Count) RebuildPanel();
                    foreach (Action update in live) update();
                }
            }
        }

        private void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            if (typing) return;
            if (Input.GetKeyDown(KeyCode.Tab)) TogglePanel();
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (mission.PanelOpen && tab == HelpTab && mission.IsRunning) mission.SetPanelOpen(false);
                else OpenHelp();
            }
            if (Input.GetKeyDown(KeyCode.P) && mission.IsRunning) mission.SetPaused(!mission.Paused);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (mission.PanelOpen && mission.IsRunning) mission.SetPanelOpen(false);
                else mission.SetPanelOpen(true);
            }
            if (Input.GetKeyDown(KeyCode.F5)) mission.SaveMission();
            if (Input.GetKeyDown(KeyCode.F9)) mission.LoadMission();
#endif
        }

        private void BuildShell()
        {
            ink = MarsPalette.DeepPurple;
            paper = MarsPalette.ClaySand;
            accent = MarsPalette.WarmOrange;
            muted = MarsPalette.PalePink;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject root = new GameObject("Mission Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("Mission Event System", typeof(EventSystem), typeof(StandaloneInputModule));

            RectTransform header = Box("Mission readouts", root.transform, WithAlpha(ink, 0.94f));
            Anchor(header, new Vector2(0, 1), Vector2.one, new Vector2(24, -140), new Vector2(-24, -20));
            status = Label(header, "MARS OUTPOST", 27, accent);
            Anchor(status.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(20, -46), new Vector2(-530, -10));
            needs = Label(header, "", 23, paper);
            Anchor(needs.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 10), new Vector2(-20, -55));
            RectTransform headerButtons = Row(header, 46);
            Anchor(headerButtons, Vector2.one, Vector2.one, new Vector2(-660, -53), new Vector2(-20, -7));
            SmallButton(headerButtons, "Help  [H]", OpenHelp);
            SmallButton(headerButtons, "Console  [Tab]", TogglePanel);
            SmallButton(headerButtons, "Pause  [P]", () => { if (mission != null) mission.SetPaused(!mission.Paused); });
            SmallButton(headerButtons, "Save  [F5]", () => { if (mission != null) mission.SaveMission(); });

            // Bottom bar: the latest message, then a survival guide for right now, then where you are.
            RectTransform footer = Box("Mission feedback", root.transform, WithAlpha(ink, 0.94f));
            Anchor(footer, Vector2.zero, new Vector2(1, 0), new Vector2(24, 18), new Vector2(-24, 172));
            alert = Label(footer, "", 22, paper);
            Anchor(alert.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(20, -42), new Vector2(-20, -8));
            guide = Label(footer, "", 23, accent);
            Anchor(guide.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 36), new Vector2(-20, -44));
            location = Label(footer, "", 19, muted);
            Anchor(location.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(20, 6), new Vector2(-20, 34));

            panel = Box("Outpost console", root.transform, WithAlpha(ink, 0.985f));
            Anchor(panel, Vector2.zero, Vector2.one, new Vector2(100, 190), new Vector2(-100, -160));
            RectTransform titleRow = Row(panel, 50);
            Anchor(titleRow, new Vector2(0, 1), Vector2.one, new Vector2(26, -64), new Vector2(-26, -14));
            Text title = Label(titleRow, "OUTPOST CONSOLE", 30, accent);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            SmallButton(titleRow, "Return to world  [Tab]", () => { if (mission != null && mission.IsRunning) mission.SetPanelOpen(false); });

            selector = Row(panel, 46);
            Anchor(selector, new Vector2(0, 1), Vector2.one, new Vector2(26, -120), new Vector2(-26, -74));
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                SmallButton(selector, TabNames[i], () => { tab = index; RebuildPanel(); });
            }

            RectTransform scrollRoot = NewRect("Scrollable content", panel);
            Anchor(scrollRoot, Vector2.zero, Vector2.one, new Vector2(26, 22), new Vector2(-26, -136));
            scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            RectTransform viewport = Box("Viewport", scrollRoot, new Color(0, 0, 0, 0));
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.padding = new RectOffset(4, 16, 4, 20);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            built = true;
            panel.gameObject.SetActive(false);
        }

        private void RefreshShell()
        {
            MissionSimulation simulation = mission.Simulation;
            if (simulation == null)
            {
                status.text = "MARS OUTPOST  /  CHOOSE A MISSION";
                needs.text = "Life control  •  Temperature  •  Radiation shielding";
                alert.text = string.IsNullOrEmpty(mission.Notice) ? "Choose how long you will maintain your outpost." : mission.Notice;
                guide.text = "HOW TO PLAY: pick a mission length, then press H at any time for the full guide.";
                location.text = "A Martian month is one-twelfth of a Martian year in this game.";
                return;
            }
            MissionState s = simulation.State;
            double duration = MissionSettings.Duration(s.length);
            status.text = string.Format(CultureInfo.InvariantCulture,
                "{0}  /  SOL {1:0.0}  /  {2:0.0}% COMPLETE   {3}", LengthName(s.length).ToUpperInvariant(),
                1 + s.elapsedSeconds / MissionSettings.SolSeconds, s.elapsedSeconds / duration * 100,
                mission.Paused ? "PAUSED" : mission.AdvanceRemaining > 0 ? "ADVANCING" : "");
            needs.text = string.Format(CultureInfo.InvariantCulture,
                "HP {0:0}    Food need {1:0}%    Hydration {2:0}%    Fatigue {3:0}%    Suit O₂ {4:0} g    Battery {5:0.0} kWh",
                s.astronaut.health, s.astronaut.satiety, s.astronaut.hydration, s.astronaut.fatigue,
                s.resources.suitOxygenGrams, s.resources.batteryKwh);
            string warning = simulation.Warnings.Count == 0 ? "" : simulation.Warnings[0];
            alert.text = !string.IsNullOrEmpty(warning) ? "WARNING: " + warning : !string.IsNullOrEmpty(mission.Notice) ? mission.Notice : "Objective: " + NextObjective(s);
            guide.text = "SURVIVE THE SOL  >  " + SurvivalGuide(s) + "   [H] full guide";
            string nearby = string.IsNullOrEmpty(mission.Context.station) ? mission.Context.zone.ToString() : mission.Context.station.Replace('_', ' ');
            location.text = string.Format(CultureInfo.InvariantCulture,
                "{0}  |  {1}  |  Solar {2:0.0} kW · Load {3:0.0} kW · Net {4:+0.0;-0.0;0.0} kW  |  [Tab] console · [P] pause",
                nearby, mission.Environment.label ?? "Environment loading", simulation.Power.generatedKw, simulation.Power.loadKw, simulation.Power.netKw);
        }

        private void RebuildPanel()
        {
            if (!built || mission == null) return;
            live.Clear();
            controls.Clear();
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject old = content.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            MissionState state = mission.Simulation == null ? null : mission.Simulation.State;
            selector.gameObject.SetActive(state != null && state.phase == MissionPhase.Running);
            if (tab == HelpTab && (state == null || state.phase == MissionPhase.Running)) BuildHelp();
            else if (state == null) BuildMissionSelector();
            else if (state.phase != MissionPhase.Running) BuildDebrief();
            else
            {
                switch (tab)
                {
                    case 0: BuildOverview(); break;
                    case 1: BuildBaseTasks(); break;
                    case 2: BuildResearch(); break;
                    case 3: BuildFieldLog(); break;
                    case 4: BuildRadio(); break;
                    case 5: BuildLearning(); break;
                    case HelpTab: BuildHelp(); break;
                    default: BuildSettings(); break;
                }
            }
            foreach (Action update in live) update();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1;
            if (EventSystem.current != null && controls.Count > 0)
                EventSystem.current.SetSelectedGameObject(controls[0].gameObject);
        }

        private void BuildMissionSelector()
        {
            Heading("HOW LONG WILL YOU STAY?");
            Paragraph("One astronaut. Three power controls. A real patch of Gale Crater. Keep every essential system supplied, and leave enough energy for exploration and tomorrow.");
            MissionChoice(MissionLength.Month, "ONE MARTIAN MONTH", "About 56 sols · A compact campaign with every survival system and all four research paths.");
            MissionChoice(MissionLength.HalfYear, "HALF A MARTIAN YEAR", "About 334 sols · Prepare for changing conditions and longer resource demands.");
            MissionChoice(MissionLength.Year, "ONE MARTIAN YEAR", "About 669 sols · The full endurance mission, with safe time advancement and local saves.");
            Paragraph("A Martian month means one-twelfth of a Martian year in this game. All presets use the same physical units. You can pause, save, and continue later.", 20);
            Button(content, "Continue saved mission", () => mission.LoadMission());
        }

        private void MissionChoice(MissionLength length, string title, string description)
        {
            RectTransform card = Card();
            LabelInFlow(card, title, 27, accent);
            LabelInFlow(card, description, 22, paper);
            Button(card, "Start " + LengthName(length).ToLowerInvariant(), () =>
            {
                tab = 0;
                mission.NewMission(length);
                mission.SetPanelOpen(true);
                RebuildPanel();
            });
        }

        private void BuildOverview()
        {
            Heading("SPEND ENOUGH. KEEP A RESERVE.");
            Paragraph("Each knob sets the power that system requests. Power beyond a system's useful need is wasted and costs three times as much battery energy, so overdrawing drains the battery fast. If supply runs short, the three channels share the shortfall; other work waits for remaining power.", 21);
            string[] names = { "Life control", "Temperature", "Radiation shielding" };
            string[] hints = { "Oxygen production, air processing and water recycling", "Heating and cooling for a safe habitat", "Extra modeled protection; permanent shielding stays in place" };
            for (int i = 0; i < 3; i++)
            {
                int channel = i;
                RectTransform card = Card();
                LabelInFlow(card, names[i], 27, accent);
                LabelInFlow(card, hints[i], 19, muted);
                Slider knob = Slider(card, (float)State.knobs[channel], value =>
                {
                    if (suppressSliders) return;
                    if (mission.Dashboard != null) mission.Dashboard.NotifySoftwareOverride((PowerChannel)channel);
                    mission.SetPowerRequest((PowerChannel)channel, value);
                });
                Text readout = LabelInFlow(card, "", 22, paper);
                live.Add(() =>
                {
                    suppressSliders = true;
                    knob.SetValueWithoutNotify((float)State.knobs[channel]);
                    suppressSliders = false;
                    PowerSnapshot p = mission.Simulation.Power;
                    readout.text = string.Format(CultureInfo.InvariantCulture,
                        "Setting {0:0}%   ·   Request {1:0.00} kW   ·   Delivered {2:0.00} kW   ·   Useful need ~{3:0.00} kW{4}",
                        State.knobs[channel] * 100, p.requestedKw[channel], p.deliveredKw[channel], p.neededKw[channel],
                        p.deliveredKw[channel] + 0.01 < p.neededKw[channel] ? "   — SHORTFALL" : p.deliveredKw[channel] > p.neededKw[channel] + 0.3 ? "   — EXTRA DRAW" : "");
                });
            }
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "Battery {0:0.0} / {1:0.0} kWh   ·   Solar {2:0.00} kW   ·   Load {3:0.00} kW   ·   {4}\nGreenhouse {5:0.00} kW   ·   Task {6:0.00} kW   ·   Charging {7:0.00} kW   ·   Radio {8:0.00} kW   ·   Overdraw waste {9:0.00} kW",
                State.resources.batteryKwh, mission.Simulation.BatteryCapacity, mission.Simulation.Power.generatedKw,
                mission.Simulation.Power.loadKw, Endurance(), mission.Simulation.Power.greenhouseKw,
                mission.Simulation.Power.taskKw, mission.Simulation.Power.chargingKw, mission.Simulation.Power.radioKw,
                mission.Simulation.Power.overdrawLossKw));
            LiveParagraph(() => "Next step: " + NextObjective(State));
            BuildTimeControls();
            Heading("CONDITIONS AND WARNINGS");
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "Outside {0:0.0} °C   ·   Habitat {1:0.0} °C   ·   Exposure {2:0.0} µGy/h   ·   Accumulated {3:0.0} µGy\n{4}",
                mission.Environment.temperatureC, State.equipment.habitatTemperatureC, mission.Environment.radiationMicrograyPerHour,
                State.astronaut.doseMicrogray, mission.Simulation.Warnings.Count == 0 ? "No immediate warnings." : string.Join("\n", mission.Simulation.Warnings.ToArray())));
            TaskProgress();
        }

        private void BuildTimeControls()
        {
            Heading("ADVANCE SAFELY");
            Paragraph("Stay inside and stationary. The daily routine uses real supplies for meals, rest, exercise and crop care. Advancement stops when a decision is needed.", 20);
            RectTransform row = Row(content, 48);
            SmallButton(row, "Pause / resume", () => mission.SetPaused(!mission.Paused));
            SmallButton(row, "+ 1 hour", () => mission.Advance(3600));
            SmallButton(row, "+ 1 sol", () => mission.Advance(MissionSettings.SolSeconds));
            SmallButton(row, "+ 5 sols", () => mission.Advance(5 * MissionSettings.SolSeconds));
            SmallButton(row, "Stop advancement", () => mission.StopAdvance("Time advancement stopped."));
            LiveParagraph(() => "Daily routine: " + (State.routineEnabled ? "ON" : "OFF") +
                (mission.AdvanceRemaining > 0 ? "   ·   Remaining requested time: " + Duration(mission.AdvanceRemaining) : ""), 20);
            Button(content, "Toggle daily routine", () => mission.Execute("routine"));
        }

        private void BuildBaseTasks()
        {
            Heading("KEEP THE OUTPOST WORKING");
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "Food {0:0.0} portions · Clean water {1:0.0} L · Wastewater {2:0.0} L\nHabitat O₂ {3:0} g · Suit O₂ {4:0} g · Field battery {5:0.00} kWh · Rover {6:0.0} kWh\nParts {7:0.0} · Seeds {8:0.0} · Nutrients {9:0.0} · Medical supplies {10:0.0}",
                State.resources.food, State.resources.cleanWaterLiters, State.resources.wasteWaterLiters,
                State.resources.oxygenGrams, State.resources.suitOxygenGrams, State.resources.fieldKwh, State.resources.roverKwh,
                State.resources.parts, State.resources.seeds, State.resources.nutrients, State.resources.medicine));
            TaskProgress();
            Heading("ASTRONAUT CARE");
            Paragraph("Use the galley/life-support area for food, water and suit refills; the quarters for rest/exercise; the medical bay for treatment. One staffed task runs at a time.", 20);
            RectTransform row = Row(content, 48);
            SmallButton(row, "Eat", () => mission.Execute("eat"));
            SmallButton(row, "Drink", () => mission.Execute("drink"));
            SmallButton(row, "Refill suit", () => mission.Execute("refill"));
            SmallButton(row, "Rest", () => mission.StartTask(TaskKind.Rest));
            SmallButton(row, "Exercise", () => mission.StartTask(TaskKind.Exercise));
            SmallButton(row, "Medical treatment", () => mission.StartTask(TaskKind.Treat));
            Heading("GREENHOUSE");
            LiveParagraph(() => "Greenhouse power: " + (State.greenhouseEnabled ? "ON" : "OFF") + " · Both crops need delivered power, water, nutrients and safe temperatures.", 20);
            Button(content, "Toggle greenhouse power", () => mission.Execute("greenhouse"));
            foreach (CropState crop in State.crops)
            {
                int bed = crop.bed;
                RectTransform card = Card();
                LiveLabel(card, () => string.Format(CultureInfo.InvariantCulture,
                    "Bed {0}  ·  {1}  ·  {2}  ·  stress {3:0}%", bed + 1, crop.crop,
                    crop.ready ? "READY TO HARVEST" : crop.planted ? "growth " + (crop.growth * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "empty", crop.stress * 100), 23);
                row = Row(card, 48);
                SmallButton(row, "Plant leafy crop", () => mission.StartTask(TaskKind.Plant, bed + ":leafy"));
                SmallButton(row, "Plant staple crop", () => mission.StartTask(TaskKind.Plant, bed + ":staple"));
                SmallButton(row, "Harvest", () => mission.StartTask(TaskKind.Harvest, bed.ToString(CultureInfo.InvariantCulture)));
            }
            Heading("MAINTENANCE AND CHARGING");
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "Base condition {0:0}% · Rover condition {1:0}% · Panel condition {2:0}% · Panel dust {3:0}%\nRover charging: {4} · Field battery charging: {5}",
                State.equipment.baseCondition * 100, State.equipment.roverCondition * 100, State.equipment.panelCondition * 100,
                State.equipment.panelDust * 100, State.chargeRover ? "requested" : "off", State.chargeField ? "requested" : "off"));
            row = Row(content, 48);
            SmallButton(row, "Repair base", () => mission.StartTask(TaskKind.RepairBase));
            SmallButton(row, "Repair rover", () => mission.StartTask(TaskKind.RepairRover));
            SmallButton(row, "Dust panels", () => mission.StartTask(TaskKind.DustPanels));
            SmallButton(row, "Toggle rover charge", () => mission.Execute("charge_rover"));
            SmallButton(row, "Toggle field charge", () => mission.Execute("charge_field"));
            Paragraph("Repairs require the relevant equipment, parts and powered tools. Field operations spend the portable or rover battery. Dusting removes deposits; it cannot clear airborne dust or generate electricity at night.", 20);
        }

        private void BuildResearch()
        {
            Heading("RESEARCH THAT CHANGES THE OUTPOST");
            LiveParagraph(() => "Available research: " + State.resources.researchPoints + " points   ·   Parts: " + State.resources.parts.ToString("0.0", CultureInfo.InvariantCulture));
            Paragraph("Research unlocks a design. Installing it at the energy/work area spends parts, time and electricity before its effect becomes active. Explore and analyze samples to earn points.", 20);
            string previousBranch = "";
            foreach (ResearchNode node in ResearchCatalog.Nodes)
            {
                ResearchNode captured = node;
                if (previousBranch != node.branch) { Heading(node.branch.ToUpperInvariant()); previousBranch = node.branch; }
                RectTransform card = Card();
                LiveLabel(card, () => captured.id + "  " + captured.title + "  ·  " + ResearchStatus(captured), 24);
                LabelInFlow(card, node.description, 21, paper);
                LabelInFlow(card, string.Format(CultureInfo.InvariantCulture,
                    "Unlock: {0} points · Install: {1:0} parts + {2} and power · Prerequisites: {3}",
                    node.points, node.parts, Duration(node.installationSeconds), node.prerequisites.Length == 0 ? "none" : string.Join(", ", node.prerequisites)), 19, muted);
                RectTransform row = Row(card, 48);
                Button unlock = SmallButton(row, "Research", () => mission.Research(captured.id));
                Button install = SmallButton(row, "Install", () => mission.StartTask(TaskKind.Install, captured.id));
                live.Add(() =>
                {
                    unlock.interactable = CanResearch(captured);
                    install.interactable = State.researched.Contains(captured.id) && !State.installed.Contains(captured.id) && State.task.kind == TaskKind.None;
                });
            }
            Heading("INSTALL ADDITIONAL EQUIPMENT");
            Paragraph("Array expansion unlocks extra panel slots. Battery modules unlock extra storage slots. Panels generate energy; storage only keeps energy already generated.", 20);
            RectTransform equipmentRow = Row(content, 48);
            SmallButton(equipmentRow, "Install solar panel", () => mission.StartTask(TaskKind.Install, "panel"));
            SmallButton(equipmentRow, "Install battery cabinet", () => mission.StartTask(TaskKind.Install, "battery"));
            TaskProgress();
        }

        private void BuildFieldLog()
        {
            Heading("PLAN THE RETURN JOURNEY");
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "Distance from base {0:0} m · Suit estimate {1:0.0} hours at reference use · Field energy {2:0.00} kWh · Rover {3:0.0} kWh",
                mission.Context.distanceFromBase, mission.Simulation.SuitHoursRemaining, State.resources.fieldKwh, State.resources.roverKwh));
            Paragraph("The map uses real terrain coordinates. Mission sites, markers and local relay coverage are authored gameplay. Select a site, budget a round trip, and keep a reserve for weather or delays.", 20);
            BuildMap();
            for (int i = 0; i < State.sites.Count; i += 3)
            {
                RectTransform row = Row(content, 48);
                for (int j = i; j < Math.Min(i + 3, State.sites.Count); j++)
                {
                    SiteState site = State.sites[j];
                    SmallButton(row, site.name + (site.analyzed ? " ✓" : ""), () => { mission.SelectSite(site.id); RebuildPanel(); });
                }
            }
            SiteState selected = State.sites.Find(s => s.id == mission.SelectedSiteId);
            if (selected != null)
            {
                Heading(selected.name.ToUpperInvariant());
                LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                    "{0:0.0000}°, {1:0.0000}° · {2:0} m from base · {3} research points for analysis\nSurvey {4} · Measurement {5} · Soil {6} · Analysis {7} · Marker {8} · Photo {9}",
                    selected.latitude, selected.longitude, selected.distanceMeters, selected.reward,
                    Done(selected.surveyed), Done(selected.measured), Done(selected.sampled), Done(selected.analyzed), Done(selected.markerPlaced), Done(selected.photographed)));
                double walkingMinutes = selected.distanceMeters * 2 / (3.4 / 3.6) / 60;
                Paragraph(string.Format(CultureInfo.InvariantCulture,
                    "Straight-line round trip: at least {0:0.0} walking minutes, before slope, detours, tasks and reserve. Longer routes are better suited to the rover. Actual terrain and conditions can increase the cost.", walkingMinutes), 20);
                RectTransform row = Row(content, 48);
                SmallButton(row, "Survey", () => mission.Execute("survey", selected.id));
                SmallButton(row, "Measure", () => mission.Execute("measure", selected.id));
                SmallButton(row, "Sample soil", () => mission.StartTask(TaskKind.SampleSoil, selected.id));
                SmallButton(row, "Place marker", () => mission.StartTask(TaskKind.PlaceMarker, selected.id));
                SmallButton(row, "Capture marker photo", () => mission.CapturePhoto(selected.id));
                SmallButton(row, "Analyze at lab", () => mission.StartTask(TaskKind.Analyze, selected.id));
                Paragraph("Stand beside the selected site to survey, measure, sample or place its marker. Face the marker and target before taking the photo. Return to the laboratory for sample analysis.", 20);
                if (!string.IsNullOrEmpty(selected.photoPath)) Paragraph("Photo evidence saved: " + System.IO.Path.GetFileName(selected.photoPath), 19);
            }
            else Paragraph("Select a site above to see its objectives and actions.");
            TaskProgress();
        }

        private void BuildMap()
        {
            RectTransform map = Box("Site map", content, WithAlpha(MarsPalette.Mauve, 0.22f));
            Height(map, 320);
            double minX = State.playerX, maxX = minX, minZ = State.playerZ, maxZ = minZ;
            MarsOutpost outpost = FindObjectOfType<MarsOutpost>();
            Vector3 home = outpost == null ? Vector3.zero : outpost.DoorPosition;
            minX = Math.Min(minX, home.x); maxX = Math.Max(maxX, home.x);
            minZ = Math.Min(minZ, home.z); maxZ = Math.Max(maxZ, home.z);
            foreach (SiteState site in State.sites)
            {
                minX = Math.Min(minX, site.x); maxX = Math.Max(maxX, site.x);
                minZ = Math.Min(minZ, site.z); maxZ = Math.Max(maxZ, site.z);
            }
            double spanX = Math.Max(200, maxX - minX), spanZ = Math.Max(200, maxZ - minZ);
            Func<double, double, Vector2> project = (x, z) => new Vector2((float)(0.08 + 0.84 * (x - minX) / spanX), (float)(0.12 + 0.76 * (z - minZ) / spanZ));
            Text legend = Label(map, "N ↑     ● You     ■ Habitat     Numbered sites: select to navigate", 18, muted);
            Anchor(legend.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -32), new Vector2(-12, -4));
            Text baseMarker = Label(map, "■ HOME", 19, paper);
            MapPosition(baseMarker.rectTransform, project(home.x, home.z), new Vector2(110, 26));
            for (int i = 0; i < State.sites.Count; i++)
            {
                SiteState site = State.sites[i];
                Button point = Button(map, (i + 1).ToString(CultureInfo.InvariantCulture), () => { mission.SelectSite(site.id); RebuildPanel(); });
                MapPosition((RectTransform)point.transform, project(site.x, site.z), new Vector2(38, 34));
                point.GetComponent<Image>().color = site.id == mission.SelectedSiteId ? accent : MarsPalette.Mauve;
            }
            Text player = Label(map, "●", 27, accent);
            live.Add(() => MapPosition(player.rectTransform, project(State.playerX, State.playerZ), new Vector2(26, 26)));
        }

        private void BuildRadio()
        {
            drawnReports = State.reports.Count;
            Heading("HOUSTON, HERE IS OUR UPDATE");
            Paragraph("Houston is simulated mission control. Reports contain status, repairs, samples and marker photographs. The game stays offline; no real message is sent to NASA.", 20);
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "Radio: {0}   ·   Signal: {1:0}%   ·   Next usable window: {2}\nField radio uses local battery power; the base radio uses habitat power. Weak signal extends transmission time and energy use.",
                State.radioMode, mission.Environment.signal * 100,
                mission.Environment.signal > 0.1 ? "available now" : Duration(mission.Environment.nextWindowSeconds)));
            RectTransform row = Row(content, 48);
            SmallButton(row, "Off", () => mission.Execute("radio", "off"));
            SmallButton(row, "Listen", () => mission.Execute("radio", "listen"));
            SmallButton(row, "Transmit", () => mission.Execute("radio", "transmit"));
            SmallButton(row, "Queue status report", () => { mission.Execute("report"); RebuildPanel(); });
            Paragraph("No link? Move to better coverage or wait for a window. Reports remain queued instead of being marked delivered. Adjust power to leave enough capacity for the radio.", 20);
            Heading("REPORT QUEUE");
            if (State.reports.Count == 0) Paragraph("No reports queued yet. Complete fieldwork or maintenance, then create an update.");
            foreach (ReportState report in State.reports)
            {
                ReportState captured = report;
                RectTransform card = Card();
                LiveLabel(card, () => captured.id + "  ·  " + (captured.acknowledged ? "ACKNOWLEDGED" : "QUEUED / TRANSMITTING") +
                    "  ·  Progress " + Duration(captured.progressSeconds), 22);
                LabelInFlow(card, report.summary, 20, paper);
            }
        }

        private void BuildLearning()
        {
            Heading("LEARN BY RUNNING THE OUTPOST");
            LiveParagraph(() => State.tutorialStep < 0 ? "Guided objectives skipped. Learning activities remain available." : "Guided objective " + Math.Min(10, State.tutorialStep + 1) + "/10: " + TutorialObjective(State));
            RectTransform tutorialControls = Row(content, 48);
            SmallButton(tutorialControls, "Replay guided objectives", () => State.tutorialStep = 0);
            SmallButton(tutorialControls, "Skip guided objectives", () => State.tutorialStep = -1);
            Paragraph("Predict what will happen, make one change, observe the result, and explain why. You can replay these activities without losing points for a wrong prediction.");
            LearningCard("1. Electricity and food are different", "Predict whether a meal will recharge the habitat battery. Eat when hungry and compare satiety with stored kWh. Food supports the astronaut; solar panels supply electrical energy.",
                "NASA plants in space", "https://www.nasa.gov/exploration-research-and-technology/growing-plants-in-space/");
            LearningCard("2. Oxygen and water share resources", "Lower Life control briefly while reserves are safe. Watch delivered power and oxygen/water processing. Restore enough power before reserves become dangerous. Recycling recovers part of an input; it cannot create water from nothing.",
                "NASA ECLSS", "https://www.nasa.gov/reference/environmental-control-and-life-support-systems-eclss/");
            LearningCard("3. Crops need a controlled environment", "Plant a crop and predict how a shortage changes growth. Compare water, temperature, stress and delivered greenhouse power. Crop durations and yields here are tuned game values; the source explains controlled growth requirements.",
                "NASA controlled growing systems", "https://www.nasa.gov/exploration-research-and-technology/growing-plants-in-space/");
            LearningCard("4. Shelter changes future exposure", "Compare the dose-rate readout outside and inside shelter during a warning. Protection reduces future accumulation; it does not erase the dose already received. The powered shielding curve and HP penalties are teaching abstractions.",
                "NASA Curiosity radiation measurements", "https://science.nasa.gov/resource/radiation-measurements-on-mars/");
            LearningCard("5. Dust changes the energy budget", "Predict whether a battery can last through a dusty period. Compare generation before and after cleaning deposited dust. Cleaning does not remove airborne dust, and a clean panel still produces no power at night.",
                "NASA Mars surface power study", "https://www.nasa.gov/wp-content/uploads/2024/01/mars-surface-power-generation-challenges-and-considerations.pdf");
            LearningCard("6. Rest and exercise are investments", "Compare task efficiency when tired and after rest. Exercise costs supplies and briefly adds fatigue while supporting longer-term fitness. It is not an instant cure for oxygen shortage or injury.",
                "NASA exercise overview", "https://www.nasa.gov/ochmo-tb-031-exercise-overview-2/");
            Heading("WHERE THESE NUMBERS COME FROM");
            LiveParagraph(() => "Temperature: " + (mission.Environment.temperatureSource ?? "Loading") +
                "\nRadiation: " + (mission.Environment.radiationSource ?? "Loading") +
                "\nEvents: " + (mission.Environment.eventSource ?? "Loading"), 21);
            Paragraph("Terrain comes from the bundled USGS MOLA–HRSC elevation product, sampled on a 200 m grid. Smaller rocks, science caches, radio coverage and mission markers are gameplay objects. The data panel [F3] preserves terrain source and scale information. Absorbed radiation dose is shown in microgray; it is not a medical diagnosis.", 20);
            Button(content, "NASA Mars Trek", () => Application.OpenURL("https://trek.nasa.gov/mars/"));
        }

        private void LearningCard(string title, string body, string sourceName, string url)
        {
            RectTransform card = Card();
            LabelInFlow(card, title, 25, accent);
            LabelInFlow(card, body, 21, paper);
            Button(card, sourceName + " ↗", () => Application.OpenURL(url));
        }

        private void BuildSettings()
        {
            Heading("CONTROLS AND ACCESSIBILITY");
            Paragraph("W A S D: move · Mouse: look · Shift: sprint · Space: jump / rover brake · E: board or leave rover\nTab: console · P: pause · Escape: close/open console · F5: save · F9: load · F3: terrain sources\nTab/Shift+Tab and arrow keys navigate controls; Enter/Space activates selected buttons. Use left/right arrows on a selected power slider.", 22);
            Button(content, "Save mission", () => mission.SaveMission());
            Button(content, "Load mission", () => mission.LoadMission());
            Button(content, "Restart this mission", () => mission.Restart());
            LiveParagraph(() => "Reduced weather motion: " + (mission.ReducedMotion ? "ON" : "OFF"), 20);
            Button(content, "Toggle reduced weather motion", () => mission.ReducedMotion = !mission.ReducedMotion);
            Paragraph("Interface scale", 21);
            Slider scale = Slider(content, (uiScale - 0.8f) / 0.4f, value =>
            {
                uiScale = Mathf.Lerp(0.8f, 1.2f, value);
                scaler.referenceResolution = new Vector2(1920 / uiScale, 1080 / uiScale);
            });
            Heading("THREE-KNOB DASHBOARD");
            Paragraph("Arduino Uno over USB serial (115200 baud, COM7 when present). Left knob: Life control. Middle: Temperature. Right: Radiation shielding. Turn a knob counter-clockwise for more power, clockwise for less. " +
                      "Each LED group is green when the power put in meets the useful need, yellow when 1-15% short, red when more than 15% short; the buzzer sounds on red and on warnings. " +
                      "The needle shows total stored battery energy: clockwise as it rises, counter-clockwise as it falls. Close the Arduino Serial Monitor before playing.", 20);
            LiveParagraph(() => "Serial link: " + (mission.Arduino == null ? "not created" : mission.Arduino.Status), 21);
            LiveParagraph(() => mission.Dashboard == null ? "Software controls active." : mission.Dashboard.Status, 21);
            if (mission.Dashboard != null)
            {
                Button(content, "Start software input emulator", () => { mission.Dashboard.BeginEmulator(); RebuildPanel(); });
                Button(content, "Stop dashboard / emulator", () => { mission.Dashboard.Disconnect(); RebuildPanel(); });
                if (mission.Dashboard.EmulatorActive)
                {
                    Paragraph("These emulate physical positions. Move each to its saved setting to take control; the same crossing rule applies after a real reconnect.", 20);
                    double[] positions = { State.knobs[0], State.knobs[1], State.knobs[2] };
                    for (int i = 0; i < 3; i++)
                    {
                        int channel = i;
                        Paragraph(((PowerChannel)i).ToString(), 21);
                        Slider(content, (float)positions[i], value =>
                        {
                            positions[channel] = value;
                            mission.Dashboard.FeedNormalized(positions[0], positions[1], positions[2]);
                        });
                    }
                    live.Add(() => mission.Dashboard.FeedNormalized(positions[0], positions[1], positions[2]));
                    LiveParagraph(() => "Waiting for takeover: " +
                        (mission.Dashboard.AwaitingTakeover[0] ? "Life control " : "") +
                        (mission.Dashboard.AwaitingTakeover[1] ? "Temperature " : "") +
                        (mission.Dashboard.AwaitingTakeover[2] ? "Radiation shielding" : ""), 20);
                }
            }
            Paragraph("Hardware input and software controls share the same power requests. Disconnecting preserves the last settings and stops fast-forward. On-screen controls remain available.", 20);
        }

        private void BuildDebrief()
        {
            bool won = State.phase == MissionPhase.Won;
            Heading(won ? "MISSION COMPLETE" : "MISSION ENDED");
            Paragraph(won ? "Your outpost supported the astronaut through the selected mission." : "Review what happened, then load a save or try another approach.");
            Paragraph(State.endingReason ?? "");
            Paragraph(string.Format(CultureInfo.InvariantCulture,
                "Survived {0:0.00} sols of {1:0.00}\nSolar energy generated: {2:0.0} kWh · Energy consumed: {3:0.0} kWh\nResearch installed: {4}/16 · Analyzed sites: {5} · Marker photos: {6}\nRepairs completed: {7} · Panel cleanings: {8} · Houston acknowledgments: {9}",
                State.elapsedSeconds / MissionSettings.SolSeconds, MissionSettings.Duration(State.length) / MissionSettings.SolSeconds,
                State.totalGeneratedKwh, State.totalConsumedKwh, State.installed.Count, State.sites.FindAll(s => s.analyzed).Count,
                State.sites.FindAll(s => s.photographed).Count, State.repairsCompleted, State.panelsCleaned, State.reports.FindAll(r => r.acknowledged).Count));
            Heading("DECISIONS AND CONSEQUENCES");
            int from = Math.Max(0, State.history.Count - 14);
            for (int i = from; i < State.history.Count; i++) Paragraph(State.history[i], 20);
            RectTransform row = Row(content, 48);
            SmallButton(row, "Retry this mission", () => mission.Restart());
            SmallButton(row, "Load saved mission", () => mission.LoadMission());
            SmallButton(row, "Start one month", () => mission.NewMission(MissionLength.Month));
            SmallButton(row, "Start half-year", () => mission.NewMission(MissionLength.HalfYear));
            SmallButton(row, "Start full year", () => mission.NewMission(MissionLength.Year));
        }

        private void TaskProgress()
        {
            LiveParagraph(() => State.task.kind == TaskKind.None ? "No staffed task in progress." : string.Format(CultureInfo.InvariantCulture,
                "{0} · {1:0}% · {2} remaining · {3:0.00} kW requested · {4:0.000} kWh spent",
                State.task.kind, State.task.durationSeconds > 0 ? State.task.progressSeconds / State.task.durationSeconds * 100 : 0,
                Duration(Math.Max(0, State.task.durationSeconds - State.task.progressSeconds)), State.task.powerKw, State.task.spentKwh), 21);
            Button cancel = Button(content, "Cancel current task", () => mission.Execute("cancel"));
            live.Add(() => cancel.interactable = State.task.kind != TaskKind.None);
        }

        private string ResearchStatus(ResearchNode node)
        {
            if (State.installed.Contains(node.id)) return "INSTALLED";
            if (State.researched.Contains(node.id)) return "RESEARCHED — INSTALL TO USE";
            foreach (string id in node.prerequisites) if (!State.researched.Contains(id)) return "LOCKED";
            return CanResearch(node) ? "AVAILABLE" : "NEEDS POINTS";
        }

        private bool CanResearch(ResearchNode node)
        {
            if (State.researched.Contains(node.id) || State.resources.researchPoints < node.points) return false;
            foreach (string id in node.prerequisites) if (!State.researched.Contains(id)) return false;
            return true;
        }

        private string Endurance()
        {
            double deficit = -mission.Simulation.Power.netKw;
            return deficit > 0.01 ? string.Format(CultureInfo.InvariantCulture, "~{0:0.0} h at this net draw", State.resources.batteryKwh / deficit) : "Currently charging / balanced";
        }

        /// <summary>
        /// The single most urgent thing to do right now to stay alive and keep the day on track,
        /// with where to go, which tool to carry and which button to press.
        /// </summary>
        private string SurvivalGuide(MissionState s)
        {
            MissionSimulation sim = mission.Simulation;
            PlayerContext ctx = mission.Context;
            EnvironmentSample env = mission.Environment;
            bool outside = ctx.zone == PlayerZone.Outdoors;
            if (s.task.kind != TaskKind.None)
                return "Working: " + s.task.kind + ". Stand still until it finishes; inside, Tab > + 1 hour waits faster.";
            if (outside && s.resources.suitOxygenGrams < 300)
                return "Suit oxygen is low. Walk back to the airlock now (follow the ramp to the white door).";
            if (env.solarEvent > 0)
                return "Solar storm! Go to the storm shelter (far west end of the base) and wait there with Tab > + 1 hour.";
            if (outside && env.dust > 0.3)
                return "Dust storm. Return inside; dust the panels when it clears.";
            for (int i = 0; i < 3; i++)
                if (sim.Power.neededKw[i] > 0 && sim.Power.deliveredKw[i] + 0.01 < sim.Power.neededKw[i])
                    return "Not enough power for " + (i == 0 ? "life control" : i == 1 ? "temperature" : "radiation shielding") +
                           ". Tab > Overview: raise that slider until Delivered matches Useful need.";
            if (s.resources.batteryKwh < sim.BatteryCapacity * 0.15 && sim.Power.netKw < 0)
                return "Battery low. Lower sliders showing EXTRA DRAW, switch off rover/field charging, and wait for daylight.";
            if (s.astronaut.hydration < 45) return outside ? "You are thirsty. Head inside, then Tab > Base tasks > Drink." : "You are thirsty. Tab > Base tasks > Drink.";
            if (s.astronaut.satiety < 45) return outside ? "You are hungry. Head inside, then Tab > Base tasks > Eat." : "You are hungry. Tab > Base tasks > Eat.";
            if (s.astronaut.health < 70)
                return "Health is low. Carry the Radiation Dosimeter to the medical bay, then Tab > Base tasks > Medical treatment.";
            if (s.astronaut.fatigue > 70) return "You are exhausted. Inside, Tab > Base tasks > Rest (6 h).";
            if (outside && s.resources.suitOxygenGrams < 500)
                return "Suit oxygen under half. Finish up and plan your walk back.";
            foreach (CropState crop in s.crops)
                if (crop.ready) return "A crop is ready. Carry the Hydroponic Sprayer to the greenhouse, then Tab > Base tasks > Harvest.";
            foreach (CropState crop in s.crops)
                if (!crop.planted && s.resources.seeds >= 1)
                    return "Bed " + (crop.bed + 1) + " is empty. Pick up the Regolith Shovel and Hydroponic Sprayer in the greenhouse, then Tab > Base tasks > Plant.";
            if (s.equipment.baseCondition < 0.8)
                return "The base is wearing out. Take the Field Wrench and Energy Meter (energy room), then Tab > Base tasks > Repair base.";
            if (s.equipment.panelDust > 0.15)
                return "Solar panels are dusty. Take the Solar Panel Duster (energy room) out to the array, then Tab > Base tasks > Dust panels.";
            if (env.solarFraction <= 0.01)
                return outside ? "It is night: cold and no solar power. Go back inside." : "Night: no solar power. Keep the sliders lean and pass the night with Tab > + 1 hour (daily routine ON).";
            if (s.astronaut.fatigue > 50) return "Getting tired. Plan a Rest inside before the next trip.";
            return NextObjective(s);
        }

        private static string NextObjective(MissionState state)
        {
            if (state.tutorialStep >= 0 && state.tutorialStep < 10) return TutorialObjective(state);
            if (!state.crops.Exists(c => c.planted || c.harvests > 0)) return "Plant both greenhouse beds, then use the daily routine and safe advancement.";
            if (state.totalFoodHarvested == 0) return "Keep crops powered and watered; harvest when ready.";
            if (!state.sites.Exists(s => s.sampled)) return "Refill your suit and field battery, then sample a nearby site.";
            if (!state.sites.Exists(s => s.analyzed)) return "Return to the laboratory and analyze your soil sample.";
            if (state.researched.Count == 0) return "Spend research points on your first improvement.";
            if (state.installed.Count == 0) return "Install the researched improvement at the energy/work area.";
            if (!state.sites.Exists(s => s.photographed)) return "Place a survey marker and photograph it with its target.";
            if (!state.reports.Exists(r => r.acknowledged)) return "Queue an update for Houston and transmit during a usable radio window.";
            return "Maintain equipment, watch the forecast, and keep enough energy for every system.";
        }
        private static string TutorialObjective(MissionState state)
        {
            string[] objectives = {
                "Use the three sliders to meet useful power need. Compare extra draw before advancing.",
                "Eat, drink and refill as needed. Walk to the greenhouse and plant a bed.",
                "Enable the daily routine and advance safely. Harvest when a crop is ready.",
                "Check suit/field reserves, select a nearby site and sample soil.",
                "Return to the laboratory and analyze the sample.",
                "Spend your research points on a useful design.",
                "Install the researched design in the energy room; compare its actual benefit.",
                "Place a survey marker, frame it with the camera, and capture a photo.",
                "Queue an update for Houston, then transmit during a usable radio window.",
                "Repair worn equipment and dust dirty panels. Watch the forecast and rebalance before storms."
            };
            return state.tutorialStep >= objectives.Length ? "Guided objectives complete. Continue maintaining the outpost and reacting to weather." : objectives[Math.Max(0, state.tutorialStep)];
        }

        private void OpenHelp()
        {
            if (mission == null) return;
            tab = HelpTab;
            mission.SetPanelOpen(true);
            RebuildPanel();
        }

        /// <summary>How to complete each daily task: where to go, which tools to carry, which button to press.</summary>
        private void BuildHelp()
        {
            Heading("HOW TO COMPLETE YOUR DAILY TASKS");
            Paragraph("Every task follows the same three steps:", 22);
            Paragraph("1.  Pick up the tools.  Walk up to a tool in the base, look at it and press E. You can carry many at once; press G inside the base to put them all back.\n" +
                      "2.  Go to the right room.  Tasks only start at their station (the bottom bar names the room you are in). Stand still.\n" +
                      "3.  Press the task button.  Open the console (Tab), choose the task and wait. Moving pauses the task; it resumes when you stand still. Inside, + 1 hour waits faster.", 21);
            if (mission != null && mission.Tools != null && mission.Simulation != null)
                LiveParagraph(() => "You are carrying: " + mission.Tools.CarriedSummary(), 21);
            if (mission != null && mission.Simulation == null)
            {
                Paragraph("First choose a mission length.", 21);
                Button(content, "Choose a mission", () => { tab = 0; RebuildPanel(); });
            }

            Heading("EVERY SOL");
            HelpCard("Balance power", "Overview tab", "none",
                "Move the three sliders until each channel's Delivered matches its Useful need. Shortfalls harm the astronaut; extra draw wastes battery.");
            HelpCard("Eat and drink", "Anywhere inside (the galley is in the command hub)", "none",
                "Base tasks > Eat, Drink. Keep Food need and Hydration up. The daily routine does this for you while time advances.");
            HelpCard("Rest and exercise", "Anywhere inside (crew quarters has the bunks)", "none",
                "Base tasks > Rest (6 h) lowers fatigue; Exercise (30 min) keeps fitness up.");
            HelpCard("Medical treatment", "Medical bay", "Radiation Dosimeter (medical bay)",
                "Base tasks > Medical treatment when health drops. Uses one medical supply.");

            Heading("GREENHOUSE");
            HelpCard("Plant a bed", "Greenhouse", "Regolith Shovel + Hydroponic Sprayer (greenhouse)",
                "Base tasks > Plant leafy (fast) or staple (filling) crop on an empty bed. Keep greenhouse power ON.");
            HelpCard("Harvest", "Greenhouse", "Hydroponic Sprayer (greenhouse)",
                "When a bed says READY TO HARVEST, press Harvest. Food storage must have room.");

            Heading("MAINTENANCE");
            HelpCard("Repair the base", "Inside the habitat", "Field Wrench + Energy Meter (energy room)",
                "Base tasks > Repair base when base condition falls. Uses 2 parts.");
            HelpCard("Dust the solar panels", "Solar array beside the energy room (outside)", "Solar Panel Duster (energy room)",
                "Base tasks > Dust panels when panel dust builds up. Go out through the airlock.");
            HelpCard("Repair the rover", "Beside the rover", "Field Wrench (energy room)",
                "Base tasks > Repair rover. Uses 2 parts.");
            HelpCard("Install research and equipment", "Energy room", "Field Wrench + Energy Meter (energy room)",
                "Research tab > Install, after a design is researched.");

            Heading("FIELD SCIENCE (EVA)");
            HelpCard("Get ready", "Anywhere inside, then the lab", "Portable O2 Canister (lab, life support or medical bay)",
                "Base tasks > Refill suit before leaving. Then walk through the lab door into the airlock: the inner door closes before the outer one opens.");
            HelpCard("Find a site", "Outside", "none",
                "Field log > select a site and walk or drive toward it. Its marker pops up when you are within about 30 m.");
            HelpCard("Survey and measure", "Within 5 m of the site", "Rock Hammer + Radiation Dosimeter (lab, hub)",
                "Field log > Survey, then Measure.");
            HelpCard("Sample soil", "Within 5 m of the site", "Sample Scoop + Sample Canister Rack (lab)",
                "Field log > Sample soil.");
            HelpCard("Mark and photograph", "Within 5 m of the site", "Survey Marker + Survey Camera (lab)",
                "Field log > Place marker, face the marker, then Capture marker photo.");
            HelpCard("Analyze the sample", "EVA prep and sample lab", "Core Sample Drill (lab)",
                "Back inside, Field log > Analyze at lab. Earns research points.");

            Heading("COMMUNICATIONS");
            HelpCard("Report to Houston", "Anywhere", "Comm Radio (hub, shelter or lab)",
                "Radio tab > Queue status report, then Transmit during a usable radio window.");
            Paragraph("Carrying the Habitat Work Lamp (quarters or shelter) lights your way at night.", 20);
        }

        private void HelpCard(string title, string where, string tools, string how)
        {
            RectTransform card = Card();
            LabelInFlow(card, title, 25, accent);
            LabelInFlow(card, "Where: " + where + "   ·   Tools: " + tools, 19, muted);
            LabelInFlow(card, how, 20, paper);
        }

        private void TogglePanel()
        {
            if (mission == null) return;
            if (!mission.IsRunning) { mission.SetPanelOpen(true); return; }
            mission.SetPanelOpen(!mission.PanelOpen);
        }

        private MissionState State { get { return mission.Simulation.State; } }
        private static string Done(bool value) { return value ? "done" : "pending"; }
        private static string LengthName(MissionLength length) { return length == MissionLength.Month ? "One Martian month" : length == MissionLength.HalfYear ? "Half a Martian year" : "One Martian year"; }
        private static string Duration(double seconds)
        {
            if (seconds <= 0) return "0 s";
            if (seconds >= MissionSettings.SolSeconds) return (seconds / MissionSettings.SolSeconds).ToString("0.0", CultureInfo.InvariantCulture) + " sols";
            if (seconds >= 3600) return (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h";
            if (seconds >= 60) return (seconds / 60).ToString("0.0", CultureInfo.InvariantCulture) + " min";
            return seconds.ToString("0", CultureInfo.InvariantCulture) + " s";
        }

        private RectTransform Card()
        {
            RectTransform card = Box("Card", content, WithAlpha(MarsPalette.Mauve, 0.19f));
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return card;
        }

        private void Heading(string value) { LabelInFlow(content, value, 27, accent); }
        private void Paragraph(string value, int size = 23) { LabelInFlow(content, value, size, paper); }
        private void LiveParagraph(Func<string> value, int size = 23) { LiveLabel(content, value, size); }
        private void LiveLabel(Transform parent, Func<string> value, int size)
        {
            Text label = LabelInFlow(parent, "", size, paper);
            live.Add(() => label.text = value());
        }

        private Text LabelInFlow(Transform parent, string value, int size, Color color)
        {
            Text label = Label(parent, value, size, color);
            label.gameObject.AddComponent<LayoutElement>().minHeight = size + 8;
            return label;
        }

        private Text Label(Transform parent, string value, int size, Color color)
        {
            Text label = NewRect("Text", parent).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.raycastTarget = false;
            return label;
        }

        private Button Button(Transform parent, string text, UnityAction action)
        {
            RectTransform rect = Box(text, parent, WithAlpha(MarsPalette.Mauve, 0.5f));
            Height(rect, 46);
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.25f, 1.15f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = MarsPalette.WarmOrange;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(action);
            Text label = Label(rect, text, 20, paper);
            label.alignment = TextAnchor.MiddleCenter;
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
            controls.Add(button);
            return button;
        }

        private Button SmallButton(Transform parent, string text, UnityAction action)
        {
            Button button = Button(parent, text, action);
            button.GetComponent<LayoutElement>().flexibleWidth = 1;
            return button;
        }

        private Slider Slider(Transform parent, float initial, UnityAction<float> changed)
        {
            RectTransform rect = NewRect("Power slider", parent);
            Height(rect, 34);
            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.wholeNumbers = false;
            RectTransform track = Box("Track", rect, WithAlpha(MarsPalette.Mauve, 0.6f));
            Anchor(track, Vector2.zero, Vector2.one, new Vector2(12, 11), new Vector2(-12, -11));
            RectTransform fillArea = NewRect("Fill area", rect);
            Anchor(fillArea, Vector2.zero, Vector2.one, new Vector2(12, 11), new Vector2(-12, -11));
            RectTransform fill = Box("Fill", fillArea, accent);
            Stretch(fill);
            RectTransform handleArea = NewRect("Handle area", rect);
            Anchor(handleArea, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0));
            RectTransform handle = Box("Handle", handleArea, paper);
            handle.sizeDelta = new Vector2(24, 32);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            slider.SetValueWithoutNotify(initial);
            slider.onValueChanged.AddListener(changed);
            controls.Add(slider);
            return slider;
        }

        private RectTransform Row(Transform parent, float height)
        {
            RectTransform row = NewRect("Actions", parent);
            Height(row, height);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return row;
        }

        private static void Height(RectTransform rect, float value)
        {
            LayoutElement layout = rect.GetComponent<LayoutElement>();
            if (layout == null) layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = value;
            layout.minHeight = value;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Box(string name, Transform parent, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        private static void Anchor(RectTransform rect, Vector2 minimum, Vector2 maximum, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = minimum;
            rect.anchorMax = maximum;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(RectTransform rect) { Anchor(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); }
        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        private static void MapPosition(RectTransform rect, Vector2 point, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = point;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }
    }
}
