using System.Collections;
using System.Collections.Generic;
using MarsSurvival.World;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsSurvival.UI
{
    /// <summary>
    /// Title screen shown when the Mars scene starts. The camera slowly circles the
    /// outpost while the menu (Start mission, How to play, Quit) sits on the left.
    /// Start mission fades out, puts the camera back where the scene had it, and hands
    /// control to the astronaut (which waits while <see cref="IsOpen"/> is true).
    ///
    /// The whole UI is built in code with uGUI and the eight palette colours, so no
    /// scene, prefab or font asset is needed. A home screen is added automatically when
    /// a scene with a MarsTerrain starts without one; to change the text or turn it off,
    /// add this component to a scene object and edit it there.
    /// </summary>
    public sealed class MarsHomeScreen : MonoBehaviour
    {
        /// <summary>True while the home screen owns the camera and the player should wait.</summary>
        public static bool IsOpen { get; private set; }

        [Header("Text")]
        public string eyebrow = "GALE CRATER  ·  4.6° S  137.4° E";
        [Tooltip("Marsivity: Mars + immersivity. The first part is drawn in white, the second in orange.")]
        public string titleWhite = "MARS";
        public string titleOrange = "IVITY";
        [TextArea] public string tagline = "The connection between nature, technology, and humanity";
        public string credit = "Terrain from NASA / USGS elevation data of Gale Crater  ·  NASA Space Apps Challenge 2026";

        [Header("Background camera (real metres)")]
        public bool showOnStart = true;
        [Tooltip("What the camera circles. Empty: the object named \"Mars Outpost\" if there is one, " +
                 "otherwise a point 25 m ahead of the scene's starting view.")]
        public Transform orbitTarget;
        public float orbitRadiusMeters = 34f;
        public float orbitHeightMeters = 9f;
        public float orbitDegreesPerSecond = 2.5f;
        [Tooltip("Turns the view so the outpost sits to the right of the menu.")]
        public float framingYawDegrees = 15f;
        [Range(20f, 90f)] public float fieldOfView = 45f;

        private enum State { Open, Closing, Closed }

        private State state = State.Open;
        private Camera cam;
        private Transform camParent;
        private Vector3 camPosition;
        private Quaternion camRotation;
        private float camFov;
        private MarsTerrainFlyCamera flyCamera;
        private bool flyCameraWasEnabled;
        private MarsTerrainDataOverlay dataOverlay;
        private MarsClockHud clockHud;
        private bool clockHudWasVisible;
        private MarsTerrain terrain;
        private float orbitAngle;
        private Vector3 fallbackTarget;

        private TMP_FontAsset font;
        private Sprite roundFill, roundRing;
        private GameObject menuCanvas;
        private Image fade;
        private CanvasGroup helpGroup;
        private RectTransform helpCard;
        private bool helpOpen;
        private float helpBlend;
        private TMP_Text hint;
        private float openedAt;

        private struct Reveal
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Vector2 home;
            public float delay;
        }
        private readonly List<Reveal> reveals = new List<Reveal>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AddToTerrainScene()
        {
            if (FindObjectOfType<MarsHomeScreen>() != null) return;
            if (FindObjectOfType<MarsTerrain>() == null || Camera.main == null) return;
            new GameObject("Home Screen").AddComponent<MarsHomeScreen>();
        }

        private void Awake()
        {
            if (!showOnStart)
            {
                Destroy(gameObject);
                return;
            }
            // Set straight away so the astronaut, created in the same frame, waits.
            IsOpen = true;
        }

        private void OnDestroy()
        {
            if (state != State.Closed) IsOpen = false;
        }

        private void Start()
        {
            cam = Camera.main;
            if (cam == null)
            {
                IsOpen = false;
                Destroy(gameObject);
                return;
            }
            camParent = cam.transform.parent;
            camPosition = cam.transform.position;
            camRotation = cam.transform.rotation;
            camFov = cam.fieldOfView;

            flyCamera = cam.GetComponent<MarsTerrainFlyCamera>();
            if (flyCamera != null)
            {
                flyCameraWasEnabled = flyCamera.enabled;
                flyCamera.enabled = false;
            }
            dataOverlay = FindObjectOfType<MarsTerrainDataOverlay>();
            if (dataOverlay != null)
            {
                dataOverlay.visible = false;
            }
            clockHud = FindObjectOfType<MarsClockHud>();
            if (clockHud != null)
            {
                clockHudWasVisible = clockHud.visible;
                clockHud.visible = false;
            }
            terrain = FindObjectOfType<MarsTerrain>();
            if (orbitTarget == null)
            {
                GameObject outpost = GameObject.Find("Mars Outpost");
                if (outpost != null) orbitTarget = outpost.transform;
            }

            Vector3 forward = camRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            fallbackTarget = camPosition + forward.normalized * Units(25f);
            // Start the orbit on the player's side of the base, so the first view matches the game.
            Vector3 fromTarget = camPosition - OrbitTarget();
            orbitAngle = Mathf.Atan2(fromTarget.x, fromTarget.z) * Mathf.Rad2Deg;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // MarsTrainer UI typeface (Counter-Strike UI SDF), shared with the mission HUD.
            font = MarsTrainer.UI.UiTypeface.Font;
            if (font == null) font = TMP_Settings.defaultFontAsset;
            roundFill = MarsTrainer.UI.UiTheme.Slot(false);
            roundRing = MarsTrainer.UI.UiTheme.Slot(true);
            EnsureEventSystem();
            BuildFade();
            BuildMenu();
            openedAt = Time.unscaledTime;
            fade.color = WithAlpha(MarsTrainer.UI.UiTheme.Ink, 1f);
        }

        private float Units(float meters)
        {
            return terrain != null && terrain.config != null ? terrain.config.ToUnits(meters) : meters;
        }

        // ------------------------------------------------------------------ camera

        private Vector3 OrbitTarget()
        {
            if (orbitTarget != null) return orbitTarget.position + Vector3.up * Units(2f);
            return fallbackTarget;
        }

        private void LateUpdate()
        {
            if (state == State.Closed || cam == null) return;

            orbitAngle += orbitDegreesPerSecond * Time.unscaledDeltaTime;
            Vector3 target = OrbitTarget();
            float a = orbitAngle * Mathf.Deg2Rad;
            Vector3 position = target + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Units(orbitRadiusMeters)
                                      + Vector3.up * Units(orbitHeightMeters);
            if (terrain != null && terrain.IsReady)
            {
                position.y = Mathf.Max(position.y, terrain.GetGroundY(position) + Units(2f));
            }

            Vector3 look = target - position;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            cam.transform.SetParent(null, true);
            cam.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw - framingYawDegrees, 0f));
            cam.fieldOfView = fieldOfView;
        }

        // ------------------------------------------------------------------ flow

        private void Update()
        {
            float now = Time.unscaledTime - openedAt;
            float dt = Time.unscaledDeltaTime;

            // Clicked buttons would stay selected and also react to Enter; keep nothing selected.
            EventSystem events = EventSystem.current;
            // Preserve keyboard navigation focus between frames.

            if (state == State.Open)
            {
                // Fade in from deep purple, then slide the menu items in one after another.
                fade.color = WithAlpha(MarsTrainer.UI.UiTheme.Ink, 1f - Smooth(now / 1.2f));
                for (int i = 0; i < reveals.Count; i++)
                {
                    float t = Smooth(Mathf.Clamp01((now - reveals[i].delay) / 0.7f));
                    reveals[i].group.alpha = t;
                    reveals[i].rect.anchoredPosition = reveals[i].home + new Vector2(-40f * (1f - t), 0f);
                }
                if (hint != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(now * 2.4f);
                    hint.color = WithAlpha(MarsTrainer.UI.UiTheme.Text, Mathf.Lerp(0.35f, 0.75f, pulse) * Smooth((now - 1.4f) / 0.8f));
                }

#if ENABLE_LEGACY_INPUT_MANAGER
                if (helpOpen)
                {
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)) SetHelp(false);
                }
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    StartMission();
                }
