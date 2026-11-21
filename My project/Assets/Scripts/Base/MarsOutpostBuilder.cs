using System;
using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;
using static MarsSurvival.Base.OutpostGeometry;

namespace MarsSurvival.Base
{
    /// <summary>
    /// Builds the enterable outpost: the pressurised modules, the tunnels between them,
    /// their furniture, and the tool models from Task_assets in the rooms that use them.
    ///
    /// Plan (outpost-local metres; +Z is the front, where the airlock and ramp are;
    /// y = 0 is the floor):
    ///
    ///                           Airlock - landing - ramp
    ///                              |
    ///            Storage ------ HUB (reception) ------ Utilities
    ///                              |
    ///                          Greenhouse
    ///
    /// Each room stands for one or more survival systems in AGENT_IMPLEMENTATION_GUIDE.md.
    /// When the Blender-made modules are present (<see cref="OutpostModules"/>) they are the
    /// walls, roofs and tunnels, inside and out, and this builder adds only the floors'
    /// colliders, furniture, tools, sliding doors and lamps. Without them it falls back to its
    /// own shells, which are white with dark red accents: the hub is a twelve-sided dome, the
    /// other modules are capsules with vaulted roofs (two with rounded ends), and the
    /// tunnels are tubes. Furniture keeps the eight palette colours of the tool kit.
    /// Walls, floors and furniture get box colliders, which is what the astronaut's
    /// movement collides with.
    /// </summary>
    public sealed class MarsOutpostBuilder
    {
        public sealed class Room
        {
            public string name;
            /// <summary>What the room is for, in terms of the survival systems.</summary>
            public string feature;
            /// <summary>The tools kept here.</summary>
            public string tools;
            public FloorRect rect;
            /// <summary>Above zero for a round room: its radius about the centre of <see cref="rect"/>.</summary>
            public float radius;

            public bool Contains(float x, float z)
            {
                if (!rect.Contains(x, z)) return false;
                if (radius <= 0f) return true;
                float dx = x - rect.CentreX, dz = z - rect.CentreZ;
                return dx * dx + dz * dz <= radius * radius;
            }
        }

        private struct Opening
        {
            public float centre, width, bottom, top;
            public int kind; // 0 open archway, 1 sliding door, 2 window
        }

        public const float WallThickness = 0.25f;
        private const float T = WallThickness;
        private const float TunnelHalf = 1.5f, TunnelHeight = 2.3f;
        private const int West = 0, East = 1, Back = 2, Front = 3;

        // Interior floor rectangles. Neighbours are 2.5 m apart: two walls and a 2 m tunnel.
        /// <summary>The hub is round: twelve flat wall facets this far from its centre.</summary>
        public const float HubApothem = 5f;
        public static readonly FloorRect Hub = new FloorRect(-HubApothem, -HubApothem, HubApothem, HubApothem);
        /// <summary>Seeds, tools, food and the bunk (west wing).</summary>
        public static readonly FloorRect Storage = new FloorRect(-16f, -4f, -7.5f, 4f);
        /// <summary>Power and water (east wing).</summary>
        public static readonly FloorRect Utilities = new FloorRect(7.5f, -4f, 16f, 4f);
        public static readonly FloorRect Greenhouse = new FloorRect(-3.5f, -18f, 3.5f, -7f);
        public static readonly FloorRect Airlock = new FloorRect(-1.7f, 13.25f, 1.7f, 16.75f);
        public static readonly FloorRect Landing = new FloorRect(-1.9f, 17f, 1.9f, 18.5f);

        /// <summary>
        /// The levelled regolith pad the whole outpost stands on, including the solar array and
        /// the outdoor tank. Its top is <see cref="PadTop"/> below the floor.
        /// </summary>
        public static readonly FloorRect Pad = new FloorRect(-30.5f, -24f, 25.5f, 27f);
        /// <summary>Where the rover is parked on the pad, and the way it faces (degrees about the vertical).</summary>
        public static readonly Vector3 RoverParking = new Vector3(-9.5f, PadTop, 15f);
        public const float RoverParkingYaw = 0f;
        public const float PadTop = -0.25f;

        /// <summary>Tool id (file name in Resources/MarsTools) to the lowest point of its model, metres.</summary>
        private static readonly Dictionary<string, float> ToolLowestPoint = new Dictionary<string, float>
        {
            { "comm_radio", 0f }, { "core_drill", -0.080f }, { "dosimeter", -0.015f }, { "hydro_sprayer", 0f },
            { "multimeter", -0.005f }, { "oxygen_canister", 0f }, { "panel_brush", -0.125f },
            { "power_wrench", -0.212f }, { "regolith_shovel", -0.340f }, { "rock_hammer", -0.100f },
            { "sample_canisters", 0f }, { "sample_scoop", -0.087f }, { "survey_camera", 0f },
            { "survey_flag", -0.007f }, { "work_lamp", -0.020f },
        };

        public readonly List<Room> rooms = new List<Room>();
        public readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        public int toolsPlaced, toolsMissing;

        private readonly Transform root;
        private const float depth = 0.3f; // module foundations reach just into the pad
        private readonly float padDepth;
        private readonly Func<float, float, float> groundAt;
        private readonly Material[] materials;
        private readonly float lightIntensity;
        private readonly bool graded;

        private OutpostGeometry g;
        private GameObject current;
        private Mesh doorLeafX, doorLeafZ, panelMesh;
        private Material[] doorLeafMaterials, panelMaterials;

        /// <param name="padDepth">How far the pad reaches below the floor to meet the lowest ground, metres.</param>
        /// <param name="groundAt">Terrain height at an outpost-local x, z, as a local y (0 = floor).</param>
        /// <param name="graded">The terrain itself was graded into the pad (MarsOutpost.TryGradePad), so no
        /// pad block is drawn: only its collider, sintered paving, the path and tyre tracks.</param>
        public MarsOutpostBuilder(Transform root, float padDepth, Func<float, float, float> groundAt,
            float interiorLightIntensity, bool graded = false)
        {
            this.graded = graded;
            this.root = root;
            this.padDepth = Mathf.Max(0.6f, padDepth);
            this.groundAt = groundAt;
            lightIntensity = interiorLightIntensity;
            materials = CreateMaterials();
        }

        /// <summary>
        /// True when the Blender-made modules (<see cref="OutpostModules"/>) are present. They are
        /// the walls, roofs and tunnels, inside and out; the builder then adds only what lives in
        /// the rooms (furniture, tools, sliding doors, lamps) and the floor colliders.
        /// </summary>
        private bool moduled;

        public void Build()
        {
            moduled = OutpostModules.Available;
            BuildHub();
            BuildStorage();
            BuildUtilities();
            BuildGreenhouse();
            BuildAirlock();
            BuildTunnels();
            BuildExterior();
            // The printed modules (Blender models) are the walls, roofs and tunnels around the rooms built above.
            if (moduled) OutpostModules.Attach(root, owned);
        }

