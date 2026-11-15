using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class SunZapMeterView : MonoBehaviour
    {
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text rateText;
        [SerializeField] private StatBar total;
        [SerializeField] private RectTransform needle;
        public SuitShieldState CurrentState { get; private set; }
        public void ShowState(SuitShieldState state)
        {
            CurrentState = state;
            string word; uint color;
            switch (state)
            {
                case SuitShieldState.Warn: word = "Radiation / Warning"; color = GameConstants.UiWarn; break;
                case SuitShieldState.Danger: word = "Radiation / Danger"; color = GameConstants.UiDanger; break;
                case SuitShieldState.Critical: word = "Radiation / Critical"; color = GameConstants.UiCritical; break;
                default: word = "Radiation / Safe"; color = GameConstants.UiSafe; break;
            }
            stateText.text = word; stateText.color = StatBar.ColorOf(color);
        }
        public void ShowDose(double rate, double dose)
        {
            rateText.text = "Dose: " + rate.ToString("0.00") + " mSv / Martian Day";
            total.Show("Total dose (mSv)", dose, GameConstants.H_limit);
            if (needle != null) needle.localRotation = Quaternion.Euler(0, 0,
                -GameConstants.UiDialDegrees * Mathf.Clamp01((float)(rate / GameConstants.UiDialMaxRate)));
        }
        public static SunZapMeterView Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Sun-Zap Meter", .53f, .31f, .97f, .59f, GameConstants.UiPanel);
            StatBar.Stack(panel);
            var view = panel.gameObject.AddComponent<SunZapMeterView>();
            view.stateText = StatBar.Text(panel, "Radiation / Safe");
            view.rateText = StatBar.Text(panel, "Dose: -- mSv / Martian Day");
            view.total = StatBar.Create(panel, "Total dose (mSv)");
            return view;
        }
    }
}
