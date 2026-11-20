using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private Button resume;
        private SimClock clock;
        private bool wasPaused;
        private GameObject previousFocus;
        public System.Action Closed { get; set; }
        /// <summary>Set by the game's wardrobe; the pause menu's Wardrobe button calls it.</summary>
        public static System.Action OpenWardrobe { get; set; }
        public void Bind(SimClock source)
        {
            clock = source; resume.onClick.RemoveAllListeners(); resume.onClick.AddListener(Close);
        }
        public void Open()
        {
            if (gameObject.activeSelf || clock == null) return;
            wasPaused = clock.Paused; clock.Paused = true;
            previousFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            gameObject.SetActive(true); resume.Select();
        }
        public void Close()
        {
            if (!gameObject.activeSelf) return;
            if (clock != null) clock.Paused = wasPaused;
            gameObject.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(previousFocus);
            Closed?.Invoke();
        }
        public static PauseMenu Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Pause menu", 0, 0, 1, 1, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            var view = panel.gameObject.AddComponent<PauseMenu>();
            var content = StatBar.Frame(panel, "Pause content", 0.12f, 0.06f, 0.88f, 0.94f); StatBar.Stack(content);
            StatBar.Text(content, "Take a Breather", GameConstants.UiTitle, GameConstants.UiButton);
            StatBar.Text(content, "Your outpost is paused. Nothing uses energy while time is stopped.", height: GameConstants.UiLine * 2);
            ControlGuide.Create(content, "Exploration Controls", ControlGuide.Exploration, 2);
            ControlGuide.Create(content, "Menu Controls", ControlGuide.Menus, 2);
            MissionSettings.Create(content);
            CameraSettings.Create(content);
            // One row: Wardrobe, then the HUD size control.
            var hudRow = HudSettings.Create(content);
            var wardrobe = StatBar.Button(hudRow, "Wardrobe [C]", () => { if (OpenWardrobe != null) OpenWardrobe(); });
            wardrobe.GetComponent<LayoutElement>().flexibleWidth = 1;
            wardrobe.transform.SetAsFirstSibling();
            view.resume = StatBar.Button(content, "Back to Mars [Escape]", null);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