        // ------------------------------------------------------------------ materials

        private Material[] CreateMaterials()
        {
            Material[] all = OutpostMaterials.Create();
            owned.AddRange(all);
            return all;
        }

        // ------------------------------------------------------------------ building blocks

        private void Begin(string objectName)
        {
            current = new GameObject(objectName);
            current.transform.SetParent(root, false);
            g = new OutpostGeometry();
            hull = new OutpostGeometry();
        }

        /// <summary>
        /// Collision copy of curved or sloped shapes (roofs, dome, mound, tube, pad slopes), so the
        /// astronaut stands on and is stopped by the surface that is drawn, not a box around it.
        /// </summary>
        private OutpostGeometry hull;

        /// <summary>Draws the shapes, then draws them again into the collision hull.</summary>
        private void AlsoSolid(System.Action draw)
        {
            draw();
            OutpostGeometry drawn = g;
            g = hull;
            try { draw(); }
            finally { g = drawn; }
        }

        private Room BeginRoom(string roomName, FloorRect rect, string feature, string tools)
        {
            Begin(roomName);
            Room room = new Room { name = roomName, rect = rect, feature = feature, tools = tools };
            rooms.Add(room);
            return room;
        }

        private void End()
        {
            if (!g.IsEmpty)
            {
                Material[] used;
                Mesh mesh = g.ToMesh(current.name, materials, out used);
                owned.Add(mesh);
                current.AddComponent<MeshFilter>().sharedMesh = mesh;
                current.AddComponent<MeshRenderer>().sharedMaterials = used;
            }
            if (hull != null && !hull.IsEmpty)
            {
                Material[] unused;
                Mesh shape = hull.ToMesh(current.name + " hull", materials, out unused);
                owned.Add(shape);
                GameObject solid = new GameObject("Hull collider");
                solid.transform.SetParent(current.transform, false);
                solid.AddComponent<MeshCollider>().sharedMesh = shape;
            }
            hull = null;
            g = null;
            current = null;
        }

        /// <summary>A box that is drawn and also blocks the astronaut.</summary>
        private void Solid(int material, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            g.Box(material, x0, y0, z0, x1, y1, z1);
            AddCollider(x0, y0, z0, x1, y1, z1);
        }

        private bool framed;
        private Vector3 frameOrigin;
        private float frameYaw;

        /// <summary>
        /// Until <see cref="ClearFrame"/>, geometry and colliders are given in a local frame whose
        /// +Z points along the heading (degrees about the vertical) and whose origin is here.
        /// </summary>
        private void SetFrame(Vector3 origin, float yawDegrees)
        {
            framed = true;
            frameOrigin = origin;
            frameYaw = yawDegrees;
            g.SetFrame(origin, yawDegrees);
            if (hull != null) hull.SetFrame(origin, yawDegrees);
        }

        private void ClearFrame()
        {
            framed = false;
            g.ClearFrame();
            if (hull != null) hull.ClearFrame();
        }

        private void AddCollider(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f || z1 - z0 < 1e-3f) return;
            Vector3 centre = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            Vector3 size = new Vector3(x1 - x0, y1 - y0, z1 - z0);
            if (framed)
            {
                GameObject turned = new GameObject("Collider");
                turned.transform.SetParent(current.transform, false);
                Quaternion rotation = Quaternion.Euler(0f, frameYaw, 0f);
                turned.transform.localRotation = rotation;
                turned.transform.localPosition = frameOrigin + rotation * centre;
                turned.AddComponent<BoxCollider>().size = size;
                return;
            }
            BoxCollider box = current.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        private void Lamp(float x, float y, float z, float range, float scale = 1f)
        {
            GameObject lamp = new GameObject("Light");
            lamp.transform.SetParent(current.transform, false);
            lamp.transform.localPosition = new Vector3(x, y, z);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.93f, 0.84f);
            light.range = range;
            light.intensity = lightIntensity * scale;
            light.shadows = LightShadows.None;
        }

        // Every doorway between modules and tunnels gets a sliding door that opens on approach;
        // an empty frame read as a door the astronaut could walk straight through.
        private static Opening Arch(float centre) { return Door(centre); }
        private static Opening Door(float centre) { return new Opening { centre = centre, width = 2f, top = 2.4f, kind = 1 }; }
        private static Opening Window(float centre, float width = 1.2f)
        {
            return new Opening { centre = centre, width = width, bottom = 1f, top = 1.95f, kind = 2 };
        }

        /// <summary>
        /// A piece of one wall of a room, between two positions along the wall and two heights.
        /// West and east walls run along z; back and front walls run along x and include the corners.
        /// </summary>
        private void WallPiece(int side, FloorRect r, float a0, float a1, float y0, float y1, int material,
            float inset = 0f, bool draw = true, float colliderExtra = -1f)
        {
            if (a1 - a0 < 1e-3f || y1 - y0 < 1e-3f) return;
            float x0, x1, z0, z1;
            float outer = colliderExtra >= 0f ? T + colliderExtra : T - inset;
            float inner = colliderExtra >= 0f ? 0f : inset;
            switch (side)
            {
                case West: x0 = r.x0 - outer; x1 = r.x0 - inner; z0 = a0; z1 = a1; break;
                case East: x0 = r.x1 + inner; x1 = r.x1 + outer; z0 = a0; z1 = a1; break;
                case Back: z0 = r.z0 - outer; z1 = r.z0 - inner; x0 = a0; x1 = a1; break;
                default: z0 = r.z1 + inner; z1 = r.z1 + outer; x0 = a0; x1 = a1; break;
            }
            if (draw) g.Box(material, x0, y0, z0, x1, y1, z1);
            else AddCollider(x0, y0, z0, x1, y1, z1);
        }

        private void Wall(int side, FloorRect r, float height, int material, params Opening[] openings)
        {
            if (moduled)
            {
                // The module's printed end wall is the wall; it has the doorway built in.
                foreach (Opening o in openings) if (o.kind == 1) SlidingDoor(side, r, o);
                return;
            }
            bool alongZ = side == West || side == East;
            float start = alongZ ? r.z0 : r.x0 - T, end = alongZ ? r.z1 : r.x1 + T;
            Array.Sort(openings, (a, b) => a.centre.CompareTo(b.centre));

            float cursor = start, solidFrom = start;
            foreach (Opening o in openings)
            {
                float o0 = o.centre - o.width * 0.5f, o1 = o.centre + o.width * 0.5f;
                WallPiece(side, r, cursor, o0, 0f, height, material);
                WallPiece(side, r, o0, o1, o.top, height, material);
                if (o.kind == 2)
                {
                    WallPiece(side, r, o0, o1, 0f, o.bottom, material);
                    WallPiece(side, r, o0, o1, o.bottom, o.top, Glass, 0.1f);
                }
                else
                {
                    // A way through: the wall's collider stops here and starts again after it.
                    WallPiece(side, r, solidFrom, o0, 0f, height, material, 0f, false, 0.2f);
                    solidFrom = o1;
                    Frame(side, r, o0, o1, o.top);
                    if (o.kind == 1) SlidingDoor(side, r, o);
                }
                cursor = o1;
            }
            WallPiece(side, r, cursor, end, 0f, height, material);
            WallPiece(side, r, solidFrom, end, 0f, height, material, 0f, false, 0.2f);
        }

