using System;
using System.Collections.Generic;
using System.Globalization;
using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Hardware;
using MarsSurvival.Simulation;
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
    /// The mission HUD and outpost console, drawn with the MarsTrainer UI kit in the Electrolize
    /// typeface. The HUD stays minimal: a small sol / battery / radiation block top left, quiet key
    /// hints top right, one warning at a time, suit vitals (oxygen first) that brighten only when
    /// low, small eat/drink buttons and one fading message line. The console (Tab) holds the three
    /// power knobs, the daily task routine, research, field log, radio, learning sources, help and
    /// settings; Escape opens the pause menu and a report closes each mission.
    /// </summary>
    public sealed class MarsMissionHud : MonoBehaviour
    {
        private static readonly string[] TabNames = { "Overview", "Base tasks", "Research", "Field log", "Radio", "Learning", "Help", "Settings" };
        private const int HelpTab = 6;
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
        private RectTransform panel, content, selector, gameplay, alertPlate;
        private TMP_Text solText, navigation, notice, guide, batteryText, radiationText, alertText;
        private Image batteryFill, radiationFill, noticePlate;
        private TMP_Text[] vitalLabels;
        private Image[] vitalFills;
        private CanvasGroup[] vitalGroups;
        private PauseMenu pause;
        private EndScreen end;
        private Button eatButton, drinkButton;
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
            bool visible = mission != null && mission.Ready && !MarsHomeScreen.IsOpen;
            canvas.enabled = visible;
            if (!visible) return;

            HandleKeys();
            MissionState state = mission.Simulation == null ? null : mission.Simulation.State;
            if (state == null && !mission.PanelOpen && !ScreenOpen) mission.SetPanelOpen(true);
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
            gameplay.gameObject.SetActive(state != null);
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Ui.UiRefreshSeconds;
                RefreshShell();
                if (mission.PanelOpen)
                {
                    if (state != null && tab == 4 && drawnReports != state.reports.Count) RebuildPanel();
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
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (pause.gameObject.activeSelf) pause.Close();
                else if (end.gameObject.activeSelf) { }
                else if (mission.PanelOpen && mission.IsRunning) mission.SetPanelOpen(false);
                else if (mission.IsRunning) OpenPause();
                else mission.SetPanelOpen(true);
                return;
            }
            if (ScreenOpen) return;
            if (Input.GetKeyDown(KeyCode.Tab)) TogglePanel();
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (mission.PanelOpen && tab == HelpTab && mission.IsRunning) mission.SetPanelOpen(false);
                else OpenHelp();
            }
            if (Input.GetKeyDown(KeyCode.P) && mission.IsRunning) mission.SetPaused(!mission.Paused);
            if (Input.GetKeyDown(KeyCode.F) && mission.IsRunning && !mission.PanelOpen) mission.Execute("eat");
            if (Input.GetKeyDown(KeyCode.G) && mission.IsRunning && !mission.PanelOpen) mission.Execute("drink");
            // X stops the task in progress (it would otherwise wait for you to come back and stand still).
            if (Input.GetKeyDown(KeyCode.X) && mission.IsRunning && mission.Simulation.State.task.kind != TaskKind.None) mission.Execute("cancel");
            if (Input.GetKeyDown(KeyCode.F5)) mission.SaveMission();
            if (Input.GetKeyDown(KeyCode.F9)) mission.LoadMission();
