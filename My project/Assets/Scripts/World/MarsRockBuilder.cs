using System.Collections.Generic;
using UnityEngine;

namespace MarsSurvival.World
{
    /// <summary>
    /// Scatters chunky flat-shaded icosphere rocks over one rock tile.
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
        private static readonly Vector3[] Shaped = new Vector3[12];

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

        /// <param name="chunkCorner">Terrain-local X/Z of the tile's south-west corner (Unity units).</param>
        /// <param name="chunkIndex">Seed for this tile; the same value gives the same rocks.</param>
        /// <returns>A mesh in tile-local space (origin at the tile corner, Y = terrain-local Y).</returns>
        public static Mesh Build(MarsHeightmap map, MarsTerrainConfig config, Vector2 chunkCorner,
            float chunkSizeX, float chunkSizeZ, int chunkIndex, out int rockCount)
        {
            Positions.Clear();
            Normals.Clear();
            Indices.Clear();
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
                if (map.SampleSlopeDegrees(localX, localZ, stride) > config.rockMaxSlopeDegrees) continue;

                // Mostly small rocks with an occasional boulder.
                float radius = Mathf.Lerp(minRadius, maxRadius, sizeT * sizeT * sizeT);
                float groundY = map.SampleFacetedY(localX, localZ, stride);
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
            }

            Mesh mesh = new Mesh();
            mesh.name = "MarsRocks " + chunkIndex;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetTriangles(Indices, 0, true);
            return mesh;
        }
    }
}
