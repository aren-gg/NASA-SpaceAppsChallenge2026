using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class EndScreen : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text report;
        [SerializeField] private Button review;
        [SerializeField] private Button restart;
        public System.Action ReviewRequested { get; set; }
        public void Show(bool completed, double sols, string hurt, string saved, double totalDose)
        {
            gameObject.SetActive(true);
            title.text = completed ? "You Survived " + GameConstants.MissionDurationDays + " Martian Days!" : "Your Mission Has Ended";
            report.text = "Mission Report - " + sols.ToString("0.0") + " Martian Days\n\nWhat Hurt: " + hurt
                + "\n\nWhat Helped: " + saved + "\n\nTotal Dose: " + totalDose.ToString("0.00") + " mSv";
            review.onClick.RemoveAllListeners(); review.onClick.AddListener(Review);
            if (restart != null) { restart.onClick.RemoveAllListeners(); restart.onClick.AddListener(Restart); }
            review.Select();
        }
        private void Restart()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        }
        private void Review() { gameObject.SetActive(false); ReviewRequested?.Invoke(); }
        public static EndScreen Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Year report", 0, 0, 1, 1, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            var content = StatBar.Frame(panel, "Report content", 0.14f, 0.08f, 0.86f, 0.92f); StatBar.Stack(content);
            var view = panel.gameObject.AddComponent<EndScreen>();
            view.title = StatBar.Text(content, "Your Mission Report", GameConstants.UiTitle, GameConstants.UiCard);
            view.report = StatBar.Text(content, "What Hurt: awaiting mission data\n\nWhat Helped: awaiting mission data\n\nTotal Dose: -- mSv",
                height: GameConstants.UiCard * 3);
            view.review = StatBar.Button(content, "Review my outpost (time stays stopped)", null);
            view.restart = StatBar.Button(content, "Start a new mission", null);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
