using UnityEngine;

namespace MarsSurvival.World
{
    /// <summary>
    /// Minimal free camera for inspecting the terrain before the astronaut exists.
    /// WASD moves, Q/E lowers and raises, hold right mouse to look, Shift is faster,
    /// the scroll wheel changes the base speed. Stays above the ground.
    /// Speeds are in real metres per second. Replace with the real player camera later.
    /// </summary>
    public sealed class MarsTerrainFlyCamera : MonoBehaviour
    {
        public MarsTerrain terrain;
        [Tooltip("Base speed in real metres per second. 1.4 is walking pace; the scroll wheel changes it during play.")]
        public float speedMetersPerSecond = 150f;
        public float fastMultiplier = 15f;
        public Vector2 speedRangeMetersPerSecond = new Vector2(1f, 5000f);
        public float lookSensitivity = 2f;
        [Tooltip("Lowest allowed height above the ground, in real metres (1.7 = astronaut eye height).")]
        public float minHeightAboveGroundMeters = 1.7f;

        private float yaw;
        private float pitch;

        private void Start()
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        private float MetersToUnits(float meters)
        {
            return terrain != null && terrain.config != null ? terrain.config.ToUnits(meters) : meters;
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f)
            {
                speedMetersPerSecond = Mathf.Clamp(speedMetersPerSecond * Mathf.Pow(1.25f, scroll),
                    speedRangeMetersPerSecond.x, speedRangeMetersPerSecond.y);
            }

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= transform.forward;
            if (Input.GetKey(KeyCode.D)) move += transform.right;
            if (Input.GetKey(KeyCode.A)) move -= transform.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
            float speed = speedMetersPerSecond * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
            transform.position += move * (MetersToUnits(speed) * Time.unscaledDeltaTime);
#endif
            if (terrain != null && terrain.IsReady)
            {
                Vector3 position = transform.position;
                float floor = terrain.GetGroundY(position) + MetersToUnits(minHeightAboveGroundMeters);
                if (position.y < floor)
                {
                    position.y = floor;
                    transform.position = position;
                }
            }
        }
    }
}
