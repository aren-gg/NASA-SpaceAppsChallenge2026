using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.TimeAndWeather;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Vehicles
{
    /// <summary>
    /// A drivable battery rover. Walk up to it and press E to board; E again (when nearly
    /// stopped) steps out. W accelerates, S brakes and then reverses, A and D steer,
    /// Space is the handbrake, the mouse orbits the chase camera.
    ///
    /// Driving: the rover follows the terrain under its four wheels (and any solid surface
    /// such as the outpost pad and ramps), pitching and rolling with the ground. Its pull
    /// tails off toward the top speed, slopes speed it up or slow it down under Mars
    /// gravity, and steering is limited by tyre grip, so turns are wide at speed.
    /// Buildings and any step higher than 0.7 m stop it.
    ///
    /// Battery: a fixed capacity in kWh and a fixed use in kWh per kilometre, so a full
    /// battery lasts exactly capacity / use kilometres however it is driven. It starts
    /// full. With an empty battery the motors give nothing and the rover rolls to a stop.
    /// Parked on the outpost pad it recharges.
    ///
    /// All speeds and distances are real metres. The rover adds itself to a scene with a
    /// MarsTerrain and parks on the outpost pad.
    /// </summary>
    public sealed class MarsRover : MonoBehaviour
    {
        public MarsTerrain terrain;

        [Header("Performance")]
        [Tooltip("Top speed on level ground.")]
        public float maxSpeedKmh = 50f;
        public float reverseSpeedKmh = 20f;
        [Tooltip("Pull from rest in m/s2. Mars gravity limits tyre grip to roughly this.")]
        public float acceleration = 2.4f;
        [Tooltip("Braking in m/s2 (S while moving forward).")]
        public float braking = 3.2f;
        [Tooltip("Handbrake in m/s2 (Space).")]
        public float handbrake = 4.5f;
        [Tooltip("Slowing with no throttle, m/s2 at rest; it grows with speed.")]
        public float rollingResistance = 0.25f;
        [Tooltip("Sideways tyre grip in m/s2. Sets the tightest turn at speed.")]
        public float lateralGrip = 3f;
        public float maxSteerDegrees = 32f;
        [Tooltip("Mars surface gravity, for slopes.")]
        public float gravity = 3.71f;

        [Header("Battery")]
        public float batteryCapacityKwh = 60f;
        [Tooltip("Energy used per kilometre driven. Range on a full battery = capacity / this.")]
        public float consumptionKwhPerKm = 0.5f;
        [Tooltip("Percent of the battery regained per second while parked on the outpost pad.")]
        public float rechargePercentPerSecond = 1.5f;

        [Header("Boarding and camera")]
        public float boardDistance = 7f;
        [Tooltip("Chase camera distance (metres); the scroll wheel changes it between the limits below.")]
        public float cameraDistance = 14f;
        public float minCameraDistance = 7f;
        public float maxCameraDistance = 30f;
        public bool showHud = true;

        /// <summary>Forward speed in km/h; negative in reverse.</summary>
        public float SpeedKmh { get { return speed * 3.6f; } }
        public float BatteryKwh { get; private set; }
        public float BatteryPercent { get { return batteryCapacityKwh > 0f ? 100f * BatteryKwh / batteryCapacityKwh : 0f; } }
        /// <summary>Kilometres a full battery lasts.</summary>
        public float FullRangeKm { get { return batteryCapacityKwh / Mathf.Max(0.001f, consumptionKwhPerKm); } }
        public float RangeLeftKm { get { return BatteryKwh / Mathf.Max(0.001f, consumptionKwhPerKm); } }
        public float OdometerKm { get; private set; }
        public bool IsDriven { get { return driven; } }
        public bool IsCharging { get; private set; }
        public bool IsReady { get { return placed; } }

        private const float S = MarsRoverModel.Scale;
        private const float BodyHalfWidth = 1.45f * S, BodyHalfLength = 3.05f * S;

        private MarsRoverModel.Parts parts;
        private readonly List<Object> owned = new List<Object>();
        private MarsAstronautPlayer player;
        private MarsOutpost outpost;
        private MarsDayNightCycle cycle;
        private Light headlights;
        private bool placed, driven;
        private float speed;            // m/s along the heading
        private float heading;          // degrees
        private float steer;            // degrees, current front-wheel angle
        private float pitch, roll;      // degrees, smoothed
        private float spin;             // degrees
        private readonly float[] wheelGround = new float[4];
        private readonly float[] nextGround = new float[4];
        private float cameraYaw, cameraPitch = 16f, baseFieldOfView = 70f, boomLength = -1f;
        private bool playerNear;
        private string notice = "";
        private float noticeUntil;
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private readonly Collider[] overlaps = new Collider[16];
        private GUIStyle hudStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AddToTerrainScene()
        {
            if (FindObjectOfType<MarsRover>() != null) return;
            if (FindObjectOfType<MarsTerrain>() == null) return;
            new GameObject("Mars Rover").AddComponent<MarsRover>();
        }

        private void Start()
        {
            if (terrain == null) terrain = FindObjectOfType<MarsTerrain>();
            BatteryKwh = batteryCapacityKwh;

            Material[] materials = OutpostMaterials.Create();
            owned.AddRange(materials);
            parts = MarsRoverModel.Build(transform, materials, owned);
            gameObject.AddComponent<MarsRoverTracks>().Init(parts.wheelPivots);

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.35f * S, 0f);
            box.size = new Vector3(BodyHalfWidth * 2f, 1.9f * S, BodyHalfLength * 2f);

            headlights = parts.headlightAnchor.gameObject.AddComponent<Light>();
            headlights.type = LightType.Spot;
            headlights.spotAngle = 75f;
            headlights.range = 90f;
            headlights.intensity = 40f;
            headlights.color = new Color(1f, 0.95f, 0.85f);
            headlights.shadows = LightShadows.None;
            headlights.enabled = false;
            parts.headlightAnchor.localRotation = Quaternion.Euler(8f, 0f, 0f);
        }

        private float Units { get { return terrain != null && terrain.config != null ? terrain.config.ToUnits(1f) : 1f; } }

        private void OnDestroy()
        {
            foreach (Object o in owned)
            {
                if (o != null) Destroy(o);
            }
        }

        // ------------------------------------------------------------------ placement

        private void TryPlace()
        {
            if (terrain == null || !terrain.IsReady) return;
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (outpost != null && !outpost.IsPlaced) return;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();

            Vector3 position;
            if (outpost != null)
            {
                position = outpost.transform.TransformPoint(MarsOutpostBuilder.RoverParking);
                heading = outpost.transform.eulerAngles.y + MarsOutpostBuilder.RoverParkingYaw;
            }
            else
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                Vector3 side = cam.transform.right;
                side.y = 0f;
                position = cam.transform.position + side.normalized * 8f * Units;
                heading = cam.transform.eulerAngles.y;
            }

            transform.localScale = Vector3.one * Units;
            Physics.SyncTransforms();
            // Parked in the garage: look for the floor just above it, not from the sky (that finds the roof).
            position.y = outpost != null ? position.y + 1f * Units : terrain.GetGroundY(position) + 12f * Units;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            float sum = 0f;
            for (int i = 0; i < 4; i++)
            {
                wheelGround[i] = GroundAt(WheelWorld(position, heading, i), position.y);
                sum += wheelGround[i];
            }
            position.y = sum * 0.25f;
            transform.position = position;
            Physics.SyncTransforms();
            placed = true;
        }

        private Vector3 WheelWorld(Vector3 position, float headingDegrees, int wheel)
        {
            Vector3 rest = parts.wheelRest[wheel];
            return position + Quaternion.Euler(0f, headingDegrees, 0f) * new Vector3(rest.x, 0f, rest.z) * Units;
        }

        private bool IsObstacle(Collider other)
        {
            if (other == null || other.isTrigger) return false;
            if (other.transform.IsChildOf(transform)) return false;
            return terrain == null || !other.transform.IsChildOf(terrain.transform);
        }

        /// <summary>
        /// Height of the surface a wheel would stand on: the terrain, or a solid surface above
        /// it (the outpost pad, a ramp) that is no higher than <paramref name="fromY"/>.
        /// </summary>
        private float GroundAt(Vector3 world, float fromY)
        {
            float ground = terrain.GetGroundY(world);
            float reach = fromY - ground;
            if (reach <= 0f) return ground;
            int count = Physics.RaycastNonAlloc(new Vector3(world.x, fromY, world.z), Vector3.down, hits, reach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsObstacle(hits[i].collider)) ground = Mathf.Max(ground, hits[i].point.y);
            }
            return ground;
        }

        // ------------------------------------------------------------------ driving

        private void Update()
        {
            if (!placed)
            {
                TryPlace();
                return;
            }
            var mission = MarsSurvival.Gameplay.MarsMissionController.Instance;
            if (mission != null && mission.InputBlocked) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (cycle == null) cycle = FindObjectOfType<MarsDayNightCycle>();

            float throttle = 0f, steerInput = 0f;
            bool handbrakeHeld = false, boardPressed = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            boardPressed = Input.GetKeyDown(KeyCode.E);
            if (driven)
            {
                if (Input.GetKey(KeyCode.W)) throttle += 1f;
                if (Input.GetKey(KeyCode.S)) throttle -= 1f;
                if (Input.GetKey(KeyCode.D)) steerInput += 1f;
                if (Input.GetKey(KeyCode.A)) steerInput -= 1f;
                handbrakeHeld = Input.GetKey(KeyCode.Space);
                // The cursor is handled by the astronaut, exactly as on foot: captured with shift
                // lock (and taken back cleanly after Escape or a menu), free without it, with the
                // right mouse button turning the view and the pointer staying where it was clicked.
                // Its mouse turn already includes the camera sensitivity setting.
                if (player != null)
                {
                    Vector2 look = player.LookDelta;
                    cameraYaw += look.x;
                    cameraPitch = Mathf.Clamp(cameraPitch - look.y * 0.75f, 4f, 65f);
                    float scroll = Input.mouseScrollDelta.y;
                    if (player.ScrollFree && Mathf.Abs(scroll) > 0.01f)
                        cameraDistance = Mathf.Clamp(cameraDistance * Mathf.Pow(0.88f, scroll), minCameraDistance, maxCameraDistance);
                }
            }
#endif
            playerNear = !driven && player != null && player.IsReady && !player.IsDriving &&
                         (player.transform.position - transform.position).magnitude < boardDistance * Units;
            if (boardPressed)
            {
                if (driven) TryLeave();
                else if (playerNear) Board();
            }

            Drive(throttle, steerInput, handbrakeHeld, dt);
            Recharge(dt);

            if (headlights != null)
            {
                headlights.enabled = driven && (cycle == null || cycle.SkyLight < 0.6f);
            }
        }

        private void Board()
        {
            if (!player.BoardVehicle()) return;
            driven = true;
            boomLength = -1f;
            cameraYaw = 0f;
            cameraPitch = 16f;
            if (player.ViewCamera != null) baseFieldOfView = player.ViewCamera.fieldOfView;
        }

        private void TryLeave()
        {
            if (Mathf.Abs(speed) > 2f)
            {
                Notice("Slow down to step out");
                return;
            }
            driven = false;
            speed = 0f;
            Quaternion facing = Quaternion.Euler(0f, heading, 0f);
            Vector3 door = transform.position - facing * Vector3.right * (BodyHalfWidth + 1.1f) * Units;
            player.LeaveVehicle(door + Vector3.up * 0.3f * Units, heading);
        }

        private void Notice(string text)
        {
            notice = text;
            noticeUntil = Time.time + 2.5f;
        }

        private void Drive(float throttle, float steerInput, bool handbrakeHeld, float dt)
        {
            float top = maxSpeedKmh / 3.6f, topReverse = reverseSpeedKmh / 3.6f;
            bool powered = BatteryKwh > 0f;

            // Longitudinal: motor pull that tails off toward top speed, brakes, rolling drag, slope.
            float change = 0f;
            if (handbrakeHeld || !driven)
            {
                speed = Mathf.MoveTowards(speed, 0f, handbrake * dt);
            }
            else if (throttle > 0f)
            {
                if (speed < -0.2f) speed = Mathf.MoveTowards(speed, 0f, braking * dt);
                else if (powered) change += acceleration * Mathf.Clamp01((1f - speed / top) * 2.5f); // full pull to 60% of top speed
            }
            else if (throttle < 0f)
            {
                if (speed > 0.2f) speed = Mathf.MoveTowards(speed, 0f, braking * dt);
                else if (powered) change -= acceleration * 0.6f * Mathf.Clamp01(1f + speed / topReverse);
            }
            if (driven && !handbrakeHeld)
            {
                // Under Mars gravity a slope adds or removes g * sin(pitch). Nose up is negative pitch here.
                change += gravity * Mathf.Sin(pitch * Mathf.Deg2Rad);
                speed += change * dt;
                float drag = rollingResistance * (1f + 2f * Mathf.Abs(speed) / top);
                if (throttle == 0f || !powered || Mathf.Abs(speed) > top) speed = Mathf.MoveTowards(speed, 0f, drag * dt);
            }
            if (Mathf.Abs(speed) < 0.02f && throttle == 0f) speed = 0f;

            // Steering: the wheels turn less at speed, and the turn rate is capped by tyre grip.
            float fraction = Mathf.Clamp01(Mathf.Abs(speed) / top);
            float steerLimit = Mathf.Lerp(maxSteerDegrees, 5f, Mathf.Sqrt(fraction));
            steer = Mathf.MoveTowards(steer, steerInput * steerLimit, 70f * dt);
            float turnRate = speed / MarsRoverModel.WheelBase * Mathf.Tan(steer * Mathf.Deg2Rad); // rad/s
            float gripRate = lateralGrip / Mathf.Max(1.5f, Mathf.Abs(speed));
            turnRate = Mathf.Clamp(turnRate, -gripRate, gripRate);
            float newHeading = heading + turnRate * Mathf.Rad2Deg * dt;

            Vector3 forward = Quaternion.Euler(0f, newHeading, 0f) * Vector3.forward;
            Vector3 position = transform.position + forward * (speed * dt * Units);
            bool moving = Mathf.Abs(speed) > 0.001f;

            // Where the wheels would stand. A sudden step up, or a building in the way, stops the rover.
            float[] ground = nextGround;
            bool blocked = false;
            float reachY = transform.position.y + 1.3f * Units;
            for (int i = 0; i < 4; i++)
            {
                ground[i] = GroundAt(WheelWorld(position, newHeading, i), reachY);
                if (moving && ground[i] - wheelGround[i] > 0.7f * Units) blocked = true;
            }
            if (moving && !blocked) blocked = HitsBuilding(position, newHeading);
            if (blocked)
            {
                speed = -speed * 0.15f;
                position = transform.position;
                newHeading = heading;
                for (int i = 0; i < 4; i++) ground[i] = wheelGround[i];
            }

            float travelled = (position - transform.position).magnitude / Units;
            heading = newHeading;
            for (int i = 0; i < 4; i++) wheelGround[i] = ground[i];

            // Body height, pitch and roll from the four wheel heights.
            float front = (ground[0] + ground[1]) * 0.5f, rear = (ground[2] + ground[3]) * 0.5f;
            float left = (ground[0] + ground[2]) * 0.5f, right = (ground[1] + ground[3]) * 0.5f;
            float targetPitch = -Mathf.Atan2((front - rear) / Units, MarsRoverModel.WheelBase) * Mathf.Rad2Deg;
            float targetRoll = Mathf.Atan2((right - left) / Units, MarsRoverModel.Track) * Mathf.Rad2Deg;
            float ease = 1f - Mathf.Exp(-12f * dt);
            pitch = Mathf.Lerp(pitch, targetPitch, ease);
            roll = Mathf.Lerp(roll, targetRoll, ease);
            position.y = Mathf.Lerp(transform.position.y, (front + rear) * 0.5f, 1f - Mathf.Exp(-18f * dt));
            transform.SetPositionAndRotation(position,
                Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(pitch, 0f, roll));

            // Wheels: spin with distance, steer at the front, follow the ground a little.
            spin += Mathf.Sign(speed) * travelled / MarsRoverModel.WheelRadius * Mathf.Rad2Deg;
            for (int i = 0; i < 4; i++)
            {
                Vector3 rest = parts.wheelRest[i];
                float planeY = transform.TransformPoint(new Vector3(rest.x, 0f, rest.z)).y;
                rest.y += Mathf.Clamp((ground[i] - planeY) / Units, -0.3f, 0.3f);
                parts.wheelPivots[i].localPosition = rest / S;
                parts.wheelPivots[i].localRotation = Quaternion.Euler(0f, i < 2 ? steer : 0f, 0f);
                parts.wheelSpin[i].localRotation = Quaternion.Euler(spin, 0f, 0f);
            }

            // Battery: a fixed amount of energy per kilometre covered.
            float km = travelled / 1000f;
            OdometerKm += km;
            if (km > 0f)
            {
                BatteryKwh = Mathf.Max(0f, BatteryKwh - km * consumptionKwhPerKm);
                if (BatteryKwh <= 0f && powered) Notice("Battery empty");
            }

            if (driven && player != null) player.transform.position = transform.position;
        }

        private bool HitsBuilding(Vector3 position, float headingDegrees)
        {
            float u = Units;
            Vector3 centre = position + Vector3.up * 1.5f * S * u;
            Vector3 half = new Vector3(BodyHalfWidth, 0.75f * S, BodyHalfLength) * u;
            int count = Physics.OverlapBoxNonAlloc(centre, half, overlaps, Quaternion.Euler(0f, headingDegrees, 0f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsObstacle(overlaps[i])) return true;
            }
            return false;
        }

        public void ApplyMissionBattery(float value)
        {
            BatteryKwh = Mathf.Clamp(value, 0, batteryCapacityKwh);
        }

        public void RestoreMissionPose(Vector3 position, float yaw, double odometer)
        {
            speed = 0;
            if (driven) TryLeave();
            heading = yaw;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            OdometerKm = (float)odometer;
        }
        public void RestoreDriving() { if (!driven && player != null) Board(); }

        private void Recharge(float dt)
        {
            IsCharging = false;
            if (MarsSurvival.Gameplay.MarsMissionController.Instance != null) return;
            if (outpost == null || Mathf.Abs(speed) > 0.1f || BatteryKwh >= batteryCapacityKwh) return;
            Vector3 local = outpost.transform.InverseTransformPoint(transform.position);
            if (!MarsOutpostBuilder.Pad.Contains(local.x, local.z)) return;
            IsCharging = true;
            BatteryKwh = Mathf.Min(batteryCapacityKwh, BatteryKwh + batteryCapacityKwh * rechargePercentPerSecond * 0.01f * dt);
        }

        // ------------------------------------------------------------------ camera and HUD

        private void LateUpdate()
        {
            if (!driven || player == null || player.ViewCamera == null) return;
            Camera cam = player.ViewCamera;
            float u = Units;
            Vector3 target = transform.position + Vector3.up * 1.7f * S * u;
            Quaternion orbit = Quaternion.Euler(cameraPitch, heading + cameraYaw, 0f);
            Vector3 back = -(orbit * Vector3.forward);
            float wanted = cameraDistance * u;
            // Pull in at once when a building or the outpost is between the camera and the rover,
            // and ease back out, so the camera never sits inside walls and does not pop.
            // The outpost's roofs are one-sided mesh colliders; seen from a module's open end the
            // ray only meets their inner faces, so back faces must count here.
            float allowed = wanted;
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            for (int pass = 0; pass < 2; pass++)
            {
                int count = pass == 0
                    ? Physics.SphereCastNonAlloc(target, 0.3f * u, back, hits, wanted, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    : Physics.RaycastNonAlloc(target, back, hits, wanted, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)) continue;
                    if (terrain != null && hits[i].collider.transform.IsChildOf(terrain.transform)) continue;
                    if (hits[i].distance > 0f) allowed = Mathf.Min(allowed, Mathf.Max(0f, hits[i].distance - 0.3f * u));
                }
            }
            Physics.queriesHitBackfaces = backfaces;
            if (boomLength < 0f || allowed <= boomLength) boomLength = allowed;
            else boomLength = Mathf.Min(allowed, boomLength + 20f * u * Time.deltaTime);
            Vector3 position = target + back * boomLength;
            float floor = terrain.GetGroundY(position) + 0.6f * u;
            if (position.y < floor) position.y = floor;
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            // A wider view at speed.
            float fraction = Mathf.Clamp01(Mathf.Abs(speed) / (maxSpeedKmh / 3.6f));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFieldOfView + 14f * fraction, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        private void OnGUI()
        {
            if (!showHud || !placed) return;
            string text;
            if (driven)
            {
                text = "ROVER   " + Mathf.Abs(SpeedKmh).ToString("F0") + " km/h" + (speed < -0.1f ? "  (reverse)" : "") +
                       "\nBattery " + BatteryPercent.ToString("F0") + "%   ·   " + RangeLeftKm.ToString("F1") + " km left of " +
                       FullRangeKm.ToString("F0") + (IsCharging ? "   ·   charging" : "") +
                       "\nDriven " + OdometerKm.ToString("F2") + " km" +
                       "\nW / S drive and brake   ·   A / D steer   ·   Space handbrake   ·   E step out";
                if (BatteryKwh <= 0f) text += "\nBattery empty: park on the outpost pad to recharge";
            }
            else if (playerNear)
            {
                text = "E   drive the rover\nBattery " + BatteryPercent.ToString("F0") + "%   ·   " +
                       RangeLeftKm.ToString("F0") + " km" + (IsCharging ? "   ·   charging" : "");
            }
            else
            {
                return;
            }
            if (Time.time < noticeUntil) text += "\n" + notice;

            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.box);
                hudStyle.alignment = TextAnchor.UpperCenter;
                hudStyle.fontSize = 15;
                hudStyle.wordWrap = false;
                hudStyle.padding = new RectOffset(14, 14, 8, 8);
                hudStyle.normal.textColor = MarsPalette.PalePink;
            }
            Vector2 size = hudStyle.CalcSize(new GUIContent(text));
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height - size.y - 24f, size.x, size.y), text, hudStyle);
        }
    }
}
