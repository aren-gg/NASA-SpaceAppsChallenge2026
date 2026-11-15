using System.Collections;
using System.IO;
using MarsTrainer.UI;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using MarsTrainer.Data.Research;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MarsTrainer.Tests
{
    public sealed class HudInputTests
    {
        private GameObject root;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.BackgroundBehavior previousBackground;
        private bool previousRunInBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
#endif
        [SetUp] public void Setup()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousRunInBackground = Application.runInBackground;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
#if UNITY_EDITOR
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (root != null) Object.Destroy(root);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = previousBackground;
            Application.runInBackground = previousRunInBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
#endif
            yield return null;
        }
        [UnityTest]
        public IEnumerator DisabledHudIgnoresEventsAndReenablePreservesWarnings()
        {
            root = Object.Instantiate(Resources.Load<GameObject>("MarsTrainerHUD"));
            var sim = new Simulation(); root.GetComponent<HudView>().Bind(sim); yield return null;
            var banner = root.GetComponentInChildren<AlertBanner>(true);
            sim.Events.ReportFlareAlert(1, 1); root.SetActive(false); yield return null;
            sim.Events.ReportFlareAlert(2, 1); Assert.That(banner.PendingCount, Is.EqualTo(1));
            root.SetActive(true); yield return null; sim.Events.ReportFlareAlert(3, 1);
            Assert.That(banner.PendingCount, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator KeyboardAndMouseAt720p() { yield return Exercise(1280, 720); }
        [UnityTest] public IEnumerator KeyboardAndMouseAt1080p() { yield return Exercise(1920, 1080); }
        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        private IEnumerator Click(Button button)
        {
            var rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
        }
        private IEnumerator Exercise(int width, int height)
        {
            Screen.SetResolution(width, height, false); yield return null; yield return null;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            root = Object.Instantiate(Resources.Load<GameObject>("MarsTrainerHUD"));
            var sim = new Simulation();
            ResearchSystem upgrades = null;
#if UNITY_EDITOR
            var ids = UnityEditor.AssetDatabase.FindAssets("t:ResearchNodeSO", new[] { "Assets/Data/Research" });
            var nodes = new ResearchNode[ids.Length];
            for (int i = 0; i < ids.Length; i++) nodes[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ResearchNodeSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(ids[i])).ToNode();
            upgrades = new ResearchSystem(nodes, sim.Modifiers); upgrades.AddRP(20);
#endif
            root.GetComponent<HudView>().Bind(sim, upgrades);
            yield return null; yield return null;
            var research = root.GetComponentInChildren<ResearchScreen>(true);
            var pause = root.GetComponentInChildren<PauseMenu>(true);
            var end = root.GetComponentInChildren<EndScreen>(true);
            yield return Press(Key.Digit3); Assert.That(sim.Clock.Speed, Is.EqualTo(4));
            yield return Press(Key.Space); Assert.That(sim.Clock.Paused, Is.True);
            yield return Press(Key.Space); Assert.That(sim.Clock.Paused, Is.False);
            Capture("hud", width);
            var hud = root.GetComponent<HudView>();
            yield return Press(Key.E); Assert.That(hud.KitOpen, Is.True);
            yield return Press(Key.Tab);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(
                root.transform.Find("Safe area/Mission HUD/Field kit")), Is.True);
            yield return Press(Key.Escape); Assert.That(hud.KitOpen, Is.False);
            Assert.That(pause.gameObject.activeSelf, Is.False);
            yield return Press(Key.E); Assert.That(hud.KitOpen, Is.True);
            var close = root.transform.Find("Safe area/Mission HUD/Field kit/Close [E]").GetComponent<Button>();
            yield return Click(close); Assert.That(hud.KitOpen, Is.False);
            yield return Press(Key.R); Assert.That(research.gameObject.activeSelf, Is.True);
            Assert.That(sim.Clock.Paused, Is.True); Capture("research", width);
            var scroll = research.GetComponentInChildren<ScrollRect>();
            var firstCard = scroll.content.GetComponentInChildren<Button>();
            var cardRect = (RectTransform)firstCard.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, cardRect.TransformPoint(cardRect.rect.center));
            float before = scroll.verticalNormalizedPosition;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = new Vector2(0, -1) }); yield return null;
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(before), "Wheel over a card must scroll its path");
            yield return Press(Key.Tab);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(research.transform), Is.True);
            yield return Press(Key.Escape); Assert.That(research.gameObject.activeSelf, Is.False);
            Assert.That(sim.Clock.Paused, Is.False);
            yield return Press(Key.Escape); Assert.That(pause.gameObject.activeSelf, Is.True); Capture("pause", width);
            yield return Press(Key.Enter); Assert.That(pause.gameObject.activeSelf, Is.False);
            var menu = root.transform.Find("Safe area/Mission HUD/Screen buttons/Menu [Escape]").GetComponent<Button>();
            yield return Click(menu); Assert.That(pause.gameObject.activeSelf, Is.True);
            yield return Press(Key.Escape);
            sim.State.Health = 0; sim.Tick(0); yield return null;
            Assert.That(end.gameObject.activeSelf, Is.True); Capture("report", width);
            yield return Press(Key.Enter); Assert.That(end.gameObject.activeSelf, Is.False);
            yield return null; yield return null;
        }
        private void Capture(string screen, int width)
        {
            const string directory = "/tmp/mars-p10-previews"; Directory.CreateDirectory(directory);
            var canvas = root.GetComponent<Canvas>(); var scaler = root.GetComponent<CanvasScaler>();
            var cameraOwner = new GameObject("UI capture camera", typeof(Camera));
            var camera = cameraOwner.GetComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = StatBar.ColorOf(GameConstants.GroundColor);
            var target = new RenderTexture(width, width * 9 / 16, 24); target.Create(); camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            scaler.enabled = false; canvas.scaleFactor = width / GameConstants.UiWidth;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, "p10-" + screen + "-" + width + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            scaler.enabled = true; camera.targetTexture = null; target.Release();
            Object.Destroy(target); Object.Destroy(image); Object.Destroy(cameraOwner);
        }
    }
}
