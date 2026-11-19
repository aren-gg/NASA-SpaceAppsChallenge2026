using System;
using UnityEngine;

namespace MarsTrainer.UI
{
    /// <summary>
    /// Saved camera options, modelled on Roblox's settings menu: a camera (mouse) sensitivity
    /// multiplier, a "Shift Lock Switch" that allows shift lock at all, and whether shift lock
    /// is currently on. Shift lock captures the cursor in third person so the mouse turns the
    /// view and the astronaut faces where the camera looks; with it off the cursor is free and
    /// the right mouse button turns the camera. Stored in PlayerPrefs.
    /// </summary>
    public static class CameraPrefs
    {
        public const float MinSensitivity = 0.1f;
        public const float MaxSensitivity = 4f;
        public const float SensitivityStep = 0.1f;

        private const string SensitivityKey = "camera.sensitivity";
        private const string SwitchKey = "camera.shiftLockSwitch";
        private const string OnKey = "camera.shiftLockOn";

        private static bool loaded;
        private static float sensitivity = 1f;
        private static bool shiftLockSwitch = true;
        private static bool shiftLockOn = true;

        /// <summary>Raised whenever any camera option changes.</summary>
        public static event Action Changed;

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
            shiftLockSwitch = PlayerPrefs.GetInt(SwitchKey, 1) != 0;
            shiftLockOn = PlayerPrefs.GetInt(OnKey, 1) != 0;
        }

        /// <summary>Multiplies mouse look speed. 1 is the default.</summary>
        public static float Sensitivity
        {
            get { Load(); return sensitivity; }
            set
            {
                Load();
                float v = Mathf.Clamp(Mathf.Round(value / SensitivityStep) * SensitivityStep, MinSensitivity, MaxSensitivity);
                if (Mathf.Approximately(v, sensitivity)) return;
                sensitivity = v;
                PlayerPrefs.SetFloat(SensitivityKey, v);
                Changed?.Invoke();
            }
        }

        /// <summary>Whether the player may use shift lock at all (Roblox's "Shift Lock Switch").</summary>
        public static bool ShiftLockSwitch
        {
            get { Load(); return shiftLockSwitch; }
            set
            {
                Load();
                if (value == shiftLockSwitch) return;
                shiftLockSwitch = value;
                PlayerPrefs.SetInt(SwitchKey, value ? 1 : 0);
                Changed?.Invoke();
            }
        }

        /// <summary>Whether shift lock is toggled on (the in-game key flips this).</summary>
        public static bool ShiftLockOn
        {
            get { Load(); return shiftLockOn; }
            set
            {
                Load();
                if (value == shiftLockOn) return;
                shiftLockOn = value;
                PlayerPrefs.SetInt(OnKey, value ? 1 : 0);
                Changed?.Invoke();
            }
        }

        /// <summary>True when shift lock is both allowed and switched on.</summary>
        public static bool ShiftLockActive { get { return ShiftLockSwitch && ShiftLockOn; } }

        public static string SensitivityText { get { return Sensitivity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); } }
    }
}
