using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.Tools
{
    /// <summary>The eight art-guide colours, used to paint generated meshes and imported models.</summary>
    public enum Paint { Terracotta, DustyRose, MutedOlive, WarmOrange, DeepPurple, PalePink, Mauve, ClaySand }

    /// <summary>
    /// Palette materials (one shared matte material per colour) and the tool models,
    /// read from OBJ text in Resources and painted with those materials.
    /// </summary>
    public static class MarsLowPoly
    {
        private static readonly string[] Names =
            { "terracotta", "dusty_rose", "muted_olive", "warm_orange", "deep_purple", "pale_pink", "mauve", "clay_sand" };
        private static Material[] materials;

        public static Color ColorOf(Paint paint)
        {
            switch (paint)
            {
                case Paint.Terracotta: return MarsPalette.Terracotta;
                case Paint.DustyRose: return MarsPalette.DustyRose;
                case Paint.MutedOlive: return MarsPalette.MutedOlive;
                case Paint.WarmOrange: return MarsPalette.WarmOrange;
                case Paint.DeepPurple: return MarsPalette.DeepPurple;
                case Paint.PalePink: return MarsPalette.PalePink;
                case Paint.Mauve: return MarsPalette.Mauve;
                default: return MarsPalette.ClaySand;
            }
        }

        public static Material Material(Paint paint)
        {
            if (materials == null)
            {
                materials = new Material[Names.Length];
                for (int i = 0; i < Names.Length; i++)
                {
                    materials[i] = MarsPalette.CreateMatteMaterial("Palette " + Names[i], ColorOf((Paint)i));
                    materials[i].hideFlags = HideFlags.DontSave;
                }
            }
            return materials[(int)paint];
        }

        /// <summary>Palette colour whose .mtl name appears in an imported material or object name.</summary>
        public static Paint PaintForName(string name, Paint fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            string key = name.ToLowerInvariant();
            for (int i = 0; i < Names.Length; i++)
            {
                if (key.Contains(Names[i])) return (Paint)i;
            }
            return fallback;
        }

        private static readonly Dictionary<string, Mesh> toolMeshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material[]> toolMaterials = new Dictionary<string, Material[]>();

        /// <summary>
        /// Creates a tool model from Assets/MarsTools/Resources/MarsTools/Models/&lt;id&gt;.obj.txt.
        /// The OBJ text is read directly (not through Unity's model importer) so the
        /// orientation is exactly as authored in Task_assets: metres, Y up, grip at the
        /// origin. OBJ is right-handed, so X is mirrored as Unity's importer would do.
        /// Returns null if the file is missing.
        /// </summary>
        public static GameObject LoadTool(string id, Transform parent)
        {
            Mesh mesh;
            Material[] used;
            if (!toolMeshes.TryGetValue(id, out mesh))
            {
                TextAsset text = Resources.Load<TextAsset>("MarsTools/Models/" + id + ".obj");
                if (text == null) return null;
                mesh = ParseObj(id, text.text, out used);
                toolMeshes[id] = mesh;
                toolMaterials[id] = used;
            }
            used = toolMaterials[id];
            GameObject go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = used;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>Flat-shaded mesh from OBJ text (v, vn, usemtl, f), one submesh per palette colour.</summary>
        public static Mesh ParseObj(string name, string text, out Material[] used)
        {
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            List<Vector3> v = new List<Vector3>();
            List<Vector3> vn = new List<Vector3>();
            List<Vector3> positions = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            Dictionary<Paint, List<int>> indices = new Dictionary<Paint, List<int>>();
            List<int> current = null;
            Paint paint = Paint.DustyRose;
            int[] corner = new int[16];
            int[] cornerNormal = new int[16];

            foreach (string raw in text.Split(new[] { '\n' }))
            {
                string line = raw.Trim();
                if (line.Length < 2 || line[0] == '#') continue;
                string[] parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v":
                        v.Add(new Vector3(-float.Parse(parts[1], invariant), float.Parse(parts[2], invariant), float.Parse(parts[3], invariant)));
                        break;
                    case "vn":
                        vn.Add(new Vector3(-float.Parse(parts[1], invariant), float.Parse(parts[2], invariant), float.Parse(parts[3], invariant)));
                        break;
                    case "usemtl":
                        paint = PaintForName(parts.Length > 1 ? parts[1] : null, Paint.DustyRose);
                        current = null;
                        break;
                    case "f":
                        if (current == null && !indices.TryGetValue(paint, out current))
                        {
                            current = new List<int>();
                            indices.Add(paint, current);
                        }
                        int count = Mathf.Min(parts.Length - 1, corner.Length);
                        for (int i = 0; i < count; i++)
                        {
                            string[] refs = parts[i + 1].Split('/');
                            int vi = int.Parse(refs[0], invariant);
                            corner[i] = vi < 0 ? v.Count + vi : vi - 1;
                            int ni = refs.Length > 2 && refs[2].Length > 0 ? int.Parse(refs[2], invariant) : 0;
                            cornerNormal[i] = ni == 0 ? -1 : (ni < 0 ? vn.Count + ni : ni - 1);
                        }
                        for (int i = 1; i + 1 < count; i++)
                        {
                            // Mirroring X flips the winding, so emit the triangle reversed.
                            int a = corner[0], b = corner[i + 1], c = corner[i];
                            Vector3 faceNormal = Vector3.Cross(v[b] - v[a], v[c] - v[a]).normalized;
                            int first = positions.Count;
                            positions.Add(v[a]);
                            positions.Add(v[b]);
                            positions.Add(v[c]);
                            normals.Add(cornerNormal[0] >= 0 ? vn[cornerNormal[0]] : faceNormal);
                            normals.Add(cornerNormal[i + 1] >= 0 ? vn[cornerNormal[i + 1]] : faceNormal);
                            normals.Add(cornerNormal[i] >= 0 ? vn[cornerNormal[i]] : faceNormal);
                            current.Add(first);
                            current.Add(first + 1);
                            current.Add(first + 2);
                        }
                        break;
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.DontSave;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.subMeshCount = indices.Count;
            used = new Material[indices.Count];
            int sub = 0;
            foreach (KeyValuePair<Paint, List<int>> pair in indices)
            {
                mesh.SetTriangles(pair.Value, sub, false);
                used[sub] = Material(pair.Key);
                sub++;
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Builds flat-shaded meshes from boxes, prisms and spikes, one submesh per palette
    /// colour. Every face gets its own vertices so the shading stays faceted.
    /// </summary>
    public sealed class LowPolyMesh
    {
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly Dictionary<Paint, List<int>> indices = new Dictionary<Paint, List<int>>();

        public bool IsEmpty { get { return positions.Count == 0; } }

        /// <summary>A triangle wound to face away from <paramref name="inside"/>.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Paint paint)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();
            if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
                normal = -normal;
            }
            List<int> list;
            if (!indices.TryGetValue(paint, out list))
            {
                list = new List<int>();
                indices.Add(paint, list);
            }
            int first = positions.Count;
            positions.Add(a);
            positions.Add(b);
            positions.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            list.Add(first);
            list.Add(first + 1);
            list.Add(first + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Paint paint)
        {
            Triangle(a, b, c, inside, paint);
            Triangle(a, c, d, inside, paint);
        }

        public void Box(Vector3 centre, Vector3 size, Quaternion rotation, Paint paint)
        {
            Vector3 x = rotation * new Vector3(size.x * 0.5f, 0f, 0f);
            Vector3 y = rotation * new Vector3(0f, size.y * 0.5f, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, size.z * 0.5f);
            Vector3[] p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                p[i] = centre + ((i & 1) == 0 ? -x : x) + ((i & 2) == 0 ? -y : y) + ((i & 4) == 0 ? -z : z);
            }
            Quad(p[0], p[1], p[3], p[2], centre, paint); // -Z
            Quad(p[4], p[5], p[7], p[6], centre, paint); // +Z
            Quad(p[0], p[2], p[6], p[4], centre, paint); // -X
            Quad(p[1], p[3], p[7], p[5], centre, paint); // +X
            Quad(p[0], p[1], p[5], p[4], centre, paint); // -Y
            Quad(p[2], p[3], p[7], p[6], centre, paint); // +Y
        }

        public void Box(Vector3 centre, Vector3 size, Paint paint)
        {
            Box(centre, size, Quaternion.identity, paint);
        }

        /// <summary>An upright n-sided prism (post, tank, stem) standing on <paramref name="baseCentre"/>.</summary>
        public void Prism(Vector3 baseCentre, float radius, float height, int sides, Quaternion rotation, Paint paint,
                          float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            Vector3 up = rotation * Vector3.up;
            Vector3 centre = baseCentre + up * (height * 0.5f);
            Vector3 top = baseCentre + up * height;
            Vector3[] low = new Vector3[sides];
            Vector3[] high = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f;
                Vector3 dir = rotation * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                low[i] = baseCentre + dir * radius;
                high[i] = top + dir * topRadius;
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Quad(low[i], low[j], high[j], high[i], centre, paint);
                Triangle(baseCentre, low[i], low[j], centre, paint);
                if (topRadius > 0f) Triangle(top, high[i], high[j], centre, paint);
            }
        }

        /// <summary>A pointed spike (leaf, flag tip) from a small triangular base to a tip.</summary>
        public void Spike(Vector3 baseCentre, Vector3 tip, float width, Paint paint)
        {
            Vector3 axis = (tip - baseCentre).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized * width;
            Vector3 other = Vector3.Cross(axis, side).normalized * width * 0.45f;
            Vector3 a = baseCentre + side;
            Vector3 b = baseCentre - side;
            Vector3 c = baseCentre + other;
            Vector3 inside = (a + b + c + tip) * 0.25f;
            Triangle(a, b, tip, inside, paint);
            Triangle(b, c, tip, inside, paint);
            Triangle(c, a, tip, inside, paint);
            Triangle(a, b, c, inside, paint);
        }

        /// <summary>
        /// A chunky angular boulder: an icosahedron with jittered vertices, squashed and
        /// rotated, like the terrain's procedural rocks.
        /// </summary>
        public void Boulder(Vector3 centre, float radius, float squash, float yaw, System.Random random, Paint paint)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] v =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i].normalized * (0.8f + 0.35f * (float)random.NextDouble());
                p.y *= squash;
                v[i] = centre + rotation * (p * radius);
            }
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int i = 0; i < f.Length; i += 3) Triangle(v[f[i]], v[f[i + 1]], v[f[i + 2]], centre, paint);
        }

        public Mesh ToMesh(string name, out Material[] usedMaterials)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.DontSave;
            if (positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.subMeshCount = indices.Count;
            usedMaterials = new Material[indices.Count];
            int sub = 0;
            foreach (KeyValuePair<Paint, List<int>> pair in indices)
            {
                mesh.SetTriangles(pair.Value, sub, false);
                usedMaterials[sub] = MarsLowPoly.Material(pair.Key);
                sub++;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Creates a child object showing this mesh. Adds a MeshCollider if asked.</summary>
        public GameObject Build(string name, Transform parent, bool collider = false, bool shadows = true)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Material[] used;
            Mesh mesh = ToMesh(name, out used);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = used;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
