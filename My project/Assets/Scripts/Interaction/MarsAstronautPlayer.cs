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
        [Tooltip("3.4 km/h.")]
        public float walkSpeed = 3.4f / 3.6f;
        [Tooltip("29 km/h.")]
        public float sprintSpeed = 29f / 3.6f;
        public float groundAcceleration = 12f;
        public float airAcceleration = 2f;
        [Tooltip("Mars surface gravity.")]
        public float gravity = 3.71f;
        [Tooltip("Height of a standing jump under the gravity above.")]
        public float jumpHeight = 1.6f;
        [Tooltip("The astronaut stays stuck to ground that drops by less than this per frame.")]
        public float stepDown = 0.35f;

        [Header("View")]
        public float lookSensitivity = 2f;
        [Range(40f, 100f)] public float fieldOfView = 70f;
        [Tooltip("Eye height of the 1.8 m astronaut.")]
        public float eyeHeight = 1.68f;

        [Header("Collision with buildings (real metres)")]
        public float bodyRadius = 0.35f;
        [Tooltip("Ledges up to this height above the feet are stepped onto rather than blocking.")]
        public float stepUp = 0.45f;

        [Header("Stride length per full cycle (two steps), metres")]
        public float walkStride = 1.25f;
        public float sprintStride = 3.6f;

        public bool IsGrounded { get { return grounded; } }
        /// <summary>True while the astronaut is aboard a vehicle and the vehicle owns the camera.</summary>
        public bool IsDriving { get { return driving; } }
        public Camera ViewCamera { get { return viewCamera; } }
        /// <summary>True once the astronaut has been set down on the ground at the start.</summary>
        public bool IsReady { get { return placed && neck != null; } }
        public bool IsSprinting { get { return sprinting; } }
        /// <summary>Horizontal speed in real metres per second.</summary>
        public float Speed { get { return new Vector2(velocity.x, velocity.z).magnitude; } }

        private const float NeckHeight = 1.55f;
        private const float EyeForward = 0.10f;

        private Transform neck;
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
            yaw = cam.eulerAngles.y;
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

            LockCursor(true);
        }

        private void MountCamera()
        {
            Transform cam = viewCamera.transform;
            cam.SetParent(neck, false);
            cam.localPosition = new Vector3(0f, eyeHeight - NeckHeight, EyeForward);
            cam.localRotation = Quaternion.identity;
            cam.localScale = Vector3.one;
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
            LockCursor(false);
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
            if (mission != null && mission.InputBlocked) { velocity = Vector3.zero; sprinting = false; return; }
            if (driving) return;

            float forwardInput = 0f, sideInput = 0f;
            bool sprintHeld = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Debug.isDebugBuild && mission == null && Input.GetKeyDown(KeyCode.F2) && flyCamera != null) SetFlying(!flying);
            if (flying) return;

            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
            else if (Input.GetMouseButtonDown(0)) LockCursor(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * lookSensitivity, -80f, 80f);
            }
            if (Input.GetKey(KeyCode.W)) forwardInput += 1f;
            if (Input.GetKey(KeyCode.S)) forwardInput -= 1f;
            if (Input.GetKey(KeyCode.D)) sideInput += 1f;
            if (Input.GetKey(KeyCode.A)) sideInput -= 1f;
            sprintHeld = Input.GetKey(KeyCode.LeftShift);
            if (Input.GetKeyDown(KeyCode.Space)) jumpBuffer = 0.15f;
#endif
            float dt = Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // The head pitches about the neck, so looking down also moves the eyes forward.
            neck.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            Move(forwardInput, sideInput, sprintHeld, dt);
            AnimateBody(dt);
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
            Vector3 wish = transform.forward * forwardInput + transform.right * sideInput;
            wish.y = 0f;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // Sprinting needs forward motion; it carries through a jump.
            sprinting = sprintHeld && forwardInput > 0f;
            Vector3 target = wish * (sprinting ? sprintSpeed : walkSpeed);
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, target, (grounded ? groundAcceleration : airAcceleration) * dt);
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

            Vector3 position = transform.position + velocity * Units(1f) * dt;
            if (TerrainBlocks(transform.position, position))
            {
                position.x = transform.position.x; position.z = transform.position.z;
                velocity.x = 0; velocity.z = 0;
            }
            position = ResolveWalls(position);
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
            yaw = heading; pitch = 0; velocity = Vector3.zero; grounded = true;
            MountCamera();
        }

        /// <summary>Turns the body and head to look at a world point (used by scripted checks and cut-ins).</summary>
        public void AimAt(Vector3 point)
        {
            if (neck == null) return;
            // The eyes sit ahead of the neck pivot, so refine the aim a few times from the eye.
            for (int pass = 0; pass < 4; pass++)
            {
                Vector3 eye = viewCamera != null && viewCamera.transform.IsChildOf(transform) ? viewCamera.transform.position : neck.position;
                Vector3 d = point - eye;
                if (d.sqrMagnitude < 1e-6f) return;
                yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -80f, 80f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                neck.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
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
            yaw = heading;
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
            yaw = cam.eulerAngles.y;
            pitch = 0f;
            velocity = Vector3.zero;
            grounded = true;
            MountCamera();
            LockCursor(true);
        }
    }
}
