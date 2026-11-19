using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.World
{
    /// <summary>
    /// Streams the Mars world from the prepared elevation tile.
    ///
    /// The tile is divided into a grid of square chunk slots. Only slots within the
    /// render distance of the viewer hold a chunk; chunks are generated as the
    /// viewer approaches and unloaded once it moves away (with a margin so chunks on
    /// the boundary do not flicker in and out). Each loaded chunk is a flat-shaded
    /// mesh built by <see cref="MarsChunkMeshJob"/> on a worker thread at one of
    /// several detail levels chosen by distance. Work is nearest-first and limited
    /// per frame. Unloaded chunk objects are pooled and their mesh memory released.
    ///
    /// Heights for gameplay queries (ground, altitude, slope) come from the
    /// heightmap, not the meshes, so they work everywhere on the tile, loaded or not.
    /// The terrain shape comes entirely from the dataset; the only invented geometry
    /// is the rock scatter, which streams separately in small tiles around the viewer
    /// and is reported separately. Config distances are real metres and are converted
    /// to Unity units with the configured scale.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsTerrain : MonoBehaviour
    {
        public MarsTerrainConfig config;
        [Tooltip("Set fog and sky colour from the render distance. Turned off at run time by MarsWorldWeatherLink " +
                 "when the weather module owns the atmosphere; the camera far clip is still set either way.")]
        public bool manageFogAndSky = true;
        [Tooltip("Chunks stream around this transform. Defaults to the main camera.")]
        public Transform viewer;
        [Tooltip("At play time, slide the terrain so the Curiosity landing site (where the outpost is built) sits at the " +
                 "world origin. The site is about 50 km from the dataset's origin, and that far out 32-bit positions only " +
                 "hold steps of 2-4 mm: walking drifted sideways and close surfaces (door leaves, frames) flickered.")]
        public bool centreOnLandingSite = true;
        private bool centred;
        /// <summary>How far the terrain was moved at startup (add it to a position from before the move).</summary>
        public Vector3 OriginShift { get; private set; }

        /// <summary>Live counters for the explicit data budget.</summary>
        public struct TerrainStats
        {
            public int chunkSlots;
            public int loadedChunks;
            public int pooledChunks;
            public int[] chunksPerLod;
            public long vertices;
            public long triangles;
            public long meshBytes;
            public long peakMeshBytes;
            public int rocks;
            public int rockTiles;
            public long rockMeshBytes;
            public int chunkBuilds;
            public int chunkLoads;
            public int chunkUnloads;
            public float lastBuildMilliseconds;
        }

        private sealed class Chunk
        {
            public int index = -1;
            public int originX;
            public int originZ;
            public Vector2 centre;
            public GameObject gameObject;
            public Mesh mesh;
            public MeshCollider collider;
            public int lod = -1;
            public int pendingLod = -1;
            public int vertexCount;
            public JobHandle handle;
            public Mesh.MeshDataArray meshData;
            public NativeArray<int> bandTriangles;
            public NativeArray<float> yRange;
        }

        private sealed class RockTile
        {
            public long key;
            public Vector2 centre;
            public GameObject gameObject;
            public Mesh mesh;
            public int rockCount;
        }

        /// <summary>A pending load or detail change, ordered nearest first.</summary>
        private struct WorkItem
        {
            public int slot;
            public int lod;
            public float distance;
        }

        private static readonly System.Comparison<WorkItem> NearestFirst = (a, b) => a.distance.CompareTo(b.distance);

        private MarsHeightmap map;
        private int chunksX;
        private int chunksZ;
        private float chunkSizeX;
        private float chunkSizeZ;
        private Chunk[] slots;
        private readonly List<Chunk> active = new List<Chunk>(256);
        private readonly Stack<Chunk> pool = new Stack<Chunk>();
        private readonly List<WorkItem> work = new List<WorkItem>(256);
        private Material[] bandMaterials;
        private Material rockMaterial;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private TerrainStats stats;
        private int pendingJobs;
        private bool settled;
        private Vector3 lastEvaluatedPosition;
        private Camera viewerCamera;

        private readonly Dictionary<long, RockTile> rockTiles = new Dictionary<long, RockTile>();
        private readonly Stack<RockTile> rockPool = new Stack<RockTile>();
        private readonly List<long> rockKeys = new List<long>();
        private Vector3 lastRockPosition;
        private bool rocksSettled;
        private readonly List<Vector3> rockClearings = new List<Vector3>(); // local x, local z, radius

        public MarsHeightmap Heightmap { get { return map; } }
        public TerrainStats Stats { get { return stats; } }
        public bool IsReady { get { return map != null; } }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (config == null || config.heightData == null || config.metadata == null)
            {
                Debug.LogError("MarsTerrain needs a MarsTerrainConfig with height data and metadata. " +
                               "Run tools/data/fetch_mars_dem.py, then Mars > Terrain > Create Gale Crater Terrain.", this);
                enabled = false;
                return;
            }

            map = new MarsHeightmap(config.heightData, config.metadata, config.realMetersPerUnityUnit, config.verticalExaggeration);

            int chunkCells = config.chunkCells;
            chunksX = (map.Width - 1) / chunkCells;
            chunksZ = (map.Height - 1) / chunkCells;
            if (chunksX < 1 || chunksZ < 1)
            {
                Debug.LogError("MarsTerrain: chunkCells is larger than the heightmap.", this);
                map.Dispose();
                map = null;
                enabled = false;
                return;
            }
            if ((map.Width - 1) % chunkCells != 0 || (map.Height - 1) % chunkCells != 0)
            {
                Debug.LogWarning("MarsTerrain: heightmap size minus one is not a multiple of chunkCells; " +
                                 "the east and north remainder is not drawn.", this);
            }

            CentreOnLandingSite();

            chunkSizeX = chunkCells * map.CellX;
            chunkSizeZ = chunkCells * map.CellZ;
            ResolveMaterials();

            slots = new Chunk[chunksX * chunksZ];
            stats = new TerrainStats { chunkSlots = slots.Length, chunksPerLod = new int[config.LodCount] };

            // Initial load: everything inside the render distance, built in parallel before the first frame.
            Vector3 viewerLocal = ViewerLocalPosition();
            SyncFogAndFarClip();
            float startTime = Time.realtimeSinceStartup;
            CollectWork(viewerLocal);
            for (int i = 0; i < work.Count; i++)
            {
                Chunk chunk = Acquire(work[i].slot);
                Schedule(chunk, work[i].lod);
            }
            JobHandle.ScheduleBatchedJobs();
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pendingLod >= 0) Apply(active[i]);
            }
            stats.lastBuildMilliseconds = (Time.realtimeSinceStartup - startTime) * 1000f;
            rocksSettled = false;
            UpdateRocks(viewerLocal, int.MaxValue);
            lastEvaluatedPosition = viewerLocal;
            settled = true;
        }

        private void OnDisable()
        {
            if (slots != null)
            {
                for (int i = active.Count - 1; i >= 0; i--) DestroyChunk(active[i]);
                while (pool.Count > 0) DestroyChunk(pool.Pop());
                active.Clear();
                slots = null;
            }
            foreach (RockTile tile in rockTiles.Values) DestroyRockTile(tile);
            rockTiles.Clear();
            while (rockPool.Count > 0) DestroyRockTile(rockPool.Pop());
            for (int i = 0; i < ownedMaterials.Count; i++) SafeDestroy(ownedMaterials[i]);
            ownedMaterials.Clear();
            if (map != null)
            {
                map.Dispose();
                map = null;
            }
            pendingJobs = 0;
            settled = false;
        }

        private void Update()
        {
            if (map == null) return;

            // Finish builds started on earlier frames.
            if (pendingJobs > 0)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i].pendingLod >= 0 && active[i].handle.IsCompleted) Apply(active[i]);
                }
            }

            Vector3 viewerLocal = ViewerLocalPosition();
            UpdateRocks(viewerLocal, config.maxRockTilesPerFrame);

            // Re-evaluate chunks only after the viewer has moved about one facet, or while work is outstanding.
            float threshold = config.StrideForLod(0) * map.CellZ;
            float dx = viewerLocal.x - lastEvaluatedPosition.x;
            float dz = viewerLocal.z - lastEvaluatedPosition.z;
            if (settled && pendingJobs == 0 && dx * dx + dz * dz < threshold * threshold) return;

            SyncFogAndFarClip();
            UnloadDistantChunks(viewerLocal);

            CollectWork(viewerLocal);
            int budget = Mathf.Min(config.maxChunkBuildsPerFrame, work.Count);
            for (int i = 0; i < budget; i++)
            {
                WorkItem item = work[i];
                Chunk chunk = slots[item.slot] ?? Acquire(item.slot);
                Schedule(chunk, item.lod);
            }
            if (budget > 0) JobHandle.ScheduleBatchedJobs();

            settled = work.Count <= budget;
            if (settled) lastEvaluatedPosition = viewerLocal;
        }

        // ------------------------------------------------------------------ streaming

        /// <summary>
        /// Fills <see cref="work"/> with slots inside the render distance that are
        /// unloaded or at the wrong detail level, nearest first. Only the square of
        /// slots around the viewer is visited, not the whole grid.
        /// </summary>
        private void CollectWork(Vector3 viewerLocal)
        {
            work.Clear();
            float radius = config.ToUnits(config.renderDistanceMeters);
            Vector2 sw = map.GridToLocal(0, 0);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((viewerLocal.x - radius - sw.x) / chunkSizeX));
            int x1 = Mathf.Min(chunksX - 1, Mathf.FloorToInt((viewerLocal.x + radius - sw.x) / chunkSizeX));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((viewerLocal.z - radius - sw.y) / chunkSizeZ));
            int z1 = Mathf.Min(chunksZ - 1, Mathf.FloorToInt((viewerLocal.z + radius - sw.y) / chunkSizeZ));

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int slot = z * chunksX + x;
                    Chunk chunk = slots[slot];
                    if (chunk != null && chunk.pendingLod >= 0) continue;
                    float distance = DistanceToRect(SlotCentre(x, z), viewerLocal);
                    int lod;
                    if (chunk == null)
                    {
                        if (distance > radius) continue;
                        lod = LodForDistance(distance);
                    }
                    else
                    {
                        lod = DesiredLod(chunk.lod, distance);
                        if (lod == chunk.lod) continue;
                    }
                    work.Add(new WorkItem { slot = slot, lod = lod, distance = distance });
                }
            }
            work.Sort(NearestFirst);
        }

        private void UnloadDistantChunks(Vector3 viewerLocal)
        {
            float limit = config.ToUnits(config.renderDistanceMeters + config.unloadMarginMeters);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Chunk chunk = active[i];
                if (DistanceToRect(chunk.centre, viewerLocal) <= limit) continue;
                // Swap-remove keeps this O(1).
                active[i] = active[active.Count - 1];
                active.RemoveAt(active.Count - 1);
                Release(chunk);
            }
        }

        /// <summary>Takes a pooled chunk object (or creates one) and assigns it to a slot.</summary>
        private Chunk Acquire(int slot)
        {
            Chunk chunk = pool.Count > 0 ? pool.Pop() : CreateChunkObject();
            int x = slot % chunksX;
            int z = slot / chunksX;
            chunk.index = slot;
            chunk.originX = x * config.chunkCells;
            chunk.originZ = z * config.chunkCells;
            chunk.centre = SlotCentre(x, z);
            chunk.lod = -1;
            chunk.vertexCount = 0;
            Vector2 corner = map.GridToLocal(chunk.originX, chunk.originZ);
            chunk.gameObject.transform.localPosition = new Vector3(corner.x, 0f, corner.y);
#if UNITY_EDITOR
            chunk.gameObject.name = "Chunk " + x + "_" + z;
#endif
            chunk.gameObject.SetActive(true);

            slots[slot] = chunk;
            active.Add(chunk);
            stats.loadedChunks++;
            stats.pooledChunks = pool.Count;
            stats.chunkLoads++;
            return chunk;
        }

        /// <summary>Unloads a chunk: cancels its build, frees its mesh memory and pools or destroys the object.</summary>
        private void Release(Chunk chunk)
        {
            if (chunk.pendingLod >= 0)
            {
                chunk.handle.Complete();
                chunk.meshData.Dispose();
                chunk.pendingLod = -1;
                pendingJobs--;
            }
            if (chunk.lod >= 0) stats.chunksPerLod[chunk.lod]--;
            CountMesh(-chunk.vertexCount);
            chunk.vertexCount = 0;
            chunk.lod = -1;
            chunk.mesh.Clear(false);
            if (chunk.collider != null) chunk.collider.sharedMesh = null;

            slots[chunk.index] = null;
            chunk.index = -1;
            stats.loadedChunks--;
            stats.chunkUnloads++;

            if (pool.Count < config.maxPooledChunks)
            {
                chunk.gameObject.SetActive(false);
                pool.Push(chunk);
            }
            else
            {
                DestroyChunk(chunk);
            }
            stats.pooledChunks = pool.Count;
        }

        /// <summary>
        /// Floating origin, done once before anything else is placed: every terrain query goes
        /// through this transform, and the outpost, astronaut and rover are all placed from
        /// those queries afterwards, so the whole play area ends up around (0, 0, 0).
        /// </summary>
        private void CentreOnLandingSite()
        {
            if (!centreOnLandingSite || centred || !Application.isPlaying) return;
            centred = true;
            Vector3 site = LatLonToWorld(MarsSurvival.TimeAndWeather.MarsTime.CuriosityLatitude,
                MarsSurvival.TimeAndWeather.MarsTime.CuriosityLongitudeEast);
            OriginShift = new Vector3(-site.x, 0f, -site.z);
            transform.position += OriginShift;
        }

        private Chunk CreateChunkObject()
        {
            Chunk chunk = new Chunk();
            chunk.gameObject = new GameObject("Chunk");
            chunk.gameObject.hideFlags = HideFlags.DontSave;
            chunk.gameObject.layer = gameObject.layer;
            chunk.gameObject.transform.SetParent(transform, false);

            chunk.mesh = new Mesh();
            chunk.mesh.name = "MarsChunk";
            chunk.mesh.hideFlags = HideFlags.DontSave;
            chunk.mesh.MarkDynamic();
            chunk.gameObject.AddComponent<MeshFilter>().sharedMesh = chunk.mesh;
            MeshRenderer renderer = chunk.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = bandMaterials;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            chunk.bandTriangles = new NativeArray<int>(MarsChunkMeshJob.BandCount, Allocator.Persistent);
            chunk.yRange = new NativeArray<float>(2, Allocator.Persistent);
            return chunk;
        }

        private void DestroyChunk(Chunk chunk)
        {
            if (chunk.index >= 0)
            {
                // Still loaded (called from OnDisable): settle its job and counters first.
                if (chunk.pendingLod >= 0)
                {
                    chunk.handle.Complete();
                    chunk.meshData.Dispose();
                    chunk.pendingLod = -1;
                }
            }
            if (chunk.bandTriangles.IsCreated) chunk.bandTriangles.Dispose();
            if (chunk.yRange.IsCreated) chunk.yRange.Dispose();
            SafeDestroy(chunk.mesh);
            SafeDestroy(chunk.gameObject);
        }

        private Vector2 SlotCentre(int x, int z)
        {
            Vector2 corner = map.GridToLocal(x * config.chunkCells, z * config.chunkCells);
            return new Vector2(corner.x + chunkSizeX * 0.5f, corner.y + chunkSizeZ * 0.5f);
        }

        /// <summary>Horizontal distance from the viewer to the nearest point of a chunk.</summary>
        private float DistanceToRect(Vector2 centre, Vector3 viewerLocal)
        {
            float dx = Mathf.Max(Mathf.Abs(viewerLocal.x - centre.x) - chunkSizeX * 0.5f, 0f);
            float dz = Mathf.Max(Mathf.Abs(viewerLocal.z - centre.y) - chunkSizeZ * 0.5f, 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <param name="distance">Unity units.</param>
        private int LodForDistance(float distance)
        {
            float[] distances = config.lodDistancesMeters;
            float meters = distance * map.RealMetersPerUnit;
            int lod = 0;
            if (distances != null)
            {
                while (lod < distances.Length && meters >= distances[lod]) lod++;
            }
            return Mathf.Min(lod, stats.chunksPerLod.Length - 1);
        }

        private int DesiredLod(int currentLod, float distance)
        {
            int lod = LodForDistance(distance);
            if (currentLod >= 0 && lod > currentLod)
            {
                // Only coarsen once the viewer is clearly past the boundary.
                lod = Mathf.Max(currentLod, LodForDistance(distance / (1f + config.lodHysteresis)));
            }
            return lod;
        }

        /// <summary>Visibility distance (Unity units) at which fog should fully hide the terrain.</summary>
        public float FogEndDistance
        {
            get { return config.ToUnits(config.renderDistanceMeters) * 0.95f; }
        }

        private void SyncFogAndFarClip()
        {
            if (!config.syncFogAndFarClip) return;
            float render = config.ToUnits(config.renderDistanceMeters);
            if (viewerCamera != null)
            {
                viewerCamera.farClipPlane = render + config.ToUnits(config.unloadMarginMeters) + map.MaxY;
            }
            // When another system (such as the weather module) owns fog and sky, only the far clip is set here.
            if (!manageFogAndSky) return;
            if (RenderSettings.fogMode != FogMode.Linear) RenderSettings.fogMode = FogMode.Linear;
            if (!RenderSettings.fog) RenderSettings.fog = true;
            RenderSettings.fogEndDistance = FogEndDistance;
            RenderSettings.fogStartDistance = render * config.fogStartFraction;
            if (viewerCamera != null)
            {
                // A sky the same colour as the fog hides where loaded terrain ends.
                viewerCamera.clearFlags = CameraClearFlags.SolidColor;
                viewerCamera.backgroundColor = RenderSettings.fogColor;
            }
        }

        // ------------------------------------------------------------------ mesh building

        private void Schedule(Chunk chunk, int lod)
        {
            int stride = config.StrideForLod(lod);
            int cells = config.chunkCells / stride;

            chunk.meshData = Mesh.AllocateWritableMeshData(1);
            MarsChunkMeshJob.ConfigureBuffers(chunk.meshData[0], cells);

            float steepTan = Mathf.Tan(config.steepSlopeDegrees * Mathf.Deg2Rad);
            float flatTan = Mathf.Tan(config.flatSlopeDegrees * Mathf.Deg2Rad);
            MarsChunkMeshJob job = new MarsChunkMeshJob
            {
                heights = map.Samples,
                mapWidth = map.Width,
                mapHeight = map.Height,
                originX = chunk.originX,
                originZ = chunk.originZ,
                cells = cells,
                stride = stride,
                cellX = map.CellX,
                cellZ = map.CellZ,
                baseElevation = map.BaseElevation,
                yPerMeter = map.YPerMeter,
                metersPerSampleX = (float)map.Meta.metersPerSampleEastWest,
                metersPerSampleZ = (float)map.Meta.metersPerSampleNorthSouth,
                bandHalfWidth = config.bandSmoothingSamples,
                steepTan2 = steepTan * steepTan,
                flatTan2 = flatTan * flatTan,
                skirtDepth = config.skirtDepthMeters * map.YPerMeter,
                mesh = chunk.meshData[0],
                bandTriangles = chunk.bandTriangles,
                yRange = chunk.yRange
            };
            chunk.handle = job.Schedule();
            chunk.pendingLod = lod;
            pendingJobs++;
        }

        private void Apply(Chunk chunk)
        {
            chunk.handle.Complete();

            float minY = chunk.yRange[0];
            float maxY = chunk.yRange[1];
            Bounds bounds = new Bounds(
                new Vector3(chunkSizeX * 0.5f, (minY + maxY) * 0.5f, chunkSizeZ * 0.5f),
                new Vector3(chunkSizeX, Mathf.Max(maxY - minY, 0.01f), chunkSizeZ));

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            Mesh.MeshData data = chunk.meshData[0];
            data.subMeshCount = MarsChunkMeshJob.BandCount;
            int start = 0;
            for (int band = 0; band < MarsChunkMeshJob.BandCount; band++)
            {
                int count = chunk.bandTriangles[band] * 3;
                SubMeshDescriptor descriptor = new SubMeshDescriptor(start, count, MeshTopology.Triangles);
                // Indices are the identity, so the vertex range equals the index range.
                descriptor.firstVertex = start;
                descriptor.vertexCount = count;
                descriptor.bounds = bounds;
                data.SetSubMesh(band, descriptor, flags);
                start += count;
            }
            Mesh.ApplyAndDisposeWritableMeshData(chunk.meshData, chunk.mesh, flags);
            chunk.mesh.bounds = bounds;

            if (chunk.lod >= 0) stats.chunksPerLod[chunk.lod]--;
            CountMesh(start - chunk.vertexCount);
            stats.chunkBuilds++;
            chunk.vertexCount = start;
            chunk.lod = chunk.pendingLod;
            chunk.pendingLod = -1;
            stats.chunksPerLod[chunk.lod]++;
            pendingJobs--;

            if (config.generateColliders)
            {
                if (chunk.lod == 0)
                {
                    if (chunk.collider == null) chunk.collider = chunk.gameObject.AddComponent<MeshCollider>();
                    chunk.collider.sharedMesh = null;
                    chunk.collider.sharedMesh = chunk.mesh;
                    chunk.collider.enabled = true;
                }
                else if (chunk.collider != null)
                {
                    chunk.collider.enabled = false;
                }
            }
        }

        private void CountMesh(int vertexDelta)
        {
            stats.vertices += vertexDelta;
            stats.triangles += vertexDelta / 3;
            stats.meshBytes += (long)vertexDelta * MarsChunkMeshJob.BytesPerVertex;
            if (stats.meshBytes > stats.peakMeshBytes) stats.peakMeshBytes = stats.meshBytes;
        }

        private Vector3 ViewerLocalPosition()
        {
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            if (viewer != null && (viewerCamera == null || viewerCamera.transform != viewer))
            {
                viewerCamera = viewer.GetComponent<Camera>();
            }
            return viewer != null ? transform.InverseTransformPoint(viewer.position) : Vector3.zero;
        }

        // ------------------------------------------------------------------ public queries

        /// <summary>World-space Y of the visible ground (nearest detail level) below a world position.</summary>
        public float GetGroundY(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float y = map.SampleFacetedY(local.x, local.z, config.StrideForLod(0));
            return transform.TransformPoint(new Vector3(local.x, y, local.z)).y;
        }

        /// <summary>Snaps a world position onto the visible ground.</summary>
        public Vector3 SnapToGround(Vector3 worldPosition)
        {
            worldPosition.y = GetGroundY(worldPosition);
            return worldPosition;
        }

        /// <summary>
        /// Real Mars ground elevation in metres below a world position, relative to the
        /// dataset's vertical reference (see <see cref="MarsTerrainMetadata.verticalReference"/>).
        /// </summary>
        public float GetGroundElevationMeters(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return map.SampleElevation(local.x, local.z);
        }

        /// <summary>Real terrain slope in degrees, unaffected by vertical exaggeration.</summary>
        public float GetSlopeDegrees(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return map.SampleSlopeDegrees(local.x, local.z, config.StrideForLod(0));
        }

        /// <summary>Latitude (x) and east longitude (y) in degrees.</summary>
        public Vector2 GetLatLon(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return map.LocalToLatLon(local.x, local.z);
        }

        /// <summary>World position on the ground at a Mars latitude and east longitude.</summary>
        public Vector3 LatLonToWorld(double latitudeDeg, double longitudeDeg)
        {
            Vector2 local = map.LatLonToLocal(latitudeDeg, longitudeDeg);
            float y = map.SampleFacetedY(local.x, local.y, config.StrideForLod(0));
            return transform.TransformPoint(new Vector3(local.x, y, local.y));
        }

        /// <summary>
        /// Keeps procedural rocks out of a circle around a world position, for example
        /// under a building, and rebuilds the rock tiles it touches straight away.
        /// </summary>
        public void ClearRocks(Vector3 worldCentre, float radiusMeters)
        {
            Vector3 local = transform.InverseTransformPoint(worldCentre);
            float radius = config.ToUnits(radiusMeters);
            rockClearings.Add(new Vector3(local.x, local.z, radius));
            if (map == null) return;

            float tileSize = config.ToUnits(config.rockTileSizeMeters);
            float reach = radius + config.ToUnits(config.rockRadiusRangeMeters.y);
            rockKeys.Clear();
            foreach (KeyValuePair<long, RockTile> pair in rockTiles)
            {
                if (RectDistance(pair.Value.centre, tileSize, local) <= reach) rockKeys.Add(pair.Key);
            }
            for (int i = 0; i < rockKeys.Count; i++)
            {
                RockTile tile = rockTiles[rockKeys[i]];
                rockTiles.Remove(rockKeys[i]);
                ReleaseRockTile(tile);
            }
            rocksSettled = false;
            UpdateRocks(ViewerLocalPosition(), int.MaxValue);
        }

        public bool IsInsideTile(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return map.Contains(local.x, local.z);
        }

        /// <summary>Real metres represented by a distance in Unity units.</summary>
        public float UnitsToRealMeters(float unityUnits)
        {
            return unityUnits * map.RealMetersPerUnit;
        }

        /// <summary>
        /// Plain-text statement of what data the terrain uses and what it costs:
        /// source, resolution, transfer at preparation time, bundled size and live memory.
        /// </summary>
        public string BuildDataReport()
        {
            if (map == null) return "Mars terrain is not loaded.";
            MarsTerrainMetadata m = map.Meta;
            StringBuilder sb = new StringBuilder(1200);
            sb.Append("MARS TERRAIN DATA\n");
            sb.Append("Source      ").Append(m.sourceProductId).Append('\n');
            sb.Append("            ").Append(m.sourceInstruments).Append('\n');
            sb.Append("Grid        ").Append(m.metersPerSampleNorthSouth.ToString("F1")).Append(" m N-S, ")
              .Append(m.metersPerSampleEastWest.ToString("F1")).Append(" m E-W per sample; resampling: ").Append(m.resampling).Append('\n');
            sb.Append("Tile        ").Append(m.width).Append(" x ").Append(m.height).Append(" samples = ")
              .Append(((m.width - 1) * m.metersPerSampleEastWest / 1000.0).ToString("F1")).Append(" x ")
              .Append(((m.height - 1) * m.metersPerSampleNorthSouth / 1000.0).ToString("F1")).Append(" km\n");
            sb.Append("            lat ").Append(m.latSouthDeg.ToString("F3")).Append(" to ").Append(m.latNorthDeg.ToString("F3"))
              .Append(", lon ").Append(m.lonWestDeg.ToString("F3")).Append(" to ").Append(m.lonEastDeg.ToString("F3")).Append(" E\n");
            sb.Append("Elevation   ").Append(m.minElevationM).Append(" to ").Append(m.maxElevationM).Append(" m, ")
              .Append(m.verticalReference).Append('\n');
            sb.Append("Missing     ").Append(m.noDataSamplesFilled).Append(" samples filled\n");
            sb.Append("Downloaded  ").Append(m.downloadedBytes.ToString("N0")).Append(" bytes in ").Append(m.downloadRequests)
              .Append(" requests (source file ").Append(m.sourceFileBytes.ToString("N0")).Append(" bytes), ").Append(m.retrievedUtc).Append('\n');
            sb.Append("Bundled     ").Append(m.heightmapBytes.ToString("N0")).Append(" bytes; no network use at run time\n");
            sb.Append("Scale       1:").Append(map.RealMetersPerUnit.ToString("0.##")).Append(" horizontal, vertical x")
              .Append(map.VerticalExaggeration.ToString("0.##")).Append("; world ")
              .Append(map.SizeX.ToString("F0")).Append(" x ").Append(map.SizeZ.ToString("F0")).Append(" units, relief ")
              .Append(map.MaxY.ToString("F1")).Append(" units\n");
            sb.Append("Facets      nearest ").Append((config.StrideForLod(0) * m.metersPerSampleNorthSouth).ToString("F0"))
              .Append(" m real, farthest ").Append((config.StrideForLod(config.LodCount - 1) * m.metersPerSampleNorthSouth).ToString("F0"))
              .Append(" m real\n");
            sb.Append("Streaming   render distance ").Append((config.renderDistanceMeters / 1000f).ToString("F0")).Append(" km real (")
              .Append(config.ToUnits(config.renderDistanceMeters).ToString("F0")).Append(" units); ")
              .Append(stats.loadedChunks).Append(" of ").Append(stats.chunkSlots).Append(" chunks loaded, ")
              .Append(stats.pooledChunks).Append(" pooled, ").Append(pendingJobs).Append(" building\n");
            sb.Append("            per detail level:");
            for (int i = 0; i < stats.chunksPerLod.Length; i++) sb.Append(' ').Append(stats.chunksPerLod[i]);
            sb.Append("; loads ").Append(stats.chunkLoads).Append(", unloads ").Append(stats.chunkUnloads)
              .Append(", builds ").Append(stats.chunkBuilds).Append("; first load ")
              .Append(stats.lastBuildMilliseconds.ToString("F0")).Append(" ms\n");
            sb.Append("Memory      heightmap ").Append(map.MemoryBytes.ToString("N0")).Append(" B; meshes ")
              .Append(stats.meshBytes.ToString("N0")).Append(" B (peak ").Append(stats.peakMeshBytes.ToString("N0")).Append(" B; ")
              .Append(stats.vertices.ToString("N0")).Append(" vertices, ")
              .Append(stats.triangles.ToString("N0")).Append(" triangles)\n");
            sb.Append("Procedural  ").Append(stats.rocks).Append(" rocks in ").Append(stats.rockTiles)
              .Append(" tiles within ").Append(config.rockDrawDistanceMeters.ToString("F0"))
              .Append(" m (decoration, not data), ").Append(stats.rockMeshBytes.ToString("N0")).Append(" B");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ materials

        private void ResolveMaterials()
        {
            // Palette from docs/game-design-references.md.
            Material baseMaterial = config.baseMaterial != null ? config.baseMaterial : CreateSolidMaterial("Terrain Terracotta", MarsPalette.Terracotta);
            Material steepMaterial = config.steepMaterial != null ? config.steepMaterial : CreateSolidMaterial("Terrain Mauve", MarsPalette.Mauve);
            Material flatMaterial = config.flatMaterial != null ? config.flatMaterial : CreateSolidMaterial("Terrain Dusty Rose", MarsPalette.DustyRose);
            bandMaterials = new Material[MarsChunkMeshJob.BandCount];
            bandMaterials[MarsChunkMeshJob.BandBase] = baseMaterial;
            bandMaterials[MarsChunkMeshJob.BandSteep] = steepMaterial;
            bandMaterials[MarsChunkMeshJob.BandFlat] = flatMaterial;
            rockMaterial = config.rockMaterial != null ? config.rockMaterial : CreateSolidMaterial("Rock Olive", MarsPalette.MutedOlive);
        }

        private Material CreateSolidMaterial(string materialName, Color color)
        {
            // Fallback for scenes without material assets. Builds should assign assets in
            // the config so the shader is guaranteed to be included.
            Material material = MarsPalette.CreateMatteMaterial(materialName, color);
            material.hideFlags = HideFlags.DontSave;
            ownedMaterials.Add(material);
            return material;
        }

        // ------------------------------------------------------------------ rocks

        /// <summary>
        /// Streams rock tiles: small square tiles on their own grid, built when within the
        /// rock draw distance and pooled when beyond it. Re-evaluated after the viewer moves
        /// a quarter tile, or while tiles are still waiting to be built.
        /// </summary>
        private void UpdateRocks(Vector3 viewerLocal, int buildBudget)
        {
            if (!config.rocksEnabled || config.rocksPerTile <= 0) return;
            float tileSize = config.ToUnits(config.rockTileSizeMeters);
            float drawDistance = config.ToUnits(config.rockDrawDistanceMeters);
            float dx = viewerLocal.x - lastRockPosition.x;
            float dz = viewerLocal.z - lastRockPosition.z;
            float threshold = tileSize * 0.25f;
            if (rocksSettled && dx * dx + dz * dz < threshold * threshold) return;

            // Remove tiles that are now too far away.
            float removeDistance = drawDistance + tileSize;
            rockKeys.Clear();
            foreach (KeyValuePair<long, RockTile> pair in rockTiles)
            {
                if (RectDistance(pair.Value.centre, tileSize, viewerLocal) > removeDistance) rockKeys.Add(pair.Key);
            }
            for (int i = 0; i < rockKeys.Count; i++)
            {
                RockTile tile = rockTiles[rockKeys[i]];
                rockTiles.Remove(rockKeys[i]);
                ReleaseRockTile(tile);
            }

            // Build missing tiles in range, within this frame's budget.
            int tx0 = Mathf.FloorToInt((viewerLocal.x - drawDistance) / tileSize);
            int tx1 = Mathf.FloorToInt((viewerLocal.x + drawDistance) / tileSize);
            int tz0 = Mathf.FloorToInt((viewerLocal.z - drawDistance) / tileSize);
            int tz1 = Mathf.FloorToInt((viewerLocal.z + drawDistance) / tileSize);
            bool complete = true;
            for (int tz = tz0; tz <= tz1; tz++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    long key = ((long)tx << 32) ^ (uint)tz;
                    if (rockTiles.ContainsKey(key)) continue;
                    Vector2 centre = new Vector2((tx + 0.5f) * tileSize, (tz + 0.5f) * tileSize);
                    if (RectDistance(centre, tileSize, viewerLocal) > drawDistance) continue;
                    if (!map.Contains(centre.x, centre.y)) continue;
                    if (buildBudget <= 0)
                    {
                        complete = false;
                        continue;
                    }
                    rockTiles.Add(key, BuildRockTile(key, tx, tz, tileSize, centre));
                    buildBudget--;
                }
            }
            rocksSettled = complete;
            if (complete) lastRockPosition = viewerLocal;
        }

        private RockTile BuildRockTile(long key, int tx, int tz, float tileSize, Vector2 centre)
        {
            RockTile tile;
            if (rockPool.Count > 0)
            {
                tile = rockPool.Pop();
                SafeDestroy(tile.mesh);
                tile.gameObject.SetActive(true);
            }
            else
            {
                tile = new RockTile();
                tile.gameObject = new GameObject("Rocks");
                tile.gameObject.hideFlags = HideFlags.DontSave;
                tile.gameObject.layer = gameObject.layer;
                tile.gameObject.transform.SetParent(transform, false);
                tile.gameObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = tile.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = rockMaterial;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            tile.key = key;
            tile.centre = centre;
            Vector2 corner = new Vector2(tx * tileSize, tz * tileSize);
            tile.gameObject.transform.localPosition = new Vector3(corner.x, 0f, corner.y);
            int seed = unchecked(tx * 73856093 ^ tz * 19349663);
            tile.mesh = MarsRockBuilder.Build(map, config, corner, tileSize, tileSize, seed, out tile.rockCount, rockClearings);
            tile.mesh.hideFlags = HideFlags.DontSave;
            tile.gameObject.GetComponent<MeshFilter>().sharedMesh = tile.mesh;

            stats.rocks += tile.rockCount;
            stats.rockTiles++;
            stats.rockMeshBytes += (long)tile.rockCount * MarsRockBuilder.BytesPerRock;
            return tile;
        }

        private void ReleaseRockTile(RockTile tile)
        {
            stats.rocks -= tile.rockCount;
            stats.rockTiles--;
            stats.rockMeshBytes -= (long)tile.rockCount * MarsRockBuilder.BytesPerRock;
            tile.rockCount = 0;
            // Free the rock mesh now; the object is kept for reuse.
            tile.gameObject.GetComponent<MeshFilter>().sharedMesh = null;
            SafeDestroy(tile.mesh);
            tile.mesh = null;
            tile.gameObject.SetActive(false);
            rockPool.Push(tile);
        }

        private void DestroyRockTile(RockTile tile)
        {
            if (tile.gameObject != null && tile.gameObject.activeSelf)
            {
                stats.rocks -= tile.rockCount;
                stats.rockTiles--;
                stats.rockMeshBytes -= (long)tile.rockCount * MarsRockBuilder.BytesPerRock;
            }
            SafeDestroy(tile.mesh);
            SafeDestroy(tile.gameObject);
        }

        private static float RectDistance(Vector2 centre, float size, Vector3 point)
        {
            float dx = Mathf.Max(Mathf.Abs(point.x - centre.x) - size * 0.5f, 0f);
            float dz = Mathf.Max(Mathf.Abs(point.z - centre.y) - size * 0.5f, 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
