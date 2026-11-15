using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class TutorialPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text prompt;
        [SerializeField] private Button skip;
        private bool wired;
        private bool skipped;
        private int shown = -1;
        private static readonly string[] Steps =
        {
            "WASD / Left Stick: Move\nRMB / Right Stick: Look\nReturn Before Air Runs Out.",
            "Farm: Tend Farm plants, waters and harvests. Eat and drink often.",
            "Energy: Check In, Out and battery. Dust and night cut solar power.",
            "Shelter: Enter base or a cave before flares. Dose stays with you.",
            "Research: Press R, choose an upgrade, then buy. Find samples in caves."
        };

        public void ShowSol(int sol)
        {
            Wire();
            bool visible = !skipped && sol >= 0 && sol < GameConstants.TutorialSols;
            gameObject.SetActive(visible);
            if (!visible || shown == sol) return;
            shown = sol; prompt.text = Steps[sol];
        }
        public void Skip() { skipped = true; gameObject.SetActive(false); }
        private void Awake() { Wire(); }
        private void Wire()
        {
            if (wired || skip == null) return;
            wired = true; skip.onClick.AddListener(Skip);
        }

        public static TutorialPanel Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "First five sols", .53f, .59f, .97f, .89f, GameConstants.UiSurface);
            StatBar.Stack(panel);
            var view = panel.gameObject.AddComponent<TutorialPanel>();
            view.prompt = StatBar.Text(panel, Steps[0], height: GameConstants.UiLine * 2.5f);
            view.prompt.margin = new Vector4(GameConstants.UiControlIndent, 0, 0, 0);
            view.skip = StatBar.Button(panel, "Skip tutorial", null); view.Wire();
            return view;
        }
    }
}
