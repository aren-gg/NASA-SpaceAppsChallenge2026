using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class TimeControls : MonoBehaviour
    {
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button pause;
        [SerializeField] private Button normal;
        [SerializeField] private Button fast;
        [SerializeField] private Button fastest;
        private SimClock clock;
        public void Bind(SimClock source)
        {
            clock = source;
            pause.onClick.RemoveAllListeners(); normal.onClick.RemoveAllListeners();
            fast.onClick.RemoveAllListeners(); fastest.onClick.RemoveAllListeners();
            pause.onClick.AddListener(TogglePause);
            normal.onClick.AddListener(() => SetSpeed(1)); fast.onClick.AddListener(() => SetSpeed(2));
            fastest.onClick.AddListener(() => SetSpeed(4)); Refresh();
        }
        public void TogglePause() { if (clock == null) return; clock.Paused = !clock.Paused; Refresh(); }
        public void SetSpeed(int speed) { if (clock == null) return; clock.Speed = speed; Refresh(); }
        public void Refresh()
        {
            status.text = clock == null ? "Waiting for Mission" : clock.Paused ? "Paused" : "Speed: " + clock.Speed + "x";
        }
        public static TimeControls Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Time controls", 0.73f, 0.78f, 0.98f, 0.98f, GameConstants.UiSurface);
            StatBar.Stack(panel); var view = panel.gameObject.AddComponent<TimeControls>();
            view.status = StatBar.Text(panel, "Speed: 1x");
            var row = StatBar.Frame(panel, "Speed buttons", 0, 0, 1, 1); StatBar.Height(row, GameConstants.UiButton);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = GameConstants.UiGap;
            layout.childControlWidth = true; layout.childForceExpandWidth = true;
            layout.childControlHeight = true; layout.childForceExpandHeight = true;
            view.pause = StatBar.Button(row, "Pause", null);
            view.normal = StatBar.Button(row, "1x", null); view.fast = StatBar.Button(row, "2x", null);
            view.fastest = StatBar.Button(row, "4x", null);
            view.pause.GetComponent<LayoutElement>().flexibleWidth = 2;
            ControlGuide.Create(panel, "Time Controls", new[,] {{"Space", "Pause"}, {"1 / 2 / 3", "Set Speed"}});
            return view;
        }
    }
}
