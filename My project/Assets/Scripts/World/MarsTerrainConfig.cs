using UnityEngine;

namespace MarsSurvival.World
{
    /// <summary>
    /// Persistent terrain settings. Session state lives in <see cref="MarsTerrain"/>.
    /// Every distance and size here is in real Mars metres, so the settings stay
    /// correct whatever <see cref="realMetersPerUnityUnit"/> is.
    /// </summary>
    [CreateAssetMenu(menuName = "Mars/Terrain Config", fileName = "MarsTerrainConfig")]
    public class MarsTerrainConfig : ScriptableObject
    {
        [Header("Prepared NASA/USGS data (tools/data/fetch_mars_dem.py)")]
        [Tooltip("Raw int16 little-endian elevations in metres, row 0 = north.")]
        public TextAsset heightData;
        [Tooltip("The .metadata.json written next to the height data.")]
        public TextAsset metadata;

        [Header("Scale")]
        [Tooltip("Real Mars metres represented by one Unity unit. 1 = life size (1:1): one metre in the " +
                 "game is one metre on Mars, horizontally and vertically.")]
        [Min(0.01f)] public float realMetersPerUnityUnit = 1f;
        [Tooltip("1 = true relief. Values above 1 stretch heights only and are reported on the data overlay.")]
        [Min(0.01f)] public float verticalExaggeration = 1f;

        [Header("Streaming (real metres; chunks load inside the render distance and unload outside it)")]
        [Tooltip("Horizontal distance from the viewer within which terrain chunks exist.")]
        [Min(1000f)] public float renderDistanceMeters = 40000f;
        [Tooltip("Extra distance beyond the render distance before a chunk is unloaded, so chunks " +
                 "on the boundary do not load and unload repeatedly.")]
        [Min(0f)] public float unloadMarginMeters = 3000f;
        [Tooltip("Unloaded chunk objects kept for reuse instead of destroyed. Their mesh memory is released.")]
        [Min(0)] public int maxPooledChunks = 48;
        [Tooltip("Set the camera far clip, fog end and sky colour from the render distance, so chunks fade " +
                 "into the haze before they unload instead of popping.")]
        public bool syncFogAndFarClip = true;
        [Tooltip("Fog starts at this fraction of the render distance.")]
        [Range(0f, 0.95f)] public float fogStartFraction = 0.15f;

        [Header("Chunks and level of detail")]
        [Tooltip("Heightmap cells along one chunk edge (64 cells x 200 m = 12.8 km). Power of two that divides (width - 1).")]
        public int chunkCells = 64;
        [Tooltip("Heightmap samples per facet edge at the nearest detail level. 1 uses every 200 m source sample.")]
        public int lod0Stride = 1;
        [Tooltip("Distance (real metres) at which each coarser detail level starts. Each level doubles the facet size.")]
        public float[] lodDistancesMeters = { 3000f, 8000f, 20000f };
        [Tooltip("Extra distance fraction before a chunk drops to a coarser level, to stop flicker.")]
        [Range(0f, 0.5f)] public float lodHysteresis = 0.1f;
        [Tooltip("Mesh build jobs started per frame after the initial load.")]
        [Min(1)] public int maxChunkBuildsPerFrame = 4;
        [Tooltip("Depth (real metres) of the vertical skirt that hides gaps between detail levels.")]
        [Min(0f)] public float skirtDepthMeters = 600f;
        [Tooltip("Attach a MeshCollider to nearest-detail chunks. Leave off if movement uses MarsTerrain.GetGroundY.")]
        public bool generateColliders = false;

        [Header("Flat-colour bands (art guide palette, one solid material each)")]
        public Material baseMaterial;
        [Tooltip("Cells whose real slope, averaged over the smoothing window, exceeds this use the steep material. " +
                 "11.5 degrees marks about the steepest 10% of this tile: crater walls and Mount Sharp.")]
        [Range(0f, 89f)] public float steepSlopeDegrees = 11.5f;
        public Material steepMaterial;
        [Tooltip("Cells flatter than this use the flat material. 0.8 degrees marks about the flattest 7%.")]
        [Range(0f, 89f)] public float flatSlopeDegrees = 0.8f;
        public Material flatMaterial;
        [Tooltip("Half-width, in source samples, of the window slope is averaged over before choosing a colour. " +
                 "Larger values give bigger, calmer colour regions. 4 samples = a 1.6 km window.")]
        [Range(1, 16)] public int bandSmoothingSamples = 4;

        [Header("Rocks (procedural decoration, not NASA data; streamed in small tiles around the viewer)")]
        public bool rocksEnabled = true;
        public int rockSeed = 2026;
        [Tooltip("Edge length (real metres) of one rock tile.")]
        [Min(8f)] public float rockTileSizeMeters = 64f;
        [Tooltip("Rock placement attempts per tile; steep spots are skipped.")]
        [Range(0, 64)] public int rocksPerTile = 24;
        [Tooltip("Rock radius range in real metres.")]
        public Vector2 rockRadiusRangeMeters = new Vector2(0.15f, 1.2f);
        [Range(0f, 89f)] public float rockMaxSlopeDegrees = 15f;
        [Tooltip("Rock tiles exist only within this distance (real metres) of the viewer.")]
        [Min(10f)] public float rockDrawDistanceMeters = 250f;
        [Tooltip("Rock tiles built per frame at most.")]
        [Min(1)] public int maxRockTilesPerFrame = 8;
        public Material rockMaterial;

        public int LodCount
        {
            get { return (lodDistancesMeters != null ? lodDistancesMeters.Length : 0) + 1; }
        }

        /// <summary>Heightmap samples per facet edge at a detail level.</summary>
        public int StrideForLod(int lod)
        {
            int stride = Mathf.Max(1, lod0Stride) << lod;
            return Mathf.Min(stride, chunkCells);
        }

        /// <summary>Converts real metres to Unity units at the configured scale.</summary>
        public float ToUnits(float meters)
        {
            return meters / Mathf.Max(0.01f, realMetersPerUnityUnit);
        }

        private void OnValidate()
        {
            chunkCells = Mathf.ClosestPowerOfTwo(Mathf.Clamp(chunkCells, 4, 64));
            lod0Stride = Mathf.ClosestPowerOfTwo(Mathf.Clamp(lod0Stride, 1, chunkCells));
            if (rockRadiusRangeMeters.y < rockRadiusRangeMeters.x) rockRadiusRangeMeters.y = rockRadiusRangeMeters.x;
            if (flatSlopeDegrees > steepSlopeDegrees) flatSlopeDegrees = steepSlopeDegrees;
        }
    }
}
