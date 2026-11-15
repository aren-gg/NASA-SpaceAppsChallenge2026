using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Base
{
    /// <summary>A rectangle on the floor plan: x and z ranges in outpost-local metres.</summary>
    public struct FloorRect
    {
        public float x0, z0, x1, z1;

        public FloorRect(float x0, float z0, float x1, float z1)
        {
            this.x0 = x0; this.z0 = z0; this.x1 = x1; this.z1 = z1;
        }

        public float CentreX { get { return (x0 + x1) * 0.5f; } }
        public float CentreZ { get { return (z0 + z1) * 0.5f; } }
        public float Width { get { return x1 - x0; } }
        public float Depth { get { return z1 - z0; } }

        public FloorRect Grow(float by) { return new FloorRect(x0 - by, z0 - by, x1 + by, z1 + by); }
        public bool Contains(float x, float z) { return x >= x0 && x <= x1 && z >= z0 && z <= z1; }
    }

    /// <summary>
    /// Collects flat-shaded low-poly solids (boxes, tapered boxes, prisms) into one mesh
    /// with a submesh per material, in the style of the base and tool kits: hard
    /// normals, no textures.
    /// </summary>
    public sealed class OutpostGeometry
    {
        // Palette order matches MarsPalette and the kits' mars_palette.mtl.
        public const int Terracotta = 0, DustyRose = 1, Olive = 2, Orange = 3, Purple = 4, PalePink = 5,
            Mauve = 6, ClaySand = 7, Glass = 8, GlowWarm = 9, GlowOrange = 10,
            // Hull colours for the habitat shells and the rover: white panels, dark red accents, charcoal trim.
            White = 11, DarkRed = 12, Charcoal = 13, SolarBlue = 14, MaterialCount = 15;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int>[] triangles = new List<int>[MaterialCount];

        public OutpostGeometry()
        {
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();
        }

        public bool IsEmpty { get { return vertices.Count == 0; } }

        private bool framed;
        private Vector3 frameOrigin;
        private Quaternion frameRotation = Quaternion.identity;

        /// <summary>
        /// Until <see cref="ClearFrame"/>, everything added is turned about the vertical axis
        /// and moved: local +Z points along the heading, and the local origin lands on
        /// <paramref name="origin"/>. Used for the facets of round rooms.
        /// </summary>
        public void SetFrame(Vector3 origin, float yawDegrees)
        {
            framed = true;
            frameOrigin = origin;
            frameRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        public void ClearFrame()
        {
            framed = false;
        }

        private Vector3 Place(Vector3 p)
        {
            return framed ? frameOrigin + frameRotation * p : p;
        }

        /// <summary>
        /// Skin stretched over a series of cross-sections (each with the same number of points,
        /// going the same way round), closed at both ends unless told otherwise. Used for
        /// rounded hulls, vaults and tubes.
        /// </summary>
        public void Loft(int material, IList<Vector3[]> rings, bool capStart = true, bool capEnd = true)
        {
            for (int i = 0; i + 1 < rings.Count; i++)
            {
                Vector3[] a = rings[i], b = rings[i + 1];
                Vector3 inside = (Centroid(a) + Centroid(b)) * 0.5f;
                for (int k = 0; k < a.Length; k++)
                {
                    int n = (k + 1) % a.Length;
                    Quad(material, a[k], a[n], b[n], b[k], inside);
                }
            }
            if (capStart) Cap(material, rings[0], Centroid(rings[1]));
            if (capEnd) Cap(material, rings[rings.Count - 1], Centroid(rings[rings.Count - 2]));
        }

        private void Cap(int material, Vector3[] ring, Vector3 inside)
        {
            Vector3 centre = Centroid(ring);
            for (int k = 0; k < ring.Length; k++)
            {
                Triangle(material, centre, ring[k], ring[(k + 1) % ring.Length], inside);
            }
        }

        private static Vector3 Centroid(Vector3[] ring)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < ring.Length; i++) sum += ring[i];
            return sum / ring.Length;
        }

        public void Box(int material, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            if (x1 - x0 < 1e-4f || y1 - y0 < 1e-4f || z1 - z0 < 1e-4f) return;
            Hexa(material,
                new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1),
                new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1));
        }

        public void Box(int material, FloorRect r, float y0, float y1)
        {
            Box(material, r.x0, y0, r.z0, r.x1, y1, r.z1);
        }

        /// <summary>A box whose bottom and top rectangles differ: roofs, berms, tapered parts.</summary>
        public void Frustum(int material, float y0, FloorRect bottom, float y1, FloorRect top)
        {
            Hexa(material,
                new Vector3(bottom.x0, y0, bottom.z0), new Vector3(bottom.x1, y0, bottom.z0),
                new Vector3(bottom.x1, y0, bottom.z1), new Vector3(bottom.x0, y0, bottom.z1),
                new Vector3(top.x0, y1, top.z0), new Vector3(top.x1, y1, top.z0),
                new Vector3(top.x1, y1, top.z1), new Vector3(top.x0, y1, top.z1));
        }

        /// <summary>Six-sided solid from a bottom ring (b0-b3) and the matching top ring (t0-t3).</summary>
        public void Hexa(int material, Vector3 b0, Vector3 b1, Vector3 b2, Vector3 b3,
            Vector3 t0, Vector3 t1, Vector3 t2, Vector3 t3)
        {
            Vector3 inside = (b0 + b1 + b2 + b3 + t0 + t1 + t2 + t3) * 0.125f;
            Quad(material, b0, b1, t1, t0, inside);
            Quad(material, b1, b2, t2, t1, inside);
            Quad(material, b2, b3, t3, t2, inside);
            Quad(material, b3, b0, t0, t3, inside);
            Quad(material, t0, t1, t2, t3, inside);
            Quad(material, b0, b1, b2, b3, inside);
        }

        /// <summary>Upright prism (8 sides by default) for tanks, posts and stools; topRadius tapers it.</summary>
        public void Prism(int material, float cx, float cz, float radius, float y0, float y1, int sides = 8,
            float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            Vector3 inside = new Vector3(cx, (y0 + y1) * 0.5f, cz);
            Vector3 bottomCentre = new Vector3(cx, y0, cz), topCentre = new Vector3(cx, y1, cz);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i + 0.5f) / sides * Mathf.PI * 2f, a1 = (i + 1.5f) / sides * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 p0 = bottomCentre + d0 * radius, p1 = bottomCentre + d1 * radius;
                Vector3 q0 = topCentre + d0 * topRadius, q1 = topCentre + d1 * topRadius;
                Quad(material, p0, p1, q1, q0, inside);
                Triangle(material, topCentre, q0, q1, inside);
                Triangle(material, bottomCentre, p0, p1, inside);
            }
        }

        public void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            a = Place(a); b = Place(b); c = Place(c); d = Place(d); inside = Place(inside);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) normal = Vector3.Cross(c - a, d - a);
            normal.Normalize();
            bool flip = Vector3.Dot(normal, (a + b + c + d) * 0.25f - inside) < 0f;
            if (flip) normal = -normal;
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            List<int> list = triangles[material];
            if (flip)
            {
                list.Add(start); list.Add(start + 2); list.Add(start + 1);
                list.Add(start); list.Add(start + 3); list.Add(start + 2);
            }
            else
            {
                list.Add(start); list.Add(start + 1); list.Add(start + 2);
                list.Add(start); list.Add(start + 2); list.Add(start + 3);
            }
        }

        public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            a = Place(a); b = Place(b); c = Place(c); inside = Place(inside);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 1e-12f) return;
            bool flip = Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0f;
            if (flip) normal = -normal;
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int i = 0; i < 3; i++) normals.Add(normal);
            List<int> list = triangles[material];
            list.Add(start);
            list.Add(flip ? start + 2 : start + 1);
            list.Add(flip ? start + 1 : start + 2);
        }

        /// <summary>Builds the mesh and returns the materials for the submeshes that have geometry.</summary>
        public Mesh ToMesh(string meshName, Material[] allMaterials, out Material[] usedMaterials)
        {
            Mesh mesh = new Mesh();
            mesh.name = meshName;
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            List<Material> used = new List<Material>();
            int count = 0;
            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i].Count > 0) count++;
            }
            mesh.subMeshCount = count;
            int subMesh = 0;
            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i].Count == 0) continue;
                mesh.SetTriangles(triangles[i], subMesh++);
                used.Add(allMaterials[i]);
            }
            mesh.RecalculateBounds();
            usedMaterials = used.ToArray();
            return mesh;
        }
    }

    /// <summary>The materials that go with <see cref="OutpostGeometry"/>, indexed by its constants.</summary>
    public static class OutpostMaterials
    {
        /// <summary>Creates one set. The caller owns the materials and destroys them.</summary>
        public static Material[] Create()
        {
            Color[] palette =
            {
                MarsPalette.Terracotta, MarsPalette.DustyRose, MarsPalette.MutedOlive, MarsPalette.WarmOrange,
                MarsPalette.DeepPurple, MarsPalette.PalePink, MarsPalette.Mauve, MarsPalette.ClaySand,
            };
            Material[] all = new Material[OutpostGeometry.MaterialCount];
            for (int i = 0; i < palette.Length; i++)
            {
                all[i] = MarsPalette.CreateMatteMaterial("Outpost " + i, palette[i]);
            }
            all[OutpostGeometry.Glass] = Unlit("Outpost Glass", new Color(0.62f, 0.74f, 0.88f, 0.28f), true);
            all[OutpostGeometry.GlowWarm] = Unlit("Outpost Light", new Color(1f, 0.93f, 0.86f, 1f), false);
            all[OutpostGeometry.GlowOrange] = Unlit("Outpost Indicator", new Color(1f, 0.66f, 0.38f, 1f), false);
            all[OutpostGeometry.White] = MarsPalette.CreateMatteMaterial("Hull White", new Color(0.93f, 0.92f, 0.90f));
            all[OutpostGeometry.DarkRed] = MarsPalette.CreateMatteMaterial("Hull Dark Red", new Color(0.42f, 0.12f, 0.13f));
            all[OutpostGeometry.Charcoal] = MarsPalette.CreateMatteMaterial("Hull Charcoal", new Color(0.13f, 0.13f, 0.15f));
            all[OutpostGeometry.SolarBlue] = MarsPalette.CreateMatteMaterial("Solar Cell Blue", new Color(0.20f, 0.30f, 0.62f));
            return all;
        }

        /// <summary>Unlit, so light panels and screens read as lit at night. Glass is see-through.</summary>
        private static Material Unlit(string materialName, Color color, bool transparent)
        {
            Shader shader = transparent ? null : Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Material material = new Material(shader);
            material.name = materialName;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }
    }
}
