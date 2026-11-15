using Cinemachine;
using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarsTrainer.Gameplay.Player
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        private PlayerController player;
        private CinemachineTransposer body;
        private float yaw = GameConstants.OrbitInitialYaw, pitch = GameConstants.OrbitPitch, distance = GameConstants.OrbitDistance;
        public void Initialize(PlayerController source, CinemachineTransposer follow)
        {
            player = source; body = follow; Apply();
        }
        private void Update()
        {
            var mouse = Mouse.current;
            if (player == null || !player.CanLook) { ReleaseCursor(); return; }
            bool dragging = mouse != null && mouse.rightButton.isPressed;
            if (dragging)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                var delta = mouse.delta.ReadValue() * GameConstants.OrbitMouseSensitivity;
                Orbit(delta.x, -delta.y);
            }
            else ReleaseCursor();
            var pad = Gamepad.current;
            if (pad != null)
            {
                var delta = pad.rightStick.ReadValue() * (GameConstants.OrbitStickDegreesPerSecond * Time.unscaledDeltaTime);
                Orbit(delta.x, -delta.y);
            }
            if (mouse != null)
            {
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * GameConstants.OrbitZoomStep,
                    GameConstants.OrbitMinimumDistance, GameConstants.OrbitMaximumDistance);
                Apply();
            }
        }
        public void Orbit(float horizontal, float vertical)
        {
            yaw = Mathf.Repeat(yaw + horizontal, GameConstants.FullOrbitDegrees);
            pitch = Mathf.Clamp(pitch + vertical, GameConstants.OrbitMinimumPitch, GameConstants.OrbitMaximumPitch);
            Apply();
        }
        private void Apply()
        {
            if (body == null) return;
            body.m_FollowOffset = Quaternion.Euler(pitch, yaw, 0) * (Vector3.back * distance);
            player.ViewYaw = yaw;
        }
        private void OnDisable() { ReleaseCursor(); }
        private static void ReleaseCursor()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }
}
