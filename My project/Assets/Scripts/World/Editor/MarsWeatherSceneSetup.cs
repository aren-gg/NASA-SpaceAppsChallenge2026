using MarsSurvival.Integration;
using MarsSurvival.World;
using MarsWeather;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsSurvival.World.Editor
{
    /// <summary>Adds the independently owned weather prefab and bridge to the current Mars scene.</summary>
    public static class MarsWeatherSceneSetup
    {
        [MenuItem("Mars/Integrate Weather Into Open Scene")]
        public static void Integrate()
        {
            if (!EditorSceneManager.GetActiveScene().path.EndsWith("Assets/Scenes/Mars.unity"))
                EditorSceneManager.OpenScene("Assets/Scenes/Mars.unity");
            var terrain = Object.FindObjectOfType<MarsTerrain>();
            var camera = Camera.main;
            var light = Object.FindObjectOfType<Light>();
            if (terrain == null || camera == null || light == null)
            {
                Debug.LogError("Open the Mars scene containing MarsTerrain, MainCamera, and its directional light first.");
                return;
            }

            const string prefabPath = "Assets/MarsWeather/Prefabs/MarsWeather.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("Weather prefab missing: " + prefabPath);
                return;
            }

            foreach (var old in Object.FindObjectsOfType<WeatherController>())
                if (old.gameObject.scene == terrain.gameObject.scene) Object.DestroyImmediate(old.gameObject);
            foreach (var oldLink in Object.FindObjectsOfType<MarsWorldWeatherLink>())
                if (oldLink.gameObject.scene == terrain.gameObject.scene) Object.DestroyImmediate(oldLink.gameObject);

            var weatherObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, terrain.gameObject.scene);
            weatherObject.name = "Mars Weather";
            var weather = weatherObject.GetComponent<WeatherController>();
            weather.sun = light;
            PrefabUtility.RecordPrefabInstancePropertyModifications(weather);

            var linkObject = new GameObject("Mars World Weather Link");
            SceneManager.MoveGameObjectToScene(linkObject, terrain.gameObject.scene);
            var link = linkObject.AddComponent<MarsWorldWeatherLink>();
            link.terrain = terrain;
            link.weather = weather;
            link.followViewer = true;
            link.scaleFogToWorld = true;

            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            EditorSceneManager.SaveScene(terrain.gameObject.scene);
            Debug.Log("Mars terrain is linked to the reusable Mars Weather prefab.");
        }
    }
}
