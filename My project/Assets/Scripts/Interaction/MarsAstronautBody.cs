using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// Procedural blocky astronaut, about 1.9 m to the top of the head, built in metres with the
    /// feet at the local origin and +Z forward. The style follows classic blocky avatars (Roblox's
    /// R6 rig, voxel characters): every part is a box - a cube head with a flat pixel face, a slab
    /// torso, square arms and legs - in flat colours with hard edges. Built as a modular template,
    /// the way CC0 modular character kits are organised: one fixed skeleton (hips, spine, two-bone arms and legs, animated by
    /// <see cref="Animate"/>) carrying swappable mesh slots - pelvis, torso, arms, legs, head
    /// and face, hair, helmet and backpack. Every slot reads the player's
    /// <see cref="AstronautLook"/> (body frame, skin, hair, suit and accent colours, visor,
    /// helmet and backpack), so the wardrobe only changes that look and calls
    /// <see cref="Apply"/>; adding an item means one entry in AstronautLook and one case here.
    ///
    /// Suit details after NASA's xEMU, in blocks: a chest control box, joint bands, accent ID
    /// bands, gloves, boots, a life-support pack, and a box helmet with a sun visor and lamps.
    /// The helmet comes off indoors (optional), showing the face and hair.
    ///
    /// The arms, pelvis and legs are always drawn. The torso, backpack, head, hair and helmet
    /// only cast shadows in first person (they would block the view) and are drawn in third
    /// person (<see cref="SetHeadVisible"/>).
    /// </summary>
    public sealed class MarsAstronautBody : MonoBehaviour
    {
        // Material slots (one solid colour each).
        private const int Suit = 0, Joint = 1, Accent = 2, Trim = 3, Glove = 4, Boot = 5, Visor = 6,
            Skin = 7, Hair = 8, Eye = 9, Pack = 10, Screen = 11, Lamp = 12, Lip = 13;
        private const int SlotCount = 14;

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
        /// <summary>The skinned astronaut, when its models are present; null for the procedural blocky body.</summary>
        private AstronautRig rig;
        private AstronautLook look;

        // Renderers that only cast shadows in first person, and the groups the helmet toggles.
        private readonly List<MeshRenderer> upper = new List<MeshRenderer>();
        private GameObject helmetPart, headPart, hairPart, hairExtrasPart;
        private bool headVisible = true, helmetOn = true;

        /// <summary>Which look the body was last built with (a copy).</summary>
        public AstronautLook Look { get { return look; } }

        /// <summary>
        /// Draws the torso, backpack, head and helmet for a third-person camera. In first person
        /// they only cast shadows so they never block the view.
        /// </summary>
        public void SetHeadVisible(bool visible)
        {
            headVisible = visible;
            if (rig != null) { rig.SetHeadVisible(visible); return; }
            ShadowCastingMode mode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (MeshRenderer r in upper)
            {
                if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
            }
        }

        /// <summary>The helmet stays sealed at all times, indoors and out; the call is kept for callers.</summary>
        public void SetHelmet(bool on)
        {
            helmetOn = on = true;
            if (rig != null) return;
            bool clear = look != null && look.ClearVisor;
            if (helmetPart != null) helmetPart.SetActive(on);
            if (headPart != null) headPart.SetActive(!on || clear);
            if (hairPart != null) hairPart.SetActive(!on || clear);
            // Ponytails, buns and long hair would poke through the helmet.
            if (hairExtrasPart != null) hairExtrasPart.SetActive(!on);
        }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            if (built) return;
            Apply(AstronautLook.Current);
        }

        /// <summary>(Re)builds every mesh slot for a look. The skeleton and animation carry on unchanged.</summary>
        public void Apply(AstronautLook newLook)
        {
            look = (newLook ?? new AstronautLook()).Clone();
            Clear();
            built = true;
            // The skinned NASA-suit astronaut (from Blender) when its model is in Resources.
            if (AstronautRig.Available())
            {
                rig = new AstronautRig(transform, look);
                if (rig.Usable)
                {
                    rig.SetHeadVisible(headVisible);
                    return;
                }
                Debug.LogWarning("Astronaut: Resources/Astronaut model is not Humanoid; using the blocky astronaut.");
                rig.Destroy();
                rig = null;
            }
            CreateMaterials();
            bool frameB = AstronautLook.Mod(look.frame, AstronautLook.Frames.Length) == 1;

            hips = Node("Hips", transform, new Vector3(0f, HipHeight, 0f));
            AlwaysShown(hips, "Pelvis", BuildPelvis(frameB));
            BuildLeg(-1f, frameB, out thighL, out shinL);
            BuildLeg(1f, frameB, out thighR, out shinR);

            spine = Node("Spine", hips, new Vector3(0f, 0.22f, 0f));
            Upper(spine, "Torso", BuildTorso(frameB));
            Upper(spine, "Backpack", BuildBackpack(look.backpack, frameB));
            Transform headRoot = spine;
            headPart = Upper(headRoot, "Head", BuildHead(frameB)).gameObject;
            PartBuilder extras = new PartBuilder();
            hairPart = Upper(headRoot, "Hair", BuildHair(look.hairStyle, extras)).gameObject;
            hairExtrasPart = Upper(headRoot, "Hair Extras", extras).gameObject;
            helmetPart = Upper(spine, "Helmet", BuildHelmet(look.helmet)).gameObject;

            float shoulder = frameB ? 0.26f : 0.285f;
            BuildArm(-1f, shoulder, frameB, out upperArmL, out forearmL);
            BuildArm(1f, shoulder, frameB, out upperArmR, out forearmR);

            SetHeadVisible(headVisible);
            SetHelmet(helmetOn);
            Animate(0f, 0f, 0f, 0f, 0f, 0f);
        }

        private void Clear()
        {
            if (rig != null) { rig.Destroy(); rig = null; }
            upper.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            foreach (Object o in owned)
            {
                if (o != null) Destroy(o);
            }
            owned.Clear();
        }

        private void OnEnable() { AstronautLook.Changed += OnLookChanged; }
        private void OnDisable() { AstronautLook.Changed -= OnLookChanged; }
        private void OnLookChanged() { if (built) Apply(AstronautLook.Current); }

        // ------------------------------------------------------------------ slots
        // Every part is a box (centre, size) in its bone's space, in metres. Proportions follow
        // the classic blocky avatar (Roblox R6 / voxel characters): a cube head, a slab torso and
        // square limbs, with flat colours and hard edges.

        private static PartBuilder BuildPelvis(bool frameB)
        {
            PartBuilder b = new PartBuilder();
            b.Box(Suit, new Vector3(0f, 0.04f, 0f), new Vector3(frameB ? 0.42f : 0.38f, 0.22f, 0.23f));
            b.Box(Joint, new Vector3(0f, 0.12f, 0f), new Vector3(frameB ? 0.43f : 0.39f, 0.05f, 0.24f));   // belt
            b.Box(Trim, new Vector3(0f, 0.12f, 0.122f), new Vector3(0.07f, 0.04f, 0.012f));                // buckle
            return b;
        }

        private static PartBuilder BuildTorso(bool frameB)
        {
            PartBuilder b = new PartBuilder();
            if (frameB)
            {
                b.Box(Suit, new Vector3(0f, 0.11f, 0f), new Vector3(0.34f, 0.22f, 0.23f));                 // narrow waist
                b.Box(Suit, new Vector3(0f, 0.36f, 0f), new Vector3(0.40f, 0.28f, 0.25f));                 // chest
            }
            else b.Box(Suit, new Vector3(0f, 0.25f, 0f), new Vector3(0.44f, 0.50f, 0.25f));
            b.Box(Joint, new Vector3(0f, 0.0f, 0f), new Vector3(frameB ? 0.35f : 0.45f, 0.05f, 0.26f));    // waist ring
            b.Box(Trim, new Vector3(0f, 0.51f, 0f), new Vector3(0.30f, 0.04f, 0.22f));                     // collar
            // Chest control box: a grey block with a screen, a knob and a row of switches.
            b.Box(Pack, new Vector3(0f, 0.32f, 0.14f), new Vector3(0.24f, 0.14f, 0.05f));
            b.Box(Screen, new Vector3(-0.03f, 0.335f, 0.167f), new Vector3(0.12f, 0.05f, 0.008f));
            b.Box(Accent, new Vector3(0.075f, 0.335f, 0.169f), new Vector3(0.035f, 0.035f, 0.012f));
            b.Box(Joint, new Vector3(-0.01f, 0.28f, 0.166f), new Vector3(0.17f, 0.018f, 0.006f));
            return b;
        }

        private static PartBuilder BuildBackpack(int style, bool frameB)
        {
            PartBuilder b = new PartBuilder();
            const float back = -0.125f; // the torso's back face
            switch (AstronautLook.Mod(style, AstronautLook.Backpacks.Length))
            {
                case 1: // Twin tanks on a harness plate.
                    b.Box(Joint, new Vector3(0f, 0.26f, back - 0.012f), new Vector3(0.32f, 0.36f, 0.025f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = 0.085f * side;
                        b.Box(Pack, new Vector3(x, 0.26f, back - 0.10f), new Vector3(0.15f, 0.42f, 0.15f));
                        b.Box(Trim, new Vector3(x, 0.485f, back - 0.10f), new Vector3(0.10f, 0.03f, 0.10f));
                        b.Box(Accent, new Vector3(x, 0.40f, back - 0.10f), new Vector3(0.16f, 0.03f, 0.16f));
                    }
                    break;
                case 2: // Expedition pack: bigger, with side pouches and an antenna.
                    b.Box(Pack, new Vector3(0f, 0.24f, back - 0.105f), new Vector3(0.42f, 0.48f, 0.21f));
                    b.Box(Trim, new Vector3(0f, 0.495f, back - 0.105f), new Vector3(0.40f, 0.03f, 0.20f));
                    for (int side = -1; side <= 1; side += 2)
                        b.Box(Accent, new Vector3(0.235f * side, 0.14f, back - 0.105f), new Vector3(0.05f, 0.17f, 0.12f));
                    b.Box(Accent, new Vector3(0f, 0.40f, back - 0.212f), new Vector3(0.30f, 0.03f, 0.008f));
                    b.Box(Joint, new Vector3(0.15f, 0.60f, back - 0.15f), new Vector3(0.015f, 0.22f, 0.015f));      // antenna
                    break;
                default: // Slim pack.
                    b.Box(Pack, new Vector3(0f, 0.26f, back - 0.075f), new Vector3(0.36f, 0.40f, 0.15f));
                    b.Box(Trim, new Vector3(0f, 0.475f, back - 0.075f), new Vector3(0.34f, 0.03f, 0.14f));
                    b.Box(Accent, new Vector3(0f, 0.38f, back - 0.152f), new Vector3(0.26f, 0.03f, 0.008f));
                    for (int side = -1; side <= 1; side += 2)
                        for (int i = 0; i < 3; i++)
                            b.Box(Joint, new Vector3(0.181f * side, 0.30f - i * 0.06f, back - 0.075f), new Vector3(0.008f, 0.03f, 0.09f));
                    break;
            }
            return b;
        }

        /// <summary>A cube head with a flat, pixel-style face: square eyes, brows, a nose block and a mouth line.</summary>
        private static PartBuilder BuildHead(bool frameB)
        {
            PartBuilder b = new PartBuilder();
            b.Box(Skin, new Vector3(0f, 0.49f, 0f), new Vector3(0.12f, 0.04f, 0.12f));                       // neck
            b.Box(Skin, new Vector3(0f, 0.635f, 0f), new Vector3(0.26f, 0.27f, 0.25f));                      // head
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 0.055f * side;
                b.Box(Eye, new Vector3(x, 0.655f, 0.127f), new Vector3(0.04f, 0.045f, 0.006f));
                b.Box(Hair, new Vector3(x, 0.70f, 0.127f), new Vector3(0.06f, frameB ? 0.012f : 0.016f, 0.006f)); // brow
            }
            b.Box(Skin, new Vector3(0f, 0.625f, 0.133f), new Vector3(0.03f, 0.035f, 0.018f));                // nose
            b.Box(Lip, new Vector3(0f, 0.585f, 0.127f), new Vector3(0.07f, 0.015f, 0.006f));                 // mouth
            return b;
        }

        /// <summary>
        /// Hair as blocks on the cube head (a cap, back and side slabs, a fringe). The part that
        /// fits inside a helmet goes in the returned builder; ponytails, buns and long lengths go in
        /// <paramref name="extras"/>, which is hidden while the helmet is on.
        /// </summary>
        private static PartBuilder BuildHair(int style, PartBuilder extras)
        {
            PartBuilder b = new PartBuilder();
            switch (AstronautLook.Mod(style, AstronautLook.HairStyles.Length))
            {
                case 1: // Buzz
                    Crop(b, 0.012f, 0.66f, 0.68f);
                    b.Box(Hair, new Vector3(0f, 0.765f, 0.127f), new Vector3(0.26f, 0.02f, 0.008f));
                    break;
                case 2: // Side part: a crop with a swept block of fringe
                    Crop(b, 0.025f, 0.62f, 0.66f);
                    b.Box(Hair, new Vector3(-0.05f, 0.75f, 0.135f), new Vector3(0.18f, 0.06f, 0.025f));
                    b.Box(Hair, new Vector3(-0.04f, 0.81f, 0.02f), new Vector3(0.18f, 0.035f, 0.20f));
                    break;
                case 3: // Bob, down to the jaw
                    Crop(b, 0.03f, 0.53f, 0.53f);
                    b.Box(Hair, new Vector3(0f, 0.75f, 0.135f), new Vector3(0.27f, 0.05f, 0.025f));
                    break;
                case 4: // Ponytail
                    Crop(b, 0.025f, 0.60f, 0.66f);
                    b.Box(Hair, new Vector3(0f, 0.755f, 0.132f), new Vector3(0.27f, 0.04f, 0.02f));
                    extras.Box(Accent, new Vector3(0f, 0.68f, -0.15f), new Vector3(0.06f, 0.04f, 0.035f));
                    extras.Box(Hair, new Vector3(0f, 0.58f, -0.17f), new Vector3(0.07f, 0.18f, 0.06f));
                    break;
                case 5: // High bun
                    Crop(b, 0.025f, 0.60f, 0.66f);
                    b.Box(Hair, new Vector3(0f, 0.755f, 0.132f), new Vector3(0.27f, 0.04f, 0.02f));
                    extras.Box(Hair, new Vector3(0f, 0.83f, -0.04f), new Vector3(0.13f, 0.10f, 0.13f));
                    break;
                case 6: // Long, past the shoulders
                    Crop(b, 0.03f, 0.53f, 0.53f);
                    b.Box(Hair, new Vector3(0f, 0.755f, 0.135f), new Vector3(0.27f, 0.04f, 0.025f));
                    extras.Box(Hair, new Vector3(0f, 0.455f, -0.145f), new Vector3(0.28f, 0.15f, 0.03f));
                    break;
                case 7: // Curly: chunky volume
                    b.Box(Hair, new Vector3(0f, 0.80f, 0f), new Vector3(0.32f, 0.07f, 0.31f));
                    b.Box(Hair, new Vector3(0f, 0.66f, -0.145f), new Vector3(0.32f, 0.30f, 0.05f));
                    for (int side = -1; side <= 1; side += 2)
                        b.Box(Hair, new Vector3(0.15f * side, 0.67f, -0.02f), new Vector3(0.05f, 0.26f, 0.24f));
                    b.Box(Hair, new Vector3(0f, 0.76f, 0.14f), new Vector3(0.30f, 0.05f, 0.03f));
                    b.Box(Hair, new Vector3(-0.08f, 0.845f, 0.05f), new Vector3(0.10f, 0.03f, 0.10f));
                    b.Box(Hair, new Vector3(0.07f, 0.845f, -0.06f), new Vector3(0.10f, 0.03f, 0.10f));
                    break;
                default: // Short crop
                    Crop(b, 0.025f, 0.62f, 0.66f);
                    b.Box(Hair, new Vector3(0f, 0.755f, 0.132f), new Vector3(0.27f, 0.04f, 0.02f));
                    break;
            }
            return b;
        }

        /// <summary>A hair cap <paramref name="thick"/> deep on the head, with back and side slabs down to the given heights.</summary>
        private static void Crop(PartBuilder b, float thick, float backTo, float sidesTo)
        {
            const float top = 0.77f, halfW = 0.13f, halfD = 0.125f;
            b.Box(Hair, new Vector3(0f, top + thick * 0.5f, 0f), new Vector3(halfW * 2f + thick, thick, halfD * 2f + thick));
            b.Box(Hair, new Vector3(0f, (backTo + top) * 0.5f, -halfD - thick * 0.5f), new Vector3(halfW * 2f + thick, top - backTo, thick));
            for (int side = -1; side <= 1; side += 2)
                b.Box(Hair, new Vector3((halfW + thick * 0.5f) * side, (sidesTo + top) * 0.5f, -0.04f),
                    new Vector3(thick, top - sidesTo, 0.17f + thick));
        }

        private static PartBuilder BuildHelmet(int style)
        {
            PartBuilder b = new PartBuilder();
            if (AstronautLook.Mod(style, AstronautLook.Helmets.Length) == 1)
            {
                // Stepped dome: voxel layers narrowing to the top, the visor across the widest one.
                b.Walls(Suit, new Vector3(0f, 0.51f, 0.01f), new Vector3(0.36f, 0.10f, 0.36f), 0.03f, true);
                b.Walls(Suit, new Vector3(0f, 0.66f, 0.01f), new Vector3(0.42f, 0.20f, 0.40f), 0.03f, false);
                b.Box(Suit, new Vector3(0f, 0.79f, 0.01f), new Vector3(0.36f, 0.06f, 0.36f));
                b.Box(Suit, new Vector3(0f, 0.845f, 0.01f), new Vector3(0.26f, 0.05f, 0.26f));
                b.Box(Visor, new Vector3(0f, 0.66f, 0.20f), new Vector3(0.36f, 0.20f, 0.02f));
                Lamps(b, 0.225f, 0.72f, 0.14f);
            }
            else
            {
                // Classic: a big cube with a visor window, a chin plate and a brow plate.
                b.Walls(Suit, new Vector3(0f, 0.665f, 0.01f), new Vector3(0.38f, 0.39f, 0.36f), 0.03f, false);
                b.Box(Suit, new Vector3(0f, 0.855f, 0.01f), new Vector3(0.38f, 0.03f, 0.36f));               // top
                b.Box(Suit, new Vector3(0f, 0.53f, 0.175f), new Vector3(0.38f, 0.12f, 0.03f));               // chin plate
                b.Box(Suit, new Vector3(0f, 0.83f, 0.175f), new Vector3(0.38f, 0.06f, 0.03f));               // brow plate
                b.Box(Visor, new Vector3(0f, 0.695f, 0.18f), new Vector3(0.32f, 0.21f, 0.025f));
                b.Box(Trim, new Vector3(0f, 0.475f, 0.01f), new Vector3(0.39f, 0.03f, 0.37f));               // neck seal
                Lamps(b, 0.205f, 0.77f, 0.10f);
            }
            return b;
        }

        private static void Lamps(PartBuilder b, float x, float y, float z)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(Pack, new Vector3(x * side, y, z), new Vector3(0.03f, 0.07f, 0.10f));
                b.Box(Lamp, new Vector3(x * side, y, z + 0.055f), new Vector3(0.025f, 0.05f, 0.01f));
            }
        }

        private void BuildLeg(float side, bool frameB, out Transform thigh, out Transform shin)
        {
            string label = side < 0f ? " L" : " R";
            float w = frameB ? 0.18f : 0.17f;
            thigh = Node("Thigh" + label, hips, new Vector3((frameB ? 0.11f : 0.10f) * side, 0f, 0f));
            PartBuilder t = new PartBuilder();
            t.Box(Suit, new Vector3(0f, -0.19f, 0f), new Vector3(w, 0.46f, 0.19f));
            t.Box(Accent, new Vector3(0f, -0.20f, 0f), new Vector3(w + 0.01f, 0.05f, 0.20f));            // ID band
            AlwaysShown(thigh, "Mesh", t);

            shin = Node("Shin" + label, thigh, new Vector3(0f, -ThighLength, 0f));
            PartBuilder s = new PartBuilder();
            s.Box(Joint, new Vector3(0f, -0.01f, 0f), new Vector3(w + 0.01f, 0.07f, 0.20f));             // knee
            s.Box(Suit, new Vector3(0f, -0.20f, 0f), new Vector3(w - 0.01f, 0.36f, 0.18f));
            s.Box(Boot, new Vector3(0f, -0.425f, 0.035f), new Vector3(w + 0.01f, 0.09f, 0.26f));        // boot
            s.Box(Joint, new Vector3(0f, -0.485f, 0.035f), new Vector3(w + 0.02f, 0.03f, 0.27f));       // sole
            s.Box(Trim, new Vector3(0f, -0.445f, 0.16f), new Vector3(w, 0.05f, 0.02f));                 // toe cap
            AlwaysShown(shin, "Mesh", s);
        }

        private void BuildArm(float side, float shoulderX, bool frameB, out Transform upperArm, out Transform forearm)
        {
            string label = side < 0f ? " L" : " R";
            float w = frameB ? 0.115f : 0.13f;
            upperArm = Node("Upper Arm" + label, spine, new Vector3(shoulderX * side, 0.44f, 0f));
            PartBuilder u = new PartBuilder();
            u.Box(Suit, new Vector3(0f, -0.12f, 0f), new Vector3(w, 0.32f, w + 0.01f));
            u.Box(Accent, new Vector3(0f, -0.13f, 0f), new Vector3(w + 0.01f, 0.05f, w + 0.02f));        // ID band
            AlwaysShown(upperArm, "Mesh", u);

            forearm = Node("Forearm" + label, upperArm, new Vector3(0f, -UpperArmLength, 0f));
            PartBuilder f = new PartBuilder();
            f.Box(Joint, new Vector3(0f, 0f, 0f), new Vector3(w + 0.005f, 0.06f, w + 0.015f));           // elbow
            f.Box(Suit, new Vector3(0f, -0.13f, 0f), new Vector3(w - 0.01f, 0.22f, w));
            f.Box(Joint, new Vector3(0f, -0.245f, 0f), new Vector3(w + 0.005f, 0.03f, w + 0.015f));      // cuff
            f.Box(Glove, new Vector3(0f, -0.30f, 0.005f), new Vector3(w, 0.12f, w + 0.01f));             // glove
            f.Box(Glove, new Vector3(-0.07f * side, -0.28f, 0.03f), new Vector3(0.03f, 0.05f, 0.05f));    // thumb
            AlwaysShown(forearm, "Mesh", f);
        }

        private static Transform Node(string nodeName, Transform parent, Vector3 localPosition)
        {
            Transform node = new GameObject(nodeName).transform;
            node.SetParent(parent, false);
            node.localPosition = localPosition;
            return node;
        }

        private MeshRenderer AlwaysShown(Transform parent, string partName, PartBuilder part)
        {
            return Attach(parent, partName, part, ShadowCastingMode.On);
        }

        private MeshRenderer Upper(Transform parent, string partName, PartBuilder part)
        {
            MeshRenderer r = Attach(parent, partName, part, ShadowCastingMode.On);
            upper.Add(r);
            return r;
        }

        private MeshRenderer Attach(Transform parent, string partName, PartBuilder part, ShadowCastingMode shadows)
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
            return renderer;
        }

        private void OnDestroy()
        {
            if (rig != null) { rig.Destroy(); rig = null; }
            foreach (Object o in owned)
            {
                if (o != null) Destroy(o);
            }
            owned.Clear();
        }

        // ------------------------------------------------------------------ materials

        private void CreateMaterials()
        {
            Color suit = look.Suit;
            bool darkSuit = suit.grayscale < 0.45f;
            materials = new Material[SlotCount];
            materials[Suit] = Solid("Astronaut Suit", suit);
            materials[Joint] = Solid("Astronaut Joint", darkSuit ? new Color(0.16f, 0.17f, 0.21f) : new Color(0.46f, 0.47f, 0.53f));
            materials[Accent] = Solid("Astronaut Accent", look.Accent);
            materials[Trim] = Solid("Astronaut Trim", Color.Lerp(suit, new Color(0.55f, 0.60f, 0.70f), 0.55f));
            materials[Glove] = Solid("Astronaut Glove", new Color(0.52f, 0.54f, 0.60f));
            materials[Boot] = Solid("Astronaut Boot", new Color(0.60f, 0.61f, 0.66f));
            materials[Visor] = Solid("Astronaut Visor", look.Visor, 0.85f);
            if (look.ClearVisor) MakeSeeThrough(materials[Visor], 0.28f);
            materials[Skin] = Solid("Astronaut Skin", look.Skin);
            materials[Hair] = Solid("Astronaut Hair", look.Hair);
            materials[Eye] = Solid("Astronaut Eye", new Color(0.12f, 0.10f, 0.11f));
            materials[Pack] = Solid("Astronaut Pack", Color.Lerp(suit, new Color(0.78f, 0.79f, 0.83f), 0.5f));
            materials[Screen] = Solid("Astronaut Screen", new Color(0.18f, 0.42f, 0.48f), 0.6f);
            materials[Lamp] = Solid("Astronaut Lamp", new Color(1f, 0.96f, 0.86f));
            if (materials[Lamp].HasProperty("_EmissionColor"))
            {
                materials[Lamp].EnableKeyword("_EMISSION");
                materials[Lamp].SetColor("_EmissionColor", new Color(1f, 0.93f, 0.78f) * 1.5f);
            }
            materials[Lip] = Solid("Astronaut Lip", Color.Lerp(look.Skin, new Color(0.55f, 0.22f, 0.24f), 0.45f));
        }

        private Material Solid(string materialName, Color color, float smoothness = -1f)
        {
            Material material = MarsPalette.CreateMatteMaterial(materialName, color);
            if (smoothness >= 0f && material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (smoothness >= 0f && material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            owned.Add(material);
            return material;
        }

        /// <summary>Turns a URP Lit (or built-in Standard) material into an alpha-blended glass.</summary>
        internal static void MakeSeeThrough(Material m, float alpha)
        {
            Color c = m.color; c.a = alpha; m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // ------------------------------------------------------------------ holding things

        public bool IsBuilt { get { return built; } }

        /// <summary>World position of a shoulder joint.</summary>
        public Vector3 ShoulderPosition(bool right)
        {
            if (rig != null) return rig.ShoulderPosition(right);
            return (right ? upperArmR : upperArmL).position;
        }

        /// <summary>Shoulder-to-glove distance with the arm straight, in world units.</summary>
        public float ArmReach(bool right)
        {
            if (rig != null) return rig.ArmReach(right);
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
            if (rig != null) { rig.ReachArm(right, grip, handleUp, elbowHint); return; }
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
            if (rig != null) { rig.Animate(phase, move, sprint, air, vertical, time, Time.deltaTime); return; }
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
        // ------------------------------------------------------------------ mesh template

        /// <summary>
        /// Collects flat-shaded boxes into one mesh with a submesh per material slot (the
        /// blocky template's only primitive, plus hollow walls for helmets).
        /// </summary>
        private sealed class PartBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<int>[] triangles = NewLists();

            private static List<int>[] NewLists()
            {
                List<int>[] lists = new List<int>[SlotCount];
                for (int i = 0; i < lists.Length; i++) lists[i] = new List<int>();
                return lists;
            }

            public bool IsEmpty { get { return vertices.Count == 0; } }

            /// <summary>A box from its centre and size.</summary>
            public void Box(int slot, Vector3 centre, Vector3 size)
            {
                Vector3 half = size * 0.5f;
                Corners(slot, centre - half, centre + half);
            }

            /// <summary>
            /// Four walls of a hollow box (no top or bottom), <paramref name="thickness"/> thick;
            /// without <paramref name="front"/> the front wall is left out (a visor window).
            /// </summary>
            public void Walls(int slot, Vector3 centre, Vector3 size, float thickness, bool front)
            {
                Vector3 h = size * 0.5f;
                Corners(slot, new Vector3(centre.x - h.x, centre.y - h.y, centre.z - h.z), new Vector3(centre.x - h.x + thickness, centre.y + h.y, centre.z + h.z));
                Corners(slot, new Vector3(centre.x + h.x - thickness, centre.y - h.y, centre.z - h.z), new Vector3(centre.x + h.x, centre.y + h.y, centre.z + h.z));
                Corners(slot, new Vector3(centre.x - h.x + thickness, centre.y - h.y, centre.z - h.z), new Vector3(centre.x + h.x - thickness, centre.y + h.y, centre.z - h.z + thickness));
                if (front)
                    Corners(slot, new Vector3(centre.x - h.x + thickness, centre.y - h.y, centre.z + h.z - thickness), new Vector3(centre.x + h.x - thickness, centre.y + h.y, centre.z + h.z));
            }

            /// <summary>A box between two opposite corners.</summary>
            public void Corners(int slot, Vector3 min, Vector3 max)
            {
                Vector3 c = (min + max) * 0.5f;
                Vector3 a = new Vector3(min.x, min.y, min.z), b = new Vector3(max.x, min.y, min.z);
                Vector3 d = new Vector3(min.x, max.y, min.z), e = new Vector3(max.x, max.y, min.z);
                Vector3 f = new Vector3(min.x, min.y, max.z), g = new Vector3(max.x, min.y, max.z);
                Vector3 h = new Vector3(min.x, max.y, max.z), k = new Vector3(max.x, max.y, max.z);
                Quad(slot, a, b, e, d, c); // back
                Quad(slot, f, g, k, h, c); // front
                Quad(slot, a, f, h, d, c); // left
                Quad(slot, b, g, k, e, c); // right
                Quad(slot, d, e, k, h, c); // top
                Quad(slot, a, b, g, f, c); // bottom
            }

            /// <summary>A flat quad facing away from <paramref name="inside"/>.</summary>
            private void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) normal = Vector3.Cross(c - a, d - a);
                if (normal.sqrMagnitude < 1e-12f) return;
                normal.Normalize();
                bool flip = Vector3.Dot(normal, (a + b + c + d) * 0.25f - inside) < 0f;
                if (flip) normal = -normal;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                List<int> list = triangles[slot];
                if (flip) { list.Add(start); list.Add(start + 2); list.Add(start + 1); list.Add(start); list.Add(start + 3); list.Add(start + 2); }
                else { list.Add(start); list.Add(start + 1); list.Add(start + 2); list.Add(start); list.Add(start + 2); list.Add(start + 3); }
            }

            public Mesh ToMesh(Material[] allMaterials, out Material[] usedMaterials)
            {
                Mesh mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
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
