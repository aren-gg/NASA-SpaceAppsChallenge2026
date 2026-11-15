using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// Procedural low-poly astronaut, 1.8 m tall, built in metres with the feet at the
    /// local origin and +Z forward. Made for a first-person camera at eye height:
    /// the arms, pelvis and legs are drawn; the chest, backpack and helmet only cast
    /// shadows, so they never block or clip the view.
    ///
    /// The suit uses four small generated pixel textures (white panelled fabric, dark
    /// joint fabric, blue-grey trim, glove/boot) at 64 pixels per metre, point filtered.
    /// Idle, walk, sprint and airborne poses are computed in <see cref="Animate"/>.
    /// </summary>
    public sealed class MarsAstronautBody : MonoBehaviour
    {
        private const int Suit = 0, Dark = 1, Trim = 2, Boot = 3;
        private const int TextureSize = 32;
        private const float PixelsPerMeter = 64f;
        // Widens every part. Parts grow sideways and backwards only: their front faces stay
        // where they were and the arms sit further out, so the view is no more blocked.
        private const float Bulk = 1.22f;

        private const float HipHeight = 0.92f;
        private const float ThighLength = 0.42f;
        private const float UpperArmLength = 0.28f;
        /// <summary>Elbow to the middle of the glove, where a held handle sits.</summary>
        private const float GripDistance = 0.32f;

        private readonly List<Object> owned = new List<Object>();
        private Material[] materials;
        private Transform hips, spine;
        private Transform thighL, thighR, shinL, shinR;
        private Transform upperArmL, upperArmR, forearmL, forearmR;
        private bool built;

        // ------------------------------------------------------------------ build

        public void Build()
        {
            if (built) return;
            built = true;
            CreateMaterials();

            hips = Node("Hips", transform, new Vector3(0f, HipHeight, 0f));
            PartBuilder pelvis = new PartBuilder();
            pelvis.Frustum(Suit, 0.22f, -0.06f, new Vector2(0.40f, 0.24f), new Vector2(0.36f, 0.22f));
            pelvis.Frustum(Trim, 0.22f, 0.17f, new Vector2(0.41f, 0.25f), new Vector2(0.41f, 0.25f));
            Attach(hips, "Pelvis", pelvis, ShadowCastingMode.On);

            BuildLeg(-1f, out thighL, out shinL);
            BuildLeg(1f, out thighR, out shinR);

            spine = Node("Spine", hips, new Vector3(0f, 0.22f, 0f));
            PartBuilder torso = new PartBuilder();
            torso.Frustum(Suit, 0.36f, 0f, new Vector2(0.46f, 0.28f), new Vector2(0.40f, 0.24f));
            torso.Frustum(Suit, 0.45f, 0.02f, new Vector2(0.36f, 0.18f), new Vector2(0.36f, 0.18f),
                new Vector2(0f, -0.2f), new Vector2(0f, -0.2f));
            torso.Frustum(Suit, 0.66f, 0.36f, new Vector2(0.26f, 0.28f), new Vector2(0.32f, 0.32f));
            Attach(spine, "Torso And Helmet (shadow only)", torso, ShadowCastingMode.ShadowsOnly);

            BuildArm(-1f, out upperArmL, out forearmL);
            BuildArm(1f, out upperArmR, out forearmR);

            Animate(0f, 0f, 0f, 0f, 0f, 0f);
        }

        private void BuildLeg(float side, out Transform thigh, out Transform shin)
        {
            string label = side < 0f ? " L" : " R";
            thigh = Node("Thigh" + label, hips, new Vector3(0.112f * side, 0f, 0f));
            PartBuilder t = new PartBuilder();
            t.Frustum(Suit, 0.02f, -ThighLength, new Vector2(0.17f, 0.19f), new Vector2(0.15f, 0.16f));
            t.Frustum(Trim, -0.20f, -0.23f, new Vector2(0.168f, 0.182f), new Vector2(0.166f, 0.18f));
            Attach(thigh, "Mesh", t, ShadowCastingMode.On);

            shin = Node("Shin" + label, thigh, new Vector3(0f, -ThighLength, 0f));
            PartBuilder s = new PartBuilder();
            s.Frustum(Dark, 0.03f, -0.07f, new Vector2(0.158f, 0.168f), new Vector2(0.158f, 0.168f));
            s.Frustum(Suit, -0.07f, -0.37f, new Vector2(0.15f, 0.16f), new Vector2(0.165f, 0.185f));
            s.Frustum(Trim, -0.33f, -0.37f, new Vector2(0.172f, 0.192f), new Vector2(0.175f, 0.195f));
            // Boot: wider at the sole and pushed forward for the toe.
            s.Frustum(Boot, -0.37f, -0.47f, new Vector2(0.17f, 0.20f), new Vector2(0.18f, 0.30f),
                Vector2.zero, new Vector2(0f, 0.045f));
            s.Frustum(Dark, -0.47f, -0.50f, new Vector2(0.185f, 0.305f), new Vector2(0.185f, 0.305f),
                new Vector2(0f, 0.045f), new Vector2(0f, 0.045f));
            Attach(shin, "Mesh", s, ShadowCastingMode.On);
        }

        private void BuildArm(float side, out Transform upperArm, out Transform forearm)
        {
            string label = side < 0f ? " L" : " R";
            upperArm = Node("Upper Arm" + label, spine, new Vector3(0.315f * side, 0.28f, 0f));
            PartBuilder u = new PartBuilder();
            u.Frustum(Suit, 0.06f, -0.06f, new Vector2(0.15f, 0.16f), new Vector2(0.15f, 0.16f));
            u.Frustum(Suit, -0.06f, -UpperArmLength, new Vector2(0.13f, 0.14f), new Vector2(0.115f, 0.12f));
            u.Frustum(Trim, -0.13f, -0.16f, new Vector2(0.134f, 0.142f), new Vector2(0.132f, 0.14f));
            Attach(upperArm, "Mesh", u, ShadowCastingMode.On);

            forearm = Node("Forearm" + label, upperArm, new Vector3(0f, -UpperArmLength, 0f));
            PartBuilder f = new PartBuilder();
            f.Frustum(Dark, 0.03f, -0.05f, new Vector2(0.122f, 0.128f), new Vector2(0.122f, 0.128f));
            f.Frustum(Suit, -0.05f, -0.24f, new Vector2(0.11f, 0.115f), new Vector2(0.10f, 0.105f));
            f.Frustum(Trim, -0.23f, -0.26f, new Vector2(0.112f, 0.117f), new Vector2(0.112f, 0.117f));
            // Glove with a thumb on the inner side.
            f.Frustum(Boot, -0.26f, -0.38f, new Vector2(0.095f, 0.105f), new Vector2(0.08f, 0.075f));
            f.Frustum(Boot, -0.27f, -0.33f, new Vector2(0.035f, 0.04f), new Vector2(0.03f, 0.035f),
                new Vector2(-0.06f * side, 0.03f), new Vector2(-0.07f * side, 0.045f));
            Attach(forearm, "Mesh", f, ShadowCastingMode.On);
        }

        private static Transform Node(string nodeName, Transform parent, Vector3 localPosition)
        {
            Transform node = new GameObject(nodeName).transform;
            node.SetParent(parent, false);
            node.localPosition = localPosition;
            return node;
        }

        private void Attach(Transform parent, string partName, PartBuilder part, ShadowCastingMode shadows)
        {
            GameObject go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            Mesh mesh = part.ToMesh(materials, out Material[] used);
            mesh.name = parent.name + " " + partName;
            owned.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = used;
            renderer.shadowCastingMode = shadows;
        }

        private void OnDestroy()
        {
            foreach (Object o in owned)
            {
                if (o != null) Destroy(o);
            }
            owned.Clear();
        }

        // ------------------------------------------------------------------ materials

        private void CreateMaterials()
        {
            materials = new Material[4];
            materials[Suit] = SuitMaterial("Astronaut Suit", new Color(0.93f, 0.92f, 0.96f), 0.05f,
                new Color(0.62f, 0.68f, 0.79f), 16, 32);
            materials[Dark] = SuitMaterial("Astronaut Joint", new Color(0.43f, 0.39f, 0.48f), 0.05f,
                new Color(0.34f, 0.31f, 0.40f), 4, 0);
            materials[Trim] = SuitMaterial("Astronaut Trim", new Color(0.60f, 0.66f, 0.78f), 0.04f,
                new Color(0.50f, 0.56f, 0.69f), 0, 0);
            materials[Boot] = SuitMaterial("Astronaut Glove", new Color(0.86f, 0.86f, 0.91f), 0.04f,
                new Color(0.66f, 0.69f, 0.78f), 8, 0);
        }

        /// <summary>
        /// A mottled pixel texture with seam lines every <paramref name="rowSpacing"/> pixels
        /// horizontally and every <paramref name="columnSpacing"/> pixels vertically (0 = none).
        /// </summary>
        private Material SuitMaterial(string materialName, Color baseColor, float mottle, Color seam,
            int rowSpacing, int columnSpacing)
        {
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = materialName;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            Color32[] pixels = new Color32[TextureSize * TextureSize];
            int seed = materialName.Length * 7919;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // Blocky 2x2 mottling, slightly lavender in the darker patches.
                    float noise = Hash(x / 2, y / 2, seed) - 0.5f;
                    Color c = baseColor;
                    c.r += noise * mottle * 2.2f;
                    c.g += noise * mottle * 2.4f;
                    c.b += noise * mottle * 1.4f;
                    bool onRow = rowSpacing > 0 && y % rowSpacing == 0;
                    bool onColumn = columnSpacing > 0 && x % columnSpacing == 0;
                    if (onRow || onColumn) c = Color.Lerp(seam, c, 0.15f);
                    c.a = 1f;
                    pixels[y * TextureSize + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            owned.Add(texture);

            Material material = MarsPalette.CreateMatteMaterial(materialName, Color.white);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            owned.Add(material);
            return material;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // ------------------------------------------------------------------ holding things

        public bool IsBuilt { get { return built; } }

        /// <summary>World position of a shoulder joint.</summary>
        public Vector3 ShoulderPosition(bool right)
        {
            return (right ? upperArmR : upperArmL).position;
        }

        /// <summary>Shoulder-to-glove distance with the arm straight, in world units.</summary>
        public float ArmReach(bool right)
        {
            return (UpperArmLength + GripDistance) * (right ? upperArmR : upperArmL).lossyScale.y;
        }

        /// <summary>
        /// Bends an arm (two-bone IK) so the middle of the glove sits on <paramref name="grip"/>.
        /// The elbow bends toward <paramref name="elbowHint"/>; the fist turns so the handle
        /// (<paramref name="handleUp"/>, pointing to the tool's head) runs through it with the
        /// thumb toward the head. Call after <see cref="Animate"/> (it overrides that arm's pose).
        /// </summary>
        public void ReachArm(bool right, Vector3 grip, Vector3 handleUp, Vector3 elbowHint)
        {
            if (!built) return;
            Transform upper = right ? upperArmR : upperArmL;
            Transform fore = right ? forearmR : forearmL;
            float scale = upper.lossyScale.y;
            float a = UpperArmLength * scale, b = GripDistance * scale;
            Vector3 shoulder = upper.position;
            Vector3 toGrip = grip - shoulder;
            float distance = toGrip.magnitude;
            if (distance < 1e-5f) return;
            Vector3 dir = toGrip / distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.9999f);

            // Elbow on the circle where the two bones meet, on the side of the hint.
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            float outward = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 hint = elbowHint - shoulder;
            Vector3 bend = hint - dir * Vector3.Dot(hint, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.down - dir * Vector3.Dot(Vector3.down, dir);
            bend.Normalize();
            Vector3 elbow = shoulder + dir * along + bend * outward;
            Vector3 hand = shoulder + dir * distance;

            // Limbs hang along local -Y; the forearm folds toward the upper arm's local +Z.
            Vector3 upperDir = (elbow - shoulder).normalized;
            Vector3 foreDir = (hand - elbow).normalized;
            Vector3 fold = foreDir - upperDir * Vector3.Dot(foreDir, upperDir);
            if (fold.sqrMagnitude < 1e-8f) fold = -bend;
            upper.rotation = Quaternion.LookRotation(fold.normalized, -upperDir);

            // The thumb is on the glove's inner side (-X on the right hand, +X on the left):
            // point it along the handle toward the tool's head.
            Vector3 across = (right ? -handleUp : handleUp);
            across -= foreDir * Vector3.Dot(across, foreDir);
            Vector3 front = across.sqrMagnitude > 1e-6f
                ? Vector3.Cross(across.normalized, -foreDir)
                : fold - foreDir * Vector3.Dot(fold, foreDir);
            if (front.sqrMagnitude < 1e-8f) front = upper.forward;
            fore.rotation = Quaternion.LookRotation(front.normalized, -foreDir);
        }

        // ------------------------------------------------------------------ animation

        /// <summary>
        /// Poses the body. <paramref name="phase"/> is the stride cycle in radians (one
        /// full turn = a left and a right step). <paramref name="move"/> blends idle to
        /// walk, <paramref name="sprint"/> walk to sprint and <paramref name="air"/> to the
        /// airborne pose; each is 0-1. <paramref name="vertical"/> is the vertical speed as
        /// a fraction of the take-off speed (1 leaving the ground, 0 at the top, -1 coming
        /// down), which moves the airborne pose from push-off through tuck to reaching
        /// for the ground.
        /// </summary>
        public void Animate(float phase, float move, float sprint, float air, float vertical, float time)
        {
            if (!built) return;
            float breathe = Mathf.Sin(time * 1.6f);

            float rise = Mathf.Clamp01(vertical), fall = Mathf.Clamp01(-vertical);
            PoseLeg(thighL, shinL, phase, move, sprint, air, rise, fall, 1f);
            PoseLeg(thighR, shinR, phase + Mathf.PI, move, sprint, air, rise, fall, 0.45f);
            PoseArm(upperArmL, forearmL, -1f, phase, move, sprint, air, rise, fall, breathe);
            PoseArm(upperArmR, forearmR, 1f, phase + Mathf.PI, move, sprint, air, rise, fall, breathe);

            // Lean into the run; hips drop slightly on each footfall.
            float lean = Mathf.Lerp(2.5f * move, 11f, sprint) * (1f - air) - 4f * air;
            spine.localRotation = Quaternion.Euler(lean, 0f, 0f);
            float bob = Mathf.Lerp(0.012f, 0.035f, sprint) * move * (1f - air);
            hips.localPosition = new Vector3(0f, HipHeight - bob * (0.5f + 0.5f * Mathf.Cos(2f * phase)), 0f);
        }

        // Negative X rotation swings a hanging limb forward; positive knee angle folds the shin back.
        private static void PoseLeg(Transform thigh, Transform shin, float phase, float move, float sprint,
            float air, float rise, float fall, float lead)
        {
            float s = Mathf.Sin(phase);
            float swing = Mathf.Max(0f, Mathf.Cos(phase)); // the leg is travelling forward

            float walkThigh = -22f * s;
            float walkKnee = 4f + 36f * swing;
            float sprintThigh = -14f - 44f * s;
            float sprintKnee = 16f + 84f * Mathf.Max(0f, Mathf.Cos(phase - 0.35f));

            float thighAngle = Mathf.Lerp(Mathf.Lerp(0f, walkThigh, move), sprintThigh, sprint);
            float kneeAngle = Mathf.Lerp(Mathf.Lerp(2f, walkKnee, move), sprintKnee, sprint);
            // Airborne: legs straight at push-off, tucked at the top, reaching down to land.
            float airThigh = Blend3(6f, -38f * lead, -12f * lead, rise, fall);
            float airKnee = Blend3(6f, 12f + 62f * lead, 22f, rise, fall);
            thighAngle = Mathf.Lerp(thighAngle, airThigh, air);
            kneeAngle = Mathf.Lerp(kneeAngle, airKnee, air);

            thigh.localRotation = Quaternion.Euler(thighAngle, 0f, 0f);
            shin.localRotation = Quaternion.Euler(kneeAngle, 0f, 0f);
        }

        // The arm on one side swings opposite to the leg on that side.
        private static void PoseArm(Transform upperArm, Transform forearm, float side, float phase, float move,
            float sprint, float air, float rise, float fall, float breathe)
        {
            float s = Mathf.Sin(phase);
            float forward = Mathf.Max(0f, -s);

            float idleShoulder = 2f + breathe * 1.2f;
            float walkShoulder = -2f + 20f * s;
            float walkElbow = -(14f + 12f * forward);
            float sprintShoulder = -8f + 48f * s;      // fists pump up into view
            float sprintElbow = -(95f - 15f * s);

            float shoulderAngle = Mathf.Lerp(Mathf.Lerp(idleShoulder, walkShoulder, move), sprintShoulder, sprint);
            float elbowAngle = Mathf.Lerp(Mathf.Lerp(-10f, walkElbow, move), sprintElbow, sprint);
            float spread = Mathf.Lerp(6f, 4f, sprint);  // the suit holds the arms off the body

            // Airborne: arms throw up at push-off, then spread for balance on the way down.
            shoulderAngle = Mathf.Lerp(shoulderAngle, Blend3(-55f, -30f, -15f, rise, fall), air);
            elbowAngle = Mathf.Lerp(elbowAngle, Blend3(-40f, -55f, -35f, rise, fall), air);
            spread = Mathf.Lerp(spread, Blend3(10f, 26f, 34f, rise, fall), air);

            upperArm.localRotation = Quaternion.Euler(shoulderAngle, 0f, spread * side);
            forearm.localRotation = Quaternion.Euler(elbowAngle, 0f, 0f);
        }

        /// <summary>The apex value, pulled toward the rising or the falling value.</summary>
        private static float Blend3(float rising, float apex, float falling, float rise, float fall)
        {
            return Mathf.Lerp(Mathf.Lerp(apex, rising, rise), falling, fall);
        }

        // ------------------------------------------------------------------ mesh helper

        /// <summary>Collects flat-shaded tapered boxes into one mesh with a submesh per material.</summary>
        private sealed class PartBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            /// <summary>
            /// A box between two heights whose top and bottom rectangles (x, z sizes) can
            /// differ and be offset in x, z.
            /// </summary>
            public void Frustum(int material, float yTop, float yBottom, Vector2 topSize, Vector2 bottomSize,
                Vector2 topOffset = default(Vector2), Vector2 bottomOffset = default(Vector2))
            {
                // Thicken without moving the front face forward (see Bulk).
                topOffset = new Vector2(topOffset.x * Bulk, topOffset.y - topSize.y * (Bulk - 1f) * 0.5f);
                bottomOffset = new Vector2(bottomOffset.x * Bulk, bottomOffset.y - bottomSize.y * (Bulk - 1f) * 0.5f);
                topSize *= Bulk;
                bottomSize *= Bulk;
                Vector3[] t = Ring(yTop, topSize, topOffset);
                Vector3[] b = Ring(yBottom, bottomSize, bottomOffset);
                Vector3 centre = Vector3.zero;
                for (int i = 0; i < 4; i++) centre += (t[i] + b[i]) * 0.125f;

                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Quad(material, b[i], b[j], t[j], t[i], centre);
                }
                Quad(material, t[0], t[1], t[2], t[3], centre);
                Quad(material, b[0], b[1], b[2], b[3], centre);
            }

            private static Vector3[] Ring(float y, Vector2 size, Vector2 offset)
            {
                float hx = size.x * 0.5f, hz = size.y * 0.5f;
                return new[]
                {
                    new Vector3(offset.x - hx, y, offset.y - hz),
                    new Vector3(offset.x + hx, y, offset.y - hz),
                    new Vector3(offset.x + hx, y, offset.y + hz),
                    new Vector3(offset.x - hx, y, offset.y + hz),
                };
            }

            // a-b is the bottom edge, so texture rows run along it. The winding is chosen
            // so the face points away from the box centre.
            private void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 boxCentre)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                bool flip = Vector3.Dot(normal, (a + b + c + d) * 0.25f - boxCentre) < 0f;
                if (flip) normal = -normal;

                Vector3 uAxis = (b - a).normalized;
                Vector3 vAxis = Vector3.Cross(uAxis, normal).normalized;
                if (Vector3.Dot(vAxis, d - a) < 0f) vAxis = -vAxis;
                float tilesPerMeter = PixelsPerMeter / TextureSize;

                int start = vertices.Count;
                Vector3[] corners = { a, b, c, d };
                for (int i = 0; i < 4; i++)
                {
                    vertices.Add(corners[i]);
                    normals.Add(normal);
                    Vector3 offset = corners[i] - a;
                    uvs.Add(new Vector2(Vector3.Dot(offset, uAxis), Vector3.Dot(offset, vAxis)) * tilesPerMeter);
                }
                List<int> list = triangles[material];
                if (flip)
                {
                    list.Add(start); list.Add(start + 2); list.Add(start + 1);
                    list.Add(start); list.Add(start + 3); list.Add(start + 2);
                }
                else
                {
                    list.Add(start); list.Add(start + 1); list.Add(start + 2);
                    list.Add(start); list.Add(start + 2); list.Add(start + 3);
                }
            }

            public Mesh ToMesh(Material[] allMaterials, out Material[] usedMaterials)
            {
                Mesh mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                List<Material> used = new List<Material>();
                int count = 0;
                for (int i = 0; i < triangles.Length; i++)
                {
                    if (triangles[i].Count > 0) count++;
                }
                mesh.subMeshCount = count;
                int subMesh = 0;
                for (int i = 0; i < triangles.Length; i++)
                {
                    if (triangles[i].Count == 0) continue;
                    mesh.SetTriangles(triangles[i], subMesh++);
                    used.Add(allMaterials[i]);
                }
                mesh.RecalculateBounds();
                usedMaterials = used.ToArray();
                return mesh;
            }
        }
    }
}
