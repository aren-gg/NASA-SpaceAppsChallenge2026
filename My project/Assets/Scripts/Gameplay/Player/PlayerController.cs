using Cinemachine;
using MarsTrainer.Sim;
using MarsTrainer.Sim.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarsTrainer.Gameplay.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private CharacterController controller;
        private Simulation sim;
        private ExpeditionSystem expedition;
        private InputAction move;
        private InputAction sprint;
        private float verticalSpeed;
        public ExpeditionSystem Expedition => expedition;
        public IWalkingSurface WalkingSurface { get; set; }
        private bool inputBlocked;
        public bool InputBlocked
        {
            get => inputBlocked;
            set { inputBlocked = value; if (value && expedition != null) expedition.SprintRequested = false; }
        }
        public float ViewYaw { get; set; }
        private bool cameraRelative;
        public bool CanLook => !InputBlocked && sim != null && sim.State.Alive && !sim.Clock.Paused;

        public void Initialize(Simulation sim, ExpeditionSystem expedition)
        {
            this.sim = sim;
            this.expedition = expedition;
            controller = GetComponent<CharacterController>();
            controller.height = GameConstants.PlayerHeight;
            controller.radius = GameConstants.PlayerRadius;
            controller.center = Vector3.up * GameConstants.PlayerHeight / 2;
            controller.stepOffset = GameConstants.PlayerStepHeight;
            controller.slopeLimit = GameConstants.PlayerSlopeLimit;
            if (move != null) return;
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            sprint.AddBinding("<Gamepad>/leftStickPress");
            if (isActiveAndEnabled && Application.isPlaying) { move.Enable(); sprint.Enable(); }
        }

        private void OnEnable()
        {
            if (Application.isPlaying) { move?.Enable(); sprint?.Enable(); }
        }
        private void OnDisable()
        {
            move?.Disable(); sprint?.Disable();
            if (expedition != null) expedition.SprintRequested = false;
        }
        private void OnDestroy() { move?.Dispose(); sprint?.Dispose(); }

        private void Update()
        {
            if (InputBlocked || move == null) return;
            var input = move.ReadValue<Vector2>();
            if (cameraRelative)
            {
                float angle = ViewYaw * Mathf.Deg2Rad;
                input = new Vector2(input.x * Mathf.Cos(angle) + input.y * Mathf.Sin(angle),
                    input.y * Mathf.Cos(angle) - input.x * Mathf.Sin(angle));
            }
            Move(input, sprint.IsPressed(), Time.deltaTime);
        }

        // World-aligned movement keeps the fixed follow camera controls predictable.
        public void Move(Vector2 input, bool sprintHeld, float dt)
        {
            if (sim == null || expedition == null || dt <= 0 || InputBlocked) return;
            expedition.SprintRequested = false;
            if (!sim.State.Alive || sim.State.Health <= 0 || sim.Clock.Paused ||
                sim.Clock.ElapsedSols >= GameConstants.MissionDurationDays) return;
            input = Vector2.ClampMagnitude(input, 1);
            if (input.sqrMagnitude > 0 && sim.Survival != null) sim.Survival.Sleeping = false;
            bool running = input.sqrMagnitude > 0 && sprintHeld && expedition.CanSprint(sim.State) &&
                sim.State.EnergyKWh >= GameConstants.SprintKWhPerSecond * dt * sim.Clock.Speed;
            expedition.SprintRequested = running;
            float speed = running ? GameConstants.SprintSpeed : GameConstants.WalkSpeed;
            if (WalkingSurface != null)
            {
                var target = WalkingSurface.ClampPosition(transform.position + new Vector3(input.x, 0, input.y) * (speed * dt));
                target.y = WalkingSurface.GroundHeight(target) + GameConstants.PlayerSpawnHeight;
                controller.Move(target - transform.position);
                RefreshLocation();
                return;
            }
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = GameConstants.GroundStickSpeed;
            verticalSpeed += GameConstants.MarsGravity * dt;
            var displacement = new Vector3(input.x * speed, verticalSpeed, input.y * speed) * dt;
            float bound = GameConstants.WorldSize / 2 - GameConstants.PlayerRadius;
            displacement.x = Mathf.Clamp(transform.localPosition.x + displacement.x, -bound, bound) - transform.localPosition.x;
            displacement.z = Mathf.Clamp(transform.localPosition.z + displacement.z, -bound, bound) - transform.localPosition.z;
            controller.Move(displacement);
            RefreshLocation();
        }

        public void RefreshLocation()
        {
            if (expedition == null) return;
            var position = transform.localPosition;
            expedition.AtBase = position.x * position.x + position.z * position.z <=
                GameConstants.BaseFlatRadius * GameConstants.BaseFlatRadius;
        }

        public Camera CreateFollowCamera(Transform parent, bool orbit = false, float farClip = GameConstants.CameraFarClip)
        {
            var cameraOwner = new GameObject("P9 Camera", typeof(Camera), typeof(CinemachineBrain), typeof(AudioListener));
            cameraOwner.transform.SetParent(parent, false);
            cameraOwner.tag = "MainCamera";
            var camera = cameraOwner.GetComponent<Camera>();
            camera.fieldOfView = GameConstants.CameraFov;
            camera.nearClipPlane = GameConstants.CameraNearClip;
            camera.farClipPlane = farClip;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color((GameConstants.SkyColor >> 16 & 255) / 255f,
                (GameConstants.SkyColor >> 8 & 255) / 255f, (GameConstants.SkyColor & 255) / 255f);
            var focus = new GameObject("Camera target").transform;
            focus.SetParent(transform, false);
            focus.localPosition = Vector3.up * GameConstants.PlayerHeight;
            var rigOwner = new GameObject("Cinemachine follow");
            rigOwner.transform.SetParent(parent, false);
            var rig = rigOwner.AddComponent<CinemachineVirtualCamera>();
            rig.Follow = focus;
            rig.LookAt = focus;
            rig.m_Lens.FieldOfView = GameConstants.CameraFov;
            rig.m_Lens.FarClipPlane = farClip;
            var body = rig.AddCinemachineComponent<CinemachineTransposer>();
            body.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
            body.m_FollowOffset = new Vector3(0, GameConstants.CameraHeight, -GameConstants.CameraDistance);
            body.m_XDamping = body.m_YDamping = body.m_ZDamping = GameConstants.CameraDamping;
            rig.AddCinemachineComponent<CinemachineComposer>();
            if (orbit)
            {
                cameraRelative = true;
                rigOwner.AddComponent<OrbitCamera>().Initialize(this, body);
                rigOwner.AddComponent<TerrainCameraClearance>().Surface = WalkingSurface;
            }
            return camera;
        }
    }
}
