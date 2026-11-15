using System.Collections.Generic;
using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class AlertBanner : MonoBehaviour
    {
        [SerializeField] private TMP_Text message;
        [SerializeField] private TMP_Text countdown;
        [SerializeField] private Button recall;
        [SerializeField] private Button next;
        [SerializeField] private CanvasGroup visibility;
        private bool flareForecast;
        private readonly Queue<string> warnings = new Queue<string>();
        public int PendingCount => warnings.Count;
        public System.Action RecallRequested { get; set; }
        public void Enqueue(string warning)
        {
            if (warnings.Count >= GameConstants.UiAlertCapacity) warnings.Dequeue();
            warnings.Enqueue(warning); Render();
        }
        public void Clear() { warnings.Clear(); flareForecast = false; countdown.text = "No Flare Forecast"; Render(); }
        private void Render()
        {
            message.text = warnings.Count == 0 ? "O  No Warnings. Keep exploring." : "!  " + warnings.Peek();
            next.interactable = warnings.Count > 0;
            if (visibility != null)
            {
                bool show = warnings.Count > 0 || flareForecast;
                visibility.alpha = show ? 1 : 0; visibility.interactable = visibility.blocksRaycasts = show;
            }
        }
        public void HideCountdown() { flareForecast = false; countdown.text = "No Flare Forecast"; Render(); }
        public void ShowCountdown(double sols)
        {
            flareForecast = true; Render();
            countdown.text = sols > 0 ? "Flare In " + sols.ToString("0.00") + " Martian Days" : "Flare Active - Find Shelter";
        }
        public void ShowRecall(bool available)
        {
            if (recall != null) recall.interactable = available;
        }
        public void Acknowledge() { if (warnings.Count > 0) warnings.Dequeue(); Render(); }
        private bool wired;
        private void Awake() { Wire(); }
        private void Wire()
        {
            if (wired || next == null) return;
            wired = true;
            next.onClick.AddListener(Acknowledge);
            if (recall != null) recall.onClick.AddListener(RequestRecall);
        }
        private void RequestRecall() { RecallRequested?.Invoke(); }
        public static AlertBanner Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Warnings", .31f, .73f, .69f, .90f, GameConstants.UiSurface);
            var view = panel.gameObject.AddComponent<AlertBanner>();
            view.visibility = panel.gameObject.AddComponent<CanvasGroup>();
            view.message = StatBar.Text(panel, "No Warnings");
            Position(view.message, .03f, .38f, .82f, .95f);
            view.countdown = StatBar.Text(panel, "No Flare Forecast");
            Position(view.countdown, .03f, .05f, .82f, .35f);
            view.next = StatBar.Button(panel, "OK", null);
            Position(view.next, .85f, .52f, .98f, .90f);
            view.Wire(); view.Render();
            return view;
        }
        private static void Position(Component component, float x0, float y0, float x1, float y1)
        {
            var rect = (RectTransform)component.transform;
            rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
