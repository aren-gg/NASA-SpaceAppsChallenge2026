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
        public enum Kind { Plant, Food, Water, Bunk, Shop, Shed, Garage, Seat }

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
        /// <summary>Where the astronaut lies in the crew bunk: feet end of the mattress, outpost-local; the head is toward -x (the pillow).</summary>
        public static readonly Vector3 BedFeet = new Vector3(-13.95f, 0.62f, 3.33f);
        /// <summary>The console chair's seat (outpost-local, floor height); you sit facing the screens (-x).</summary>
        public static readonly Vector3 SeatPoint = new Vector3(-14.7f, 0f, 0.15f);
        /// <summary>The seed kiosk and water pump are drawn this much larger than modelled.</summary>
        private const float StationScale = 1.3f;
        /// <summary>Water pump against the resources wing's south wall (the seed kiosk faces it).</summary>
        public static readonly Vector3 PumpPoint = new Vector3(-12.5f, 0f, -3.2f);
        /// <summary>The hangar's roll-up door, in the east wing's outer end wall (5 m wide, 3.6 m tall).</summary>
        public static readonly Vector3 GarageDoorPoint = new Vector3(21.19f, 0f, 0f);
        private const float GarageDoorHeight = 3.6f, GarageDoorWidth = 5f;
        private Transform garageDoor;
        private MarsSurvival.Vehicles.MarsRover rover;
        private const float SenseRange = 16f;   // metres from the door: about a second's warning at top speed
        private float garageOpen;
        /// <summary>True while the garage door is open (or opening).</summary>
        public bool GarageOpen { get; private set; }

        /// <summary>E at the garage door: rolls it up or down.</summary>
        public void ToggleGarage() { GarageOpen = !GarageOpen; }
        /// <summary>Where a new mission starts: in the hub in front of the oxygen tree, facing it.</summary>
        public static readonly Vector3 SpawnPoint = new Vector3(0f, 0.05f, 3.6f);
        /// <summary>Tool shed against the greenhouse's back wall.</summary>
        public static readonly Vector3 ShedPoint = new Vector3(0f, 0f, -17.75f);

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
            /// <summary>One model per crop kind (SeedKind.All order), and its Bud, Unripe and Ripe stages.</summary>
            public Transform[] crop;
            public Transform[][] stages;
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
            DriftMotes();
            MissionState state = mission == null || mission.Simulation == null ? null : mission.Simulation.State;
            if (state == null) return;
            UpdatePlants(mission.Simulation);
            // Roll the garage door up into its lintel, or down to the floor (about 1.3 s). It also opens by
            // itself while someone drives the rover within SenseRange of it, and closes once they are gone.
            if (rover == null) rover = FindObjectOfType<MarsSurvival.Vehicles.MarsRover>();
            bool roverNear = rover != null && rover.IsDriven
                && (outpost.transform.InverseTransformPoint(rover.transform.position) - GarageDoorPoint).sqrMagnitude < SenseRange * SenseRange;
            garageOpen = Mathf.MoveTowards(garageOpen, GarageOpen || roverNear ? 1f : 0f, Time.deltaTime * 0.75f);
            if (garageDoor != null)
            {
                float h = Mathf.Max(0.02f, GarageDoorHeight * (1f - garageOpen));
                garageDoor.localScale = new Vector3(0.12f, h, GarageDoorWidth);
                garageDoor.localPosition = GarageDoorPoint + new Vector3(0f, GarageDoorHeight - h / 2f, 0f);
            }
        }

        private struct Mote { public Transform t; public Vector3 baked; public float angle, speed, radius, height, phase; }
        private readonly List<Mote> motes = new List<Mote>();

        /// <summary>Each orb circles the tree at its own speed, swaying in and out and bobbing up and down.</summary>
        private void DriftMotes()
        {
            float time = Time.time;
            foreach (Mote m in motes)
            {
                float a = (m.angle + m.speed * time) * Mathf.Deg2Rad;
                float r = m.radius + 0.6f * Mathf.Sin(time * 0.12f + m.phase);
                float y = m.height + 0.5f * Mathf.Sin(time * 0.18f + m.phase * 2f);
                m.t.position = root.TransformPoint(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)) - m.baked;
            }
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
            // The garage's roll-up door: E opens and closes it; open, the rover can drive out.
            garageDoor = Cube("Garage door", root, GarageDoorPoint + new Vector3(0f, GarageDoorHeight / 2f, 0f), new Vector3(0.12f, GarageDoorHeight, GarageDoorWidth), new Color32(0x3A, 0x3D, 0x44, 255), true).transform;
            for (int i = 1; i < 9; i++)   // slat seams; they squeeze together as the door rolls up
                Cube("Garage door slat", garageDoor, new Vector3(0f, -0.5f + i / 9f, 0f), new Vector3(1.15f, 0.012f, 1f), new Color32(0x22, 0x24, 0x29, 255), false);
            Cube("Garage door stripe", garageDoor, new Vector3(0f, -0.45f, 0f), new Vector3(1.2f, 0.05f, 1f), new Color32(0xE2, 0xA2, 0x1B, 255), false);
            Usable("Garage door", GarageDoorPoint + new Vector3(0f, 1.5f, 0f), new Vector3(1.6f, 3f, GarageDoorWidth + 0.2f), MarsTaskTarget.Kind.Garage);

            // The water pump against the resources wing's south wall, facing into the room; E fills you up.
            GameObject pump = new GameObject("Water pump");
            pump.transform.SetParent(root, false);
            pump.transform.localPosition = PumpPoint;
            Transform pumpModel = Model("WaterPump", pump.transform);
            pumpModel.localRotation = Quaternion.identity;   // faces +z, into the room
            pumpModel.localScale = Vector3.one * StationScale;
            Usable("Water pump", PumpPoint + new Vector3(0f, 1.2f, 0.4f), new Vector3(2.9f, 2.4f, 1.4f), MarsTaskTarget.Kind.Water);
            // The rest of the resources wing: reserve tanks, supply rack, crates, console, floor marks and the sleep pod.
            GameObject decor = new GameObject("Resources decor");
            decor.transform.SetParent(root, false);
            decor.transform.localPosition = new Vector3(-11.75f, 0f, 0f);
            Model("ResourcesDecor", decor.transform);

            // The oxygen spirit tree in the middle of the hub; its round planter stops you.
            GameObject tree = new GameObject("Oxygen tree");
            tree.transform.SetParent(root, false);
            Transform treeModel = Model("OxygenTree", tree.transform);
            // Its light motes leave the crown and drift round the dome as floating orbs.
            var rnd = new System.Random(7);
            foreach (Transform part in treeModel)
            {
                if (part.name != "Light mote" || !part.TryGetComponent(out Renderer orb)) continue;
                motes.Add(new Mote
                {
                    t = part, baked = orb.bounds.center - part.position,
                    angle = (float)rnd.NextDouble() * 360f, speed = 2f + (float)rnd.NextDouble() * 3f,   // degrees a second: a slow drift
                    radius = 2f + (float)rnd.NextDouble() * 1.6f, height = 1.2f + (float)rnd.NextDouble() * 5f,   // inside the dome as it narrows
                    phase = (float)rnd.NextDouble() * 6.28f,
                });
            }
            CapsuleCollider planter = tree.AddComponent<CapsuleCollider>();
            planter.radius = 1.95f; planter.height = 4f; planter.center = new Vector3(0f, 0.3f, 0f);
            Usable("Console chair", SeatPoint + new Vector3(0f, 0.6f, 0f), new Vector3(0.6f, 0.5f, 0.6f), MarsTaskTarget.Kind.Seat);
            // The crew bunk: the E target sits at its open front edge, where you stand and look, not inside the pod.
            Usable("Bunk", new Vector3(-14.8f, 0.7f, 2.7f), new Vector3(2.3f, 0.8f, 0.4f), MarsTaskTarget.Kind.Bunk);
            // The seed shop kiosk in storage.
            GameObject shop = new GameObject("Seed shop");
            shop.transform.SetParent(root, false);
            shop.transform.localPosition = ShopPoint;
            Model("SeedKiosk", shop.transform).localScale = Vector3.one * StationScale;
            BoxCollider shopBox = shop.AddComponent<BoxCollider>();
            shopBox.center = new Vector3(0f, 1.4f, 0f);
            shopBox.size = new Vector3(2.5f, 2.8f, 1.2f);
            shop.AddComponent<MarsTaskTarget>().kind = MarsTaskTarget.Kind.Shop;

            // The tool shed at the greenhouse's far end, facing the aisle; E on it returns your tools.
            GameObject shed = new GameObject("Tool shed");
            shed.transform.SetParent(root, false);
            shed.transform.localPosition = ShedPoint;
            Model("ToolShed", shed.transform).localRotation = Quaternion.identity;   // faces +z, toward the door
            // One target per bay (index = the tool's place in MarsTool.Order): E there returns just that tool.
            // Seen from the aisle the bays are sprayer (+x), shovel (middle), lamp (-x).
            float[] bayX = { 0f, 1f, -1f };   // shovel, sprayer, lamp
            for (int i = 0; i < bayX.Length; i++)
                Usable("Tool shed bay", ShedPoint + new Vector3(bayX[i], 1.3f, 0.1f), new Vector3(0.95f, 1.6f, 0.4f), MarsTaskTarget.Kind.Shed).index = i;
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
            view.stages = new Transform[SeedKind.All.Length][];
            for (int k = 0; k < SeedKind.All.Length; k++)
            {
                Transform crop = Model(SeedKind.All[k].name, spot.transform);
                crop.localRotation = Quaternion.Euler(0f, slot * 47f, 0f) * crop.localRotation;   // not all facing the same way
                view.crop[k] = crop;
                view.stages[k] = new[] { crop.Find("Bud"), crop.Find("Unripe"), crop.Find("Ripe") };
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
        public static Transform Model(string name, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(GardenFolder + "/" + name);
            if (prefab == null) { Transform none = new GameObject(name).transform; none.SetParent(parent, false); return none; }
            Transform model = Instantiate(prefab, parent, false).transform;
            model.name = name;
            model.localRotation = OutpostModules.ImportFix;   // same Blender to Unity turn as the outpost modules
            // Blender makes names unique across its scene ("Bud.001"); drop the suffix so parts are found by name.
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int dot = part.name.LastIndexOf('.');
                if (dot > 0 && int.TryParse(part.name.Substring(dot + 1), out _)) part.name = part.name.Substring(0, dot);
            }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                slots[i] = slots[i] != null && !slots[i].name.Contains("#") ? Kept(slots[i]) : Named(slots[i] == null ? "" : slots[i].name);
                r.sharedMaterials = slots;
            }
            return model;
        }

        private static readonly Dictionary<string, Material> named = new Dictionary<string, Material>();   // shared by every model, for the whole run

        /// <summary>
        /// A textured material the model brought (the spirit tree's bark and leaves) kept as imported;
        /// the leaves are cut out by their alpha, drawn on both sides and glow teal.
        /// </summary>
        private static Material Kept(Material imported)
        {
            Material m;
            if (!imported.name.StartsWith("Spirit leaves"))
            {
                // shortcut: the bark textures don't survive the FBX export (glTF-packed images), so the bark is
                // a plain dark brown; embed them from tools/blender/assets if a closer look needs them.
                bool bare = !imported.HasProperty("_BaseMap") || imported.GetTexture("_BaseMap") == null;
                if (!bare) return imported;
                if (!named.TryGetValue("Spirit bark", out m)) { m = MarsPalette.CreateMatteMaterial("Spirit bark", new Color32(0x4A, 0x36, 0x2A, 255)); named["Spirit bark"] = m; }
                return m;
            }
            if (named.TryGetValue("Spirit leaves", out m)) return m;
            m = new Material(imported) { name = "Spirit leaves" };
            m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", 0.4f);
            m.SetFloat("_Cull", 0f); m.renderQueue = 2450;
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
            m.SetColor("_EmissionColor", new Color(0.25f, 1f, 0.8f) * 0.45f);
            named["Spirit leaves"] = m;
            return m;
        }

        private static Material Named(string imported)
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
                m.SetColor("_EmissionColor", color * 1.1f);
            }
            named[key] = m;
            return m;
        }

        private MarsTaskTarget Usable(string name, Vector3 centre, Vector3 size, MarsTaskTarget.Kind kind)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = centre;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true; // aimed at with E, never pushes the astronaut
            MarsTaskTarget target = go.AddComponent<MarsTaskTarget>();
            target.kind = kind;
            return target;
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

        /// <summary>
        /// Growth at which each crop leaves the bud stage (SeedKind.All order): lettuce buds for the first
        /// 35%, tomato 25% (a long green-fruit phase), watermelon 40% (melons set late). Ripe at 100%.
        /// </summary>
        private static readonly float[] BudEnds = { 0.35f, 0.25f, 0.4f };

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
                // Three stages: bud, unripe, ripe; each grows a little within its stage.
                float growth = (float)plot.growth, budEnd = BudEnds[kind];
                int stage = plot.Ripe ? 2 : growth < budEnd ? 0 : 1;
                float within = stage == 0 ? growth / budEnd : stage == 1 ? (growth - budEnd) / (1f - budEnd) : 1f;
                view.crop[kind].localScale = Vector3.one * (stage == 0 ? 0.6f + 0.4f * within : stage == 1 ? 0.75f + 0.25f * within : 1f);
                for (int st = 0; st < 3; st++)
                    if (view.stages[kind][st] != null && view.stages[kind][st].gameObject.activeSelf != (st == stage)) view.stages[kind][st].gameObject.SetActive(st == stage);

                // Timer: time left while moist, a prompt while dry, READY when ripe.
                double left = plot.SecondsLeft;
                view.timer.text = plot.Ripe ? "READY" : wet ? ((int)left / 60) + ":" + ((int)left % 60).ToString("00", CultureInfo.InvariantCulture) : "WATER";
                view.timer.color = plot.Ripe ? TimerReady : wet ? TimerGrow : TimerDry;
                view.timer.transform.localPosition = new Vector3(0f, stage == 0 ? 0.4f : kind == 1 ? 1.15f : 0.6f, 0f);
                if (cam != null) view.timer.transform.rotation = Quaternion.LookRotation(view.timer.transform.position - cam.transform.position, Vector3.up);
            }
        }
    }
}
