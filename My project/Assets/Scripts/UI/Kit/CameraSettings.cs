using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>
    /// Pause-menu row for the camera options in <see cref="CameraPrefs"/>:
    /// [-] [Camera Sensitivity 1.0] [+] [Shift Lock Switch: On]. Clicking the sensitivity
    /// label resets it to 1.0.
    /// </summary>
    public sealed class CameraSettings : MonoBehaviour
    {
        private TMP_Text sensitivityLabel, shiftLockLabel;

        private void OnEnable() { CameraPrefs.Changed += Render; Render(); }
        private void OnDisable() { CameraPrefs.Changed -= Render; }

        private void Render()
        {
            if (sensitivityLabel != null) sensitivityLabel.text = "Camera Sensitivity: " + CameraPrefs.SensitivityText;
            if (shiftLockLabel != null) shiftLockLabel.text = "Shift Lock Switch (Ctrl): " + (CameraPrefs.ShiftLockSwitch ? "On" : "Off");
        }

        public static void Create(Transform parent)
        {
            var owner = StatBar.Frame(parent, "Camera Settings", 0, 0, 1, 1);
            StatBar.Height(owner, GameConstants.UiButton);
            owner.GetComponent<LayoutElement>().flexibleHeight = 0;
            var row = owner.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = GameConstants.UiGap;
            row.childControlHeight = row.childForceExpandHeight = true;
            row.childControlWidth = true; row.childForceExpandWidth = false;

            var down = StatBar.Button(owner, "-", () => CameraPrefs.Sensitivity -= CameraPrefs.SensitivityStep);
            var label = StatBar.Button(owner, "Camera Sensitivity: 1.0", () => CameraPrefs.Sensitivity = 1f);
            var up = StatBar.Button(owner, "+", () => CameraPrefs.Sensitivity += CameraPrefs.SensitivityStep);
            var shiftLock = StatBar.Button(owner, "Shift Lock Switch (Ctrl): On", () => CameraPrefs.ShiftLockSwitch = !CameraPrefs.ShiftLockSwitch);
            Width(down, GameConstants.UiButton, 0); Width(up, GameConstants.UiButton, 0);
            Width(label, 0, 1); Width(shiftLock, 0, 1);

            var view = owner.gameObject.AddComponent<CameraSettings>();
            view.sensitivityLabel = label.GetComponentInChildren<TMP_Text>();
            view.shiftLockLabel = shiftLock.GetComponentInChildren<TMP_Text>();
            view.Render();
        }

        private static void Width(Button button, float preferred, float flexible)
        {
            var element = button.GetComponent<LayoutElement>();
            element.minWidth = preferred; element.preferredWidth = preferred; element.flexibleWidth = flexible;
        }
    }
}
