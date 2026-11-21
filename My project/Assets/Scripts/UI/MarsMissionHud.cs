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
    /// The in-game screen: sol and time top left, the three vitals as flat bars bottom left
    /// (with credits and plot count), the latest message, the seed shop, pause and end screens.
    /// The mission starts as soon as the home screen closes.
    /// </summary>
    public sealed class MarsMissionHud : MonoBehaviour
    {
        private const float Low = 25f;
        private MarsMissionController mission;
        private TMP_FontAsset font;
        private Color ink, paper, muted, danger;
        private TMP_Text clock, money, plots, notice;
        private readonly Image[] fills = new Image[3];
        private readonly TMP_Text[] values = new TMP_Text[3];
        private readonly Color[] colors = new Color[3];
        private RectTransform shop;
        private TMP_Text shopCredits, expandRow;
        private TMP_Text[] owned, basketRows;
        private Button[] buy, sell;
        private Button expand;
        private GameObject gameplay;
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
            muted = Color.Lerp(paper, ink, 0.35f);
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

            // Top left: sol and local time. Top right: the keys.
            RectTransform top = Plate(hud, "Clock", new Vector2(0, 1), new Vector2(16, -16), new Vector2(250, 44));
            clock = Label(top, "", 22, paper, TextAlignmentOptions.Center);
            RectTransform keys = Plate(hud, "Keys", new Vector2(1, 1), new Vector2(-16, -16), new Vector2(300, 44));
            Label(keys, "Esc  Menu     F5  Save", 20, muted, TextAlignmentOptions.Center);

            // Bottom left: credits and plots, then oxygen, water and food as straight bars.
            RectTransform vitals = Plate(hud, "Vitals", Vector2.zero, new Vector2(16, 16), new Vector2(380, 168));
            money = Label(Region(vitals, 16, 124, 348, 32), "", 22, paper, TextAlignmentOptions.MidlineLeft);
            plots = Label(Region(vitals, 16, 124, 348, 32), "", 20, muted, TextAlignmentOptions.MidlineRight);
            string[] names = { "Oxygen", "Water", "Food" };
            ResourceSymbol[] symbols = { ResourceSymbol.Air, ResourceSymbol.Water, ResourceSymbol.Food };
            for (int i = 0; i < 3; i++)
            {
                RectTransform row = Region(vitals, 16, 84 - i * 36, 348, 30);
                colors[i] = StatBar.ColorOf(ResourceGlyph.ColorFor(symbols[i]));
                ResourceGlyph icon = ResourceGlyph.Create(row, symbols[i], 18);
                icon.Plain = true;
                icon.rectTransform.anchoredPosition = Vector2.zero;
                Label(Region(row, 26, 0, 80, 30), names[i], 20, paper, TextAlignmentOptions.MidlineLeft);
                RectTransform track = Region(row, 108, 12, 190, 6);
                Fill(track, WithAlpha(paper, 0.15f));
                RectTransform bar = Region(track, 0, 0, 0, 0);
                bar.anchorMax = new Vector2(1, 1);
                fills[i] = Fill(bar, colors[i]);
                values[i] = Label(Region(row, 300, 0, 48, 30), "100", 20, paper, TextAlignmentOptions.MidlineRight);
            }

            // Bottom centre, above the tool hotbar: the latest message.
            RectTransform line = StatBar.Frame(hud, "Message", .25f, .215f, .75f, .25f);
            notice = Label(line, "", 22, paper, TextAlignmentOptions.Center);

            BuildShop(safe);
            pause = PauseMenu.Create(safe);
            pause.Closed = () => mission.SetPaused(false);
            end = EndScreen.Create(safe);
        }

        /// <summary>
        /// The seed shop: a list of item cards, each with its crop icon, grow time, sell price, how many
        /// you own and a price button; then the greenhouse expansion and the harvest to sell.
        /// </summary>
        private void BuildShop(Transform parent)
        {
            Color accent = UiTheme.Accent;
            shop = Plate(parent, "Seed shop", new Vector2(1, .5f), new Vector2(-16, 0), new Vector2(620, 0));
            shop.pivot = new Vector2(1, .5f);
            shop.GetComponent<Image>().color = WithAlpha(ink, 0.94f);
            VerticalLayoutGroup list = shop.gameObject.AddComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset(22, 22, 18, 18);
            list.spacing = 8;
            list.childControlHeight = list.childControlWidth = true;
            list.childForceExpandHeight = false;
            shop.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform head = Line(shop, 56, 0f);
            Label(head, "SEED SHOP", 32, paper, TextAlignmentOptions.MidlineLeft).fontStyle = FontStyles.Bold;
            shopCredits = Label(head, "", 24, accent, TextAlignmentOptions.MidlineRight);
            Fill(Line(shop, 2, 0f), WithAlpha(accent, 0.6f));

            int n = SeedKind.All.Length;
            owned = new TMP_Text[n]; basketRows = new TMP_Text[n]; buy = new Button[n]; sell = new Button[n];
            for (int i = 0; i < n; i++)
            {
                SeedKind k = SeedKind.All[i];
                string id = k.id;
                RectTransform card = Line(shop, 104, 0.06f);
                Icon(card, id, 12, 8, 88);
                RectTransform name = Region(card, 116, 56, 300, 36);
                Label(name, k.name, 27, paper, TextAlignmentOptions.MidlineLeft).fontStyle = FontStyles.Bold;
                Label(Region(card, 116, 30, 320, 26), "Ripe " + Duration(k.seconds) + " after watering  ·  sells " + k.sellPrice, 18, muted, TextAlignmentOptions.MidlineLeft);
                owned[i] = Label(Region(card, 116, 6, 300, 24), "", 18, WithAlpha(accent, 0.9f), TextAlignmentOptions.MidlineLeft);
                buy[i] = ShopButton(card, k.price + " cr", 150, 56, accent, () => mission.Execute("buy", id));
            }
            RectTransform grow = Line(shop, 64, 0.06f);
            expandRow = Label(Region(grow, 16, 0, 380, 64), "", 22, paper, TextAlignmentOptions.MidlineLeft);
            expand = ShopButton(grow, "", 150, 48, accent, () => mission.Execute("buy", "plots"));

            RectTransform harvestHead = Line(shop, 40, 0f);
            Label(harvestHead, "HARVEST", 22, muted, TextAlignmentOptions.BottomLeft);
            for (int i = 0; i < n; i++)
            {
                string id = SeedKind.All[i].id;
                RectTransform row = Line(shop, 60, 0.04f);
                Icon(row, id, 10, 6, 48);
                basketRows[i] = Label(Region(row, 70, 0, 360, 60), "", 21, paper, TextAlignmentOptions.MidlineLeft);
                sell[i] = ShopButton(row, "Sell", 120, 44, StatBar.ColorOf(Ui.UiButtonColor), () => mission.Execute("sell", id));
            }
            RectTransform foot = Line(shop, 52, 0f);
            ShopButton(foot, "Close (Esc)", 180, 44, StatBar.ColorOf(Ui.UiButtonColor), () => mission.SetShopOpen(false));
            shop.gameObject.SetActive(false);
        }

        /// <summary>Greys out a button you can't use (can't afford, nothing to sell), not by colour alone: it stops clicking too.</summary>
        private void Enable(Button button, bool on, Color fill)
        {
            button.interactable = on;
            button.targetGraphic.color = on ? fill : WithAlpha(muted, 0.35f);
            button.GetComponentInChildren<TMP_Text>().color = on ? (fill.grayscale > 0.5f ? ink : paper) : WithAlpha(paper, 0.5f);
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
            Fill(tile, WithAlpha(ink, 0.8f));
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
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = fill;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock states = ColorBlock.defaultColorBlock;
            states.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);
            button.colors = states;
            button.onClick.AddListener(() => { click(); MarsSfx.Play(MarsSfx.Sound.Click); });
            Label(rect, label, 22, fill.grayscale > 0.5f ? ink : paper, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            return button;
        }

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            MissionState s = mission.Simulation.State;
            AstronautState a = s.astronaut;
            int sol = (int)(s.elapsedSeconds / MissionSettings.SolSeconds) + 1;
            double hour = mission.Environment.localHour;
            clock.text = "Sol " + sol + "  ·  " + ((int)hour).ToString("00") + ":" + ((int)(hour % 1 * 60)).ToString("00");
            money.text = s.credits + " credits";
            plots.text = s.unlockedPlots + " / " + MissionSettings.MaxPlots + " plots";
            double[] v = { a.oxygen, a.water, a.food };
            for (int i = 0; i < 3; i++)
            {
                float f = Mathf.Clamp01((float)v[i] / 100f);
                fills[i].rectTransform.anchorMax = new Vector2(f, 1);
                fills[i].color = v[i] < Low ? danger : colors[i];
                values[i].text = Mathf.CeilToInt((float)v[i]).ToString(CultureInfo.InvariantCulture);
                values[i].color = v[i] < Low ? danger : paper;
            }
            notice.text = mission.Simulation.Asleep ? "Sleeping until 07:00..." : mission.Notice;

            bool open = mission.ShopOpen;
            if (shop.gameObject.activeSelf != open) shop.gameObject.SetActive(open);
            if (open)
            {
                MissionSimulation sim = mission.Simulation;
                shopCredits.text = s.credits + " credits";
                for (int i = 0; i < SeedKind.All.Length; i++)
                {
                    SeedKind k = SeedKind.All[i];
                    owned[i].text = s.seeds[i] > 0 ? "Owned  ×" + s.seeds[i] : "None owned";
                    Enable(buy[i], s.credits >= k.price, UiTheme.Accent);
                    basketRows[i].text = k.name + "  ×" + s.basket[i] + "<pos=55%><color=#" + ColorUtility.ToHtmlStringRGB(muted) + ">+" + k.sellPrice + " each</color>";
                    Enable(sell[i], s.basket[i] > 0, StatBar.ColorOf(Ui.UiButtonColor));
                }
                expandRow.text = sim.CanExpand ? "Greenhouse  ·  add " + MissionSettings.PlotsPerExpansion + " plots" : "Greenhouse full";
                Enable(expand, sim.CanExpand && s.credits >= sim.ExpansionPrice, UiTheme.Accent);
                expand.GetComponentInChildren<TMP_Text>().text = sim.CanExpand ? sim.ExpansionPrice + " cr" : "—";
            }

            if (!mission.IsRunning && !ended)
            {
                ended = true;
                end.Show(s.endingReason, s.elapsedSeconds / MissionSettings.SolSeconds);
            }
            if (mission.IsRunning) ended = false;
        }

        // ------------------------------------------------------------------ pieces

        private RectTransform Plate(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Fill(rect, WithAlpha(ink, 0.72f));
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
