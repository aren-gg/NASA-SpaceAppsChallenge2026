using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Interaction;
using MarsSurvival.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// The astronaut's hand tools. Shows the selected tool in the right hand, works out
    /// what the crosshair is aimed at, and runs the tool's action.
    ///
    /// Controls (while walking with the mouse captured): number keys pick a tool (the
    /// wheel zooms the camera instead), left click uses it, R is the tool's second action.
    /// One click starts an action; it carries on by itself while you keep aiming at the
    /// same thing (short glances away are forgiven), so nothing needs to be held down.
    /// A slot holds its tool once the astronaut has picked it up from its outpost rack
    /// (<see cref="MarsToolPickup"/>); the tools only work while the mission is running.
    /// </summary>
    public sealed class MarsToolBelt : MonoBehaviour
    {
        [Tooltip("Seconds the aim may leave the target before an action stops.")]
        public float aimGraceSeconds = 0.5f;

        public MarsTool[] Tools { get; private set; }
        public int Selected { get; private set; }
        public MarsTool Current { get { return Tools[Selected]; } }
        public bool Active { get; private set; }
        public ToolTarget Target { get; private set; } = new ToolTarget();
        public ToolAction Plan { get; private set; }
        /// <summary>The action being worked on, or null.</summary>
        public ToolAction Working { get; private set; }
        /// <summary>0..1 progress of <see cref="Working"/>.</summary>
        public float Progress { get; private set; }
        /// <summary>True while the aim has drifted off the thing being worked on.</summary>
        public bool AimLost { get { return Working != null && lostFor > 0f; } }
        public string Readout { get; private set; }
        public string SecondaryLabel { get; private set; }
        public float RefusedAt { get; private set; } = -10f;
        public float CompletedAt { get; private set; } = -10f;
        public float FlashAt { get; private set; } = -10f;
        public float SwitchedAt { get; private set; } = -10f;
        public Camera ViewCamera { get; private set; }
        public bool Zoomed { get; set; }

        /// <summary>True when tool <paramref name="index"/> is in the hotbar: picked up from the shed, or a crop you have harvested.</summary>
        public bool Carried(int index)
        {
            var mission = MarsMissionController.Instance;
            if (mission == null || mission.Tools == null || index < 0 || index >= Tools.Length) return false;
            CropItem crop = Tools[index] as CropItem;
            if (crop != null) return mission.Simulation != null && mission.Simulation.State.basket[crop.Kind] > 0;
            return mission.Tools.Has(Tools[index].Id);
        }

        /// <summary>The hotbar: indices into <see cref="Tools"/> in the order they were picked up; key 1 is the first.</summary>
        public readonly List<int> Hotbar = new List<int>();

        /// <summary>Hotbar place (0-based) of a tool or crop id, or -1 when it isn't in the hotbar.</summary>
        public int SlotOf(string id)
        {
            for (int k = 0; k < Hotbar.Count; k++) if (Tools[Hotbar[k]].Id == id) return k;
            return -1;
        }

        /// <summary>Drops what left the hotbar and appends what joined it, keeping pickup order.</summary>
        private void UpdateHotbar()
        {
            Hotbar.RemoveAll(i => !Carried(i));
            var mission = MarsMissionController.Instance;
            if (mission != null && mission.Tools != null)
                foreach (string id in mission.Tools.CarriedIds())
                {
                    int i = System.Array.FindIndex(Tools, t => t.Id == id);
                    if (i >= 0 && !Hotbar.Contains(i)) Hotbar.Add(i);
                }
            for (int i = 0; i < Tools.Length; i++) if (Tools[i] is CropItem && Carried(i) && !Hotbar.Contains(i)) Hotbar.Add(i);
        }

        /// <summary>True while the selected tool is in hand; pressing its number again puts it away.</summary>
        public bool Equipped { get; private set; }

        /// <summary>True while a carried tool is in hand (not just in the hotbar).</summary>
        public bool CurrentCarried { get { return Equipped && Carried(Selected); } }

        /// <summary>The held tool in front of the camera, or null when the slot is empty or the tools are inactive.</summary>
        public Transform HeldView
        {
            get { return Active && pivots != null && CurrentCarried && pivots[Selected].gameObject.activeSelf ? pivots[Selected] : null; }
        }

        public bool LampOn
        {
            get { return lampOn; }
            set
            {
                lampOn = value;
                if (lamp != null) lamp.enabled = value && Active;
            }
        }

        private MarsExpedition expedition;
        private MarsAstronautPlayer player;
        private Transform viewRoot;
        private Transform[] pivots;
        private Transform[] models;
        private MarsAstronautBody body;
        private Transform helmetMount;
        private MeshRenderer lampRenderer;
        private Material[] lensOff, lensOn;
        private bool lensGlowing;
        private Light lamp;
        private bool lampOn;
        private float lostFor;
        private float workTime;
        private float kick;
        private float baseFov = -1f;
        private Quaternion lastCameraRotation;
        private Vector2 sway;
        private float walkPhase;
        private float moveBlend, sprintBlend, airBlend, landDip;
        private bool wasGrounded = true;
        private Vector3 basePosition, baseAngles;
        private Vector3 lag;
        private Vector3 lastPlayerPosition;
        private int lastStroke;

        private static readonly KeyCode[] NumberKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
        };
        private static readonly KeyCode[] PadKeys =
        {
            KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
            KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9, KeyCode.Keypad0,
        };

        private void Awake()
        {
            expedition = GetComponent<MarsExpedition>();
            Tools = MarsTool.CreateAll();
            foreach (MarsTool tool in Tools)
            {
                tool.Attach(this);
                CropItem crop = tool as CropItem;
                tool.Icon = crop != null ? Resources.Load<Texture2D>(MarsWorldTasks.GardenFolder + "/Icons/" + crop.Seed.id)
                                         : Resources.Load<Texture2D>("MarsTools/Icons/" + tool.Id);
            }
            Selected = 0;
        }

        /// <summary>
        /// Number key: takes that tool in hand, or puts it away if it is already in hand. Tools not in
        /// the hotbar can't be selected.
        /// </summary>
        public void Select(int index)
        {
            index = (index % Tools.Length + Tools.Length) % Tools.Length;
            if (!Carried(index)) return;
            if (Equipped) Current.OnUnequip();
            bool putAway = index == Selected && Equipped;
            Selected = index;
            Equipped = !putAway;
            if (Equipped) Current.OnEquip();
            StopWork();
            SwitchedAt = Time.unscaledTime;
            ShowView();
            MarsSfx.Play(MarsSfx.Sound.Select);
        }

        public void Flash()
        {
            FlashAt = Time.unscaledTime;
        }

        public void Debris(ToolTarget target, Paint paint, int count)
        {
            if (!target.Hit) return;
            Vector3 toward = ViewCamera != null ? ViewCamera.transform.position - target.Point : Vector3.up;
            MarsDebris.Burst(target.Point, target.Normal + toward.normalized * 0.5f, paint, count, 0.05f, expedition.Units(1f));
        }

        private void Update()
        {
            if (expedition == null) return;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            Camera cam = Camera.main;

            // Tools work only while walking as the astronaut on a running mission (not on the home screen,
            // in the F2 fly camera, in the shop or pause menu).
            var mission = MarsMissionController.Instance;
            bool walking = !MarsHomeScreen.IsOpen && player != null && cam != null && cam.transform.IsChildOf(player.transform) &&
                           mission != null && mission.IsRunning && !mission.InputBlocked;
            if (walking != Active || cam != ViewCamera) SetActive(walking, cam);
            if (!Active) return;

            UpdateHotbar();
            HandleInput();
            Target = FindTarget();
            if (CurrentCarried)
            {
                Plan = Current.Plan(Target);
                SecondaryLabel = Current.SecondaryLabel(Target);
                Readout = Current.Readout(Target);
            }
            else
            {
                Plan = null;   // empty hands: nothing to do with a click
                SecondaryLabel = null;
                Readout = null;
            }
            UpdateWork();
            UpdateZoom();
        }

        // The astronaut moves the camera in its own Update; animating the held tool afterwards
        // keeps it locked to the view every frame instead of trailing a frame behind.
        private void LateUpdate()
        {
            if (!Active) return;
            ShowView();
            AnimateView();
            if (CurrentCarried) HoldInHand();
            UpdateLamp();
        }

        private void SetActive(bool active, Camera cam)
        {
            Active = active;
            if (!active) StopWork();
            if (active && (ViewCamera != cam || viewRoot == null))
            {
                ViewCamera = cam;
                BuildViews(cam);
            }
            if (viewRoot != null) viewRoot.gameObject.SetActive(active);
            if (lamp != null) lamp.enabled = lampOn && active;
            if (!active) RestoreFov();
            if (cam != null) lastCameraRotation = MountFor(cam).rotation;
        }

        // ------------------------------------------------------------------ input and work

        private bool MouseCaptured { get { return Cursor.lockState == CursorLockMode.Locked; } }

        /// <summary>
        /// The astronaut's eyes: aiming, reach and the held tool hang off these rather than the
        /// camera, so they stay with the astronaut when the camera pulls back into third person.
        /// </summary>
        private Transform MountFor(Camera cam)
        {
            Transform eyes = player != null ? player.AimTransform : null;
            return eyes != null ? eyes : cam.transform;
        }

        private Transform Aim { get { return MountFor(ViewCamera); } }

        /// <summary>True while the mouse pointer, not the screen centre, aims (shift lock off).</summary>
        public bool PointerAims { get { return player != null && player.PointerAims; } }

        private void HandleInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            for (int k = 0; k < NumberKeys.Length && k < Hotbar.Count; k++)
            {
                if (Input.GetKeyDown(NumberKeys[k]) || Input.GetKeyDown(PadKeys[k])) Select(Hotbar[k]);
            }
            if (!MouseCaptured) return;
            // The scroll wheel zooms the camera (see MarsAstronautPlayer), so tools use the number keys.

            if (Input.GetKeyDown(KeyCode.R) && SecondaryLabel != null)
            {
                Current.Secondary(Target);
                kick = 1f;
            }
