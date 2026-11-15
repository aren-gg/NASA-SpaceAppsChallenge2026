using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Base
{
    /// <summary>
    /// The player's outpost (quarters, tunnel, command hub, tunnel, greenhouse) from
    /// Base_assets. Loads the model from Resources, puts the eight palette colours on
    /// it as matte materials, adds mesh colliders so the astronaut cannot walk through
    /// it, settles it on the terrain and clears procedural rocks from under it.
    ///
    /// By default it is placed a short walk in front of where the player starts, with
    /// the hub's airlock door facing the player. Untick <see cref="placeInFrontOfPlayer"/>
    /// to keep the object's own position instead (its Y is still snapped to the ground).
    ///
    /// An outpost is added automatically when a scene with a MarsTerrain starts without
    /// one, so no scene edit is needed.
    /// </summary>
    public sealed class MarsOutpost : MonoBehaviour
    {
        public const string DefaultModel = "MarsBase/base_outpost";

        public MarsTerrain terrain;
        [Tooltip("Model under a Resources folder (Assets/MarsBase/Resources).")]
        public string modelResource = DefaultModel;

        [Header("Placement")]
        public bool placeInFrontOfPlayer = true;
        [Tooltip("Distance (real metres) from the player's start to the command hub's centre.")]
        [Min(5f)] public float distanceFromPlayerMeters = 20f;
        [Tooltip("Extra rock-free ground (real metres) around the base footprint.")]
        [Min(0f)] public float rockClearanceMeters = 4f;
        [Tooltip("Mesh colliders on the modules so the astronaut walks around them and up the entry ramp.")]
        public bool addColliders = true;

        /// <summary>World position just outside the command hub's airlock door.</summary>
        public Vector3 DoorPosition
        {
            get { return transform.TransformPoint(new Vector3(0f, 0f, 4.6f)); }
        }

        public bool IsPlaced { get { return placed; } }

        private static readonly (string name, Color color)[] Palette =
        {
            ("terracotta", MarsPalette.Terracotta), ("dusty_rose", MarsPalette.DustyRose),
            ("muted_olive", MarsPalette.MutedOlive), ("warm_orange", MarsPalette.WarmOrange),
            ("deep_purple", MarsPalette.DeepPurple), ("pale_pink", MarsPalette.PalePink),
            ("mauve", MarsPalette.Mauve), ("clay_sand", MarsPalette.ClaySand),
        };
        private static Dictionary<string, Material> materials;

        private GameObject model;
        private bool placed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AddToTerrainScene()
        {
            if (FindObjectOfType<MarsOutpost>() != null) return;
            if (FindObjectOfType<MarsTerrain>() == null) return;
            MarsOutpost outpost = new GameObject("Mars Outpost").AddComponent<MarsOutpost>();
            // Place now, before the player's Start moves the camera, so "in front of the
            // player" uses the scene's starting view.
            outpost.TryPlace();
        }

        private void Start()
        {
            TryPlace();
        }

        private void Update()
        {
            if (!placed) TryPlace();
        }

        private void TryPlace()
        {
            if (placed) return;
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            if (terrain == null || !terrain.IsReady) return;
            if (model == null && !BuildModel())
            {
                enabled = false;
                return;
            }

            float scale = terrain.config.ToUnits(1f);
            transform.localScale = Vector3.one * scale;

            if (placeInFrontOfPlayer)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 forward = cam.transform.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                    forward.Normalize();
                    Vector3 position = cam.transform.position + forward * terrain.config.ToUnits(distanceFromPlayerMeters);
                    // The door (+Z) faces back toward the player.
                    transform.SetPositionAndRotation(position, Quaternion.LookRotation(-forward, Vector3.up));
                }
            }

            SettleOnGround();
            Physics.SyncTransforms();

            Bounds local = LocalBounds();
            Vector3 centre = transform.TransformPoint(new Vector3(local.center.x, 0f, local.center.z));
            float radius = new Vector2(local.extents.x, local.extents.z).magnitude + rockClearanceMeters;
            terrain.ClearRocks(centre, radius);

            placed = true;
        }

        private bool BuildModel()
        {
            GameObject prefab = Resources.Load<GameObject>(modelResource);
            if (prefab == null)
            {
                Debug.LogError("MarsOutpost: model '" + modelResource + "' was not found under a Resources folder. " +
                               "Expected Assets/MarsBase/Resources/" + modelResource + ".obj.", this);
                return false;
            }

            model = Instantiate(prefab, transform, false);
            model.name = prefab.name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++) shared[i] = PaletteMaterial(shared[i], renderer.gameObject.name);
                renderer.sharedMaterials = shared;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            if (addColliders)
            {
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                }
            }
            return true;
        }

        /// <summary>
        /// Swaps an imported OBJ material for the matching matte palette material, by the
        /// material name (the .mtl name) or else the OBJ group name (base_outpost_mauve).
        /// </summary>
        private static Material PaletteMaterial(Material imported, string objectName)
        {
            if (materials == null)
            {
                materials = new Dictionary<string, Material>();
                foreach (var (name, color) in Palette)
                {
                    materials[name] = MarsPalette.CreateMatteMaterial("Outpost " + name, color);
                }
            }
            string key = imported != null ? imported.name.ToLowerInvariant() : "";
            foreach (var (name, _) in Palette)
            {
                if (key.Contains(name)) return materials[name];
            }
            key = objectName.ToLowerInvariant();
            foreach (var (name, _) in Palette)
            {
                if (key.Contains(name)) return materials[name];
            }
            return materials["terracotta"];
        }

        /// <summary>Model bounds in this object's local space (real metres).</summary>
        private Bounds LocalBounds()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 toLocal = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 min = mesh.min, max = mesh.max;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                    Vector3 p = toLocal.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        /// <summary>
        /// Rests the base on the lowest ground under its footprint, so no edge floats;
        /// on a slope the uphill side sits slightly into the ground instead.
        /// </summary>
        private void SettleOnGround()
        {
            Bounds local = LocalBounds();
            float lowest = float.MaxValue, highest = float.MinValue;
            const int Samples = 6;
            for (int z = 0; z <= Samples; z++)
            {
                for (int x = 0; x <= Samples; x++)
                {
                    Vector3 p = new Vector3(
                        Mathf.Lerp(local.min.x, local.max.x, x / (float)Samples), 0f,
                        Mathf.Lerp(local.min.z, local.max.z, z / (float)Samples));
                    float y = terrain.GetGroundY(transform.TransformPoint(p));
                    lowest = Mathf.Min(lowest, y);
                    highest = Mathf.Max(highest, y);
                }
            }
            Vector3 position = transform.position;
            position.y = lowest;
            transform.position = position;

            float rise = terrain.UnitsToRealMeters(highest - lowest);
            if (rise > 1f)
            {
                Debug.LogWarning("MarsOutpost: the ground rises " + rise.ToString("F1") +
                                 " m across the base footprint, so the uphill side sits in the ground.", this);
            }
        }
    }
}
