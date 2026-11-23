using System.Collections.Generic;
using UnityEngine;

namespace MarsSurvival.World
{
    /// <summary>
    /// Scatters flat-shaded rocks over one rock tile: the five Blender rock shapes
    /// (Resources/Garden/MarsRocks, tools/blender/garden_assets.py mars_rocks) when present, else
    /// chunky icospheres.
    ///
    /// This is procedural decoration, not NASA data: the 200 m elevation grid
    /// cannot resolve individual rocks. Placement is deterministic for a given
    /// seed and chunk, so the same rocks reappear when a chunk comes back into range.
    /// </summary>
    public static class MarsRockBuilder
    {
        private const int FacesPerRock = 20;
        private const int VerticesPerRock = FacesPerRock * 3;
        /// <summary>Position + normal (24 B) and a 16-bit index per vertex.</summary>
        public const int BytesPerRock = VerticesPerRock * (24 + 2);

        private static readonly Vector3[] IcoVertices = BuildIcoVertices();
        private static readonly int[] IcoFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        private static readonly List<Vector3> Positions = new List<Vector3>(640);
        private static readonly List<Vector3> Normals = new List<Vector3>(640);
        private static readonly List<int> Indices = new List<int>(640);
        private static readonly List<int> SolidIndices = new List<int>(640);
        /// <summary>Rocks smaller than this (radius, metres) can be stepped over, so they get no collider.</summary>
        private const float SolidRadiusMeters = 0.3f;
        private static readonly Vector3[] Shaped = new Vector3[12];

        /// <summary>The Blender rock shapes as triangle soups (about 1 m across, origin at the base), or null.</summary>
        private static Vector3[][] shapes;
        private static bool shapesLoaded;

        private static Vector3[][] Shapes()
        {
            if (shapesLoaded) return shapes;
            shapesLoaded = true;
            GameObject prefab = Resources.Load<GameObject>("Garden/MarsRocks");
            if (prefab == null) return null;
            var list = new List<Vector3[]>();
            foreach (MeshFilter f in prefab.GetComponentsInChildren<MeshFilter>())
            {
                Mesh m = f.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Vector3[] v = m.vertices; int[] t = m.triangles;
                var soup = new Vector3[t.Length];
                for (int i = 0; i < t.Length; i++) soup[i] = Quaternion.Euler(0f, 180f, 0f) * v[t[i]];   // same turn as MarsWorldTasks.Model
                list.Add(soup);
            }
            shapes = list.Count > 0 ? list.ToArray() : null;
            return shapes;
        }

