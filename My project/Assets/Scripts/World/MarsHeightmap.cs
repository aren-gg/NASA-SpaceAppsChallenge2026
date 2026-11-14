using System;
using Unity.Collections;
using UnityEngine;

namespace MarsSurvival.World
{
    /// <summary>
    /// The prepared elevation tile plus every conversion between heightmap samples,
    /// Unity units, real metres and Mars latitude/longitude.
    ///
    /// Grid convention: ix grows east, iz grows north. The file stores row 0 = north,
    /// so file row = (Height - 1 - iz). Local space: +X east, +Z north, +Y up, origin
    /// at the tile centre, Y = 0 at the tile's lowest elevation.
    /// </summary>
    public sealed class MarsHeightmap : IDisposable
    {
        public readonly MarsTerrainMetadata Meta;
        public readonly int Width;
        public readonly int Height;
        /// <summary>Unity units between neighbouring samples, east-west.</summary>
        public readonly float CellX;
        /// <summary>Unity units between neighbouring samples, north-south.</summary>
        public readonly float CellZ;
        /// <summary>Unity units of height per real metre (includes any exaggeration).</summary>
        public readonly float YPerMeter;
        public readonly float RealMetersPerUnit;
        public readonly float VerticalExaggeration;
        public readonly float BaseElevation;

        private NativeArray<short> samples;

        public NativeArray<short> Samples { get { return samples; } }
        public float SizeX { get { return (Width - 1) * CellX; } }
        public float SizeZ { get { return (Height - 1) * CellZ; } }
        public float MaxY { get { return (Meta.maxElevationM - BaseElevation) * YPerMeter; } }
        /// <summary>Bytes of elevation data held in memory.</summary>
        public long MemoryBytes { get { return (long)Width * Height * sizeof(short); } }

        public MarsHeightmap(TextAsset heightData, TextAsset metadata, float realMetersPerUnit, float verticalExaggeration)
        {
            if (heightData == null) throw new ArgumentNullException("heightData");
            if (metadata == null) throw new ArgumentNullException("metadata");

            Meta = JsonUtility.FromJson<MarsTerrainMetadata>(metadata.text);
            Width = Meta.width;
            Height = Meta.height;

            NativeArray<short> source = heightData.GetData<short>();
            if (Width < 2 || Height < 2 || source.Length != Width * Height)
            {
                throw new InvalidOperationException(
                    "Height data holds " + source.Length + " samples but the metadata declares " +
                    Width + " x " + Height + ". Re-run tools/data/fetch_mars_dem.py.");
            }
            if (!BitConverter.IsLittleEndian)
            {
                throw new PlatformNotSupportedException("Height data is little-endian int16.");
            }
            // Own a copy so worker jobs never depend on the TextAsset's lifetime.
            samples = new NativeArray<short>(source, Allocator.Persistent);

            RealMetersPerUnit = Mathf.Max(0.01f, realMetersPerUnit);
            VerticalExaggeration = Mathf.Max(0.01f, verticalExaggeration);
            CellX = (float)(Meta.metersPerSampleEastWest / RealMetersPerUnit);
            CellZ = (float)(Meta.metersPerSampleNorthSouth / RealMetersPerUnit);
            YPerMeter = VerticalExaggeration / RealMetersPerUnit;
            BaseElevation = Meta.minElevationM;
        }

        public void Dispose()
        {
            if (samples.IsCreated) samples.Dispose();
        }

        /// <summary>Source elevation in real metres at a grid sample (clamped to the tile).</summary>
        public int ElevationAt(int ix, int iz)
        {
            ix = Mathf.Clamp(ix, 0, Width - 1);
            iz = Mathf.Clamp(iz, 0, Height - 1);
            return samples[(Height - 1 - iz) * Width + ix];
        }

        public float ElevationToY(float elevationMeters)
        {
            return (elevationMeters - BaseElevation) * YPerMeter;
        }

        public float YToElevation(float y)
        {
            return y / YPerMeter + BaseElevation;
        }

        /// <summary>Local X/Z to fractional grid coordinates.</summary>
        public void LocalToGrid(float x, float z, out float gx, out float gz)
        {
            gx = x / CellX + (Width - 1) * 0.5f;
            gz = z / CellZ + (Height - 1) * 0.5f;
        }

        public Vector2 GridToLocal(float gx, float gz)
        {
            return new Vector2((gx - (Width - 1) * 0.5f) * CellX, (gz - (Height - 1) * 0.5f) * CellZ);
        }

        public bool Contains(float x, float z)
        {
            return Mathf.Abs(x) <= SizeX * 0.5f && Mathf.Abs(z) <= SizeZ * 0.5f;
        }

