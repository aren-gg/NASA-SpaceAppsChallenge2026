using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>A thing in the outpost that a tool works on, or that E uses.</summary>
    public sealed class MarsTaskTarget : MonoBehaviour
    {
        public enum Kind { Plant, Food, Water, Bunk, Shop }

        public Kind kind;
        /// <summary>Plot index for plants (bed * 12 + spot).</summary>
        public int index;

        public static readonly List<MarsTaskTarget> Usables = new List<MarsTaskTarget>();

        /// <summary>True for things used with E rather than a tool.</summary>
        public bool UsedWithE
        {
            get { return true; }
        }

        private void OnEnable() { if (UsedWithE) Usables.Add(this); }
        private void OnDisable() { Usables.Remove(this); }
    }

    /// <summary>
    /// The physical side of the game, built on top of the outpost: the 24 greenhouse plots
    /// (two beds of 12) that show what each crop is doing, the seed shop terminal, and the
    /// galley food locker, water dispenser and bunks used with E.
    /// </summary>
    public sealed class MarsWorldTasks : MonoBehaviour
    {
        /// <summary>Galley, bunk and greenhouse (outpost-local).</summary>
        public static readonly Vector3 GalleyPoint = new Vector3(-2.6f, 1.3f, 2.6f);
        public static readonly Vector3 BunkPoint = new Vector3(-17f, 0.8f, -2.4f);
        public static readonly Vector3 GreenhousePoint = new Vector3(0f, 1.3f, -12.5f);
        /// <summary>Seed shop terminal, beside the aisle at the greenhouse entrance.</summary>
        public static readonly Vector3 ShopPoint = new Vector3(-2.5f, 0f, -7.85f);

        private const float BedTop = 0.83f;
        private MarsOutpost outpost;
        private Transform root;
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly List<PlantView> plants = new List<PlantView>();
        public static MarsWorldTasks Instance { get; private set; }

        private sealed class PlantView
        {
            public int index;
            public Transform body, soil, fruit;
            public Renderer[] leaves;
        }

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; foreach (Material m in materials.Values) Destroy(m); }

        /// <summary>World position of an outpost-local point (for waypoints).</summary>
        public static Vector3 World(Vector3 local)
        {
            MarsOutpost o = Instance == null ? null : Instance.outpost;
            return o == null ? local : o.transform.TransformPoint(local);
        }

        private void Update()
        {
            var mission = MarsMissionController.Instance;
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (outpost == null || !outpost.IsPlaced) return;
            if (root == null) Build();
            MissionState state = mission == null || mission.Simulation == null ? null : mission.Simulation.State;
            if (state == null) return;
            UpdatePlants(mission.Simulation);
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            root = new GameObject("Daily task objects").transform;
            root.SetParent(outpost.transform, false);

            // Greenhouse: 2 columns x 6 rows of plant spots, centred in each raised bed.
            for (int bed = 0; bed < 2; bed++)
            {
                float x0 = bed == 0 ? -3.3f : 1.3f;
                for (int slot = 0; slot < MissionSettings.SpotsPerBed; slot++)
                {
                    float x = x0 + 0.55f + (slot % 2) * 0.9f;
                    float z = -15.55f + (slot / 2) * 1.3f;
                    plants.Add(BuildPlant(bed * MissionSettings.SpotsPerBed + slot, new Vector3(x, BedTop, z)));
                }
            }

            // Things used with E.
            Usable("Galley food locker", new Vector3(-2.7f, 1.34f, 3.12f), new Vector3(1.4f, 0.82f, 0.55f), MarsTaskTarget.Kind.Food);
            Usable("Galley water dispenser", new Vector3(-3.05f, 1.2f, 2.35f), new Vector3(0.45f, 0.6f, 0.45f), MarsTaskTarget.Kind.Water);
            Usable("Bunk", new Vector3(-18.35f, 0.6f, -2.5f), new Vector3(2f, 0.4f, 0.9f), MarsTaskTarget.Kind.Bunk);
            Usable("Bunk", new Vector3(-15.6f, 0.6f, -2.5f), new Vector3(2f, 0.4f, 0.9f), MarsTaskTarget.Kind.Bunk);
            // The seed shop terminal at the greenhouse door.
            GameObject shop = new GameObject("Seed shop");
            shop.transform.SetParent(root, false);
            shop.transform.localPosition = ShopPoint;
            Cube("Cabinet", shop.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.7f, 1.1f, 0.45f), MarsPalette.DeepPurple, false);
            Cube("Screen", shop.transform, new Vector3(0f, 1.2f, 0.05f), new Vector3(0.6f, 0.4f, 0.06f), MarsPalette.MutedOlive, false);
            BoxCollider shopBox = shop.AddComponent<BoxCollider>();
            shopBox.center = new Vector3(0f, 0.75f, 0f);
            shopBox.size = new Vector3(0.8f, 1.5f, 0.6f);
            shop.AddComponent<MarsTaskTarget>().kind = MarsTaskTarget.Kind.Shop;
        }

        private PlantView BuildPlant(int index, Vector3 at)
        {
            int slot = index % MissionSettings.SpotsPerBed;
            GameObject spot = new GameObject("Plot " + (index + 1));
            spot.transform.SetParent(root, false);
            spot.transform.localPosition = at;
            BoxCollider box = spot.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.2f, 0f);
            box.size = new Vector3(0.7f, 0.4f, 0.9f);
            MarsTaskTarget target = spot.AddComponent<MarsTaskTarget>();
            target.kind = MarsTaskTarget.Kind.Plant;
            target.index = index;

            var view = new PlantView { index = index };
            view.soil = Cube("Soil", spot.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.38f, 0.03f, 0.38f), MarsPalette.DeepPurple, false).transform;
            GameObject body = new GameObject("Plant");
            body.transform.SetParent(spot.transform, false);
            view.body = body.transform;
            // Three crossed leaf blades read as a low-poly bush rather than a box.
            view.leaves = new Renderer[3];
            for (int k = 0; k < 3; k++)
            {
                GameObject blade = Cube("Leaf " + k, body.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 0.2f), MarsPalette.MutedOlive, false);
                blade.transform.localRotation = Quaternion.Euler(0f, 60f * k + slot * 17f, 0f);
                view.leaves[k] = blade.GetComponent<Renderer>();
            }
            // Fruit hangs off the spot itself (not the scaled plant) so it keeps its size.
            GameObject fruit = new GameObject("Fruit");
            fruit.transform.SetParent(spot.transform, false);
            for (int i = 0; i < 3; i++)
                Cube("Fruit " + i, fruit.transform, Vector3.zero, new Vector3(0.13f, 0.13f, 0.13f), MarsPalette.WarmOrange, false);
            view.fruit = fruit.transform;
            return view;
        }

        private void Usable(string name, Vector3 centre, Vector3 size, MarsTaskTarget.Kind kind)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = centre;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true; // aimed at with E, never pushes the astronaut
            go.AddComponent<MarsTaskTarget>().kind = kind;
        }

        private static void Target(GameObject go, MarsTaskTarget.Kind kind)
        {
            go.AddComponent<MarsTaskTarget>().kind = kind;
        }

        private GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Color color, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            return go;
        }

        private Material Material(Color color)
        {
            Material m;
            if (!materials.TryGetValue(color, out m))
            {
                m = MarsPalette.CreateMatteMaterial("Task " + ColorUtility.ToHtmlStringRGB(color), color);
                materials[color] = m;
            }
            return m;
        }

        // ------------------------------------------------------------------ per frame

        // Leaf colour and size per seed kind (SeedKind.All order): lettuce, potato, tomato.
        private static readonly Color[] LeafColors = { new Color(0.47f, 0.66f, 0.35f), new Color(0.36f, 0.48f, 0.30f), new Color(0.30f, 0.52f, 0.30f) };
        private static readonly Color[] FruitColors = { new Color(0.55f, 0.75f, 0.40f), new Color(0.62f, 0.48f, 0.32f), new Color(0.80f, 0.22f, 0.16f) };
        private static readonly float[] Heights = { 0.30f, 0.45f, 0.80f };

        private void UpdatePlants(MissionSimulation sim)
        {
            MissionState state = sim.State;
            foreach (PlantView view in plants)
            {
                PlotState plot = sim.Plot(view.index);
                bool unlocked = sim.Unlocked(view.index);
                int kind = plot == null ? -1 : System.Array.FindIndex(SeedKind.All, k => k.id == plot.seed);
                bool show = unlocked && kind >= 0;
                view.body.gameObject.SetActive(show);
                view.fruit.gameObject.SetActive(show && plot.Ripe);
                // Locked plots are covered with a pale lid; dry soil is light, moist soil dark.
                bool wet = show && plot.wetUntil > state.elapsedSeconds;
                view.soil.localScale = unlocked ? new Vector3(0.38f, 0.03f, 0.38f) : new Vector3(0.6f, 0.02f, 0.8f);
                view.soil.GetComponent<Renderer>().sharedMaterial = Material(!unlocked ? MarsPalette.ClaySand : wet ? MarsPalette.DeepPurple : MarsPalette.Mauve);
                if (!show) continue;
                float growth = 0.05f + 0.95f * (float)plot.growth;
                float height = 0.06f + growth * Heights[kind];
                float width = 0.12f + growth * 0.45f;
                bool dry = !wet && !plot.Ripe;
                view.body.localScale = new Vector3(width, height * (dry ? 0.8f : 1f), width);
                Color leaf = dry ? Color.Lerp(LeafColors[kind], MarsPalette.ClaySand, 0.45f) : LeafColors[kind];
                Material leafMaterial = Material(Quantize(leaf));
                foreach (Renderer r in view.leaves) r.sharedMaterial = leafMaterial;
                if (!plot.Ripe) continue;
                Material fruitMaterial = Material(FruitColors[kind]);
                for (int i = 0; i < view.fruit.childCount; i++)
                {
                    float a = i * 2.1f + view.index;
                    Transform f = view.fruit.GetChild(i);
                    f.localPosition = new Vector3(Mathf.Cos(a) * width * 0.42f, height * (0.45f + 0.2f * i), Mathf.Sin(a) * width * 0.42f);
                    f.GetComponent<Renderer>().sharedMaterial = fruitMaterial;
                }
            }
        }

        private static Color Quantize(Color c)
        {
            return new Color(Mathf.Round(c.r * 32f) / 32f, Mathf.Round(c.g * 32f) / 32f, Mathf.Round(c.b * 32f) / 32f, 1f);
        }
    }
}
