using System.IO;
using MarsTrainer.Look;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MarsWeather;

namespace MarsTrainer.Look.Editor
{
    public static class P11LookSetup
    {
        private const string Materials = "Assets/MarsWeather/Materials/P11";
        private const string ProfilePath = "Assets/Resources/MarsLook.asset";
        [MenuItem("Mars/P11/Apply Look")]
        public static void Apply()
        {
            Directory.CreateDirectory(Materials); Directory.CreateDirectory("Assets/Resources"); AssetDatabase.Refresh();
            var look = AssetDatabase.LoadAssetAtPath<MarsLookProfile>(ProfilePath);
            if (look == null) { look = ScriptableObject.CreateInstance<MarsLookProfile>(); AssetDatabase.CreateAsset(look, ProfilePath); }
            var ground = Matte("Terracotta", look.terracotta);
            var rose = Matte("Rose", look.rose); var olive = Matte("Olive", look.olive);
            var orange = Matte("Orange", look.orange); var purple = Matte("Purple", look.purple);
            var pink = Matte("Pink", look.pink); Matte("Mauve", look.mauve); var sand = Matte("Sand", look.sand);
            look.safeShell = olive; look.warnShell = orange; look.dangerShell = rose; look.criticalShell = pink;
            look.skyMaterial = Asset("SunsetSky", AssetDatabase.LoadAssetAtPath<Shader>("Assets/MarsWeather/Shaders/SunsetSky.shadergraph"));
            look.skyMaterial.renderQueue = (int)RenderQueue.Background;
            look.dustMaterial = Asset("Dust", Shader.Find("MarsWeather/SoftDust"));
            look.edgeMaterial = Asset("VisorEdges", Shader.Find("MarsTrainer/Look/VisorEdges"));
            EditorUtility.SetDirty(look);
            const string prefabPath = "Assets/MarsWeather/Prefabs/MarsWeather.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            var weather = prefab.GetComponent<WeatherController>(); weather.look = look;
            weather.skyTemplate = look.skyMaterial; weather.particleMaterial = look.dustMaterial;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath); PrefabUtility.UnloadPrefabContents(prefab);
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/MarsWeather/Scenes/WeatherPreview.unity")
                scene = EditorSceneManager.OpenScene("Assets/MarsWeather/Scenes/WeatherPreview.unity");
            foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                string label = renderer.gameObject.name;
                renderer.sharedMaterial = label.Contains("surface") ? ground
                    : label.Contains("band") || label.Contains("ring") ? orange
                    : label.Contains("cap") || label.Contains("platform") || label.Contains("plinth") ? purple : sand;
            }
            foreach (var filter in Object.FindObjectsOfType<MeshFilter>())
                if (filter.gameObject.name.Contains("globe") || filter.gameObject.name.Contains("cap")) filter.sharedMesh = P11PreviewMeshes.Globe();
            foreach (var controller in Object.FindObjectsOfType<WeatherController>())
            {
                controller.look = look; controller.skyTemplate = look.skyMaterial; controller.particleMaterial = look.dustMaterial;
                EditorUtility.SetDirty(controller); PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            }
            foreach (var light in Object.FindObjectsOfType<Light>())
            {
                light.enabled = light.type == LightType.Directional;
                light.color = look.orange; light.intensity = look.sunIntensity;
                light.transform.rotation = Quaternion.Euler(look.sunPitch, look.sunYaw, 0);
                light.shadows = LightShadows.Hard;
            }
            foreach (var camera in Object.FindObjectsOfType<Camera>())
            {
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true; camera.allowHDR = true;
                camera.transform.position = new Vector3(8, 4, -10);
                camera.transform.LookAt(new Vector3(0, 4, 10)); camera.fieldOfView = 48;
            }
            RenderSettings.skybox = look.skyMaterial; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = look.mauve; RenderSettings.fogDensity = look.clearFog;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = look.mauve * look.ambientIntensity;
            RenderSettings.subtractiveShadowColor = look.purple;
            Shader.SetGlobalColor("_MarsHorizon", look.orange); Shader.SetGlobalColor("_MarsMiddle", look.rose); Shader.SetGlobalColor("_MarsZenith", look.purple);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("P11_LOOK_APPLIED: eight shared matte colours; texture-free sky and visor shaders.");
        }
        [MenuItem("Mars/P11/Create Review Scene")]
        public static void CreateReviewScene()
        {
            const string path = "Assets/MarsWeather/Scenes/P11LookReview.unity";
            var original = EditorSceneManager.OpenScene("Assets/MarsWeather/Scenes/WeatherPreview.unity");
            EditorSceneManager.SaveScene(original, path, true);
            var review = EditorSceneManager.OpenScene(path);
            var terrain = GameObject.Find("Mars Terrain");
            if (terrain != null) terrain.SetActive(false);
            foreach (var component in Object.FindObjectsOfType<MonoBehaviour>())
                if (component.GetType().Name == "MarsTerrainFlyCamera") component.enabled = false;
            foreach (var camera in Object.FindObjectsOfType<Camera>())
            {
                camera.transform.position = new Vector3(8, 4, -10);
                camera.transform.LookAt(new Vector3(0, 4, 10)); camera.farClipPlane = 300;
                camera.clearFlags = CameraClearFlags.Skybox;
            }
            EditorSceneManager.MarkSceneDirty(review); EditorSceneManager.SaveScene(review); AssetDatabase.SaveAssets();
        }

        private static Material Matte(string name, Color color)
        {
            var m = Asset(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", 0); m.SetFloat("_Smoothness", 0);
            m.SetFloat("_SpecularHighlights", 0); m.SetFloat("_EnvironmentReflections", 0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.enableInstancing = true;
            EditorUtility.SetDirty(m); return m;
        }
        private static Material Asset(string name, Shader shader)
        {
            if (shader == null) throw new System.InvalidOperationException("P11 shader missing: " + name);
            string path = Materials + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }
    }
}
