using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.Gameplay.World
{
    public sealed class WorldHud : MonoBehaviour
    {
        private TerrainGenerator world;
        private Image tank;
        private Text status;
        private Text points;
        private Font font;
        private string[] pointLabels;
        private int collected;

        public void Initialize(TerrainGenerator world)
        {
            this.world = world;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasOwner = new GameObject("P9 HUD", typeof(Canvas), typeof(CanvasScaler));
            canvasOwner.transform.SetParent(transform, false);
            var canvas = canvasOwner.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = new GameObject("Readout", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(canvasOwner.transform, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 1);
            panel.sizeDelta = new Vector2(GameConstants.HudWidth, GameConstants.HudHeight);
            AddText(panel, "WASD / left stick: walk | Shift: sprint", 0);
            AddText(panel, "Cave crystals: +" + GameConstants.PickupRP.ToString("0") + " RP | Base: refill O2", 1);
            points = AddText(panel, "RP: 0", 2);
            status = AddText(panel, "At base: oxygen refilling", 3);
            var bar = new GameObject("Oxygen remaining", typeof(RectTransform), typeof(Image));
            var rect = bar.GetComponent<RectTransform>();
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, -GameConstants.HudFontSize * 5);
            rect.sizeDelta = new Vector2(GameConstants.HudWidth, GameConstants.HudFontSize);
            tank = bar.GetComponent<Image>();
            tank.color = TerrainGenerator.ColorFromHex(GameConstants.PickupColor);
            var pickups = world.GetComponentsInChildren<ResearchPickup>();
            pointLabels = new string[pickups.Length + 1];
            for (int i = 0; i < pointLabels.Length; i++) pointLabels[i] = "RP from samples: " + (i * GameConstants.PickupRP).ToString("0");
            points.text = pointLabels[0];
            foreach (var pickup in pickups) pickup.CollectedChanged += OnCollected;
        }

        private Text AddText(Transform parent, string message, int row)
        {
            var owner = new GameObject(message, typeof(RectTransform), typeof(Text));
            var rect = owner.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, -row * GameConstants.HudFontSize);
            rect.sizeDelta = new Vector2(GameConstants.HudWidth * 2, GameConstants.HudFontSize * 2);
            var text = owner.GetComponent<Text>();
            text.font = font;
            text.fontSize = GameConstants.HudFontSize;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = message;
            return text;
        }

        private void OnCollected() { points.text = pointLabels[++collected]; }
        private void Update()
        {
            if (world == null || world.Player == null || tank == null) return;
            // Cached literal strings and a scaled bar: no formatted strings each frame.
            tank.rectTransform.localScale = new Vector3((float)(world.Player.Expedition.TankSeconds /
                GameConstants.OxygenTankSeconds), 1, 1);
            status.text = !world.Simulation.State.Alive ? "Run ended: oxygen exhausted" : world.Player.Expedition.AtBase
                ? "At base: oxygen refilling" : "Outside: oxygen draining. Return to base.";
        }
    }
}
