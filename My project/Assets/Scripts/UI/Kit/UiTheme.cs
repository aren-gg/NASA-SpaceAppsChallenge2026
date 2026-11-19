using UnityEngine;

namespace MarsTrainer.UI
{
    /// <summary>
    /// The game's one UI look, shared by every screen: NASA-punk mission hardware. Flat
    /// semi-transparent smoky plates with cut corners, a hairline edge and an amber stencil bar;
    /// slanted segment gauges (<see cref="SegmentGauge"/>); hazard-stripe tabs on headers;
    /// off-white text, amber readouts and red only for danger. Type rules live in
    /// <see cref="UiStyle"/>: Barlow Condensed throughout, tracked capitals for labels and
    /// SemiBold italic for numbers.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Ink = Hex(0x15181C);      // deepest tone, text on amber
        public static readonly Color Panel = Hex(0x2A2E33);    // smoky plate fill
        public static readonly Color Raised = Hex(0x3A3F46);   // buttons and slots
        public static readonly Color Hover = Hex(0x4C535B);
        public static readonly Color SteelLight = Hex(0xA9B0B7);  // frame bevel, lit edge
        public static readonly Color SteelMid = Hex(0x6B7179);
        public static readonly Color SteelDark = Hex(0x33383E);   // frame bevel, shadow edge
        public static readonly Color Line = new Color(0.66f, 0.69f, 0.72f, 0.55f);  // dividers
        public static readonly Color Text = Hex(0xECE6DA);
        public static readonly Color Dim = Hex(0xB3AC9F);
        public static readonly Color Accent = Hex(0xFFB547);   // amber CRT
        public static readonly Color Safe = Hex(0x8FD694);
        public static readonly Color Warn = Hex(0xFFD166);
        public static readonly Color Danger = Hex(0xFF5A4A);
        public static readonly Color Critical = Hex(0xFF3B3B);

        /// <summary>Plates are semi-transparent smoky glass.</summary>
        public const float PanelAlpha = 0.66f;
        /// <summary>Full-screen menus a little denser, so long text stays easy to read.</summary>
        public const float ScreenAlpha = 0.9f;
        /// <summary>Corner cut of panels and slots, in reference pixels.</summary>
        public const float Cut = 6f;
        /// <summary>Steel frame width on large panels (small ones get two thirds).</summary>
        public const float Frame = 4f;
        public const float Hairline = 1.5f;

        public static Color Hex(uint rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        private static Sprite slotFill, slotRing;

        /// <summary>
        /// A nine-sliced slot: top-left and bottom-right corners cut, filled (tint it with the
        /// Image colour) or, with <paramref name="ring"/>, just the hairline outline.
        /// </summary>
        public static Sprite Slot(bool ring)
        {
            if (ring ? slotRing != null : slotFill != null) return ring ? slotRing : slotFill;
            const int size = 32, cut = 8;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color32[] px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance inside the shape (in pixels) from its outline, with the two cut corners.
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float d = Mathf.Min(Mathf.Min(fx, size - fx), Mathf.Min(fy, size - fy));
                    float topLeft = (fx + (size - fy) - cut) * 0.7071f;   // cut across the top-left corner
                    float bottomRight = ((size - fx) + fy - cut) * 0.7071f;
                    d = Mathf.Min(d, Mathf.Min(topLeft, bottomRight));
                    float inside = Mathf.Clamp01(d);
                    float a = ring ? inside * (1f - Mathf.Clamp01(d - 1.5f)) : inside;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(cut + 2, cut + 2, cut + 2, cut + 2));
            if (ring) slotRing = s; else slotFill = s;
            return s;
        }
    }
}
