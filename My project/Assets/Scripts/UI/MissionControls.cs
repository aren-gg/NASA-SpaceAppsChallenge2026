using MarsTrainer.Core;
using MarsTrainer.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    public sealed class MissionControls : MonoBehaviour
    {
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button[] buttons;
        private bool wired;
        private MissionSession mission;
        private System.Action exercise;
        public void Bind(MissionSession source, System.Action exercise = null) { mission = source; this.exercise = exercise; Wire(); }
        private void Awake() { Wire(); }
        private void Wire()
        {
            if (wired || buttons == null || buttons.Length == 0) return;
            wired = true;
            buttons[0].onClick.AddListener(Tend); buttons[1].onClick.AddListener(Eat);
            buttons[2].onClick.AddListener(Drink); buttons[3].onClick.AddListener(Sleep);
            buttons[4].onClick.AddListener(Heal);
            if (buttons.Length > 5) buttons[5].onClick.AddListener(Exercise);
        }
        private bool CanAct => mission != null && mission.Simulation.State.Alive &&
            !mission.Simulation.Clock.Paused && mission.Simulation.Clock.ElapsedSols < GameConstants.MissionDurationDays;
        public bool FoodFull => mission != null && mission.Simulation.State.Hunger <= 0;
        public bool WaterFull => mission != null && mission.Simulation.State.Water >= GameConstants.StatMaximum;
        public bool CanEat => CanAct && !FoodFull && mission.Inventory.FoodKcal > 0;
        public bool CanDrink => CanAct && !WaterFull && mission.Inventory.WaterLitres > 0;
        private bool CanUseBase => CanAct && mission.AtBase;
        private void Tend() { if (CanUseBase) mission.TendFarm(); }
        public void Eat() { if (CanEat) mission.Simulation.Survival.Eat(mission.Simulation.State, GameConstants.AstronautKcalPerSol); }
        public void Drink() { if (CanDrink) mission.Simulation.Survival.Drink(mission.Simulation.State, GameConstants.WaterLitresPerSol); }
        private void Sleep() { if (CanUseBase) mission.Simulation.Survival.Sleeping = !mission.Simulation.Survival.Sleeping; }
        private void Exercise() { if (CanUseBase) exercise?.Invoke(); }
        private void Heal() { if (CanUseBase) mission.Simulation.Survival.Medbay(mission.Simulation.State, GameConstants.BotMedbayKWh); }
        public void Refresh()
        {
            if (mission == null) return;
            for (int i = 0; i < buttons.Length; i++) buttons[i].interactable = CanAct && (i == 1 || i == 2 || mission.AtBase) &&
                (i != 5 || (exercise != null && !mission.Simulation.Survival.Sleeping));
            buttons[1].interactable = CanEat; buttons[2].interactable = CanDrink;
            status.text = (mission.AtBase ? "Base" : mission.Sheltered ? "Cave" : "EVA") +
                " | Food " + mission.Inventory.FoodKcal.ToString("0") + " kcal | Water " + mission.Inventory.WaterLitres.ToString("0") +
                " L | " + (mission.Simulation.Survival.Sleeping ? "Resting" : "Awake");
        }
        public static MissionControls Create(Transform parent)
        {
            var panel = StatBar.Frame(parent, "Base actions", 0.03f, 0.015f, 0.97f, 0.30f, GameConstants.UiSurface);
            StatBar.Stack(panel); var view = panel.gameObject.AddComponent<MissionControls>();
            view.status = StatBar.Text(panel, "Landing supplies");
            var row = StatBar.Frame(panel, "Actions", 0, 0, 1, 1); StatBar.Height(row, GameConstants.UiButton * 2 + GameConstants.UiGap);
            var layout = row.gameObject.AddComponent<GridLayoutGroup>();
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 3;
            layout.cellSize = new Vector2(GameConstants.UiKitActionWidth, GameConstants.UiButton);
            layout.spacing = Vector2.one * GameConstants.UiGap; layout.childAlignment = TextAnchor.MiddleCenter;
            view.buttons = new[]
            {
                StatBar.Button(row, "Tend farm", null), StatBar.Button(row, "Eat", null),
                StatBar.Button(row, "Drink", null), StatBar.Button(row, "Rest / Wake", null),
                StatBar.Button(row, "Heal: Max " + GameConstants.BotMedbayKWh.ToString("0.#") + " kWh", null),
                StatBar.Button(row, "Exercise: " + GameConstants.MissionExerciseSols.ToString("0.##") + " day", null)
            };
            ResourceSymbol[] icons = {ResourceSymbol.Food,ResourceSymbol.Food,ResourceSymbol.Water,
                ResourceSymbol.Rest,ResourceSymbol.Health,ResourceSymbol.Power};
            for(int i=0;i<view.buttons.Length;i++) ResourceGlyph.Decorate(view.buttons[i],icons[i]);
            view.Wire();
            return view;
        }
    }
}
