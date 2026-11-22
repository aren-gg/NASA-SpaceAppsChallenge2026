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
    /// Title screen shown when the Mars scene starts. The camera slowly circles close over the
    /// outpost (framed so no empty terrain or sky fills the view) with a depth-of-field blur
    /// on everything behind it, while the title and three buttons (Start mission, How to play,
    /// Credits) sit in the top-left, clear of the base. The title settles from wide tracking.
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
        [Tooltip("Marsivity: Mars + immersivity. The first part is drawn in white, the second in orange.")]
        public string titleWhite = "MARS";
        public string titleOrange = "IVITY";

        [Header("Background camera (real metres)")]
        public bool showOnStart = true;
        [Tooltip("What the camera circles. Empty: the object named \"Mars Outpost\" if there is one, " +
                 "otherwise a point 25 m ahead of the scene's starting view.")]
        public Transform orbitTarget;
        // Close and high enough that the view looks down onto the base: the horizon (and the
        // empty terrain and sky beyond it) stays just above the top of the screen.
        public float orbitRadiusMeters = 24f;
        public float orbitHeightMeters = 11f;
        public float orbitDegreesPerSecond = 2.5f;
        [Tooltip("Slow push in and out, in metres, so the shot breathes.")]
        public float breatheMeters = 1.5f;
        [Tooltip("0 keeps the outpost centred behind the title.")]
        public float framingYawDegrees = 0f;
        [Range(20f, 90f)] public float fieldOfView = 38f;

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
        /// <summary>A modal card (How to play, Credits) with its backdrop and fade.</summary>
        private sealed class Overlay
        {
            public CanvasGroup group;
            public RectTransform card;
            public bool open;
            public float blend;
            public RectTransform close;
        }
        private Overlay help, credits;
        private CanvasGroup menuGroup;
        private TMP_Text title;
        private float openedAt;

        // Depth-of-field blur and a soft vignette while the title screen owns the camera.
        private GameObject blurVolume;
        private Component cameraData;
        private bool cameraPostWasOn;

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
            EnableBlur(true);
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
            float radius = orbitRadiusMeters + breatheMeters * Mathf.Sin(Time.unscaledTime * 0.21f);
            Vector3 position = target + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Units(radius)
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
                    reveals[i].rect.anchoredPosition = reveals[i].home + new Vector2(0f, -28f * (1f - t));
                }
                // The title settles from wide tracking.
                if (title != null) title.characterSpacing = Mathf.Lerp(30f, 8f, Smooth((now - 0.3f) / 1.6f));

#if ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (help.open) help.open = false;
                    if (credits.open) credits.open = false;
                }
