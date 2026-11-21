using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.World
{
    /// <summary>Placement and provenance written by tools/data/fetch_hirise_dtm.py.</summary>
    [Serializable]
    public class MarsTerrainInsetMetadata
    {
        public string name;
        public int width;
        public int height;
        public float minElevationM;
        public float maxElevationM;
        public float elevationStepM;
        public string regionalTile;
        public int regionalOriginX;
        public int regionalOriginZ;
        public int regionalCells;
        public double latSouthDeg;
        public double latNorthDeg;
        public double lonWestDeg;
        public double lonEastDeg;
        public float medianOffsetFromRegionalM;
        public string sourceProductId;
        public string sourceInstruments;
        public double sourceGridSpacingM;
        public long downloadedBytes;
        public long heightmapBytes;
        public string retrievedUtc;
    }

    /// <summary>Textures for one paint layer of the 1 m patch.</summary>
    [Serializable]
    public class MarsTerrainLayerTextures
    {
        public string name;
        public Texture2D albedo;
        public Texture2D normal;
        [Tooltip("URP terrain mask: R metallic, G occlusion, B height, A smoothness.")]
        public Texture2D mask;
        [Tooltip("Real metres covered by one repeat of the textures.")]
        [Min(0.1f)] public float tileMeters = 4f;
    }

    /// <summary>
    /// The 1 m HiRISE elevation patch around the outpost, drawn as a Unity Terrain on
    /// top of the 200 m regional mesh.
    ///
    /// The patch covers a square of whole regional cells (its edges are regional grid
    /// lines). Inside it, <see cref="MarsChunkMeshJob"/> sinks the regional mesh out of
    /// sight; the patch's border is blended onto the regional faceted surface, so the
    /// two meet along the grid lines without a step. Heights are real metres from the
    /// HiRISE DTM mosaic (no exaggeration other than the config's). Paint layers are
    /// chosen per 2 m texel from the real slope and local relief.
    /// </summary>
    public sealed class MarsTerrainInset : IDisposable
    {
        public const int Layers = 4;
        public const int LayerDust = 0, LayerSand = 1, LayerBedrock = 2, LayerGravel = 3;

        public readonly MarsTerrainInsetMetadata Meta;
        public readonly Terrain Terrain;
        /// <summary>Regional grid samples of the patch corners (south-west and north-east).</summary>
        public readonly int GridX0, GridZ0, GridX1, GridZ1;
        /// <summary>South-west corner and size in MarsTerrain local units.</summary>
        public readonly float X0, Z0, SizeX, SizeZ;
        public readonly float BuildMilliseconds;
        /// <summary>Where the build time went, for the data overlay.</summary>
        public readonly string BuildBreakdown;
        public long MemoryBytes { get { return (long)Meta.width * Meta.height * 2 + (long)alphaResolution * alphaResolution * 4; } }

        private readonly TerrainData data;
        private readonly int alphaResolution;
        private readonly TerrainLayer[] layers;
        private readonly Material material;

        public static MarsTerrainInset TryCreate(Transform parent, MarsHeightmap map, MarsTerrainConfig config, int layer)
        {
            if (config.insetHeightData == null || config.insetMetadata == null || !config.insetEnabled) return null;
            try
            {
                return new MarsTerrainInset(parent, map, config, layer);
            }
            catch (Exception e)
            {
                Debug.LogError("MarsTerrain: the 1 m HiRISE patch could not be built; using the 200 m terrain only. " + e.Message);
                return null;
            }
        }

        private MarsTerrainInset(Transform parent, MarsHeightmap map, MarsTerrainConfig config, int layer)
        {
            float start = Time.realtimeSinceStartup;
            string breakdown = "";
            Meta = JsonUtility.FromJson<MarsTerrainInsetMetadata>(config.insetMetadata.text);
            if (Meta.regionalTile != map.Meta.name)
                throw new InvalidOperationException("patch was prepared for " + Meta.regionalTile + ", not " + map.Meta.name);
            int n = Meta.width;
            if (Meta.height != n || n < 33 || n > 4097 || ((n - 1) & (n - 2)) != 0)
                throw new InvalidOperationException("patch must be square with 2^k + 1 samples, at most 4097");
            NativeArray<ushort> raw = config.insetHeightData.GetData<ushort>();
            if (raw.Length != n * n) throw new InvalidOperationException("height data does not match the metadata; re-run tools/data/fetch_hirise_dtm.py");

            GridX0 = Meta.regionalOriginX;
            GridZ0 = Meta.regionalOriginZ;
            GridX1 = GridX0 + Meta.regionalCells;
            GridZ1 = GridZ0 + Meta.regionalCells;
            Vector2 sw = map.GridToLocal(GridX0, GridZ0);
            Vector2 ne = map.GridToLocal(GridX1, GridZ1);
            X0 = sw.x; Z0 = sw.y;
            SizeX = ne.x - sw.x; SizeZ = ne.y - sw.y;

            // Vertical range: the data plus the regional surface the border blends onto.
            float yMin = map.ElevationToY(Meta.minElevationM), yMax = map.ElevationToY(Meta.maxElevationM);
            for (int iz = GridZ0; iz <= GridZ1; iz++)
            {
                for (int ix = GridX0; ix <= GridX1; ix++)
                {
                    float y = map.ElevationToY(map.ElevationAt(ix, iz));
                    yMin = Mathf.Min(yMin, y);
                    yMax = Mathf.Max(yMax, y);
                }
            }
            yMin -= 1f;
            yMax += 1f;

            // Heights, normalised to the terrain's vertical range, on worker threads.
            var heights = new NativeArray<float>(n * n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            var heightJob = new HeightJob
            {
                raw = raw,
                regional = map.Samples,
                regionalWidth = map.Width,
                regionalHeight = map.Height,
                size = n,
                gridX0 = GridX0,
                gridZ0 = GridZ0,
                cells = Meta.regionalCells,
                minElevation = Meta.minElevationM,
                elevationStep = Meta.elevationStepM,
                baseElevation = map.BaseElevation,
                yPerMeter = map.YPerMeter,
                yMin = yMin,
                yRange = yMax - yMin,
                blendSamples = Mathf.Max(1f, config.insetBlendMeters / (float)(Meta.regionalCells * map.Meta.metersPerSampleNorthSouth) * (n - 1)),
                heights = heights
            };
            heightJob.Schedule(n, 16).Complete();
            breakdown += "heights job " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("F0") + " ms; ";

            data = new TerrainData();
            data.name = "HiRISE 1 m patch (" + Meta.name + ")";
            data.heightmapResolution = n;
            data.size = new Vector3(SizeX, yMax - yMin, SizeZ);
            const int block = 256;
            float[] flat = new float[block * n];
            float[,] rows = new float[block, n];
            for (int z0 = 0; z0 < n; z0 += block)
            {
                int count = Mathf.Min(block, n - z0);
                if (count != block) rows = new float[count, n];
                NativeArray<float>.Copy(heights, z0 * n, flat, 0, count * n);
                Buffer.BlockCopy(flat, 0, rows, 0, count * n * sizeof(float));
                data.SetHeightsDelayLOD(0, z0, rows);
            }
            data.SyncHeightmap();
            breakdown += "upload heights " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("F0") + " ms; ";

            // Paint layers per texel from slope and local relief.
            alphaResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(config.insetSplatResolution), 256, 4096);
            data.alphamapResolution = alphaResolution;
            layers = BuildLayers(config);
            data.terrainLayers = layers;
            var weights = new NativeArray<float>(alphaResolution * alphaResolution * Layers, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            var splatJob = new SplatJob
            {
                heights = heights,
                size = n,
                resolution = alphaResolution,
                metersPerSample = (float)(Meta.regionalCells * map.Meta.metersPerSampleNorthSouth / (n - 1)),
                metersPerUnitY = (yMax - yMin) / map.YPerMeter,
                edgeFadeTexels = Mathf.Max(1f, config.insetBlendMeters / (float)(Meta.regionalCells * map.Meta.metersPerSampleNorthSouth) * (alphaResolution - 1)),
                weights = weights
            };
            splatJob.Schedule(alphaResolution, 16).Complete();
            breakdown += "paint job " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("F0") + " ms; ";
            heights.Dispose();
            int rowFloats = alphaResolution * Layers;
            float[] flatAlpha = new float[block * rowFloats];
            float[,,] alpha = new float[block, alphaResolution, Layers];
            for (int z0 = 0; z0 < alphaResolution; z0 += block)
            {
                int count = Mathf.Min(block, alphaResolution - z0);
                if (count != block) alpha = new float[count, alphaResolution, Layers];
                NativeArray<float>.Copy(weights, z0 * rowFloats, flatAlpha, 0, count * rowFloats);
                Buffer.BlockCopy(flatAlpha, 0, alpha, 0, count * rowFloats * sizeof(float));
                data.SetAlphamaps(0, z0, alpha);
            }
            weights.Dispose();
            breakdown += "upload paint " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("F0") + " ms; ";

            // Always a private copy, so the keywords below never touch the asset.
            material = config.insetMaterial != null
                ? new Material(config.insetMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            material.name = "HiRISE patch";
            material.hideFlags = HideFlags.DontSave;
            material.SetFloat("_EnableHeightBlend", 1f);
            material.SetFloat("_HeightTransition", config.insetHeightTransition);
            material.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_MASKMAP");
            material.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            material.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");

            GameObject go = new GameObject("HiRISE 1 m patch");
            go.hideFlags = HideFlags.DontSave;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(X0, yMin, Z0);
            Terrain = go.AddComponent<Terrain>();
            Terrain.terrainData = data;
            Terrain.materialTemplate = material;
            Terrain.drawInstanced = true;
            Terrain.heightmapPixelError = config.insetPixelError;
            Terrain.basemapDistance = config.insetBasemapDistanceMeters;
            Terrain.shadowCastingMode = ShadowCastingMode.On;
            Terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Terrain.drawTreesAndFoliage = false;
            Terrain.allowAutoConnect = false;
            if (config.generateColliders) go.AddComponent<TerrainCollider>().terrainData = data;

            BuildMilliseconds = (Time.realtimeSinceStartup - start) * 1000f;
            BuildBreakdown = breakdown + "total " + BuildMilliseconds.ToString("F0") + " ms (cumulative)";
        }

        private static TerrainLayer[] BuildLayers(MarsTerrainConfig config)
        {
            var result = new TerrainLayer[Layers];
            for (int i = 0; i < Layers; i++)
            {
                MarsTerrainLayerTextures src = config.insetLayers != null && i < config.insetLayers.Length ? config.insetLayers[i] : null;
                var layer = new TerrainLayer();
                layer.hideFlags = HideFlags.DontSave;
                if (src != null)
                {
                    layer.name = src.name;
                    layer.diffuseTexture = src.albedo;
                    layer.normalMapTexture = src.normal;
                    layer.maskMapTexture = src.mask;
                    float tile = config.ToUnits(src.tileMeters);
                    layer.tileSize = new Vector2(tile, tile);
                }
                layer.normalScale = 1f;
                layer.metallic = 0f;
                layer.smoothness = 0.1f;
                layer.diffuseRemapMin = Vector4.zero;
                layer.diffuseRemapMax = Vector4.one;
                layer.maskMapRemapMin = Vector4.zero;
                layer.maskMapRemapMax = Vector4.one;
                result[i] = layer;
            }
            return result;
        }

        /// <summary>True if a MarsTerrain-local position is on the patch.</summary>
        public bool Contains(float localX, float localZ)
        {
            return localX >= X0 && localX <= X0 + SizeX && localZ >= Z0 && localZ <= Z0 + SizeZ;
        }

        /// <summary>MarsTerrain-local Y of the patch surface (bilinear between the 1 m samples).</summary>
        public float SampleY(float localX, float localZ)
        {
            float u = Mathf.Clamp01((localX - X0) / SizeX);
            float v = Mathf.Clamp01((localZ - Z0) / SizeZ);
            return Terrain.transform.localPosition.y + data.GetInterpolatedHeight(u, v);
        }

        /// <summary>Slope of the patch surface in degrees (vertical exaggeration included).</summary>
        public float SampleSlopeDegrees(float localX, float localZ)
        {
            float u = Mathf.Clamp01((localX - X0) / SizeX);
            float v = Mathf.Clamp01((localZ - Z0) / SizeZ);
            return data.GetSteepness(u, v);
        }

        /// <summary>True if a MarsTerrain-local rectangle lies wholly on the patch, clear of its blended border.</summary>
        public bool ContainsRect(float minX, float minZ, float maxX, float maxZ, float margin)
        {
            return minX >= X0 + margin && minZ >= Z0 + margin && maxX <= X0 + SizeX - margin && maxZ <= Z0 + SizeZ - margin;
        }

        /// <summary>
        /// Reshapes and repaints part of the patch, for example to grade a building pad.
        /// <paramref name="height"/> maps (local x, local z, current local y) to the new local y;
        /// <paramref name="paint"/> maps (local x, local z) to (compacted, berm) weights 0..1:
        /// compacted ground is painted as dust with a little gravel, berms as gravel and sand.
        /// </summary>
        public void Grade(float minX, float minZ, float maxX, float maxZ,
            Func<float, float, float, float> height, Func<float, float, Vector2> paint)
        {
            int n = data.heightmapResolution;
            float baseY = Terrain.transform.localPosition.y;
            float sizeY = data.size.y;
            int i0 = Mathf.Clamp(Mathf.FloorToInt((minX - X0) / SizeX * (n - 1)), 0, n - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt((maxX - X0) / SizeX * (n - 1)), 0, n - 1);
            int j0 = Mathf.Clamp(Mathf.FloorToInt((minZ - Z0) / SizeZ * (n - 1)), 0, n - 1);
            int j1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - Z0) / SizeZ * (n - 1)), 0, n - 1);
            if (i1 > i0 && j1 > j0)
            {
                float[,] h = data.GetHeights(i0, j0, i1 - i0 + 1, j1 - j0 + 1);
                for (int j = j0; j <= j1; j++)
                {
                    float z = Z0 + (float)j / (n - 1) * SizeZ;
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = X0 + (float)i / (n - 1) * SizeX;
                        float y = baseY + h[j - j0, i - i0] * sizeY;
                        h[j - j0, i - i0] = Mathf.Clamp01((height(x, z, y) - baseY) / sizeY);
                    }
                }
                data.SetHeights(i0, j0, h);
            }

            int r = alphaResolution;
            int a0 = Mathf.Clamp(Mathf.FloorToInt((minX - X0) / SizeX * r), 0, r - 1);
            int a1 = Mathf.Clamp(Mathf.CeilToInt((maxX - X0) / SizeX * r), 0, r - 1);
            int b0 = Mathf.Clamp(Mathf.FloorToInt((minZ - Z0) / SizeZ * r), 0, r - 1);
            int b1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - Z0) / SizeZ * r), 0, r - 1);
            if (a1 <= a0 || b1 <= b0) return;
            float[,,] w = data.GetAlphamaps(a0, b0, a1 - a0 + 1, b1 - b0 + 1);
            for (int b = b0; b <= b1; b++)
            {
                float z = Z0 + (b + 0.5f) / r * SizeZ;
                for (int a = a0; a <= a1; a++)
                {
                    float x = X0 + (a + 0.5f) / r * SizeX;
                    Vector2 p = paint(x, z);
                    if (p.x <= 0f && p.y <= 0f) continue;
                    int ia = a - a0, ib = b - b0;
                    float compacted = Mathf.Clamp01(p.x), berm = Mathf.Clamp01(p.y) * (1f - compacted);
                    float keep = 1f - compacted - berm;
                    w[ib, ia, LayerDust] = w[ib, ia, LayerDust] * keep + compacted * 0.82f + berm * 0.1f;
                    w[ib, ia, LayerSand] = w[ib, ia, LayerSand] * keep + berm * 0.3f;
                    w[ib, ia, LayerBedrock] = w[ib, ia, LayerBedrock] * keep;
                    w[ib, ia, LayerGravel] = w[ib, ia, LayerGravel] * keep + compacted * 0.18f + berm * 0.6f;
                }
            }
            data.SetAlphamaps(a0, b0, w);
        }

        public void Dispose()
        {
            if (Terrain != null) Destroy(Terrain.gameObject);
            Destroy(data);
            if (layers != null) for (int i = 0; i < layers.Length; i++) Destroy(layers[i]);
            Destroy(material);
        }

        private static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ jobs

        /// <summary>One row of the patch: decode, blend the border onto the regional facets, normalise.</summary>
        private struct HeightJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ushort> raw;
            [ReadOnly] public NativeArray<short> regional;
            public int regionalWidth, regionalHeight;
            public int size;
            public int gridX0, gridZ0, cells;
            public float minElevation, elevationStep, baseElevation, yPerMeter;
            public float yMin, yRange;
            public float blendSamples;
            [NativeDisableParallelForRestriction] public NativeArray<float> heights;

            public void Execute(int j)
            {
                int last = size - 1;
                float dz = Mathf.Min(j, last - j);
                float gz = gridZ0 + (float)j / last * cells;
                for (int i = 0; i < size; i++)
                {
                    float y = (minElevation + raw[j * size + i] * elevationStep - baseElevation) * yPerMeter;
                    float d = Mathf.Min(dz, Mathf.Min(i, last - i));
                    if (d < blendSamples)
                    {
                        float t = d / blendSamples;
                        t = t * t * (3f - 2f * t);
                        float gx = gridX0 + (float)i / last * cells;
                        y = Mathf.Lerp(RegionalFacetY(gx, gz), y, t);
                    }
                    heights[j * size + i] = Mathf.Clamp01((y - yMin) / yRange);
                }
            }

            private float At(int ix, int iz)
            {
                return regional[(regionalHeight - 1 - iz) * regionalWidth + ix];
            }

            /// <summary>Same triangle split as MarsHeightmap.SampleFacetedY at stride 1.</summary>
            private float RegionalFacetY(float gx, float gz)
            {
                int cx = Mathf.Clamp((int)gx, 0, regionalWidth - 2);
                int cz = Mathf.Clamp((int)gz, 0, regionalHeight - 2);
                float u = Mathf.Clamp01(gx - cx), v = Mathf.Clamp01(gz - cz);
                float h00 = At(cx, cz), h10 = At(cx + 1, cz), h01 = At(cx, cz + 1), h11 = At(cx + 1, cz + 1);
                float h;
                if (((cx + cz) & 1) == 0)
                    h = v >= u ? h00 + (h11 - h01) * u + (h01 - h00) * v : h00 + (h10 - h00) * u + (h11 - h10) * v;
                else
                    h = u + v <= 1f ? h00 + (h10 - h00) * u + (h01 - h00) * v : h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
                return (h - baseElevation) * yPerMeter;
            }
        }

        /// <summary>One row of paint weights (dust, sand, bedrock, gravel) from slope and local relief.</summary>
        private struct SplatJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heights;
            public int size, resolution;
            public float metersPerSample, metersPerUnitY;
            /// <summary>Texels along the edge over which the paint fades to dust, the colour of the 200 m flats.</summary>
            public float edgeFadeTexels;
            [NativeDisableParallelForRestriction] public NativeArray<float> weights;

            private float H(int x, int z)
            {
                x = x < 0 ? 0 : x >= size ? size - 1 : x;
                z = z < 0 ? 0 : z >= size ? size - 1 : z;
                return heights[z * size + x] * metersPerUnitY;
            }

            private static float Smooth(float a, float b, float x)
            {
                float t = Mathf.Clamp01((x - a) / (b - a));
                return t * t * (3f - 2f * t);
            }

            private static float Noise(int x, int z, int seed)
            {
                uint h = (uint)(x * 374761393 + z * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }

            /// <summary>Smooth value noise with a cell size in texels.</summary>
            private static float ValueNoise(float x, float z, float cell, int seed)
            {
                x /= cell; z /= cell;
                int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
                float u = x - ix, v = z - iz;
                u = u * u * (3f - 2f * u); v = v * v * (3f - 2f * v);
                return Mathf.Lerp(Mathf.Lerp(Noise(ix, iz, seed), Noise(ix + 1, iz, seed), u),
                                  Mathf.Lerp(Noise(ix, iz + 1, seed), Noise(ix + 1, iz + 1, seed), u), v);
            }

            public void Execute(int row)
            {
                float scale = (size - 1) / (float)(resolution - 1);
                int z = Mathf.RoundToInt(row * scale);
                for (int col = 0; col < resolution; col++)
                {
                    int x = Mathf.RoundToInt(col * scale);
                    float gx = (H(x + 1, z) - H(x - 1, z)) / (2f * metersPerSample);
                    float gz = (H(x, z + 1) - H(x, z - 1)) / (2f * metersPerSample);
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;

                    // Local relief: height above the mean of a ring about 8 m out (+ = knob or ridge).
                    int r = Mathf.Max(1, Mathf.RoundToInt(8f / metersPerSample));
                    int rd = Mathf.Max(1, Mathf.RoundToInt(r * 0.707f));
                    float ring = (H(x + r, z) + H(x - r, z) + H(x, z + r) + H(x, z - r) +
                                  H(x + rd, z + rd) + H(x - rd, z + rd) + H(x + rd, z - rd) + H(x - rd, z - rd)) * 0.125f;
                    float relief = H(x, z) - ring;

                    float n1 = ValueNoise(col, row, 24f, 1) * 0.65f + ValueNoise(col, row, 6f, 2) * 0.35f;
                    float n2 = ValueNoise(col, row, 40f, 3);

                    // Bradbury-like plains: pebbly, dusty ground; rippled sand only in hollows and
                    // drifts; bedrock on steep slopes and knobs.
                    float bedrock = Smooth(10f, 22f, slope) + Smooth(0.3f, 1.0f, relief) * 0.9f;
                    float gravel = 0.35f + Smooth(2f, 8f, slope) * 0.5f + Smooth(0.45f, 0.8f, n1) * 0.6f;
                    float dust = 0.2f + Smooth(0.3f, 0.7f, n2) * 0.6f + Smooth(1.5f, 0.3f, slope) * 0.2f;
                    float sand = Smooth(-0.1f, -0.6f, relief) * 1.2f + Smooth(0.3f, 0.1f, n2) * 0.8f;
                    int last = resolution - 1;
                    float edge = Mathf.Min(Mathf.Min(row, last - row), Mathf.Min(col, last - col));
                    float keep = Smooth(0f, edgeFadeTexels, edge);
                    sand *= keep; bedrock *= keep; gravel *= keep;
                    dust = dust * keep + (1f - keep) * 2f;
                    float sum = dust + sand + bedrock + gravel;

                    int o = (row * resolution + col) * Layers;
                    weights[o + LayerDust] = dust / sum;
                    weights[o + LayerSand] = sand / sum;
                    weights[o + LayerBedrock] = bedrock / sum;
                    weights[o + LayerGravel] = gravel / sum;
                }
            }
        }
    }
}
