using MarsSurvival.TimeAndWeather;
using MarsSurvival.UI;
using MarsSurvival.World;
using MarsWeather;
using UnityEngine;

namespace MarsSurvival.Integration
{
    /// <summary>
    /// When a scene with a MarsTerrain starts, adds the Mars clock and day-night cycle,
    /// the weather schedule and the clock info tab, and connects them to the terrain,
    /// the sun and the weather module. Nothing is added if a MarsDayNightCycle is
    /// already in the scene, so a hand-placed setup wins.
    ///
    /// If the scene has no weather prefab, the editor loads it for this play session
    /// and logs how to add it permanently; a built player needs it in the scene.
    /// </summary>
    public static class MarsEnvironmentBootstrap
    {
        private const string WeatherPrefabPath = "Assets/MarsWeather/Prefabs/MarsWeather.prefab";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            MarsTerrain terrain = Object.FindObjectOfType<MarsTerrain>();
            if (terrain == null || Object.FindObjectOfType<MarsDayNightCycle>() != null) return;

            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light light in Object.FindObjectsOfType<Light>())
                {
                    if (light.type == LightType.Directional) { sun = light; break; }
                }
            }

            WeatherController weather = Object.FindObjectOfType<WeatherController>();
#if UNITY_EDITOR
            if (weather == null)
            {
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(WeatherPrefabPath);
                if (prefab != null)
                {
                    weather = Object.Instantiate(prefab).GetComponent<WeatherController>();
                    Debug.LogWarning("The scene has no weather prefab, so it was added for this play session only. " +
                                     "Choose Mars > Add Weather To Terrain Scene and save the scene to keep it.");
                }
            }
#endif
            if (weather != null)
            {
                if (weather.sun == null && sun != null)
                {
                    // Re-enable so the controller records the sun's original values for its own clean-up.
                    weather.enabled = false;
                    weather.sun = sun;
                    weather.enabled = true;
                }
                MarsWorldWeatherLink link = Object.FindObjectOfType<MarsWorldWeatherLink>();
                if (link == null) link = new GameObject("World Weather Link").AddComponent<MarsWorldWeatherLink>();
                if (link.terrain == null) link.terrain = terrain;
                if (link.weather == null) link.weather = weather;
            }
            else
            {
                Debug.LogWarning("No weather module in the scene: the clock and day-night cycle run, " +
                                 "but weather events are listed on the info tab without visuals.");
            }

            GameObject environment = new GameObject("Mars Environment");
            MarsDayNightCycle cycle = environment.AddComponent<MarsDayNightCycle>();
            cycle.terrain = terrain;
            cycle.sun = sun;
            cycle.weather = weather;
            MarsWeatherSchedule schedule = environment.AddComponent<MarsWeatherSchedule>();
            schedule.cycle = cycle;
            schedule.weather = weather;
            MarsClockHud hud = environment.AddComponent<MarsClockHud>();
            hud.cycle = cycle;
            hud.schedule = schedule;
        }
    }
}
