using System;
using System.Collections.Generic;
using System.Collections;
using MarsTrainer.Data;
using MarsTrainer.Core;
using MarsTrainer.Gameplay.Player;
using MarsTrainer.Gameplay.Radiation;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using MarsTrainer.Sim.World;
using MarsTrainer.Sim.Systems;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsTrainer.Gameplay.World
{
    [DefaultExecutionOrder(1000)]
    public sealed class TerrainGenerator : MonoBehaviour
    {
        [SerializeField] private MarsSurvival.World.MarsTerrainConfig openWorldConfig;
        private OpenWorldTerrain openSurface;
        public MarsSurvival.World.MarsTerrainConfig OpenWorldConfig { get => openWorldConfig; set => openWorldConfig = value; }
        public MarsSurvival.World.MarsTerrain StreamedTerrain => openSurface != null ? openSurface.Terrain : null;
        public bool OpenWorldActive => openSurface != null && openSurface.Ready;
        [SerializeField] private Texture2D heightmap;
        [SerializeField] private Shader surfaceShader;
        [SerializeField] private TextAsset elevationData;
        [SerializeField] private TextAsset elevationMetadata;
        public TextAsset ElevationData { get => elevationData; set => elevationData = value; }
        public TextAsset ElevationMetadata { get => elevationMetadata; set => elevationMetadata = value; }
        public bool UsesBundledElevation { get; private set; }
        [SerializeField] private int seed = GameConstants.DataSeed;
        [SerializeField] private bool randomizeSeed = true;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly List<Vector2> caves = new List<Vector2>();
        private float[,] heights;
        [SerializeField] private MarsTrainer.Look.MarsLookProfile lookProfile;
        private Simulation simulation;
        private ResearchSystem research;
        private ExpeditionSystem expedition;
        private bool drivesClock;
        private ResearchNode[] researchCatalog = Array.Empty<ResearchNode>();
        // The UI composition root supplies authored nodes before standalone Start runs.
        public bool UsesMissionHud { get; set; }
        public void ConfigureResearch(ResearchNode[] catalog)
        {
            if (simulation != null) return; // External Build owns its own research system.
            researchCatalog = catalog ?? Array.Empty<ResearchNode>();
        }
        public Shader SurfaceShader { get => surfaceShader; set => surfaceShader = value; }
        public Texture2D Heightmap { get => heightmap; set => heightmap = value; }
        public IReadOnlyList<Vector2> CavePositions => caves;
        public PlayerController Player { get; private set; }
        public ResearchSystem Research => research;
        public Simulation Simulation => simulation;
        public MissionSession Mission { get; private set; }
        public int RunSeed { get; private set; }

        private IEnumerator Start()
        {
            if (simulation != null) yield break;
            // Standalone P9 scene: external games call Build with their shared simulation.
            var data = new DataRepository();
            yield return data.Load();
            GameServices.DataRepository = data;
            int runSeed = randomizeSeed ? Environment.TickCount : seed;
            Mission = new MissionSession(data, runSeed, researchCatalog,
                openWorldConfig != null ? GameConstants.OpenWorldRunDurationSeconds : GameConstants.RunDurationSeconds);
            if (openWorldConfig != null) Mission.Simulation.Clock.Tick(
                Mission.Simulation.Clock.SecondsPerSol * GameConstants.OpenWorldLandingSol);
            var sim = Mission.Simulation;
            Build(sim, Mission.Research, runSeed);
            Debug.Log("P12_READY bundledData=" + !data.UsingFallback + "; seed=" + runSeed + "; rows=" + data.Schedule.Count + "; crops=" + data.Crops.Count);
            drivesClock = true;
            GameServices.SimClock = sim.Clock;
            GameServices.GameState = sim.State;
            float viewDistance = OpenWorldActive ? StreamedTerrain.config.ToUnits(
                StreamedTerrain.config.renderDistanceMeters + StreamedTerrain.config.unloadMarginMeters) + StreamedTerrain.Heightmap.MaxY
                : GameConstants.CameraFarClip;
            var camera = Player.CreateFollowCamera(transform, OpenWorldActive, viewDistance);
            if (OpenWorldActive)
            {
                StreamedTerrain.viewer = camera.transform;
                Debug.Log("OPEN_WORLD_READY width=" + StreamedTerrain.Heightmap.SizeX.ToString("0") +
                    "; depth=" + StreamedTerrain.Heightmap.SizeZ.ToString("0") + "; scale=" + StreamedTerrain.config.realMetersPerUnityUnit);
            }
            if (!UsesMissionHud) gameObject.AddComponent<WorldHud>().Initialize(this);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-p12Smoke") >= 0)
            {
                yield return new WaitForSeconds(GameConstants.MissionSmokeSeconds);
                bool passed = !data.UsingFallback && sim.Clock.ElapsedSols > 0 && sim.Survival != null &&
                    (openWorldConfig == null || OpenWorldActive);
                Debug.Log(passed ? "P12_SMOKE_PASS" : "P12_SMOKE_FAIL");
                Application.Quit(passed ? 0 : 1);
            }
        }

        public void Build(Simulation sim, ResearchSystem rp, int runSeed)
        {
            if (simulation != null) throw new InvalidOperationException("World is already built.");
            simulation = sim ?? throw new ArgumentNullException(nameof(sim));
            research = rp ?? throw new ArgumentNullException(nameof(rp));
            RunSeed = runSeed;
            var rng = new System.Random(runSeed);
            int count = rng.Next(GameConstants.CaveMinCount, GameConstants.CaveMaxCount + 1);
            float offset = (float)rng.NextDouble() * Mathf.PI * 2;
            for (int i = 0; i < count; i++)
            {
                float angle = offset + i * Mathf.PI * 2 / count;
                float distance = Mathf.Lerp(GameConstants.CaveMinDistance, GameConstants.CaveMaxDistance, (float)rng.NextDouble());
                caves.Add(new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * distance);
            }
            if (openWorldConfig != null)
            {
                openSurface = gameObject.AddComponent<OpenWorldTerrain>();
                if (!openSurface.Initialize(openWorldConfig)) { Destroy(openSurface); openSurface = null; }
            }
            if (!OpenWorldActive) GenerateHeights(rng);
            else UsesBundledElevation = true;
            Material ground = MakeMaterial(GameConstants.GroundColor);
            Material rockMaterial = MakeMaterial(GameConstants.RockColor);
            Material caveMaterial = MakeMaterial(GameConstants.ShelterColor);
            Material baseMaterial = MakeMaterial(GameConstants.BaseColor);
            Material pickupMaterial = MakeMaterial(GameConstants.PickupColor);
            if (!OpenWorldActive) BuildChunks(ground);
            Mesh rock = FlatShadedMesh.Rock(); Own(rock);
            if (!OpenWorldActive)
            {
                var scatter = new GameObject("Props");
                scatter.transform.SetParent(transform, false);
                scatter.AddComponent<PropScatter>().Build(this, rockMaterial, rock, runSeed);
            }
            var playerOwner = new GameObject("Astronaut");
            playerOwner.transform.SetParent(transform, false);
            playerOwner.transform.localPosition = new Vector3(0,
                SampleHeight(0, 0) + GameConstants.BaseDeckThickness + GameConstants.PlayerSpawnHeight,
                GameConstants.PlayerSpawnZ);
            EnergySystem energy = null;
            for (int i = 0; i < sim.Systems.Count; i++)
                if (sim.Systems[i] is EnergySystem system) { energy = system; break; }
            expedition = new ExpeditionSystem(sim.Clock, energy, OpenWorldActive ? GameConstants.OpenWorldTankSeconds : GameConstants.OxygenTankSeconds);
            sim.RegisterSystem(expedition);
            Player = playerOwner.AddComponent<PlayerController>();
            Player.Initialize(sim, expedition);
            if (OpenWorldActive) Player.WalkingSurface = openSurface;
            Box(playerOwner.transform, "Suit", Vector3.up * GameConstants.PlayerHeight / 2,
                new Vector3(GameConstants.PlayerRadius * 2, GameConstants.PlayerHeight, GameConstants.PlayerRadius * 2),
                baseMaterial, false);
            BuildBase(sim, baseMaterial);
            for (int i = 0; i < caves.Count; i++)
            {
                Vector2 point = caves[i];
                var cave = new GameObject("Cave " + (i + 1));
                cave.transform.SetParent(transform, false);
                cave.transform.localPosition = new Vector3(point.x, SampleHeight(point.x, point.y), point.y);
                cave.transform.localRotation = Quaternion.LookRotation(new Vector3(point.x, 0, point.y));
                cave.AddComponent<CaveShelter>().Build(sim, playerOwner.transform, caveMaterial);
                var pickup = new GameObject("Research sample");
                pickup.transform.SetParent(cave.transform, false);
                pickup.transform.localPosition = Vector3.up * GameConstants.PickupHeight;
                pickup.AddComponent<ResearchPickup>().Initialize(sim, rp, playerOwner.transform);
                var visual = new GameObject("Crystal");
                visual.transform.SetParent(pickup.transform, false);
                visual.transform.localScale = Vector3.one * GameConstants.PickupVisualScale;
                visual.AddComponent<MeshFilter>().sharedMesh = rock;
                visual.AddComponent<MeshRenderer>().sharedMaterial = pickupMaterial;
            }
            var zones = GetComponentsInChildren<ShelterZone>();
            if (energy != null)
                playerOwner.AddComponent<RecallDrone>().Initialize(sim, energy, playerOwner.transform, zones,
                    flying => { if (Player == null) return; Player.enabled = !flying; Player.GetComponent<CharacterController>().enabled = !flying; });
            if (Mission != null)
            {
                playerOwner.AddComponent<PlayerActions>().Initialize(sim, Mission.Inventory, Mission.Energy);
                gameObject.AddComponent<OutpostView>().Initialize(this, Mission, baseMaterial, pickupMaterial, caveMaterial);
            }
            if (Application.isPlaying) BuildLook(sim, playerOwner.transform, baseMaterial);
        }

        public void SetInputBlocked(bool blocked) { if (Player != null) Player.InputBlocked = blocked; }
        public string LocationCaption()
        {
            if (Player == null) return "Gale Crater";
            float distance = new Vector2(Player.transform.localPosition.x, Player.transform.localPosition.z).magnitude;
            if (distance <= GameConstants.BaseFlatRadius) return "Base / Shelter";
            var position = Player.transform.localPosition;
            float bearing = Mathf.Atan2(-position.x, -position.z) * Mathf.Rad2Deg;
            float angle = Mathf.DeltaAngle(Player.ViewYaw, bearing);
            string direction = Mathf.Abs(angle) <= GameConstants.NavigationAheadAngle ? "Ahead"
                : Mathf.Abs(angle) >= GameConstants.NavigationBehindAngle ? "Behind" : angle < 0 ? "Left" : "Right";
            return "Base " + distance.ToString("0") + " m / " + direction;
        }

        public void Exercise()
        {
            if (Mission != null && Mission.AtBase && !Mission.Simulation.Survival.Sleeping)
                Player.GetComponent<PlayerActions>().Exercise(GameConstants.MissionExerciseSols);
        }

        private void BuildLook(Simulation sim, Transform player, Material suitMaterial)
        {
            BuildLighting();
            if (lookProfile == null) lookProfile = Resources.Load<MarsTrainer.Look.MarsLookProfile>("MarsLook");
            var owner = new GameObject("P11 Weather");
            owner.SetActive(false); owner.transform.SetParent(transform, false);
            var weather = owner.AddComponent<MarsWeather.WeatherController>();
            weather.look = lookProfile;
            weather.sun = GetComponentInChildren<Light>();
            var presenter = gameObject.AddComponent<MarsTrainer.Gameplay.Weather.WeatherPresenter>();
            presenter.visuals = weather; presenter.viewer = player;
            presenter.suitShell = Box(player, "Suit shield shell", Vector3.up * GameConstants.PlayerHeight / 2,
                new Vector3(GameConstants.PlayerRadius * 2 * GameConstants.LookShellScale,
                    GameConstants.PlayerHeight * GameConstants.LookShellHeight,
                    GameConstants.PlayerRadius * 2 * GameConstants.LookShellScale), suitMaterial, false).GetComponent<Renderer>();
            presenter.Initialize(sim);
            if (OpenWorldActive)
            {
                var link = owner.AddComponent<MarsSurvival.Integration.MarsWorldWeatherLink>();
                link.terrain = StreamedTerrain; link.weather = weather; link.debugKeys = false;
            }
            owner.SetActive(true);
        }

        private void GenerateHeights(System.Random rng)
        {
            MissionElevation elevation = null;
            if (elevationData != null && elevationMetadata != null)
            {
                try { elevation = new MissionElevation(elevationData.bytes, elevationMetadata.text); }
                catch (Exception error) { Debug.LogWarning("Mission elevation unavailable; using fallback: " + error.Message, this); }
            }
            UsesBundledElevation = elevation != null;
            bool readable = heightmap != null && heightmap.isReadable;
            if (elevation == null && !readable) Debug.LogWarning("P9 heightmap missing or unreadable; using seeded terrain noise.", this);
            int cells = GameConstants.WorldCells;
            heights = new float[cells + 1, cells + 1];
            float amplitude = 1 + ((float)rng.NextDouble() * 2 - 1) * GameConstants.WorldHeightVariation;
            float noiseX = (float)rng.NextDouble() * GameConstants.WorldSize;
            float noiseZ = (float)rng.NextDouble() * GameConstants.WorldSize;
            for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++)
                {
                    float u = (float)x / cells, v = (float)z / cells;
                    heights[x, z] = elevation != null ? elevation.Sample(u, v) * GameConstants.WorldHeight * amplitude : readable ? heightmap.GetPixelBilinear(u, v).r * GameConstants.WorldHeight * amplitude
                        : Mathf.PerlinNoise(u * 2 + noiseX, v * 2 + noiseZ) * GameConstants.WorldHeight;
                    heights[x, z] += Mathf.PerlinNoise(x * GameConstants.WorldSize / cells * GameConstants.WorldNoiseScale + noiseX,
                        z * GameConstants.WorldSize / cells * GameConstants.WorldNoiseScale + noiseZ) * GameConstants.WorldNoiseHeight;
                }
            float baseHeight = SampleHeight(0, 0);
            var caveHeights = new float[caves.Count];
            for (int i = 0; i < caves.Count; i++) caveHeights[i] = SampleHeight(caves[i].x, caves[i].y);
            for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++)
                {
                    var point = new Vector2((float)x / cells - 0.5f, (float)z / cells - 0.5f) * GameConstants.WorldSize;
                    heights[x, z] = Flatten(heights[x, z], baseHeight, point.magnitude, GameConstants.BaseFlatRadius);
                    for (int i = 0; i < caves.Count; i++)
                        heights[x, z] = Flatten(heights[x, z], caveHeights[i], Vector2.Distance(point, caves[i]), GameConstants.CaveFlatRadius);
                }
        }

        private static float Flatten(float height, float target, float distance, float radius)
        {
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((distance - radius) / GameConstants.TerrainBlendWidth));
            return Mathf.Lerp(target, height, blend);
        }

        public float SampleHeight(float x, float z)
        {
            if (OpenWorldActive)
                return transform.InverseTransformPoint(StreamedTerrain.SnapToGround(transform.TransformPoint(new Vector3(x, 0, z)))).y;
            float gx = Mathf.Clamp01(x / GameConstants.WorldSize + 0.5f) * GameConstants.WorldCells;
            float gz = Mathf.Clamp01(z / GameConstants.WorldSize + 0.5f) * GameConstants.WorldCells;
            int ix = Mathf.Min((int)gx, GameConstants.WorldCells - 1);
            int iz = Mathf.Min((int)gz, GameConstants.WorldCells - 1);
            return Mathf.Lerp(Mathf.Lerp(heights[ix, iz], heights[ix + 1, iz], gx - ix),
                Mathf.Lerp(heights[ix, iz + 1], heights[ix + 1, iz + 1], gx - ix), gz - iz);
        }

        private void BuildChunks(Material material)
        {
            int size = GameConstants.WorldChunkCells;
            float spacing = GameConstants.WorldSize / GameConstants.WorldCells;
            for (int z = 0; z < GameConstants.WorldCells; z += size)
                for (int x = 0; x < GameConstants.WorldCells; x += size)
                {
                    var vertices = new Vector3[(size + 1) * (size + 1)];
                    var triangles = new int[size * size * 6];
                    for (int vz = 0; vz <= size; vz++)
                        for (int vx = 0; vx <= size; vx++)
                            vertices[vz * (size + 1) + vx] = new Vector3(vx * spacing, heights[x + vx, z + vz], vz * spacing);
                    for (int tz = 0; tz < size; tz++)
                        for (int tx = 0; tx < size; tx++)
                        {
                            int a = tz * (size + 1) + tx, b = a + size + 1, t = (tz * size + tx) * 6;
                            triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1;
                            triangles[t + 3] = a + 1; triangles[t + 4] = b; triangles[t + 5] = b + 1;
                        }
                    var mesh = FlatShadedMesh.Create(vertices, triangles); Own(mesh);
                    var chunk = new GameObject("Terrain chunk " + x + "," + z);
                    chunk.transform.SetParent(transform, false);
                    chunk.transform.localPosition = new Vector3(x * spacing - GameConstants.WorldSize / 2, 0,
                        z * spacing - GameConstants.WorldSize / 2);
                    chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                    chunk.AddComponent<MeshRenderer>().sharedMaterial = material;
                    chunk.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
        }

        private void BuildBase(Simulation sim, Material material)
        {
            float height = SampleHeight(0, 0);
            Box(transform, "Base deck", Vector3.up * (height + GameConstants.BaseDeckThickness / 2),
                new Vector3(GameConstants.BaseWidth, GameConstants.BaseDeckThickness, GameConstants.BaseWidth), material);
            Box(transform, "Base beacon", new Vector3(0, height + GameConstants.BaseMarkerHeight / 2, 0),
                new Vector3(GameConstants.CaveWall, GameConstants.BaseMarkerHeight, GameConstants.CaveWall), material, false);
            var zoneOwner = new GameObject("Base shelter");
            zoneOwner.transform.SetParent(transform, false);
            zoneOwner.transform.localPosition = Vector3.up * height;
            var box = zoneOwner.AddComponent<BoxCollider>();
            box.center = Vector3.up * GameConstants.CaveHeight / 2;
            box.size = new Vector3(GameConstants.BaseWidth, GameConstants.CaveHeight, GameConstants.BaseWidth);
            box.isTrigger = true;
            zoneOwner.transform.localPosition += Vector3.up * GameConstants.BaseDeckThickness;
            zoneOwner.AddComponent<ShelterZone>().Initialize(sim.Modifiers, Player.transform);
        }

        public static GameObject Box(Transform parent, string label, Vector3 position, Vector3 scale, Material material, bool collide = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collide) box.GetComponent<BoxCollider>().enabled = false;
            return box;
        }

        public void Own(UnityEngine.Object asset) { owned.Add(asset); }
        private Material MakeMaterial(uint rgb)
        {
            var material = new Material(surfaceShader != null ? surfaceShader : Shader.Find("Universal Render Pipeline/Lit"));
            material.color = ColorFromHex(rgb);
            material.SetFloat("_Smoothness", 0);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_SpecularHighlights", 0);
            material.SetFloat("_EnvironmentReflections", 0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.enableInstancing = true;
            Own(material);
            return material;
        }
        public static Color ColorFromHex(uint rgb)
        {
            return new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        }
        private void BuildLighting()
        {
            var lightOwner = new GameObject("Low sunset sun");
            lightOwner.transform.SetParent(transform, false);
            lightOwner.transform.localRotation = Quaternion.Euler(GameConstants.SunPitch, GameConstants.SunYaw, 0);
            var light = lightOwner.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = ColorFromHex(GameConstants.BaseColor);
            light.intensity = GameConstants.SunIntensity;
            light.shadows = LightShadows.Hard;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ColorFromHex(GameConstants.BaseColor) * GameConstants.AmbientIntensity;
        }
        private void Update()
        {
            if (!drivesClock) return;
            Player.RefreshLocation();
            Mission.AtBase = expedition.AtBase;
            Mission.Sheltered = expedition.AtBase || simulation.Modifiers.Apply(GameConstants.RadiationShieldStat, 0) >= GameConstants.ShelterShield;
            simulation.Tick(Time.deltaTime);
        }
        private void OnDestroy()
        {
            if (drivesClock && ReferenceEquals(GameServices.SimClock, simulation.Clock))
            { GameServices.SimClock = null; GameServices.GameState = null; }
            if (simulation != null && expedition != null)
            {
                simulation.UnregisterSystem(expedition);
                simulation.State.UsingOxygenTank = false;
            }
            for (int i = 0; i < owned.Count; i++)
                if (Application.isPlaying) Destroy(owned[i]); else DestroyImmediate(owned[i]);
        }
    }
}
