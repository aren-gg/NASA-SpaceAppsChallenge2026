using System;
using System.Collections.Generic;
using System.Globalization;
using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Hardware;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using MarsSurvival.World;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ControlGuide = MarsTrainer.UI.ControlGuide;
using EndScreen = MarsTrainer.UI.EndScreen;
using FacetedImage = MarsTrainer.UI.FacetedImage;
using PauseMenu = MarsTrainer.UI.PauseMenu;
using ResourceGlyph = MarsTrainer.UI.ResourceGlyph;
using ResourceSymbol = MarsTrainer.UI.ResourceSymbol;
using StatBar = MarsTrainer.UI.StatBar;
using UiTypeface = MarsTrainer.UI.UiTypeface;
using SimClock = MarsTrainer.Sim.SimClock;
using Ui = MarsTrainer.Sim.GameConstants;

namespace MarsSurvival.UI
{
    /// <summary>
    /// The mission HUD and outpost console, drawn with the MarsTrainer UI kit in the Barlow Condensed
    /// typeface (VT323 for readouts). The HUD stays minimal: a small sol / battery / radiation block top left, quiet key
    /// hints top right, one warning at a time, suit vitals (oxygen first) that brighten only when
    /// low, small eat/drink buttons and one fading message line. The console (Tab) holds the three
    /// power knobs, crew care, greenhouse, equipment, research, field sites, radio and learning as
    /// live displays that each open a window; Escape opens the pause menu and a report closes each mission.
    /// </summary>
    public sealed class MarsMissionHud : MonoBehaviour
    {
        // Console views: a home page of live system tiles; each tile opens its own window.
        private const int Home = 0, PowerView = 1, CrewView = 2, GreenhouseView = 3, EquipmentView = 4, ResearchView = 5,
            FieldView = 6, RadioView = 7, LearningView = 8, HelpTab = 9, SettingsView = 10;
        private static readonly string[] ViewNames = { "Outpost console", "Power", "Crew", "Greenhouse", "Equipment", "Research",
            "Field sites", "Radio", "Learning and data", "How to play", "Settings" };
        private const float NoticeSeconds = 6f;
        private readonly List<Action> live = new List<Action>();
        private readonly List<Selectable> controls = new List<Selectable>();
        private readonly List<string> warningQueue = new List<string>();
        private readonly HashSet<string> dismissed = new HashSet<string>();
        private MarsMissionController mission;
        private MissionState drawnState;
        private Canvas canvas;
        private CanvasScaler scaler;
        private TMP_FontAsset font;
        private RectTransform panel, content, gameplay, alertPlate;
        private TMP_Text consoleTitle;
        private Button backButton;
        private string detailId;
        private TMP_Text solText, navigation, notice, guide, batteryText, radiationText, alertText;
        private Image batteryFill, radiationFill, noticePlate;
        private TMP_Text[] vitalLabels;
        private Image[] vitalFills;
        private CanvasGroup[] vitalGroups;
        private TMP_Text[] vitalValues;
        private PauseMenu pause;
        private EndScreen end;
        // Daily plan (top right) and the morning summary card.
        private RectTransform planPlate, summaryPlate;
        private TMP_Text planTitle, planDetail, summaryTitle, summaryBody;
        private TMP_Text[] planRows;
        private MarsMiniMap map;
        // Mission briefing shown before the first mission instead of the console.
        private RectTransform briefing;
        private ScrollRect scroll;
        private int tab, drawnPhase = -1, drawnReports = -1;
        private bool built, lastOpen, suppressSliders, reportShown, pausedByMenu;
        private float nextRefresh, uiScale = 1, noticeChangedAt = -100f;
        private string lastNotice = "";
        private Color ink, paper, accent, muted;

        /// <summary>True while the pause menu or mission report covers the screen.</summary>
        public bool ScreenOpen { get { return (pause != null && pause.gameObject.activeSelf) || (end != null && end.gameObject.activeSelf); } }

        private void Start()
        {
            BuildShell();
        }

        private void Update()
        {
            if (!built) return;
            mission = MarsMissionController.Instance;
            bool visible = mission != null && mission.Ready && !MarsHomeScreen.IsOpen && !MarsWardrobe.IsOpen;
            canvas.enabled = visible;
            if (!visible) return;

            HandleKeys();
            MissionState state = mission.Simulation == null ? null : mission.Simulation.State;
            bool brief = state == null && !mission.PanelOpen && !ScreenOpen;
            if (briefing.gameObject.activeSelf != brief)
            {
                briefing.gameObject.SetActive(brief);
                if (brief) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
            int phase = state == null ? -1 : (int)state.phase;
            if (state != drawnState || phase != drawnPhase)
            {
                if (state != drawnState) { warningQueue.Clear(); dismissed.Clear(); reportShown = false; }
                drawnState = state;
                drawnPhase = phase;
                if (state != null && state.phase != MissionPhase.Running && !reportShown) ShowReport();
                else if (state == null || state.phase != MissionPhase.Running) mission.SetPanelOpen(true);
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
            // The console replaces the walking HUD while it is open, so nothing shows through its glass.
            gameplay.gameObject.SetActive(state != null && !mission.PanelOpen && !ScreenOpen);
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Ui.UiRefreshSeconds;
                RefreshShell();
                if (mission.PanelOpen)
                {
                    if (state != null && tab == RadioView && detailId == null && drawnReports != state.reports.Count) RebuildPanel();
                    foreach (Action update in live) update();
                }
            }
            FadeNotice();
        }

        private void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
            if (typing) return;
            if (Input.GetKeyDown(KeyCode.Escape) && map != null && map.Expanded) { map.SetExpanded(false); return; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (pause.gameObject.activeSelf) pause.Close();
                else if (end.gameObject.activeSelf) { }
                else if (mission.PanelOpen && (tab != Home || detailId != null)) Back();
                else if (mission.PanelOpen && mission.IsRunning) mission.SetPanelOpen(false);
                else if (mission.IsRunning) OpenPause();
                else mission.SetPanelOpen(true);
                return;
            }
            if (ScreenOpen) return;
            if (Input.GetKeyDown(KeyCode.M) && mission.IsRunning && !mission.PanelOpen) map.Toggle();
            if (Input.GetKeyDown(KeyCode.Tab) && map.Expanded) map.SetExpanded(false);
            if (Input.GetKeyDown(KeyCode.Tab)) TogglePanel();
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (mission.PanelOpen && tab == HelpTab && mission.IsRunning) mission.SetPanelOpen(false);
                else OpenHelp();
            }
            if (Input.GetKeyDown(KeyCode.P) && mission.IsRunning) mission.SetPaused(!mission.Paused);
            if (summaryPlate != null && summaryPlate.gameObject.activeSelf && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)))
                CloseSummary();
            // X stops the task in progress (it would otherwise wait for you to come back and stand still).
            if (Input.GetKeyDown(KeyCode.X) && mission.IsRunning && mission.Simulation.State.task.kind != TaskKind.None) mission.Execute("cancel");
            if (Input.GetKeyDown(KeyCode.F5)) mission.SaveMission();
            if (Input.GetKeyDown(KeyCode.F9)) mission.LoadMission();
