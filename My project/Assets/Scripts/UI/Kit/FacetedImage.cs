using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>
    /// The shared panel shape (see <see cref="UiTheme"/>): a retro instrument plate. A
    /// semi-transparent smoky fill set in a gunmetal steel frame whose bevel is lit at the
    /// top-left and shaded at the bottom-right, with small corner cuts and, on large panels,
    /// four rivets. Flat polygons only, no textures or blur.
    /// </summary>
    public sealed class FacetedImage : Image
    {
        private static readonly Vector2 LightFrom = new Vector2(-0.6f, 0.8f); // light comes from the top-left

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float small = Mathf.Min(rect.width, rect.height);
            float cut = Mathf.Min(UiTheme.Cut, small / 4f);
            bool large = rect.width >= 140f && rect.height >= 70f;
            bool screen = rect.width > 1100f && rect.height > 600f;

            Color fill = color;
            if (large) fill.a *= screen ? UiTheme.ScreenAlpha : UiTheme.PanelAlpha;
            Vector2[] outer = Shape(rect, cut);
            if (small < 14f) { Fan(mesh, outer, fill); return; }

            float frame = large ? UiTheme.Frame : Mathf.Max(2f, UiTheme.Frame * 0.66f);
            Vector2[] middle = Shape(Inset(rect, frame - 1f), Mathf.Max(0f, cut - frame * 0.4f));
            Vector2[] inner = Shape(Inset(rect, frame), Mathf.Max(0f, cut - frame * 0.5f));
            Fan(mesh, inner, fill);
            Bevel(mesh, outer, middle);
            Strip(mesh, middle, inner, UiTheme.SteelDark);   // the dark seam between frame and plate

            if (large && !screen)
            {
                // Rivets just inside each corner of the frame.
                float r = 1.6f, inset = frame + 6f;
                Rivet(mesh, new Vector2(rect.xMin + inset + cut * 0.5f, rect.yMax - inset), r);
                Rivet(mesh, new Vector2(rect.xMax - inset, rect.yMax - inset), r);
                Rivet(mesh, new Vector2(rect.xMin + inset, rect.yMin + inset), r);
                Rivet(mesh, new Vector2(rect.xMax - inset - cut * 0.5f, rect.yMin + inset), r);
            }
        }

        private static Rect Inset(Rect r, float by)
        {
            return new Rect(r.x + by, r.y + by, Mathf.Max(0f, r.width - by * 2f), Mathf.Max(0f, r.height - by * 2f));
        }

        /// <summary>The outline: six corners, with the top-left and bottom-right cut.</summary>
        private static Vector2[] Shape(Rect r, float cut)
        {
            return new[]
            {
                new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMax - cut, r.yMin),
                new Vector2(r.xMax, r.yMin + cut),
                new Vector2(r.xMax, r.yMax),
                new Vector2(r.xMin + cut, r.yMax),
                new Vector2(r.xMin, r.yMax - cut),
            };
        }

        /// <summary>The steel frame: each side shaded by how much it faces the light.</summary>
        private static void Bevel(VertexHelper mesh, Vector2[] outer, Vector2[] inner)
        {
            for (int i = 0; i < outer.Length; i++)
            {
                int j = (i + 1) % outer.Length;
                Vector2 edge = outer[j] - outer[i];
                Vector2 normal = new Vector2(edge.y, -edge.x).normalized;   // outward for this winding
                float lit = Mathf.Clamp01(Vector2.Dot(normal, LightFrom.normalized) * 0.5f + 0.5f);
                Color steel = Color.Lerp(UiTheme.SteelDark, UiTheme.SteelLight, lit);
                Quad(mesh, outer[i], outer[j], inner[j], inner[i], steel);
            }
        }

        private static void Rivet(VertexHelper mesh, Vector2 at, float r)
        {
            Quad(mesh, at + new Vector2(-r, -r), at + new Vector2(r, -r), at + new Vector2(r, r), at + new Vector2(-r, r), UiTheme.SteelMid);
            Quad(mesh, at + new Vector2(-r, 0f), at + new Vector2(0f, 0f), at + new Vector2(0f, r), at + new Vector2(-r, r), UiTheme.SteelLight);
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
