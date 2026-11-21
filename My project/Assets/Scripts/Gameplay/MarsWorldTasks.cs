using System.Collections.Generic;
using System.Globalization;
using MarsSurvival.Base;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using MarsSurvival.World;
using TMPro;
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
    /// (two beds of 12) that show what each crop is doing with a timer above it, the seed shop kiosk, and the
    /// food locker, water dispenser and bunk used with E.
    /// </summary>
    public sealed class MarsWorldTasks : MonoBehaviour
    {
        /// <summary>Greenhouse centre (outpost-local).</summary>
        public static readonly Vector3 GreenhousePoint = new Vector3(0f, 1.3f, -12.5f);
        /// <summary>Seed shop kiosk, against the north wall of storage, facing into the room.</summary>
        public static readonly Vector3 ShopPoint = new Vector3(-10.6f, 0f, 3.2f);
        /// <summary>Crop and kiosk models (tools/blender/garden_assets.py), and their shop icons.</summary>
        public const string GardenFolder = "Garden";

        private const float BedTop = 0.83f;
        private MarsOutpost outpost;
        private Transform root;
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly List<PlantView> plants = new List<PlantView>();
        public static MarsWorldTasks Instance { get; private set; }

        private sealed class PlantView
        {
            public int index;
            public Transform soil;
            /// <summary>One model per crop kind (SeedKind.All order), and its "Fruit" part, shown when ripe.</summary>
            public Transform[] crop, fruit;
            public TextMeshPro timer;
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

            // Greenhouse: one plot per cell of the printed planters' grid (2 columns x 6 rows per bed,
            // cells 0.9 x 1.3 m, see garden() in tools/blender/outpost_modules.py); plot 1 by the door.
            for (int bed = 0; bed < 2; bed++)
            {
                float x0 = bed == 0 ? -3.3f : 1.3f;
                for (int slot = 0; slot < MissionSettings.SpotsPerBed; slot++)
                {
                    float x = x0 + 0.55f + (slot % 2) * 0.9f;
                    float z = -9.05f - (slot / 2) * 1.3f;
                    plants.Add(BuildPlant(bed * MissionSettings.SpotsPerBed + slot, new Vector3(x, BedTop, z)));
                }
            }

            // Things used with E.
            // Food and the bunk in storage, drinking water at the extractor in utilities (MarsOutpostBuilder).
            Usable("Food locker", new Vector3(-14.6f, 0.9f, 3.25f), new Vector3(2f, 1.75f, 0.7f), MarsTaskTarget.Kind.Food);
            Usable("Water dispenser", new Vector3(14.4f, 0.8f, 3f), new Vector3(1.4f, 1.4f, 1f), MarsTaskTarget.Kind.Water);
            Usable("Bunk", new Vector3(-8.9f, 0.6f, -3.25f), new Vector3(2f, 0.4f, 0.9f), MarsTaskTarget.Kind.Bunk);
            // The seed shop kiosk in storage.
            GameObject shop = new GameObject("Seed shop");
            shop.transform.SetParent(root, false);
            shop.transform.localPosition = ShopPoint;
            Model("SeedKiosk", shop.transform);
            BoxCollider shopBox = shop.AddComponent<BoxCollider>();
            shopBox.center = new Vector3(0f, 1.1f, 0f);
            shopBox.size = new Vector3(1.9f, 2.2f, 0.9f);
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
            view.soil = Cube("Soil", spot.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.8f, 0.02f, 1.2f), MarsPalette.DeepPurple, false).transform;
            view.crop = new Transform[SeedKind.All.Length];
            view.fruit = new Transform[SeedKind.All.Length];
            for (int k = 0; k < SeedKind.All.Length; k++)
            {
                Transform crop = Model(SeedKind.All[k].name, spot.transform);
                crop.localRotation = Quaternion.Euler(0f, slot * 47f, 0f) * crop.localRotation;   // not all facing the same way
                view.crop[k] = crop;
                view.fruit[k] = crop.Find("Fruit");
                crop.gameObject.SetActive(false);
            }
            GameObject label = new GameObject("Timer");
            label.transform.SetParent(spot.transform, false);
            view.timer = label.AddComponent<TextMeshPro>();
            view.timer.font = MarsTrainer.UI.UiTypeface.Font;
            view.timer.fontSize = 2.4f;
            view.timer.alignment = TextAlignmentOptions.Center;
            view.timer.enableWordWrapping = false;
            view.timer.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
            return view;
        }

        /// <summary>
        /// A Garden model (kiosk or crop) under <paramref name="parent"/>, its materials rebuilt from their
        /// names ("Mat #RRGGBB", "Metal #RRGGBB", "Glow #RRGGBB"). Empty if the model is missing.
        /// </summary>
        private Transform Model(string name, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(GardenFolder + "/" + name);
            if (prefab == null) { Transform none = new GameObject(name).transform; none.SetParent(parent, false); return none; }
            Transform model = Instantiate(prefab, parent, false).transform;
            model.name = name;
            model.localRotation = OutpostModules.ImportFix;   // same Blender to Unity turn as the outpost modules
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = Named(slots[i] == null ? "" : slots[i].name);
                r.sharedMaterials = slots;
            }
            return model;
        }

        private readonly Dictionary<string, Material> named = new Dictionary<string, Material>();

        private Material Named(string imported)
        {
            int hash = imported.IndexOf('#');
            string key = hash < 0 || imported.Length < hash + 7 ? "Mat #808080" : imported.Substring(0, hash + 7);
            Material m;
            if (named.TryGetValue(key, out m)) return m;
            Color color;
            if (!ColorUtility.TryParseHtmlString(key.Substring(key.IndexOf('#')), out color)) color = Color.grey;
            m = MarsPalette.CreateMatteMaterial("Garden " + key, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", key.StartsWith("Metal") ? 0.6f : 0.3f);
            if (key.StartsWith("Metal") && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.8f);
            if (key.StartsWith("Glow"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 1.8f);
            }
            named[key] = m;
            return m;
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

        private static readonly Color TimerGrow = new Color(0.25f, 0.88f, 1f), TimerDry = new Color(0.7f, 0.8f, 0.9f), TimerReady = new Color(1f, 0.71f, 0.28f);

        private void UpdatePlants(MissionSimulation sim)
        {
            MissionState state = sim.State;
            Camera cam = Camera.main;
            foreach (PlantView view in plants)
            {
                PlotState plot = sim.Plot(view.index);
                bool unlocked = sim.Unlocked(view.index);
                int kind = plot == null ? -1 : System.Array.FindIndex(SeedKind.All, k => k.id == plot.seed);
                bool show = unlocked && kind >= 0;
                // Locked plots are covered with a pale lid; dry soil is light, moist soil dark.
                bool wet = show && plot.wetUntil > state.elapsedSeconds;
                view.soil.localScale = new Vector3(0.8f, unlocked ? 0.02f : 0.03f, 1.2f);   // fills its grid cell
                view.soil.GetComponent<Renderer>().sharedMaterial = Material(!unlocked ? MarsPalette.ClaySand : wet ? MarsPalette.DeepPurple : MarsPalette.Mauve);
                for (int k = 0; k < view.crop.Length; k++)
                {
                    bool on = show && k == kind;
                    if (view.crop[k].gameObject.activeSelf != on) view.crop[k].gameObject.SetActive(on);
                }
                view.timer.gameObject.SetActive(show);
                if (!show) continue;
                float growth = (float)plot.growth;
                view.crop[kind].localScale = Vector3.one * (0.2f + 0.8f * growth);
                if (view.fruit[kind] != null) view.fruit[kind].gameObject.SetActive(plot.Ripe);

                // Timer: time left while moist, a prompt while dry, READY when ripe.
                double left = plot.SecondsLeft;
                view.timer.text = plot.Ripe ? "READY" : wet ? ((int)left / 60) + ":" + ((int)left % 60).ToString("00", CultureInfo.InvariantCulture) : "WATER";
                view.timer.color = plot.Ripe ? TimerReady : wet ? TimerGrow : TimerDry;
                view.timer.transform.localPosition = new Vector3(0f, 0.35f + 0.65f * Mathf.Max(0.3f, growth) * (kind == 1 ? 1.4f : 0.6f), 0f);
                if (cam != null) view.timer.transform.rotation = Quaternion.LookRotation(view.timer.transform.position - cam.transform.position, Vector3.up);
            }
        }
    }
}
