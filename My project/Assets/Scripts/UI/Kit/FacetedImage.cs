using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>
    /// The shared panel shape (see <see cref="UiTheme"/>): a flat dark plate with both top corners
    /// cut, like the door portals, and a gunmetal hairline edge. Flat polygons only.
    /// </summary>
    public sealed class FacetedImage : Image
    {
        /// <summary>Use the colour's alpha as given (HUD plates share one opacity whatever their size).</summary>
        public bool KeepAlpha;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float small = Mathf.Min(rect.width, rect.height);
            float cut = Mathf.Min(UiTheme.Cut, small / 4f);
            bool large = rect.width >= 140f && rect.height >= 70f;
            bool screen = rect.width > 1100f && rect.height > 600f;

            Color fill = color;
            if (large && !KeepAlpha) fill.a *= screen ? UiTheme.ScreenAlpha : UiTheme.PanelAlpha;
            Vector2[] outer = Shape(rect, cut);
            if (small < 14f) { Fan(mesh, outer, fill); return; }

            Vector2[] inner = Shape(Inset(rect, 1f), Mathf.Max(0f, cut - 0.4f));
            Fan(mesh, inner, fill);
            Strip(mesh, outer, inner, UiTheme.WithAlpha(UiTheme.Edge, fill.a < 0.5f ? 0.6f : 1f));
        }

        private static Rect Inset(Rect r, float by)
        {
            return new Rect(r.x + by, r.y + by, Mathf.Max(0f, r.width - by * 2f), Mathf.Max(0f, r.height - by * 2f));
        }

        /// <summary>The outline: six corners, with both top corners cut like a door portal.</summary>
        private static Vector2[] Shape(Rect r, float cut)
        {
            return new[]
            {
                new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax - cut),
                new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin + cut, r.yMax),
                new Vector2(r.xMin, r.yMax - cut),
            };
        }

        private static void Fan(VertexHelper mesh, Vector2[] poly, Color tint)
        {
            int first = mesh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = tint;
            Vector2 centre = Vector2.zero;
            foreach (Vector2 p in poly) centre += p;
            v.position = centre / poly.Length;
            mesh.AddVert(v);
            for (int i = 0; i < poly.Length; i++)
            {
                v.position = poly[i];
                mesh.AddVert(v);
            }
            for (int i = 0; i < poly.Length; i++) mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % poly.Length);
        }

        private static void Strip(VertexHelper mesh, Vector2[] outer, Vector2[] inner, Color tint)
        {
            for (int i = 0; i < outer.Length; i++)
            {
                int j = (i + 1) % outer.Length;
                Quad(mesh, outer[i], outer[j], inner[j], inner[i], tint);
            }
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int first = mesh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = tint;
            v.position = a; mesh.AddVert(v);
            v.position = b; mesh.AddVert(v);
            v.position = c; mesh.AddVert(v);
            v.position = d; mesh.AddVert(v);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