#endif
            }

            if (helpGroup != null)
            {
                helpBlend = Mathf.MoveTowards(helpBlend, helpOpen ? 1f : 0f, dt * 5f);
                float t = Smooth(helpBlend);
                helpGroup.alpha = t;
                helpGroup.blocksRaycasts = helpOpen;
                helpGroup.interactable = helpOpen;
                helpCard.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, t);
                helpGroup.gameObject.SetActive(helpBlend > 0f);
            }
        }

        public void StartMission()
        {
            if (state != State.Open) return;
            state = State.Closing;
            StartCoroutine(CloseRoutine());
        }

        private IEnumerator CloseRoutine()
        {
            Color purple = MarsTrainer.UI.UiTheme.Ink;
            float start = fade.color.a;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.45f)
            {
                fade.color = WithAlpha(purple, Mathf.Lerp(start, 1f, Smooth(t)));
                yield return null;
            }
            fade.color = WithAlpha(purple, 1f);

            // Give the scene back exactly as it started; the astronaut takes over from here.
            state = State.Closed;
            cam.transform.SetParent(camParent, true);
            cam.transform.SetPositionAndRotation(camPosition, camRotation);
            cam.fieldOfView = camFov;
            if (flyCamera != null) flyCamera.enabled = flyCameraWasEnabled;
            // The terrain data panel stays hidden in play (F3 shows it) so the corner is free for the mission card.
            if (dataOverlay != null) dataOverlay.visible = false;
            if (clockHud != null) clockHud.visible = clockHudWasVisible;
            Destroy(menuCanvas);
            IsOpen = false;

            yield return null; // the astronaut mounts the camera on its next Update
            yield return null;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f)
            {
                fade.color = WithAlpha(purple, 1f - Smooth(t));
                yield return null;
            }
            Destroy(gameObject);
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetHelp(bool open)
        {
            helpOpen = open;
            if (open) helpGroup.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ UI construction

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private GameObject NewCanvas(string name, int order)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private void BuildFade()
        {
            GameObject canvas = NewCanvas("Fade", 200);
            fade = NewRect("Fade", canvas.transform).gameObject.AddComponent<Image>();
            Stretch(fade.rectTransform);
            fade.raycastTarget = false;
        }

        private void BuildMenu()
        {
            menuCanvas = NewCanvas("Home Screen UI", 100);
            Transform root = menuCanvas.transform;
            Color pink = MarsTrainer.UI.UiTheme.Text;
            Color orange = MarsTrainer.UI.UiTheme.Accent;
            Color purple = MarsTrainer.UI.UiTheme.Ink;

            // Soft deep-purple shade behind the menu so the text reads over any terrain.
            RawImage shade = NewRect("Shade", root).gameObject.AddComponent<RawImage>();
            shade.texture = ShadeTexture();
            shade.raycastTarget = false;
            RectTransform shadeRect = shade.rectTransform;
            shadeRect.anchorMin = new Vector2(0f, 0f);
            shadeRect.anchorMax = new Vector2(0f, 1f);
            shadeRect.pivot = new Vector2(0f, 0.5f);
            shadeRect.sizeDelta = new Vector2(1300f, 0f);
            shadeRect.anchoredPosition = Vector2.zero;

            // Left column, laid out top-down and centred vertically.
            RectTransform column = NewRect("Menu", root);
            column.anchorMin = column.anchorMax = new Vector2(0f, 0.5f);
            column.pivot = new Vector2(0f, 1f);
            column.sizeDelta = new Vector2(900f, 820f);
            column.anchoredPosition = new Vector2(150f, 410f);

            float y = 0f;
            int index = 0;

            TMP_Text eyebrowText = NewText(column, eyebrow, 26, orange, FontStyle.Bold, TextAnchor.UpperLeft);
            Place(eyebrowText.rectTransform, ref y, 900f, 34f, 22f, index++);

            // One word on one line: MARS in white, IVITY in orange.
            TMP_Text title = NewText(column, titleWhite + "<color=#" + ColorUtility.ToHtmlStringRGB(orange) + ">" + titleOrange + "</color>",
                                 150, Color.white, FontStyle.Bold, TextAnchor.UpperLeft);
            title.enableWordWrapping = false;
            Place(title.rectTransform, ref y, 1100f, 170f, 26f, index++);

            Image rule = NewRect("Rule", column).gameObject.AddComponent<Image>();
            rule.sprite = roundFill;
            rule.type = Image.Type.Sliced;
            rule.pixelsPerUnitMultiplier = 8f;
            rule.color = orange;
            rule.raycastTarget = false;
            Place(rule.rectTransform, ref y, 120f, 8f, 30f, index++);

            TMP_Text taglineText = NewText(column, tagline, 32, WithAlpha(pink, 0.92f), FontStyle.Normal, TextAnchor.UpperLeft);
            taglineText.enableWordWrapping = true;
            taglineText.lineSpacing = 10f;
            Place(taglineText.rectTransform, ref y, 1100f, 50f, 56f, index++);

            RectTransform start = MakeButton(column, "Start mission", ButtonStyle.Primary, StartMission);
            Place(start, ref y, 460f, 92f, 18f, index++);
            RectTransform how = MakeButton(column, "How to play", ButtonStyle.Secondary, () => SetHelp(true));
            Place(how, ref y, 460f, 76f, 10f, index++);
            RectTransform quit = MakeButton(column, "Quit", ButtonStyle.Ghost, Quit);
            Place(quit, ref y, 460f, 60f, 0f, index++);

            // Footer: data credit bottom-left, start hint bottom-right.
            TMP_Text creditText = NewText(root, credit, 20, WithAlpha(pink, 0.6f), FontStyle.Normal, TextAnchor.LowerLeft);
            RectTransform creditRect = creditText.rectTransform;
            creditRect.anchorMin = creditRect.anchorMax = creditRect.pivot = Vector2.zero;
            creditRect.sizeDelta = new Vector2(1400f, 30f);
            AddReveal(creditRect, new Vector2(150f, 52f), 1.1f);

            hint = NewText(root, "Press Enter to start", 22, WithAlpha(pink, 0f), FontStyle.Bold, TextAnchor.LowerRight);
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1f, 0f);
            hintRect.sizeDelta = new Vector2(500f, 30f);
            hintRect.anchoredPosition = new Vector2(-80f, 52f);

            BuildHelp(root);
        }

        private void BuildHelp(Transform root)
        {
            Color pink = MarsTrainer.UI.UiTheme.Text;
            Color purple = MarsTrainer.UI.UiTheme.Ink;

            RectTransform overlay = NewRect("How To Play", root);
            Stretch(overlay);
            helpGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            // The backdrop is a sibling of the card, so clicks on the card do not close it.
            RectTransform dimRect = NewRect("Backdrop", overlay);
            Stretch(dimRect);
            Image dim = dimRect.gameObject.AddComponent<Image>();
            dim.color = WithAlpha(purple, 0.55f);
            Button dimButton = dimRect.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.navigation = new Navigation { mode = Navigation.Mode.None };
            dimButton.onClick.AddListener(() => SetHelp(false));

            helpCard = NewRect("Card", overlay);
            helpCard.sizeDelta = new Vector2(860f, 960f);
            Image card = helpCard.gameObject.AddComponent<Image>();
            card.sprite = roundFill;
            card.type = Image.Type.Sliced;
            card.color = WithAlpha(Color.Lerp(purple, MarsTrainer.UI.UiTheme.Hover, 0.12f), 0.97f);

            TMP_Text heading = NewText(helpCard, "How to play", 40, MarsTrainer.UI.UiTheme.Accent, FontStyle.Bold, TextAnchor.UpperLeft);
            TopLeft(heading.rectTransform, new Vector2(64f, -56f), new Vector2(700f, 50f));

            string[,] rows =
            {
                { "W A S D", "Walk around" },
                { "Mouse", "Look around" },
                { "Shift", "Run" },
                { "Space", "Jump (gravity is low on Mars!)" },
                { "1 – 9, 0", "Pick a tool from the hotbar" },
                { "Left click", "Use the tool" },
                { "R", "The tool's extra action" },
                { "E", "Pick up a tool / board the rover" },
                { "F / G", "Eat / drink" },
                { "Tab", "Outpost console: power, tasks" },
                { "H", "Help: how to do your daily tasks" },
                { "P", "Pause or resume the mission" },
                { "Esc", "Menu: pause, controls, sound" },
                { "F5 / F9", "Save / load mission" },
                { "F3", "Terrain data" },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                float rowY = -136f - i * 46f;
                RectTransform chip = NewRect("Key", helpCard);
                TopLeft(chip, new Vector2(64f, rowY), new Vector2(170f, 38f));
                Image chipImage = chip.gameObject.AddComponent<Image>();
                chipImage.sprite = roundRing;
                chipImage.type = Image.Type.Sliced;
                chipImage.pixelsPerUnitMultiplier = 1.6f;
                chipImage.color = WithAlpha(pink, 0.55f);
                TMP_Text key = NewText(chip, rows[i, 0], 22, pink, FontStyle.Bold, TextAnchor.MiddleCenter);
                Stretch(key.rectTransform);

                TMP_Text action = NewText(helpCard, rows[i, 1], 27, WithAlpha(pink, 0.92f), FontStyle.Normal, TextAnchor.MiddleLeft);
                TopLeft(action.rectTransform, new Vector2(262f, rowY), new Vector2(540f, 38f));
            }

            RectTransform close = MakeButton(helpCard, "Got it", ButtonStyle.Primary, () => SetHelp(false));
            close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 0f);
            close.sizeDelta = new Vector2(240f, 72f);
            close.anchoredPosition = new Vector2(-56f, 48f);

            helpGroup.alpha = 0f;
            overlay.gameObject.SetActive(false);
        }

        private enum ButtonStyle { Primary, Secondary, Ghost }

        private RectTransform MakeButton(Transform parent, string label, ButtonStyle style, UnityAction onClick)
        {
            Color pink = MarsTrainer.UI.UiTheme.Text;
            RectTransform rect = NewRect(label, parent);
            Image background = rect.gameObject.AddComponent<Image>();
            background.sprite = style == ButtonStyle.Secondary ? roundRing : roundFill;
            background.type = Image.Type.Sliced;

            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            TMP_Text text = NewText(rect, label, style == ButtonStyle.Ghost ? 26 : 30,
                                Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);

            MarsMenuButtonFx fx = rect.gameObject.AddComponent<MarsMenuButtonFx>();
            fx.background = background;
            fx.label = text;
            switch (style)
            {
                case ButtonStyle.Primary:
                    fx.backgroundNormal = MarsTrainer.UI.UiTheme.Accent;
                    fx.backgroundHover = Color.Lerp(MarsTrainer.UI.UiTheme.Accent, pink, 0.4f);
                    fx.labelNormal = fx.labelHover = MarsTrainer.UI.UiTheme.Ink;
                    break;
                case ButtonStyle.Secondary:
                    fx.backgroundNormal = WithAlpha(pink, 0.6f);
                    fx.backgroundHover = pink;
                    fx.labelNormal = WithAlpha(pink, 0.9f);
                    fx.labelHover = pink;
                    Image fill = NewRect("Fill", rect).gameObject.AddComponent<Image>();
                    fill.transform.SetAsFirstSibling();
                    Stretch(fill.rectTransform);
                    fill.sprite = roundFill;
                    fill.type = Image.Type.Sliced;
                    fill.raycastTarget = false;
                    fx.fill = fill;
                    fx.fillNormal = WithAlpha(pink, 0f);
                    fx.fillHover = WithAlpha(pink, 0.14f);
                    break;
                default:
                    fx.backgroundNormal = fx.backgroundHover = WithAlpha(pink, 0f);
                    fx.labelNormal = WithAlpha(pink, 0.6f);
                    fx.labelHover = pink;
                    break;
            }
            fx.Apply(0f);
            return rect;
        }

        // ------------------------------------------------------------------ helpers

        private void Place(RectTransform rect, ref float y, float width, float height, float gapAfter, int index)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            AddReveal(rect, new Vector2(0f, -y), 0.35f + index * 0.08f);
            y += height + gapAfter;
        }

        private void AddReveal(RectTransform rect, Vector2 home, float delay)
        {
            rect.anchoredPosition = home;
            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            reveals.Add(new Reveal { rect = rect, group = group, home = home, delay = delay });
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private TMP_Text NewText(Transform parent, string content, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            TextMeshProUGUI text = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = style == FontStyle.Bold || size >= 40 ? MarsTrainer.UI.UiTypeface.Strong : font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.alignment = anchor == TextAnchor.UpperLeft ? TextAlignmentOptions.TopLeft
                : anchor == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center
                : anchor == TextAnchor.MiddleLeft ? TextAlignmentOptions.MidlineLeft
                : anchor == TextAnchor.LowerLeft ? TextAlignmentOptions.BottomLeft
                : anchor == TextAnchor.LowerRight ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.Center;
            text.richText = true;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void TopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
        }

        /// <summary>White rounded square (or ring) with soft edges, 9-sliced so it fits any size.</summary>
        private static Sprite RoundedSprite(int size, float radius, float ring)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;
            float half = size * 0.5f;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - distance);
                    if (ring > 0f) alpha *= Mathf.Clamp01(distance + ring + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = Mathf.Ceil(radius) + 1f;
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>Horizontal deep-purple gradient, solid on the left and clear on the right.</summary>
        private static Texture2D ShadeTexture()
        {
            const int Width = 256;
            Texture2D texture = new Texture2D(Width, 1, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;
            Color32 purple = MarsTrainer.UI.UiTheme.Ink;
            for (int x = 0; x < Width; x++)
            {
                float t = x / (Width - 1f);
                float alpha = 0.82f * Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, t), 1.4f);
                purple.a = (byte)Mathf.RoundToInt(alpha * 255f);
                texture.SetPixel(x, 0, purple);
            }
            texture.Apply(false, true);
            return texture;
        }
    }
}
