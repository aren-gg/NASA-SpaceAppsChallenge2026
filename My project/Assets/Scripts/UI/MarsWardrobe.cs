using System;
using System.Collections.Generic;
using MarsSurvival.Gameplay;
using MarsSurvival.Interaction;
using MarsSurvival.World;
using MarsTrainer.Sim;
using MarsTrainer.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsSurvival.UI
{
    /// <summary>
    /// The wardrobe: customise the astronaut (body frame, skin, hair style and colour, suit and
    /// accent colours, visor, helmet, backpack, and whether the helmet comes off indoors).
    /// Press C while walking, or use the pause menu's Wardrobe button. The game pauses, the
    /// camera swings round to face the astronaut on the left of the screen, and every change
    /// is applied to the real body and saved straight away. Drag on the left to turn the
    /// astronaut; Reset puts back the look you opened with; C or Escape closes.
    /// </summary>
    public sealed class MarsWardrobe : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private static MarsWardrobe instance;
        private MarsMissionController mission;
        private MarsAstronautPlayer player;
        private GameObject canvasObject;
        private AstronautLook draft, opened;
        private bool wasPaused, previewHelmet;
        private float orbit = 180f;
        private int openedFrame;
        private readonly List<Action> refreshers = new List<Action>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AddToScene()
        {
            if (FindObjectOfType<MarsTerrain>() == null || FindObjectOfType<MarsWardrobe>() != null) return;
            new GameObject("Wardrobe").AddComponent<MarsWardrobe>();
        }

        private void Awake()
        {
            instance = this;
            PauseMenu.OpenWardrobe = OpenFromPauseMenu;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            IsOpen = false;
        }

        private void OpenFromPauseMenu()
        {
            PauseMenu pause = FindObjectOfType<PauseMenu>();
            if (pause != null) pause.Close();
            Show();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.C) && CanOpen()) Show();
                return;
            }
            if (Time.frameCount != openedFrame && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C))) { Hide(); return; }
            // Drag on the left half to turn the astronaut.
            if (Input.GetMouseButton(0) && Input.mousePosition.x < Screen.width * 0.55f)
                orbit -= Input.GetAxis("Mouse X") * 6f;