        /// <summary>Dark red trim around a doorway, standing slightly proud of both wall faces.</summary>
        private void Frame(int side, FloorRect r, float o0, float o1, float top)
        {
            // The trim reaches a hair into the doorway (e) so its faces never lie in the same
            // plane as the wall's own doorway edges or the underside of the wall above the door
            // (coplanar faces z-fight and flicker). The head piece sits a few millimetres behind
            // the jambs' faces for the same reason where the two overlap. The sliding leaves'
            // edges and tops tuck inside this trim when shut.
            const float w = 0.1f, e = 0.015f;
            WallPiece(side, r, o0 - w, o0 + e, 0f, top + w, DarkRed, -0.04f);
            WallPiece(side, r, o1 - e, o1 + w, 0f, top + w, DarkRed, -0.04f);
            WallPiece(side, r, o0, o1, top - e, top + w - 0.005f, DarkRed, -0.035f);
        }

        private readonly List<MarsOutpostDoor> doors = new List<MarsOutpostDoor>();
        /// <summary>How far a door moves out from the old 25 cm wall's middle into a printed wall.</summary>
        private const float ModuleDoorShift = 0.07f, HubDoorShift = 0.475f;

        private MarsOutpostDoor SlidingDoor(int side, FloorRect r, Opening o)
        {
            bool alongZ = side == West || side == East;
            if (doorLeafX == null)
            {
                doorLeafX = DoorLeafMesh(false, o.width * 0.5f, o.top);
                doorLeafZ = DoorLeafMesh(true, o.width * 0.5f, o.top);
            }
            // In a printed module the wall is thicker, so the door moves out to its middle and
            // the leaves slide into the wall (in the hub, into the double shell).
            float shift = !moduled ? 0f : framed ? HubDoorShift : ModuleDoorShift;
            float mid = side == West ? r.x0 - T * 0.5f - shift : side == East ? r.x1 + T * 0.5f + shift
                : side == Back ? r.z0 - T * 0.5f - shift : r.z1 + T * 0.5f + shift;
            GameObject door = new GameObject("Door");
            door.transform.SetParent(current.transform, false);
            Vector3 local = alongZ ? new Vector3(mid, 0f, o.centre) : new Vector3(o.centre, 0f, mid);
            if (framed)
            {
                // Hub facets and tunnels are built in a turned frame; the door must follow it.
                Quaternion turn = Quaternion.Euler(0f, frameYaw, 0f);
                door.transform.localPosition = frameOrigin + turn * local;
                door.transform.localRotation = turn;
            }
            else door.transform.localPosition = local;

            MarsOutpostDoor script = door.AddComponent<MarsOutpostDoor>();
            script.slideAxis = alongZ ? Vector3.forward : Vector3.right;
            script.leafOffset = o.width * 0.25f;
            script.travel = o.width * 0.5f - 0.03f;
            // Wide enough that the door is open before a sprinting astronaut reaches it.
            script.openRange = 3.5f;
            script.secondsToOpen = 0.45f;
            script.leafA = Leaf(door.transform, alongZ ? doorLeafZ : doorLeafX);
            script.leafB = Leaf(door.transform, alongZ ? doorLeafZ : doorLeafX);
            script.blocker = door.AddComponent<BoxCollider>();
            script.blocker.center = new Vector3(0f, o.top * 0.5f, 0f);
            script.blocker.size = alongZ ? new Vector3(T + 0.3f, o.top, o.width) : new Vector3(o.width, o.top, T + 0.3f);
            doors.Add(script);
            return script;
        }

        private Transform Leaf(Transform door, Mesh mesh)
        {
            GameObject leaf = new GameObject("Leaf");
            leaf.transform.SetParent(door, false);
            leaf.AddComponent<MeshFilter>().sharedMesh = mesh;
            leaf.AddComponent<MeshRenderer>().sharedMaterials = doorLeafMaterials;
            return leaf.transform;
        }

