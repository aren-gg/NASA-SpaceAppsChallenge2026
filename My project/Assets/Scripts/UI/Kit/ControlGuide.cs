using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.UI
{
    public static class ControlGuide
    {
        public static readonly string[,] Exploration =
        {
            // Outpost mission controls (first-person astronaut, rover and tools).
            {"WASD / Mouse", "Walk / Look"}, {"Shift / Space", "Sprint / Jump"},
            {"Scroll", "Camera Zoom (1st / 3rd)"}, {"Ctrl", "Shift Lock On / Off"},
            {"Right Mouse", "Turn Camera (no shift lock)"},
            {"1 - 9, 0", "Choose Tool"}, {"Click / R", "Use Tool / Extra"},
            {"E", "Pick Up Tool / Rover"}, {"Q / B", "Put Tool / All Back"},
            {"F / G", "Eat / Drink"}, {"Tab", "Outpost Console"},
            {"H", "Daily Task Help"}, {"P", "Pause / Resume"},
            {"C", "Wardrobe"},
            {"F5 / F9", "Save / Load"}, {"Escape", "Menu / Back"}
        };
        public static readonly string[,] Menus =
        {
            {"Arrows", "Move Focus"}, {"Enter", "Choose"},
            {"Tab", "Next Choice"}, {"Shift + Tab", "Previous Choice"}
        };
        public static RectTransform Create(Transform parent, string name, string[,] controls, int columns = 1)
        {
            int rows = (controls.GetLength(0) + columns - 1) / columns;
            var owner = StatBar.Frame(parent, name, 0, 0, 1, 1);
            StatBar.Height(owner, rows * GameConstants.UiControlRowHeight);
            for (int i = 0; i < controls.GetLength(0); i++)
            {
                int column = i / rows, row = i % rows;
                float x = column / (float)columns, y = 1 - (row + 1f) / rows;
                var line = StatBar.Frame(owner, controls[i, 1] + " Control", x, y, x + 1f / columns, y + 1f / rows);
                var key = StatBar.Text(line, controls[i, 0]);
                Place(key.rectTransform, 0, GameConstants.UiControlKeyFraction);
                key.margin = new Vector4(GameConstants.UiControlIndent, 0, GameConstants.UiGap, 0);
                key.color = StatBar.ColorOf(GameConstants.UiCopper);
                key.font = UiTypeface.Strong;                       // key names: SemiBold, never italic
                key.fontStyle &= ~TMPro.FontStyles.Italic;
                var action = StatBar.Text(line, controls[i, 1]);
                Place(action.rectTransform, GameConstants.UiControlKeyFraction, 1);
                action.margin = new Vector4(GameConstants.UiGap, 0, GameConstants.UiGap, 0);
            }
            return owner;
        }
        private static void Place(RectTransform rect, float left, float right)
        {
            rect.anchorMin = new Vector2(left, 0); rect.anchorMax = new Vector2(right, 1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
