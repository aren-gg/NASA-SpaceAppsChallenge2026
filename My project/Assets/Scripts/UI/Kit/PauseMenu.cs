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
        public static PauseMenu Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Pause menu", 0, 0, 1, 1, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            var view = panel.gameObject.AddComponent<PauseMenu>();
            var content = StatBar.Frame(panel, "Pause content", 0.12f, 0.06f, 0.88f, 0.94f); StatBar.Stack(content);
            StatBar.Text(content, "Take a Breather", GameConstants.UiTitle, GameConstants.UiButton);
            StatBar.Text(content, "Your outpost is paused.", height: GameConstants.UiLine * 2);
            ControlGuide.Create(content, "Exploration Controls", ControlGuide.Exploration, 2);
            ControlGuide.Create(content, "Menu Controls", ControlGuide.Menus, 2);
            MissionSettings.Create(content);
            CameraSettings.Create(content);
            HudSettings.Create(content);
            view.resume = StatBar.Button(content, "Back to Mars [Escape]", view.Close);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