#endif
        }

        // ------------------------------------------------------------------ shell

        // Minimal HUD: only what matters now, small and quiet until it needs attention.
        private const float Small = 18f, Tiny = 16f;
        // HUD plates (sol block, vitals) and the small key buttons share one backing so they read as one set.
        private const float ChipAlpha = 0.6f;

        private void BuildShell()
        {
            ink = StatBar.ColorOf(Ui.UiSurface);
            paper = StatBar.ColorOf(Ui.UiText);
            accent = StatBar.ColorOf(Ui.UiCopper);
            muted = Color.Lerp(paper, ink, 0.35f);
            font = UiTypeface.Font;
            GameObject root = new GameObject("Mission Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Ui.UiWidth, Ui.UiHeight);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("Mission Event System", typeof(EventSystem), typeof(StandaloneInputModule));

            RectTransform safe = StatBar.Frame(root.transform, "Safe area", 0, 0, 1, 1);
            gameplay = StatBar.Frame(safe, "Mission HUD", 0, 0, 1, 1);

            // Top left: sol counter, with battery and radiation right under it.
            RectTransform status = Plate(gameplay, "Mission status", .010f, .855f, .175f, .985f);
            solText = Text(status, "Sol 1.0 / 56", 21, paper, .07f, .68f, .95f, .97f);
            batteryText = Text(status, "Battery", Tiny, muted, .07f, .42f, .95f, .66f);
            batteryFill = Bar(status, .07f, .37f, .93f, .41f, StatBar.ColorOf(Ui.UiPower));
            radiationText = Text(status, "Radiation", Tiny, muted, .07f, .12f, .95f, .36f);
            radiationFill = Bar(status, .07f, .07f, .93f, .11f, StatBar.ColorOf(Ui.UiRadiation));

            // Top centre: where you are, as quiet text.
            RectTransform where = Plate(gameplay, "Location", .36f, .958f, .64f, .99f);
            where.GetComponent<Image>().color = WithAlpha(ink, 0.45f);
            navigation = Text(where, "", Tiny, Color.Lerp(paper, ink, 0.12f), .02f, 0, .98f, 1);
            navigation.alignment = TextAlignmentOptions.Center;

            // Top right: key hints that also work as small buttons.
            RectTransform keys = StatBar.Frame(gameplay, "Screen keys", .80f, .955f, .99f, .99f);
            HorizontalLayoutGroup keyRow = keys.gameObject.AddComponent<HorizontalLayoutGroup>();
            keyRow.spacing = 6; keyRow.childAlignment = TextAnchor.MiddleRight;
            keyRow.childControlHeight = keyRow.childForceExpandHeight = keyRow.childControlWidth = true;
            keyRow.childForceExpandWidth = false;
            HintButton(keys, "Tab  Console", TogglePanel);
            HintButton(keys, "H  Help", OpenHelp);
            HintButton(keys, "Esc  Menu", OpenPause);

            // One slim warning at a time, only when there is one.
            alertPlate = Plate(gameplay, "Warning", .32f, .885f, .68f, .945f);
            alertPlate.GetComponent<Image>().color = WithAlpha(Color.Lerp(ink, StatBar.ColorOf(Ui.UiDanger), 0.25f), 0.85f);
            alertText = Text(alertPlate, "", Small, paper, .02f, 0, .86f, 1);
            Button ok = HintButton(alertPlate, "OK", DismissWarning);
            Place((RectTransform)ok.transform, .88f, .15f, .98f, .85f);

            // Bottom left: suit vitals, oxygen first, with no plate behind them. Each row is an
            // icon, a label, a big amber CRT readout (100/100) and a gauge in a steel housing.
            RectTransform vitalsArea = StatBar.Frame(gameplay, "Suit vitals", .012f, .018f, .215f, .265f);
            string[] labels = { "Oxygen", "Health", "Water", "Food", "Rest" };
            ResourceSymbol[] symbols = { ResourceSymbol.Air, ResourceSymbol.Health, ResourceSymbol.Water, ResourceSymbol.Food, ResourceSymbol.Rest };
            vitalLabels = new TMP_Text[5]; vitalValues = new TMP_Text[5]; vitalFills = new Image[5]; vitalGroups = new CanvasGroup[5];
            for (int i = 0; i < 5; i++)
            {
                float top = 1 - i * .2f, bottom = top - .2f;
                RectTransform row = StatBar.Frame(vitalsArea, labels[i], 0f, bottom, 1f, top);
                vitalGroups[i] = row.gameObject.AddComponent<CanvasGroup>();
                ResourceGlyph icon = ResourceGlyph.Create(row, symbols[i], 26);
                icon.rectTransform.anchoredPosition = new Vector2(2, 4);
                vitalLabels[i] = Text(row, labels[i].ToUpperInvariant(), Small, paper, .17f, .42f, .60f, 1);
                vitalLabels[i].font = UiTypeface.Strong;
                vitalLabels[i].characterSpacing = 4f;
                Readable(vitalLabels[i]);
                vitalValues[i] = Text(row, "100/100", Small * UiTypeface.MonoScale * 1.55f, accent, .50f, .36f, 1, 1.1f);
                vitalValues[i].font = UiTypeface.Mono;
                vitalValues[i].alignment = TextAlignmentOptions.MidlineRight;
                Readable(vitalValues[i]);
                vitalFills[i] = SteelGauge(row, .17f, .08f, 1f, .38f, StatBar.ColorOf(ResourceGlyph.ColorFor(symbols[i])));
            }

            // Bottom right: small eat and drink buttons.
            // Top right, under the keys: today's plan, one line per job, the current one explained.
            planPlate = NewRect("Daily plan", gameplay);
            planPlate.anchorMin = planPlate.anchorMax = planPlate.pivot = new Vector2(1f, 1f);
            planPlate.anchoredPosition = new Vector2(-16f, -58f - MarsMiniMap.MiniHeight - 8f);
            planPlate.sizeDelta = new Vector2(400f, 100f);
            Image planBack = planPlate.gameObject.AddComponent<Image>();
            planBack.color = WithAlpha(ink, ChipAlpha);
            planBack.raycastTarget = false;
            VerticalLayoutGroup planLayout = planPlate.gameObject.AddComponent<VerticalLayoutGroup>();
            planLayout.padding = new RectOffset(16, 16, 10, 12);
            planLayout.spacing = 2;
            planLayout.childControlHeight = planLayout.childControlWidth = true;
            planLayout.childForceExpandHeight = false;
            planPlate.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            planTitle = PlanLine(Small, accent);
            planRows = new TMP_Text[8];
            planDetail = null;
            for (int i = 0; i < planRows.Length; i++)
            {
                planRows[i] = PlanLine(Tiny + 1, paper);
                if (i == 0) { planDetail = PlanLine(Tiny, paper); planDetail.enableWordWrapping = true; }
            }

            // Minimap above the plan; the large map opens over everything.
            map = gameObject.AddComponent<MarsMiniMap>();
            map.Build(gameplay, safe, font, ink, paper, accent, muted, ChipAlpha);

            // Morning card after a night's sleep.
            summaryPlate = Plate(gameplay, "Morning summary", .32f, .34f, .68f, .70f);
            summaryPlate.GetComponent<Image>().color = WithAlpha(ink, 0.94f);
            summaryTitle = Text(summaryPlate, "", 30, accent, .06f, .80f, .94f, .95f);
            summaryBody = Text(summaryPlate, "", Small + 2, paper, .06f, .22f, .94f, .80f);
            summaryBody.enableWordWrapping = true;
            summaryBody.overflowMode = TextOverflowModes.Overflow;
            summaryBody.alignment = TextAlignmentOptions.TopLeft;
            Button startDay = HintButton(summaryPlate, "Start the day  (Enter)", CloseSummary);
            Place((RectTransform)startDay.transform, .30f, .05f, .70f, .16f);
            summaryPlate.gameObject.SetActive(false);

            // Above the tool hotbar: the latest message, which fades into the next step.
            noticePlate = Plate(gameplay, "Message", .30f, .215f, .70f, .25f).GetComponent<Image>();
            notice = Text(noticePlate.rectTransform, "", Small, paper, .02f, 0, .98f, 1);
            notice.alignment = TextAlignmentOptions.Center;
            guide = Text(noticePlate.rectTransform, "", Small, accent, .02f, 0, .98f, 1);
            guide.alignment = TextAlignmentOptions.Center;

            // The outpost console.
            panel = StatBar.Frame(root.transform, "Outpost console", .045f, .10f, .955f, .92f, Ui.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            RectTransform titleRow = NewRect("Title", panel);
            Anchor(titleRow, new Vector2(0, 1), Vector2.one, new Vector2(26, -72), new Vector2(-26, -16));
            consoleTitle = Label(titleRow, "Outpost console", 30, accent);
            Place(consoleTitle.rectTransform, 0, 0, .6f, 1);
            RectTransform headerKeys = StatBar.Frame(titleRow, "Console keys", .4f, .12f, 1, .88f);
            HorizontalLayoutGroup headerRow = headerKeys.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerRow.spacing = 6; headerRow.childAlignment = TextAnchor.MiddleRight;
            headerRow.childControlHeight = headerRow.childForceExpandHeight = headerRow.childControlWidth = true;
            headerRow.childForceExpandWidth = false;
            backButton = HintButton(headerKeys, "‹  Back  (Esc)", Back);
            HintButton(headerKeys, "Help", () => Open(HelpTab));
            HintButton(headerKeys, "Settings", () => Open(SettingsView));
            HintButton(headerKeys, "Close  (Tab)", () => { if (mission != null && mission.IsRunning) mission.SetPanelOpen(false); });

            RectTransform scrollRoot = NewRect("Scrollable content", panel);
            Anchor(scrollRoot, Vector2.zero, Vector2.one, new Vector2(26, 22), new Vector2(-26, -84));
            scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            RectTransform viewport = NewRect("Viewport", scrollRoot);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Ui.UiGap;
            layout.padding = new RectOffset(4, 16, 4, 20);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            // Full-screen MarsTrainer screens: pause menu (Escape) and mission report.
            pause = PauseMenu.Create(safe);
            pause.Bind(new SimClock());
            pause.Closed = ClosePause;
            end = EndScreen.Create(safe);
            end.ReviewRequested = () => { tab = Home; detailId = null; mission.SetPanelOpen(true); RebuildPanel(); };
            BuildBriefing(safe);
            built = true;
            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// First screen of a mission: who you are, what keeps you alive, the three energy outputs,
        /// and how long to stay. Choosing a length starts the mission straight into the game.
        /// </summary>
        private void BuildBriefing(RectTransform root)
        {
            briefing = StatBar.Frame(root, "Mission briefing", 0, 0, 1, 1);
            Image shade = briefing.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.45f);
            RectTransform card = Plate(briefing, "Briefing card", .2f, .1f, .8f, .9f);
            card.GetComponent<Image>().color = WithAlpha(ink, 0.95f);
            Text(card, "MISSION BRIEFING", 15, muted, .05f, .91f, .95f, .97f);
            Text(card, "You are in charge of a Martian outpost", 34, accent, .05f, .82f, .95f, .91f);
            TMP_Text body = Text(card,
                "You are the commander, and the only astronaut, of a small outpost in Gale Crater on Mars. Your job is to survive until the mission ends.\n\n" +
                "<b>Follow your daily tasks.</b> Each sol lasts about 15 minutes. The plan at the top right tells you what to do next and an orange marker shows where: " +
                "eat and drink at the galley, tend the greenhouse, fix whatever breaks, explore a science site, report to Houston, then sleep in a bunk.\n\n" +
                "<b>Manage the 3 energy outputs</b> (Tab > Power, or the three-knob dashboard):\n" +
                "-  Life control: oxygen, air and water recycling\n" +
                "-  Temperature: heating and cooling the habitat\n" +
                "-  Radiation shielding: protection, most of all during solar storms\n" +
                "Give each one enough power, but no more. Extra draw drains the battery, and the solar panels only charge it in daylight.\n\n" +
                "The minimap (M) shows the rooms and where you are. H opens the full guide.",
                19, paper, .05f, .27f, .95f, .81f);
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;
            body.richText = true;
            body.alignment = TextAlignmentOptions.TopLeft;
            Text(card, "How long will you stay?", 22, accent, .05f, .19f, .95f, .25f);
            MissionLength[] lengths = { MissionLength.Month, MissionLength.HalfYear, MissionLength.Year };
            string[] labels = { "One Martian month  (56 sols)", "Half a Martian year  (334 sols)", "One Martian year  (669 sols)" };
            for (int i = 0; i < 3; i++)
            {
                MissionLength length = lengths[i];
                Button choose = Button(card, labels[i], () => StartFromBriefing(length));
                Place((RectTransform)choose.transform, .05f + i * .305f, .095f, .05f + i * .305f + .29f, .175f);
            }
            Button load = HintButton(card, "Continue a saved mission", () => { if (mission != null) mission.LoadMission(); });
            Place((RectTransform)load.transform, .35f, .025f, .65f, .075f);
            briefing.gameObject.SetActive(false);
        }

        private void StartFromBriefing(MissionLength length)
        {
            if (mission == null) return;
            tab = Home; detailId = null;
            mission.NewMission(length);
            briefing.gameObject.SetActive(false);
            mission.SetPanelOpen(false);
        }

        private void RefreshShell()
        {
            MissionSimulation simulation = mission.Simulation;
            if (simulation == null) return;
            MissionState s = simulation.State;
            double duration = MissionSettings.Duration(s.length);
            double hour = simulation.LocalHour;
            int hh = (int)Math.Floor(hour), mm = (int)Math.Floor((hour - hh) * 60);
            bool asleep = s.task.kind == TaskKind.Rest && s.task.targetId == "sleep";
            solText.text = string.Format(CultureInfo.InvariantCulture, "Sol {0:0} / {1:0}   {2:00}:{3:00}{4}",
                Math.Floor(1 + s.elapsedSeconds / MissionSettings.SolSeconds), duration / MissionSettings.SolSeconds, hh, mm,
                mission.Paused ? "  paused" : asleep && mission.AdvanceRemaining > 0 ? "  sleeping" : mission.AdvanceRemaining > 0 ? "  waiting" : "");
            string nearby = string.IsNullOrEmpty(mission.Context.station) ? mission.Context.zone.ToString() : Sentence(mission.Context.station.Replace('_', ' '));
            navigation.text = string.Format(CultureInfo.InvariantCulture, "{0}  ·  base {1:0} m  ·  {2}",
                nearby, mission.Context.distanceFromBase, (mission.Environment.label ?? "").ToLowerInvariant());

            // Battery and radiation, right under the sol counter.
            double charge = s.resources.batteryKwh / Math.Max(1, simulation.BatteryCapacity);
            batteryText.text = string.Format(CultureInfo.InvariantCulture, "Battery {0:0}%   {1:+0.0;-0.0;0.0} kW", charge * 100, simulation.Power.netKw);
            batteryText.color = charge < 0.15 ? StatBar.ColorOf(Ui.UiDanger) : muted;
            SetBar(batteryFill, charge);
            double rate = SurvivalNeeds.MilliSievert(mission.Environment.radiationMicrograyPerHour) * MissionSettings.SolSeconds / 3600;
            double budget = SurvivalNeeds.DoseBudgetFraction(s.astronaut);
            bool flare = mission.Environment.solarEvent > 0.05;
            string level = budget >= Ui.ShieldDangerFraction ? "danger" : budget >= Ui.ShieldWarnFraction || flare ? "high" : "safe";
            radiationText.text = string.Format(CultureInfo.InvariantCulture, "Radiation {0}   {1:0.0} mSv/sol", level, rate);
            radiationText.color = level == "safe" ? muted : StatBar.ColorOf(level == "danger" ? Ui.UiDanger : Ui.UiWarn);
            SetBar(radiationFill, budget);

            AstronautState a = s.astronaut;
            double[] values = { a.oxygen, a.health, a.hydration, a.satiety, 100 - a.fatigue };
            string[] labels = { "Oxygen", "Health", "Water", "Food", "Rest" };
            for (int i = 0; i < 5; i++)
            {
                double v = Math.Max(0, Math.Min(100, values[i]));
                vitalValues[i].text = v.ToString("0", CultureInfo.InvariantCulture) + "/100";
                SetBar(vitalFills[i], v / 100);
                bool low = v < 40;
                Color state = v < 20 ? StatBar.ColorOf(Ui.UiDanger) : low ? StatBar.ColorOf(Ui.UiWarn) : accent;
                vitalValues[i].color = state;
                vitalLabels[i].color = low ? state : paper;
                vitalGroups[i].alpha = 1f;
            }
            RefreshPlan(s);

            // Warnings: one at a time; OK hides it until it clears and comes back.
            dismissed.RemoveWhere(w => !simulation.Warnings.Contains(w));
            warningQueue.Clear();
            if (flare) warningQueue.Add("Solar flare now: go to the storm shelter.");
            foreach (string warning in simulation.Warnings) if (!dismissed.Contains(warning)) warningQueue.Add(warning);
            alertPlate.gameObject.SetActive(warningQueue.Count > 0);
            if (warningQueue.Count > 0) alertText.text = warningQueue[0] + (warningQueue.Count > 1 ? "   (+" + (warningQueue.Count - 1) + ")" : "");

            string message = mission.Notice ?? "";
            if (message != lastNotice) { lastNotice = message; noticeChangedAt = Time.unscaledTime; }
            notice.text = message;
            guide.text = "Next: " + SurvivalGuide(s);
        }

        private void DismissWarning()
        {
            if (warningQueue.Count == 0) return;
            if (!warningQueue[0].StartsWith("Solar flare")) dismissed.Add(warningQueue[0]);
            warningQueue.RemoveAt(0);
            alertPlate.gameObject.SetActive(warningQueue.Count > 0);
            if (warningQueue.Count > 0) alertText.text = warningQueue[0];
        }

        /// <summary>A new message shows for a few seconds, then gives way to the next step.</summary>
        private void FadeNotice()
        {
            if (notice == null) return;
            float age = Time.unscaledTime - noticeChangedAt;
            float a = string.IsNullOrEmpty(notice.text) ? 0f : Mathf.Clamp01((NoticeSeconds - age) / 0.6f);
            notice.alpha = a;
            guide.alpha = 1f - a;
        }

        private void ShowReport()
        {
            reportShown = true;
            MissionState s = mission.Simulation.State;
            mission.SetPanelOpen(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            string helped = string.Format(CultureInfo.InvariantCulture, "{0} kWh of solar energy, {1:0} food portions harvested, {2} repairs, {3} research installs",
                s.totalGeneratedKwh.ToString("0", CultureInfo.InvariantCulture), s.totalFoodHarvested, s.repairsCompleted, s.installed.Count);
            string hurt = string.IsNullOrEmpty(s.astronaut.lastDamageCause) ? "No serious warnings recorded" : s.astronaut.lastDamageCause;
            end.Show(s.phase == MissionPhase.Won, s.elapsedSeconds / MissionSettings.SolSeconds, hurt, helped,
                SurvivalNeeds.MilliSievert(s.astronaut.doseMicrogray),
                s.phase == MissionPhase.Won ? "You Survived " + LengthName(s.length) + "!" : "Your Mission Has Ended");
        }

        private void OpenPause()
        {
            if (mission == null || !mission.IsRunning || ScreenOpen) return;
            mission.SetPanelOpen(false);
            pausedByMenu = !mission.Paused;
            mission.SetPaused(true);
            pause.Open();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ClosePause()
        {
            if (mission == null) return;
            if (pausedByMenu) mission.SetPaused(false);
            pausedByMenu = false;
            mission.SetPanelOpen(false);
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
            bool running = state != null && state.phase == MissionPhase.Running;
            consoleTitle.text = tab != Home ? ViewNames[tab] : state == null ? "Choose a mission" : running ? ViewNames[Home] : "Mission report";
            backButton.gameObject.SetActive(tab != Home || detailId != null);
            if (tab == HelpTab && (state == null || running)) BuildHelp();
            else if (state == null) BuildMissionSelector();
            else if (!running) BuildDebrief();
            else
            {
                switch (tab)
                {
                    case PowerView: BuildPowerWindow(); break;
                    case CrewView: BuildCrewWindow(); break;
                    case GreenhouseView: BuildGreenhouseWindow(); break;
                    case EquipmentView: BuildEquipmentWindow(); break;
                    case ResearchView: if (detailId != null) BuildResearchDetail(detailId); else BuildResearchWindow(); break;
                    case FieldView: BuildFieldWindow(); break;
                    case RadioView: BuildRadioWindow(); break;
                    case LearningView: if (detailId != null) BuildLearningDetail(detailId); else BuildLearningWindow(); break;
                    case SettingsView: BuildSettings(); break;
                    default: BuildHome(); break;
                }
            }
            foreach (Action update in live) update();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1;
            if (EventSystem.current != null && controls.Count > 0)
                EventSystem.current.SetSelectedGameObject(controls[0].gameObject);
        }

        /// <summary>Opens a console window (and optionally one item inside it).</summary>
        private void Open(int view, string detail = null)
        {
            tab = view;
            detailId = detail;
            if (mission != null && !mission.PanelOpen) mission.SetPanelOpen(true);
            RebuildPanel();
        }

        /// <summary>One level up: item to window, window to the console's home.</summary>
        private void Back()
        {
            if (detailId != null) detailId = null; else tab = Home;
            RebuildPanel();
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
                tab = Home; detailId = null;
                mission.NewMission(length);
                mission.SetPanelOpen(true);
                RebuildPanel();
            });
        }

        // ------------------------------------------------------------------ console: home of live tiles

        /// <summary>
        /// The console's front page: what to do next, eight live system displays (click one to open
        /// its window) and the fast-forward strip. Actions live inside the windows, not here.
        /// </summary>
        private void BuildHome()
        {
            RectTransform now = Card();
            LiveLabel(now, () => "Next:  " + SurvivalGuide(State), 21);
            FlowMeter(now, () =>
            {
                List<PlanStep> steps = MissionDailyPlan.Steps(mission);
                return "Today's plan: " + steps.FindAll(p => p.done).Count + " of " + steps.Count + " done";
            }, () =>
            {
                List<PlanStep> steps = MissionDailyPlan.Steps(mission);
                return steps.Count == 0 ? 0 : (double)steps.FindAll(p => p.done).Count / steps.Count;
            }, () => accent, false, 18);

            List<RectTransform> cells = Grid(8, 4, 214);
            PowerTile(cells[0]);
            CrewTile(cells[1]);
            GreenhouseTile(cells[2]);
            EquipmentTile(cells[3]);
            ResearchTile(cells[4]);
            FieldTile(cells[5]);
            RadioTile(cells[6]);
            LearningTile(cells[7]);
            TimeStrip();
        }

        private void PowerTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Power", () => Open(PowerView));
            LiveLabel(t, () => "Battery " + Percent(BatteryFraction()), 30, () => BatteryFraction() < 0.15 ? Danger : paper);
            LiveLabel(t, () => string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0} kW   ·   {1}", mission.Simulation.Power.netKw, Endurance()), 16, null, muted);
            string[] names = { "Life", "Heat", "Shield" };
            for (int i = 0; i < 3; i++)
            {
                int c = i;
                FlowMeter(t, () => names[c] + "  " + ChannelWord(c), () => ChannelRatio(c), () => ChannelColor(c), true, 16);
            }
        }

        private void CrewTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Crew", () => Open(CrewView));
            string[] names = { "Oxygen", "Health", "Water", "Food", "Rest" };
            ResourceSymbol[] symbols = { ResourceSymbol.Air, ResourceSymbol.Health, ResourceSymbol.Water, ResourceSymbol.Food, ResourceSymbol.Rest };
            for (int i = 0; i < 5; i++)
            {
                int c = i;
                Color color = StatBar.ColorOf(ResourceGlyph.ColorFor(symbols[i]));
                FlowMeter(t, () => names[c] + "  " + Percent(Vital(c) / 100), () => Vital(c) / 100, () => Vital(c) < 20 ? Danger : color, true, 16);
            }
        }

        private void GreenhouseTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Greenhouse", () => Open(GreenhouseView));
            LiveLabel(t, () => "Power " + (State.greenhouseEnabled ? "on" : "OFF"), 18, () => State.greenhouseEnabled ? paper : Warn);
            foreach (CropState crop in State.crops)
            {
                CropState c = crop;
                FlowMeter(t, () => "Bed " + (c.bed + 1) + "  " + CropWord(c), () => c.planted ? c.growth : 0, () => CropColor(c), true, 16);
            }
            LiveLabel(t, () => string.Format(CultureInfo.InvariantCulture, "Seeds {0:0}   ·   Food {1:0} portions", State.resources.seeds, State.resources.food), 16, null, muted);
        }

        private void EquipmentTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Equipment", () => Open(EquipmentView));
            FlowMeter(t, () => "Base  " + Percent(State.equipment.baseCondition), () => State.equipment.baseCondition, () => ConditionColor(State.equipment.baseCondition), true, 16);
            FlowMeter(t, () => "Rover  " + Percent(State.equipment.roverCondition), () => State.equipment.roverCondition, () => ConditionColor(State.equipment.roverCondition), true, 16);
            FlowMeter(t, () => "Panels  " + Percent(State.equipment.panelCondition), () => State.equipment.panelCondition, () => ConditionColor(State.equipment.panelCondition), true, 16);
            FlowMeter(t, () => "Dust  " + Percent(State.equipment.panelDust), () => State.equipment.panelDust, () => State.equipment.panelDust > 0.15 ? Warn : muted, true, 16);
        }

        private void ResearchTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Research", () => Open(ResearchView));
            LiveLabel(t, () => State.resources.researchPoints + " points", 30);
            LiveLabel(t, () => "Installed " + State.installed.Count + " / " + ResearchCatalog.Nodes.Length, 17);
            LiveLabel(t, () =>
            {
                int ready = 0, waiting = 0;
                foreach (ResearchNode node in ResearchCatalog.Nodes)
                {
                    if (CanResearch(node)) ready++;
                    if (State.researched.Contains(node.id) && !State.installed.Contains(node.id)) waiting++;
                }
                return ready + " ready to research   ·   " + waiting + " to install";
            }, 16, null, muted);
        }

        private void FieldTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Field sites", () => Open(FieldView));
            LiveLabel(t, () => State.sites.FindAll(s => s.analyzed).Count + " / " + State.sites.Count + " analysed", 30);
            LiveLabel(t, () =>
            {
                SiteState selected = State.sites.Find(s => s.id == mission.SelectedSiteId);
                return selected == null ? "No site selected" : "Target: " + selected.name;
            }, 17);
            LiveLabel(t, () => string.Format(CultureInfo.InvariantCulture, "Suit {0:0.0} h   ·   field battery {1:0.00} kWh",
                mission.Simulation.SuitHoursRemaining, State.resources.fieldKwh), 16, null, muted);
        }

        private void RadioTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Radio", () => Open(RadioView));
            LiveLabel(t, () => Sentence(State.radioMode.ToString()), 30);
            FlowMeter(t, () => "Signal  " + Percent(mission.Environment.signal), () => mission.Environment.signal,
                () => mission.Environment.signal > 0.1 ? Safe : Warn, true, 16);
            LiveLabel(t, () => State.reports.FindAll(r => !r.acknowledged).Count + " queued   ·   " +
                State.reports.FindAll(r => r.acknowledged).Count + " acknowledged", 16, null, muted);
        }

        private void LearningTile(RectTransform cell)
        {
            RectTransform t = Tile(cell, "Learning and data", () => Open(LearningView));
            LiveLabel(t, () => State.tutorialStep < 0 ? "Guided objectives off" :
                State.tutorialStep >= 10 ? "Guided objectives done" : "Objective " + (State.tutorialStep + 1) + " / 10", 20);
            LiveLabel(t, () => State.tutorialStep >= 0 && State.tutorialStep < 10 ? TutorialObjective(State) : "Experiments and NASA sources", 16, null, muted);
        }

        /// <summary>Waiting: an hour at a time, inside only. Nights pass by sleeping in a bunk.</summary>
        private void TimeStrip()
        {
            RectTransform card = Card();
            LabelInFlow(card, "Time", 22, accent);
            LiveLabel(card, () => mission.AdvanceRemaining > 0
                ? "Waiting   ·   " + Duration(mission.AdvanceRemaining) + " to go. It stops by itself when something needs you."
                : "A sol lasts about 15 minutes. Sleep in a bunk to skip the night; wait here only for a storm or a radio window. Nothing eats or drinks for you.", 17, null, muted);
            RectTransform row = Row(card, 46);
            SmallButton(row, "Wait 1 hour", () => mission.Advance(MissionSettings.HourSeconds));
            Button stop = SmallButton(row, "Stop waiting", () => mission.StopAdvance("Stopped waiting."));
            live.Add(() => stop.interactable = mission.AdvanceRemaining > 0);
        }

        /// <summary>One plain line in the daily plan plate.</summary>
        private TMP_Text PlanLine(float size, Color color)
        {
            TMP_Text text = Label(planPlate, "", size, color, true);
            text.enableWordWrapping = false;
            text.richText = true;
            text.gameObject.AddComponent<LayoutElement>().minHeight = size + 6;
            return text;
        }

        private void RefreshPlan(MissionState s)
        {
            List<PlanStep> steps = MissionDailyPlan.Steps(mission);
            int current = MissionDailyPlan.Current(steps, mission.Simulation.LocalHour);
            int done = steps.FindAll(p => p.done).Count;
            planTitle.text = "Sol " + Math.Max(1, s.day.number) + " plan   " + done + " / " + steps.Count;
            string safe = "#" + ColorUtility.ToHtmlStringRGB(StatBar.ColorOf(Ui.UiSafe));
            string dim = "#" + ColorUtility.ToHtmlStringRGB(muted);
            for (int i = 0; i < planRows.Length; i++)
            {
                bool shown = i < steps.Count;
                planRows[i].gameObject.SetActive(shown);
                if (!shown) continue;
                PlanStep step = steps[i];
                string mark = step.done ? "<color=" + safe + ">done</color>" : step.skipped ? "<color=" + dim + ">skipped</color>" : i == current ? ">" : "";
                planRows[i].text = (i + 1) + "  " + step.title + (mark.Length > 0 ? "   " + mark : "");
                planRows[i].color = i == current ? accent : step.done || step.skipped ? muted : paper;
                planRows[i].fontSize = i == current ? Small + 1 : Tiny + 1;
                // The current job's explanation sits right under it.
                if (i == current) planDetail.transform.SetSiblingIndex(planRows[i].transform.GetSiblingIndex() + 1);
            }
            planDetail.gameObject.SetActive(current >= 0);
            if (current >= 0) planDetail.text = steps[current].detail;

            bool morning = s.day.summaryPending && mission.AdvanceRemaining <= 0 && !mission.PanelOpen && !ScreenOpen;
            if (morning && !summaryPlate.gameObject.activeSelf)
            {
                summaryPlate.gameObject.SetActive(true);
                mission.SetPaused(true);
                summaryTitle.text = "Good morning!  Sol " + s.day.number + ", 07:00";
                summaryBody.text = "Yesterday: " + (string.IsNullOrEmpty(s.day.yesterday) ? "a quiet day." : s.day.yesterday + ".") +
                    string.Format(CultureInfo.InvariantCulture, "\n\nStores: {0:0} food portions, {1:0} L water, battery {2:0}%.",
                        s.resources.food, s.resources.cleanWaterLiters, 100 * s.resources.batteryKwh / Math.Max(1, mission.Simulation.BatteryCapacity)) +
                    "\nWeather: " + (mission.Environment.label ?? "clear") + "." +
                    (s.faultActive ? "\nA fault is sparking in the " + MarsWorldTasks.FaultRooms[Mathf.Clamp(s.faultSpot, 0, MarsWorldTasks.FaultRooms.Length - 1)] + "." : "") +
                    "\n\nYou woke up hungry and thirsty: breakfast in the galley comes first. Your plan is on the right.";
            }
        }

        private void CloseSummary()
        {
            if (summaryPlate == null || !summaryPlate.gameObject.activeSelf) return;
            summaryPlate.gameObject.SetActive(false);
            if (mission != null && mission.Simulation != null) mission.Simulation.State.day.summaryPending = false;
            if (mission != null) { mission.SetPaused(false); mission.Notice = "A new sol begins. Breakfast first: the galley in the command hub."; }
        }

        // ------------------------------------------------------------------ console windows

        private void BuildPowerWindow()
        {
            RectTransform battery = Card();
            LiveLabel(battery, () => string.Format(CultureInfo.InvariantCulture, "Battery {0}   ·   {1:0.0} of {2:0.0} kWh   ·   {3}",
                Percent(BatteryFraction()), State.resources.batteryKwh, mission.Simulation.BatteryCapacity, Endurance()), 24);
            FlowMeter(battery, () => "", BatteryFraction, () => BatteryFraction() < 0.15 ? Danger : StatBar.ColorOf(Ui.UiPower), false, 4);
            LiveLabel(battery, () =>
            {
                PowerSnapshot p = mission.Simulation.Power;
                return string.Format(CultureInfo.InvariantCulture,
                    "Solar in {0:0.00} kW   ·   Load {1:0.00} kW   ·   Greenhouse {2:0.00}   ·   Task {3:0.00}   ·   Charging {4:0.00}   ·   Radio {5:0.00}   ·   Wasted by overdraw {6:0.00}",
                    p.generatedKw, p.loadKw, p.greenhouseKw, p.taskKw, p.chargingKw, p.radioKw, p.overdrawLossKw);
            }, 17, null, muted);
            LiveLabel(battery, NeedleText, 17, null, muted);

            Paragraph("Drag each knob until the bar under it is full. A short bar is a shortfall that hurts the astronaut; drawing past the need wastes battery three times over.", 19);
            string[] names = { "Life control", "Temperature", "Radiation shielding" };
            string[] hints = { "Oxygen, air processing and water recycling", "Heating and cooling for the habitat", "Extra protection on top of the permanent shielding" };
            for (int i = 0; i < 3; i++)
            {
                int channel = i;
                RectTransform card = Card();
                LabelInFlow(card, names[i], 24, accent);
                LabelInFlow(card, hints[i], 17, muted);
                Slider knob = Slider(card, (float)State.knobs[channel], value =>
                {
                    if (suppressSliders) return;
                    if (mission.Dashboard != null) mission.Dashboard.NotifySoftwareOverride((PowerChannel)channel);
                    mission.SetPowerRequest((PowerChannel)channel, value);
                });
                live.Add(() =>
                {
                    suppressSliders = true;
                    knob.SetValueWithoutNotify((float)State.knobs[channel]);
                    suppressSliders = false;
                });
                FlowMeter(card, () =>
                {
                    PowerSnapshot p = mission.Simulation.Power;
                    return string.Format(CultureInfo.InvariantCulture, "Knob {0:0}%   ·   delivered {1:0.00} of {2:0.00} kW needed   ·   {3}",
                        State.knobs[channel] * 100, p.deliveredKw[channel], p.neededKw[channel], ChannelWord(channel));
                }, () => ChannelRatio(channel), () => ChannelColor(channel), false, 18);
            }

            RectTransform conditions = Card();
            LabelInFlow(conditions, "Conditions", 22, accent);
            LiveLabel(conditions, () => string.Format(CultureInfo.InvariantCulture,
                "Outside {0:0.0} °C   ·   Habitat {1:0.0} °C   ·   Dose rate {2:0.0} µGy/h   ·   Total {3:0.0} µGy\n{4}",
                mission.Environment.temperatureC, State.equipment.habitatTemperatureC, mission.Environment.radiationMicrograyPerHour,
                State.astronaut.doseMicrogray, mission.Simulation.Warnings.Count == 0 ? "No warnings." : string.Join("\n", mission.Simulation.Warnings.ToArray())), 18);
        }

        /// <summary>What the dashboard needle was last told (shown in Power and Settings).</summary>
        private string NeedleText()
        {
            ArduinoLink link = mission.Arduino == null ? null : mission.Arduino.Link;
            if (link == null || !link.Connected) return "Dashboard needle: no Arduino connected.";
            return string.Format(CultureInfo.InvariantCulture, "Dashboard needle: S{0} of {1} steps ({2:0}% battery, {3:0.0} deg)   ·   {4} needle commands sent   ·   moves in 1% steps",
                link.GaugeSentSteps, link.gaugeFullSteps, 100.0 * Math.Abs(link.GaugeSentSteps) / Math.Max(1, link.gaugeFullSteps),
                180.0 * Math.Abs(link.GaugeSentSteps) / Math.Max(1, link.gaugeFullSteps), link.GaugeCommandsSent);
        }

        private void BuildCrewWindow()
        {
            RectTransform vitals = Card();
            string[] names = { "Oxygen", "Health", "Water", "Food", "Rest" };
            ResourceSymbol[] symbols = { ResourceSymbol.Air, ResourceSymbol.Health, ResourceSymbol.Water, ResourceSymbol.Food, ResourceSymbol.Rest };
            for (int i = 0; i < 5; i++)
            {
                int c = i;
                Color color = StatBar.ColorOf(ResourceGlyph.ColorFor(symbols[i]));
                FlowMeter(vitals, () => names[c] + "  " + Percent(Vital(c) / 100), () => Vital(c) / 100, () => Vital(c) < 20 ? Danger : color, true, 19);
            }
            FlowMeter(vitals, () => "Fitness  " + Percent(State.astronaut.fitness / 100), () => State.astronaut.fitness / 100, () => Safe, true, 19);

            Paragraph("Look after the astronaut in person, at the right place in the base:", 19);
            List<RectTransform> cells = Grid(6, 3, 132);
            InfoTile(cells[0], "Eat", "Galley in the command hub: press E at the food locker. " + State.resources.food.ToString("0", CultureInfo.InvariantCulture) + " portions in store.");
            InfoTile(cells[1], "Drink", "Galley in the command hub: press E at the water dispenser. " + State.resources.cleanWaterLiters.ToString("0", CultureInfo.InvariantCulture) + " L clean water.");
            ActionTile(cells[2], "Refill suit", () => string.Format(CultureInfo.InvariantCulture, "Suit {0:0} g oxygen   ·   habitat {1:0} g", State.resources.suitOxygenGrams, State.resources.oxygenGrams), () => mission.Execute("refill"));
            InfoTile(cells[3], "Sleep", "Bunk in the crew quarters (E), after 17:00. You wake at 07:00.");
            InfoTile(cells[4], "Exercise", "Exercise bike in the crew quarters (E). Keeps fitness up.");
            InfoTile(cells[5], "Medical treatment", "Treatment bed in the medical bay (E). " + State.resources.medicine.ToString("0", CultureInfo.InvariantCulture) + " supplies left.");
        }

        private void BuildGreenhouseWindow()
        {
            Paragraph("Crops are tended by hand: sow each of a bed's 12 spots with the Shovel (9), water every growing bed once a day with the Sprayer (0), and pick ripe plants one by one with E.", 19);
            List<RectTransform> beds = Grid(State.crops.Count, 2, 236);
            for (int i = 0; i < State.crops.Count; i++)
            {
                CropState c = State.crops[i];
                RectTransform t = Tile(beds[i], "Bed " + (c.bed + 1), null);
                LiveLabel(t, () => Sentence(c.crop) + " crop   ·   " + CropWord(c), 26, () => c.ready ? Safe : paper);
                FlowMeter(t, () => "Growth  " + Percent(c.planted ? c.growth : 0), () => c.planted ? c.growth : 0, () => CropColor(c), true, 17);
                FlowMeter(t, () => "Soil moisture  " + Percent(Math.Min(1, c.moisture)), () => c.moisture, () => c.moisture <= 0.15 ? Danger : c.moisture > 1.15 ? Warn : StatBar.ColorOf(Ui.UiWater), true, 17);
                FlowMeter(t, () => "Stress  " + Percent(c.stress), () => c.stress, () => c.stress > 0.4 ? Danger : Warn, true, 17);
                LiveLabel(t, () => c.ready ? "Ripe: pick each plant with E." : !c.planted ? "Sow it with the Shovel (9): " + CropState.Count(c.sownMask) + " of 12 spots." :
                    c.moisture <= 0.15 ? "Dry, not growing: water it with the Sprayer (0)." : "Growing. Harvested " + c.harvests + " times.", 17, null, muted);
            }
            List<RectTransform> cells = Grid(2, 2, 120);
            RectTransform power = Tile(cells[0], "Greenhouse power", () => mission.Execute("greenhouse"));
            LiveLabel(power, () => State.greenhouseEnabled ? "On   ·   click to switch off" : "OFF   ·   click to switch on", 22, () => State.greenhouseEnabled ? Safe : Warn);
            RectTransform supplies = Tile(cells[1], "Supplies", null);
            LiveLabel(supplies, () => string.Format(CultureInfo.InvariantCulture, "Seeds {0:0.0}   ·   Nutrients {1:0.0}\nClean water {2:0.0} L   ·   Food {3:0.0} portions",
                State.resources.seeds, State.resources.nutrients, State.resources.cleanWaterLiters, State.resources.food), 18);
        }

        private void BuildEquipmentWindow()
        {
            RectTransform card = Card();
            FlowMeter(card, () => "Base condition  " + Percent(State.equipment.baseCondition), () => State.equipment.baseCondition, () => ConditionColor(State.equipment.baseCondition), true, 19);
            FlowMeter(card, () => "Rover condition  " + Percent(State.equipment.roverCondition), () => State.equipment.roverCondition, () => ConditionColor(State.equipment.roverCondition), true, 19);
            FlowMeter(card, () => "Panel condition  " + Percent(State.equipment.panelCondition), () => State.equipment.panelCondition, () => ConditionColor(State.equipment.panelCondition), true, 19);
            FlowMeter(card, () => "Panel dust  " + Percent(State.equipment.panelDust), () => State.equipment.panelDust, () => State.equipment.panelDust > 0.15 ? Warn : muted, true, 19);
            LiveLabel(card, () => string.Format(CultureInfo.InvariantCulture, "Spare parts {0:0.0}   ·   Field battery {1:0.00} kWh   ·   Rover battery {2:0.0} kWh",
                State.resources.parts, State.resources.fieldKwh, State.resources.roverKwh), 17, null, muted);

            List<RectTransform> switches = Grid(2, 2, 110);
            RectTransform rover = Tile(switches[0], "Rover charging", () => mission.Execute("charge_rover"));
            LiveLabel(rover, () => State.chargeRover ? "On   ·   click to stop" : "Off   ·   click to charge", 21, () => State.chargeRover ? Safe : paper);
            RectTransform field = Tile(switches[1], "Field battery charging", () => mission.Execute("charge_field"));
            LiveLabel(field, () => State.chargeField ? "On   ·   click to stop" : "Off   ·   click to charge", 21, () => State.chargeField ? Safe : paper);

            Paragraph("Repairs are done with tools, where the equipment is:", 19);
            List<RectTransform> jobs = Grid(3, 3, 120);
            InfoTile(jobs[0], "Fix faults", State.faultActive ? "Sparking box on the " + MarsWorldTasks.FaultRooms[Mathf.Clamp(State.faultSpot, 0, MarsWorldTasks.FaultRooms.Length - 1)] + " ceiling: Field Wrench (8), tighten 3 bolts." : "No fault today. When one sparks, fix it with the Field Wrench (8).");
            InfoTile(jobs[1], "Repair the rover", "Field Wrench (8), beside the rover. Tighten 3 bolts; 2 parts.");
            InfoTile(jobs[2], "Sweep the panels", "Panel Duster (7) at the solar array outside: hold the brush on each dusty panel.");
        }

        private void BuildResearchWindow()
        {
            LiveParagraph(() => State.resources.researchPoints + " research points   ·   " +
                State.resources.parts.ToString("0.0", CultureInfo.InvariantCulture) + " parts. Analyse samples to earn points; click a design to see it.", 20);
            string branch = null;
            List<ResearchNode> group = new List<ResearchNode>();
            foreach (ResearchNode node in ResearchCatalog.Nodes)
            {
                if (branch != null && node.branch != branch) { ResearchGroup(branch, group); group.Clear(); }
                branch = node.branch;
                group.Add(node);
            }
            if (group.Count > 0) ResearchGroup(branch, group);

            Heading("EXTRA EQUIPMENT");
            List<RectTransform> cells = Grid(2, 2, 110);
            ActionTile(cells[0], "Install a solar panel", () => "Needs the array expansion design and parts.", () => mission.StartTask(TaskKind.Install, "panel"));
            ActionTile(cells[1], "Install a battery cabinet", () => "Needs a battery module design and parts.", () => mission.StartTask(TaskKind.Install, "battery"));
        }

        private void ResearchGroup(string branch, List<ResearchNode> nodes)
        {
            Heading(branch.ToUpperInvariant());
            List<RectTransform> cells = Grid(nodes.Count, 4, 104);
            for (int i = 0; i < nodes.Count; i++)
            {
                ResearchNode node = nodes[i];
                RectTransform t = Tile(cells[i], node.id + "  " + node.title, () => Open(ResearchView, node.id));
                LiveLabel(t, () => Sentence(ResearchStatus(node)), 17, () => ResearchColor(node));
            }
        }

        private void BuildResearchDetail(string id)
        {
            ResearchNode node = null;
            foreach (ResearchNode n in ResearchCatalog.Nodes) if (n.id == id) node = n;
            if (node == null) { detailId = null; BuildResearchWindow(); return; }
            consoleTitle.text = node.id + "  " + node.title;
            RectTransform card = Card();
            LiveLabel(card, () => Sentence(ResearchStatus(node)), 22, () => ResearchColor(node));
            LabelInFlow(card, node.description, 20, paper);
            LabelInFlow(card, string.Format(CultureInfo.InvariantCulture,
                "Unlock: {0} points   ·   Install: {1:0} parts, {2} and power   ·   Needs first: {3}",
                node.points, node.parts, Duration(node.installationSeconds), node.prerequisites.Length == 0 ? "nothing" : string.Join(", ", node.prerequisites)), 18, muted);
            RectTransform row = Row(card, 50);
            Button act = SmallButton(row, "", () =>
            {
                if (CanResearch(node)) mission.Research(node.id);
                else mission.StartTask(TaskKind.Install, node.id);
            });
            TMP_Text label = act.GetComponentInChildren<TMP_Text>();
            live.Add(() =>
            {
                bool install = State.researched.Contains(node.id) && !State.installed.Contains(node.id);
                label.text = State.installed.Contains(node.id) ? "Installed" : install ? "Install (in the energy room)" : "Research for " + node.points + " points";
                act.interactable = CanResearch(node) || (install && State.task.kind == TaskKind.None);
            });
        }

        private void BuildFieldWindow()
        {
            LiveParagraph(() => string.Format(CultureInfo.InvariantCulture,
                "{0:0} m from base   ·   suit {1:0.0} h left   ·   field battery {2:0.00} kWh   ·   rover {3:0.0} kWh. Click a numbered site to target it.",
                mission.Context.distanceFromBase, mission.Simulation.SuitHoursRemaining, State.resources.fieldKwh, State.resources.roverKwh), 19);
            BuildMap();
            SiteState site = State.sites.Find(s => s.id == mission.SelectedSiteId);
            if (site == null) return;
            RectTransform card = Card();
            LabelInFlow(card, site.name, 24, accent);
            double walkingMinutes = site.distanceMeters * 2 / (3.4 / 3.6) / 60;
            LabelInFlow(card, string.Format(CultureInfo.InvariantCulture,
                "{0:0.0000}°, {1:0.0000}°   ·   {2:0} m from base   ·   round trip at least {3:0} min on foot   ·   {4} research points",
                site.latitude, site.longitude, site.distanceMeters, walkingMinutes, site.reward), 17, muted);
            List<RectTransform> steps = Grid(6, 3, 98);
            StepTile(steps[0], "Survey", "Rock Hammer (1)", () => site.surveyed, null);
            StepTile(steps[1], "Measure", "click here at the site", () => site.measured, () => mission.Execute("measure", site.id));
            StepTile(steps[2], "Core sample", "Core Drill (2)", () => site.sampled, null);
            StepTile(steps[3], "Marker", "Rock Hammer (1)", () => site.markerPlaced, null);
            StepTile(steps[4], "Photo", "Survey Camera (3)", () => site.photographed, null);
            StepTile(steps[5], "Analysis", "Core Drill (2) at the lab", () => site.analyzed, null);
        }

        private void BuildMap()
        {
            RectTransform map = Box("Site map", content, WithAlpha(MarsTrainer.UI.UiTheme.Hover, 0.22f));
            Height(map, 330);
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
            TMP_Text legend = Label(map, "N ↑     ● You     ■ Habitat     Orange: target     Faded: done", 17, muted);
            Anchor(legend.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -32), new Vector2(-12, -4));
            TMP_Text baseMarker = Label(map, "■ HOME", 19, paper);
            MapPosition(baseMarker.rectTransform, project(home.x, home.z), new Vector2(110, 26));
            for (int i = 0; i < State.sites.Count; i++)
            {
                SiteState site = State.sites[i];
                Button point = Button(map, (i + 1).ToString(CultureInfo.InvariantCulture), () => { mission.SelectSite(site.id); RebuildPanel(); });
                MapPosition((RectTransform)point.transform, project(site.x, site.z), new Vector2(52, 34));
                point.GetComponent<Image>().color = site.id == mission.SelectedSiteId ? accent : WithAlpha(MarsTrainer.UI.UiTheme.Hover, site.analyzed ? 0.45f : 1f);
            }
            TMP_Text player = Label(map, "●", 27, accent);
            live.Add(() => MapPosition(player.rectTransform, project(State.playerX, State.playerZ), new Vector2(26, 26)));
        }

        private void BuildRadioWindow()
        {
            drawnReports = State.reports.Count;
            RectTransform card = Card();
            FlowMeter(card, () => "Signal  " + Percent(mission.Environment.signal) + "   ·   " +
                (mission.Environment.signal > 0.1 ? "link available now" : "next window in " + Duration(mission.Environment.nextWindowSeconds)),
                () => mission.Environment.signal, () => mission.Environment.signal > 0.1 ? Safe : Warn, false, 20);
            LabelInFlow(card, "Houston is simulated mission control; nothing is really sent. Weak signal makes a transmission take longer and use more power.", 17, muted);

            List<RectTransform> cells = Grid(4, 4, 104);
            RadioMode[] modes = { RadioMode.Off, RadioMode.Listen, RadioMode.Transmit };
            string[] notes = { "Saves power", "Hear Houston", "Send queued reports" };
            for (int i = 0; i < 3; i++)
            {
                RadioMode mode = modes[i];
                RectTransform t = Tile(cells[i], mode.ToString(), () => mission.Execute("radio", mode.ToString().ToLowerInvariant()));
                LiveLabel(t, () => State.radioMode == mode ? "Selected" : notes[(int)mode], 18, () => State.radioMode == mode ? accent : muted);
            }
            ActionTile(cells[3], "New status report", () => "Queue today's update", () => { mission.Execute("report"); RebuildPanel(); });

            Heading("REPORTS");
            if (State.reports.Count == 0) Paragraph("None yet. Do some fieldwork or maintenance, then queue an update.", 19);
            foreach (ReportState report in State.reports)
            {
                ReportState r = report;
                RectTransform item = Card();
                LiveLabel(item, () => (r.acknowledged ? "Acknowledged" : "Queued") + "   ·   sent for " + Duration(r.progressSeconds), 19, () => r.acknowledged ? Safe : accent);
                LabelInFlow(item, report.summary, 17, paper);
            }
        }

        private static readonly string[][] Lessons =
        {
            new[] { "Electricity and food are different", "Predict whether a meal will recharge the habitat battery. Eat when hungry and compare satiety with stored kWh. Food supports the astronaut; solar panels supply electrical energy.", "NASA plants in space", "https://www.nasa.gov/exploration-research-and-technology/growing-plants-in-space/" },
            new[] { "Oxygen and water share resources", "Lower Life control briefly while reserves are safe. Watch delivered power and oxygen/water processing. Restore enough power before reserves become dangerous. Recycling recovers part of an input; it cannot create water from nothing.", "NASA ECLSS", "https://www.nasa.gov/reference/environmental-control-and-life-support-systems-eclss/" },
            new[] { "Crops need a controlled environment", "Plant a crop and predict how a shortage changes growth. Compare water, temperature, stress and delivered greenhouse power. Crop durations and yields here are tuned game values; the source explains controlled growth requirements.", "NASA controlled growing systems", "https://www.nasa.gov/exploration-research-and-technology/growing-plants-in-space/" },
            new[] { "Shelter changes future exposure", "Compare the dose-rate readout outside and inside shelter during a warning. Protection reduces future accumulation; it does not erase the dose already received. The powered shielding curve and HP penalties are teaching abstractions.", "NASA Curiosity radiation measurements", "https://science.nasa.gov/resource/radiation-measurements-on-mars/" },
            new[] { "Dust changes the energy budget", "Predict whether a battery can last through a dusty period. Compare generation before and after cleaning deposited dust. Cleaning does not remove airborne dust, and a clean panel still produces no power at night.", "NASA Mars surface power study", "https://www.nasa.gov/wp-content/uploads/2024/01/mars-surface-power-generation-challenges-and-considerations.pdf" },
            new[] { "Rest and exercise are investments", "Compare task efficiency when tired and after rest. Exercise costs supplies and briefly adds fatigue while supporting longer-term fitness. It is not an instant cure for oxygen shortage or injury.", "NASA exercise overview", "https://www.nasa.gov/ochmo-tb-031-exercise-overview-2/" },
        };

        private static readonly string[][] Sources =
        {
            new[] { "Curiosity RAD", "Daily galactic cosmic ray dose at Gale Crater (about 210 microgray per day) sets the background radiation for every sol.", "Curiosity RAD", "https://science.nasa.gov/resource/radiation-measurements-on-mars/" },
            new[] { "NASA DONKI", "Space weather flare records set each sol's flare level, which scales simulated solar particle events. Not a forecast.", "NASA DONKI", "https://ccmc.gsfc.nasa.gov/tools/DONKI/" },
            new[] { "Curiosity REMS / InSight", "The REMS sol 224 measurements give the shape of the day; the schedule's seasonal minimum and maximum set its range.", "REMS data", "https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/curiosity/rems.html" },
            new[] { "NASA-STD-3001 Vol. 1", "The career radiation limit (600 mSv, scaled for play) is the dose budget behind the Sun-Zap meter and radiation health damage.", "NASA-STD-3001", "https://www.nasa.gov/reference/4-0-human-performance/" },
            new[] { "KSC Veggie / CEA", "Veggie crop identities and per-plant water estimates (provisional) set the leafy greenhouse bed's water use.", "Veggie fact sheet", "https://www.nasa.gov/wp-content/uploads/2019/04/veggie_fact_sheet_508.pdf" },
            new[] { "NASA Mars24", "Sun position, local solar time and Sun distance for day, night and solar power.", "Mars24 algorithm", "https://www.giss.nasa.gov/tools/mars24/help/algorithm.html" },
            new[] { "USGS MOLA-HRSC terrain", "Terrain comes from the bundled USGS MOLA–HRSC elevation product, sampled on a 200 m grid. Smaller rocks, science caches, radio coverage and mission markers are gameplay objects. F3 shows the terrain source and scale.", "NASA Mars Trek", "https://trek.nasa.gov/mars/" },
        };

        private void BuildLearningWindow()
        {
            RectTransform card = Card();
            LiveLabel(card, () => State.tutorialStep < 0 ? "Guided objectives are off." : "Guided objective " + Math.Min(10, State.tutorialStep + 1) + " / 10:  " + TutorialObjective(State), 20);
            RectTransform row = Row(card, 42);
            SmallButton(row, "Start over", () => State.tutorialStep = 0);
            SmallButton(row, "Turn off", () => State.tutorialStep = -1);

            Heading("TRY IT YOURSELF");
            List<RectTransform> cells = Grid(Lessons.Length, 3, 104);
            for (int i = 0; i < Lessons.Length; i++)
            {
                string key = "L" + i;
                RectTransform t = Tile(cells[i], (i + 1) + ".  " + Lessons[i][0], () => Open(LearningView, key));
                LabelInFlow(t, "Predict, change one thing, observe", 16, muted);
            }

            Heading("WHERE THE NUMBERS COME FROM");
            LiveParagraph(() => "Temperature: " + (mission.Environment.temperatureSource ?? "loading") +
                "\nRadiation: " + (mission.Environment.radiationSource ?? "loading") +
                "\nEvents: " + (mission.Environment.eventSource ?? "loading") +
                "\nSchedule: " + MissionDataSources.ScheduleSourceLabel + "   ·   Crops: " + MissionDataSources.CropSourceLabel +
                string.Format(CultureInfo.InvariantCulture, "\nDose budget used: {0:0.0}% of {1:0} mSv ({2:0.0} mSv so far)",
                    SurvivalNeeds.DoseBudgetFraction(State.astronaut) * 100, Ui.H_limit, SurvivalNeeds.MilliSievert(State.astronaut.doseMicrogray)), 17);
            cells = Grid(Sources.Length, 4, 96);
            for (int i = 0; i < Sources.Length; i++)
            {
                string key = "S" + i;
                RectTransform t = Tile(cells[i], Sources[i][0], () => Open(LearningView, key));
                LabelInFlow(t, "Data source", 16, muted);
            }
        }

        private void BuildLearningDetail(string key)
        {
            string[][] list = key[0] == 'L' ? Lessons : Sources;
            int index;
            if (!int.TryParse(key.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0 || index >= list.Length)
            {
                detailId = null; BuildLearningWindow(); return;
            }
            string[] item = list[index];
            consoleTitle.text = item[0];
            RectTransform card = Card();
            LabelInFlow(card, item[1], 22, paper);
            LabelInFlow(card, "Source: " + item[2], 18, muted);
            RectTransform row = Row(card, 48);
            SmallButton(row, "Open the source in your browser", () => Application.OpenURL(item[3]));
        }

        private void BuildSettings()
        {
            RectTransform game = Card();
            LabelInFlow(game, "Mission", 22, accent);
            RectTransform row = Row(game, 46);
            SmallButton(row, "Save  (F5)", () => mission.SaveMission());
            SmallButton(row, "Load  (F9)", () => mission.LoadMission());
            SmallButton(row, "Restart this mission", () => mission.Restart());

            List<RectTransform> cells = Grid(2, 2, 104);
            RectTransform motion = Tile(cells[0], "Reduced weather motion", () => mission.ReducedMotion = !mission.ReducedMotion);
            LiveLabel(motion, () => mission.ReducedMotion ? "On   ·   click to turn off" : "Off   ·   click to turn on", 20, () => mission.ReducedMotion ? Safe : paper);
            RectTransform scaleTile = Tile(cells[1], "Interface scale", null);
            Slider(scaleTile, (uiScale - 0.8f) / 0.4f, value =>
            {
                uiScale = Mathf.Lerp(0.8f, 1.2f, value);
                scaler.referenceResolution = new Vector2(Ui.UiWidth / uiScale, Ui.UiHeight / uiScale);
            });

            List<RectTransform> cameraCells = Grid(2, 2, 132);
            RectTransform sensitivityTile = Tile(cameraCells[0], "Camera sensitivity", null);
            LiveLabel(sensitivityTile, () => MarsTrainer.UI.CameraPrefs.SensitivityText + "x   ·   drag to change", 20, () => paper);
            const float minSens = MarsTrainer.UI.CameraPrefs.MinSensitivity, maxSens = MarsTrainer.UI.CameraPrefs.MaxSensitivity;
            Slider(sensitivityTile, Mathf.InverseLerp(minSens, maxSens, MarsTrainer.UI.CameraPrefs.Sensitivity),
                value => MarsTrainer.UI.CameraPrefs.Sensitivity = Mathf.Lerp(minSens, maxSens, value));
            RectTransform shiftLockTile = Tile(cameraCells[1], "Shift Lock Switch (Ctrl toggles)",
                () => MarsTrainer.UI.CameraPrefs.ShiftLockSwitch = !MarsTrainer.UI.CameraPrefs.ShiftLockSwitch);
            LiveLabel(shiftLockTile, () => !MarsTrainer.UI.CameraPrefs.ShiftLockSwitch ? "Off   ·   click to allow shift lock"
                : (MarsTrainer.UI.CameraPrefs.ShiftLockOn ? "On, locked   ·   Ctrl frees the mouse" : "On, mouse free   ·   Ctrl locks it"),
                20, () => MarsTrainer.UI.CameraPrefs.ShiftLockSwitch ? Safe : paper);

            RectTransform sound = Card();
            LabelInFlow(sound, "Sound and text size", 22, accent);
            MarsTrainer.UI.MissionSettings.Create(sound);

            RectTransform keys = Card();
            LabelInFlow(keys, "Controls", 22, accent);
            LabelInFlow(keys, "W A S D: move · Mouse: look · C: wardrobe · Scroll: zoom · Ctrl: shift lock · Right mouse: turn camera · Shift: sprint · Space: jump / rover brake · E: pick up, board or leave rover\n1-9, 0: tools · Click / R: use tool · X: stop task · F / G: eat / drink\nTab: console · Esc: back / menu · H: help · P: pause · F5 / F9: save / load · F3: terrain sources", 18, paper);

            RectTransform hardware = Card();
            LabelInFlow(hardware, "Three-knob dashboard (Arduino)", 22, accent);
            LabelInFlow(hardware, "Arduino Uno over USB serial (115200 baud, COM7 when present). Left knob: Life control. Middle: Temperature. Right: Radiation shielding. All three start fully counter-clockwise (no power); turn clockwise for more power. " +
                      "Each LED group is green when the need is met, yellow when 1-15% short and red beyond that; the buzzer sounds on red and on warnings. " +
                      "The needle shows stored battery energy. Close the Arduino Serial Monitor before playing.", 17, muted);
            LiveLabel(hardware, () => "Serial link: " + (mission.Arduino == null ? "not created" : mission.Arduino.Status), 18);
            LiveLabel(hardware, NeedleText, 18);
            LiveLabel(hardware, () => mission.Dashboard == null ? "Software controls active." : mission.Dashboard.Status, 18);
            if (mission.Dashboard == null) return;
            row = Row(hardware, 46);
            SmallButton(row, "Start software emulator", () => { mission.Dashboard.BeginEmulator(); RebuildPanel(); });
            SmallButton(row, "Disconnect", () => { mission.Dashboard.Disconnect(); RebuildPanel(); });
            if (!mission.Dashboard.EmulatorActive) return;
            LabelInFlow(hardware, "These stand in for the physical knobs. Move each to its saved setting to take control.", 17, muted);
            double[] positions = { State.knobs[0], State.knobs[1], State.knobs[2] };
            for (int i = 0; i < 3; i++)
            {
                int channel = i;
                LabelInFlow(hardware, ((PowerChannel)i).ToString(), 18, paper);
                Slider(hardware, (float)positions[i], value =>
                {
                    positions[channel] = value;
                    mission.Dashboard.FeedNormalized(positions[0], positions[1], positions[2]);
                });
            }
            live.Add(() => mission.Dashboard.FeedNormalized(positions[0], positions[1], positions[2]));
            LiveLabel(hardware, () => "Waiting for takeover: " +
                (mission.Dashboard.AwaitingTakeover[0] ? "Life control " : "") +
                (mission.Dashboard.AwaitingTakeover[1] ? "Temperature " : "") +
                (mission.Dashboard.AwaitingTakeover[2] ? "Radiation shielding" : ""), 17);
        }

        // ------------------------------------------------------------------ console display pieces

        private Color Safe { get { return StatBar.ColorOf(Ui.UiSafe); } }
        private Color Warn { get { return StatBar.ColorOf(Ui.UiWarn); } }
        private Color Danger { get { return StatBar.ColorOf(Ui.UiDanger); } }

        private double TaskFraction()
        {
            return State.task.kind == TaskKind.None ? 0 : State.task.progressSeconds / Math.Max(1, State.task.durationSeconds);
        }

        private double BatteryFraction() { return State.resources.batteryKwh / Math.Max(0.001, mission.Simulation.BatteryCapacity); }

        private double Vital(int index)
        {
            AstronautState a = State.astronaut;
            double v = index == 0 ? a.oxygen : index == 1 ? a.health : index == 2 ? a.hydration : index == 3 ? a.satiety : 100 - a.fatigue;
            return Math.Max(0, Math.Min(100, v));
        }

        private double ChannelRatio(int channel)
        {
            PowerSnapshot p = mission.Simulation.Power;
            return p.neededKw[channel] <= 0.001 ? 1 : p.deliveredKw[channel] / p.neededKw[channel];
        }

        private string ChannelWord(int channel)
        {
            PowerSnapshot p = mission.Simulation.Power;
            if (p.deliveredKw[channel] + 0.01 < p.neededKw[channel]) return "short";
            if (p.deliveredKw[channel] > p.neededKw[channel] + 0.3) return "extra draw";
            return "ok";
        }

        private Color ChannelColor(int channel)
        {
            string word = ChannelWord(channel);
            return word == "short" ? Danger : word == "extra draw" ? Warn : Safe;
        }

        private static string CropWord(CropState crop)
        {
            return crop.ready ? "ready" : crop.planted ? Percent(crop.growth) : "empty";
        }

        private Color CropColor(CropState crop)
        {
            return crop.ready ? Safe : crop.stress > 0.3 ? Warn : StatBar.ColorOf(Ui.UiFood);
        }

        private Color ConditionColor(double condition)
        {
            return condition < 0.5 ? Danger : condition < 0.8 ? Warn : Safe;
        }

        private Color ResearchColor(ResearchNode node)
        {
            if (State.installed.Contains(node.id)) return Safe;
            if (State.researched.Contains(node.id) || CanResearch(node)) return accent;
            return muted;
        }

        private static string Percent(double fraction)
        {
            return (Math.Max(0, fraction) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Equal-width cells in rows of <paramref name="columns"/>; short last rows keep the same widths.</summary>
        private List<RectTransform> Grid(int count, int columns, float height)
        {
            List<RectTransform> cells = new List<RectTransform>();
            for (int i = 0; i < count; i += columns)
            {
                RectTransform row = Row(content, height);
                for (int j = 0; j < columns; j++)
                {
                    RectTransform cell = NewRect(i + j < count ? "Cell" : "Spacer", row);
                    cell.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                    if (i + j < count) cells.Add(cell);
                }
            }
            return cells;
        }

        /// <summary>A live display card filling a grid cell. With an action, the whole card is clickable.</summary>
        private RectTransform Tile(RectTransform cell, string title, UnityAction action)
        {
            // No box per tile: a faint smoky wash (it brightens on hover) under a steel rule.
            RectTransform tile = NewRect("Tile", cell);
            Image wash = tile.gameObject.AddComponent<Image>();
            wash.color = WithAlpha(MarsTrainer.UI.UiTheme.Panel, 0.28f);
            wash.raycastTarget = false;
            Stretch(tile);
            Rule(tile);
            VerticalLayoutGroup layout = tile.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 12);
            layout.spacing = 4;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            TMP_Text heading = LabelInFlow(tile, action == null ? title : title + "   ›", 20, accent);
            heading.enableWordWrapping = false;
            heading.overflowMode = TextOverflowModes.Ellipsis;
            if (action == null) return tile;
            Image image = tile.GetComponent<Image>();
            image.raycastTarget = true;
            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = StatBar.UiStates;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(action);
            controls.Add(button);
            return tile;
        }

        /// <summary>A card that does something when clicked, with a live line about what it uses.</summary>
        private void ActionTile(RectTransform cell, string title, Func<string> info, UnityAction action)
        {
            RectTransform t = Tile(cell, title, action);
            LiveLabel(t, info, 17, null, muted);
        }

        private void InfoTile(RectTransform cell, string title, string body)
        {
            RectTransform t = Tile(cell, title, null);
            LabelInFlow(t, body, 17, muted);
        }

        /// <summary>One field-science step: done or still to do, and the tool that does it.</summary>
        private void StepTile(RectTransform cell, string title, string how, Func<bool> done, UnityAction action)
        {
            RectTransform t = Tile(cell, title, action);
            LiveLabel(t, () => done() ? "Done" : "To do   ·   " + how, 17, () => done() ? Safe : paper);
        }

        /// <summary>A toggle that shows its state; lit while on.</summary>
        private void Switch(Transform parent, Func<string> label, Func<bool> on, UnityAction toggle)
        {
            Button button = SmallButton(parent, "", toggle);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            Image image = button.GetComponent<Image>();
            Color off = image.color;
            live.Add(() =>
            {
                text.text = label();
                image.color = on() ? StatBar.ColorOf(Ui.UiButtonFocus) : off;
                text.color = on() ? paper : muted;
            });
        }

        /// <summary>A labelled bar that follows a live value. Inline puts the label left of the bar.</summary>
        private void FlowMeter(Transform parent, Func<string> label, Func<double> value, Func<Color> color, bool inline, float size)
        {
            RectTransform row = NewRect("Meter", parent);
            bool hasLabel = size > 5;
            Height(row, inline ? size + 10 : hasLabel ? size + 22 : 12);
            TMP_Text text = null;
            if (hasLabel)
            {
                text = Label(row, "", size, paper, true);
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                if (inline) Place(text.rectTransform, 0, 0, .52f, 1);
                else Place(text.rectTransform, 0, .4f, 1, 1);
            }
            Image fill = inline ? Bar(row, .54f, .3f, 1, .62f, paper) : hasLabel ? Bar(row, 0, 0, 1, .26f, paper) : Bar(row, 0, 0, 1, 1, paper);
            fill.transform.parent.GetComponent<Image>().raycastTarget = false;
            live.Add(() =>
            {
                if (text != null) text.text = label();
                SetBar(fill, value());
                fill.color = color();
            });
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
            return deficit > 0.01 ? string.Format(CultureInfo.InvariantCulture, "{0:0.0} h left at this draw", State.resources.batteryKwh / deficit) : "charging or balanced";
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
            if (s.task.kind == TaskKind.Rest && s.task.targetId == "sleep") return "Sleeping until 07:00...";
            if (s.task.kind == TaskKind.Transmit) return "Transmitting to Houston. Stand still until it is sent.";
            if (outside && s.resources.suitOxygenGrams < 300)
                return "Suit oxygen is low. Walk back to the airlock now (follow the ramp to the white door).";
            if (env.solarEvent > 0)
                return "Solar storm! Go to the storm shelter (far west end of the base) and wait there (Tab > Wait 1 hour).";
            if (outside && env.dust > 0.3)
                return "Dust storm. Return inside; dust the panels when it clears.";
            for (int i = 0; i < 3; i++)
                if (sim.Power.neededKw[i] > 0 && sim.Power.deliveredKw[i] + 0.01 < sim.Power.neededKw[i])
                    return "Not enough power for " + (i == 0 ? "life control" : i == 1 ? "temperature" : "radiation shielding") +
                           ". Tab > Power: drag that knob until its bar is full.";
            if (s.resources.batteryKwh < sim.BatteryCapacity * 0.15 && sim.Power.netKw < 0)
                return "Battery low. Lower sliders showing EXTRA DRAW, switch off rover/field charging, and wait for daylight.";
            if (s.astronaut.hydration < 30) return "You are very thirsty. Go to the galley (command hub) and press E at the water dispenser.";
            if (s.astronaut.satiety < 30) return "You are very hungry. Go to the galley (command hub) and press E at the food locker.";
            if (s.astronaut.health < 50)
                return "Health is low. Lie on the treatment bed in the medical bay (E).";
            if (s.astronaut.fatigue > 85) return "You are exhausted. Sleep in a bunk in the crew quarters (E).";
            if (outside && s.resources.suitOxygenGrams < 500)
                return "Suit oxygen under half. Finish up and plan your walk back.";
            if (outside && env.solarFraction <= 0.01) return "It is night: cold and no solar power. Go back inside.";
            List<PlanStep> steps = MissionDailyPlan.Steps(mission);
            int current = MissionDailyPlan.Current(steps, sim.LocalHour);
            return current < 0 ? NextObjective(s) : steps[current].title + ": " + steps[current].detail;
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
                "Use the three power knobs to meet the useful need. Compare extra draw before advancing.",
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
            detailId = null;
            mission.SetPanelOpen(true);
            RebuildPanel();
        }

        /// <summary>How to complete each daily task: where to go, which tools to carry, which button to press.</summary>
        private void BuildHelp()
        {
            Heading("YOUR DAY ON MARS");
            Paragraph("Each sol lasts about 15 minutes and runs from 07:00 to bedtime. The plan at the top right lists the jobs in order and an orange marker shows where to go:", 21);
            Paragraph("1.  Breakfast: galley in the command hub, E at the food locker and the water dispenser.\n" +
                      "2.  Check the power: Tab > Power, fill each bar without extra draw.\n" +
                      "3.  Greenhouse: sow empty beds, water growing beds, pick ripe plants.\n" +
                      "4.  Maintenance: fix today's sparking fault, sweep dusty panels.\n" +
                      "5.  Field trip: survey and core a science site, analyse the core at the lab.\n" +
                      "6.  Report to Houston with the Radio.\n" +
                      "7.  Dinner at the galley, then 8. sleep in a bunk (after 17:00). You wake at 07:00.", 20);
            Paragraph("Tools: look at a tool on its rack and press E to put it in your hotbar (keys 1-9, 0). Q puts the selected tool back, B returns them all. Select a tool and click to use it; most jobs are a short challenge (timing, holding, aiming). X stops one.", 20);
            if (mission != null && mission.Tools != null && mission.Simulation != null)
                LiveParagraph(() => "In your hotbar: " + mission.Tools.CarriedSummary(), 21);
            if (mission != null && mission.Simulation == null)
            {
                Paragraph("First choose a mission length.", 21);
                Button(content, "Choose a mission", () => { tab = Home; RebuildPanel(); });
            }

            Heading("GREENHOUSE");
            HelpCard("Sow a bed", "Greenhouse", "Shovel (9), kept in the greenhouse",
                "Aim at each empty soil spot and click: 12 spots per bed, one seed per bed. R switches between leafy (3 sols) and staple (6 sols) crops.");
            HelpCard("Water", "Greenhouse", "Sprayer (0), kept in the greenhouse",
                "Once a day per growing bed: hold the button to spray and let go while the gauge is in the green. A dry bed stops growing; too much water stresses the roots.");
            HelpCard("Harvest", "Greenhouse", "Your hands",
                "Ripe plants carry orange fruit. Look at each one and press E to pick it into the food store. The last one empties the bed and saves a seed.");

            Heading("MAINTENANCE");
            HelpCard("Fix the fault", "A ceiling box sparking somewhere in the base (follow the marker)", "Field Wrench (8), kept in the energy room",
                "Aim at the box and click when the needle is in the green, three times. Unfixed faults wear the base out fast.");
            HelpCard("Sweep the solar panels", "Solar array beside the energy room (outside)", "Panel Duster (7), kept in the energy room",
                "Hold the brush on each dusty panel until it is clean.");
            HelpCard("Repair the rover, install upgrades", "Beside the rover / energy room", "Field Wrench (8)",
                "The same three-bolt timing challenge.");

            Heading("FIELD SCIENCE (EVA)");
            HelpCard("Get ready", "Anywhere inside, then the lab", "Oxygen Canister (5), kept in life support, the medical bay or the lab",
                "Click the canister inside to fill it and your suit; on EVA, click to top your suit up. Then walk through the lab door into the airlock: the inner door closes before the outer one opens.");
            HelpCard("Find a site", "Outside", "Radio (4)",
                "Radio R (or a site on the map in Tab > Field sites) picks the nearest site with work left; follow its marker. Markers pop up within about 30 m.");
            HelpCard("Survey and mark", "Within 5 m of the site", "Rock Hammer (1), kept in the lab",
                "Strike the 3 glowing rocks around the marker to survey the site, then hammer in the survey marker with three timed strikes.");
            HelpCard("Core sample", "Within 5 m of the site", "Core Drill (2), kept in the lab",
                "Hold to drill and let go before the heat gauge reaches the red; overheating jams the bit.");
            HelpCard("Photograph", "Within 5 m of the site", "Survey Camera (3), kept in the lab",
                "Centre the survey marker in view and click. R zooms.");
            HelpCard("Analyze the sample", "EVA prep and sample lab", "Core Drill (2)",
                "At the lab bench, click each time the scan line crosses the green band (3 readings). Earns research points.");

            Heading("COMMUNICATIONS");
            HelpCard("Report to Houston", "Anywhere", "Radio (4), kept in the hub, shelter or lab",
                "Click to queue a status report, then click again to transmit during a usable radio window (or Tab > Radio).");
            Paragraph("The Work Lamp (6), kept in the quarters or shelter, lights your way at night: click to switch it on; it clips to your helmet when you change tools.", 20);
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

        // ------------------------------------------------------------------ MarsTrainer UI kit primitives

        private RectTransform Card()
        {
            // Sections are separated by a steel rule, not boxed in.
            RectTransform card = NewRect("Card", content);
            Rule(card);
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return card;
        }

        /// <summary>A thin steel rule along the top edge, kept out of layout.</summary>
        private static void Rule(RectTransform parent)
        {
            RectTransform rule = NewRect("Rule", parent);
            rule.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Anchor(rule, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero);
            Image image = rule.gameObject.AddComponent<Image>();
            image.color = MarsTrainer.UI.UiTheme.SteelMid;
            image.raycastTarget = false;
        }

        private void Heading(string value) { LabelInFlow(content, Sentence(value), 28, accent); }
        private void Paragraph(string value, int size = 23) { LabelInFlow(content, value, size, paper); }
        private void LiveParagraph(Func<string> value, int size = 23) { LiveLabel(content, value, size); }
        private void LiveLabel(Transform parent, Func<string> value, float size, Func<Color> color = null, Color? tint = null)
        {
            TMP_Text label = LabelInFlow(parent, "", size, tint ?? paper);
            if (color == null) live.Add(() => label.text = value());
            else live.Add(() => { label.text = value(); label.color = color(); });
        }

        /// <summary>Barlow Condensed is narrow, so HUD text is set a little larger than the old face for the same readability.</summary>
        private static float Size(float legacy) { return legacy * 1.15f; }

        private TMP_Text LabelInFlow(Transform parent, string value, float size, Color color)
        {
            TMP_Text label = Label(parent, value, size, color);
            label.gameObject.AddComponent<LayoutElement>().minHeight = Size(size) * 1.3f + 6;
            return label;
        }

        private TMP_Text Label(Transform parent, string value, float size, Color color, bool exact = false)
        {
            TextMeshProUGUI label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            bool readout = size < 28f && UiTypeface.LooksNumeric(value);
            label.font = size >= 28f ? UiTypeface.Strong : readout ? UiTypeface.Mono : font;
            if (readout) size *= UiTypeface.MonoScale;
            if (color == accent)
            {
                // Accent text is a section label: small tracked capitals in the semibold face.
                label.font = UiTypeface.Strong;
                label.fontStyle = FontStyles.UpperCase;
                label.characterSpacing = 6f;
                size *= 0.85f;
            }
            label.text = value;
            label.fontSize = exact ? size : Size(size);
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;
            label.lineSpacing = Ui.UiTextLineSpacing;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Text filling a HUD plate, inset by the kit gap.</summary>
        private TMP_Text Overlay(RectTransform parent, string value, float size)
        {
            TMP_Text text = Label(parent, value, size, paper);
            text.fontSize = size;
            text.margin = new Vector4(Ui.UiGap * 1.5f, 0, Ui.UiGap * 1.5f, 0);
            Stretch(text.rectTransform);
            return text;
        }

        private Button Button(Transform parent, string text, UnityAction action)
        {
            RectTransform rect = Box(text, parent, StatBar.ColorOf(Ui.UiButtonColor));
            Height(rect, 46);
            Image image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = StatBar.UiStates;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(action);
            TMP_Text label = Label(rect, text, 19, paper);
            label.alignment = TextAlignmentOptions.Center;
            label.margin = new Vector4(Ui.UiGap, 0, Ui.UiGap, 0);
            Stretch(label.rectTransform);
            controls.Add(button);
            return button;
        }

        private Button SmallButton(Transform parent, string text, UnityAction action)
        {
            Button button = Button(parent, text, action);
            button.GetComponent<LayoutElement>().flexibleWidth = 1;
            return button;
        }

        /// <summary>A small, quiet text button (key hints, eat/drink, OK).</summary>
        private Button HintButton(Transform parent, string text, UnityAction action)
        {
            RectTransform rect = NewRect(text, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = WithAlpha(ink, ChipAlpha);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = StatBar.UiStates;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            TMP_Text label = Label(rect, text, Tiny, paper, true);
            label.alignment = TextAlignmentOptions.Center;
            label.margin = new Vector4(10, 0, 10, 0);
            Stretch(label.rectTransform);
            LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = label.GetPreferredValues(text).x + 24;
            return button;
        }

        /// <summary>A translucent backing so small text stays readable over the bright ground.</summary>
        private RectTransform Plate(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            RectTransform rect = StatBar.Frame(parent, name, x0, y0, x1, y1);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = WithAlpha(ink, ChipAlpha);
            image.raycastTarget = false;
            return rect;
        }

        private TMP_Text Text(Transform parent, string value, float size, Color color, float x0, float y0, float x1, float y1)
        {
            TMP_Text text = Label(parent, value, size, color, true);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Place(text.rectTransform, x0, y0, x1, y1);
            return text;
        }

        /// <summary>A thin progress bar; returns the fill to resize with <see cref="SetBar"/>.</summary>
        private static Image Bar(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            RectTransform track = NewRect("Bar", parent);
            Place(track, x0, y0, x1, y1);
            track.gameObject.AddComponent<Image>().color = WithAlpha(MarsTrainer.UI.UiTheme.SteelDark, 0.85f);
            RectTransform fill = NewRect("Fill", track);
            Stretch(fill);
            Image image = fill.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A gauge in a small steel housing: frame, dark well, coloured fill (set with SetBar).</summary>
        private static Image SteelGauge(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            RectTransform housing = Box("Gauge", parent, StatBar.ColorOf(Ui.UiSurface));
            Place(housing, x0, y0, x1, y1);
            RectTransform well = NewRect("Well", housing);
            Anchor(well, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            RectTransform fill = NewRect("Fill", well);
            Stretch(fill);
            Image image = fill.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A dark outline so HUD text with no plate behind it stays readable over bright ground and sky.</summary>
        private static void Readable(TMP_Text text)
        {
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color32(10, 12, 14, 230);
        }

        private static void SetBar(Image fill, double fraction)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)fraction), 1);
        }

        private Slider Slider(Transform parent, float initial, UnityAction<float> changed)
        {
            RectTransform rect = NewRect("Power slider", parent);
            Height(rect, 40);
            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.wholeNumbers = false;
            RectTransform track = Box("Track", rect, StatBar.ColorOf(Ui.UiButtonColor));
            Anchor(track, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -12));
            RectTransform fillArea = NewRect("Fill area", rect);
            Anchor(fillArea, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -12));
            RectTransform fill = Box("Fill", fillArea, StatBar.ColorOf(Ui.UiSafe));
            Stretch(fill);
            RectTransform handleArea = NewRect("Handle area", rect);
            Anchor(handleArea, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0));
            RectTransform handle = Box("Handle", handleArea, paper);
            handle.sizeDelta = new Vector2(26, 38);
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
            layout.spacing = Ui.UiGap;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return row;
        }

        /// <summary>Sentence case: "KEEP THE OUTPOST WORKING" reads as "Keep the outpost working".</summary>
        private static string Sentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string lower = text.ToLowerInvariant().Replace("houston", "Houston").Replace("mars", "Mars").Replace(" sol", " sol");
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
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
            FacetedImage image = rect.gameObject.AddComponent<FacetedImage>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
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
