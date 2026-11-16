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
        public enum Kind { Plant, Panel, Fault, SurveyRock, Food, Water, Bunk, MedicalBed, Bike }

        public Kind kind;
        public int bed, slot, index;
        public string siteId = "";

        public static readonly List<MarsTaskTarget> Usables = new List<MarsTaskTarget>();

        /// <summary>True for things used with E rather than a tool.</summary>
        public bool UsedWithE
        {
            get { return kind == Kind.Food || kind == Kind.Water || kind == Kind.Bunk || kind == Kind.MedicalBed || kind == Kind.Bike || kind == Kind.Plant; }
        }

        private void OnEnable() { if (UsedWithE) Usables.Add(this); }
        private void OnDisable() { Usables.Remove(this); }
    }

    /// <summary>
    /// The physical side of the daily tasks, built on top of the outpost: 12 plant spots in each
    /// greenhouse bed that show what the crop is really doing, dust on each array panel, the
    /// sparking fault box, glowing rocks to survey at science sites, and the galley food locker,
    /// water dispenser, bunks, medical bed and exercise bike used with E.
    /// </summary>
    public sealed class MarsWorldTasks : MonoBehaviour
    {
        /// <summary>Ceiling junction boxes where a fault can appear (outpost-local), with the room name.</summary>
        public static readonly Vector3[] FaultSpots =
        {
            new Vector3(-8.3f, 2.5f, 0.9f), new Vector3(-15.8f, 2.5f, -0.9f), new Vector3(8.3f, 2.5f, 0.9f),
            new Vector3(17.5f, 2.5f, -0.9f), new Vector3(0.9f, 2.5f, 10.4f), new Vector3(0f, 2.45f, -12.5f),
            new Vector3(-24f, 2.3f, 0.9f),
        };
        public static readonly string[] FaultRooms = { "medical bay", "crew quarters", "energy room", "life support", "lab", "greenhouse", "storm shelter" };

        /// <summary>Galley food locker, galley water, bunk, medical bed (outpost-local).</summary>
        public static readonly Vector3 GalleyPoint = new Vector3(-2.6f, 1.3f, 2.6f);
        public static readonly Vector3 BunkPoint = new Vector3(-17f, 0.8f, -2.4f);
        public static readonly Vector3 GreenhousePoint = new Vector3(0f, 1.3f, -12.5f);
        public static readonly Vector3 LabPoint = new Vector3(0f, 1.3f, 10f);
        public static readonly Vector3 ArrayPoint = new Vector3(12.1f, 1.2f, 9.6f);
        public static readonly Vector3 MedicalPoint = new Vector3(-9.6f, 1f, -1.9f);

        private const float BedTop = 0.83f;
        private MarsOutpost outpost;
        private Transform root;
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly List<PlantView> plants = new List<PlantView>();
        private readonly List<Transform> dust = new List<Transform>();
        private GameObject fault;
        private float nextSpark;
        private readonly Dictionary<string, GameObject[]> rocks = new Dictionary<string, GameObject[]>();
        /// <summary>Rocks hit so far at each site (bit per rock); the survey completes at three.</summary>
        public readonly Dictionary<string, int> RockHits = new Dictionary<string, int>();

        public static MarsWorldTasks Instance { get; private set; }

        private sealed class PlantView
        {
            public int bed, slot;
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
            UpdatePlants(state);
            UpdateDust(state);
            UpdateFault(state);
            UpdateRocks(state, mission);
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
                for (int slot = 0; slot < CropState.Slots; slot++)
                {
                    float x = x0 + 0.55f + (slot % 2) * 0.9f;
                    float z = -15.55f + (slot / 2) * 1.3f;
                    plants.Add(BuildPlant(bed, slot, new Vector3(x, BedTop, z)));
                }
            }

            // Dust layer on each array panel (the builder names the tilted tops "Panel").
            var panels = new List<Transform>();
            foreach (Transform t in outpost.GetComponentsInChildren<Transform>(true))
                if (t.name == "Panel" && t.GetComponent<BoxCollider>() != null) panels.Add(t);
            panels.Sort((a, b) =>
            {
                Vector3 pa = outpost.transform.InverseTransformPoint(a.position), pb = outpost.transform.InverseTransformPoint(b.position);
                int byRow = Mathf.RoundToInt(pa.z).CompareTo(Mathf.RoundToInt(pb.z));
                return byRow != 0 ? byRow : pa.x.CompareTo(pb.x);
            });
            for (int i = 0; i < panels.Count && i < MissionSimulation.PanelCount; i++)
            {
                MarsTaskTarget target = panels[i].gameObject.AddComponent<MarsTaskTarget>();
                target.kind = MarsTaskTarget.Kind.Panel;
                target.index = i;
                GameObject layer = Cube("Dust", panels[i], new Vector3(0f, 0.075f, 0f), new Vector3(3f, 0.02f, 1.8f), MarsPalette.ClaySand, false);
                dust.Add(layer.transform);
            }

            // The fault: a junction box with a glowing face, moved to today's spot.
            fault = Cube("Sparking fault", root, Vector3.zero, new Vector3(0.5f, 0.3f, 0.5f), MarsPalette.DeepPurple, true);
            Cube("Glow", fault.transform, new Vector3(0f, -0.55f, 0f), new Vector3(0.7f, 0.15f, 0.7f), MarsPalette.WarmOrange, false);
            Target(fault, MarsTaskTarget.Kind.Fault);
            fault.SetActive(false);

            // Things used with E.
            Usable("Galley food locker", new Vector3(-2.7f, 1.34f, 3.12f), new Vector3(1.4f, 0.82f, 0.55f), MarsTaskTarget.Kind.Food);
            Usable("Galley water dispenser", new Vector3(-3.05f, 1.2f, 2.35f), new Vector3(0.45f, 0.6f, 0.45f), MarsTaskTarget.Kind.Water);
            Usable("Bunk", new Vector3(-18.35f, 0.6f, -2.5f), new Vector3(2f, 0.4f, 0.9f), MarsTaskTarget.Kind.Bunk);
            Usable("Bunk", new Vector3(-15.6f, 0.6f, -2.5f), new Vector3(2f, 0.4f, 0.9f), MarsTaskTarget.Kind.Bunk);
            Usable("Treatment bed", new Vector3(-9.6f, 0.75f, -1.98f), new Vector3(2f, 0.3f, 0.85f), MarsTaskTarget.Kind.MedicalBed);
            // Exercise bike in the crew quarters.
            GameObject bike = new GameObject("Exercise bike");
            bike.transform.SetParent(root, false);
            bike.transform.localPosition = new Vector3(-18.8f, 0f, 1.35f);
            Cube("Frame", bike.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.12f, 0.3f), MarsPalette.DeepPurple, false);
            Cube("Post", bike.transform, new Vector3(-0.25f, 0.55f, 0f), new Vector3(0.08f, 0.6f, 0.08f), MarsPalette.Mauve, false);
            Cube("Seat", bike.transform, new Vector3(-0.25f, 0.88f, 0f), new Vector3(0.3f, 0.06f, 0.22f), MarsPalette.Terracotta, false);
            Cube("Bars", bike.transform, new Vector3(0.32f, 0.85f, 0f), new Vector3(0.08f, 0.7f, 0.45f), MarsPalette.Mauve, false);
            Cube("Wheel", bike.transform, new Vector3(0.2f, 0.32f, 0f), new Vector3(0.45f, 0.45f, 0.08f), MarsPalette.WarmOrange, false);
            BoxCollider bikeBox = bike.AddComponent<BoxCollider>();
            bikeBox.center = new Vector3(0f, 0.5f, 0f);
            bikeBox.size = new Vector3(1f, 1f, 0.5f);
            bike.AddComponent<MarsTaskTarget>().kind = MarsTaskTarget.Kind.Bike;
        }

        private PlantView BuildPlant(int bed, int slot, Vector3 at)
        {
            GameObject spot = new GameObject("Plant " + (bed + 1) + "-" + (slot + 1));
            spot.transform.SetParent(root, false);
            spot.transform.localPosition = at;
            BoxCollider box = spot.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.2f, 0f);
            box.size = new Vector3(0.7f, 0.4f, 0.9f);
            MarsTaskTarget target = spot.AddComponent<MarsTaskTarget>();
            target.kind = MarsTaskTarget.Kind.Plant;
            target.bed = bed;
            target.slot = slot;

            var view = new PlantView { bed = bed, slot = slot };
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

        private void UpdatePlants(MissionState state)
        {
            foreach (PlantView view in plants)
            {
                CropState crop = state.crops.Find(c => c.bed == view.bed);
                if (crop == null) continue;
                int bit = 1 << view.slot;
                bool sown = (crop.sownMask & bit) != 0 || crop.planted;
                bool picked = (crop.pickedMask & bit) != 0;
                bool show = sown && !picked;
                view.body.gameObject.SetActive(show);
                view.fruit.gameObject.SetActive(show && crop.ready);
                view.soil.localScale = new Vector3(0.38f, show ? 0.03f : 0.05f, 0.38f);
                if (!show) continue;
                float growth = crop.planted ? (float)crop.growth : 0.02f;
                bool staple = crop.crop == "staple";
                float height = 0.06f + growth * (staple ? 0.75f : 0.5f);
                float width = 0.12f + growth * (staple ? 0.38f : 0.55f);
                bool dry = crop.planted && !crop.ready && crop.moisture <= 0.15;
                float droop = dry ? 0.7f : 1f;
                view.body.localScale = new Vector3(width, height * droop, width);
                Color leaf = crop.ready ? MarsPalette.MutedOlive : dry ? Color.Lerp(MarsPalette.MutedOlive, MarsPalette.ClaySand, 0.65f)
                    : Color.Lerp(MarsPalette.MutedOlive, new Color(0.45f, 0.62f, 0.38f), 0.5f);
                if (crop.stress > 0.4f) leaf = Color.Lerp(leaf, MarsPalette.Terracotta, 0.4f);
                Material leafMaterial = Material(Quantize(leaf));
                foreach (Renderer r in view.leaves) r.sharedMaterial = leafMaterial;
                view.fruit.gameObject.SetActive(crop.ready);
                if (crop.ready)
                    for (int i = 0; i < view.fruit.childCount; i++)
                    {
                        float a = i * 2.1f + view.slot;
                        view.fruit.GetChild(i).localPosition = new Vector3(Mathf.Cos(a) * width * 0.42f, height * (0.45f + 0.2f * i), Mathf.Sin(a) * width * 0.42f);
                    }
            }
        }

        private static Color Quantize(Color c)
        {
            return new Color(Mathf.Round(c.r * 32f) / 32f, Mathf.Round(c.g * 32f) / 32f, Mathf.Round(c.b * 32f) / 32f, 1f);
        }

        private void UpdateDust(MissionState state)
        {
            if (state.panelDustEach == null) return;
            for (int i = 0; i < dust.Count && i < state.panelDustEach.Length; i++)
            {
                float d = Mathf.Clamp01((float)state.panelDustEach[i] / 0.6f);
                dust[i].gameObject.SetActive(d > 0.02f);
                float cover = Mathf.Sqrt(d);
                dust[i].localScale = new Vector3(3f * cover, 0.02f, 1.8f * cover);
            }
        }

        private void UpdateFault(MissionState state)
        {
            bool active = state.faultActive && state.faultSpot >= 0 && state.faultSpot < FaultSpots.Length;
            if (fault.activeSelf != active) fault.SetActive(active);
            if (!active) return;
            fault.transform.localPosition = FaultSpots[state.faultSpot];
            if (Time.time >= nextSpark)
            {
                nextSpark = Time.time + Random.Range(0.25f, 0.9f);
                MarsDebris.Burst(fault.transform.position + Vector3.down * 0.2f, Vector3.down, Paint.WarmOrange, 5, 0.03f, 1f);
            }
        }

        private void UpdateRocks(MissionState state, MarsMissionController mission)
        {
            MarsExpedition x = MarsExpedition.Instance;
            Vector3 here = x == null ? Vector3.zero : x.PlayerPosition;
            foreach (SiteState site in state.sites)
            {
                Vector3 centre = new Vector3((float)site.x, (float)site.y, (float)site.z);
                bool near = !site.surveyed && Vector3.Distance(here, centre) < 40f;
                GameObject[] set;
                rocks.TryGetValue(site.id, out set);
                if (near && set == null) rocks[site.id] = set = BuildRocks(site, centre);
                if (set == null) continue;
                int hits;
                RockHits.TryGetValue(site.id, out hits);
                for (int i = 0; i < set.Length; i++)
                    if (set[i] != null) set[i].SetActive(near && (hits & (1 << i)) == 0);
                if (site.surveyed && set != null)
                {
                    foreach (GameObject rock in set) if (rock != null) Destroy(rock);
                    rocks.Remove(site.id);
                }
            }
        }

        private GameObject[] BuildRocks(SiteState site, Vector3 centre)
        {
            MarsExpedition x = MarsExpedition.Instance;
            var set = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                float angle = i * 2.094f + site.id.GetHashCode() % 7;
                float radius = x == null ? 2.5f : x.Units(2.5f);
                Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (x != null && x.Terrain != null) p.y = x.Terrain.GetGroundY(p);
                GameObject rock = Cube("Survey rock " + (i + 1), null, p + Vector3.up * 0.18f, new Vector3(0.45f, 0.35f, 0.4f), MarsPalette.WarmOrange, true);
                rock.transform.rotation = Quaternion.Euler(12f * i, 40f * i + 10f, 8f);
                MarsTaskTarget target = rock.AddComponent<MarsTaskTarget>();
                target.kind = MarsTaskTarget.Kind.SurveyRock;
                target.siteId = site.id;
                target.index = i;
                set[i] = rock;
            }
            return set;
        }

        /// <summary>Marks one survey rock as hit; returns how many of the three are done.</summary>
        public int HitRock(string siteId, int index)
        {
            int hits;
            RockHits.TryGetValue(siteId, out hits);
            hits |= 1 << index;
            RockHits[siteId] = hits;
            int n = 0;
            for (int i = 0; i < 3; i++) if ((hits & (1 << i)) != 0) n++;
            return n;
        }
    }
}
