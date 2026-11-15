using System.Collections.Generic;
using MarsSurvival.Tools;
using MarsSurvival.World;
using UnityEngine;
using UnityEngine.UI;

namespace MarsSurvival.UI
{
    /// <summary>
    /// On-screen display for the tools, built in code with uGUI and the palette, and
    /// written for players aged 7-12:
    /// a "what to do next" card, a numbered hotbar of the 10 tools, a crosshair with a
    /// progress ring and "CLICK to ..." prompts, a card describing whatever you look at,
    /// oxygen / research / sample-bag stats, the held tool's readout, messages, floating
    /// "+research" pop-ups, waypoints, a low-oxygen warning glow and a camera viewfinder.
    /// Only shown while walking as the astronaut.
    /// </summary>
    public sealed class MarsToolHud : MonoBehaviour
    {
        private const int ToastCount = 4;
        private const int WaypointCount = 4;
        private const int PopupCount = 6;
        private const float ToastSeconds = 7f;
        private const float SlotSize = 88f;
        private const float SlotGap = 12f;

        private MarsExpedition expedition;
        private MarsToolBelt belt;
        private Font font;
        private Sprite round, ring, circle, disc;

        private Canvas canvas;
        private RectTransform canvasRect;
        private Image crosshair, crosshairRing, progressRing;
        private RectTransform promptRect;
        private Text prompt, secondary;
        private RectTransform lookCard;
        private Text lookTitle, lookBody;
        private RectTransform nextCard;
        private Image nextBackground;
        private Text nextBody;
        private string lastNext = "";
        private float nextChangedAt = -10f;
        private Text toolName, toolHint;
        private RectTransform[] slots;
        private Image[] slotBackgrounds, slotRings;
        private Image oxygenFill;
        private Text oxygenLabel, researchLabel, bagLabel, extrasLabel, radiationLabel;
        private RectTransform readoutCard;
        private Text readoutTitle, readoutBody;
        private readonly List<Image> toastCards = new List<Image>();
        private readonly List<Text> toastTexts = new List<Text>();
        private readonly List<RectTransform> waypoints = new List<RectTransform>();
        private readonly List<Image> waypointIcons = new List<Image>();
        private readonly List<Text> waypointTexts = new List<Text>();
        private readonly List<Text> popups = new List<Text>();
        private Image flash;
        private RawImage warning;
        private RectTransform viewfinder;
        private int lastResearch;
        private float researchBumpAt = -10f;

        private void Start()
        {
            expedition = GetComponent<MarsExpedition>();
            belt = GetComponent<MarsToolBelt>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            round = RoundedSprite(64, 18f, 0f);
            ring = RoundedSprite(64, 18f, 3f);
            circle = CircleSprite(128, 0.36f);
            disc = CircleSprite(64, 0f);
        }

        // ------------------------------------------------------------------ layout