#endif
        }

        private void UpdateWork()
        {
            bool click = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            bool pointer = player != null && player.PointerAims;
            click = (MouseCaptured || pointer) && Input.GetMouseButtonDown(0) &&
                    !(pointer && UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
#endif
            bool usable = Plan != null && !Plan.Blocked;

            if (Working == null)
            {
                if (!click) return;
                if (!usable)
                {
                    RefusedAt = Time.unscaledTime;
                    MarsSfx.Play(MarsSfx.Sound.Deny, 0.6f);
                    return;
                }
                if (Plan.Seconds <= 0f)
                {
                    Finish(Plan);
                    return;
                }
                Working = Plan;
                Progress = 0f;
                workTime = 0f;
                lostFor = 0f;
                lastStroke = 0;
                MarsSfx.Play(MarsSfx.Sound.Start, 0.7f);
                return;
            }

            // Keep going while the same thing is still in the crosshair; forgive short slips.
            if (usable && object.Equals(Plan.Key, Working.Key))
            {
                Working = Plan; // fresh label and completion for the current aim point
                lostFor = 0f;
            }
            else
            {
                lostFor += Time.deltaTime;
                if (lostFor > aimGraceSeconds)
                {
                    StopWork();
                    return;
                }
            }

            if (lostFor <= 0f)
            {
                workTime += Time.deltaTime;
                Progress = Mathf.Clamp01(Progress + Time.deltaTime / Working.Seconds);
            }
            MarsSfx.SetLoop(lostFor <= 0f ? Current.LoopSound : MarsSfx.Loop.None);
            StrokeSounds();

            if (Progress >= 1f)
            {
                ToolAction done = Working;
                StopWork();
                Finish(done);
            }
        }

        /// <summary>Hammer blows and shovel scoops make a sound on each stroke.</summary>
        private void StrokeSounds()
        {
            ToolMotion motion = Current.Motion;
            if (motion != ToolMotion.Swing && motion != ToolMotion.Dig) return;
            int stroke = Mathf.FloorToInt(workTime * StrokesPerSecond + 0.5f);
            if (stroke > lastStroke)
            {
                lastStroke = stroke;
                MarsSfx.Play(motion == ToolMotion.Swing ? MarsSfx.Sound.Hit : MarsSfx.Sound.Dig, 0.8f);
                if (Target.Hit) MarsDebris.Burst(Target.Point, Target.Normal, motion == ToolMotion.Swing ? Paint.Mauve : Paint.Terracotta,
                                                 3, 0.035f, expedition.Units(1f));
            }
        }

        private const float StrokesPerSecond = 2.2f;

        private void Finish(ToolAction plan)
        {
            kick = 1f;
            CompletedAt = Time.unscaledTime;
            if (plan.Complete != null) plan.Complete();
            if (plan.Seconds > 0f) MarsSfx.Play(MarsSfx.Sound.Success, 0.6f);
        }

        private void StopWork()
        {
            Working = null;
            Progress = 0f;
            workTime = 0f;
            lostFor = 0f;
            MarsSfx.SetLoop(MarsSfx.Loop.None);
        }

        // ------------------------------------------------------------------ aiming

        private ToolTarget FindTarget()
        {
            ToolTarget target = new ToolTarget();
            Transform view = Aim;
            Ray ray = new Ray(view.position, view.forward);
            float reach = expedition.Reach;

            float best = reach;
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                best = hit.distance;
                target.Hit = true;
                target.Point = hit.point;
                target.Normal = hit.normal;
                target.Distance = hit.distance;
                target.Collider = hit.collider;
            }

            // The terrain has no colliders, so march along the ray against the heightmap.
            if (expedition.Terrain != null && expedition.Terrain.IsReady)
            {
                const int Steps = 48;
                float step = best / Steps;
                float previous = 0f;
                for (int i = 1; i <= Steps; i++)
                {
                    float d = i * step;
                    Vector3 p = ray.GetPoint(d);
                    if (p.y > expedition.Terrain.GetGroundY(p))
                    {
                        previous = d;
                        continue;
                    }
                    float lo = previous, hi = d;
                    for (int k = 0; k < 6; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        Vector3 m = ray.GetPoint(mid);
                        if (m.y > expedition.Terrain.GetGroundY(m)) lo = mid;
                        else hi = mid;
                    }
                    Vector3 ground = ray.GetPoint(hi);
                    ground.y = expedition.Terrain.GetGroundY(ground);
                    target.Hit = true;
                    target.Ground = true;
                    target.Point = ground;
                    target.Normal = Vector3.up;
                    target.Distance = hi;
                    target.Collider = null;
                    break;
                }
            }

            AimAtPlant(ray, reach, target);
            return target;
        }

        /// <summary>
        /// Greenhouse aim assist: the plant spot nearest the crosshair within reach wins, even if
        /// the ray first clips the bed edge or a front-row plant, so the far row can be worked
        /// from the aisle without climbing onto the bed.
        /// </summary>
        private static void AimAtPlant(Ray ray, float reach, ToolTarget target)
        {
            const float AssistDegrees = 9f;
            MarsTaskTarget best = null;
            float bestAngle = AssistDegrees;
            foreach (MarsTaskTarget t in MarsTaskTarget.Usables)
            {
                if (t == null || t.kind != MarsTaskTarget.Kind.Plant) continue;
                Vector3 centre = t.transform.position + Vector3.up * 0.2f;
                Vector3 to = centre - ray.origin;
                if (to.magnitude > reach) continue;
                float angle = Vector3.Angle(ray.direction, to);
                if (angle < bestAngle) { bestAngle = angle; best = t; }
            }
            if (best == null) return;
            Collider collider = best.GetComponent<Collider>();
            if (collider == null) return;
            Vector3 point = best.transform.position + Vector3.up * 0.2f;
            target.Hit = true;
            target.Ground = false;
            target.Collider = collider;
            target.Point = point;
            target.Normal = Vector3.up;
            target.Distance = Vector3.Distance(ray.origin, point);
        }

        // ------------------------------------------------------------------ first-person view

        private void BuildViews(Camera cam)
        {
            if (viewRoot != null) Destroy(viewRoot.gameObject);
            viewRoot = new GameObject("Held Tool").transform;
            viewRoot.SetParent(MountFor(cam), false);
            pivots = new Transform[Tools.Length];
            models = new Transform[Tools.Length];
            for (int i = 0; i < Tools.Length; i++)
            {
                MarsTool tool = Tools[i];
                Transform pivot = new GameObject(tool.Name).transform;
                pivot.SetParent(viewRoot, false);
                CropItem crop = tool as CropItem;
                GameObject model = crop != null
                    ? (MarsWorldTasks.Instance == null ? null : MarsWorldTasks.Instance.Model(crop.Seed.name + "Item", pivot).gameObject)
                    : MarsLowPoly.LoadTool(tool.Id, pivot);
                if (model == null)
                {
                    LowPolyMesh fallback = new LowPolyMesh();
                    fallback.Box(new Vector3(0f, 0.12f, 0f), new Vector3(0.06f, 0.24f, 0.06f), Paint.DustyRose);
                    model = fallback.Build("Placeholder", pivot);
                }
                // The grip sits on the pivot, and the tool is turned so it is held the right way up.
                model.transform.localRotation = HoldRotation(tool);
                model.transform.localPosition = -(model.transform.localRotation * tool.Grip);
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                pivot.gameObject.SetActive(false);
                pivots[i] = pivot;
                models[i] = model.transform;
                if (tool is WorkLamp) PrepareLens(model.GetComponent<MeshRenderer>());
            }

            BuildLamp(cam);

            baseFov = -1f;
            SwitchedAt = Time.unscaledTime;
            basePosition = HoldPosition(Current.Pose) + Current.ViewOffset;
            baseAngles = Vector3.zero;
            ShowView();
        }

        /// <summary>Turns the model so its working end and top face the pose's directions (camera space).</summary>
        private static Quaternion HoldRotation(MarsTool tool)
        {
            Vector3 forward, up;
            switch (tool.Pose)
            {
                case ToolPose.Point: forward = new Vector3(-0.05f, 0.03f, 1f); up = Vector3.up; break;
                case ToolPose.Pole: forward = new Vector3(-0.28f, -0.22f, 0.93f); up = Vector3.up; break;
                case ToolPose.Camera: forward = Vector3.forward; up = Vector3.up; break;
                // Upright: leaning a little away and in toward the middle, like a hand holding it up.
                default: forward = new Vector3(-0.08f, -0.28f, 1f); up = new Vector3(-0.12f, 1f, 0f); break;
            }
            return Quaternion.LookRotation(forward, up) * Quaternion.Inverse(Quaternion.LookRotation(tool.ModelForward, tool.ModelUp));
        }

        /// <summary>
        /// Where the glove holds each pose, in camera space (metres). All within the right
        /// arm's 0.6 m reach from the shoulder when looking straight ahead or down.
        /// </summary>
        private static Vector3 HoldPosition(ToolPose pose)
        {
            switch (pose)
            {
                case ToolPose.Point: return new Vector3(0.2f, -0.24f, 0.42f);
                case ToolPose.Pole: return new Vector3(0.2f, -0.28f, 0.4f);
                case ToolPose.Camera: return new Vector3(0.12f, -0.16f, 0.4f);
                default: return new Vector3(0.22f, -0.28f, 0.42f);
            }
        }

        private void ShowView()
        {
            if (pivots == null) return;
            for (int i = 0; i < pivots.Length; i++)
            {
                bool show = i == Selected && Equipped && Carried(i);
                if (pivots[i].gameObject.activeSelf != show) pivots[i].gameObject.SetActive(show);
            }
        }

        private void AnimateView()
        {
            if (pivots == null) return;
            MarsTool tool = Current;
            Transform pivot = pivots[Selected];
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float settle = 1f - Mathf.Exp(-14f * dt);

            // Mouse sway: the tool lags a little behind where you look.
            Quaternion now = Aim.rotation;
            Quaternion delta = Quaternion.Inverse(lastCameraRotation) * now;
            lastCameraRotation = now;
            Vector3 euler = delta.eulerAngles;
            Vector2 look = new Vector2(Mathf.DeltaAngle(0f, euler.y), Mathf.DeltaAngle(0f, euler.x));
            sway = Vector2.Lerp(sway, Vector2.ClampMagnitude(-look * 0.8f, 8f), 1f - Mathf.Exp(-10f * dt));

            // Footsteps: same stride lengths as the astronaut (1.25 m walking, 3.6 m sprinting per two steps).
            float speed = player != null ? player.Speed : 0f;
            bool grounded = player == null || player.IsGrounded;
            bool sprinting = player != null && player.IsSprinting;
            moveBlend = Mathf.MoveTowards(moveBlend, Mathf.Clamp01(speed / 0.9f), dt * 4f);
            sprintBlend = Mathf.MoveTowards(sprintBlend, sprinting && speed > 2f ? 1f : 0f, dt * 5f);
            airBlend = Mathf.MoveTowards(airBlend, grounded ? 0f : 1f, dt * (grounded ? 8f : 3f));
            if (grounded && !wasGrounded) landDip = 1f;
            wasGrounded = grounded;
            landDip = Mathf.MoveTowards(landDip, 0f, dt * 3.5f);
            float strideLength = Mathf.Lerp(1.25f, 3.6f, sprintBlend);
            if (grounded) walkPhase += dt * 2f * Mathf.PI * speed / strideLength;
            else walkPhase += dt * 2f; // keep drifting gently in the air

            // Lag a little behind the way you are moving (in camera space).
            Vector3 velocity = Vector3.zero;
            if (player != null)
            {
                Vector3 moved = player.transform.position - lastPlayerPosition;
                lastPlayerPosition = player.transform.position;
                velocity = moved.sqrMagnitude < expedition.Units(5f) * expedition.Units(5f) ? moved / dt : Vector3.zero; // ignore teleports
                velocity /= Mathf.Max(1e-4f, expedition.Units(1f));
            }
            Vector3 localVelocity = Aim.InverseTransformDirection(velocity);
            lag = Vector3.Lerp(lag, Vector3.ClampMagnitude(-localVelocity * 0.006f, 0.05f), 1f - Mathf.Exp(-6f * dt));

            float raise = Mathf.Clamp01((Time.unscaledTime - SwitchedAt) / 0.3f);
            raise = 1f - (1f - raise) * (1f - raise) * (1f - raise);

            // Base pose: where the tool is held, lowered and turned while sprinting.
            Vector3 position = HoldPosition(tool.Pose) + tool.ViewOffset;
            Vector3 angles = Vector3.zero;
            position += new Vector3(0.05f, -0.07f, -0.06f) * sprintBlend;
            angles += new Vector3(22f, -24f, 12f) * sprintBlend;
            position.y -= (1f - raise) * 0.4f;
            angles.x += (1f - raise) * 30f;

            if (Working != null && !AimLost)
            {
                float t = workTime;
                float stroke = Mathf.Repeat(t * StrokesPerSecond, 1f);
                switch (tool.Motion)
                {
                    case ToolMotion.Swing:
                        // Lift back, then strike forward and down.
                        float swing = stroke < 0.6f ? -25f * Mathf.Sin(stroke / 0.6f * Mathf.PI * 0.5f) : Mathf.Lerp(-25f, 50f, (stroke - 0.6f) / 0.4f);
                        angles.x += swing;
                        position.z += Mathf.Max(0f, swing) * 0.002f;
                        break;
                    case ToolMotion.Dig:
                        float scoop = Mathf.Sin(stroke * Mathf.PI * 2f);
                        position += new Vector3(0f, -0.06f * Mathf.Max(0f, scoop), 0.12f * Mathf.Max(0f, scoop));
                        angles.x += 18f * scoop;
                        break;
                    case ToolMotion.Sweep:
                        angles.y += Mathf.Sin(t * 9f) * 24f;
                        position.x += Mathf.Sin(t * 9f) * 0.08f;
                        break;
                    case ToolMotion.Buzz:
                        position += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * 0.004f;
                        position.z += 0.06f;
                        angles.z += Mathf.Sin(t * 50f) * 2f;
                        break;
                    case ToolMotion.Spray:
                        position.z += 0.05f;
                        angles.x += 20f + Mathf.Sin(t * 14f) * 3f;
                        break;
                    default:
                        position = Vector3.Lerp(position, new Vector3(0.08f, -0.2f, 0.38f), Mathf.Clamp01(t * 6f));
                        break;
                }
            }

            // The pose eases to its target; the movement below is added on top every frame, unsmoothed,
            // so steps and look sway show at full size and in time with the camera.
            basePosition = Vector3.Lerp(basePosition, position, settle);
            baseAngles = new Vector3(Mathf.LerpAngle(baseAngles.x, angles.x, settle),
                                     Mathf.LerpAngle(baseAngles.y, angles.y, settle),
                                     Mathf.LerpAngle(baseAngles.z, angles.z, settle));

            // Bob: down on each footstep (twice per stride), side to side once per stride, bigger when running.
            float stepAmount = moveBlend * (1f - airBlend);
            float bobY = Mathf.Lerp(0.022f, 0.05f, sprintBlend) * stepAmount;
            float bobX = Mathf.Lerp(0.018f, 0.04f, sprintBlend) * stepAmount;
            Vector3 bob = new Vector3(Mathf.Sin(walkPhase) * bobX, -Mathf.Abs(Mathf.Cos(walkPhase)) * bobY + bobY * 0.5f, 0f);
            Vector3 bobAngles = new Vector3(Mathf.Abs(Mathf.Cos(walkPhase)) * 4f, Mathf.Sin(walkPhase) * 3f, Mathf.Sin(walkPhase) * 5f) *
                                stepAmount * Mathf.Lerp(1f, 1.6f, sprintBlend);

            // Idle breathing, floating in the low gravity, and a dip when landing.
            Vector3 idle = new Vector3(0f, Mathf.Sin(Time.time * 1.6f) * 0.004f, 0f) * (1f - moveBlend);
            Vector3 air = new Vector3(0f, 0.03f * airBlend - 0.05f * landDip * landDip, 0f);
            Vector3 airAngles = new Vector3(-8f * airBlend + 10f * landDip, 0f, 0f);

            kick = Mathf.MoveTowards(kick, 0f, dt * 4f);
            Vector3 kickOffset = new Vector3(0f, 0f, -kick * 0.05f);

            pivot.localPosition = basePosition + bob + idle + air + lag + kickOffset;
            pivot.localRotation = Quaternion.Euler(baseAngles + bobAngles + airAngles +
                                                   new Vector3(sway.y - kick * 10f, sway.x, -sway.x * 0.6f));
        }

        // ------------------------------------------------------------------ hand and lamp

        /// <summary>
        /// Puts the astronaut's right glove on the tool's grip: the tool is posed in camera
        /// space (with bob and sway), then the arm bends to hold it, so tool and hand move
        /// together. If the grip is out of reach (looking up), the tool is pulled back toward
        /// the shoulder rather than leaving the hand.
        /// </summary>
        private void HoldInHand()
        {
            if (pivots == null || Zoomed) return;
            if (body == null && player != null) body = player.GetComponentInChildren<MarsAstronautBody>();
            if (body == null || !body.IsBuilt || !body.gameObject.activeInHierarchy) return;

            Transform pivot = pivots[Selected];
            Vector3 grip = pivot.position;
            Vector3 shoulder = body.ShoulderPosition(true);
            float reach = body.ArmReach(true) * 0.97f;
            Vector3 toGrip = grip - shoulder;
            if (toGrip.magnitude > reach)
            {
                grip = shoulder + toGrip.normalized * reach;
                pivot.position = grip;
            }
            Vector3 handleUp = models[Selected].TransformDirection(Vector3.up);
            // Elbow out to the side, down and a little back, like holding something in front of you.
            Vector3 elbowHint = body.transform.TransformPoint(new Vector3(0.62f, 0.95f, -0.12f));
            body.ReachArm(true, grip, handleUp, elbowHint);
        }

        private void BuildLamp(Camera cam)
        {
            if (lamp != null) Destroy(lamp.gameObject);
            if (helmetMount != null) Destroy(helmetMount.gameObject);

            // Helmet light just above the eyes; used when the lamp is clipped on (another tool in hand).
            helmetMount = new GameObject("Helmet Lamp Mount").transform;
            helmetMount.SetParent(MountFor(cam), false);
            helmetMount.localPosition = new Vector3(0f, 0.09f, 0.12f);

            GameObject lampObject = new GameObject("Work Lamp Light");
            lampObject.transform.SetParent(helmetMount, false);
            lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.renderMode = LightRenderMode.ForcePixel;
            lamp.color = new Color(1f, 0.9f, 0.78f);
            // Unity lights fade with the square of distance: this lights the ground well to about 15 m.
            lamp.intensity = 28f;
            lamp.range = expedition.Units(40f);
            lamp.spotAngle = 62f;
            lamp.innerSpotAngle = 24f;
            lamp.cookie = BeamCookie();
            lamp.shadows = LightShadows.Soft;
            lamp.shadowStrength = 0.85f;
            lamp.shadowBias = 0.03f;
            lamp.shadowNormalBias = 0.3f;
            lamp.shadowNearPlane = expedition.Units(0.15f);
            SetHighShadowResolution(lamp);
            lamp.enabled = lampOn && Active;
        }

        /// <summary>
        /// Keeps the light where the lamp is: at the lens of the lamp in your hand, pointing where
        /// it points (so it sways with your steps), or on the helmet when another tool is held.
        /// The lens glows while the lamp is on.
        /// </summary>
        private void UpdateLamp()
        {
            if (lamp == null || models == null) return;
            // The lamp only shines while it is in the hotbar (picked up from its rack).
            if (lampOn && !Carried(System.Array.IndexOf(MarsTool.Order, "work_lamp"))) lampOn = false;
            bool inHand = Current is WorkLamp && !Zoomed && CurrentCarried;
            Transform mount = inHand ? models[Selected] : helmetMount;
            if (lamp.transform.parent != mount) lamp.transform.SetParent(mount, false);
            if (inHand)
            {
                // The lens is at the top of the torch (model +Y); the switch side (-X) faces up.
                lamp.transform.localPosition = new Vector3(0f, 0.32f, 0f);
                lamp.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.left);
            }
            else
            {
                lamp.transform.localPosition = Vector3.zero;
                lamp.transform.localRotation = Quaternion.identity;
            }
            lamp.enabled = lampOn;
            if (lampRenderer != null && lensOn != null && lensGlowing != lampOn)
            {
                lensGlowing = lampOn;
                lampRenderer.sharedMaterials = lampOn ? lensOn : lensOff;
            }
        }

        /// <summary>Makes a glowing copy of the lamp's materials with the lens (pale pink) swapped for a bright unlit one.</summary>
        private void PrepareLens(MeshRenderer renderer)
        {
            if (renderer == null) return;
            lampRenderer = renderer;
            lensGlowing = false;
            lensOff = renderer.sharedMaterials;
            lensOn = (Material[])lensOff.Clone();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) return;
            Material glow = new Material(unlit);
            glow.name = "Lamp Lens Glow";
            glow.hideFlags = HideFlags.DontSave;
            Color warm = new Color(1f, 0.95f, 0.85f);
            glow.color = warm;
            if (glow.HasProperty("_BaseColor")) glow.SetColor("_BaseColor", warm);
            Material lens = MarsLowPoly.Material(Paint.PalePink);
            for (int i = 0; i < lensOn.Length; i++)
            {
                if (lensOn[i] == lens) lensOn[i] = glow;
            }
        }

        /// <summary>
        /// A soft round beam for the spot light's cookie: a bright core, a gentle falloff to a
        /// dark edge and a faint outer ring, like a real torch.
        /// </summary>
        private static Texture2D BeamCookie()
        {
            const int Size = 128;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.name = "Work Lamp Beam";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;
            Color32[] pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size * 2f - 1f;
                    float dy = (y + 0.5f) / Size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float fill = 1f - Mathf.SmoothStep(0.45f, 0.95f, r);
                    float core = 1f - Mathf.SmoothStep(0f, 0.35f, r);
                    float ring = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.06f, 2f));
                    float v = Mathf.Clamp01(0.72f * fill + 0.28f * core + 0.08f * ring * fill);
                    if (r > 0.98f) v = 0f;
                    byte b = (byte)Mathf.RoundToInt(v * 255f);
                    pixels[y * Size + x] = new Color32(b, b, b, b);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Asks URP for its high shadow-map tier for this light (by reflection, so no compile-time URP reference).</summary>
        private static void SetHighShadowResolution(Light light)
        {
            System.Type dataType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
            if (dataType == null) return;
            Component data = light.GetComponent(dataType);
            if (data == null) data = light.gameObject.AddComponent(dataType);
            System.Reflection.PropertyInfo tier = dataType.GetProperty("additionalLightsShadowResolutionTier");
            if (tier != null && tier.CanWrite) tier.SetValue(data, 2, null); // 0 low, 1 medium, 2 high
        }


        private void UpdateZoom()
        {
            if (ViewCamera == null) return;
            if (baseFov < 0f) baseFov = ViewCamera.fieldOfView;
            float goal = Zoomed ? baseFov * 0.4f : baseFov;
            ViewCamera.fieldOfView = Mathf.Lerp(ViewCamera.fieldOfView, goal, 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (viewRoot != null) viewRoot.gameObject.SetActive(!Zoomed);
        }

        private void RestoreFov()
        {
            Zoomed = false;
            if (ViewCamera != null && baseFov > 0f) ViewCamera.fieldOfView = baseFov;
            baseFov = -1f;
        }
    }
}