        /// <summary>
        /// Real elevation in metres (dataset vertical reference) by bilinear
        /// interpolation of the source samples. Use this for altitude readouts.
        /// </summary>
        public float SampleElevation(float x, float z)
        {
            float gx, gz;
            LocalToGrid(x, z, out gx, out gz);
            gx = Mathf.Clamp(gx, 0f, Width - 1);
            gz = Mathf.Clamp(gz, 0f, Height - 1);
            int ix = Mathf.Min((int)gx, Width - 2);
            int iz = Mathf.Min((int)gz, Height - 2);
            float u = gx - ix;
            float v = gz - iz;
            float south = Mathf.Lerp(ElevationAt(ix, iz), ElevationAt(ix + 1, iz), u);
            float north = Mathf.Lerp(ElevationAt(ix, iz + 1), ElevationAt(ix + 1, iz + 1), u);
            return Mathf.Lerp(south, north, v);
        }

        /// <summary>
        /// Local Y of the visible faceted surface at a detail stride. Uses the same
        /// triangle split as <see cref="MarsChunkMeshJob"/>, so objects placed with it
        /// sit exactly on the rendered facets of that detail level.
        /// </summary>
        public float SampleFacetedY(float x, float z, int stride)
        {
            float gx, gz;
            LocalToGrid(x, z, out gx, out gz);
            float sx = Mathf.Clamp(gx, 0f, Width - 1) / stride;
            float sz = Mathf.Clamp(gz, 0f, Height - 1) / stride;
            int cx = Mathf.Min((int)sx, (Width - 1) / stride - 1);
            int cz = Mathf.Min((int)sz, (Height - 1) / stride - 1);
            float u = Mathf.Clamp01(sx - cx);
            float v = Mathf.Clamp01(sz - cz);

            float h00 = ElevationAt(cx * stride, cz * stride);
            float h10 = ElevationAt((cx + 1) * stride, cz * stride);
            float h01 = ElevationAt(cx * stride, (cz + 1) * stride);
            float h11 = ElevationAt((cx + 1) * stride, (cz + 1) * stride);

            float h;
            if (((cx + cz) & 1) == 0)
            {
                // Diagonal from south-west to north-east.
                h = v >= u
                    ? h00 + (h11 - h01) * u + (h01 - h00) * v
                    : h00 + (h10 - h00) * u + (h11 - h10) * v;
            }
            else
            {
                // Diagonal from north-west to south-east.
                h = u + v <= 1f
                    ? h00 + (h10 - h00) * u + (h01 - h00) * v
                    : h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
            }
            return ElevationToY(h);
        }

        /// <summary>
        /// Real terrain slope in degrees over one stride, from central differences of
        /// the source elevations. Independent of vertical exaggeration.
        /// </summary>
        public float SampleSlopeDegrees(float x, float z, int stride)
        {
            float gx, gz;
            LocalToGrid(x, z, out gx, out gz);
            int ix = Mathf.RoundToInt(gx);
            int iz = Mathf.RoundToInt(gz);
            double dx = (ElevationAt(ix + stride, iz) - ElevationAt(ix - stride, iz)) /
                        (2.0 * stride * Meta.metersPerSampleEastWest);
            double dz = (ElevationAt(ix, iz + stride) - ElevationAt(ix, iz - stride)) /
                        (2.0 * stride * Meta.metersPerSampleNorthSouth);
            return (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * (180.0 / Math.PI));
        }

        /// <summary>Planetocentric latitude (x) and east longitude (y) in degrees.</summary>
        public Vector2 LocalToLatLon(float x, float z)
        {
            float gx, gz;
            LocalToGrid(x, z, out gx, out gz);
            double lon = Meta.lonWestDeg + (Meta.lonEastDeg - Meta.lonWestDeg) * (gx / (Width - 1));
            double lat = Meta.latSouthDeg + (Meta.latNorthDeg - Meta.latSouthDeg) * (gz / (Height - 1));
            return new Vector2((float)lat, (float)lon);
        }

        /// <summary>Local X (x) and Z (y) for a latitude and east longitude in degrees.</summary>
        public Vector2 LatLonToLocal(double latitudeDeg, double longitudeDeg)
        {
            double gx = (longitudeDeg - Meta.lonWestDeg) / (Meta.lonEastDeg - Meta.lonWestDeg) * (Width - 1);
            double gz = (latitudeDeg - Meta.latSouthDeg) / (Meta.latNorthDeg - Meta.latSouthDeg) * (Height - 1);
            return GridToLocal((float)gx, (float)gz);
        }
    }
}