        private void Build()
        {
            GameObject root = new GameObject("Tool HUD", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            canvasRect = (RectTransform)root.transform;

            Color pink = MarsPalette.PalePink;
            Color purple = MarsPalette.DeepPurple;
            Color orange = MarsPalette.WarmOrange;

            // Low-oxygen glow around the screen edges (drawn first, under everything).
            warning = NewRect("Low Oxygen Glow", canvasRect, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            Stretch(warning.rectTransform);
            warning.texture = VignetteTexture();
            warning.color = WithAlpha(MarsPalette.Terracotta, 0f);
            warning.raycastTarget = false;

            // Camera viewfinder: four corner brackets.
            viewfinder = NewRect("Viewfinder", canvasRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 460f));
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = new Vector2(i % 2 == 0 ? -1f : 1f, i < 2 ? 1f : -1f);
                Vector2 at = new Vector2(corner.x * 380f, corner.y * 230f);
                Panel(viewfinder, "Across", round, new Vector2(0.5f, 0.5f), at - new Vector2(corner.x * 30f, 0f), new Vector2(64f, 6f), WithAlpha(pink, 0.8f)).pixelsPerUnitMultiplier = 8f;
                Panel(viewfinder, "Down", round, new Vector2(0.5f, 0.5f), at - new Vector2(0f, corner.y * 30f), new Vector2(6f, 64f), WithAlpha(pink, 0.8f)).pixelsPerUnitMultiplier = 8f;
            }

            // Waypoints.
            for (int i = 0; i < WaypointCount; i++)
            {
                RectTransform marker = NewRect("Waypoint", canvasRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 70f));
                Image icon = Panel(marker, "Pin", disc, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(22f, 22f), orange);
                Panel(icon.rectTransform, "Dot", disc, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f), purple);
                Text label = Label(marker, "", 20, pink, FontStyle.Bold, TextAnchor.UpperCenter);
                Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(360f, 26f));
                AddShadow(label);
                waypoints.Add(marker);
                waypointIcons.Add(icon);
                waypointTexts.Add(label);
            }

            // Crosshair with a progress ring.
            crosshairRing = Panel(canvasRect, "Crosshair Ring", ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f), pink);
            crosshairRing.pixelsPerUnitMultiplier = 2.2f;
            crosshair = Panel(canvasRect, "Crosshair", disc, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f), pink);
            progressRing = Panel(canvasRect, "Progress", circle, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f), orange);
            Image track = Panel(progressRing.rectTransform, "Track", circle, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f), WithAlpha(purple, 0.55f));
            Image fill = Panel(progressRing.rectTransform, "Fill", circle, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f), orange);
            progressRing.color = Color.clear; // the parent only groups the track and the fill
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            track.transform.SetAsFirstSibling();
            progressRing = fill;

            promptRect = NewRect("Prompt", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -64f), new Vector2(1000f, 36f));
            prompt = Label(promptRect, "", 26, pink, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(prompt.rectTransform);
            AddShadow(prompt);
            secondary = Label(canvasRect, "", 21, WithAlpha(pink, 0.85f), FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(secondary.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -98f), new Vector2(1000f, 28f));
            AddShadow(secondary);

            // Card describing what the crosshair is on.
            Image look = Panel(canvasRect, "Look Card", round, new Vector2(0.5f, 0.5f), new Vector2(70f, 20f), new Vector2(420f, 100f), WithAlpha(purple, 0.82f));
            look.rectTransform.pivot = new Vector2(0f, 0.5f);
            lookCard = look.rectTransform;
            lookTitle = Label(lookCard, "", 22, orange, FontStyle.Bold, TextAnchor.UpperLeft);
            Place(lookTitle.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(390f, 28f), new Vector2(0f, 1f));
            lookBody = Label(lookCard, "", 20, pink, FontStyle.Normal, TextAnchor.UpperLeft);
            lookBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            lookBody.lineSpacing = 1.1f;
            Place(lookBody.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -42f), new Vector2(384f, 200f), new Vector2(0f, 1f));

            // "What to do next", top-left.
            nextBackground = Panel(canvasRect, "Next Card", round, new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(520f, 110f), WithAlpha(purple, 0.82f));
            nextBackground.rectTransform.pivot = new Vector2(0f, 1f);
            nextCard = nextBackground.rectTransform;
            Text nextTitle = Label(nextCard, "WHAT TO DO NEXT", 18, orange, FontStyle.Bold, TextAnchor.UpperLeft);
            Place(nextTitle.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(480f, 24f), new Vector2(0f, 1f));
            nextBody = Label(nextCard, "", 23, pink, FontStyle.Normal, TextAnchor.UpperLeft);
            nextBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            nextBody.lineSpacing = 1.1f;
            Place(nextBody.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -42f), new Vector2(480f, 200f), new Vector2(0f, 1f));

            // Hotbar with key numbers.
            int count = belt.Tools.Length;
            float width = count * SlotSize + (count - 1) * SlotGap;
            RectTransform bar = NewRect("Hotbar", canvasRect, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(width, SlotSize));
            bar.pivot = new Vector2(0.5f, 0f);
            slots = new RectTransform[count];
            slotBackgrounds = new Image[count];
            slotRings = new Image[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform slot = NewRect("Slot " + MarsTool.KeyOf(belt.Tools[i].Id), bar, new Vector2(0f, 0.5f),
                                             new Vector2(i * (SlotSize + SlotGap) + SlotSize * 0.5f, 0f), new Vector2(SlotSize, SlotSize));
                slotBackgrounds[i] = slot.gameObject.AddComponent<Image>();
                slotBackgrounds[i].sprite = round;
                slotBackgrounds[i].type = Image.Type.Sliced;
                slotBackgrounds[i].raycastTarget = false;
                RawImage icon = NewRect("Icon", slot, new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(SlotSize - 12f, SlotSize - 12f)).gameObject.AddComponent<RawImage>();
                icon.texture = belt.Tools[i].Icon;
                icon.raycastTarget = false;
                slotRings[i] = Panel(slot, "Ring", ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotSize + 8f, SlotSize + 8f), orange);
                Image chip = Panel(slot, "Key", disc, new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(28f, 28f), orange);
                Text key = Label(chip.rectTransform, MarsTool.KeyOf(belt.Tools[i].Id), 18, purple, FontStyle.Bold, TextAnchor.MiddleCenter);
                Stretch(key.rectTransform);
                slots[i] = slot;
            }

            toolName = Label(canvasRect, "", 30, pink, FontStyle.Bold, TextAnchor.LowerCenter);
            Place(toolName.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 166f), new Vector2(1000f, 38f));
            AddShadow(toolName);
            toolHint = Label(canvasRect, "", 21, WithAlpha(pink, 0.9f), FontStyle.Normal, TextAnchor.LowerCenter);
            Place(toolHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 134f), new Vector2(1200f, 30f));
            AddShadow(toolHint);

            // Stats, bottom-left.
            Image stats = Panel(canvasRect, "Stats", round, new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(360f, 196f), WithAlpha(purple, 0.82f));
            stats.rectTransform.pivot = Vector2.zero;
            RectTransform s = stats.rectTransform;
            oxygenLabel = Label(s, "", 21, pink, FontStyle.Bold, TextAnchor.UpperLeft);
            Place(oxygenLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(320f, 26f), new Vector2(0f, 1f));
            Image oxygenTrack = Panel(s, "Oxygen Track", round, new Vector2(0f, 1f), new Vector2(20f, -46f), new Vector2(320f, 16f), WithAlpha(MarsPalette.Mauve, 0.8f));
            oxygenTrack.rectTransform.pivot = new Vector2(0f, 1f);
            oxygenTrack.pixelsPerUnitMultiplier = 4f;
            oxygenFill = Panel(oxygenTrack.rectTransform, "Oxygen Fill", round, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(320f, 16f), orange);
            oxygenFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            oxygenFill.pixelsPerUnitMultiplier = 4f;
            researchLabel = Label(s, "", 22, orange, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(researchLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -88f), new Vector2(320f, 28f), new Vector2(0f, 0.5f));
            bagLabel = Label(s, "", 20, pink, FontStyle.Normal, TextAnchor.UpperLeft);
            Place(bagLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -106f), new Vector2(320f, 26f), new Vector2(0f, 1f));
            extrasLabel = Label(s, "", 19, WithAlpha(pink, 0.85f), FontStyle.Normal, TextAnchor.UpperLeft);
            Place(extrasLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -134f), new Vector2(320f, 24f), new Vector2(0f, 1f));
            radiationLabel = Label(s, "", 19, WithAlpha(pink, 0.85f), FontStyle.Normal, TextAnchor.UpperLeft);
            Place(radiationLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -160f), new Vector2(320f, 24f), new Vector2(0f, 1f));

            // Tool readout, bottom-right.
            Image readout = Panel(canvasRect, "Readout", round, new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(420f, 120f), WithAlpha(purple, 0.82f));
            readout.rectTransform.pivot = new Vector2(1f, 0f);
            readoutCard = readout.rectTransform;
            readoutTitle = Label(readoutCard, "", 19, orange, FontStyle.Bold, TextAnchor.UpperLeft);
            Place(readoutTitle.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(380f, 24f), new Vector2(0f, 1f));
            readoutBody = Label(readoutCard, "", 20, pink, FontStyle.Normal, TextAnchor.UpperLeft);
            readoutBody.lineSpacing = 1.15f;
            Place(readoutBody.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -44f), new Vector2(380f, 200f), new Vector2(0f, 1f));

            // Messages, top centre.
            for (int i = 0; i < ToastCount; i++)
            {
                Image card = Panel(canvasRect, "Message", round, new Vector2(0.5f, 1f), new Vector2(0f, -28f - i * 52f), new Vector2(600f, 44f), WithAlpha(purple, 0.85f));
                card.rectTransform.pivot = new Vector2(0.5f, 1f);
                Text text = Label(card.rectTransform, "", 21, pink, FontStyle.Normal, TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
                toastCards.Add(card);
                toastTexts.Add(text);
            }

            // Floating "+research" pop-ups.
            for (int i = 0; i < PopupCount; i++)
            {
                Text popup = Label(canvasRect, "", 32, orange, FontStyle.Bold, TextAnchor.MiddleCenter);
                Place(popup.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 40f));
                AddShadow(popup);
                popups.Add(popup);
            }

            flash = Panel(canvasRect, "Camera Flash", null, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0f));
            Stretch(flash.rectTransform);
            lastResearch = expedition.Research;
        }

        // ------------------------------------------------------------------ update

        private void LateUpdate()
        {
            if (belt == null || belt.Tools == null || font == null || expedition == null) return;
            if (canvas == null) Build();
            canvas.enabled = belt.Active;
            if (!belt.Active) return;

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            Color pink = MarsPalette.PalePink;
            Color orange = MarsPalette.WarmOrange;
            MarsTool tool = belt.Current;
            ToolAction plan = belt.Plan;
            string orangeHex = "#" + ColorUtility.ToHtmlStringRGB(orange);
            float ease = 1f - Mathf.Exp(-15f * dt);

            // Crosshair, progress ring and prompt.
            bool working = belt.Working != null;
            bool usable = plan != null && !plan.Blocked;
            crosshairRing.color = Color.Lerp(crosshairRing.color, usable ? orange : WithAlpha(pink, 0.4f), ease);
            float ringScale = working ? 0f : usable ? 1f + 0.08f * Mathf.Sin(now * 6f) : 0.8f;
            crosshairRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(crosshairRing.rectTransform.localScale.x, ringScale, ease);
            progressRing.transform.parent.gameObject.SetActive(working);
            progressRing.fillAmount = working ? Mathf.MoveTowards(progressRing.fillAmount, belt.Progress, dt * 3f) : 0f;
            progressRing.color = belt.AimLost ? WithAlpha(pink, 0.6f) : orange;
            float pop = Mathf.Clamp01(1f - (now - belt.CompletedAt) / 0.3f);
            crosshair.rectTransform.localScale = Vector3.one * (1f + pop * 1.5f);
            crosshair.color = pop > 0f ? orange : pink;

            if (working)
            {
                prompt.text = belt.AimLost ? "Keep aiming at it!" : Capitalize(belt.Working.Label) + "...";
                prompt.color = belt.AimLost ? WithAlpha(pink, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(now * 8f))) : orange;
            }
            else if (plan == null)
            {
                prompt.text = "";
            }
            else if (plan.Blocked)
            {
                prompt.text = plan.Label;
                prompt.color = WithAlpha(pink, 0.8f);
            }
            else
            {
                prompt.text = "<b><color=" + orangeHex + ">CLICK</color></b>  to " + plan.Label;
                prompt.color = pink;
            }
            float shake = Mathf.Clamp01(1f - (now - belt.RefusedAt) / 0.35f);
            promptRect.anchoredPosition = new Vector2(Mathf.Sin(now * 60f) * 10f * shake, -64f);
            secondary.text = belt.SecondaryLabel != null && !working
                ? "<b><color=" + orangeHex + ">R</color></b>  to " + belt.SecondaryLabel : "";

            // What the crosshair is on.
            string title = null, body = null;
            bool hasLook = !working && MarsLookInfo.Describe(belt.Target, expedition, out title, out body);
            lookCard.gameObject.SetActive(hasLook);
            if (hasLook)
            {
                lookTitle.text = title;
                lookBody.text = body;
                lookCard.sizeDelta = new Vector2(420f, 56f + lookBody.preferredHeight);
            }

            // What to do next.
            string next = expedition.NextStep;
            if (next != lastNext)
            {
                lastNext = next;
                nextChangedAt = now;
            }
            nextBody.text = next;
            nextCard.gameObject.SetActive(!string.IsNullOrEmpty(next));
            nextCard.sizeDelta = new Vector2(520f, 58f + nextBody.preferredHeight);
            float glow = Mathf.Clamp01(1f - (now - nextChangedAt) / 0.8f);
            nextBackground.color = Color.Lerp(WithAlpha(MarsPalette.DeepPurple, 0.82f), WithAlpha(MarsPalette.Mauve, 0.95f), glow);
            nextCard.localScale = Vector3.one * (1f + 0.04f * glow);

            // Hotbar.
            float slotEase = 1f - Mathf.Exp(-18f * dt);
            for (int i = 0; i < slots.Length; i++)
            {
                bool selected = i == belt.Selected;
                float scale = Mathf.Lerp(slots[i].localScale.x, selected ? 1.15f : 1f, slotEase);
                slots[i].localScale = new Vector3(scale, scale, 1f);
                Vector2 p = slots[i].anchoredPosition;
                p.y = Mathf.Lerp(p.y, selected ? 10f : 0f, slotEase);
                slots[i].anchoredPosition = p;
                slotBackgrounds[i].color = selected ? MarsPalette.Mauve : WithAlpha(MarsPalette.DeepPurple, 0.75f);
                slotRings[i].enabled = selected;
            }
            toolName.text = tool.Name;
            toolHint.text = tool.Hint;
            toolHint.color = WithAlpha(pink, 0.9f * Mathf.Clamp01(1f - (now - belt.SwitchedAt - 5f) / 1f));

            // Stats.
            float oxygen = expedition.SuitOxygen;
            bool low = oxygen < 25f;
            bool atCamp = expedition.InsideCamp(expedition.PlayerPosition);
            oxygenLabel.text = "Oxygen  " + Mathf.RoundToInt(oxygen) + "%" + (atCamp && oxygen < 99.5f ? "   (filling up at camp)" : "");
            oxygenFill.rectTransform.sizeDelta = new Vector2(320f * oxygen / 100f, 16f);
            oxygenFill.color = low && Mathf.Repeat(now, 0.8f) < 0.4f ? MarsPalette.Terracotta : orange;
            if (expedition.Research != lastResearch)
            {
                lastResearch = expedition.Research;
                researchBumpAt = now;
            }
            float bump = Mathf.Clamp01(1f - (now - researchBumpAt) / 0.5f);
            researchLabel.text = "Research points:  " + expedition.Research;
            researchLabel.rectTransform.localScale = Vector3.one * (1f + 0.15f * bump);
            string bag = "";
            for (int i = 0; i < MarsExpedition.BagCapacity; i++) bag += i < expedition.Samples.Count ? "■ " : "□ ";
            bagLabel.text = "Sample bag:  " + bag;
            extrasLabel.text = "Soil bags " + expedition.RegolithBags + "/" + MarsExpedition.MaxRegolithBags +
                               "   ·   O2 canister " + expedition.CanisterCharges + "/" + MarsExpedition.MaxCanisterCharges;
            radiationLabel.text = "Radiation: " + expedition.RadiationWord;
            radiationLabel.color = expedition.RadiationHigh ? orange : WithAlpha(pink, 0.85f);

            // Low-oxygen glow.
            float danger = Mathf.Clamp01((25f - oxygen) / 25f);
            warning.color = WithAlpha(warning.color, danger * (0.45f + 0.25f * Mathf.Sin(now * 5f)));

            // Readout.
            string readout = belt.Readout;
            readoutCard.gameObject.SetActive(!string.IsNullOrEmpty(readout));
            if (!string.IsNullOrEmpty(readout))
            {
                readoutTitle.text = tool.Name.ToUpperInvariant();
                readoutBody.text = readout;
                readoutCard.sizeDelta = new Vector2(420f, 60f + readoutBody.preferredHeight);
            }

            viewfinder.gameObject.SetActive(tool.Pose == ToolPose.Camera);
            float frame = Mathf.Lerp(viewfinder.localScale.x, belt.Zoomed ? 1.15f : 1f, ease);
            viewfinder.localScale = new Vector3(frame, frame, 1f);

            UpdateToasts(now);
            UpdateWaypoints();
            UpdatePopups(now);

            float flashAlpha = Mathf.Clamp01(1f - (now - belt.FlashAt) / 0.35f);
            flash.color = new Color(1f, 1f, 1f, flashAlpha * 0.85f);
        }

        private void UpdateToasts(float now)
        {
            List<MarsExpedition.Message> messages = expedition.Messages;
            int shown = 0;
            for (int m = messages.Count - 1; m >= 0 && shown < ToastCount; m--)
            {
                float age = now - messages[m].time;
                if (age > ToastSeconds) break;
                Image card = toastCards[shown];
                Text text = toastTexts[shown];
                card.gameObject.SetActive(true);
                text.text = messages[m].text;
                float alpha = Mathf.Clamp01((ToastSeconds - age) / 1f) * Mathf.Clamp01(age / 0.15f);
                MarsExpedition.Tone tone = messages[m].tone;
                Color textColor = tone == MarsExpedition.Tone.Good ? MarsPalette.WarmOrange
                                : tone == MarsExpedition.Tone.Radio ? MarsPalette.ClaySand : MarsPalette.PalePink;
                text.color = WithAlpha(textColor, alpha);
                Color background = tone == MarsExpedition.Tone.Warning ? MarsPalette.Terracotta : MarsPalette.DeepPurple;
                card.color = WithAlpha(background, 0.85f * alpha);
                float slide = 1f - Mathf.Clamp01(age / 0.2f);
                card.rectTransform.anchoredPosition = new Vector2(0f, -28f - shown * 52f + slide * 20f);
                card.rectTransform.sizeDelta = new Vector2(Mathf.Min(1500f, text.preferredWidth + 44f), 44f);
                shown++;
            }
            for (int i = shown; i < ToastCount; i++) toastCards[i].gameObject.SetActive(false);
        }

        private bool ToCanvas(Vector3 world, out Vector2 local, out bool behind)
        {
            Camera cam = belt.ViewCamera;
            local = Vector2.zero;
            behind = false;
            if (cam == null) return false;
            Vector3 screen = cam.WorldToScreenPoint(world);
            behind = screen.z <= 0f;
            if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        }

        private void UpdateWaypoints()
        {
            List<MarsExpedition.Waypoint> points = expedition.Waypoints;
            int used = 0;
            Camera cam = belt.ViewCamera;
            for (int i = 0; i < points.Count && used < WaypointCount && cam != null; i++)
            {
                float meters = expedition.Meters(Vector3.Distance(cam.transform.position, points[i].position));
                if (meters < 6f) continue;
                Vector2 local;
                bool behind;
                if (!ToCanvas(points[i].position, out local, out behind)) continue;
                // Off-screen markers stick to the edge so the player knows which way to turn.
                Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(120f, 150f);
                if (behind) local = local.normalized * 10000f;
                local.x = Mathf.Clamp(local.x, -half.x, half.x);
                local.y = Mathf.Clamp(local.y, -half.y, half.y);
                RectTransform marker = waypoints[used];
                marker.gameObject.SetActive(true);
                marker.anchoredPosition = local;
                Color color = MarsLowPoly.ColorOf(points[i].paint);
                waypointIcons[used].color = color;
                waypointIcons[used].rectTransform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f));
                waypointTexts[used].text = points[i].label + "  " + Mathf.RoundToInt(meters) + " m";
                waypointTexts[used].color = color;
                used++;
            }
            for (int i = used; i < WaypointCount; i++) waypoints[i].gameObject.SetActive(false);
        }

        private void UpdatePopups(float now)
        {
            List<MarsExpedition.Popup> list = expedition.Popups;
            int used = 0;
            for (int i = list.Count - 1; i >= 0 && used < PopupCount; i--)
            {
                float age = now - list[i].time;
                if (age > 1.6f) continue;
                Vector2 local = new Vector2(0f, 60f);
                bool behind = false;
                if (list[i].hasWorld && (!ToCanvas(list[i].world, out local, out behind) || behind)) local = new Vector2(0f, 60f);
                Text text = popups[used];
                text.gameObject.SetActive(true);
                text.text = list[i].text;
                text.rectTransform.anchoredPosition = local + new Vector2(0f, 30f + age * 70f + used * 36f);
                float alpha = Mathf.Clamp01((1.6f - age) / 0.5f);
                text.color = WithAlpha(MarsPalette.WarmOrange, alpha);
                text.rectTransform.localScale = Vector3.one * (1f + 0.4f * Mathf.Clamp01(1f - age / 0.2f));
                used++;
            }
            for (int i = used; i < PopupCount; i++) popups[i].gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ helpers

        private static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            if (pivot.HasValue) rect.pivot = pivot.Value;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Panel(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            Image image = NewRect(name, parent, anchor, position, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Label(Transform parent, string content, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            Text text = NewRect("Text", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 30f)).gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void AddShadow(Text text)
        {
            Shadow shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = WithAlpha(MarsPalette.DeepPurple, 0.85f);
            shadow.effectDistance = new Vector2(0f, -2f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>White rounded square (or ring) with soft edges, 9-sliced so it fits any size.</summary>
        private static Sprite RoundedSprite(int size, float radius, float ringWidth)
        {
            Texture2D texture = NewTexture(size, size);
            float half = size * 0.5f;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - distance);
                    if (ringWidth > 0f) alpha *= Mathf.Clamp01(distance + ringWidth + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = Mathf.Ceil(radius) + 1f;
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>A soft-edged disc, or a ring when <paramref name="hole"/> (fraction of the radius) is above zero.</summary>
        private static Sprite CircleSprite(int size, float hole)
        {
            Texture2D texture = NewTexture(size, size);
            float half = size * 0.5f;
            float inner = (half - 1f) * (1f - hole);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                    float alpha = Mathf.Clamp01(half - 1f - d + 0.5f);
                    if (hole > 0f) alpha *= Mathf.Clamp01(d - inner + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Clear in the middle, solid at the edges: used for the low-oxygen glow.</summary>
        private static Texture2D VignetteTexture()
        {
            const int Size = 128;
            Texture2D texture = NewTexture(Size, Size);
            Color32[] pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size * 2f - 1f;
                    float dy = (y + 0.5f) / Size * 2f - 1f;
                    float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + new Vector2(dx, dy).magnitude * 0.4f;
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, d));
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D NewTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;
            return texture;
        }
    }
}
