using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// The playable astronauts: NASA's public astronaut model detailed in Blender, with VRoid
    /// (CC0) anime heads for the male and female astronaut (tools/blender/astronaut_nasa.py, models
    /// in Resources/Astronaut). The clips in Astronaut_Clips (Quaternius Universal Animation
    /// Library, CC0, plus a hand-made Helmet_On) are retargeted onto the suit's own skeleton in
    /// Blender (tools/blender/astronaut_retarget.py), so Unity's Humanoid mapping between the
    /// clip file and the bodies is between identical skeletons and plays the motion as authored.
    ///
    /// Driven by <see cref="MarsAstronautBody"/>, which keeps its public interface: a playable
    /// graph blends Idle, Walk, Run, Sprint and Fall from the player's move, sprint and air
    /// blends (the stride clips follow the player's stride phase so the feet keep pace), and
    /// Helmet_On plays on an upper-body layer when the helmet goes on, the helmet riding between
    /// the hands until it settles on the head. Faces and hair keep their anime textures with a
    /// soft toon lift; the suit takes the <see cref="AstronautLook"/> colours.
    /// </summary>
    public sealed class AstronautRig
    {
        public const string Folder = "Astronaut";

        public static bool Available(int frame) { return Resources.Load<GameObject>(Path(frame)) != null; }
        private static string Path(int frame)
        {
            return Folder + "/Astronaut_" + (AstronautLook.Mod(frame, AstronautLook.Frames.Length) == 1 ? "Girl" : "Boy");
        }

        /// <summary>False when the model did not import as Humanoid (it can't animate); the caller falls back.</summary>
        public bool Usable { get { return animator != null && animator.isHuman && upperArmL != null && upperArmR != null; } }

        public readonly GameObject root;
        private readonly Transform model;
        private readonly Animator animator;
        private PlayableGraph graph;
        private AnimationMixerPlayable locomotion;
        private AnimationLayerMixerPlayable layers;
        private readonly AnimationClipPlayable[] stride = new AnimationClipPlayable[3];   // walk, run, sprint
        private AnimationClipPlayable helmetOn;
        private readonly Transform head, upperArmL, upperArmR, lowerArmL, lowerArmR, handL, handR, helmet;
        private readonly Vector3 helmetRestPosition;
        private readonly Quaternion helmetRestRotation;
        /// <summary>The middle of the helmet's meshes in the Helmet node's space (the node itself sits elsewhere).</summary>
        private readonly Vector3 helmetCentre;
        private readonly List<Renderer> helmetParts = new List<Renderer>();
        private readonly List<Object> owned = new List<Object>();
        private readonly AstronautLook look;
        private bool helmetWorn = true, headVisible = true, first = true;
        private float helmetClock = -1f, helmetLength = 2f;
        private Vector3 headScale = Vector3.one;
        private const float BlendRate = 12f;
        private readonly float[] weight = new float[5], target = new float[5];   // idle, walk, run, sprint, fall
        private bool weightsSet;

        public AstronautRig(Transform parent, AstronautLook look)
        {
            this.look = look;
            root = new GameObject("Astronaut Rig");
            root.transform.SetParent(parent, false);
            GameObject prefab = Resources.Load<GameObject>(Path(look.frame));
            model = Object.Instantiate(prefab, root.transform, false).transform;
            model.name = prefab.name;
            animator = model.GetComponent<Animator>() ?? model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            head = Bone(HumanBodyBones.Head);
            upperArmL = Bone(HumanBodyBones.LeftUpperArm); upperArmR = Bone(HumanBodyBones.RightUpperArm);
            lowerArmL = Bone(HumanBodyBones.LeftLowerArm); lowerArmR = Bone(HumanBodyBones.RightLowerArm);
            handL = Bone(HumanBodyBones.LeftHand); handR = Bone(HumanBodyBones.RightHand);
            if (head != null) headScale = head.localScale;

            // Humanoid animation already faces the body along the Animator's +Z (Unity normalises the
            // avatar's orientation), so the model is left as imported; turning it by the bind pose's
            // toes as well made the astronaut walk backwards.
            Transform foot = Bone(HumanBodyBones.LeftFoot), toes = Bone(HumanBodyBones.LeftToes);
            if (!animator.isHuman && foot != null && toes != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(toes.position - foot.position, Vector3.up);
                if (forward.sqrMagnitude > 1e-6f)
                    model.rotation = Quaternion.FromToRotation(forward.normalized, root.transform.forward) * model.rotation;
            }

            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == "Helmet") { helmet = t; break; }
            if (helmet != null)
            {
                helmetRestPosition = helmet.localPosition;
                helmetRestRotation = helmet.localRotation;
                helmetParts.AddRange(helmet.GetComponentsInChildren<Renderer>(true));
                if (helmetParts.Count > 0)
                {
                    Bounds b = helmetParts[0].bounds;
                    foreach (Renderer r in helmetParts) b.Encapsulate(r.bounds);
                    helmetCentre = helmet.InverseTransformPoint(b.center);
                }
            }
            Recolour();
            BuildGraph();
        }

        private Transform Bone(HumanBodyBones b)
        {
            return animator != null && animator.isHuman ? animator.GetBoneTransform(b) : null;
        }

        // ------------------------------------------------------------------ animation

        private static AnimationClip Clip(AnimationClip[] clips, string name)
        {
            foreach (AnimationClip c in clips)
                if (c.name == name || c.name.EndsWith("|" + name)) return c;
            return null;
        }

        private void BuildGraph()
        {
            // Only humanoid clips retarget onto these bodies; a generic import of the clip file would
            // drive the model's root node and lay it flat, so it is ignored (the body stands still).
            AnimationClip[] clips = System.Array.FindAll(Resources.LoadAll<AnimationClip>(Folder + "/Astronaut_Clips"), c => c.humanMotion);
            if (clips.Length == 0 || !animator.isHuman)
            {
                Debug.LogWarning("Astronaut: no humanoid clips in Resources/" + Folder + "/Astronaut_Clips (re-import it as Humanoid); the astronaut stands still.");
                return;
            }
            graph = PlayableGraph.Create("Astronaut");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Body", animator);

            locomotion = AnimationMixerPlayable.Create(graph, 5);
            Add(locomotion, 0, Clip(clips, "Idle"));
            stride[0] = Add(locomotion, 1, Clip(clips, "Walk"));
            stride[1] = Add(locomotion, 2, Clip(clips, "Run"));
            stride[2] = Add(locomotion, 3, Clip(clips, "Sprint"));
            Add(locomotion, 4, Clip(clips, "Fall"));
            locomotion.SetInputWeight(0, 1f);

            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(locomotion, 0, layers, 0);
            layers.SetInputWeight(0, 1f);
            AnimationClip put = Clip(clips, "Helmet_On");
            if (put != null)
            {
                helmetOn = AnimationClipPlayable.Create(graph, put);
                helmetOn.SetSpeed(0);
                helmetLength = put.length;
                graph.Connect(helmetOn, 0, layers, 1);
                layers.SetLayerMaskFromAvatarMask(1, UpperBodyMask());
            }
            layers.SetInputWeight(1, 0f);
            output.SetSourcePlayable(layers);
            graph.Play();
        }

        private AnimationClipPlayable Add(AnimationMixerPlayable mixer, int input, AnimationClip clip)
        {
            AnimationClipPlayable p = clip != null ? AnimationClipPlayable.Create(graph, clip) : AnimationClipPlayable.Create(graph, new AnimationClip());
            graph.Connect(p, 0, mixer, input);
            mixer.SetInputWeight(input, 0f);
            return p;
        }

        /// <summary>Spine, arms and head: the helmet clip leaves the legs walking.</summary>
        private AvatarMask UpperBodyMask()
        {
            var mask = new AvatarMask();
            owned.Add(mask);
            for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                             part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                             part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            return mask;
        }

        public void Animate(float phase, float move, float sprint, float air, float vertical, float time, float dt)
        {
            if (!graph.IsValid()) return;
            float ground = 1f - air;
            target[0] = (1f - move) * ground;
            target[1] = move * ground * Mathf.Clamp01(1f - sprint * 2f);
            target[2] = move * ground * (sprint < 0.5f ? sprint * 2f : 2f - sprint * 2f);
            target[3] = move * ground * Mathf.Clamp01(sprint * 2f - 1f);
            target[4] = air;
            // Each clip eases towards its weight (about a fifth of a second), so take-offs, landings
            // and walk-to-run changes cross-fade instead of snapping; the sum is kept at one.
            float ease = weightsSet ? 1f - Mathf.Exp(-BlendRate * dt) : 1f;
            float total = 0f;
            for (int i = 0; i < weight.Length; i++) { weight[i] = Mathf.Lerp(weight[i], target[i], ease); total += weight[i]; }
            weightsSet = true;
            for (int i = 0; i < weight.Length; i++) locomotion.SetInputWeight(i, total > 1e-4f ? weight[i] / total : (i == 0 ? 1f : 0f));

            // The stride clips follow the player's stride phase (one turn = a left and a right step).
            float cycle = Mathf.Repeat(phase / (2f * Mathf.PI), 1f);
            foreach (AnimationClipPlayable p in stride)
                if (p.IsValid() && p.GetAnimationClip() != null) { p.SetSpeed(0); p.SetTime(cycle * p.GetAnimationClip().length); }

            float carry = 0f;
            if (helmetClock >= 0f)
            {
                helmetClock += dt;
                float w = Mathf.Clamp01(helmetClock / 0.15f) * Mathf.Clamp01((helmetLength - helmetClock) / 0.3f);
                layers.SetInputWeight(1, w);
                helmetOn.SetTime(Mathf.Min(helmetClock, helmetLength));
                // The hands carry the helmet until about three quarters through, then it seats on the head.
                carry = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(helmetLength * 0.6f, helmetLength * 0.78f, helmetClock));
                if (helmetClock >= helmetLength) { helmetClock = -1f; layers.SetInputWeight(1, 0f); carry = 0f; }
            }
            graph.Evaluate(dt);
            PlaceHelmet(carry);
            ApplyHead();
        }

        private void PlaceHelmet(float carry)
        {
            if (helmet == null) return;
            helmet.localPosition = helmetRestPosition;
            helmet.localRotation = helmetRestRotation;
            if (carry <= 0f || handL == null || handR == null) return;
            Vector3 between = (handL.position + handR.position) * 0.5f;
            helmet.position += (between - helmet.TransformPoint(helmetCentre)) * carry;
        }

        // ------------------------------------------------------------------ helmet and head

        public void SetHelmet(bool on)
        {
            if (on == helmetWorn && !first) return;
            bool animate = on && !first && helmetOn.IsValid();
            helmetWorn = on;
            first = false;
            foreach (Renderer r in helmetParts) if (r != null) r.enabled = on;
            helmetClock = animate ? 0f : -1f;
        }

        /// <summary>First person hides the head (and the helmet with it) by shrinking the head bone.</summary>
        public void SetHeadVisible(bool visible) { headVisible = visible; ApplyHead(); }

        private void ApplyHead()
        {
            if (head != null) head.localScale = headVisible ? headScale : headScale * 0.001f;
        }

        // ------------------------------------------------------------------ holding things

        public Vector3 ShoulderPosition(bool right) { return (right ? upperArmR : upperArmL).position; }

        public float ArmReach(bool right)
        {
            Transform u = right ? upperArmR : upperArmL, l = right ? lowerArmR : lowerArmL, h = right ? handR : handL;
            return Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, h.position) + 0.07f * root.transform.lossyScale.y;
        }

        /// <summary>Two-bone IK on the skinned arm so the glove sits on <paramref name="grip"/>.</summary>
        public void ReachArm(bool right, Vector3 grip, Vector3 handleUp, Vector3 elbowHint)
        {
            Transform upper = right ? upperArmR : upperArmL, fore = right ? lowerArmR : lowerArmL, hand = right ? handR : handL;
            if (upper == null || fore == null || hand == null) return;
            Vector3 shoulder = upper.position;
            float palm = 0.07f * root.transform.lossyScale.y;
            float a = Vector3.Distance(shoulder, fore.position), b = Vector3.Distance(fore.position, hand.position) + palm;
            Vector3 toGrip = grip - shoulder;
            float distance = toGrip.magnitude;
            if (distance < 1e-5f) return;
            Vector3 dir = toGrip / distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.9999f);
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            float outward = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 hint = elbowHint - shoulder;
            Vector3 bend = hint - dir * Vector3.Dot(hint, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.down - dir * Vector3.Dot(Vector3.down, dir);
            Vector3 elbow = shoulder + dir * along + bend.normalized * outward;
            Vector3 target = shoulder + dir * distance;

            upper.rotation = Quaternion.FromToRotation(fore.position - shoulder, elbow - shoulder) * upper.rotation;
            Vector3 palmPoint = hand.position + (hand.position - fore.position).normalized * palm;
            fore.rotation = Quaternion.FromToRotation(palmPoint - fore.position, target - fore.position) * fore.rotation;
        }

        // ------------------------------------------------------------------ colours

        private void Recolour()
        {
            var made = new Dictionary<Material, Material>();
            var bySlot = new Dictionary<string, Material>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material src = slots[i];
                    if (src == null) continue;
                    Material m;
                    if (made.TryGetValue(src, out m)) { slots[i] = m; continue; }
                    string key = Strip(src.name);
                    m = Suit(key);
                    if (m != null)
                    {
                        Material shared;
                        if (bySlot.TryGetValue(key, out shared)) { Object.Destroy(m); m = shared; }
                        else { bySlot[key] = m; owned.Add(m); }
                    }
                    else { m = Anime(src, key); owned.Add(m); }
                    made[src] = m;
                    slots[i] = m;
                }
                r.sharedMaterials = slots;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            }
        }

        private static string Strip(string name)
        {
            name = name.Replace(" (Instance)", "");
            int dot = name.LastIndexOf('.');
            return dot > 0 && dot >= name.Length - 4 ? name.Substring(0, dot) : name;
        }

        /// <summary>The gear's colour slots (soft pastel, VRoid style); null for the character's own materials.</summary>
        private Material Suit(string slot)
        {
            // A realistic NASA EVA suit (PBR, no outline); only the faces and hair are anime-shaded.
            Color suit = Color.Lerp(new Color32(0xEE, 0xEC, 0xE6, 255), look.Suit, 0.35f);
            switch (slot)
            {
                case "Suit": return Lit(slot, suit, 0.28f);
                case "Helmet Shell": return Lit(slot, suit, 0.5f);
                case "Accent": return Lit(slot, look.Accent, 0.4f);
                case "Trim": return Lit(slot, new Color32(0x3B, 0x3F, 0x46, 255), 0.6f, 0.8f);
                case "Glove": return Lit(slot, new Color32(0x2A, 0x2C, 0x31, 255), 0.35f);
                case "Boot": return Lit(slot, new Color32(0x34, 0x36, 0x3C, 255), 0.3f);
                case "Comfort Cap": return Lit(slot, new Color32(0x4A, 0x4E, 0x57, 255), 0.15f);
                case "Flag Red": return Lit(slot, new Color32(0xB4, 0x23, 0x2A, 255), 0.35f);
                case "Visor":
                {
                    // Gold sun visor, mirrored and nearly opaque, as on a real EVA helmet.
                    Material v = Lit(slot, look.ClearVisor ? new Color(0.75f, 0.85f, 0.95f) : look.Visor, 0.95f, 0.9f);
                    if (look.ClearVisor) MarsAstronautBody.MakeSeeThrough(v, 0.15f);
                    return v;
                }
                case "Screen": return Lit(slot, new Color(0.05f, 0.08f, 0.11f), 0.85f, 0f, new Color(0.37f, 0.83f, 1f) * 1.2f);
                case "Lamp": return Lit(slot, Color.black, 0.5f, 0f, new Color(1f, 0.94f, 0.84f) * 1.8f);
                default: return null;
            }
        }

        /// <summary>
        /// One of the VRoid character's own materials (face, eyes, hair, the suit-coloured clothing):
        /// its anime texture under the toon shader. Lashes, brows and hair tips are cut out; the clothing
        /// takes the wardrobe's suit colour.
        /// </summary>
        private Material Anime(Material src, string key)
        {
            // The Blender export names each of these materials after its texture in Resources/Astronaut/Textures.
            Texture tex = Resources.Load<Texture2D>(Folder + "/Textures/" + key);
            if (tex == null) tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
            bool face = key.Contains("_FACE") || key.Contains("_EYE") || key.Contains("Face_00");
            bool cloth = key.Contains("CLOTH");
            Color tint = cloth && !key.Contains("Shoes") ? Color.Lerp(Color.white, look.Suit, 0.85f) : Color.white;
            // The painted suit body bends at the hips and knees: a thin outline keeps ink out of the folds.
            float outline = face ? 0f : key.Contains("Suit_CLOTH") ? 0.0008f : 0.0022f;
            Material m = Toon("Astronaut " + key, tint, tex, outline);
            bool cut = !(key.Contains("Face_00") || cloth);
            if (cut && m.HasProperty("_AlphaClip")) { m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            if (key.Contains("EyeExtra")) m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));   // VRoid's spare eye layer
            if (face && m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", new Color(0.93f, 0.86f, 0.88f));
            return m;
        }

        /// <summary>Draws both faces: where the suit folds at the hips its back faces show as suit, not as dark ink.</summary>
        private static Material TwoSided(Material m)
        {
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            return m;
        }

        private static Shader toon;

        /// <summary>The toon shader (Resources/Shaders/MarsivityToon), or URP Lit if it is missing.</summary>
        private Material Toon(string name, Color color, Texture texture, float outline = 0.0025f, Color? emission = null)
        {
            if (toon == null) toon = Resources.Load<Shader>("Shaders/MarsivityToon") ?? Shader.Find("Marsivity/Toon");
            if (toon == null || !toon.isSupported) return Lit(name, color, 0.1f, 0f, emission);
            var m = new Material(toon) { name = "Astronaut " + name };
            m.SetColor("_BaseColor", color);
            if (texture != null) m.SetTexture("_BaseMap", texture);
            m.SetFloat("_OutlineWidth", outline);
            if (emission.HasValue) m.SetColor("_EmissionColor", emission.Value);
            return m;
        }

        private static Material Lit(string name, Color color, float smoothness = 0.15f, float metallic = 0f, Color? emission = null)
        {
            Material m = MarsSurvival.World.MarsPalette.CreateMatteMaterial("Astronaut " + name, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emission.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            return m;
        }

        public void Destroy()
        {
            if (graph.IsValid()) graph.Destroy();
            foreach (Object o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
            if (root != null) Object.Destroy(root);
        }
    }
}
