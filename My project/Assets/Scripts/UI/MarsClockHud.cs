using System.Text;
using MarsSurvival.TimeAndWeather;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.UI
{
    /// <summary>
    /// Info tab in the top-right corner: the sol, the local time of day, the season, the
    /// matching Earth date, and the current and next weather event with where each came from.
    /// </summary>
    public sealed class MarsClockHud : MonoBehaviour
    {
        public MarsDayNightCycle cycle;
        public MarsWeatherSchedule schedule;
        public bool visible = true;
        public int fontSize = 14;
        public float refreshSeconds = 0.2f;

        private readonly StringBuilder builder = new StringBuilder(256);
        private string text = "";
        private float nextRefresh;
        private GUIStyle style;

        private void Start()
        {
            if (cycle == null) cycle = FindObjectOfType<MarsDayNightCycle>();
            if (schedule == null) schedule = FindObjectOfType<MarsWeatherSchedule>();
        }

        private void Update()
        {
            if (!visible || cycle == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshSeconds;

            MarsTime.State now = cycle.Now;
            builder.Length = 0;
            builder.Append("Sol ").Append(now.localSol.ToString("N0"))
                .Append("  ·  Mars Year ").Append(now.marsYear).Append('\n');
            builder.Append(MarsTime.FormatClock(now.localMeanSolarTimeHours)).Append(" LMST  ·  ")
                .Append(PartOfDay(now)).Append('\n');
            builder.Append("Ls ").Append(now.solarLongitude.ToString("F0")).Append("°  ·  ")
                .Append(cycle.LatLon.x < 0 ? "southern " : "northern ")
                .Append(MarsTime.Season(now.solarLongitude, cycle.LatLon.x)).Append('\n');
            builder.Append("Earth ").Append(cycle.UtcNow.ToString("yyyy-MM-dd HH:mm")).Append(" UTC");

            if (schedule != null)
            {
                builder.Append("\n\nWeather: ").Append(schedule.CurrentLabel).Append(SourceText(schedule.CurrentSource));
                if (schedule.NextSolarEvent != null)
                {
                    builder.Append("\nNext: ").Append(schedule.NextSolarEvent.label).Append(" in ")
                        .Append(Duration(schedule.NextSolarEventMarsHours)).Append(" (NASA DONKI)");
                }
                else if (!schedule.HasArchive)
                {
                    builder.Append("\nNo DONKI archive bundled");
                }
                else if (!schedule.InsideArchiveWindow)
                {
                    builder.Append("\nPast the end of the DONKI archive");
                }
            }
            text = builder.ToString();
        }

        private static string PartOfDay(MarsTime.State now)
        {
            if (now.sunElevation < -6.0) return "night";
            bool morning = now.localTrueSolarTimeHours < 12.0;
            if (now.sunElevation < 3.0) return morning ? "dawn" : "dusk";
            return morning ? "morning" : "afternoon";
        }

        private static string SourceText(MarsWeatherSchedule.Source source)
        {
            switch (source)
            {
                case MarsWeatherSchedule.Source.Donki: return " (NASA DONKI)";
                case MarsWeatherSchedule.Source.Simulated: return " (simulated)";
                default: return "";
            }
        }

        private static string Duration(double marsHours)
        {
            if (marsHours >= 48.0) return (marsHours / 24.0).ToString("F0") + " sols";
            if (marsHours >= 1.0) return marsHours.ToString("F0") + " h";
            return (marsHours * 60.0).ToString("F0") + " min";
        }

        private void OnGUI()
        {
            if (!visible || text.Length == 0) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.UpperLeft;
                style.wordWrap = false;
                style.padding = new RectOffset(10, 10, 8, 8);
                style.normal.textColor = MarsPalette.PalePink;
            }
            style.fontSize = fontSize;
            Vector2 size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(Screen.width - size.x - 10f, 10f, size.x, size.y), text, style);
        }
    }
}
