using System;
using System.Globalization;
using MarsSurvival.Gameplay;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using MarsTrainer.UI;
using MissionSettings = MarsSurvival.Simulation.MissionSettings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ui = MarsTrainer.Sim.GameConstants;

namespace MarsSurvival.UI
{
    /// <summary>
    /// The in-game screen: sol and time top left, key hints top right, LIFE SUPPORT bottom left (the
    /// three vitals as segmented bars, credits and plot count), the latest message, the seed shop,
    /// pause and end screens.
    /// The mission starts as soon as the home screen closes.
    /// </summary>
    public sealed class MarsMissionHud : MonoBehaviour
    {
        private const float Low = 25f;
        private MarsMissionController mission;
        private TMP_FontAsset font;
        private Color ink, paper, muted, danger;
        private TMP_Text sol, clock, money, plots, notice;
        private const int Segments = 20;
        private readonly Image[][] segments = new Image[3][];
        private readonly TMP_Text[] values = new TMP_Text[3];
        private readonly Color[] colors = new Color[3];
        private RectTransform shop;
        private TMP_Text shopCredits, expandRow, expandMeta;
        private TMP_Text[] owned, basketRows;
        private Button[] buy, sell, sellAll;
        private Button expand;
        private GameObject gameplay;
        private RectTransform noticePlate;
        private MarsSurvival.Vehicles.MarsRover rover;
        private RectTransform drive, driveKeys, ridePrompt;
        private TMP_Text speed, batteryValue, rangeLabel, rangeValue, rideText;
        private readonly Image[] battery = new Image[Segments];
        private PauseMenu pause;
        private EndScreen end;
        private bool built, ended;

        private void Start() { mission = GetComponent<MarsMissionController>(); }

        private void Update()
        {
            if (mission == null) return;
            if (MarsHomeScreen.IsOpen) { if (gameplay != null) gameplay.SetActive(false); return; }
            if (!built) Build();
            if (mission.Simulation == null && mission.Ready) mission.NewMission();
            if (mission.Simulation == null) return;
            gameplay.SetActive(true);
            HandleKeys();
            Refresh();
        }