#endif
        }

        // ------------------------------------------------------------------ shell

        // Minimal HUD: only what matters now, small and quiet until it needs attention.
        private const float Small = 17f, Tiny = 15f;

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

            // Bottom left: suit vitals, oxygen first. Quiet while fine, bright when low.
            RectTransform vitalsPlate = Plate(gameplay, "Suit vitals", .010f, .015f, .160f, .205f);
            string[] labels = { "Oxygen", "Health", "Water", "Food", "Rest" };
            ResourceSymbol[] symbols = { ResourceSymbol.Air, ResourceSymbol.Health, ResourceSymbol.Water, ResourceSymbol.Food, ResourceSymbol.Rest };
            vitalLabels = new TMP_Text[5]; vitalFills = new Image[5]; vitalGroups = new CanvasGroup[5];
            for (int i = 0; i < 5; i++)
            {
                float top = 1 - i * .2f, bottom = top - .2f;
                RectTransform row = StatBar.Frame(vitalsPlate, labels[i], .03f, bottom, .97f, top);
                vitalGroups[i] = row.gameObject.AddComponent<CanvasGroup>();
                ResourceGlyph icon = ResourceGlyph.Create(row, symbols[i], 24);
                icon.rectTransform.anchoredPosition = new Vector2(2, 0);
                vitalLabels[i] = Text(row, labels[i], Tiny, paper, .22f, .38f, 1, 1);
                vitalFills[i] = Bar(row, .22f, .18f, .98f, .30f, StatBar.ColorOf(ResourceGlyph.ColorFor(symbols[i])));
            }

            // Bottom right: small eat and drink buttons.
            eatButton = HintButton(gameplay, "F  Eat", () => { if (mission != null) mission.Execute("eat"); });
            Place((RectTransform)eatButton.transform, .875f, .02f, .93f, .06f);
            drinkButton = HintButton(gameplay, "G  Drink", () => { if (mission != null) mission.Execute("drink"); });
            Place((RectTransform)drinkButton.transform, .935f, .02f, .99f, .06f);

            // Above the tool hotbar: the latest message, which fades into the next step.
            noticePlate = Plate(gameplay, "Message", .30f, .215f, .70f, .25f).GetComponent<Image>();
            notice = Text(noticePlate.rectTransform, "", Small, paper, .02f, 0, .98f, 1);
            notice.alignment = TextAlignmentOptions.Center;
            guide = Text(noticePlate.rectTransform, "", Small, accent, .02f, 0, .98f, 1);
            guide.alignment = TextAlignmentOptions.Center;

            // The outpost console.
            panel = StatBar.Frame(root.transform, "Outpost console", .045f, .14f, .955f, .90f, Ui.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            RectTransform titleRow = Row(panel, 52);
            Anchor(titleRow, new Vector2(0, 1), Vector2.one, new Vector2(26, -68), new Vector2(-26, -16));
            TMP_Text title = Label(titleRow, "Outpost console", 30, accent);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 3;
            SmallButton(titleRow, "Back to Mars (Tab)", () => { if (mission != null && mission.IsRunning) mission.SetPanelOpen(false); });

            selector = Row(panel, 46);
            Anchor(selector, new Vector2(0, 1), Vector2.one, new Vector2(26, -124), new Vector2(-26, -78));
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                SmallButton(selector, TabNames[i], () => { tab = index; RebuildPanel(); });
            }

            RectTransform scrollRoot = NewRect("Scrollable content", panel);
            Anchor(scrollRoot, Vector2.zero, Vector2.one, new Vector2(26, 22), new Vector2(-26, -138));
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
            end.ReviewRequested = () => { tab = 0; mission.SetPanelOpen(true); RebuildPanel(); };
            built = true;
            panel.gameObject.SetActive(false);
        }

        private void RefreshShell()
        {
            MissionSimulation simulation = mission.Simulation;
            if (simulation == null) return;
            MissionState s = simulation.State;
            double duration = MissionSettings.Duration(s.length);
            solText.text = string.Format(CultureInfo.InvariantCulture, "Sol {0:0.0} / {1:0}{2}",
                1 + s.elapsedSeconds / MissionSettings.SolSeconds, duration / MissionSettings.SolSeconds,
                mission.Paused ? "  paused" : mission.AdvanceRemaining > 0 ? "  advancing" : "");
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
                vitalLabels[i].text = labels[i] + "  " + v.ToString("0", CultureInfo.InvariantCulture) + "%";
                SetBar(vitalFills[i], v / 100);
                bool low = v < 40;
                vitalLabels[i].color = v < 20 ? StatBar.ColorOf(Ui.UiDanger) : low ? StatBar.ColorOf(Ui.UiWarn) : paper;
                vitalGroups[i].alpha = low ? 1f : 0.62f;
            }
            eatButton.interactable = a.satiety <= 95 && s.resources.food >= 1;
            drinkButton.interactable = a.hydration <= 95 && s.resources.cleanWaterLiters >= SurvivalNeeds.DrinkLitres;

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
                TMP_Text readout = LabelInFlow(card, "", 22, paper);
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
                    SmallButton(row, site.name + (site.analyzed ? " (done)" : ""), () => { mission.SelectSite(site.id); RebuildPanel(); });
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
            TMP_Text legend = Label(map, "N ↑     ● You     ■ Habitat     Numbered sites: select to navigate", 18, muted);
            Anchor(legend.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -32), new Vector2(-12, -4));
            TMP_Text baseMarker = Label(map, "■ HOME", 19, paper);
            MapPosition(baseMarker.rectTransform, project(home.x, home.z), new Vector2(110, 26));
            for (int i = 0; i < State.sites.Count; i++)
            {
                SiteState site = State.sites[i];
                Button point = Button(map, (i + 1).ToString(CultureInfo.InvariantCulture), () => { mission.SelectSite(site.id); RebuildPanel(); });
                MapPosition((RectTransform)point.transform, project(site.x, site.z), new Vector2(38, 34));
                point.GetComponent<Image>().color = site.id == mission.SelectedSiteId ? accent : MarsPalette.Mauve;
            }
            TMP_Text player = Label(map, "●", 27, accent);
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

            Heading("DATA SOURCES");
            Paragraph("The seasonal schedule (StreamingAssets/mars_year_schedule.json, 668 sols) and crop table (crop_parameters.json) were prepared offline by tools/build_schedule.py and tools/build_crops.py. Rows built without a live feed are marked synthetic and are seeded from the published references below. Nothing is downloaded while you play.", 20);
            LiveParagraph(() => "Schedule: " + MissionDataSources.ScheduleSourceLabel + "\nCrops: " + MissionDataSources.CropSourceLabel +
                string.Format(CultureInfo.InvariantCulture, " - leafy bed uses {0:0.00} L per sol", mission.Simulation.Settings.leafyWaterLitersPerSol) +
                string.Format(CultureInfo.InvariantCulture, "\nDose budget used: {0:0.0}% of {1:0} mSv ({2:0.0} mSv so far)",
                    SurvivalNeeds.DoseBudgetFraction(State.astronaut) * 100, Ui.H_limit, SurvivalNeeds.MilliSievert(State.astronaut.doseMicrogray)), 20);
            SourceCard("Curiosity RAD", "Daily galactic cosmic ray dose at Gale Crater (about 210 microgray per day) sets the background radiation for every sol.",
                "https://science.nasa.gov/resource/radiation-measurements-on-mars/");
            SourceCard("NASA DONKI", "Space weather flare records set each sol's flare level, which scales simulated solar particle events. Not a forecast.",
                "https://ccmc.gsfc.nasa.gov/tools/DONKI/");
            SourceCard("Curiosity REMS / InSight", "The REMS sol 224 measurements give the shape of the day; the schedule's seasonal minimum and maximum set its range.",
                "https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/curiosity/rems.html");
            SourceCard("NASA-STD-3001 Vol. 1", "The career radiation limit (600 mSv, scaled for play) is the dose budget behind the Sun-Zap meter and radiation health damage.",
                "https://www.nasa.gov/reference/4-0-human-performance/");
            SourceCard("KSC Veggie / CEA", "Veggie crop identities and per-plant water estimates (provisional) set the leafy greenhouse bed's water use.",
                "https://www.nasa.gov/wp-content/uploads/2019/04/veggie_fact_sheet_508.pdf");
            SourceCard("NASA Mars24", "Sun position, local solar time and Sun distance for day, night and solar power.",
                "https://www.giss.nasa.gov/tools/mars24/help/algorithm.html");
        }

        private void SourceCard(string title, string body, string url)
        {
            RectTransform card = Card();
            LabelInFlow(card, title, 25, accent);
            LabelInFlow(card, body, 20, paper);
            Button(card, "Open: " + title, () => Application.OpenURL(url));
        }

        private void LearningCard(string title, string body, string sourceName, string url)
        {
            RectTransform card = Card();
            LabelInFlow(card, title, 25, accent);
            LabelInFlow(card, body, 21, paper);
            Button(card, "Open: " + sourceName, () => Application.OpenURL(url));
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
                scaler.referenceResolution = new Vector2(Ui.UiWidth / uiScale, Ui.UiHeight / uiScale);
            });
            Paragraph("Sound and text size", 21);
            MarsTrainer.UI.MissionSettings.Create(content);
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
                return "Health is low. Go to the medical bay, then Tab > Base tasks > Medical treatment.";
            if (s.astronaut.fatigue > 70) return "You are exhausted. Inside, Tab > Base tasks > Rest (6 h).";
            if (outside && s.resources.suitOxygenGrams < 500)
                return "Suit oxygen under half. Finish up and plan your walk back.";
            foreach (CropState crop in s.crops)
                if (crop.ready) return "A crop is ready. In the greenhouse, select the Sprayer (0) and click to harvest.";
            foreach (CropState crop in s.crops)
                if (!crop.planted && s.resources.seeds >= 1)
                    return "Bed " + (crop.bed + 1) + " is empty. Pick up the Shovel in the greenhouse (E), select it (9) and click to plant.";
            if (s.equipment.baseCondition < 0.8)
                return "The base is wearing out. Take the Field Wrench from the energy room (E), select it (8) and click inside to repair.";
            if (s.equipment.panelDust > 0.15)
                return "Solar panels are dusty. Take the Panel Duster from the energy room (E) out to the array, select it (7) and click.";
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
            Paragraph("1.  Fill your hotbar.  Look at a tool on its rack in the base and press E: it goes into its hotbar slot (keys 1-9, 0). Inside the base, Q puts the selected tool back and B returns them all.\n" +
                      "2.  Go to the right place.  Tasks only start at their station (the top bar names the room you are in). Stand still.\n" +
                      "3.  Use the tool.  Select it with its number key and left-click; R is its second action. The task then runs while you stand still (moving pauses it). The console (Tab) has the same tasks as buttons. Inside, + 1 hour waits faster.", 21);
            if (mission != null && mission.Tools != null && mission.Simulation != null)
                LiveParagraph(() => "In your hotbar: " + mission.Tools.CarriedSummary(), 21);
            if (mission != null && mission.Simulation == null)
            {
                Paragraph("First choose a mission length.", 21);
                Button(content, "Choose a mission", () => { tab = 0; RebuildPanel(); });
            }

            Heading("EVERY SOL");
            HelpCard("Balance power", "Overview tab", "none",
                "Move the three sliders until each channel's Delivered matches its Useful need. Shortfalls harm the astronaut; extra draw wastes battery.");
            HelpCard("Eat and drink", "Anywhere inside (the galley is in the command hub)", "none",
                "Press F to eat and G to drink (or Base tasks > Eat, Drink). Keep Food and Water up. The daily routine does this for you while time advances.");
            HelpCard("Rest and exercise", "Anywhere inside (crew quarters has the bunks)", "none",
                "Base tasks > Rest (6 h) lowers fatigue; Exercise (30 min) keeps fitness up.");
            HelpCard("Medical treatment", "Medical bay", "none",
                "Base tasks > Medical treatment when health drops. Uses one medical supply.");

            Heading("GREENHOUSE");
            HelpCard("Plant a bed", "Greenhouse", "Shovel (9), kept in the greenhouse",
                "Click to plant an empty bed; R switches between a leafy (fast) and staple (filling) crop. Keep greenhouse power ON.");
            HelpCard("Harvest and water", "Greenhouse", "Sprayer (0), kept in the greenhouse",
                "Click when a bed is ready to harvest. On a stressed bed, click to water it (1 L) so it recovers.");

            Heading("MAINTENANCE");
            HelpCard("Repair the base", "Inside the habitat", "Field Wrench (8), kept in the energy room",
                "Click inside the base when base condition falls. Uses 2 parts.");
            HelpCard("Dust the solar panels", "Solar array beside the energy room (outside)", "Panel Duster (7), kept in the energy room",
                "Click at the array when panel dust builds up. Go out through the airlock.");
            HelpCard("Repair the rover", "Beside the rover", "Field Wrench (8)",
                "Click beside the rover. Uses 2 parts.");
            HelpCard("Install research and equipment", "Energy room", "Field Wrench (8)",
                "Click in the energy room to install the next researched design (or Research tab > Install).");

            Heading("FIELD SCIENCE (EVA)");
            HelpCard("Get ready", "Anywhere inside, then the lab", "Oxygen Canister (5), kept in life support, the medical bay or the lab",
                "Click the canister inside to fill it and your suit; on EVA, click to top your suit up. Then walk through the lab door into the airlock: the inner door closes before the outer one opens.");
            HelpCard("Find a site", "Outside", "Radio (4)",
                "Radio R (or Field log) picks the nearest site with work left; follow its marker. Markers pop up within about 30 m.");
            HelpCard("Survey and mark", "Within 5 m of the site", "Rock Hammer (1), kept in the lab",
                "Click to survey the rocks, then click again to hammer in the survey marker. Measure is in the Field log.");
            HelpCard("Core sample", "Within 5 m of the site", "Core Drill (2), kept in the lab",
                "Click to drill a core sample.");
            HelpCard("Photograph", "Within 5 m of the site", "Survey Camera (3), kept in the lab",
                "Centre the survey marker in view and click. R zooms.");
            HelpCard("Analyze the sample", "EVA prep and sample lab", "Core Drill (2)",
                "Back inside at the lab bench, click to analyze the core. Earns research points.");

            Heading("COMMUNICATIONS");
            HelpCard("Report to Houston", "Anywhere", "Radio (4), kept in the hub, shelter or lab",
                "Click to queue a status report, then click again to transmit during a usable radio window (or use the Radio tab).");
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
            RectTransform card = Box("Card", content, StatBar.ColorOf(Ui.UiPanel));
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return card;
        }

        private void Heading(string value) { LabelInFlow(content, Sentence(value), 28, accent); }
        private void Paragraph(string value, int size = 23) { LabelInFlow(content, value, size, paper); }
        private void LiveParagraph(Func<string> value, int size = 23) { LiveLabel(content, value, size); }
        private void LiveLabel(Transform parent, Func<string> value, int size)
        {
            TMP_Text label = LabelInFlow(parent, "", size, paper);
            live.Add(() => label.text = value());
        }

        /// <summary>Console text sizes were authored for the legacy font; the UI kit's font reads best a little larger.</summary>
        private static float Size(float legacy) { return legacy; }

        private TMP_Text LabelInFlow(Transform parent, string value, float size, Color color)
        {
            TMP_Text label = Label(parent, value, size, color);
            label.gameObject.AddComponent<LayoutElement>().minHeight = Size(size) + 8;
            return label;
        }

        private TMP_Text Label(Transform parent, string value, float size, Color color, bool exact = false)
        {
            TextMeshProUGUI label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
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
            ColorBlock colors = button.colors;
            colors.highlightedColor = StatBar.ColorOf(Ui.UiButtonFocus);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = StatBar.ColorOf(Ui.UiCopper);
            colors.disabledColor = new Color(0.55f, 0.5f, 0.5f, 0.55f);
            button.colors = colors;
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
            image.color = WithAlpha(ink, 0.55f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1.2f);
            colors.pressedColor = StatBar.ColorOf(Ui.UiCopper);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = colors;
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
            image.color = WithAlpha(ink, 0.6f);
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
            track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            RectTransform fill = NewRect("Fill", track);
            Stretch(fill);
            Image image = fill.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
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
