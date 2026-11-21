using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.World
{
    /// <summary>
    /// Builds one flat-shaded terrain chunk on a worker thread, writing straight
    /// into Unity's mesh buffers (no managed allocations, no main-thread copy).
    ///
    /// Every triangle owns its three vertices so each facet has a single hard
    /// normal, as the art guide requires. Each facet cell is given one colour band
    /// (base / steep / flat) from the real terrain slope averaged over a window
    /// wider than the cell, so colours form coherent regions such as crater walls
    /// instead of single-triangle speckle. The bands become three submeshes, each
    /// drawn with one solid-colour material. A vertical skirt around the chunk
    /// hides gaps where neighbouring chunks use a different detail level.
    /// </summary>
    public struct MarsChunkMeshJob : IJob
    {
        public const int BandBase = 0;
        public const int BandSteep = 1;
        public const int BandFlat = 2;
        public const int BandCount = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
        }

        /// <summary>Bytes per vertex including its 16-bit index.</summary>
        public const int BytesPerVertex = 24 + 2;

        [ReadOnly] public NativeArray<short> heights;
        public int mapWidth;
        public int mapHeight;
        /// <summary>Grid sample of the chunk's south-west corner.</summary>
        public int originX;
        public int originZ;
        /// <summary>Facets along one chunk edge at this detail level.</summary>
        public int cells;
        /// <summary>Heightmap samples per facet edge.</summary>
        public int stride;
        public float cellX;
        public float cellZ;
        public float baseElevation;
        public float yPerMeter;

        /// <summary>Real metres between samples, for slope in real units.</summary>
        public float metersPerSampleX;
        public float metersPerSampleZ;
        /// <summary>Half-width, in samples, of the window the colour slope is averaged over.</summary>
        public int bandHalfWidth;
        /// <summary>tan^2 of the steep and flat slope thresholds.</summary>
        public float steepTan2;
        public float flatTan2;
        public float skirtDepth;
        /// <summary>
        /// Grid rectangle covered by the 1 m patch. Vertices strictly inside it are
        /// lowered by <see cref="sinkDepth"/> (local units) so the patch hides them;
        /// vertices on its edges stay put, so the seam is exact. 0 = no sinking.
        /// </summary>
        public int sinkX0, sinkZ0, sinkX1, sinkZ1;
        public float sinkDepth;

        public Mesh.MeshData mesh;
        /// <summary>Length 3: triangles written per band.</summary>
        public NativeArray<int> bandTriangles;
        /// <summary>Length 2: lowest and highest local Y, for bounds.</summary>
        public NativeArray<float> yRange;

        public static int TriangleCount(int cells)
        {
            return cells * cells * 2 + cells * 8;
        }

        public static void ConfigureBuffers(Mesh.MeshData mesh, int cells)
        {
            int vertexCount = TriangleCount(cells) * 3;
            var layout = new NativeArray<VertexAttributeDescriptor>(2, Allocator.Temp);
            layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
            mesh.SetVertexBufferParams(vertexCount, layout);
            layout.Dispose();
            mesh.SetIndexBufferParams(vertexCount, IndexFormat.UInt16);
        }

        public void Execute()
        {
            NativeArray<Vertex> vertices = mesh.GetVertexData<Vertex>();
            NativeArray<ushort> indices = mesh.GetIndexData<ushort>();

            // Pass 1: count cells per band so each band gets a contiguous range.
            int steep = 0;
            int flat = 0;
            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    int band = CellBand(cx, cz);
                    if (band == BandSteep) steep += 2; else if (band == BandFlat) flat += 2;
                }
            }
            int total = TriangleCount(cells);
            int baseCount = total - steep - flat;
            bandTriangles[BandBase] = baseCount;
            bandTriangles[BandSteep] = steep;
            bandTriangles[BandFlat] = flat;

            // Next free triangle slot per band.
            int cursorBase = 0;
            int cursorSteep = baseCount;
            int cursorFlat = baseCount + steep;

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            Vector3 a, b, c, d, e, f;

            // Pass 2: write the surface.
            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    CellTriangles(cx, cz, out a, out b, out c, out d, out e, out f);
                    int band = CellBand(cx, cz);
                    int slot;
                    if (band == BandSteep) { slot = cursorSteep; cursorSteep += 2; }
                    else if (band == BandFlat) { slot = cursorFlat; cursorFlat += 2; }
                    else { slot = cursorBase; cursorBase += 2; }
                    Write(vertices, slot, a, b, c, FaceNormal(a, b, c));
                    Write(vertices, slot + 1, d, e, f, FaceNormal(d, e, f));

                    // a..f cover all four corners of the cell in both diagonal layouts.
                    float low = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, Mathf.Min(e.y, f.y)));
                    float high = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, Mathf.Max(e.y, f.y)));
                    if (low < minY) minY = low;
                    if (high > maxY) maxY = high;
                }
            }

            // Skirts, in the base band. Edge direction is chosen so the wall faces outward.
            for (int i = 0; i < cells; i++)
            {
                cursorBase = Skirt(vertices, cursorBase, Point(i, 0), Point(i + 1, 0));          // south
                cursorBase = Skirt(vertices, cursorBase, Point(i + 1, cells), Point(i, cells));  // north
                cursorBase = Skirt(vertices, cursorBase, Point(0, i + 1), Point(0, i));          // west
                cursorBase = Skirt(vertices, cursorBase, Point(cells, i), Point(cells, i + 1));  // east
            }

            // Every triangle owns its vertices, so the index buffer is the identity.
            for (int i = 0; i < indices.Length; i++) indices[i] = (ushort)i;

            yRange[0] = minY - skirtDepth;
            yRange[1] = maxY;
        }

        private int Height(int ix, int iz)
        {
            ix = ix < 0 ? 0 : ix >= mapWidth ? mapWidth - 1 : ix;
            iz = iz < 0 ? 0 : iz >= mapHeight ? mapHeight - 1 : iz;
            return heights[(mapHeight - 1 - iz) * mapWidth + ix];
        }

        /// <summary>
        /// Colour band for a facet cell, from the real slope across a square window
        /// centred on the cell. Neighbouring cells share most of their window, so
        /// bands change smoothly and form regions. Independent of vertical exaggeration.
        /// </summary>
        private int CellBand(int cx, int cz)
        {
            int half = bandHalfWidth > stride ? bandHalfWidth : stride;
            int centreX = originX + cx * stride + stride / 2;
            int centreZ = originZ + cz * stride + stride / 2;
            int xl = centreX - half, xr = centreX + half;
            int zl = centreZ - half, zr = centreZ + half;
            if (xl < 0) xl = 0;
            if (zl < 0) zl = 0;
            if (xr > mapWidth - 1) xr = mapWidth - 1;
            if (zr > mapHeight - 1) zr = mapHeight - 1;

            float hSW = Height(xl, zl), hSE = Height(xr, zl);
            float hNW = Height(xl, zr), hNE = Height(xr, zr);
            float gx = ((hSE + hNE) - (hSW + hNW)) / (2f * (xr - xl) * metersPerSampleX);
            float gz = ((hNW + hNE) - (hSW + hSE)) / (2f * (zr - zl) * metersPerSampleZ);
            float g2 = gx * gx + gz * gz;
            if (g2 > steepTan2) return BandSteep;
            if (g2 < flatTan2) return BandFlat;
            return BandBase;
        }

        /// <summary>Chunk-local position of facet-grid corner (cx, cz).</summary>
        private Vector3 Point(int cx, int cz)
        {
            int ix = originX + cx * stride;
            int iz = originZ + cz * stride;
            float elevation = heights[(mapHeight - 1 - iz) * mapWidth + ix];
            float y = (elevation - baseElevation) * yPerMeter;
            if (sinkDepth > 0f && ix > sinkX0 && ix < sinkX1 && iz > sinkZ0 && iz < sinkZ1) y -= sinkDepth;
            return new Vector3(cx * stride * cellX, y, cz * stride * cellZ);
        }

        /// <summary>
        /// The two triangles of a facet cell, clockwise seen from above. The diagonal
        /// alternates in a checkerboard; MarsHeightmap.SampleFacetedY mirrors this rule.
        /// </summary>
        private void CellTriangles(int cx, int cz,
            out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d, out Vector3 e, out Vector3 f)
        {
            Vector3 p00 = Point(cx, cz);
            Vector3 p10 = Point(cx + 1, cz);
            Vector3 p01 = Point(cx, cz + 1);
            Vector3 p11 = Point(cx + 1, cz + 1);
            int globalX = originX / stride + cx;
            int globalZ = originZ / stride + cz;
            if (((globalX + globalZ) & 1) == 0)
            {
                a = p00; b = p01; c = p11;
                d = p00; e = p11; f = p10;
            }
            else
            {
                a = p00; b = p01; c = p10;
                d = p10; e = p01; f = p11;
            }
        }

        private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            return Vector3.Cross(b - a, c - a).normalized;
        }

        private static void Write(NativeArray<Vertex> vertices, int triangleSlot, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            int i = triangleSlot * 3;
            vertices[i] = new Vertex { position = a, normal = normal };
            vertices[i + 1] = new Vertex { position = b, normal = normal };
            vertices[i + 2] = new Vertex { position = c, normal = normal };
        }

        private int Skirt(NativeArray<Vertex> vertices, int triangleSlot, Vector3 p, Vector3 q)
        {
            Vector3 pLow = new Vector3(p.x, p.y - skirtDepth, p.z);
            Vector3 qLow = new Vector3(q.x, q.y - skirtDepth, q.z);
            // Upward normal: a skirt seen through a gap is lit like the ground around it.
            Write(vertices, triangleSlot, p, q, qLow, Vector3.up);
            Write(vertices, triangleSlot + 1, p, qLow, pLow, Vector3.up);
            return triangleSlot + 2;
        }
    }
}
