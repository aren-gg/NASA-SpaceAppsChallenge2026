using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private Button resume;
        private GameObject previousFocus;
        public System.Action Closed { get; set; }

        public void Open()
        {
            if (gameObject.activeSelf) return;
            previousFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            gameObject.SetActive(true); resume.Select();
        }
        public void Close()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(previousFocus);
            Closed?.Invoke();
        }
        /// <summary>
        /// A centred COMMAND-01 plate over a dimmed view: the controls, the settings and one amber
        /// button back to Mars.
        /// </summary>
        public static PauseMenu Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Pause menu", 0, 0, 1, 1);
            var scrim = panel.gameObject.AddComponent<Image>();
            scrim.color = new Color(0.03f, 0.035f, 0.05f, 0.62f);
            var view = panel.gameObject.AddComponent<PauseMenu>();
            var plate = StatBar.Frame(panel, "Pause plate", 0.2f, 0.19f, 0.8f, 0.81f, GameConstants.UiSurface);
            var face = plate.GetComponent<FacetedImage>(); face.KeepAlpha = true; face.color = UiTheme.WithAlpha(UiTheme.Panel, 0.94f);
            var content = StatBar.Frame(plate, "Pause content", 0, 0, 1, 1); StatBar.Stack(content);
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(40, 40, 30, 30);
            var head = StatBar.Frame(content, "Header", 0, 0, 1, 1); StatBar.Height(head, 104);
            UiStyle.Id(head, "Command-01", 18, 0, 0);
            var title = StatBar.Text(head, "Paused", 52, 64);
            UiStyle.Caps(title, 8f);
            var t = title.rectTransform; t.anchorMin = new Vector2(0, 0); t.anchorMax = new Vector2(1, 0); t.pivot = new Vector2(0, 0);
            t.anchoredPosition = Vector2.zero; t.sizeDelta = new Vector2(0, 64);
            ControlGuide.Create(content, "Controls", ControlGuide.Exploration, 2);
            MissionSettings.Create(content);
            CameraSettings.Create(content);
            HudSettings.Create(content);
            view.resume = StatBar.Button(content, "Back to Mars  [Esc]", view.Close);
            var resumeFace = view.resume.GetComponent<FacetedImage>(); resumeFace.KeepAlpha = true; resumeFace.color = UiTheme.Accent;
            var states = StatBar.UiStates; states.highlightedColor = states.selectedColor = new Color(1.12f, 1.12f, 1.12f, 1f);   // amber stays amber when focused
            view.resume.colors = states;
            var label = view.resume.GetComponentInChildren<TMPro.TMP_Text>(); label.color = UiTheme.Ink; UiStyle.Caps(label, 3f);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