        private Mesh DoorLeafMesh(bool alongZ, float width, float height)
        {
            OutpostGeometry leaf = new OutpostGeometry();
            float h = width * 0.5f, t = 0.05f;
            // In the printed modules the leaves are gunmetal with an amber guide strip (the
            // interiors' colour for the way through); the fallback shells keep dark red.
            int body = moduled ? Charcoal : DarkRed, band = moduled ? GlowOrange : Charcoal;
            float b0 = moduled ? 1.05f : 1.25f, b1 = moduled ? 1.12f : 1.7f;
            if (alongZ)
            {
                leaf.Box(body, -t, 0f, -h, t, height, h);
                leaf.Box(band, -t - 0.01f, b0, -h + 0.12f, t + 0.01f, b1, h - 0.12f);
            }
            else
            {
                leaf.Box(body, -h, 0f, -t, h, height, t);
                leaf.Box(band, -h + 0.12f, b0, -t - 0.01f, h - 0.12f, b1, t + 0.01f);
            }
            Mesh mesh = leaf.ToMesh("Door Leaf", materials, out doorLeafMaterials);
            owned.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// A capsule module: foundation, floor, four walls, flat ceiling, a vaulted roof with an
        /// accent stripe along its ridge, and a ceiling light. <paramref name="roundEnd"/> adds a
        /// rounded end outside one wall (a side constant), which must have no openings.
        /// </summary>
        private void Shell(FloorRect r, float height, int wallMaterial, int accent, float roofRise,
            Opening[] west, Opening[] east, Opening[] back, Opening[] front, bool sharedBackWall = false,
            int roundEnd = -1)
        {
            FloorRect outer = r.Grow(T);
            FloorRect plinth = outer.Grow(0.15f);
            AddCollider(plinth.x0, -depth, plinth.z0, plinth.x1, 0f, plinth.z1);
            if (moduled)
            {
                // The printed module is the floor deck, walls and vault; only the doors and the lamp are added.
                Wall(West, r, height, wallMaterial, west ?? new Opening[0]);
                Wall(East, r, height, wallMaterial, east ?? new Opening[0]);
                if (!sharedBackWall) Wall(Back, r, height, wallMaterial, back ?? new Opening[0]);
                Wall(Front, r, height, wallMaterial, front ?? new Opening[0]);
                Lamp(r.CentreX, height - 0.35f, r.CentreZ, Mathf.Max(r.Width, r.Depth) * 0.75f + 4f);
                return;
            }
            g.Box(Charcoal, plinth, -depth, -0.1f);
            g.Box(ClaySand, outer, -0.1f, 0f);

            Wall(West, r, height, wallMaterial, west ?? new Opening[0]);
            Wall(East, r, height, wallMaterial, east ?? new Opening[0]);
            if (!sharedBackWall) Wall(Back, r, height, wallMaterial, back ?? new Opening[0]);
            Wall(Front, r, height, wallMaterial, front ?? new Opening[0]);

            float top = height + 0.12f;
            g.Box(White, outer, height, top);
            AlsoSolid(() => Vault(outer, top, roofRise, wallMaterial == White ? White : accent, accent));
            AddCollider(outer.x0, height, outer.z0, outer.x1, top, outer.z1);
            if (roundEnd >= 0) RoundEnd(roundEnd, outer, top, roofRise);

            g.Box(GlowWarm, r.CentreX - 0.7f, height - 0.05f, r.CentreZ - 0.3f, r.CentreX + 0.7f, height, r.CentreZ + 0.3f);
            Lamp(r.CentreX, height - 0.45f, r.CentreZ, Mathf.Max(r.Width, r.Depth) * 0.75f + 4f);
        }

        /// <summary>A barrel roof along the longer side of a rectangle, with the two ridge strips in an accent colour.</summary>
        private void Vault(FloorRect outer, float y0, float rise, int material, int ridgeMaterial)
        {
            bool alongZ = outer.Depth >= outer.Width;
            float half = (alongZ ? outer.Width : outer.Depth) * 0.5f + 0.12f;
            float a0 = (alongZ ? outer.z0 : outer.x0) - 0.12f, a1 = (alongZ ? outer.z1 : outer.x1) + 0.12f;
            float mid = alongZ ? outer.CentreX : outer.CentreZ;
            Vector3 inside = new Vector3(outer.CentreX, y0, outer.CentreZ);
            Vector3 endA = alongZ ? new Vector3(mid, y0, a0) : new Vector3(a0, y0, mid);
            Vector3 endB = alongZ ? new Vector3(mid, y0, a1) : new Vector3(a1, y0, mid);
            const int steps = 8;
            Vector3 previousA = Vector3.zero, previousB = Vector3.zero;
            for (int k = 0; k <= steps; k++)
            {
                float t = Mathf.PI * k / steps;
                float across = mid + half * Mathf.Cos(t), y = y0 + rise * Mathf.Sin(t);
                Vector3 a = alongZ ? new Vector3(across, y, a0) : new Vector3(a0, y, across);
                Vector3 b = alongZ ? new Vector3(across, y, a1) : new Vector3(a1, y, across);
                if (k > 0)
                {
                    bool ridge = k == steps / 2 || k == steps / 2 + 1;
                    g.Quad(ridge ? ridgeMaterial : material, previousA, a, b, previousB, inside);
                    g.Triangle(material, endA, previousA, a, inside);
                    g.Triangle(material, endB, previousB, b, inside);
                }
                previousA = a;
                previousB = b;
            }
        }

        /// <summary>
        /// A half-round end on the outside of one wall, with a half-cone roof meeting the vault
        /// and a dark window band. Solid: the room inside keeps its flat wall.
        /// </summary>
        private void RoundEnd(int side, FloorRect outer, float top, float rise)
        {
            bool onX = side == West || side == East;
            float radius = (onX ? outer.Depth : outer.Width) * 0.5f;
            Vector3 centre = side == West ? new Vector3(outer.x0, 0f, outer.CentreZ)
                : side == East ? new Vector3(outer.x1, 0f, outer.CentreZ)
                : side == Back ? new Vector3(outer.CentreX, 0f, outer.z0)
                : new Vector3(outer.CentreX, 0f, outer.z1);
            SetFrame(centre, side == West ? -90f : side == East ? 90f : side == Back ? 180f : 0f);

            Vector3 inside = new Vector3(0f, 1f, radius * 0.3f);
            Vector3 apex = new Vector3(0f, top + rise, 0f);
            const int steps = 8;
            AlsoSolid(() => {
            for (int k = 0; k < steps; k++)
            {
                float t0 = Mathf.PI * k / steps, t1 = Mathf.PI * (k + 1) / steps;
                Vector3 d0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0)), d1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                Vector3 p0 = d0 * radius, p1 = d1 * radius;
                g.Quad(Charcoal, p0 + Vector3.up * -depth, p1 + Vector3.up * -depth, p1 + Vector3.up * -0.1f, p0 + Vector3.up * -0.1f, inside);
                g.Quad(White, p0 + Vector3.up * -0.1f, p1 + Vector3.up * -0.1f, p1 + Vector3.up * top, p0 + Vector3.up * top, inside);
                g.Triangle(k == steps / 2 - 1 || k == steps / 2 ? DarkRed : White, apex, p0 + Vector3.up * top, p1 + Vector3.up * top, inside);
                if (k >= 2 && k <= 5)
                {
                    Vector3 q0 = d0 * (radius + 0.03f), q1 = d1 * (radius + 0.03f);
                    g.Quad(Charcoal, q0 + Vector3.up * 1f, q1 + Vector3.up * 1f, q1 + Vector3.up * 1.9f, q0 + Vector3.up * 1.9f, inside);
                }
            }
            });
            AddCollider(-radius, -depth, 0f, radius, top, radius * 0.45f);
            AddCollider(-radius * 0.7f, -depth, radius * 0.45f, radius * 0.7f, top, radius * 0.85f);
            ClearFrame();
        }

        private void Tank(int material, float x, float z, float radius, float height, int capMaterial)
        {
            g.Prism(material, x, z, radius, 0f, height);
            g.Prism(capMaterial, x, z, radius * 0.55f, height, height + 0.18f, 8, radius * 0.4f);
            AddCollider(x - radius, 0f, z - radius, x + radius, height, z + radius);
        }

        /// <summary>A crate, cabinet or bench: a box on the floor that blocks the astronaut.</summary>
        private void Block(int material, float x0, float z0, float x1, float z1, float height, float from = 0f)
        {
            Solid(material, x0, from, z0, x1, height, z1);
        }

        /// <summary>A stool or small seat.</summary>
        private void Stool(float x, float z)
        {
            g.Prism(Mauve, x, z, 0.2f, 0f, 0.45f);
            AddCollider(x - 0.2f, 0f, z - 0.2f, x + 0.2f, 0.45f, z + 0.2f);
        }

        /// <summary>
        /// Places a tool model standing on a surface at height y. <paramref name="lay"/> rests
        /// it on its side instead; lean angles (degrees) prop a long tool against a wall.
        /// </summary>
        private void Tool(string id, float x, float y, float z, float yaw = 0f, bool lay = false,
            float leanX = 0f, float leanZ = 0f)
        {
            GameObject prefab = Resources.Load<GameObject>("MarsTools/" + id);
            if (prefab == null)
            {
                toolsMissing++;
                return;
            }
            GameObject tool = UnityEngine.Object.Instantiate(prefab, current.transform, false);
            tool.name = id;
            int copy = 0;
            foreach (MarsBaseTool other in current.GetComponentsInChildren<MarsBaseTool>(true)) if (other.toolId == id) copy++;
            MarsBaseTool usable = tool.AddComponent<MarsBaseTool>();
            usable.toolId = id;
            usable.key = current.name + "/" + id + "#" + copy;
            float lowest;
            ToolLowestPoint.TryGetValue(id, out lowest);
            if (lay)
            {
                tool.transform.localPosition = new Vector3(x, y + 0.03f, z);
                tool.transform.localRotation = Quaternion.Euler(90f, yaw, 0f);
            }
            else
            {
                float lean = Mathf.Max(Mathf.Abs(leanX), Mathf.Abs(leanZ)) * Mathf.Deg2Rad;
                tool.transform.localPosition = new Vector3(x, y - lowest * Mathf.Cos(lean), z);
                tool.transform.localRotation = Quaternion.Euler(leanX, yaw, leanZ);
            }
            foreach (MeshRenderer renderer in tool.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++) shared[i] = PaletteMaterial(shared[i], renderer.gameObject.name);
                renderer.sharedMaterials = shared;
            }
            toolsPlaced++;
        }

        private static readonly string[] PaletteNames =
        {
            "terracotta", "dusty_rose", "muted_olive", "warm_orange", "deep_purple", "pale_pink", "mauve", "clay_sand",
        };

        /// <summary>The matte palette material named by an imported OBJ material or its group name.</summary>
        private Material PaletteMaterial(Material imported, string objectName)
        {
            string materialName = imported != null ? imported.name.ToLowerInvariant() : "";
            string groupName = objectName.ToLowerInvariant();
            for (int i = 0; i < PaletteNames.Length; i++)
            {
                if (materialName.Contains(PaletteNames[i])) return materials[i];
            }
            for (int i = 0; i < PaletteNames.Length; i++)
            {
                if (groupName.Contains(PaletteNames[i])) return materials[i];
            }
            return materials[Terracotta];
        }

        // ------------------------------------------------------------------ rooms

        /// <summary>Corner radius of a twelve-sided ring whose flat sides are this far from the centre.</summary>
        private static float Corner(float apothem)
        {
            return apothem / Mathf.Cos(Mathf.PI / 12f);
        }

        private void BuildHub()
        {
            Room room = BeginRoom("Reception", Hub,
                "The hub: storage to the west, utilities to the east, the greenhouse behind, the airlock in front.",
                "");
            room.radius = HubApothem;
            const float h = 2.9f, a = HubApothem;

            if (!moduled)
            {
                g.Prism(Charcoal, 0f, 0f, Corner(a + T + 0.15f), -depth, -0.1f, 12);
                g.Prism(ClaySand, 0f, 0f, Corner(a + T), -0.1f, 0f, 12);
            }
            AddCollider(-a - T, -depth, -a - T, a + T, 0f, a + T);

            // Twelve wall facets. The four on the axes hold the doorways to the tunnels.
            float halfOuter = (a + T) * Mathf.Tan(Mathf.PI / 12f);
            FloorRect facet = new FloorRect(-halfOuter + T, -1f, halfOuter - T, 0f);
            for (int k = 0; k < 12; k++)
            {
                float angle = k * 30f * Mathf.Deg2Rad;
                SetFrame(new Vector3(a * Mathf.Cos(angle), 0f, a * Mathf.Sin(angle)), 90f - k * 30f);
                if (k % 3 == 0) Wall(Front, facet, h, White, Arch(0f));
                else if (k % 3 == 1) Wall(Front, facet, h, White, Window(0f, 1.3f));
                else Wall(Front, facet, h, White);
            }
            ClearFrame();

            // Flat ceiling inside, a stepped dome outside with a dark red cap and four bands.
            // The printed hub module replaces all of it with its tall double shell.
            float top = h + 0.12f;
            float[] ringRadius = { a + T + 0.12f, 4.85f, 3.9f, 2.6f, 1.1f };
            float[] ringHeight = { top, top + 0.95f, top + 1.7f, top + 2.25f, top + 2.5f };
            float domeTop = top + 2.75f;
            if (!moduled) g.Prism(White, 0f, 0f, Corner(a + T), h, top, 12);
            if (!moduled) AlsoSolid(() =>
            {
                for (int i = 0; i + 1 < ringRadius.Length; i++)
                    g.Prism(White, 0f, 0f, Corner(ringRadius[i]), ringHeight[i], ringHeight[i + 1], 12, Corner(ringRadius[i + 1]));
                g.Prism(DarkRed, 0f, 0f, Corner(1.1f), top + 2.5f, domeTop, 12, Corner(0.7f));
            });
            if (!moduled) g.Prism(DarkRed, 0f, 0f, Corner(a + T + 0.16f), h - 0.25f, top + 0.02f, 12);
            for (int band = 0; band < (moduled ? 0 : 4); band++)
            {
                SetFrame(Vector3.zero, band * 90f);
                Vector3 lift = new Vector3(0f, 0.06f, 0.06f);
                for (int i = 0; i + 1 < ringRadius.Length; i++)
                {
                    Vector3 b0 = new Vector3(-0.3f, ringHeight[i], ringRadius[i]), b1 = new Vector3(0.3f, ringHeight[i], ringRadius[i]);
                    Vector3 b2 = new Vector3(0.3f, ringHeight[i + 1], ringRadius[i + 1]), b3 = new Vector3(-0.3f, ringHeight[i + 1], ringRadius[i + 1]);
                    g.Hexa(DarkRed, b0, b1, b2, b3, b0 + lift, b1 + lift, b2 + lift, b3 + lift);
                }
                ClearFrame();
            }
            if (!moduled)
            {
                AddCollider(-a - T, h, -a - T, a + T, top, a + T);
                g.Prism(GlowWarm, 0f, 0f, 1.2f, h - 0.05f, h, 12);
                Lamp(0f, h - 0.45f, 0f, 12f);
            }
            else
            {
                // Under the hanging light ring of the tall printed dome.
                Lamp(0f, 4.6f, 0f, 14f, 1.6f);
            }

            // Command table in the middle; the way between any two doors goes round it.
            g.Prism(Charcoal, 0f, 0f, 0.85f, 0f, 0.8f);
            g.Prism(Mauve, 0f, 0f, 1.15f, 0.8f, 0.88f);
            g.Prism(GlowWarm, 0f, 0f, 0.8f, 0.88f, 0.9f);
            AddCollider(-1.05f, 0f, -1.05f, 1.05f, 0.9f, 1.05f);
            // Comms mast on the dome (the printed module has its own mast).
            if (!moduled)
            {
                g.Prism(Charcoal, 0f, 0f, 0.08f, domeTop, domeTop + 3.2f);
                g.Box(Charcoal, -0.6f, domeTop + 2.3f, -0.04f, 0.6f, domeTop + 2.38f, 0.04f);
                g.Prism(DarkRed, 0f, 0f, 0.13f, domeTop + 3.2f, domeTop + 3.4f);
            }
            End();
        }

        private void BuildStorage()
        {
            FloorRect r = Storage;
            BeginRoom("Storage", r, "The seed shop kiosk, food and the bunk.", "");
            Shell(r, 2.7f, White, DarkRed, 1.1f, null, new[] { Arch(0f) }, null, null);
            // shortcut: plain blocks stand in for furniture until it is modelled.
            Block(Olive, -15.6f, 2.9f, -13.6f, 3.6f, 1.75f);        // food locker
            Block(PalePink, -9.9f, -3.7f, -7.9f, -2.8f, 0.5f);       // bunk
            End();
        }

        private void BuildUtilities()
        {
            FloorRect r = Utilities;
            BeginRoom("Utilities", r, "Power and water: the generator, the batteries and the water extractor.", "");
            Shell(r, 2.7f, White, DarkRed, 1.1f, new[] { Arch(0f) }, null, null, null);
            Block(Charcoal, 10.2f, -3.7f, 12.8f, -2.5f, 1.6f);       // generator
            Block(Purple, 13.8f, -3.7f, 15.4f, -3f, 1.8f);           // battery bank
            g.Prism(PalePink, 11.5f, 2.9f, 0.9f, 0f, 2.2f);          // water tank
            AddCollider(10.6f, 0f, 2f, 12.4f, 2.2f, 3.8f);
            Block(Mauve, 13.7f, 2.5f, 15.1f, 3.5f, 1.4f);            // water extractor and dispenser
            End();
        }

        private void BuildGreenhouse()
        {
            FloorRect r = Greenhouse;
            BeginRoom("Greenhouse", r,
                "Greenhouse: buy seeds at the kiosk in storage, then sow, water and pick crops to eat or sell.",
                "Regolith Shovel, Hydroponic Sprayer, Work Lamp (tool shed)");
            const float h = 2.7f;
            Opening[] west = { Window(-9.5f, 1.6f), Window(-12.5f, 1.6f), Window(-15.5f, 1.6f) };
            Opening[] east = { Window(-9.5f, 1.6f), Window(-12.5f, 1.6f), Window(-15.5f, 1.6f) };
            Shell(r, h, White, Olive, 1.3f, west, east, null, new[] { Arch(0f) }, false, Back);
            Lamp(0f, h - 0.45f, -15.5f, 8f, 0.7f);
            Lamp(0f, h - 0.45f, -9.5f, 8f, 0.7f);

            // Two raised crop beds with a centre aisle.
            for (int side = -1; side <= 1; side += 2)
            {
                // The printed greenhouse draws the gridded planters (2 x 6 cells each); MarsWorldTasks
                // puts one live plant in each cell.
                float x0 = side < 0 ? -3.26f : 1.34f;
                AddCollider(x0, 0f, -16.26f, x0 + 1.92f, 0.83f, -8.34f);
            }
            // The tool shed at the far end (model: MarsWorldTasks, ToolShed) holds the three garden
            // tools in labelled bays: the sprayer, the shovel on wall hooks in the middle, and the lamp.
            AddCollider(-1.55f, 0f, -18f, 1.55f, 2.45f, -17.95f);   // pegboard
            // Seen from the aisle: sprayer (left, +x), shovel (middle), lamp (right, -x); the model imports mirrored.
            AddCollider(-1.55f, 0f, -18f, 1.55f, 0.42f, -17.5f);    // low platform under the shovel, and the cabinets' base
            AddCollider(0.5f, 0f, -18f, 1.55f, 0.95f, -17.5f);      // sprayer cabinet
            AddCollider(-1.55f, 0f, -18f, -0.5f, 0.95f, -17.5f);    // lamp cabinet
            Tool("hydro_sprayer", 1f, 0.95f, -17.75f, 180f);
            Tool("regolith_shovel", 0f, 0.53f, -17.93f, 0f);   // hanging on the board's two hooks, blade to the aisle
            Tool("work_lamp", -1f, 0.95f, -17.75f, 180f);
            Lamp(0f, 2.2f, -16.9f, 4.5f, 0.9f);   // lights the shed bays
            End();
        }

        private static float Noise(int index, int salt)
        {
            unchecked
            {
                uint n = (uint)(index * 374761393 + salt * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return ((n ^ (n >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private void BuildAirlock()
        {
            FloorRect r = Airlock;
            BeginRoom("Airlock", r,
                "The way in and out. The two doors never open together, so the habitat keeps its air. " +
                "Outside, the suit's own oxygen is all there is.",
                "");
            int inner = doors.Count;   // the back door (to the hub), then the front door (outside)
            Shell(r, 2.5f, White, DarkRed, 0.7f, null, null, new[] { Door(0f) }, new[] { Door(0f) });
            if (doors.Count == inner + 2)
            {
                doors[inner].partner = doors[doors.Count - 1];
                doors[doors.Count - 1].partner = doors[inner];
                // The airlock is only 3.5 m deep: keep the range short so the two doors take turns.
                doors[inner].openRange = doors[doors.Count - 1].openRange = 1.9f;
                doors[inner].secondsToOpen = doors[doors.Count - 1].secondsToOpen = 0.7f;
            }
            // Hazard stripes on the floor and dust-extraction grilles on the walls.
            for (int i = 0; i < 5; i++)
            {
                g.Box(Orange, -1.5f + i * 0.65f, 0f, 16.35f, -1.2f + i * 0.65f, 0.005f, 16.7f);
                g.Box(Orange, -1.5f + i * 0.65f, 0f, 13.3f, -1.2f + i * 0.65f, 0.005f, 13.65f);
            }
            g.Box(Purple, -1.7f, 0.3f, 14.2f, -1.66f, 0.9f, 15.8f);
            g.Box(Purple, 1.66f, 0.3f, 14.2f, 1.7f, 0.9f, 15.8f);
            g.Box(GlowOrange, -0.4f, 2.25f, 16.7f, 0.4f, 2.35f, 16.74f);
            End();
        }

        // ------------------------------------------------------------------ tunnels

        private void BuildTunnels()
        {
            float hub = HubApothem + T;
            Tunnel(true, Storage.x1 + T, -hub);
            Tunnel(true, hub, Utilities.x0 - T);
            Tunnel(false, Greenhouse.z1 + T, -hub);
            Tunnel(false, hub, Airlock.z0 - T);
        }

        /// <summary>
        /// A tube between two modules, running along x or along z through the origin of the
        /// other axis: a flat-floored corridor inside, an eight-sided white tube outside with
        /// dark red collars.
        /// </summary>
        private void Tunnel(bool alongX, float from, float to)
        {
            Begin("Tunnel");
            SetFrame(alongX ? new Vector3(from, 0f, 0f) : new Vector3(0f, 0f, from), alongX ? 90f : 0f);
            float w = TunnelHalf, t = 0.2f, h = TunnelHeight, length = to - from;
            AddCollider(-w - t - 0.1f, -depth, 0f, w + t + 0.1f, 0f, length);
            if (moduled)
            {
                // The printed tunnel module is the tube, deck and lights; only the floor collider stays.
                ClearFrame();
                End();
                return;
            }
            g.Box(Charcoal, -w - t - 0.1f, -depth, 0f, w + t + 0.1f, -0.1f, length);
            g.Box(ClaySand, -w - t, -0.1f, 0f, w + t, 0f, length);
            g.Box(White, -w - t, 0f, 0f, -w, h, length);
            g.Box(White, w, 0f, 0f, w + t, h, length);
            AddCollider(-w - t - 0.3f, 0f, 0f, -w, h, length);
            AddCollider(w, 0f, 0f, w + t + 0.3f, h, length);
            g.Box(White, -w - t, h, 0f, w + t, h + 0.15f, length);
            AddCollider(-w - t, h, 0f, w + t, h + 0.15f, length);
            g.Box(GlowWarm, -0.12f, h - 0.03f, 0.3f, 0.12f, h, length - 0.3f);

            AlsoSolid(() => g.Loft(White, new[] { TubeRing(1.8f, 0f), TubeRing(1.8f, length) }, false, false));
            g.Loft(DarkRed, new[] { TubeRing(1.93f, 0.1f), TubeRing(1.93f, 0.4f) }, false, false);
            g.Loft(DarkRed, new[] { TubeRing(1.93f, length - 0.4f), TubeRing(1.93f, length - 0.1f) }, false, false);
            ClearFrame();
            End();
        }

        private static Vector3[] TubeRing(float radius, float z)
        {
            Vector3[] ring = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI / 4f;
                ring[i] = new Vector3(Mathf.Cos(angle) * radius, 1.2f + Mathf.Sin(angle) * radius, z);
            }
            return ring;
        }

        // ------------------------------------------------------------------ outside

        /// <summary>Local z of the foot of the entry ramp, on the pad.</summary>
        public float RampFootZ { get; private set; }

        private void BuildExterior()
        {
            if (graded)
            {
                BuildGradedGround();
            }
            else
            {
                BuildPadBlock();
            }

            Begin("Entry landing and ramp");
            FloorRect l = Landing;
            g.Box(Charcoal, l.Grow(0.1f), -depth, -0.1f);
            g.Box(ClaySand, l, -0.1f, 0f);
            AddCollider(l.x0 - 0.1f, -depth, l.z0, l.x1 + 0.1f, 0f, l.z1);
            RampFootZ = l.z1 + 3f;
            Ramp(ClaySand, 1.6f, l.z1, 0f, RampFootZ, PadTop - 0.02f, depth, true);

            // Light posts either side of the landing.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 2.15f;
                g.Prism(Charcoal, x, l.z1 - 0.2f, 0.07f, -depth, 1.7f);
                g.Box(GlowWarm, x - 0.12f, 1.7f, l.z1 - 0.32f, x + 0.12f, 1.9f, l.z1 - 0.08f);
            }
            Lamp(0f, 2.2f, l.z1 + 0.5f, 9f, 0.8f);
            End();

            // Solar array in front of the energy room, and a water tank behind life support.
            for (int row = 0; row < 2; row++)
            {
                for (int i = 0; i < 3; i++) SolarPanel(8.5f + i * 3.6f, 7.5f + row * 4.2f);
            }
            Begin("Outdoor tanks");
            g.Prism(White, 17.5f, -5.6f, 1.1f, PadTop - 0.1f, PadTop + 2.2f);
            g.Prism(DarkRed, 17.5f, -5.6f, 1.14f, PadTop + 1.3f, PadTop + 1.6f);
            g.Prism(DarkRed, 17.5f, -5.6f, 0.7f, PadTop + 2.2f, PadTop + 2.5f, 8, 0.4f);
            g.Box(Charcoal, 17.4f, PadTop + 0.4f, -4.6f, 17.6f, PadTop + 0.6f, -2.9f);
            AddCollider(16.4f, PadTop - 0.1f, -6.7f, 18.6f, PadTop + 2.2f, -4.5f);
            // A pair of small dark red tanks beside storage.
            SmallTanks(-17.6f, 4.45f, -1f);
            End();
        }

        /// <summary>
        /// The pad on graded terrain: the ground is already flat at <see cref="PadTop"/>, so this adds
        /// the walkable collider, sintered regolith paving where the rover parks and in front of the
        /// airlock, a sintered path out over the shoulder, and tyre tracks leading off into the plain.
        /// </summary>
        private void BuildGradedGround()
        {
            Begin("Graded pad");
            AddCollider(Pad.x0, PadTop - padDepth, Pad.z0, Pad.x1, PadTop, Pad.z1);

            // Sintered paving: microwave-fused regolith tiles with narrow joints.
            Pavers(-14f, 10f, -5f, 21f, 1.5f);   // rover parking
            Pavers(-3f, 18.5f, 3f, 24f, 1.5f);   // apron at the foot of the entry ramp

            // Sintered path from the apron straight out across the shoulder.
            var path = new List<Vector2>();
            for (float z = 24f; z <= Pad.z1 + 14f; z += 1f) path.Add(new Vector2(0f, z));
            Strip(Sintered, path, 1.2f, 0.035f);

            // Tyre tracks: the rover leaves the parking bay, swings out of the pad's front left corner
            // and heads off across the plain; a second, fainter pair loops back in.
            TyreTracks(new Vector2(-9.5f, 19.5f), new Vector2(-9.5f, 32f), new Vector2(-24f, 40f), new Vector2(-46f, 58f));
            TyreTracks(new Vector2(-8.6f, 20f), new Vector2(-12f, 36f), new Vector2(-30f, 38f), new Vector2(-58f, 44f));
            End();
        }

        /// <summary>Square sintered tiles filling a rectangle on the pad, with joints between them.</summary>
        private void Pavers(float x0, float z0, float x1, float z1, float tile)
        {
            const float joint = 0.06f;
            for (float x = x0; x < x1 - 0.01f; x += tile)
            {
                for (float z = z0; z < z1 - 0.01f; z += tile)
                {
                    // Slight height jitter so the paving reads as fused blocks, not a painted floor.
                    float h = 0.025f + 0.012f * Mathf.Repeat(Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.55f, 1f);
                    g.Box(Sintered, x + joint, PadTop - 0.05f, z + joint, Mathf.Min(x + tile, x1) - joint, PadTop + h,
                        Mathf.Min(z + tile, z1) - joint);
                }
            }
        }

        /// <summary>A flat ribbon laid on the ground along a polyline (outpost-local x, z).</summary>
        private void Strip(int material, List<Vector2> points, float halfWidth, float lift)
        {
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * halfWidth;
                Vector3 p0 = OnGround(a - side, lift), p1 = OnGround(a + side, lift);
                Vector3 p2 = OnGround(b + side, lift), p3 = OnGround(b - side, lift);
                Vector3 below = (p0 + p1 + p2 + p3) * 0.25f - Vector3.up;
                g.Quad(material, p0, p1, p2, p3, below);
            }
        }

        private Vector3 OnGround(Vector2 xz, float lift)
        {
            float y = Pad.Contains(xz.x, xz.y) ? PadTop : groundAt(xz.x, xz.y);
            return new Vector3(xz.x, y + lift, xz.y);
        }

        /// <summary>Two wheel ruts along a cubic Bezier, 1.7 m apart.</summary>
        private void TyreTracks(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            var left = new List<Vector2>();
            var right = new List<Vector2>();
            const int steps = 60;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1f - t;
                Vector2 p = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
                Vector2 d = 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
                Vector2 side = new Vector2(-d.y, d.x).normalized * 0.85f;
                left.Add(p - side);
                right.Add(p + side);
            }
            Strip(Rut, left, 0.13f, 0.012f);
            Strip(Rut, right, 0.13f, 0.012f);
        }

        /// <summary>The block pad used where the terrain cannot be graded: flat top, sloping sides.</summary>
        private void BuildPadBlock()
        {
            // The levelled pad: regolith graded flat, with sloping sides down to the natural ground.
            Begin("Levelled pad");
            float spread = padDepth * 1.6f;
            AlsoSolid(() => g.Frustum(Terracotta, -padDepth, Pad.Grow(spread), PadTop, Pad));
            AddCollider(Pad.x0, -padDepth, Pad.z0, Pad.x1, PadTop, Pad.z1);

            // If the front edge of the pad stands above the ground, an apron ramp leads up onto it.
            float edge = Pad.z1;
            if (PadTop - groundAt(0f, edge + 0.5f) > 0.3f)
            {
                float run = 3f, foot = PadTop - 0.5f;
                for (int i = 0; i < 6; i++)
                {
                    foot = Mathf.Min(PadTop - 0.2f, groundAt(0f, edge + run) - 0.15f);
                    run = Mathf.Clamp((PadTop - foot) / Mathf.Tan(10f * Mathf.Deg2Rad), 3f, 45f);
                }
                Ramp(Terracotta, 2.5f, edge, PadTop, edge + run, foot, padDepth, false);
            }
            End();
        }

        /// <summary>Two small tanks side by side, piped to the wall on the given z side (-1 or +1).</summary>
        private void SmallTanks(float x, float z, float wallSide)
        {
            for (int i = 0; i < 2; i++)
            {
                float cx = x + i * 1.1f;
                g.Prism(DarkRed, cx, z, 0.45f, PadTop - 0.05f, PadTop + 1.45f);
                g.Prism(Charcoal, cx, z, 0.3f, PadTop + 1.45f, PadTop + 1.65f, 8, 0.2f);
                g.Box(Charcoal, cx - 0.06f, PadTop + 1.0f, Mathf.Min(z, z + wallSide * 1.2f), cx + 0.06f, PadTop + 1.12f,
                    Mathf.Max(z, z + wallSide * 1.2f));
            }
            AddCollider(x - 0.5f, PadTop - 0.05f, z - 0.5f, x + 1.6f, PadTop + 1.45f, z + 0.5f);
        }

        /// <summary>
        /// A ramp along +Z from (z0, y0) down to (z1, y1), with a collider the astronaut walks on.
        /// </summary>
        private void Ramp(int material, float half, float z0, float y0, float z1, float y1, float below, bool stripes)
        {
            g.Hexa(material,
                new Vector3(-half, -below, z0), new Vector3(half, -below, z0),
                new Vector3(half, y1 - 0.8f, z1), new Vector3(-half, y1 - 0.8f, z1),
                new Vector3(-half, y0, z0), new Vector3(half, y0, z0),
                new Vector3(half, y1, z1), new Vector3(-half, y1, z1));
            for (int side = -1; stripes && side <= 1; side += 2)
            {
                float a = side * half, b = side * (half - 0.12f);
                g.Hexa(Orange,
                    new Vector3(a, y0 - 0.02f, z0), new Vector3(b, y0 - 0.02f, z0),
                    new Vector3(b, y1 - 0.02f, z1), new Vector3(a, y1 - 0.02f, z1),
                    new Vector3(a, y0 + 0.01f, z0), new Vector3(b, y0 + 0.01f, z0),
                    new Vector3(b, y1 + 0.01f, z1), new Vector3(a, y1 + 0.01f, z1));
            }
            float run = z1 - z0, drop = y0 - y1;
            GameObject ramp = new GameObject("Ramp Collider");
            ramp.transform.SetParent(current.transform, false);
            ramp.transform.localRotation = Quaternion.Euler(Mathf.Atan2(drop, run) * Mathf.Rad2Deg, 0f, 0f);
            Vector3 normal = ramp.transform.localRotation * Vector3.up;
            ramp.transform.localPosition = new Vector3(0f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f) - normal * 0.25f;
            ramp.AddComponent<BoxCollider>().size = new Vector3(half * 2f, 0.5f, Mathf.Sqrt(run * run + drop * drop));
        }

        private void SolarPanel(float x, float z)
        {
            if (panelMesh == null)
            {
                OutpostGeometry panel = new OutpostGeometry();
                panel.Box(White, -1.55f, -0.04f, -0.95f, 1.55f, 0.02f, 0.95f);
                panel.Box(SolarBlue, -1.5f, 0.02f, -0.9f, 1.5f, 0.05f, 0.9f);
                for (int i = 1; i < 4; i++) panel.Box(White, -1.5f + i * 0.75f - 0.02f, 0.05f, -0.9f, -1.5f + i * 0.75f + 0.02f, 0.06f, 0.9f);
                panel.Box(White, -1.5f, 0.05f, -0.02f, 1.5f, 0.06f, 0.02f);
                panelMesh = panel.ToMesh("Solar Panel", materials, out panelMaterials);
                owned.Add(panelMesh);
            }
            const float ground = PadTop;
            Begin("Solar panel");
            g.Prism(Charcoal, x, z, 0.09f, ground - 0.4f, ground + 1.15f);
            g.Box(Charcoal, x - 0.35f, ground - 0.2f, z - 0.35f, x + 0.35f, ground + 0.08f, z + 0.35f);
            AddCollider(x - 0.35f, ground - 0.4f, z - 0.35f, x + 0.35f, ground + 1.15f, z + 0.35f);
            GameObject top = new GameObject("Panel");
            top.transform.SetParent(current.transform, false);
            top.transform.localPosition = new Vector3(x, ground + 1.2f, z);
            top.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            top.AddComponent<MeshFilter>().sharedMesh = panelMesh;
            top.AddComponent<MeshRenderer>().sharedMaterials = panelMaterials;
            top.AddComponent<BoxCollider>().size = new Vector3(3.1f, 0.1f, 1.9f);
            End();
        }
    }
}
