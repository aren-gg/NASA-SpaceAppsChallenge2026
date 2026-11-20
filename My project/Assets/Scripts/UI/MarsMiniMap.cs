using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Gameplay;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.Tools;
using MarsSurvival.Vehicles;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarsSurvival.UI
{
    /// <summary>
    /// Top-right minimap of the outpost: the rooms with their names, the corridors, the solar
    /// array, the rover, today's waypoint and the astronaut (an arrow, always in the middle).
    /// Clicking it, or pressing M, opens a large map of everything within render distance,
    /// with the science sites; click or press M / Escape to close it.
    /// </summary>
    public sealed class MarsMiniMap : MonoBehaviour
    {
        public const float MiniWidth = 400f, MiniHeight = 200f;
        private const float MiniMeters = 100f;  // metres shown across the minimap: the rooms around you, with names
        // The base pad is about 56 m across; the large map opens about 190 m across so the base fills ~30% of it.
        private const float OpenMeters = 95f;
        private const float BigSize = 820f;

        /// <summary>True while the large map covers the screen.</summary>
        public bool Expanded { get; private set; }

        private sealed class Layer
        {
            public RectTransform frame, content, player, rover, waypoint;
            public float scale;
            public readonly List<RectTransform> sites = new List<RectTransform>();
            public readonly List<TMP_Text> siteLabels = new List<TMP_Text>();
            public readonly List<GameObject> roomLabels = new List<GameObject>();
            public GameObject outpostLabel;
            public bool built;
            public float drawnScale;
            public MissionState drawnState;
        }

        private Layer mini, big;
        private RectTransform bigRoot;
        private TMP_Text bigTitle;
        private TMP_FontAsset font;
        private Color ink, paper, accent, muted;
        private MarsOutpost outpost;
        private MarsAstronautPlayer player;
        private MarsRover rover;
        private float bigMeters = OpenMeters, renderMeters = 600f;

        public void Build(RectTransform hud, RectTransform overlay, TMP_FontAsset typeface, Color inkColor, Color paperColor, Color accentColor, Color mutedColor, float alpha)
        {
            font = typeface; ink = inkColor; paper = paperColor; accent = accentColor; muted = mutedColor;

            // Minimap plate, top right under the key buttons.
            RectTransform plate = Rect("Minimap", hud);
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(1f, 1f);
            plate.anchoredPosition = new Vector2(-16f, -58f);
            plate.sizeDelta = new Vector2(MiniWidth, MiniHeight);
            Image back = MarsMissionHud.HudPlate(plate.gameObject, alpha);   // the shared HUD plate
            Button open = plate.gameObject.AddComponent<Button>();
            open.targetGraphic = back;
            open.navigation = new Navigation { mode = Navigation.Mode.None };
            open.onClick.AddListener(() => SetExpanded(true));
            plate.gameObject.AddComponent<RectMask2D>();
            mini = NewLayer(plate, MiniWidth / MiniMeters);
            TMP_Text hint = Text(plate, "M  expand", 13, muted, TextAlignmentOptions.BottomRight);
            Stretch(hint.rectTransform);
            hint.margin = new Vector4(0, 0, 10, 6);

            // Large map, centred, hidden until opened.
            bigRoot = Rect("Map", overlay);
            Stretch(bigRoot);
            Image shade = bigRoot.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.55f);
            Button close = bigRoot.gameObject.AddComponent<Button>();
            close.targetGraphic = shade;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(() => SetExpanded(false));
            RectTransform bigPlate = Rect("Map plate", bigRoot);
            bigPlate.anchorMin = bigPlate.anchorMax = new Vector2(0.5f, 0.5f);
            bigPlate.sizeDelta = new Vector2(BigSize, BigSize);
            bigPlate.anchoredPosition = new Vector2(0f, -30f);
            bigPlate.gameObject.AddComponent<Image>().color = new Color(ink.r * 0.8f, ink.g * 0.8f, ink.b * 0.8f, 1f);
            bigPlate.gameObject.AddComponent<RectMask2D>();
            big = NewLayer(bigPlate, 1f);
            bigTitle = Text(bigRoot, "", 17, accent, TextAlignmentOptions.Center);
            bigTitle.rectTransform.anchorMin = bigTitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bigTitle.rectTransform.sizeDelta = new Vector2(BigSize, 40f);
            bigTitle.rectTransform.anchoredPosition = new Vector2(0f, -30f + BigSize * 0.5f - 24f);
            bigRoot.gameObject.SetActive(false);
        }

        public void Toggle() { SetExpanded(!Expanded); }

        public void SetExpanded(bool value)
        {
            if (bigRoot == null) return;
            Expanded = value;
            bigRoot.gameObject.SetActive(value);
            if (value) { bigRoot.SetAsLastSibling(); bigMeters = Mathf.Min(OpenMeters, renderMeters); }
            var mission = MarsMissionController.Instance;
            if (mission != null)
            {
                mission.MapOpen = value;
                if (value) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                else mission.SetPanelOpen(mission.PanelOpen); // restores the cursor lock for walking
            }
        }

        private void LateUpdate()
        {
            if (mini == null) return;
            if (outpost == null) outpost = FindObjectOfType<MarsOutpost>();
            if (player == null) player = FindObjectOfType<MarsAstronautPlayer>();
            if (rover == null) rover = FindObjectOfType<MarsRover>();
            if (outpost == null || !outpost.IsPlaced || outpost.Rooms == null || player == null) return;
            if (!mini.built || mini.drawnState != State) DrawOutpost(mini, true);
            Camera cam = player.ViewCamera != null ? player.ViewCamera : Camera.main;
            renderMeters = Mathf.Clamp(cam != null ? cam.farClipPlane : 600f, 100f, 2500f);
#if ENABLE_LEGACY_INPUT_MANAGER
            // Scroll zooms the large map, from 60 m out to the whole render distance.
            if (Expanded && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) bigMeters *= Mathf.Pow(0.85f, Input.mouseScrollDelta.y);
#endif
            bigMeters = Mathf.Clamp(bigMeters, 60f, renderMeters);
            big.scale = BigSize * 0.5f / bigMeters;
            if (!big.built || big.drawnState != State || Mathf.Abs(big.drawnScale - big.scale) > 0.0001f) DrawOutpost(big, false);
            UpdateLayer(mini);
            if (Expanded)
            {
                UpdateLayer(big);
                bigTitle.text = Mathf.RoundToInt(bigMeters) + " m around you     scroll to zoom (up to " + Mathf.RoundToInt(renderMeters) + " m)     M to close";
            }
        }

        // ------------------------------------------------------------------ drawing

        private Layer NewLayer(RectTransform frame, float scale)
        {
            var layer = new Layer { frame = frame, scale = scale };
            layer.content = Rect("Content", frame);
            layer.content.anchorMin = layer.content.anchorMax = new Vector2(0.5f, 0.5f);
            layer.content.sizeDelta = Vector2.zero;
            return layer;
        }

        /// <summary>The static outpost plan, in outpost-local metres (x right, z up the map).</summary>
        private void DrawOutpost(Layer layer, bool small)
        {
            for (int i = layer.content.childCount - 1; i >= 0; i--) Destroy(layer.content.GetChild(i).gameObject);
            layer.sites.Clear(); layer.siteLabels.Clear(); layer.roomLabels.Clear();
            layer.built = true;
            layer.drawnScale = layer.scale;
            layer.drawnState = State;
            float s = layer.scale;
            Color room = Color.Lerp(ink, paper, 0.28f);
            Box(layer, "Pad", MarsOutpostBuilder.Pad, WithAlpha(Color.Lerp(ink, new Color(0.73f, 0.43f, 0.33f), 0.4f), 0.8f));
            // Corridors along the two axes.
            Box(layer, "Corridor", new FloorRect(-26f, -0.8f, 20.5f, 0.8f), Color.Lerp(ink, paper, 0.18f));
            Box(layer, "Corridor", new FloorRect(-0.8f, -18f, 0.8f, 16.75f), Color.Lerp(ink, paper, 0.18f));
            Box(layer, "Solar array", new FloorRect(6.8f, 6.5f, 17.4f, 12.7f), new Color(0.3f, 0.36f, 0.62f, 0.9f));
            Box(layer, "Airlock", MarsOutpostBuilder.Airlock, WithAlpha(accent, 0.6f));
            foreach (MarsOutpostBuilder.Room r in outpost.Rooms) Box(layer, r.name, r.rect, room);
            bool names = small || s >= 2.5f;
            int size = small ? 11 : Mathf.Clamp(Mathf.RoundToInt(s * 3f), 12, 18);
            if (names)
            {
                // The six modules along the main corridor sit close together: their names alternate
                // above and below the row so they do not overlap.
                var row = new List<MarsOutpostBuilder.Room>();
                foreach (MarsOutpostBuilder.Room r in outpost.Rooms)
                {
                    if (Mathf.Abs(r.rect.CentreZ) < 1f) row.Add(r);
                    else layer.roomLabels.Add(Label(layer, Short(r.name), r.rect.CentreX, r.rect.CentreZ, size, paper).gameObject);
                }
                row.Sort((a, b) => a.rect.CentreX.CompareTo(b.rect.CentreX));
                for (int i = 0; i < row.Count; i++)
                {
                    FloorRect r = row[i].rect;
                    float z = i % 2 == 0 ? r.z1 + 2.2f : r.z0 - 2.2f;
                    layer.roomLabels.Add(Label(layer, Short(row[i].name), r.CentreX, z, size, paper).gameObject);
                }
                layer.roomLabels.Add(Label(layer, "Solar array", 12.1f, 9.6f, size, paper).gameObject);
                layer.roomLabels.Add(Label(layer, "Airlock", 0f, 15f, size - 1, paper).gameObject);
            }
            else layer.outpostLabel = Label(layer, "Outpost", 0f, -24f, 14, paper).gameObject;

            layer.rover = Dot(layer, "Rover", new Color(0.62f, 0.62f, 0.68f), small ? 9f : 12f);
            layer.waypoint = Dot(layer, "Waypoint", accent, small ? 9f : 14f);
            layer.player = Arrow(layer, small ? 16f : 22f);
            if (!small)
            {
                MissionState state = State;
                if (state != null)
                    for (int i = 0; i < state.sites.Count; i++)
                    {
                        RectTransform dot = Dot(layer, "Site", new Color(0.91f, 0.73f, 0.74f), 10f);
                        TMP_Text label = Label(layer, state.sites[i].name, 0f, 0f, 14, paper);
                        label.alignment = TextAlignmentOptions.Left;
                        layer.sites.Add(dot);
                        layer.siteLabels.Add(label);
                    }
            }
        }

        private void UpdateLayer(Layer layer)
        {
            float s = layer.scale;
            Vector2 me = Local(player.transform.position);
            layer.content.anchoredPosition = -me * s;
            Place(layer.player, me, s);
            Vector3 forward = outpost.transform.InverseTransformDirection(player.transform.forward);
            layer.player.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            layer.player.SetAsLastSibling();

            layer.rover.gameObject.SetActive(rover != null);
            if (rover != null) Place(layer.rover, Local(rover.transform.position), s);

            Vector3? target = null;
            MarsExpedition x = MarsExpedition.Instance;
            if (x != null && x.Waypoints.Count > 0)
                foreach (MarsExpedition.Waypoint w in x.Waypoints) if (w.label != "Airlock") { target = w.position; break; }
            layer.waypoint.gameObject.SetActive(target.HasValue);
            if (target.HasValue)
            {
                // Off the edge of the map, the waypoint sticks to the border so you know which way to go.
                Vector2 rel = (Local(target.Value) - me) * s;
                Vector2 half = layer.frame.rect.size * 0.5f - new Vector2(10f, 10f);
                rel.x = Mathf.Clamp(rel.x, -half.x, half.x);
                rel.y = Mathf.Clamp(rel.y, -half.y, half.y);
                layer.waypoint.anchoredPosition = me * s + rel;
            }

            MissionState state = State;
            for (int i = 0; i < layer.sites.Count; i++)
            {
                bool shown = state != null && i < state.sites.Count;
                layer.sites[i].gameObject.SetActive(shown);
                layer.siteLabels[i].gameObject.SetActive(shown);
                if (!shown) continue;
                SiteState site = state.sites[i];
                Vector2 p = Local(new Vector3((float)site.x, (float)site.y, (float)site.z));
                Place(layer.sites[i], p, s);
                layer.sites[i].GetComponent<Image>().color = site.analyzed ? WithAlpha(muted, 0.7f) : new Color(0.91f, 0.73f, 0.74f);
                layer.siteLabels[i].rectTransform.anchoredPosition = p * s + new Vector2(72f, 0f);
                layer.siteLabels[i].gameObject.SetActive(s > 0.6f || i >= 5); // the 5 sites around the base crowd together when zoomed out
                layer.siteLabels[i].text = site.name + (site.analyzed ? "  (done)" : "");
                layer.siteLabels[i].color = site.analyzed ? muted : paper;
            }
        }

        private Vector2 Local(Vector3 world)
        {
            Vector3 p = outpost.transform.InverseTransformPoint(world);
            return new Vector2(p.x, p.z);
        }

        private static void Place(RectTransform rect, Vector2 local, float scale) { rect.anchoredPosition = local * scale; }

        private void Box(Layer layer, string name, FloorRect r, Color color)
        {
            RectTransform rect = Rect(name, layer.content);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(r.CentreX, r.CentreZ) * layer.scale;
            rect.sizeDelta = new Vector2(Mathf.Max(1f, r.Width * layer.scale), Mathf.Max(1f, r.Depth * layer.scale));
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private TMP_Text Label(Layer layer, string value, float x, float z, int size, Color color)
        {
            TMP_Text text = Text(layer.content, value, size, color, TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(130f, 20f);
            text.rectTransform.anchoredPosition = new Vector2(x, z) * layer.scale;
            return text;
        }

        private RectTransform Dot(Layer layer, string name, Color color, float size)
        {
            RectTransform rect = Rect(name, layer.content);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>The astronaut: an orange chevron pointing where they look.</summary>
        private RectTransform Arrow(Layer layer, float size)
        {
            RectTransform rect = Rect("You", layer.content);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            for (int side = -1; side <= 1; side += 2)
            {
                RectTransform wing = Rect("Wing", rect);
                wing.anchorMin = wing.anchorMax = new Vector2(0.5f, 0.5f);
                wing.sizeDelta = new Vector2(size * 0.28f, size);
                wing.anchoredPosition = new Vector2(side * size * 0.2f, 0f);
                wing.localRotation = Quaternion.Euler(0f, 0f, side * 28f);
                Image image = wing.gameObject.AddComponent<Image>();
                image.color = accent;
                image.raycastTarget = false;
            }
            RectTransform dot = Rect("Centre", rect);
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(size * 0.35f, size * 0.35f);
            dot.anchoredPosition = new Vector2(0f, -size * 0.3f);
            Image centre = dot.gameObject.AddComponent<Image>();
            centre.color = paper;
            centre.raycastTarget = false;
            return rect;
        }

        private static string Short(string room)
        {
            if (room.Contains("hub")) return "Hub & galley";
            if (room.Contains("lab")) return "Lab";
            if (room.Contains("shelter")) return "Shelter";
            if (room.Contains("quarters")) return "Quarters";
            if (room.Contains("Medical")) return "Medical";
            if (room.Contains("Energy")) return "Energy";
            return room;
        }

        private static MissionState State
        {
            get
            {
                var m = MarsMissionController.Instance;
                return m == null || m.Simulation == null ? null : m.Simulation.State;
            }
        }

        private TMP_Text Text(Transform parent, string value, float size, Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
