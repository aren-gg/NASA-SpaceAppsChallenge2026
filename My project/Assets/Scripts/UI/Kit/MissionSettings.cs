using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class MissionSettings : MonoBehaviour
    {
        [SerializeField] private TMP_Text volumeLabel;
        [SerializeField] private TMP_Text sizeLabel;
        [SerializeField] private Button volumeButton;
        [SerializeField] private Button sizeButton;
        private bool wired;
        private TMP_Text[] texts;
        private float[] sizes;
        private bool large;
        private void Awake() { Wire(); }
        private void Wire()
        {
            if (wired || volumeButton == null || sizeButton == null) return;
            wired = true;
            volumeButton.onClick.AddListener(CycleVolume); sizeButton.onClick.AddListener(ToggleText);
            Render();
        }
        private void CycleVolume() { SetVolume(AudioListener.volume > GameConstants.SettingsVolumeStep ? GameConstants.SettingsVolumeStep : AudioListener.volume > 0 ? 0 : 1); }
        private void ToggleText() { SetLargeText(!large); }
        public void SetVolume(float value) { AudioListener.volume = Mathf.Clamp01(value); Render(); }
        public void SetLargeText(bool value)
        {
            if (texts == null)
            {
                texts = transform.root.GetComponentsInChildren<TMP_Text>(true);
                sizes = new float[texts.Length];
                for (int i = 0; i < texts.Length; i++) sizes[i] = texts[i].fontSize;
            }
            large = value;
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] != null) texts[i].fontSize = sizes[i] * (large && sizes[i] < GameConstants.UiHeading ? GameConstants.SettingsLargeTextScale : 1);
            Render();
        }
        private void Render()
        {
            if (volumeLabel != null) volumeLabel.text = "Volume: " + Mathf.RoundToInt(AudioListener.volume * (float)GameConstants.StatMaximum) + "%";
            if (sizeLabel != null) sizeLabel.text = large ? "Text Size: Large" : "Text Size: Standard";
        }
        public static void Create(Transform parent)
        {
            var owner = StatBar.Frame(parent, "Settings", 0, 0, 1, 1);
            StatBar.Height(owner, GameConstants.UiButton);
            // Keep the row at button height; a force-expanding row would otherwise claim all spare height in its parent.
            owner.GetComponent<LayoutElement>().flexibleHeight = 0;
            var row = owner.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = GameConstants.UiGap;
            row.childControlHeight = row.childForceExpandHeight = true;
            row.childControlWidth = row.childForceExpandWidth = true;
            var view = owner.gameObject.AddComponent<MissionSettings>();
            var volume = StatBar.Button(owner, "Volume: 100%", null);
            var size = StatBar.Button(owner, "Text Size: Standard", null);
            view.volumeButton = volume; view.sizeButton = size;
            view.volumeLabel = volume.GetComponentInChildren<TMP_Text>(); view.sizeLabel = size.GetComponentInChildren<TMP_Text>();
            view.Wire();
        }
    }
}
