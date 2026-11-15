using System;
using System.Collections;
using MarsTrainer.Data.Research;
using MarsTrainer.Gameplay.World;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Events;
using MarsTrainer.Sim.Research;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    [DefaultExecutionOrder(2000)]
    public sealed partial class HudView : MonoBehaviour
    {
        [SerializeField] private RectTransform safeArea;
        [SerializeField] private CanvasGroup gameplay;
        [SerializeField] private TMP_Text date;
        [SerializeField] private TMP_Text energy;
        [SerializeField] private StatBar[] stats;
        [SerializeField] private StatBar battery;
        [SerializeField] private SunZapMeterView sunZap;
        [SerializeField] private AlertBanner alerts;
        [SerializeField] private TimeControls time;
        [SerializeField] private ResearchScreen research;
        [SerializeField] private PauseMenu pause;
        [SerializeField] private EndScreen end;
        [SerializeField] private Button researchButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private TutorialPanel tutorial;
        [SerializeField] private MissionControls missionControls;
        [SerializeField] private ResearchNodeSO[] catalog = Array.Empty<ResearchNodeSO>();
        [SerializeField] private MissionBriefing briefing;
        [SerializeField] private GameObject fieldKit;
        [SerializeField] private Button kitButton, closeKitButton, eatButton, drinkButton;
        [SerializeField] private GameObject walkingHint;
        [SerializeField] private TMP_Text eatCaption, drinkCaption;
        [SerializeField] private TMP_Text fieldStatus, navigation;
        private TerrainGenerator worldSource;
        private Selectable[] kitChoices;
        public bool KitOpen => fieldKit != null && fieldKit.activeSelf;
        private Simulation sim;
        private ResearchSystem researchSystem;
        private Rect lastSafe;
        private Vector2Int lastSize;
        private WeatherEvent nextFlare;
        private bool researchWasPaused;
        private bool reportClosed;
        private int recalls;
        private string lastHurt = "No serious warnings recorded";
        private GameObject returnFocus;
        private Selectable[] hudChoices, researchChoices, pauseChoices, endChoices;
        public Simulation Source => sim;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindObjectOfType<TerrainGenerator>() == null || FindObjectOfType<HudView>() != null) return;
            var prefab = Resources.Load<GameObject>("MarsTrainerHUD");
            if (prefab != null) Instantiate(prefab);
            else Debug.LogWarning("MarsTrainer HUD prefab missing; use Mars Trainer/Build P10 UI Prefabs.");
        }
        private void Awake()
        {
            var world = FindObjectOfType<TerrainGenerator>();
            if (world != null) world.UsesMissionHud = true;
            if (world != null && world.Simulation == null)
            {
                var nodes = new ResearchNode[catalog.Length];
                for (int i = 0; i < nodes.Length; i++) nodes[i] = catalog[i].ToNode();
                world.ConfigureResearch(nodes);
            }
        }
        private IEnumerator Start()
        {
            EnsureEventSystem();
            var world = FindObjectOfType<TerrainGenerator>();
            while (sim == null && world != null && world.Simulation == null) yield return null;
            if (sim == null)
            {
                if (world != null && world.Simulation != null) BindMission(world);
            }
            ApplySafeArea();
            if (briefing == null || !briefing.IsOpen) pauseButton.Select();
        }
        private void BindMission(TerrainGenerator world)
        {
            worldSource = world;
            Bind(world.Simulation, world.Research);
            if (missionControls != null) missionControls.Bind(world.Mission, world.Exercise);
            Refresh();
            if (briefing != null && (world.gameObject.scene.name == "Main" || world.gameObject.scene.name == "P12Main") &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-p12Smoke") < 0)
            {
                gameplay.interactable = false;
                world.SetInputBlocked(true);
                briefing.Closed = () => { RestoreGameplay(); pauseButton.Select(); };
                briefing.Open(sim.Clock);
            }
        }
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var owner = new GameObject("Mars UI EventSystem", typeof(EventSystem));
            var module = owner.AddComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
            // Reserve Space for global pause; Enter submits the focused button.
            var submit = module.submit.action; submit.Disable();
            for (int i = submit.bindings.Count - 1; i >= 0; i--)
                if (submit.bindings[i].path == "<Keyboard>/space") submit.ChangeBinding(i).Erase();
            submit.Enable();
        }
        public void Bind(Simulation source, ResearchSystem upgrades = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Unbind(); sim = source; researchSystem = upgrades; recalls = 0; reportClosed = false;
            lastHurt = "No serious warnings recorded"; nextFlare = null; alerts.Clear();
            Subscribe();
            time.Bind(sim.Clock); pause.Bind(sim.Clock); pause.Closed = RestoreGameplay;
            research.Bind(upgrades, catalog); research.Closed = CloseResearch;
            research.PurchaseRequested = Purchase;
            if (eatButton != null) { eatButton.onClick.RemoveAllListeners(); eatButton.onClick.AddListener(missionControls.Eat); }
            if (drinkButton != null) { drinkButton.onClick.RemoveAllListeners(); drinkButton.onClick.AddListener(missionControls.Drink); }
            hudChoices = gameplay.GetComponentsInChildren<Selectable>(true);
            if (fieldKit != null) kitChoices = fieldKit.GetComponentsInChildren<Selectable>(true);
            if (kitButton != null) { kitButton.onClick.RemoveAllListeners(); kitButton.onClick.AddListener(ToggleKit); }
            if (closeKitButton != null) { closeKitButton.onClick.RemoveAllListeners(); closeKitButton.onClick.AddListener(ToggleKit); }
            researchChoices = research.GetComponentsInChildren<Selectable>(true);
            pauseChoices = pause.GetComponentsInChildren<Selectable>(true);
            endChoices = end.GetComponentsInChildren<Selectable>(true);
            end.ReviewRequested = () => { reportClosed = true; RestoreGameplay(); };
            researchButton.onClick.RemoveAllListeners(); researchButton.onClick.AddListener(OpenResearch);
            pauseButton.onClick.RemoveAllListeners(); pauseButton.onClick.AddListener(OpenPause);
            alerts.ShowRecall(false); // RecallDroneModel launches automatically on danger; there is no manual sim command.
            sunZap.ShowDose(0, sim.State.RadiationDoseMSv);
            double fraction = sim.State.RadiationDoseMSv / GameConstants.H_limit;
            sunZap.ShowState(fraction >= GameConstants.ShieldCriticalFraction ? SuitShieldState.Critical
                : fraction >= GameConstants.ShieldDangerFraction ? SuitShieldState.Danger
                : fraction >= GameConstants.ShieldWarnFraction ? SuitShieldState.Warn : SuitShieldState.Safe);
            Refresh();
            if (Application.isPlaying) InvokeRepeating(nameof(Refresh), GameConstants.UiRefreshSeconds, GameConstants.UiRefreshSeconds);
        }
        private void Subscribe()
        {
            var bus = sim.Events;
            bus.SolStarted += OnSol; bus.EnergyReported += OnEnergy; bus.RadiationUpdated += sunZap.ShowDose;
            bus.SuitShieldChanged += sunZap.ShowState; bus.StatCritical += OnCritical;
            bus.FlareAlert += OnFlare; bus.WeatherWarned += OnWarning; bus.WeatherEnded += OnWeatherEnd;
            bus.RecallLaunched += OnRecall; bus.Brownout += OnBrownout;
            bus.PlayerDied += OnDeath; bus.MissionComplete += OnComplete;
        }
        private void Unbind()
        {
            CancelInvoke(); if (sim == null) return;
            var bus = sim.Events;
            bus.SolStarted -= OnSol; bus.EnergyReported -= OnEnergy; bus.RadiationUpdated -= sunZap.ShowDose;
            bus.SuitShieldChanged -= sunZap.ShowState; bus.StatCritical -= OnCritical;
            bus.FlareAlert -= OnFlare; bus.WeatherWarned -= OnWarning; bus.WeatherEnded -= OnWeatherEnd;
            bus.RecallLaunched -= OnRecall; bus.Brownout -= OnBrownout;
            bus.PlayerDied -= OnDeath; bus.MissionComplete -= OnComplete;
        }
        private void OnDisable() { Unbind(); }
        private void OnEnable()
        {
            if (sim == null) return;
            Subscribe(); Refresh();
            if (Application.isPlaying) InvokeRepeating(nameof(Refresh), GameConstants.UiRefreshSeconds, GameConstants.UiRefreshSeconds);
        }
        private void OnDestroy() { Unbind(); }
        private void OnSol(int sol) { Refresh(); }
        private void OnEnergy(EnergyReport report)
        {
            battery.Show("Battery (%)", report.BatteryPercent);
            energy.text = "In " + report.GenerationKWh.ToString("0.0") + " / Out " + report.LoadKWh.ToString("0.0") + " kWh\nPer Martian Day";
        }
        private void OnCritical(string stat, double value)
        {
            lastHurt = Friendly(stat) + " needed help"; alerts.Enqueue(lastHurt);
        }
        private static string Friendly(string id)
        {
            switch (id)
            {
                case "hunger": case "starvation": return "Food";
                case "water": case "dehydration": return "Water";
                case "oxygen": case "hypoxia": return "Air";
                case "tiredness": return "Rest";
                case "radiation": return "Sun-Zap exposure";
                case "temperature": return "Temperature";
                default: return "Health";
            }
        }
        private void OnFlare(int sol, int level) { alerts.Enqueue("Solar flare. Reach shelter; recall is automatic."); alerts.ShowCountdown(0); }
        private void OnWarning(WeatherEvent weather)
        {
            string label = weather.Type == "solar-flare" ? "Solar flare" : weather.Type == "dust-storm" ? "Dust storm"
                : weather.Type == "cold-night" ? "Very cold night" : "Hot weather";
            alerts.Enqueue(label + " approaching. Check shelter and power.");
            if (weather.Type == "solar-flare") nextFlare = weather;
        }
        private void OnWeatherEnd(WeatherEvent weather) { if (weather.Type == "solar-flare" && (nextFlare == null || nextFlare == weather)) { nextFlare = null; alerts.HideCountdown(); } }
        private void OnRecall() { recalls++; alerts.Enqueue("Recall drone launched. Shelter ahead."); }
        private void OnBrownout() { lastHurt = "Not enough power"; alerts.Enqueue("Low power! Some equipment has stopped."); }
        private void OnDeath() { ShowEnd(false); }
        private void OnComplete() { ShowEnd(true); }
        private void ShowEnd(bool complete)
        {
            SetKit(false);
            if (research.gameObject.activeSelf) CloseResearch();
            if (pause.gameObject.activeSelf) pause.Close();
            sim.Clock.Paused = true; gameplay.interactable = false;
            int owned = 0;
            if (researchSystem != null) for (int i = 0; i < catalog.Length; i++) if (researchSystem.State.IsPurchased(catalog[i].id)) owned++;
            end.Show(complete, sim.Clock.ElapsedSols,
                sim.State.DeathCause != null ? Friendly(sim.State.DeathCause) : lastHurt,
                recalls + " drone rescues; " + owned + " upgrades owned", sim.State.RadiationDoseMSv);
        }
        public void ToggleKit()
        {
            if (sim == null || pause.gameObject.activeSelf || research.gameObject.activeSelf || end.gameObject.activeSelf ||
                (briefing != null && briefing.IsOpen)) return;
            SetKit(!KitOpen);
        }
        private void SetKit(bool open)
        {
            if (fieldKit == null) return;
            fieldKit.SetActive(open); worldSource?.SetInputBlocked(open);
            if (walkingHint != null) walkingHint.SetActive(!open);
            if (eatButton != null) eatButton.transform.parent.gameObject.SetActive(!open);
            if (EventSystem.current != null)
            {
                if (open) closeKitButton.Select(); else kitButton.Select();
            }
        }

        private void Purchase(string id) { if (researchSystem != null) researchSystem.TryPurchase(id); research.Refresh(); }
        private void OpenResearch()
        {
            if (sim == null || pause.gameObject.activeSelf || end.gameObject.activeSelf || research.gameObject.activeSelf) return;
            SetKit(false);
            researchWasPaused = sim.Clock.Paused; sim.Clock.Paused = true;
            returnFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            gameplay.interactable = false; research.Open();
        }
        private void CloseResearch()
        {
            if (!research.gameObject.activeSelf) return;
            research.gameObject.SetActive(false); sim.Clock.Paused = researchWasPaused;
            RestoreGameplay();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(returnFocus);
        }
        private void OpenPause()
        {
            if (sim == null || research.gameObject.activeSelf || end.gameObject.activeSelf) return;
            SetKit(false);
            gameplay.interactable = false; pause.Open();
        }
        private void RestoreGameplay() { gameplay.interactable = true; time.Refresh(); worldSource?.SetInputBlocked(KitOpen); }
        private void Refresh()
        {
            if (sim == null) return;
            var state = sim.State;
            stats[0].Show("Health", state.Health); stats[1].Show("Food", GameConstants.StatMaximum - state.Hunger);
            stats[2].Show("Water", state.Water); stats[3].Show("Rest", GameConstants.StatMaximum - state.Tiredness);
            stats[4].Show("Air", state.Oxygen); battery.Show("Battery (kWh)", state.EnergyKWh, state.BatteryCapKWh);
            date.text = "Martian Days " + sim.Clock.ElapsedSols.ToString("00.0") + " / " + GameConstants.MissionDurationDays;
            if (fieldStatus != null) fieldStatus.text = "Power " + state.EnergyKWh.ToString("0") + " kWh / Radiation " + sunZap.CurrentState;
            if (eatButton != null) eatButton.interactable = missionControls.CanEat;
            if (eatCaption != null) eatCaption.text = missionControls.FoodFull ? "Full [F]" : "Eat [F]";
            if (drinkButton != null) drinkButton.interactable = missionControls.CanDrink;
            if (drinkCaption != null) drinkCaption.text = missionControls.WaterFull ? "Full [G]" : "Drink [G]";
            if (navigation != null) navigation.text = worldSource != null ? worldSource.LocationCaption() : "Base 0 m";
            if (nextFlare != null) alerts.ShowCountdown(Math.Max(0, nextFlare.StartSol - sim.Clock.ElapsedSols));
            time.Refresh(); if (research.gameObject.activeSelf) research.Refresh();
            if (tutorial != null) tutorial.ShowSol(sim.Clock.Sol);
            if (missionControls != null) missionControls.Refresh();
        }
        private void Update()
        {
            if (lastSafe != Screen.safeArea || lastSize.x != Screen.width || lastSize.y != Screen.height) ApplySafeArea();
            var keys = Keyboard.current; if (keys == null || sim == null) return;
            if (briefing != null && briefing.IsOpen) return;
            if (end.gameObject.activeSelf) { MoveTab(keys); return; }
            if (keys.escapeKey.wasPressedThisFrame)
            {
                if (research.gameObject.activeSelf) CloseResearch(); else if (pause.gameObject.activeSelf) pause.Close(); else if (KitOpen) SetKit(false); else OpenPause();
            }
            if (research.gameObject.activeSelf || pause.gameObject.activeSelf) { MoveTab(keys); return; }
            if (keys.eKey.wasPressedThisFrame) ToggleKit();
            if (!KitOpen && missionControls != null)
            {
                if (keys.fKey.wasPressedThisFrame) missionControls.Eat();
                if (keys.gKey.wasPressedThisFrame) missionControls.Drink();
            }
            if (keys.rKey.wasPressedThisFrame) OpenResearch();
            // Enter is the focused UI submit key; Space is reserved for pause.
            if (!reportClosed && keys.spaceKey.wasPressedThisFrame) time.TogglePause();
            if (keys.digit1Key.wasPressedThisFrame) time.SetSpeed(1);
            if (keys.digit2Key.wasPressedThisFrame) time.SetSpeed(2);
            if (keys.digit3Key.wasPressedThisFrame) time.SetSpeed(4);
            MoveTab(keys);
        }
        private void MoveTab(Keyboard keys)
        {
            if (!keys.tabKey.wasPressedThisFrame || EventSystem.current == null) return;
            var choices = end.gameObject.activeSelf ? endChoices : research.gameObject.activeSelf ? researchChoices
                : pause.gameObject.activeSelf ? pauseChoices : KitOpen ? kitChoices : hudChoices;
            if (choices == null || choices.Length == 0) return;
            var current = EventSystem.current.currentSelectedGameObject;
            int index = -1;
            for (int i = 0; i < choices.Length; i++) if (choices[i].gameObject == current) { index = i; break; }
            int direction = keys.shiftKey.isPressed ? -1 : 1;
            for (int i = 0; i < choices.Length; i++)
            {
                index = (index + direction + choices.Length) % choices.Length;
                if (choices[index].IsActive() && choices[index].IsInteractable()) { choices[index].Select(); return; }
            }
        }
        private void ApplySafeArea()
        {
            lastSafe = Screen.safeArea; lastSize = new Vector2Int(Screen.width, Screen.height);
            if (lastSize.x <= 0 || lastSize.y <= 0) return;
            safeArea.anchorMin = new Vector2(lastSafe.xMin / lastSize.x, lastSafe.yMin / lastSize.y);
            safeArea.anchorMax = new Vector2(lastSafe.xMax / lastSize.x, lastSafe.yMax / lastSize.y);
        }

    }
}
