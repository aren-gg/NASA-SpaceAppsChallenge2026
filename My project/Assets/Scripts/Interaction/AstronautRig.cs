using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// The playable astronaut: NASA's public astronaut model, detailed in Blender with its helmet
    /// sealed and the gold visor down (tools/blender/astronaut_nasa.py, Resources/Astronaut/Astronaut).
    /// The helmet never comes off.
    ///
    /// Motion is real motion capture (CMU Graphics Lab Motion Capture Database) for idle, walk,
    /// run, sprint and the airborne pose, retargeted onto the suit's own skeleton in Blender
    /// (tools/blender/astronaut_mocap.py, Resources/Astronaut/Astronaut_Clips). Both files carry
    /// the same skeleton, so Unity's Humanoid mapping between them plays the capture as recorded.
    ///
    /// Driven by <see cref="MarsAstronautBody"/>: a playable graph blends the clips from the
    /// player's move, sprint and air blends (the stride clips follow the player's stride phase so
    /// the feet keep pace), each clip easing to its weight so changes cross-fade.
    /// </summary>
    public sealed class AstronautRig
    {
        public const string Folder = "Astronaut";
        private const string ModelPath = Folder + "/Astronaut";

        public static bool Available() { return Resources.Load<GameObject>(ModelPath) != null; }

        /// <summary>False when the model did not import as Humanoid (it can't animate); the caller falls back.</summary>
        public bool Usable { get { return animator != null && animator.isHuman && upperArmL != null && upperArmR != null; } }

        public readonly GameObject root;
        private readonly Transform model;
        private readonly Animator animator;
        private PlayableGraph graph;
        private AnimationMixerPlayable locomotion;
        private readonly AnimationClipPlayable[] stride = new AnimationClipPlayable[3];   // walk, run, sprint
        private readonly Transform head, upperArmL, upperArmR, lowerArmL, lowerArmR, handL, handR;
        private readonly List<Object> owned = new List<Object>();
        private readonly AstronautLook look;
        private bool headVisible = true;
        private Vector3 headScale = Vector3.one;
        private const float BlendRate = 12f;
        private readonly float[] weight = new float[5], target = new float[5];   // idle, walk, run, sprint, fall
        private bool weightsSet;

        public AstronautRig(Transform parent, AstronautLook look)
        {
            this.look = look;
            root = new GameObject("Astronaut Rig");
            root.transform.SetParent(parent, false);
            GameObject prefab = Resources.Load<GameObject>(ModelPath);
            model = Object.Instantiate(prefab, root.transform, false).transform;
            model.name = prefab.name;
            animator = model.GetComponent<Animator>() ?? model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Humanoid animation already faces the body along the Animator's +Z, so the model is
            // left as imported (turning it by its toes as well makes it walk backwards).
            head = Bone(HumanBodyBones.Head);
            upperArmL = Bone(HumanBodyBones.LeftUpperArm); upperArmR = Bone(HumanBodyBones.RightUpperArm);
            lowerArmL = Bone(HumanBodyBones.LeftLowerArm); lowerArmR = Bone(HumanBodyBones.RightLowerArm);
            handL = Bone(HumanBodyBones.LeftHand); handR = Bone(HumanBodyBones.RightHand);
            if (head != null) headScale = head.localScale;

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
            // Only humanoid clips retarget onto the body; a generic import would drive the model's
            // root node and lay it flat, so it is ignored (the body stands still).
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
            output.SetSourcePlayable(locomotion);
            graph.Play();
        }

        private AnimationClipPlayable Add(AnimationMixerPlayable mixer, int input, AnimationClip clip)
        {
            AnimationClipPlayable p = clip != null ? AnimationClipPlayable.Create(graph, clip) : AnimationClipPlayable.Create(graph, new AnimationClip());
            graph.Connect(p, 0, mixer, input);
            mixer.SetInputWeight(input, 0f);
            return p;
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

            graph.Evaluate(dt);
            ApplyHead();
        }

        /// <summary>Bends the legs into a seat: thighs forward to level, shins back down (t 0 standing, 1 seated).
        /// Call after <see cref="Animate"/>, which poses the rest of the body.</summary>
        public void Sit(float t)
        {
            if (t <= 0f || animator == null) return;
            Vector3 side = root.transform.right;
            foreach (var (thigh, shin) in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg) })
            {
                Transform a = Bone(thigh), b = Bone(shin);
                if (a == null || b == null) continue;
                a.rotation = Quaternion.AngleAxis(-88f * t, side) * a.rotation;
                b.rotation = Quaternion.AngleAxis(88f * t, side) * b.rotation;
            }
        }

        // ------------------------------------------------------------------ head

        /// <summary>First person hides the head (and the helmet with it) by shrinking the head bone.</summary>
        public void SetHeadVisible(bool visible) { headVisible = visible; ApplyHead(); }

        private void ApplyHead()
        {
            if (head != null) head.localScale = headVisible ? headScale : headScale * 0.001f;
            // First person: the whole suit only casts its shadow, so the torso and pack never block the view.
            var mode = headVisible ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            if (renderers == null) renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers) if (r != null) r.shadowCastingMode = mode;
        }
        private Renderer[] renderers;

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
            var bySlot = new Dictionary<string, Material>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    string key = Strip(slots[i].name);
                    Material m;
                    if (!bySlot.TryGetValue(key, out m)) { m = Suit(key); bySlot[key] = m; owned.Add(m); }
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

        /// <summary>A realistic EVA suit (PBR): white layers tinted by the wardrobe, dark gloves and boots, gold visor.</summary>
        private Material Suit(string slot)
        {
            Color suit = Color.Lerp(new Color32(0xEE, 0xEC, 0xE6, 255), look.Suit, 0.35f);
            switch (slot)
            {
                case "Helmet Shell": return Lit(slot, suit, 0.5f);
                case "Accent": return Lit(slot, look.Accent, 0.4f);
                case "Trim": return Lit(slot, new Color32(0x3B, 0x3F, 0x46, 255), 0.6f, 0.8f);
                case "Glove": return Lit(slot, new Color32(0x2A, 0x2C, 0x31, 255), 0.35f);
                case "Boot": return Lit(slot, new Color32(0x34, 0x36, 0x3C, 255), 0.3f);
                case "Comfort Cap": return Lit(slot, new Color32(0x4A, 0x4E, 0x57, 255), 0.15f);
                case "Flag Red": return Lit(slot, new Color32(0xB4, 0x23, 0x2A, 255), 0.35f);
                // The sealed helmet's visor is always mirrored: the "clear" choice becomes a dark smoked visor.
                case "Visor": return Lit(slot, look.ClearVisor ? new Color(0.12f, 0.14f, 0.17f) : look.Visor, 0.95f, 0.9f);
                case "Screen": return Lit(slot, new Color(0.05f, 0.08f, 0.11f), 0.85f, 0f, new Color(0.37f, 0.83f, 1f) * 1.2f);
                case "Lamp": return Lit(slot, Color.black, 0.5f, 0f, new Color(1f, 0.94f, 0.84f) * 1.8f);
                default: return Lit(slot, suit, 0.28f);   // "Suit", "Pack" and any other white layer
            }
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
