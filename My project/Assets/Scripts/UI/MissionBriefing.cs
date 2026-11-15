using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class MissionBriefing : MonoBehaviour
    {
        [SerializeField] private Button begin;
        private SimClock clock;
        public System.Action Closed { get; set; }
        public bool IsOpen => gameObject.activeSelf;
        public void Open(SimClock source)
        {
            clock = source; clock.Paused = true; gameObject.SetActive(true);
            begin.onClick.RemoveAllListeners(); begin.onClick.AddListener(Begin);
            begin.Select();
        }
        public void Begin()
        {
            if (clock != null) clock.Paused = false;
            gameObject.SetActive(false); Closed?.Invoke();
        }
        public static MissionBriefing Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Mission briefing", 0, 0, 1, 1, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            var content = StatBar.Frame(panel, "Briefing content", .12f, .10f, .88f, .90f); StatBar.Stack(content);
            var view = panel.gameObject.AddComponent<MissionBriefing>();
            StatBar.Text(content, "Explore Mars", GameConstants.UiTitle, GameConstants.UiButton);
            StatBar.Text(content, "Gale Crater", GameConstants.UiHeading, GameConstants.UiLine);
            StatBar.Text(content, "Explore Gale Crater. Manage supplies, find shelter and survive " + GameConstants.MissionDurationDays + " Martian Days.", height: GameConstants.UiLine * 2);
            ControlGuide.Create(content, "Exploration Controls", ControlGuide.Exploration, 2);
            view.begin = StatBar.Button(content, "Begin mission", null);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
