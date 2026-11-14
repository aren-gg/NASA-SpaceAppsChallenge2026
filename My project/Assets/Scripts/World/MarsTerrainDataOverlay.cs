using UnityEngine;

namespace MarsSurvival.World
{
    /// <summary>
    /// On-screen statement of the terrain's data use: source product, resolution,
    /// bytes downloaded at preparation, bytes bundled, live memory, scale, and the
    /// real altitude and coordinates under the viewer. Press F3 to show or hide.
    /// A development panel; the player HUD shows altitude through its own UI.
    /// </summary>
    [RequireComponent(typeof(MarsTerrain))]
    public sealed class MarsTerrainDataOverlay : MonoBehaviour
    {
        public bool visible = true;
        [Min(0.1f)] public float refreshSeconds = 0.5f;

        private MarsTerrain terrain;
        private string text = string.Empty;
        private float nextRefresh;
        private GUIStyle style;

        private void Awake()
        {
            terrain = GetComponent<MarsTerrain>();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F3)) visible = !visible;
#endif
            if (!visible || !terrain.IsReady || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshSeconds;

            text = terrain.BuildDataReport();
            Transform viewer = terrain.viewer;
            if (viewer != null)
            {
                Vector3 position = viewer.position;
                Vector2 latLon = terrain.GetLatLon(position);
                float ground = terrain.GetGroundElevationMeters(position);
                float heightAboveGround = terrain.UnitsToRealMeters(position.y - terrain.GetGroundY(position)) /
                                          terrain.Heightmap.VerticalExaggeration;
                text += "\nViewer      lat " + latLon.x.ToString("F4") + ", lon " + latLon.y.ToString("F4") + " E" +
                        (terrain.IsInsideTile(position) ? "" : " (outside tile)") +
                        "\n            ground " + ground.ToString("F0") + " m, viewer " +
                        heightAboveGround.ToString("F0") + " m above ground (real scale), slope " +
                        terrain.GetSlopeDegrees(position).ToString("F1") + " deg";
            }
        }

        private void OnGUI()
        {
            if (!visible || text.Length == 0) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.UpperLeft;
                style.fontSize = 12;
                style.wordWrap = false;
                style.padding = new RectOffset(8, 8, 6, 6);
                style.normal.textColor = MarsPalette.PalePink;
            }
            Vector2 size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10f, 10f, size.x, size.y), text, style);
        }
    }
}