        private static Vector3[] BuildIcoVertices()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] v =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized;
            return v;
        }

        /// <summary>Small deterministic generator (xorshift32); no allocations.</summary>
        private struct Rng
        {
            private uint state;

            public Rng(uint seed)
            {
                state = seed != 0 ? seed : 0x9E3779B9u;
            }

            public float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xFFFFFF) / 16777216f;
            }

            public float Range(float min, float max)
            {
                return min + (max - min) * Next();
            }
        }

        private static bool InClearing(List<Vector3> clearings, float x, float z, float margin)
        {
            if (clearings == null) return false;
            for (int i = 0; i < clearings.Count; i++)
            {
                float dx = x - clearings[i].x;
                float dz = z - clearings[i].y;
                float r = clearings[i].z + margin;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        /// <param name="chunkCorner">Terrain-local X/Z of the tile's south-west corner (Unity units).</param>
        /// <param name="chunkIndex">Seed for this tile; the same value gives the same rocks.</param>
        /// <param name="clearings">Optional circles kept free of rocks: terrain-local X, Z and radius (Unity units).</param>
        /// <returns>A mesh in tile-local space (origin at the tile corner, Y = terrain-local Y).</returns>
        private static Vector3 Turn(Vector3 p, float cos, float sin, float scale, float squash)
        {
            return new Vector3((p.x * cos - p.z * sin) * scale, p.y * scale * (0.6f + squash * 0.5f), (p.x * sin + p.z * cos) * scale);
        }

        /// <param name="solid">Collision mesh of the rocks too big to step over (same vertices), or null when there are none.</param>
        public static Mesh Build(MarsHeightmap map, MarsTerrainConfig config, Vector2 chunkCorner,
            float chunkSizeX, float chunkSizeZ, int chunkIndex, out int rockCount, out Mesh solid, List<Vector3> clearings = null,
            MarsTerrainInset inset = null)
        {
            Positions.Clear();
            Normals.Clear();
            Indices.Clear();
            SolidIndices.Clear();
            float solidRadius = config.ToUnits(SolidRadiusMeters);
            rockCount = 0;

            Rng rng = new Rng(unchecked((uint)(config.rockSeed * 73856093) ^ (uint)((chunkIndex + 1) * 19349663)));
            for (int i = 0; i < 8; i++) rng.Next();
            int stride = config.StrideForLod(0);
            int attempts = config.rocksPerTile;
            float minRadius = config.ToUnits(config.rockRadiusRangeMeters.x);
            float maxRadius = config.ToUnits(config.rockRadiusRangeMeters.y);

            for (int r = 0; r < attempts; r++)
            {
                // Draw every random value up front so one rejected rock does not shift the rest.
                float px = rng.Next() * chunkSizeX;
                float pz = rng.Next() * chunkSizeZ;
                float sizeT = rng.Next();
                float squash = rng.Range(0.55f, 0.85f);
                float yaw = rng.Range(0f, Mathf.PI * 2f);
                for (int v = 0; v < 12; v++)
                {
                    Shaped[v] = IcoVertices[v] * rng.Range(0.8f, 1.15f);
                }

                float localX = chunkCorner.x + px;
                float localZ = chunkCorner.y + pz;
                if (!map.Contains(localX, localZ)) continue;
                if (InClearing(clearings, localX, localZ, maxRadius)) continue;
                bool onPatch = inset != null && inset.Contains(localX, localZ);
                float slope = onPatch ? inset.SampleSlopeDegrees(localX, localZ) : map.SampleSlopeDegrees(localX, localZ, stride);
                if (slope > config.rockMaxSlopeDegrees) continue;

                // Mostly small rocks with an occasional boulder.
                float radius = Mathf.Lerp(minRadius, maxRadius, sizeT * sizeT * sizeT);
                float groundY = onPatch ? inset.SampleY(localX, localZ) : map.SampleFacetedY(localX, localZ, stride);
                Vector3 centre = new Vector3(px, groundY - radius * squash * 0.3f, pz);
                float cos = Mathf.Cos(yaw);
                float sin = Mathf.Sin(yaw);

                for (int v = 0; v < 12; v++)
                {
                    Vector3 p = Shaped[v];
                    Shaped[v] = centre + new Vector3(
                        (p.x * cos - p.z * sin) * radius,
                        p.y * radius * squash,
                        (p.x * sin + p.z * cos) * radius);
                }

                int firstIndex = Indices.Count;
                Vector3[][] models = Shapes();
                if (models != null)
                {
                    // A Blender shape, about 1 m across: scaled to this rock's size, turned, and sunk a little.
                    Vector3[] soup = models[(int)(sizeT * 7919f) % models.Length];
                    float scale = radius * 2f;
                    Vector3 basePos = new Vector3(px, groundY - radius * 0.15f, pz);
                    for (int i = 0; i < soup.Length; i += 3)
                    {
                        Vector3 a = basePos + Turn(soup[i], cos, sin, scale, squash);
                        Vector3 b = basePos + Turn(soup[i + 1], cos, sin, scale, squash);
                        Vector3 c = basePos + Turn(soup[i + 2], cos, sin, scale, squash);
                        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                        int first = Positions.Count;
                        Positions.Add(a); Positions.Add(b); Positions.Add(c);
                        Normals.Add(normal); Normals.Add(normal); Normals.Add(normal);
                        Indices.Add(first); Indices.Add(first + 1); Indices.Add(first + 2);
                    }
                    rockCount++;
                    if (radius >= solidRadius) SolidIndices.AddRange(Indices.GetRange(firstIndex, Indices.Count - firstIndex));
                    continue;
                }

                for (int face = 0; face < FacesPerRock; face++)
                {
                    Vector3 a = Shaped[IcoFaces[face * 3]];
                    Vector3 b = Shaped[IcoFaces[face * 3 + 1]];
                    Vector3 c = Shaped[IcoFaces[face * 3 + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    // Make the winding face outward whatever the source order was.
                    if (Vector3.Dot(normal, (a + b + c) / 3f - centre) < 0f)
                    {
                        Vector3 swap = b;
                        b = c;
                        c = swap;
                        normal = -normal;
                    }
                    int first = Positions.Count;
                    Positions.Add(a);
                    Positions.Add(b);
                    Positions.Add(c);
                    Normals.Add(normal);
                    Normals.Add(normal);
                    Normals.Add(normal);
                    Indices.Add(first);
                    Indices.Add(first + 1);
                    Indices.Add(first + 2);
                }
                rockCount++;
                if (radius >= solidRadius) SolidIndices.AddRange(Indices.GetRange(firstIndex, Indices.Count - firstIndex));
            }

            Mesh mesh = new Mesh();
            mesh.name = "MarsRocks " + chunkIndex;
            if (Positions.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetTriangles(Indices, 0, true);

            solid = null;
            if (SolidIndices.Count > 0)
            {
                solid = new Mesh { name = mesh.name + " solid", indexFormat = mesh.indexFormat };
                solid.SetVertices(Positions);
                solid.SetTriangles(SolidIndices, 0, true);
            }
            return mesh;
        }
    }
}
