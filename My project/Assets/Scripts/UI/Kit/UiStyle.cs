using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>
    /// The NASA-punk type rules shared by every screen. Labels and titles are tracked capitals in
    /// Barlow Condensed SemiBold; numbers are SemiBold italic so they lean with the slanted gauges;
    /// body text stays Barlow Condensed Medium. Section headers carry an amber hazard-stripe tab.
    /// </summary>
    public static class UiStyle
    {
        /// <summary>A number readout: SemiBold, italic, amber unless the caller recolours it.</summary>
        public static void Readout(TMP_Text text)
        {
            text.font = UiTypeface.Strong;
            text.fontStyle = (text.fontStyle | FontStyles.Italic) & ~FontStyles.UpperCase;
        }

        /// <summary>A label or title: SemiBold tracked capitals.</summary>
        public static void Caps(TMP_Text text, float spacing = 4f)
        {
            text.font = UiTypeface.Strong;
            text.fontStyle |= FontStyles.UpperCase;
            text.characterSpacing = spacing;
        }

        /// <summary>
        /// A section header across the top of <paramref name="parent"/>: hazard tab, amber title,
        /// a dim sub-label after it and a hairline rule to the right edge. Returns the sub-label.
        /// </summary>
        public static TMP_Text Header(RectTransform parent, string title, string sub, float size, float y0, float y1)
        {
            RectTransform row = Rect("Header", parent, 0f, y0, 1f, y1);

            RectTransform tab = Rect("Hazard tab", row, 0f, 0.5f, 0f, 0.5f);
            tab.pivot = new Vector2(0f, 0.5f);
            tab.sizeDelta = new Vector2(size * 1.9f, size * 0.55f);
            HazardTab stripes = tab.gameObject.AddComponent<HazardTab>();
            stripes.color = UiTheme.Accent;
            stripes.raycastTarget = false;

            float x = size * 1.9f + size * 0.6f;
            TMP_Text head = Text(row, title, size, UiTheme.Accent);
            Caps(head, size * 0.25f);
            float headWidth = head.GetPreferredValues(title.ToUpperInvariant()).x + 2f;
            Span(head.rectTransform, x, headWidth);
            x += headWidth + size * 0.5f;

            TMP_Text tail = Text(row, sub, size * 0.9f, UiTheme.Dim);
            Caps(tail, size * 0.12f);
            tail.font = UiTypeface.Font;
            float tailWidth = Mathf.Max(60f, tail.GetPreferredValues(sub.ToUpperInvariant()).x + 30f);
            Span(tail.rectTransform, x, tailWidth);
            x += tailWidth + size * 0.4f;

            RectTransform rule = Rect("Rule", row, 0f, 0.5f, 1f, 0.5f);
            rule.offsetMin = new Vector2(x, -0.75f);
            rule.offsetMax = new Vector2(0f, 0.75f);
            Image line = rule.gameObject.AddComponent<Image>();
            line.color = UiTheme.WithAlpha(UiTheme.Text, 0.35f);
            line.raycastTarget = false;
            return tail;
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