        private void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (mission.ShopOpen) mission.SetShopOpen(false);
                else if (pause.gameObject.activeSelf) pause.Close();
                else if (mission.IsRunning) { mission.SetPaused(true); pause.Open(); }
            }
            if (Input.GetKeyDown(KeyCode.P) && mission.IsRunning && !mission.ShopOpen)
            {
                if (pause.gameObject.activeSelf) pause.Close(); else { mission.SetPaused(true); pause.Open(); }
            }
            if (Input.GetKeyDown(KeyCode.F5)) mission.SaveMission();
            if (Input.GetKeyDown(KeyCode.F9)) mission.LoadMission();
#endif
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            built = true;
            ink = StatBar.ColorOf(Ui.UiSurface);
            paper = StatBar.ColorOf(Ui.UiText);
            muted = UiTheme.Dim;
            danger = StatBar.ColorOf(Ui.UiDanger);
            font = UiTypeface.Font;
            GameObject root = new GameObject("Mission Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Ui.UiWidth, Ui.UiHeight);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("Mission Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            RectTransform safe = StatBar.Frame(root.transform, "Safe area", 0, 0, 1, 1);
            gameplay = StatBar.Frame(safe, "Mission HUD", 0, 0, 1, 1).gameObject;
            Transform hud = gameplay.transform;

            // Top left: sol (cyan, like a module ID) and local time. Top right: the keys.
            RectTransform top = Plate(hud, "Clock", new Vector2(0, 1), new Vector2(24, -24), new Vector2(230, 58));
            sol = Label(Region(top, 16, 0, 90, 58), "", 19, UiTheme.Info, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(sol, 3f);
            clock = Label(Region(top, 96, 0, 120, 58), "", 30, paper, TextAlignmentOptions.MidlineLeft);
            UiStyle.Readout(clock);
            RectTransform keys = Plate(hud, "Keys", new Vector2(1, 1), new Vector2(-24, -24), new Vector2(340, 50));
            string key = "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.Accent) + ">";
            TMP_Text hints = Label(keys, key + "ESC</color> MENU    " + key + "F5</color> SAVE    " + key + "P</color> PAUSE", 16, muted, TextAlignmentOptions.Center);
            UiStyle.Caps(hints, 2f);

            // Bottom left: LIFE SUPPORT, with credits, then oxygen, water and food as segmented bars.
            RectTransform vitals = Plate(hud, "Vitals", Vector2.zero, new Vector2(24, 24), new Vector2(420, 210));
            UiStyle.Id(vitals, "Life support", 18, 18, 14);
            money = Label(Region(vitals, 220, 166, 182, 34), "", 26, UiTheme.Accent, TextAlignmentOptions.MidlineRight);
            UiStyle.Readout(money);
            string[] names = { "O<sub>2</sub>", "WATER", "FOOD" };
            Color[] tints = { UiTheme.Info, UiTheme.Hex(0x3F9BFF), UiTheme.Accent };
            for (int i = 0; i < 3; i++)
            {
                RectTransform row = Region(vitals, 18, 112 - i * 34, 384, 28);
                colors[i] = tints[i];
                TMP_Text name = Label(Region(row, 0, 0, 70, 28), names[i], 15, muted, TextAlignmentOptions.MidlineLeft);
                UiStyle.Caps(name, 2f);
                segments[i] = new Image[Segments];
                for (int k = 0; k < Segments; k++)
                    segments[i][k] = Fill(Region(row, 76 + k * 12.5f, 8, 9.5f, 12), WithAlpha(paper, 0.1f));
                values[i] = Label(Region(row, 330, 0, 54, 28), "100", 21, paper, TextAlignmentOptions.MidlineRight);
                UiStyle.Readout(values[i]);
            }
            TMP_Text house = Label(Region(vitals, 18, 10, 200, 26), "Greenhouse-04", 13, muted, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(house, 2.5f);
            plots = Label(Region(vitals, 200, 10, 202, 26), "", 13, muted, TextAlignmentOptions.MidlineRight);
            UiStyle.Caps(plots, 2.5f);

            // Bottom centre while driving (the hotbar hides): speed, battery and range, with the keys above.
            drive = Plate(hud, "Rover", new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(460, 108));
            speed = Label(Region(drive, 22, 32, 120, 66), "0", 58, paper, TextAlignmentOptions.MidlineLeft);
            UiStyle.Readout(speed);
            speed.overflowMode = TextOverflowModes.Overflow;   // the big digits are taller than their line; never ellipsise
            TMP_Text kmh = Label(Region(drive, 24, 10, 100, 22), "km/h", 13, muted, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(kmh, 3f);
            TMP_Text batteryLabel = Label(Region(drive, 150, 70, 140, 24), "Battery", 13, muted, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(batteryLabel, 3f);
            batteryValue = Label(Region(drive, 290, 68, 148, 28), "", 19, paper, TextAlignmentOptions.MidlineRight);
            UiStyle.Readout(batteryValue);
            for (int k = 0; k < Segments; k++) battery[k] = Fill(Region(drive, 150 + k * 14.4f, 50, 11.4f, 12), WithAlpha(paper, 0.1f));
            rangeLabel = Label(Region(drive, 150, 14, 150, 24), "Range", 13, muted, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(rangeLabel, 3f);
            rangeValue = Label(Region(drive, 290, 12, 148, 28), "", 17, paper, TextAlignmentOptions.MidlineRight);
            UiStyle.Readout(rangeValue);
            driveKeys = Plate(hud, "Rover keys", new Vector2(.5f, 0), new Vector2(0, 142), new Vector2(560, 40));
            TMP_Text driveHints = Label(driveKeys, key + "W S</color>  drive    " + key + "A D</color>  steer    " + key + "Space</color>  brake    " + key + "E</color>  step out", 14, muted, TextAlignmentOptions.Center);
            UiStyle.Caps(driveHints, 2f);
            // Walking up to the rover: one prompt.
            ridePrompt = Plate(hud, "Rover prompt", new Vector2(.5f, .5f), new Vector2(0, -150), new Vector2(380, 46));
            rideText = Label(ridePrompt, "", 18, paper, TextAlignmentOptions.Center);
            UiStyle.Caps(rideText, 2.5f);

            // Top centre: the latest message on its own plate, sized to the text and hidden when there is none.
            noticePlate = Plate(hud, "Message", new Vector2(.5f, 1), new Vector2(0, -24), new Vector2(600, 46));
            notice = Label(noticePlate, "", 18, paper, TextAlignmentOptions.Center);
            notice.margin = new Vector4(20, 0, 20, 0);

            BuildShop(safe);
            pause = PauseMenu.Create(safe);
            pause.Closed = () => mission.SetPaused(false);
            end = EndScreen.Create(safe);
        }

        /// <summary>
        /// The seed shop, signed RESOURCES-02 like the wing it stands in: seeds (crop icon, grow time,
        /// sell price, how many you own and an amber price button), the greenhouse expansion, then the
        /// harvest to sell.
        /// </summary>
        private void BuildShop(Transform parent)
        {
            Color accent = UiTheme.Accent;
            shop = Plate(parent, "Seed shop", new Vector2(1, .5f), new Vector2(-24, 0), new Vector2(580, 0));
            shop.pivot = new Vector2(1, .5f);
            VerticalLayoutGroup list = shop.gameObject.AddComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset(26, 26, 22, 22);
            list.spacing = 4;
            list.childControlHeight = list.childControlWidth = true;
            list.childForceExpandHeight = false;
            shop.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform head = Line(shop, 92, 0f);
            UiStyle.Id(head, "Resources-02", 17, 0, 4);
            shopCredits = Label(Region(head, 300, 54, 228, 38), "", 28, accent, TextAlignmentOptions.MidlineRight);
            UiStyle.Readout(shopCredits);
            shopCredits.rectTransform.anchorMin = shopCredits.rectTransform.anchorMax = new Vector2(1, 1);
            shopCredits.rectTransform.pivot = new Vector2(1, 1);
            shopCredits.rectTransform.anchoredPosition = Vector2.zero;
            TMP_Text title = Label(Region(head, 0, 0, 400, 46), "Seed shop", 32, paper, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(title, 4f);

            Section("Seeds");
            int n = SeedKind.All.Length;
            owned = new TMP_Text[n]; basketRows = new TMP_Text[n]; buy = new Button[n]; sell = new Button[n]; sellAll = new Button[n];
            for (int i = 0; i < n; i++)
            {
                SeedKind k = SeedKind.All[i];
                string id = k.id;
                RectTransform card = Line(shop, 84, 0f);
                Icon(card, id, 0, 10, 64);
                TMP_Text name = Label(Region(card, 80, 46, 300, 30), k.name, 22, paper, TextAlignmentOptions.MidlineLeft);
                UiStyle.Caps(name, 2f);
                Label(Region(card, 80, 24, 320, 22), "Ripe " + Duration(k.seconds) + " after watering · sells " + k.sellPrice, 15, muted, TextAlignmentOptions.MidlineLeft);
                owned[i] = Label(Region(card, 80, 4, 300, 20), "", 13, UiTheme.Info, TextAlignmentOptions.MidlineLeft);
                UiStyle.Caps(owned[i], 2f);
                buy[i] = ShopButton(card, k.price + " CR", 116, 44, accent, () => Trade("buy", id));
            }
            Section("Greenhouse");
            RectTransform grow = Line(shop, 64, 0f);
            expandRow = Label(Region(grow, 0, 28, 380, 30), "", 22, paper, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(expandRow, 2f);
            expandMeta = Label(Region(grow, 0, 6, 380, 22), "", 15, muted, TextAlignmentOptions.MidlineLeft);
            expand = ShopButton(grow, "", 116, 44, accent, () => Trade("buy", "plots"));

            Section("Harvest");
            for (int i = 0; i < n; i++)
            {
                string id = SeedKind.All[i].id;
                RectTransform row = Line(shop, 56, 0f);
                Icon(row, id, 0, 6, 44);
                basketRows[i] = Label(Region(row, 60, 0, 272, 56), "", 18, paper, TextAlignmentOptions.MidlineLeft);
                sellAll[i] = ShopButton(row, "All", 70, 40, Color.clear, () => Trade("sell", id));
                sell[i] = ShopButton(row, "Sell 1", 92, 40, Color.clear, () => Trade("sellone", id));
                sell[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(-90, 0);
            }
            RectTransform foot = Line(shop, 56, 0f);
            ShopButton(foot, "<color=#" + ColorUtility.ToHtmlStringRGB(accent) + ">ESC</color>  Close", 150, 40, Color.clear, () => mission.SetShopOpen(false));
            shop.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ trade feedback

        private struct Pop { public TMP_Text text; public float born; public Vector2 from; }
        private readonly System.Collections.Generic.List<Pop> pops = new System.Collections.Generic.List<Pop>();

        /// <summary>
        /// Runs a shop action and shows what changed as text that rises off the clicked button and fades:
        /// "+1 LETTUCE SEED  −10 CR" on a buy, "+15 CR" on a sale; a refused click shows the reason in red.
        /// </summary>
        private void Trade(string action, string id)
        {
            MissionState s = mission.Simulation.State;
            int credits = s.credits, seeds = Total(s.seeds), plots = s.unlockedPlots;
            mission.Execute(action, id);
            int spent = credits - s.credits;
            SeedKind seed = SeedKind.Find(id);
            string kind = seed == null ? "" : seed.name.ToUpperInvariant();
            string info = "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.Info) + ">", cr = "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.Accent) + ">";
            string text;
            if (Total(s.seeds) > seeds) text = info + "+1 " + kind + " seed</color>   " + cr + "−" + spent + " CR</color>";
            else if (s.unlockedPlots > plots) text = info + "+" + (s.unlockedPlots - plots) + " plots</color>   " + cr + "−" + spent + " CR</color>";
            else if (spent < 0) text = cr + "+" + (-spent) + " CR</color>";
            else text = "<color=#" + ColorUtility.ToHtmlStringRGB(danger) + ">" + mission.Notice + "</color>";
            // Rises off the button that was clicked (the selected one), over everything in the shop.
            GameObject clicked = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            RectTransform button = clicked != null && clicked.transform.IsChildOf(shop) ? (RectTransform)clicked.transform : (RectTransform)buy[0].transform;
            RectTransform holder = NewRect("Pop", shop);
            holder.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // the shop is a vertical list
            holder.sizeDelta = new Vector2(440, 30);
            holder.position = button.position;
            Vector2 from = holder.anchoredPosition + new Vector2(-150f, 30f);
            TMP_Text pop = Label(holder, text, 18, paper, TextAlignmentOptions.Center);
            UiStyle.Caps(pop, 2f);
            UiStyle.Readable(pop);
            pops.Add(new Pop { text = pop, born = Time.unscaledTime, from = from });
            AnimatePops();
        }

        private static int Total(int[] counts) { int n = 0; foreach (int c in counts) n += c; return n; }

        /// <summary>Each pop rises 40 px and fades out over 1.2 s.</summary>
        private void AnimatePops()
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                float t = (Time.unscaledTime - pops[i].born) / 1.2f;
                if (t >= 1f) { Destroy(pops[i].text.transform.parent.gameObject); pops.RemoveAt(i); continue; }
                ((RectTransform)pops[i].text.transform.parent).anchoredPosition = pops[i].from + new Vector2(0, 40f * Mathf.Sqrt(t));
                pops[i].text.alpha = 1f - t * t;
            }
        }

        /// <summary>A dim tracked-capital section name with a hairline running to the right edge.</summary>
        private void Section(string name)
        {
            RectTransform row = Line(shop, 36, 0f);
            TMP_Text label = Label(Region(row, 0, 4, 160, 26), name, 13, muted, TextAlignmentOptions.MidlineLeft);
            UiStyle.Caps(label, 3.5f);
            float width = label.GetPreferredValues(name.ToUpperInvariant()).x + 12f;
            RectTransform rule = Region(row, width, 16, 0, 1);
            rule.anchorMax = new Vector2(1, 0);
            rule.sizeDelta = new Vector2(-width, 1);
            Fill(rule, UiTheme.Edge);
        }

        /// <summary>Greys out a button you can't use (can't afford, nothing to sell), not by colour alone: it stops clicking too.</summary>
        private void Enable(Button button, bool on, Color fill)
        {
            button.interactable = on;
            bool ghost = fill.a == 0f;
            button.targetGraphic.color = on ? (ghost ? WithAlpha(UiTheme.Accent, 0.12f) : fill) : WithAlpha(paper, 0.06f);
            button.GetComponentInChildren<TMP_Text>().color = on ? (ghost ? UiTheme.Accent : ink) : WithAlpha(paper, 0.3f);
        }

        private static string Duration(double seconds) { return seconds < 60 ? seconds + " s" : (seconds / 60) + " min"; }

        /// <summary>A full-width row of the shop list, with an optional faint backing.</summary>
        private RectTransform Line(Transform parent, float height, float backing)
        {
            RectTransform row = NewRect("Row", parent);
            LayoutElement e = row.gameObject.AddComponent<LayoutElement>();
            e.minHeight = e.preferredHeight = height;
            if (backing > 0f) Fill(row, WithAlpha(paper, backing));
            return row;
        }

        /// <summary>The crop's icon (Resources/Garden/Icons, rendered in Blender) on a dark tile.</summary>
        private void Icon(RectTransform row, string id, float x, float y, float size)
        {
            RectTransform tile = Region(row, x, y, size, size);
            Fill(tile, WithAlpha(paper, 0.04f));
            Image icon = NewRect("Icon", tile).gameObject.AddComponent<Image>();
            icon.rectTransform.anchorMin = Vector2.zero; icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
            icon.sprite = Resources.Load<Sprite>(MarsWorldTasks.GardenFolder + "/Icons/" + id);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        /// <summary>A button on the right of a row; its label reads in the ink colour on the fill.</summary>
        private Button ShopButton(RectTransform row, string label, float width, float height, Color fill, Action click)
        {
            RectTransform rect = NewRect(label, row);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-12, 0);
            rect.sizeDelta = new Vector2(width, height);
            FacetedImage image = rect.gameObject.AddComponent<FacetedImage>();
            image.KeepAlpha = true;
            image.color = fill.a == 0f ? WithAlpha(UiTheme.Accent, 0.12f) : fill;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock states = ColorBlock.defaultColorBlock;
            states.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);
            button.colors = states;
            button.onClick.AddListener(() => { click(); MarsSfx.Play(MarsSfx.Sound.Click); });
            TMP_Text text = Label(rect, label, 17, fill.a == 0f ? UiTheme.Accent : ink, TextAlignmentOptions.Center);
            UiStyle.Caps(text, 2.5f);
            return button;
        }

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            MissionState s = mission.Simulation.State;
            AstronautState a = s.astronaut;
            int sol = (int)(s.elapsedSeconds / MissionSettings.SolSeconds) + 1;
            double hour = mission.Environment.localHour;
            this.sol.text = "Sol " + sol.ToString("00");
            clock.text = ((int)hour).ToString("00") + ":" + ((int)(hour % 1 * 60)).ToString("00");
            money.text = s.credits + "<size=60%> CR</size>";
            plots.text = s.unlockedPlots + " / " + MissionSettings.MaxPlots + " plots";
            double[] v = { a.oxygen, a.water, a.food };
            for (int i = 0; i < 3; i++)
            {
                int lit = Mathf.CeilToInt(Mathf.Clamp01((float)v[i] / 100f) * Segments);
                Color on = v[i] < Low ? danger : colors[i], off = WithAlpha(paper, 0.1f);
                for (int k = 0; k < Segments; k++) segments[i][k].color = k < lit ? on : off;
                values[i].text = Mathf.CeilToInt((float)v[i]).ToString(CultureInfo.InvariantCulture);
                values[i].color = v[i] < Low ? danger : paper;
            }
            // Water low: say where to drink (a marker on the pump shows the way, see MarsExpedition).
            bool thirsty = a.water < MissionSettings.WaterLow;
            notice.text = mission.Simulation.Asleep ? "Sleeping until 07:00..."
                : !string.IsNullOrEmpty(mission.Notice) ? mission.Notice
                : thirsty ? "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.Accent) + ">Water low · drink at the pump in Resources-02</color>" : "";
            RefreshRover();
            bool any = !string.IsNullOrEmpty(notice.text);
            if (noticePlate.gameObject.activeSelf != any) noticePlate.gameObject.SetActive(any);
            if (any) noticePlate.sizeDelta = new Vector2(Mathf.Min(1100f, notice.GetPreferredValues(notice.text).x + 44f), 46f);

            AnimatePops();
            bool open = mission.ShopOpen;
            if (shop.gameObject.activeSelf != open) shop.gameObject.SetActive(open);
            if (open)
            {
                MissionSimulation sim = mission.Simulation;
                shopCredits.text = s.credits + "<size=60%> CR</size>";
                for (int i = 0; i < SeedKind.All.Length; i++)
                {
                    SeedKind k = SeedKind.All[i];
                    owned[i].text = s.seeds[i] > 0 ? "Owned " + s.seeds[i] : "None owned";
                    owned[i].color = s.seeds[i] > 0 ? UiTheme.Info : muted;
                    Enable(buy[i], s.credits >= k.price, UiTheme.Accent);
                    basketRows[i].text = k.name.ToUpperInvariant() + " × " + s.basket[i] + "<pos=62%><color=#" + ColorUtility.ToHtmlStringRGB(muted) + "><size=80%>+" + k.sellPrice + " each</size></color>";
                    Enable(sell[i], s.basket[i] > 0, Color.clear);
                    Enable(sellAll[i], s.basket[i] > 1, Color.clear);
                }
                expandRow.text = sim.CanExpand ? "Add " + MissionSettings.PlotsPerExpansion + " plots" : "Greenhouse full";
                expandMeta.text = s.unlockedPlots + " of " + MissionSettings.MaxPlots + " plots in use";
                Enable(expand, sim.CanExpand && s.credits >= sim.ExpansionPrice, UiTheme.Accent);
                expand.GetComponentInChildren<TMP_Text>().text = sim.CanExpand ? sim.ExpansionPrice + " CR" : "—";
            }

            if (!mission.IsRunning && !ended)
            {
                ended = true;
                end.Show(s.endingReason, s.elapsedSeconds / MissionSettings.SolSeconds);
            }
            if (mission.IsRunning) ended = false;
        }

        /// <summary>The driving panel while at the wheel, the E prompt when standing by the rover.</summary>
        private void RefreshRover()
        {
            if (rover == null) rover = FindObjectOfType<MarsSurvival.Vehicles.MarsRover>();
            bool driving = rover != null && rover.IsDriven, near = rover != null && rover.PlayerNear;
            if (drive.gameObject.activeSelf != driving) { drive.gameObject.SetActive(driving); driveKeys.gameObject.SetActive(driving); }
            if (ridePrompt.gameObject.activeSelf != near) ridePrompt.gameObject.SetActive(near);
            if (rover == null) return;
            float pct = rover.BatteryPercent;
            string key = "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.Accent) + ">";
            if (near) rideText.text = key + "E</color>  Drive rover  ·  " + pct.ToString("F0") + "%" + (rover.IsCharging ? "  ·  charging" : "");
            if (!driving) return;
            speed.text = Mathf.Abs(rover.SpeedKmh).ToString("F0");
            batteryValue.text = pct.ToString("F0") + "%";
            int lit = Mathf.CeilToInt(pct / 100f * Segments);
            Color on = pct < Low ? danger : UiTheme.Info, off = WithAlpha(paper, 0.1f);
            for (int k = 0; k < Segments; k++) battery[k].color = k < lit ? on : off;
            rangeLabel.text = rover.IsCharging ? "Charging" : "Range";
            rangeLabel.color = rover.IsCharging ? UiTheme.Info : muted;
            rangeValue.text = rover.IsCharging ? "full in " + Mathf.CeilToInt((100f - pct) / rover.rechargePercentPerSecond) + " s" : rover.RangeLeftKm.ToString("F0") + " km";
            string message = rover.Message;
            if (!string.IsNullOrEmpty(message)) notice.text = message;
        }

        // ------------------------------------------------------------------ pieces

        private RectTransform Plate(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            FacetedImage plate = rect.gameObject.AddComponent<FacetedImage>();
            plate.KeepAlpha = true;
            plate.color = WithAlpha(UiTheme.Panel, UiTheme.PanelAlpha);
            plate.raycastTarget = false;
            return rect;
        }

        /// <summary>A child rect at (x, y) from the parent's bottom left, in reference pixels.</summary>
        private static RectTransform Region(Transform parent, float x, float y, float width, float height)
        {
            RectTransform rect = NewRect("Region", parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Image Fill(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text Label(RectTransform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            RectTransform rect = NewRect("Text", parent);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
    }
}
