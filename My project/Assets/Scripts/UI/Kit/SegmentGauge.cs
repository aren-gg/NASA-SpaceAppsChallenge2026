using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>
    /// The NASA-punk gauge used across the UI: a row of slanted segments. Lit segments take the
    /// Graphic colour, empty ones sit dark. With <see cref="Hazard"/> on (a vital running out),
    /// the empty segments carry pulsing red hazard stripes so the warning is not colour alone.
    /// Flat polygons only.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SegmentGauge : MaskableGraphic
    {
        [SerializeField] private int segments;            // 0 picks a count from the width
        [SerializeField] private float fraction = 1f;
        [SerializeField] private bool hazard;
        /// <summary>Horizontal lean of each segment, as a fraction of its height.</summary>
        public float Slant = 0.45f;
        public float Gap = 2f;
        public Color Empty = new Color(0.10f, 0.11f, 0.13f, 0.78f);
        private float pulse = -1f;

        public int Segments { get => segments; set { if (segments != value) { segments = value; SetVerticesDirty(); } } }

        public float Fraction
        {
            get => fraction;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Lit(v) != Lit(fraction)) SetVerticesDirty();
                fraction = v;
            }
        }

        public bool Hazard { get => hazard; set { if (hazard != value) { hazard = value; SetVerticesDirty(); } } }

        private int Count(Rect rect)
        {
            if (segments > 0) return segments;
            return Mathf.Clamp(Mathf.RoundToInt(rect.width / 13f), 6, 40);
        }

        private int Lit(float value)
        {
            int n = Count(GetPixelAdjustedRect());
            return value <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(value * n - 0.001f), 1, n);
        }

        private void Update()
        {
            if (!hazard) return;
            float p = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
            if (Mathf.Abs(p - pulse) > 0.04f) { pulse = p; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 1f || r.height <= 0.5f) return;
            int n = Count(r);
            float lean = r.height * Slant;
            float width = (r.width - lean - Gap * (n - 1)) / n;
            if (width <= 0.5f) { width = (r.width - lean) / n; }
            int lit = Lit(fraction);
            Color stripe = UiTheme.Danger;
            stripe.a = pulse < 0f ? 0.9f : pulse;
            for (int k = 0; k < n; k++)
            {
                float x = r.xMin + k * (width + Gap);
                bool on = k < lit;
                Leaning(mesh, x, width, r.yMin, r.yMax, lean, on ? color : Empty);
                if (!on && hazard) Leaning(mesh, x + width * 0.3f, width * 0.4f, r.yMin, r.yMax, lean, stripe);
            }
        }

        private static void Leaning(VertexHelper mesh, float x, float w, float y0, float y1, float lean, Color tint)
        {
            int first = mesh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = tint;
            v.position = new Vector2(x, y0); mesh.AddVert(v);
            v.position = new Vector2(x + w, y0); mesh.AddVert(v);
            v.position = new Vector2(x + w + lean, y1); mesh.AddVert(v);
            v.position = new Vector2(x + lean, y1); mesh.AddVert(v);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }

        /// <summary>Adds a gauge filling <paramref name="parent"/> between the given anchors.</summary>
        public static SegmentGauge Create(Transform parent, float x0, float y0, float x1, float y1, Color color, int segments = 0)
        {
            RectTransform rect = new GameObject("Gauge", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            SegmentGauge gauge = rect.gameObject.AddComponent<SegmentGauge>();
            gauge.color = color;
            gauge.segments = segments;
            gauge.raycastTarget = false;
            return gauge;
        }
    }
}
