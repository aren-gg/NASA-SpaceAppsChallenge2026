using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>Pause-menu row for <see cref="HudPrefs"/>: [-] [HUD size: 100%] [+]. Clicking the label resets it to 100%.</summary>
    public sealed class HudSettings : MonoBehaviour
    {
        private TMP_Text label;

        private void OnEnable() { HudPrefs.Changed += Render; Render(); }
        private void OnDisable() { HudPrefs.Changed -= Render; }
        private void Render() { if (label != null) label.text = "HUD size: " + HudPrefs.Text; }

        public static RectTransform Create(Transform parent)
        {
            var owner = StatBar.Frame(parent, "HUD Settings", 0, 0, 1, 1);
            StatBar.Height(owner, GameConstants.UiButton);
            owner.GetComponent<LayoutElement>().flexibleHeight = 0;
            var row = owner.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = GameConstants.UiGap;
            row.childControlHeight = row.childForceExpandHeight = true;
            row.childControlWidth = true; row.childForceExpandWidth = false;
            var down = StatBar.Button(owner, "-", () => HudPrefs.Scale -= HudPrefs.Step);
            var text = StatBar.Button(owner, "HUD size: 100%", () => HudPrefs.Scale = 1f);
            var up = StatBar.Button(owner, "+", () => HudPrefs.Scale += HudPrefs.Step);
            Width(down, GameConstants.UiButton, 0); Width(up, GameConstants.UiButton, 0); Width(text, 0, 1);
            var view = owner.gameObject.AddComponent<HudSettings>();
            view.label = text.GetComponentInChildren<TMP_Text>();
            view.Render();
            return owner;
        }

        private static void Width(Button button, float preferred, float flexible)
        {
            var element = button.GetComponent<LayoutElement>();
            element.minWidth = preferred; element.preferredWidth = preferred; element.flexibleWidth = flexible;
        }
    }
}