#endif
            }

            Blend(help, dt);
            Blend(credits, dt);
            // The menu steps back while a card is open, so nothing shows through it.
            if (menuGroup != null && help.group != null && credits.group != null)
                menuGroup.alpha = 1f - Smooth(Mathf.Max(help.blend, credits.blend));
        }

        private static void Blend(Overlay o, float dt)
        {
            if (o == null || o.group == null) return;   // the menu is destroyed once the mission starts
            o.blend = Mathf.MoveTowards(o.blend, o.open ? 1f : 0f, dt * 5f);
            float t = Smooth(o.blend);
            o.group.alpha = t;
            o.group.blocksRaycasts = o.open;
            o.group.interactable = o.open;
            o.card.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, t);
            o.group.gameObject.SetActive(o.blend > 0f);
        }

        private void Show(Overlay o)
        {
            o.open = true;
            o.group.gameObject.SetActive(true);
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
            EnableBlur(false);
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

        /// <summary>
        /// A global URP volume while the title screen is up: Gaussian depth of field that keeps
        /// the outpost sharp and softens the terrain behind it, plus a light bloom.
        /// Removed (and the camera's post-processing flag restored) when the mission starts.
        /// </summary>
        private void EnableBlur(bool on)
        {
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (on)
            {
                if (data == null) data = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                cameraData = data;
                cameraPostWasOn = data.renderPostProcessing;
                data.renderPostProcessing = true;

                var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                var dof = profile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
                dof.mode.Override(UnityEngine.Rendering.Universal.DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(Units(orbitRadiusMeters + 10f));
                dof.gaussianEnd.Override(Units(orbitRadiusMeters + 70f));
                dof.gaussianMaxRadius.Override(1.5f);
                dof.highQualitySampling.Override(true);
                var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
                bloom.intensity.Override(0.35f);
                bloom.threshold.Override(1.0f);

                blurVolume = new GameObject("Home Screen Blur");
                blurVolume.transform.SetParent(transform, false);
                var volume = blurVolume.AddComponent<UnityEngine.Rendering.Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;
                volume.sharedProfile = profile;
            }
            else
            {
                if (blurVolume != null) Destroy(blurVolume);
                if (data != null && ReferenceEquals(data, cameraData)) data.renderPostProcessing = cameraPostWasOn;
            }
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
            Color orange = MarsTrainer.UI.UiTheme.Accent;

            // A light, even shade so the title and buttons read over the base.
            Image shade = NewRect("Shade", root).gameObject.AddComponent<Image>();
            shade.color = WithAlpha(MarsTrainer.UI.UiTheme.Ink, 0.35f);
            shade.raycastTarget = false;
            Stretch(shade.rectTransform);

            // Top-left column, clear of the base: the title and three buttons, nothing else.
            RectTransform column = NewRect("Menu", root);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0f, 1f);
            column.sizeDelta = new Vector2(900f, 600f);
            column.anchoredPosition = new Vector2(120f, -150f);
            menuGroup = column.gameObject.AddComponent<CanvasGroup>();

            float y = 0f;
            int index = 0;

            // One word on one line: MARS in white, IVITY in orange.
            title = NewText(column, titleWhite + "<color=#" + ColorUtility.ToHtmlStringRGB(orange) + ">" + titleOrange + "</color>",
                            118, Color.white, FontStyle.Bold, TextAnchor.UpperLeft);
            title.enableWordWrapping = false;
            title.outlineWidth = 0.08f;
            title.outlineColor = new Color32(21, 24, 28, 140);
            Place(title.rectTransform, ref y, 900f, 130f, 50f, index++);

            RectTransform start = MakeButton(column, "Start mission", ButtonStyle.Primary, StartMission);
            Place(start, ref y, 340f, 62f, 14f, index++);
            RectTransform how = MakeButton(column, "How to play", ButtonStyle.Secondary, () => Show(help));
            Place(how, ref y, 340f, 62f, 14f, index++);
            RectTransform credit = MakeButton(column, "Credits", ButtonStyle.Secondary, () => Show(credits));
            Place(credit, ref y, 340f, 62f, 0f, index++);

            BuildHelp(root);
            BuildCredits(root);
        }

        /// <summary>A modal card centred on screen with a dimmed backdrop that closes it.</summary>
        private Overlay MakeOverlay(Transform root, string name, string heading, Vector2 size)
        {
            Color purple = MarsTrainer.UI.UiTheme.Ink;
            var o = new Overlay();
            RectTransform overlay = NewRect(name, root);
            Stretch(overlay);
            o.group = overlay.gameObject.AddComponent<CanvasGroup>();
            // The backdrop is a sibling of the card, so clicks on the card do not close it.
            RectTransform dimRect = NewRect("Backdrop", overlay);
            Stretch(dimRect);
            Image dim = dimRect.gameObject.AddComponent<Image>();
            dim.color = WithAlpha(purple, 0.72f);
            Button dimButton = dimRect.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.navigation = new Navigation { mode = Navigation.Mode.None };
            dimButton.onClick.AddListener(() => o.open = false);

            o.card = NewRect("Card", overlay);
            o.card.sizeDelta = size;
            Image card = o.card.gameObject.AddComponent<Image>();
            card.sprite = roundFill;
            card.type = Image.Type.Sliced;
            card.color = WithAlpha(Color.Lerp(purple, MarsTrainer.UI.UiTheme.Hover, 0.12f), 1f);

            TMP_Text headingText = NewText(o.card, heading, 40, MarsTrainer.UI.UiTheme.Accent, FontStyle.Bold, TextAnchor.UpperLeft);
            TopLeft(headingText.rectTransform, new Vector2(64f, -56f), new Vector2(700f, 50f));

            RectTransform close = MakeButton(o.card, "Got it", ButtonStyle.Primary, () => o.open = false);
            o.close = close;
            close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 0f);
            close.sizeDelta = new Vector2(240f, 72f);
            close.anchoredPosition = new Vector2(-56f, 48f);

            o.group.alpha = 0f;
            overlay.gameObject.SetActive(false);
            return o;
        }

        private void BuildHelp(Transform root)
        {
            Color pink = MarsTrainer.UI.UiTheme.Text;
            help = MakeOverlay(root, "How To Play", "How to play", new Vector2(860f, 960f));
            RectTransform helpCard = help.card;

            string[,] rows =
            {
                { "W A S D", "Walk around" },
                { "Mouse", "Look around" },
                { "Shift", "Run" },
                { "Space", "Jump (gravity is low on Mars!)" },
                { "1 – 3", "Shovel, sprayer, work lamp" },
                { "Left click", "Use the tool" },
                { "R", "Shovel: choose the seed" },
                { "E", "Pick up a tool, shop, pick, eat, drink, sleep" },
                { "P", "Pause or resume" },
                { "Esc", "Menu: pause, controls, sound" },
                { "F5 / F9", "Save / load" },
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

        }

        /// <summary>
        /// Credits: a scrolling list built from <see cref="GameCredits"/>, grouped into Mars data,
        /// standards and research, and software and fonts. Each entry names the work, who made and
        /// publishes it, and what the game uses it for; clicking an entry opens its source page.
        /// The NASA non-endorsement note sits at the end.
        /// </summary>
        private void BuildCredits(Transform root)
        {
            Color pink = MarsTrainer.UI.UiTheme.Text;
            Color orange = MarsTrainer.UI.UiTheme.Accent;
            credits = MakeOverlay(root, "Credits", "Credits", new Vector2(1120f, 920f));

            // Scrolling viewport between the heading and the Got it button.
            RectTransform viewport = NewRect("Viewport", credits.card);
            viewport.anchorMin = new Vector2(0f, 0f); viewport.anchorMax = new Vector2(1f, 1f);
            viewport.offsetMin = new Vector2(56f, 140f); viewport.offsetMax = new Vector2(-40f, -122f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);                   // catches the scroll wheel between rows

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VerticalLayoutGroup list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 6f; list.padding = new RectOffset(8, 24, 0, 8);
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true; list.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            foreach (GameCredits.Section section in GameCredits.Sections)
            {
                TMP_Text head = FlowText(content, section.Name, 22, orange, true);
                head.fontStyle = FontStyles.UpperCase;
                head.characterSpacing = 4f;
                head.margin = new Vector4(0f, 18f, 0f, 4f);
                foreach (GameCredits.Entry entry in section.Entries) CreditRow(content, entry);
            }
            TMP_Text madeFor = FlowText(content, GameCredits.MadeFor, 21, pink, true);
            madeFor.margin = new Vector4(0f, 24f, 0f, 0f);
            FlowText(content, GameCredits.Disclaimer, 18, WithAlpha(pink, 0.7f), false);
        }

        /// <summary>One credit: title, maker and publisher, what the game uses it for. Click opens the source.</summary>
        private void CreditRow(Transform parent, GameCredits.Entry entry)
        {
            Color pink = MarsTrainer.UI.UiTheme.Text;
            RectTransform row = NewRect(entry.Title, parent);
            Image back = row.gameObject.AddComponent<Image>();
            back.sprite = roundFill; back.type = Image.Type.Sliced;
            VerticalLayoutGroup stack = row.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(16, 16, 10, 12); stack.spacing = 2f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true; stack.childForceExpandHeight = false;

            TMP_Text title = FlowText(row, entry.Title, 24, pink, true);
            FlowText(row, entry.Credit, 19, WithAlpha(pink, 0.78f), false);
            FlowText(row, "Used for: " + entry.UsedFor, 19, WithAlpha(MarsTrainer.UI.UiTheme.Accent, 0.9f), false);

            Button button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = back;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = WithAlpha(pink, 0.04f);
            colors.highlightedColor = colors.selectedColor = WithAlpha(pink, 0.12f);
            colors.pressedColor = WithAlpha(MarsTrainer.UI.UiTheme.Accent, 0.25f);
            colors.colorMultiplier = 1f; colors.fadeDuration = 0.08f;
            button.colors = colors;
            back.color = Color.white;
            string url = entry.Url;
            if (!string.IsNullOrEmpty(url))
            {
                button.onClick.AddListener(() => Application.OpenURL(url));
                title.text = entry.Title + "  <size=70%><alpha=#99>open source page</size>";
            }
        }

        /// <summary>Wrapping text sized by its layout group (for scrolling lists).</summary>
        private TMP_Text FlowText(Transform parent, string content, int size, Color color, bool strong)
        {
            TMP_Text text = NewText(parent, content, size, color, strong ? FontStyle.Bold : FontStyle.Normal, TextAnchor.UpperLeft);
            text.enableWordWrapping = true;
            text.lineSpacing = 4f;
            return text;
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

            TMP_Text text = NewText(rect, label, style == ButtonStyle.Ghost ? 22 : 24,
                                Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.margin = new Vector4(24f, 0f, 24f, 0f);
            MarsTrainer.UI.UiStyle.Caps(text, 3f);

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
                    fx.backgroundNormal = MarsTrainer.UI.UiTheme.Edge;
                    fx.backgroundHover = MarsTrainer.UI.UiTheme.Info;
                    fx.labelNormal = WithAlpha(pink, 0.9f);
                    fx.labelHover = pink;
                    Image fill = NewRect("Fill", rect).gameObject.AddComponent<Image>();
                    fill.transform.SetAsFirstSibling();
                    Stretch(fill.rectTransform);
                    fill.sprite = roundFill;
                    fill.type = Image.Type.Sliced;
                    fill.raycastTarget = false;
                    fx.fill = fill;
                    fx.fillNormal = WithAlpha(MarsTrainer.UI.UiTheme.Panel, 0.75f);
                    fx.fillHover = WithAlpha(MarsTrainer.UI.UiTheme.Panel, 0.9f);
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
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
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
                : anchor == TextAnchor.UpperCenter ? TextAlignmentOptions.Top
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

    }
}
