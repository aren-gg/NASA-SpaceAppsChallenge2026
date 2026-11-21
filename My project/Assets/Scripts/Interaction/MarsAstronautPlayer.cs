using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// First-person astronaut. Walks on the streamed terrain using
    /// <see cref="MarsTerrain.GetGroundY"/> (the terrain has no colliders by default),
    /// with Mars gravity. The main camera is moved to the astronaut's eyes and a
    /// procedural suited body (<see cref="MarsAstronautBody"/>) is built underneath it.
    ///
    /// Buildings and other solid objects with colliders (such as the outpost) block the
    /// astronaut and can be stood on; the terrain itself still uses GetGroundY.
    ///
    /// Controls: WASD move, mouse looks, Left Shift sprints, Space jumps, Esc frees the
    /// cursor (click to capture it again), F2 switches to the free inspection camera
    /// and back. All speeds and sizes are in real metres.
    ///
    /// A player is added automatically when a scene with a MarsTerrain starts without
    /// one, so no scene edit is needed.
    /// </summary>
    public sealed class MarsAstronautPlayer : MonoBehaviour
    {
        public MarsTerrain terrain;
        public Camera viewCamera;

        [Header("Movement (real metres, seconds)")]
        [Tooltip("6 km/h, a brisk human walk.")]
        public float walkSpeed = 6f / 3.6f;
        [Tooltip("29 km/h.")]
        public float sprintSpeed = 29f / 3.6f;
        [Tooltip("Speeding up on the ground, m/s². Boots push off the regolith, so this is a firm but finite push.")]
        public float groundAcceleration = 16f;
        [Tooltip("Slowing down or reversing on the ground, m/s². Planting the feet stops you quicker than pushing off starts you.")]
        public float groundBraking = 24f;
        [Tooltip("Below this speed the astronaut reaches walking pace almost at once, the way a first step does.")]
        public float stepOffSpeed = 1.2f;
        public float airAcceleration = 2.5f;
        [Tooltip("Mars surface gravity.")]
        public float gravity = 3.71f;
        [Tooltip("Height of a standing jump under the gravity above.")]
        public float jumpHeight = 1.6f;
        [Tooltip("The astronaut stays stuck to ground that drops by less than this per frame.")]
        public float stepDown = 0.35f;

        [Header("View")]
        [Tooltip("Base mouse look speed; the Camera Sensitivity setting multiplies it.")]
        public float lookSensitivity = 2f;
        [Tooltip("With shift lock off, how fast the astronaut turns to face the way they walk (degrees per second).")]
        public float turnSpeed = 600f;
        [Tooltip("Sideways grip of the boots while changing direction (m/s²). Higher turns tighter at speed.")]
        public float turnGrip = 20f;
        [Range(40f, 100f)] public float fieldOfView = 70f;
        [Tooltip("Eye height of the 1.8 m astronaut.")]
        public float eyeHeight = 1.68f;

        [Header("Third-person camera (real metres); the scroll wheel zooms")]
        [Tooltip("Distance behind the head at the start. 0 is first person.")]
        public float thirdPersonDistance = 4f;
        [Tooltip("Closest third-person distance; scrolling in past it switches to first person.")]
        public float minCameraDistance = 1.4f;
        public float maxCameraDistance = 9f;
        [Tooltip("How far each scroll-wheel notch moves the camera.")]
        public float scrollStep = 0.9f;
        [Tooltip("Sideways camera offset in shift lock (metres, + is right), so the astronaut sits a little left of " +
                 "the crosshair, like Roblox's shift lock (1.75 studs, about 0.5 m). The offset is swept out from the " +
                 "head, so it shortens against walls and door frames instead of putting the camera inside them. " +
                 "Without shift lock the camera stays centred.")]
        public float shoulderOffset = 0.5f;
        [Tooltip("How far above eye level the camera rides in third person, so the helmet sits just below the crosshair. " +
                 "Kept low enough to pass under the 2.15 m door frames and the 2.3 m tunnel ceilings.")]
        public float cameraLift = 0.35f;

        [Header("Collision with buildings (real metres)")]
        public float bodyRadius = 0.35f;
        [Tooltip("Ledges up to this height above the feet are stepped onto rather than blocking.")]
        public float stepUp = 0.45f;

        [Header("Stride length per full cycle (two steps), metres")]
        public float walkStride = 1.55f;
        public float sprintStride = 3.6f;

        public bool IsGrounded { get { return grounded; } }
        /// <summary>True while the astronaut is aboard a vehicle and the vehicle owns the camera.</summary>
        public bool IsDriving { get { return driving; } }
        public Camera ViewCamera { get { return viewCamera; } }
        /// <summary>True once the astronaut has been set down on the ground at the start.</summary>
        public bool IsReady { get { return placed && neck != null; } }
        /// <summary>True while the camera sits behind the astronaut rather than at the eyes.</summary>
        public bool IsThirdPerson { get { return cameraDistance > 0.5f; } }
        /// <summary>
        /// True when the mouse turns the view and the astronaut faces the camera: always in
        /// first person, and in third person while shift lock is on (as in Roblox).
        /// </summary>
        public bool CameraLocked
        {
            // In the rover there is no first person: only shift lock captures the cursor.
            get { return (!driving && cameraDistanceGoal < 0.5f) || MarsTrainer.UI.CameraPrefs.ShiftLockActive; }
        }

        /// <summary>
        /// This frame's mouse turn in degrees (x right, y up), already scaled by the camera
        /// sensitivity setting; zero while the mouse is not turning the view. The astronaut
        /// applies it to its own view; the rover reads it for its chase camera.
        /// </summary>
        public Vector2 LookDelta { get; private set; }

        /// <summary>True while the player is not using the scroll wheel for anything else (the cursor is in the game).</summary>
        public bool ScrollFree { get { return !cursorReleased; } }
        /// <summary>
        /// The astronaut's eyes, turned toward whatever is under the crosshair. Reach checks,
        /// tool aiming and the held tool use this, so they work the same in first and third person.
        /// </summary>
        public Transform AimTransform { get { return eye != null ? eye : (viewCamera != null ? viewCamera.transform : null); } }
        public bool IsSprinting { get { return sprinting; } }
        /// <summary>The suited body (for the wardrobe preview).</summary>
        public MarsAstronautBody Body { get { return body; } }
        /// <summary>Horizontal speed in real metres per second.</summary>
        public float Speed { get { return new Vector2(velocity.x, velocity.z).magnitude; } }

        private const float NeckHeight = 1.55f;
        private const float EyeForward = 0.10f;

        private Transform neck, eye, rig;
        private float cameraDistance, cameraDistanceGoal, shoulderBlend = 1f, bodyYaw, boomLength;
        private bool cursorReleased, lookLocked;
        private int relockFrames;
        // Horizontal position kept in double precision, so tiny per-frame steps are never
        // rounded away (which bends the walking line when far from the world origin).
        private double exactX, exactZ;
        private Vector3 lastWritten = new Vector3(float.NaN, 0f, 0f);
        private float relockUntil;
        // Right-drag camera turn with shift lock off: the OS pointer is pinned where it was clicked.
        private bool dragActive, dragPinned;
        private int skipLookFrames;
        private MarsAstronautBody body;
        private MarsTerrainFlyCamera flyCamera;
        private Vector3 velocity;          // metres per second, world axes
        private float yaw, pitch;
        private bool grounded, sprinting, placed, flying, driving;
        private float jumpBuffer;
        private float phase, moveBlend, sprintBlend, airBlend;
        private float landDip, cameraDip;
        private CapsuleCollider capsule;
        private readonly Collider[] overlaps = new Collider[16];
        private bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AddToTerrainScene()
        {
            if (FindObjectOfType<MarsAstronautPlayer>() != null) return;
            if (FindObjectOfType<MarsTerrain>() == null || Camera.main == null) return;
            new GameObject("Astronaut Player").AddComponent<MarsAstronautPlayer>();
        }

        private void Start()
        {
            // While the home screen is up it owns the camera; take over once it closes.
            if (!MarsSurvival.UI.MarsHomeScreen.IsOpen) Begin();
        }

        private void Begin()
        {
            started = true;
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null)
            {
                Debug.LogError("MarsAstronautPlayer needs a camera tagged MainCamera.", this);
                enabled = false;
                return;
            }

            // Start where the camera was, facing the way it faced.
            Transform cam = viewCamera.transform;
            transform.position = cam.position;
            yaw = bodyYaw = cam.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.localScale = Vector3.one * Units(1f);

            // Trigger, so it never shoves anything; used only to measure overlaps with buildings.
            capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = bodyRadius;
            capsule.height = Mathf.Max(bodyRadius * 2f, 1.85f - stepUp);
            capsule.center = new Vector3(0f, stepUp + capsule.height * 0.5f, 0f);

            neck = new GameObject("Neck").transform;
            neck.SetParent(transform, false);
            neck.localPosition = new Vector3(0f, NeckHeight, 0f);
            eye = new GameObject("Eyes").transform;
            eye.SetParent(neck, false);
            eye.localPosition = EyeLocal;
            // The camera hangs off its own rig so it can orbit freely while the body turns to
            // face where it walks (shift lock off); with shift lock on they turn together.
            rig = new GameObject("Camera Rig").transform;
            rig.SetParent(transform, false);
            cameraDistance = cameraDistanceGoal = Mathf.Max(0f, thirdPersonDistance);

            flyCamera = viewCamera.GetComponent<MarsTerrainFlyCamera>();
            if (flyCamera != null) flyCamera.enabled = false;
            MountCamera();
            viewCamera.fieldOfView = fieldOfView;
            viewCamera.nearClipPlane = Units(0.05f);
            if (terrain != null) terrain.viewer = cam;

            GameObject bodyObject = new GameObject("Astronaut Body");
            bodyObject.transform.SetParent(transform, false);
            body = bodyObject.AddComponent<MarsAstronautBody>();
            body.Build();
            PlaceCamera(0f);

            LockCursor(true);
        }

        private void MountCamera()
        {
            Transform cam = viewCamera.transform;
            cam.SetParent(rig, false);
            cam.localPosition = EyeLocal;
            cam.localRotation = Quaternion.identity;
            cam.localScale = Vector3.one;
            if (body != null) PlaceCamera(0f);
        }

        private Vector3 EyeLocal { get { return new Vector3(0f, eyeHeight - NeckHeight, EyeForward); } }

        /// <summary>
        /// Puts the camera at the eyes (first person) or behind and just above the head
        /// (third person), pulled in when a wall or the ground is in the way. Then turns the
        /// eyes toward what the crosshair is on, so aiming matches what the player sees.
        /// </summary>
        private void PlaceCamera(float dt)
        {
            if (neck == null || eye == null || rig == null || viewCamera == null || viewCamera.transform.parent != rig) return;
            cameraDistance = dt <= 0f ? cameraDistanceGoal
                : Mathf.Lerp(cameraDistance, cameraDistanceGoal, 1f - Mathf.Exp(-12f * dt));
            if (Mathf.Abs(cameraDistance - cameraDistanceGoal) < 0.01f) cameraDistance = cameraDistanceGoal;

            // The rig sits at the neck and turns with the camera's own yaw and pitch.
            rig.position = neck.position;
            rig.rotation = Quaternion.Euler(pitch, yaw, 0f);

            // Blend the third-person offsets in over the first two metres so zooming is smooth.
            float t = Mathf.Clamp01(cameraDistance / 2f);
            float shoulderGoal = MarsTrainer.UI.CameraPrefs.ShiftLockActive ? 1f : 0f;
            shoulderBlend = dt <= 0f ? shoulderGoal : Mathf.MoveTowards(shoulderBlend, shoulderGoal, dt * 4f);
            Transform cam = viewCamera.transform;

            // The boom pivots at the head, centred over the body, so it always starts inside
            // the space the astronaut is standing in (a doorway, a tunnel), never in a wall.
            Vector3 head = rig.TransformPoint(EyeLocal + new Vector3(0f, 0f, -EyeForward * t));
            Vector3 pivot = head;
            if (Mathf.Abs(shoulderOffset) > 1e-4f)
            {
                Vector3 side = rig.right * Units(shoulderOffset * t * shoulderBlend);
                RaycastHit sideHit;
                float sideLength = side.magnitude;
                if (sideLength > 1e-5f && Physics.SphereCast(head, Units(0.12f), side / sideLength, out sideHit, sideLength,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    side = side / sideLength * sideHit.distance;
                pivot = head + side;
            }
            // Behind the head along the view, and a little above eye level (world up, so looking
            // down does not swing the camera into the floor).
            Vector3 wanted = pivot - rig.forward * Units(cameraDistance) + Vector3.up * Units(cameraLift * t);
            Vector3 toward = wanted - pivot;
            float length = toward.magnitude;
            float allowed = length;
            if (length > 1e-5f)
            {
                RaycastHit hit;
                if (Physics.SphereCast(pivot, Units(0.12f), toward / length, out hit, length,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    allowed = hit.distance;
                }
            }
            // Pull in at once when something gets in the way, but ease back out, so walking
            // through a doorway or past a door frame does not make the camera pop in and out.
            if (dt <= 0f || allowed <= boomLength) boomLength = allowed;
            else boomLength = Mathf.Min(allowed, boomLength + Units(6f) * dt);
            wanted = length > 1e-5f ? pivot + toward / length * boomLength : pivot;
            // Outside on bare terrain, keep the camera above the ground. Not on building floors:
            // the terrain surface can lie above an outpost floor and would push the camera into the roof.
            if (TerrainReady && t > 0f && transform.position.y <= terrain.GetGroundY(transform.position) + Units(0.3f))
            {
                float floor = terrain.GetGroundY(wanted) + Units(0.3f);
                if (wanted.y < floor) wanted.y = floor;
            }
            cam.position = wanted;
            cam.localRotation = Quaternion.identity;

            eye.localPosition = EyeLocal;
            eye.localRotation = Quaternion.identity;
            // Hide the helmet when the camera is squeezed up against it (tight spots).
            if (body != null) body.SetHeadVisible(t > 0.25f && Vector3.Distance(wanted, head) > Units(0.7f));
            if (t <= 0f) return;

            // Third person: aim the eyes at the point under the crosshair.
            Ray ray = new Ray(cam.position, cam.forward);
            float far = Units(40f);
            Vector3 target = ray.GetPoint(far);
            RaycastHit aimHit;
            if (Physics.Raycast(ray, out aimHit, far, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                target = aimHit.point;
                far = aimHit.distance;
            }
            if (TerrainReady)
            {
                // The terrain may have no colliders, so march along the ray to find the ground.
                float step = Units(0.5f);
                for (float d = step; d < far; d += step)
                {
                    Vector3 p = ray.GetPoint(d);
                    if (p.y <= terrain.GetGroundY(p)) { target = p; break; }
                }
            }
            Vector3 look = target - eye.position;
            // With shift lock off the body may face away from the camera; only turn the eyes
            // toward the crosshair when it is in front of the astronaut.
            Vector3 flat = new Vector3(look.x, 0f, look.z);
            bool ahead = Vector3.Angle(flat, transform.forward) < 100f;
            if (look.sqrMagnitude > Units(0.5f) * Units(0.5f) && Vector3.Dot(look, cam.forward) > 0f && ahead)
                eye.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        private float Units(float meters)
        {
            return terrain != null && terrain.config != null ? terrain.config.ToUnits(meters) : meters;
        }

        private bool TerrainReady { get { return terrain != null && terrain.IsReady; } }

        /// <summary>
        /// Highest walkable surface under the feet: the terrain, or the top of a building
        /// collider no more than <see cref="stepUp"/> above the feet (ramps, floors, roofs).
        /// </summary>
        private float GroundY(Vector3 position)
        {
            float ground = TerrainReady ? terrain.GetGroundY(position) : 0f;
            float top = position.y + Units(stepUp);
            float reach = top - ground;
            if (reach <= 0f) return ground;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(position.x, top, position.z), Vector3.down, out hit, reach,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                ground = Mathf.Max(ground, hit.point.y);
            }
            return ground;
        }

        private bool IsBuilding(Collider other)
        {
            if (other == capsule || other.isTrigger) return false;
            // Terrain chunk colliders (optional) are handled by GetGroundY, not pushed against.
            return terrain == null || !other.transform.IsChildOf(terrain.transform);
        }

        /// <summary>True when a building is just above the head (the astronaut is 1.8 m tall).</summary>
        private bool HitsCeiling(Vector3 position)
        {
            RaycastHit hit;
            return Physics.Raycast(position + Vector3.up * Units(1.5f), Vector3.up, out hit, Units(0.45f),
                       Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && IsBuilding(hit.collider);
        }

        /// <summary>Pushes the body sideways out of any building it overlaps.</summary>
        private Vector3 ResolveWalls(Vector3 position)
        {
            if (capsule == null) return position;
            for (int pass = 0; pass < 3; pass++)
            {
                transform.position = position;
                float scale = transform.lossyScale.y;
                float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius) * scale;
                Vector3 centre = transform.TransformPoint(capsule.center);
                int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.down * half, centre + Vector3.up * half,
                    capsule.radius * scale, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlaps[i];
                    if (!IsBuilding(other)) continue;
                    Vector3 direction;
                    float distance;
                    if (!Physics.ComputePenetration(capsule, position, transform.rotation,
                            other, other.transform.position, other.transform.rotation, out direction, out distance)) continue;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 1e-4f) continue;
                    // Only the sideways part of the push; the height comes from GroundY.
                    position += direction.normalized * (distance + Units(0.001f));
                    moved = true;
                }
                if (!moved) break;
            }
            return position;
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDisable()
        {
            EndDrag();
            LockCursor(false);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) relockFrames = 2;
            else EndDrag(); // never leave the pointer frozen while another app is in front
        }

        /// <summary>Ends a right-drag camera turn and frees the pointer.</summary>
        private void EndDrag()
        {
            if (!dragActive) return;
            dragActive = false;
            if (dragPinned) SystemCursor.Pin(false);
            dragPinned = false;
        }

        /// <summary>
        /// Keeps the cursor in the state the camera mode wants. After a menu, Escape or a focus
        /// change the cursor is taken again a couple of frames later (the mouse is ignored
        /// until then, so the view does not jump).
        ///
        /// In the Unity Editor, pressing Escape in the Game view also switches off cursor
        /// locking for scripts until the Game view is clicked, so Cursor.lockState = Locked is
        /// silently ignored and the pointer roams free. <see cref="AllowEditorCursorLock"/>
        /// switches it back on, the same way that click does. Builds are not affected.
        /// </summary>
        private void ApplyCursor(bool wantLocked)
        {
            if (!wantLocked)
            {
                lookLocked = false;
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible) LockCursor(false);
                return;
            }
            if (relockFrames > 0)
            {
                // Ignore the mouse for a couple of frames after returning, so the view does not jump.
                relockFrames--;
                lookLocked = false;
                if (relockFrames > 0) return;
                relockUntil = Time.unscaledTime + 1.5f;
                AllowEditorCursorLock(true);
                Cursor.lockState = CursorLockMode.None;
                LockCursor(true);
                return;
            }
            // For a moment after returning to the game, keep re-taking the cursor: the Editor
            // handles the Escape key in its own GUI pass, which can land after our first
            // attempt and switch the lock off again.
            if (Time.unscaledTime < relockUntil)
            {
                AllowEditorCursorLock(true);
                LockCursor(true);
            }
            else if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            {
                AllowEditorCursorLock(false);
                LockCursor(true);
            }
            lookLocked = Cursor.lockState == CursorLockMode.Locked;
        }

        private static float nextEditorAllow;

        /// <summary>
        /// Automated checks only: when set, replaces the WASD keys (x = strafe, y = forward),
        /// so a test can hold "W" without a keyboard.
        /// </summary>
        public static Vector2? SimulatedMove;

        /// <param name="returning">True right after leaving a menu or pressing Escape in the game,
        /// when the Game view is known to be the one in use.</param>
        private static void AllowEditorCursorLock(bool returning)
        {
#if UNITY_EDITOR
            // Otherwise only while the Game view has focus (never grab the pointer from another
            // Editor window), and at most a few times a second.
            if (!returning && !Application.isFocused) return;
            if (Time.unscaledTime < nextEditorAllow) return;
            nextEditorAllow = Time.unscaledTime + (returning ? 0.1f : 0.25f);
            try
            {
                System.Type gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                System.Reflection.MethodInfo allow = gameView == null ? null : gameView.GetMethod("AllowCursorLockAndHide",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (allow != null)
                    foreach (Object view in Resources.FindObjectsOfTypeAll(gameView)) allow.Invoke(view, new object[] { true });
                UnityEditor.Unsupported.SetAllowCursorHide(true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Could not re-enable the Editor cursor lock: " + e.Message);
            }
#endif
        }

        private void Update()
        {
            if (!started)
            {
                if (MarsSurvival.UI.MarsHomeScreen.IsOpen) return;
                Begin();
                if (!enabled) return;
            }
            if (!placed)
            {
                if (terrain != null && !terrain.IsReady) return;
                Vector3 start = transform.position;
                start.y = GroundY(start);
                transform.position = start;
                grounded = true;
                placed = true;
            }

            var mission = MarsSurvival.Gameplay.MarsMissionController.Instance;
#if ENABLE_LEGACY_INPUT_MANAGER
            // Escape opens or closes the pause menu; either way, take the cursor again cleanly afterwards.
            if (Input.GetKeyDown(KeyCode.Escape)) relockFrames = 2;
#endif
            if (mission != null && mission.InputBlocked)
            {
                // A menu or panel owns the cursor; re-capture it cleanly once it closes.
                velocity = Vector3.zero; sprinting = false; lookLocked = false; relockFrames = 2;
                EndDrag();
                LookDelta = Vector2.zero;
                return;
            }
            LookDelta = Vector2.zero;

            float forwardInput = 0f, sideInput = 0f;
            bool sprintHeld = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!driving && Debug.isDebugBuild && mission == null && Input.GetKeyDown(KeyCode.F2) && flyCamera != null) SetFlying(!flying);
            if (flying) return;

            // Shift lock toggle (Ctrl, since Shift sprints). Allowed only when the Shift Lock
            // Switch setting is on, as in Roblox.
            if ((Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl)) && MarsTrainer.UI.CameraPrefs.ShiftLockSwitch)
                MarsTrainer.UI.CameraPrefs.ShiftLockOn = !MarsTrainer.UI.CameraPrefs.ShiftLockOn;

            // Locked camera (first person or shift lock): the cursor is captured and the mouse
            // turns the view. Free camera: the cursor moves, and holding the right mouse
            // button turns the view. Escape frees it only where no pause menu takes over.
            if (Input.GetKeyDown(KeyCode.Escape)) cursorReleased = mission == null;
            else if (Input.GetMouseButtonDown(0)) cursorReleased = false;
            // Right-drag (shift lock off): the pointer stays visible and frozen exactly where you
            // clicked while the mouse turns the camera (Roblox style), and moves freely again on
            // release. Unity's own lock is not used for this, since it hides the pointer and
            // moves it to the centre. If the OS cannot pin it, fall back to the lock.
            bool dragging = Input.GetMouseButton(1) && !CameraLocked && !cursorReleased;
            if (dragging && !dragActive)
            {
                dragActive = true;
                dragPinned = SystemCursor.Pin(true);
                skipLookFrames = 1;
            }
            else if (!dragging && dragActive) EndDrag();
            ApplyCursor(!cursorReleased && (CameraLocked || (dragging && !dragPinned)));

            bool mouseTurns = lookLocked || dragPinned;
            if (mouseTurns && skipLookFrames > 0) skipLookFrames--;
            else if (mouseTurns)
            {
                float sensitivity = lookSensitivity * MarsTrainer.UI.CameraPrefs.Sensitivity;
                LookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * sensitivity;
            }
            // In the rover the same cursor handling applies, but the rover turns its own chase
            // camera with LookDelta and uses the scroll wheel for its own zoom.
            if (driving) return;
            yaw += LookDelta.x;
            pitch = Mathf.Clamp(pitch - LookDelta.y, -80f, 80f);
            if (!cursorReleased) Zoom(Input.mouseScrollDelta.y);
            if (Input.GetKey(KeyCode.W)) forwardInput += 1f;
            if (Input.GetKey(KeyCode.S)) forwardInput -= 1f;
            if (Input.GetKey(KeyCode.D)) sideInput += 1f;
            if (Input.GetKey(KeyCode.A)) sideInput -= 1f;
            sprintHeld = Input.GetKey(KeyCode.LeftShift);
            if (Input.GetKeyDown(KeyCode.Space)) jumpBuffer = 0.15f;
#endif
            if (driving) return;
            if (SimulatedMove.HasValue) { sideInput = SimulatedMove.Value.x; forwardInput = SimulatedMove.Value.y; }
            float dt = Time.deltaTime;
            // The head pitches about the neck, so looking down also moves the eyes forward.
            neck.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            Move(forwardInput, sideInput, sprintHeld, dt);
            AnimateBody(dt);
            PlaceCamera(dt);
            UpdateHelmet(mission);
        }

        /// <summary>The helmet stays sealed everywhere, indoors and out.</summary>
        private void UpdateHelmet(MarsSurvival.Gameplay.MarsMissionController mission)
        {
            if (body != null) body.SetHelmet(true);
        }

        /// <summary>Scroll up moves the camera in, past the closest distance into first person; scroll down moves it out.</summary>
        private void Zoom(float notches)
        {
            if (Mathf.Abs(notches) < 0.01f) return;
            float goal = cameraDistanceGoal - notches * scrollStep;
            if (notches > 0f && goal < minCameraDistance) goal = 0f;
            else if (notches < 0f && cameraDistanceGoal < minCameraDistance) goal = Mathf.Max(goal, minCameraDistance);
            cameraDistanceGoal = Mathf.Clamp(goal, 0f, maxCameraDistance);
        }

        /// <summary>
        /// True when a step would leave the terrain tile or climb onto terrain steeper than
        /// 25 degrees. It never traps the astronaut: steps that stay outside the tile, go
        /// downhill or start from a building floor are always allowed.
        /// </summary>
        private bool TerrainBlocks(Vector3 from, Vector3 to)
        {
            if (!TerrainReady) return false;
            if (!terrain.IsInsideTile(to)) return terrain.IsInsideTile(from);
            float fromGround = terrain.GetGroundY(from);
            float toGround = terrain.GetGroundY(to);
            bool onTerrain = from.y <= fromGround + Units(0.5f);
            return onTerrain && toGround > fromGround && terrain.GetSlopeDegrees(to) > 25f;
        }

        private void Move(float forwardInput, float sideInput, bool sprintHeld, float dt)
        {
            // Keys move relative to where the camera looks (its yaw), never the body's facing.
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 wish = heading * new Vector3(sideInput, 0f, forwardInput);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // Locked camera: the body faces the camera. Free camera: it turns to face the way it walks.
            bool locked = CameraLocked;
            if (locked) bodyYaw = yaw;
            else if (wish.sqrMagnitude > 0.01f)
                bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg, turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);

            // Sprinting works in any direction you are moving (W, A, S or D); it carries through a jump.
            sprinting = sprintHeld && wish.sqrMagnitude > 0.01f;
            Vector3 target = wish * (sprinting ? sprintSpeed : walkSpeed);
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            // Steering: on the ground the boots redirect momentum toward the new heading instead
            // of carrying the old direction sideways (which made W drift diagonally after
            // turning). The turn rate is limited by boot grip, so walking turns crisply and a
            // sprint carves a wider arc. Reversals (over ~100 degrees) brake instead.
            float speedNow = horizontal.magnitude;
            if (grounded && speedNow > 0.05f && target.sqrMagnitude > 1e-4f && Vector3.Angle(horizontal, target) < 100f)
            {
                float maxTurn = Mathf.Min(12f, turnGrip / speedNow) * dt; // radians this frame
                horizontal = Vector3.RotateTowards(horizontal, target, maxTurn, 0f);
            }
            float rate = airAcceleration;
            if (grounded)
            {
                // Speeding up in the direction of travel uses the push-off rate; stopping,
                // reversing or cutting sideways uses the stronger braking rate. A first step
                // from standing reaches walking pace quickly, as a real step does.
                bool speedingUp = target.sqrMagnitude > horizontal.sqrMagnitude && Vector3.Dot(target, horizontal) >= 0f;
                rate = speedingUp ? groundAcceleration : groundBraking;
                if (speedingUp && horizontal.magnitude < stepOffSpeed) rate *= 2f;
            }
            horizontal = Vector3.MoveTowards(horizontal, target, rate * dt);
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;

            jumpBuffer -= dt;
            if (grounded && jumpBuffer > 0f)
            {
                velocity.y = Mathf.Sqrt(2f * gravity * jumpHeight);
                grounded = false;
                jumpBuffer = 0f;
            }
            if (!grounded) velocity.y -= gravity * dt;
            else velocity.y = 0f;

            Vector3 current = transform.position;
            if (current != lastWritten) { exactX = current.x; exactZ = current.z; } // moved by something else (teleport, vehicle)
            double nextX = exactX + (double)velocity.x * Units(1f) * dt;
            double nextZ = exactZ + (double)velocity.z * Units(1f) * dt;
            Vector3 position = new Vector3((float)nextX, current.y + velocity.y * Units(1f) * dt, (float)nextZ);
            if (TerrainBlocks(current, position))
            {
                position.x = current.x; position.z = current.z;
                nextX = exactX; nextZ = exactZ;
                velocity.x = 0; velocity.z = 0;
            }
            Vector3 beforeWalls = position;
            position = ResolveWalls(position);
            if (position.x != beforeWalls.x || position.z != beforeWalls.z)
            {
                // A wall pushed us: keep its correction exactly.
                nextX += position.x - beforeWalls.x;
                nextZ += position.z - beforeWalls.z;
            }
            if (velocity.y > 0f && HitsCeiling(position))
            {
                // Head under a roof: stop rising instead of passing through it.
                velocity.y = 0f;
                position.y = transform.position.y;
            }
            float ground = GroundY(position);
            if (grounded)
            {
                // Follow slopes; walk off anything steeper than the step-down allowance.
                if (position.y - ground <= Units(stepDown)) position.y = ground;
                else grounded = false;
            }
            else if (position.y <= ground)
            {
                position.y = ground;
                if (velocity.y <= 0f)
                {
                    landDip = Mathf.Min(0.16f, -velocity.y * 0.045f);
                    velocity.y = 0f;
                    grounded = true;
                }
            }
            transform.position = position;
            exactX = nextX; exactZ = nextZ;
            lastWritten = transform.position;
        }

        private void AnimateBody(float dt)
        {
            float speed = Speed;
            float blendRate = 1f - Mathf.Exp(-10f * dt);
            moveBlend = Mathf.Lerp(moveBlend, Mathf.Clamp01(speed / walkSpeed), blendRate);
            sprintBlend = Mathf.Lerp(sprintBlend, Mathf.InverseLerp(walkSpeed * 1.2f, sprintSpeed * 0.8f, speed), blendRate);
            airBlend = Mathf.MoveTowards(airBlend, grounded ? 0f : 1f, dt * (grounded ? 8f : 16f));

            float stride = Mathf.Lerp(walkStride, sprintStride, sprintBlend);
            float cadence = speed / Mathf.Max(0.1f, stride);
            phase += 2f * Mathf.PI * cadence * dt * (grounded ? 1f : 0.3f);
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;

            float takeOffSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
            float vertical = grounded ? 0f : Mathf.Clamp(velocity.y / takeOffSpeed, -1f, 1f);
            body.Animate(phase, moveBlend, sprintBlend, airBlend, vertical, Time.time);

            // Head bob: twice per cycle vertically, once side to side; a dip on landing.
            float grip = moveBlend * (1f - airBlend);
            float bobY = -Mathf.Lerp(0.015f, 0.04f, sprintBlend) * grip * (0.5f + 0.5f * Mathf.Cos(2f * phase));
            float bobX = Mathf.Lerp(0.01f, 0.018f, sprintBlend) * grip * Mathf.Sin(phase);
            landDip *= Mathf.Exp(-6f * dt);
            cameraDip = Mathf.Lerp(cameraDip, landDip, 1f - Mathf.Exp(-25f * dt));
            neck.localPosition = new Vector3(bobX, NeckHeight + bobY - cameraDip, 0f);
        }

        /// <summary>
        /// Boards a vehicle: the astronaut is hidden and stops responding to input, and the
        /// camera is released for the vehicle to place. Returns false if that is not possible now.
        /// </summary>
        public void RestoreMissionPose(Vector3 position, float heading)
        {
            if (driving) LeaveVehicle(position, heading);
            transform.position = position;
            yaw = bodyYaw = heading; pitch = 0; velocity = Vector3.zero; grounded = true;
            transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
            MountCamera();
        }

        /// <summary>Turns the body and head to look at a world point (used by scripted checks and cut-ins).</summary>
        public void AimAt(Vector3 point)
        {
            if (neck == null) return;
            // The eyes sit ahead of the neck pivot, so refine the aim a few times from the eye.
            for (int pass = 0; pass < 4; pass++)
            {
                Vector3 from = eye != null ? eye.position : neck.position;
                Vector3 d = point - from;
                if (d.sqrMagnitude < 1e-6f) return;
                yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -80f, 80f);
                bodyYaw = yaw;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                neck.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                if (eye != null) eye.localRotation = Quaternion.identity;
            }
            PlaceCamera(0f);
        }

        public bool BoardVehicle()
        {
            if (!IsReady || driving) return false;
            if (flying) SetFlying(false);
            driving = true;
            velocity = Vector3.zero;
            body.gameObject.SetActive(false);
            if (capsule != null) capsule.enabled = false;
            viewCamera.transform.SetParent(null, true);
            return true;
        }

        /// <summary>Steps out of a vehicle at a world position, facing a heading in degrees.</summary>
        public void LeaveVehicle(Vector3 position, float heading)
        {
            if (!driving) return;
            driving = false;
            position.y = GroundY(position);
            transform.position = position;
            yaw = bodyYaw = heading;
            transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
            pitch = 0f;
            velocity = Vector3.zero;
            grounded = true;
            body.gameObject.SetActive(true);
            if (capsule != null) capsule.enabled = true;
            MountCamera();
            viewCamera.fieldOfView = fieldOfView;
            LockCursor(true);
        }

        /// <summary>F2: hand the camera to the free inspection camera, or take it back.</summary>
        private void SetFlying(bool fly)
        {
            flying = fly;
            Transform cam = viewCamera.transform;
            body.gameObject.SetActive(!fly);
            if (fly)
            {
                cam.SetParent(null, true);
                flyCamera.terrain = terrain;
                flyCamera.enabled = true;
                LockCursor(false);
                return;
            }

            // Land the astronaut below wherever the inspection camera ended up.
            flyCamera.enabled = false;
            Vector3 position = cam.position;
            position.y = GroundY(position);
            transform.position = position;
            yaw = bodyYaw = cam.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
            pitch = 0f;
            velocity = Vector3.zero;
            grounded = true;
            MountCamera();
            LockCursor(true);
        }
    }
}
