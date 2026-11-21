using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class EndScreen : MonoBehaviour
    {
        [SerializeField] private TMP_Text report;
        [SerializeField] private Button restart;
        public void Show(string reason, double sols)
        {
            gameObject.SetActive(true);
            report.text = reason + "\n\nYou lasted " + sols.ToString("0.0") + " sols.";
            restart.Select();
        }
        private static void Restart()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        }
        public static EndScreen Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "End screen", 0, 0, 1, 1, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            var content = StatBar.Frame(panel, "End content", 0.25f, 0.3f, 0.75f, 0.7f); StatBar.Stack(content);
            var view = panel.gameObject.AddComponent<EndScreen>();
            StatBar.Text(content, "The astronaut did not make it", GameConstants.UiTitle, GameConstants.UiCard);
            view.report = StatBar.Text(content, "", height: GameConstants.UiCard);
            view.restart = StatBar.Button(content, "Start again", Restart);
            panel.gameObject.SetActive(false); return view;
        }
    }
}
