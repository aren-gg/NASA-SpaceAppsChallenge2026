using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Base
{
    /// <summary>
    /// The player's outpost: an enterable habitat with a room for each survival system
    /// (see <see cref="MarsOutpostBuilder"/> for the plan). It is built in code at Play,
    /// set on a levelled regolith pad on the terrain with an entry ramp up to the airlock,
    /// stocked with the tool models from Task_assets, and cleared of procedural rocks.
    ///
    /// By default it is placed downhill of where the player starts, with the airlock
    /// facing uphill toward the player, and the view is turned to face it. The pad then
    /// meets the ground at the entrance. Untick <see cref="placeInFrontOfPlayer"/> to
    /// keep this object's own position and heading instead (its height still comes from
    /// the ground, and an apron ramp is added if the front edge of the pad stands proud).
    ///
    /// An outpost is added automatically when a scene with a MarsTerrain starts without
    /// one, so no scene edit is needed. A label at the top of the screen names the room
    /// the astronaut is in and what it is for.
    /// </summary>
    public sealed class MarsOutpost : MonoBehaviour
    {
        public MarsTerrain terrain;

        [Header("Placement")]
        public bool placeInFrontOfPlayer = true;
        [Tooltip("Distance (real metres) from the player start to the centre of the command hub. " +
                 "The airlock is 17 m in front of the hub and the edge of the pad 27 m.")]
        [Min(25f)] public float distanceFromPlayerMeters = 40f;
        [Tooltip("Extra rock-free ground (real metres) around the base.")]
        [Min(0f)] public float rockClearanceMeters = 4f;
        [Tooltip("Height of the top of the pad above the highest ground under it, real metres.")]
        [Min(0.02f)] public float padAboveGroundMeters = 0.05f;

        [Header("Interior")]
        [Tooltip("Brightness of the ceiling lights.")]
        public float interiorLightIntensity = 5f;
        public bool showRoomLabel = true;

        /// <summary>The astronaut's eyes; doors and the room label follow this.</summary>
        public static Transform Viewer { get; private set; }

        public bool IsPlaced { get { return placed; } }
        public IReadOnlyList<MarsOutpostBuilder.Room> Rooms { get { return builder != null ? builder.rooms : null; } }
        /// <summary>The room the astronaut is standing in, or null outside and in tunnels.</summary>
        public MarsOutpostBuilder.Room CurrentRoom { get; private set; }

        /// <summary>World position at the foot of the entry ramp, in front of the airlock.</summary>
        public Vector3 DoorPosition
        {
            get
            {
                float z = builder != null ? builder.RampFootZ + 1f : 22.5f;
                return transform.TransformPoint(new Vector3(0f, MarsOutpostBuilder.PadTop, z));
            }
        }

        private MarsOutpostBuilder builder;
        private bool placed;
        private GUIStyle labelStyle;

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
            if (!placed)
            {
                TryPlace();
                return;
            }
            var astronaut = FindObjectOfType<MarsSurvival.Interaction.MarsAstronautPlayer>();
            Viewer = astronaut != null && !astronaut.IsDriving ? astronaut.transform : null;
            CurrentRoom = null;
            if (Viewer == null) return;
            Vector3 local = transform.InverseTransformPoint(Viewer.position);
            if (local.y < -0.5f || local.y > 3.5f) return;
            List<MarsOutpostBuilder.Room> rooms = builder.rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (!rooms[i].Contains(local.x, local.z)) continue;
                CurrentRoom = rooms[i];
                break;
            }
        }

        private void TryPlace()
        {
            if (placed) return;
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            if (terrain == null || !terrain.IsReady) return;

            float scale = terrain.config.ToUnits(1f);
            transform.localScale = Vector3.one * scale;

            if (placeInFrontOfPlayer)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    // Start the playable outpost near the documented landing site, away from Mount Sharp's steep centre.
                    Vector3 landing = terrain.LatLonToWorld(MarsSurvival.TimeAndWeather.MarsTime.CuriosityLatitude,
                        MarsSurvival.TimeAndWeather.MarsTime.CuriosityLongitudeEast);
                    cam.transform.position = landing + Vector3.up * terrain.config.ToUnits(1.68f);
                    // The base goes downhill of the player with its airlock facing uphill, toward
                    // them: the pad then meets the ground at the entrance instead of standing above it.
                    Vector3 uphill = UphillDirection(cam.transform.position, terrain.config.ToUnits(10f));
                    if (uphill == Vector3.zero)
                    {
                        uphill = -cam.transform.forward;
                        uphill.y = 0f;
                        uphill = uphill.sqrMagnitude < 1e-4f ? Vector3.back : uphill.normalized;
                    }
                    Vector3 position = cam.transform.position - uphill * terrain.config.ToUnits(distanceFromPlayerMeters);
                    transform.SetPositionAndRotation(position, Quaternion.LookRotation(uphill, Vector3.up));
                    // Start the player looking at the base.
                    cam.transform.rotation = Quaternion.LookRotation(-uphill, Vector3.up) * Quaternion.Euler(4f, 0f, 0f);
                }
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }

            // The pad is level and its top sits just above the highest ground under it, so the
            // terrain never shows through; its sides slope down to the lowest ground.
            FloorRect pad = MarsOutpostBuilder.Pad;
            float lowest = float.MaxValue, highest = float.MinValue;
            for (float x = pad.x0; x <= pad.x1 + 1.9f; x += 2f)
            {
                for (float z = pad.z0; z <= pad.z1 + 1.9f; z += 2f)
                {
                    float y = terrain.GetGroundY(transform.TransformPoint(
                        new Vector3(Mathf.Min(x, pad.x1), 0f, Mathf.Min(z, pad.z1))));
                    lowest = Mathf.Min(lowest, y);
                    highest = Mathf.Max(highest, y);
                }
            }
            Vector3 settled = transform.position;
            settled.y = highest + (padAboveGroundMeters - MarsOutpostBuilder.PadTop) * scale;
            transform.position = settled;
            float riseMeters = (highest - lowest) / scale;
            float padDepth = riseMeters + padAboveGroundMeters - MarsOutpostBuilder.PadTop + 1f;

            builder = new MarsOutpostBuilder(transform, padDepth, GroundLocalY, interiorLightIntensity);
            builder.Build();
            Physics.SyncTransforms();

            // Rocks are cleared from the whole pad and its sloping sides.
            Vector3 centre = transform.TransformPoint(new Vector3(pad.CentreX, 0f, pad.CentreZ));
            float radius = new Vector2(pad.Width, pad.Depth).magnitude * 0.5f + padDepth * 1.6f;
            terrain.ClearRocks(centre, radius + rockClearanceMeters);

            placed = true;
            if (builder.toolsMissing > 0)
            {
                Debug.LogWarning("MarsOutpost: " + builder.toolsMissing + " tool model(s) were not found under " +
                                 "Assets/MarsTools/Resources/MarsTools, so those spots are empty.", this);
            }
            Debug.Log("Mars outpost placed: " + builder.rooms.Count + " rooms, " + builder.toolsPlaced +
                      " tools. The ground rises " + riseMeters.ToString("F1") + " m across the pad.", this);
        }

        /// <summary>Horizontal unit vector pointing up the slope at a world position, or zero on level ground.</summary>
        private Vector3 UphillDirection(Vector3 world, float step)
        {
            float dx = terrain.GetGroundY(world + Vector3.right * step) - terrain.GetGroundY(world - Vector3.right * step);
            float dz = terrain.GetGroundY(world + Vector3.forward * step) - terrain.GetGroundY(world - Vector3.forward * step);
            Vector3 gradient = new Vector3(dx, 0f, dz);
            return gradient.magnitude < step * 0.004f ? Vector3.zero : gradient.normalized;
        }

        /// <summary>Terrain height under an outpost-local x, z, as an outpost-local y (0 = floor).</summary>
        private float GroundLocalY(float x, float z)
        {
            Vector3 world = transform.TransformPoint(new Vector3(x, 0f, z));
            return (terrain.GetGroundY(world) - transform.position.y) / transform.lossyScale.y;
        }

        private void OnGUI()
        {
            if (!showRoomLabel || CurrentRoom == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box);
                labelStyle.alignment = TextAnchor.UpperCenter;
                labelStyle.wordWrap = true;
                labelStyle.richText = true;
                labelStyle.fontSize = 14;
                labelStyle.padding = new RectOffset(12, 12, 8, 8);
                labelStyle.normal.textColor = MarsPalette.PalePink;
            }
            string text = "<b>" + CurrentRoom.name + "</b>\n" + CurrentRoom.feature;
            if (!string.IsNullOrEmpty(CurrentRoom.tools)) text += "\nTools here: " + CurrentRoom.tools;
            float width = Mathf.Min(560f, Screen.width - 40f);
            float height = labelStyle.CalcHeight(new GUIContent(text), width);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, 10f, width, height), text, labelStyle);
        }

        private void OnDestroy()
        {
            if (builder == null) return;
            foreach (Object o in builder.owned)
            {
                if (o != null) Destroy(o);
            }
        }
    }
}