#endif
        }

        private bool CanOpen()
        {
            mission = MarsMissionController.Instance;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            return mission != null && mission.IsRunning && !mission.InputBlocked && !MarsHomeScreen.IsOpen &&
                   player != null && player.IsReady && !player.IsDriving && player.Body != null;
        }

        private void Show()
        {
            if (IsOpen) return;
            mission = MarsMissionController.Instance;
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (mission == null || player == null || player.Body == null || player.IsDriving) return;
            opened = AstronautLook.Current.Clone();
            draft = opened.Clone();
            orbit = 180f;
            previewHelmet = false;
            IsOpen = true;
            openedFrame = Time.frameCount;
            wasPaused = mission.Paused;
            mission.WardrobeOpen = true;
            mission.SetPaused(true);
            if (canvasObject == null) BuildUi();
            canvasObject.SetActive(true);
            Refresh();
        }

        private void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (canvasObject != null) canvasObject.SetActive(false);
            if (mission != null)
            {
                mission.WardrobeOpen = false;
                mission.SetPaused(wasPaused);
                mission.SetPanelOpen(false);
            }
        }

        /// <summary>Faces the camera to the astronaut, who stands on the left of the screen.</summary>
        private void LateUpdate()
        {
            if (!IsOpen || player == null || player.ViewCamera == null || player.Body == null) return;
            MarsAstronautBody body = player.Body;
            body.SetHeadVisible(true);
            body.SetHelmet(previewHelmet);
            Transform cam = player.ViewCamera.transform;
            float u = player.transform.lossyScale.y;
            Vector3 target = player.transform.position + Vector3.up * 1.05f * u;
            Quaternion around = Quaternion.Euler(5f, player.transform.eulerAngles.y + orbit, 0f);
            Vector3 back = -(around * Vector3.forward);
            float distance = 2.5f * u;
            // Stay in front of walls and furniture between the camera and the astronaut.
            RaycastHit hit;
            if (Physics.SphereCast(target, 0.15f * u, back, out hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(player.transform))
                distance = Mathf.Max(0.9f * u, hit.distance - 0.1f * u);
            Vector3 position = target + back * distance;
            Quaternion look = Quaternion.LookRotation(target - position, Vector3.up);
            // Aim a little right of the astronaut so they sit in the left part of the frame.
            Vector3 aim = target + look * Vector3.right * 0.62f * u;
            cam.SetPositionAndRotation(position, Quaternion.LookRotation(aim - position, Vector3.up));
        }

        // ------------------------------------------------------------------ choices

        private void Change(Func<AstronautLook, int> get, Action<AstronautLook, int> set, int count, int delta)
        {
            set(draft, AstronautLook.Mod(get(draft) + delta, count));
            AstronautLook.Apply(draft);
            Refresh();
        }

        private void Refresh()
        {
            foreach (Action a in refreshers) a();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            canvasObject = new GameObject("Wardrobe Canvas");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(GameConstants.UiWidth, GameConstants.UiHeight);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            RectTransform root = canvasObject.GetComponent<RectTransform>();
            RectTransform title = StatBar.Frame(root, "Title", 0.03f, 0.885f, 0.27f, 0.965f, GameConstants.UiSurface);
            TMP_Text heading = StatBar.Text(title, "Wardrobe", GameConstants.UiTitle * 0.8f, GameConstants.UiLine * 1.4f);
            heading.characterSpacing = 8f;
            heading.rectTransform.anchorMin = Vector2.zero; heading.rectTransform.anchorMax = Vector2.one;
            heading.margin = new Vector4(GameConstants.UiGap * 2f, 0f, GameConstants.UiGap, 0f);
            RectTransform hint = StatBar.Frame(root, "Hint", 0.03f, 0.035f, 0.40f, 0.085f, GameConstants.UiSurface);
            TMP_Text hintText = StatBar.Text(hint, "Drag to turn the astronaut    C or Esc to close", GameConstants.UiBody * 0.7f);
            hintText.rectTransform.anchorMin = Vector2.zero; hintText.rectTransform.anchorMax = Vector2.one;
            hintText.margin = new Vector4(GameConstants.UiGap * 1.5f, 0f, GameConstants.UiGap, 0f);
            hintText.color = MarsTrainer.UI.UiTheme.Dim;

            RectTransform panel = StatBar.Frame(root, "Wardrobe Panel", 0.56f, 0.04f, 0.97f, 0.96f, GameConstants.UiSurface);
            panel.GetComponent<Image>().raycastTarget = true;
            StatBar.Stack(panel);
            VerticalLayoutGroup stack = panel.GetComponent<VerticalLayoutGroup>();
            stack.spacing = 6f;
            stack.childForceExpandWidth = true;

            Section(panel, "Body");
            Row(panel, "Frame", AstronautLook.Frames, l => l.frame, (l, v) => l.frame = v);
            Row(panel, "Skin", AstronautLook.SkinNames, l => l.skin, (l, v) => l.skin = v);
            Row(panel, "Hair", AstronautLook.HairStyles, l => l.hairStyle, (l, v) => l.hairStyle = v);
            Row(panel, "Hair colour", AstronautLook.HairColorNames, l => l.hairColor, (l, v) => l.hairColor = v);
            Section(panel, "Suit");
            Row(panel, "Suit", AstronautLook.SuitNames, l => l.suit, (l, v) => l.suit = v);
            Row(panel, "Accent", AstronautLook.AccentNames, l => l.accent, (l, v) => l.accent = v);
            Row(panel, "Visor", AstronautLook.VisorNames, l => l.visor, (l, v) => l.visor = v);
            Row(panel, "Helmet", AstronautLook.Helmets, l => l.helmet, (l, v) => l.helmet = v);
            Row(panel, "Backpack", AstronautLook.Backpacks, l => l.backpack, (l, v) => l.backpack = v);
            Row(panel, "Indoors", AstronautLook.IndoorHelmet, l => l.indoorHelmet, (l, v) => l.indoorHelmet = v);

            RectTransform actions = Line(panel, "Actions");
            Button preview = StatBar.Button(actions, "Preview: helmet off", null);
            TMP_Text previewLabel = preview.GetComponentInChildren<TMP_Text>();
            preview.onClick.AddListener(() => { previewHelmet = !previewHelmet; Refresh(); });
            refreshers.Add(() => previewLabel.text = previewHelmet ? "Preview: helmet on" : "Preview: helmet off");
            Flexible(preview, 1.4f);
            Flexible(StatBar.Button(actions, "Reset", () => { draft = opened.Clone(); AstronautLook.Apply(draft); Refresh(); }), 1f);
            Flexible(StatBar.Button(actions, "Done", Hide), 1f);
            foreach (TMP_Text t in actions.GetComponentsInChildren<TMP_Text>()) Shrink(t);
        }

        private void Section(RectTransform panel, string text)
        {
            TMP_Text t = StatBar.Text(panel, text.ToUpperInvariant(), GameConstants.UiBody * 0.7f, 34f);
            t.font = UiTypeface.Strong;
            t.characterSpacing = 6f;
            t.color = UiTheme.Accent;
        }

        private static RectTransform Line(RectTransform panel, string name)
        {
            RectTransform row = StatBar.Frame(panel, name, 0, 0, 1, 1);
            StatBar.Height(row, 54f);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;
            return row;
        }

        private void Row(RectTransform panel, string title, string[] names, Func<AstronautLook, int> get, Action<AstronautLook, int> set)
        {
            RectTransform row = Line(panel, title);
            TMP_Text label = StatBar.Text(row, title, GameConstants.UiBody * 0.8f, 54f);
            Fixed(label.gameObject, 170f);
            Button prev = StatBar.Button(row, "<", () => Change(get, set, names.Length, -1));
            Fixed(prev.gameObject, 54f);
            TMP_Text value = StatBar.Text(row, "", GameConstants.UiBody * 0.8f, 54f);
            value.alignment = TextAlignmentOptions.Center;
            Shrink(value);
            value.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Button next = StatBar.Button(row, ">", () => Change(get, set, names.Length, 1));
            Fixed(next.gameObject, 54f);
            refreshers.Add(() =>
            {
                int i = AstronautLook.Mod(get(draft), names.Length);
                value.text = names[i] + "  (" + (i + 1) + "/" + names.Length + ")";
            });
        }

        private static void Fixed(GameObject go, float width)
        {
            LayoutElement e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            e.minWidth = width; e.preferredWidth = width; e.flexibleWidth = 0f;
        }

        private static void Flexible(Button button, float weight)
        {
            LayoutElement e = button.GetComponent<LayoutElement>();
            e.minWidth = 0f; e.flexibleWidth = weight;
        }

        private static void Shrink(TMP_Text t)
        {
            t.enableWordWrapping = false;
            t.enableAutoSizing = true;
            t.fontSizeMin = 14f;
            t.fontSizeMax = t.fontSize;
        }
    }
}
