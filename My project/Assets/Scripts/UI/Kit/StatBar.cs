using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>Shared uGUI building helpers (frames, text, buttons) for the kit screens.</summary>
    public static class StatBar
    {
        /// <summary>
        /// Button states for the dark theme. UGUI multiplies these with the button colour, so
        /// hover brightens the slot clearly and press flashes it Mars orange.
        /// </summary>
        public static ColorBlock UiStates
        {
            get
            {
                ColorBlock c = ColorBlock.defaultColorBlock;
                c.normalColor = Color.white;
                c.highlightedColor = new Color(1.6f, 1.65f, 1.75f, 1f);
                c.selectedColor = c.highlightedColor;
                c.pressedColor = new Color(3.2f, 1.7f, 1.05f, 1f);
                c.disabledColor = new Color(1f, 1f, 1f, 0.4f);
                c.colorMultiplier = 1f;
                c.fadeDuration = 0.08f;
                return c;
            }
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
            bool readout = size < GameConstants.UiHeading && UiTypeface.LooksNumeric(content);
            text.font = UiTypeface.Font;
            if (size >= GameConstants.UiHeading) UiStyle.Titled(text);
            else if (readout) UiStyle.Readout(text);
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
            button.colors = UiStates;
            var text = Text(rect, label); text.alignment = TextAlignmentOptions.Center;
            text.margin = Vector4.one * GameConstants.UiGap;
            if (action != null) button.onClick.AddListener(action); return button;
        }
    }
}
