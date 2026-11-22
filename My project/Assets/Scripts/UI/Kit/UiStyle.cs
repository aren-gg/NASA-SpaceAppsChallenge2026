using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>
    /// The type rules shared by every screen. Labels and titles are tracked capitals in Chakra Petch
    /// SemiBold; numbers are SemiBold; body text is Chakra Petch Medium. Section IDs are cyan with an
    /// underline, like the signs over the base's doors.
    /// </summary>
    public static class UiStyle
    {
        // The HUD type scale (reference pixels at 1920x1080). Use these instead of loose numbers.
        public const float Caption = 11f;   // gauge scales, fine print
        public const float Label = 13f;     // tracked-capital labels
        public const float Body = 16f;      // HUD body text
        public const float Lead = 18f;      // messages, current task
        public const float Heading = 22f;   // console card headings
        public const float Title = 28f;     // window titles
        public const float Display = 44f;   // the hero readout

        /// <summary>A title: SemiBold in normal case (capitals are kept for short labels only).</summary>
        public static void Titled(TMP_Text text)
        {
            text.font = UiTypeface.Strong;
            text.fontStyle &= ~(FontStyles.UpperCase | FontStyles.Italic);
            text.characterSpacing = 0.5f;
        }

        /// <summary>A number readout: SemiBold, upright.</summary>
        public static void Readout(TMP_Text text)
        {
            text.font = UiTypeface.Strong;
            text.fontStyle &= ~(FontStyles.Italic | FontStyles.UpperCase);
        }

        /// <summary>A label or title: SemiBold tracked capitals.</summary>
        public static void Caps(TMP_Text text, float spacing = 4f)
        {
            text.font = UiTypeface.Strong;
            text.fontStyle |= FontStyles.UpperCase;
            text.characterSpacing = spacing;
        }

        /// <summary>
        /// A cyan ID label with a cyan underline, like the signs over the doors (COMMAND-01, GARAGE-03),
        /// anchored at the top left of <paramref name="parent"/> at (x, y) below its top edge.
        /// </summary>
        public static TMP_Text Id(RectTransform parent, string value, float size, float x, float y)
        {
            TMP_Text text = Text(parent, value, size, UiTheme.Info);
            Caps(text, size * 0.18f);
            float width = text.GetPreferredValues(value.ToUpperInvariant()).x + 2f;
            RectTransform r = text.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(width, size * 1.3f);
            RectTransform rule = Rect("Underline", parent, 0f, 1f, 0f, 1f);
            rule.pivot = new Vector2(0f, 1f);
            rule.anchoredPosition = new Vector2(x, -y - size * 1.4f);
            rule.sizeDelta = new Vector2(width, 2f);
            Image line = rule.gameObject.AddComponent<Image>();
            line.color = UiTheme.Info;
            line.raycastTarget = false;
            return text;
        }

        /// <summary>The dark outline that keeps floating HUD text readable over bright ground.</summary>
        public static void Readable(TMP_Text text)
        {
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(10, 12, 14, 220);
        }

        private static TMP_Text Text(RectTransform parent, string value, float size, Color color)
        {
            TextMeshProUGUI text = Rect("Text", parent, 0f, 0f, 0f, 1f).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = UiTypeface.Strong;   // before Readable: its outline makes a material for this font's atlas
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            Readable(text);
            return text;
        }

        private static void Span(RectTransform rect, float x, float width)
        {
            rect.offsetMin = new Vector2(x, 0f);
            rect.offsetMax = new Vector2(x + width, 0f);
        }

        private static RectTransform Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
