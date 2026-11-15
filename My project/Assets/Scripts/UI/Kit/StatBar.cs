using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class StatBar : MonoBehaviour
    {
        [SerializeField] private TMP_Text caption;
        [SerializeField] private Image fill;
        [SerializeField] private bool compact;
        [SerializeField] private ResourceSymbol symbol;
        public void Show(string label, double value, double maximum = GameConstants.StatMaximum)
        {
            float fraction = maximum > 0 ? Mathf.Clamp01((float)(value / maximum)) : 0;
            caption.text = compact ? (label == "Air" ? "Oxygen" : label) + "  " + value.ToString("0") + "%"
                : label + "  " + value.ToString("0") + " / " + maximum.ToString("0");
            fill.rectTransform.anchorMax = new Vector2(fraction, 1);
            fill.color = ColorOf(fraction <= GameConstants.StatEmergencyThreshold / GameConstants.StatMaximum
                ? GameConstants.UiCritical : fraction <= GameConstants.StatCriticalThreshold / GameConstants.StatMaximum
                ? GameConstants.UiWarn : ResourceGlyph.ColorFor(symbol));
        }

        public static StatBar Create(Transform parent, string label, bool compact = false)
        {
            var owner = Frame(parent, label, 0, 0, 1, 1);
            Height(owner, compact ? GameConstants.UiHudVitalHeight : GameConstants.UiButton);
            if (compact) { var plate=owner.gameObject.AddComponent<FacetedImage>(); plate.color=ColorOf(GameConstants.UiSurface); plate.raycastTarget=false; }
            var bar = owner.gameObject.AddComponent<StatBar>(); bar.compact = compact; bar.symbol = ResourceGlyph.ForLabel(label);
            bar.caption = Text(owner, label, GameConstants.UiBody, GameConstants.UiLine);
            var track = Frame(owner, "Track", 0, 0, 1, 0.16f, GameConstants.UiButtonColor);
            bar.fill = Frame(track, "Fill", 0, 0, 1, 1, GameConstants.UiSafe).GetComponent<Image>();

            if (compact)
            {
                ResourceGlyph.Create(owner,bar.symbol,GameConstants.UiBadgeDiameter);
                bar.caption.rectTransform.anchorMin = new Vector2(.17f,.30f);
                bar.caption.rectTransform.anchorMax = new Vector2(.97f,.98f);
                bar.caption.rectTransform.offsetMin = bar.caption.rectTransform.offsetMax = Vector2.zero;
                track.anchorMin = new Vector2(.18f,.14f); track.anchorMax = new Vector2(.96f,.26f);
            }
            return bar;
        }

        public static Color ColorOf(uint value) => new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
        public static RectTransform Frame(Transform parent, string name, float x0, float y0, float x1, float y1, uint color = 0)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            if (color != 0) { var image = rect.gameObject.AddComponent<FacetedImage>(); image.color = ColorOf(color); image.raycastTarget = false; }
            return rect;
        }
        public static TMP_Text Text(Transform parent, string content, float size = GameConstants.UiBody, float height = GameConstants.UiLine)
        {
            var rect = Frame(parent, content, 0, 0, 1, 1); Height(rect, height);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = UiTypeface.Font;
            text.text = content; text.fontSize = size; text.color = ColorOf(GameConstants.UiText);
            text.lineSpacing = GameConstants.UiTextLineSpacing; text.wordSpacing = GameConstants.UiTextWordSpacing;
            text.enableWordWrapping = true; text.raycastTarget = false; text.richText = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }
        public static void Height(RectTransform rect, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height; element.preferredHeight = height;
        }
        public static void Stack(RectTransform rect)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)GameConstants.UiGap, (int)GameConstants.UiGap, (int)GameConstants.UiGap, (int)GameConstants.UiGap);
            layout.spacing = GameConstants.UiGap; layout.childControlHeight = true;
            layout.childControlWidth = true; layout.childForceExpandHeight = false;
        }
        public static Button Button(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            var rect = Frame(parent, label, 0, 0, 1, 1, GameConstants.UiButtonColor);
            Height(rect, GameConstants.UiButton);
            var image = rect.GetComponent<Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.selectedColor = ColorOf(GameConstants.UiButtonFocus);
            colors.highlightedColor = ColorOf(GameConstants.UiButtonFocus); button.colors = colors;
            var text = Text(rect, label); text.alignment = TextAlignmentOptions.Center;
            text.margin = Vector4.one * GameConstants.UiGap;
            if (action != null) button.onClick.AddListener(action); return button;
        }
    }
}
