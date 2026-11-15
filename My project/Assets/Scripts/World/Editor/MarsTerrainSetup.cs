using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.World.EditorTools
{
    /// <summary>
    /// One-click setup: creates the terrain config and palette materials as assets,
    /// then adds the terrain, a sunset light, fog and an inspection camera to the
    /// open scene, following docs/game-design-references.md.
    /// </summary>
    public static class MarsTerrainSetup
    {
        private const string DataFolder = "Assets/Data/Terrain";
        private const string HeightPath = DataFolder + "/gale_crater_200m.bytes";
        private const string MetaPath = DataFolder + "/gale_crater_200m.metadata.json";
        private const string ConfigPath = DataFolder + "/MarsTerrainConfig.asset";
        private const string MaterialFolder = "Assets/Art/Materials";

        // Curiosity's landing site, used as the starting viewpoint.
        private const double StartLatitude = -4.5895;
        private const double StartLongitude = 137.4417;

        [MenuItem("Mars/Terrain/Create Gale Crater Terrain")]
        public static void CreateTerrain()
        {
            TextAsset heights = AssetDatabase.LoadAssetAtPath<TextAsset>(HeightPath);
            TextAsset meta = AssetDatabase.LoadAssetAtPath<TextAsset>(MetaPath);
            if (heights == null || meta == null)
            {
                EditorUtility.DisplayDialog("Mars terrain",
                    "Prepared elevation data was not found at " + HeightPath +
                    ".\n\nRun: python tools/data/fetch_mars_dem.py", "OK");
                return;
            }

            MarsTerrainConfig config = AssetDatabase.LoadAssetAtPath<MarsTerrainConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MarsTerrainConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            ApplyRecommendedSettings(config, heights, meta);

            MarsTerrain terrain = Object.FindObjectOfType<MarsTerrain>();
            if (terrain == null)
            {
                GameObject terrainObject = new GameObject("Mars Terrain");
                Undo.RegisterCreatedObjectUndo(terrainObject, "Create Mars Terrain");
                terrain = terrainObject.AddComponent<MarsTerrain>();
                terrainObject.AddComponent<MarsTerrainDataOverlay>();
            }
            terrain.config = config;

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create Mars Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            MarsTerrainFlyCamera fly = camera.GetComponent<MarsTerrainFlyCamera>();
            if (fly == null) fly = camera.gameObject.AddComponent<MarsTerrainFlyCamera>();
            fly.terrain = terrain;
            terrain.viewer = camera.transform;

            // Place the camera above the landing site, looking toward Mount Sharp (south-east).
            using (MarsHeightmap map = new MarsHeightmap(heights, meta, config.realMetersPerUnityUnit, config.verticalExaggeration))
            {
                Vector2 local = map.LatLonToLocal(StartLatitude, StartLongitude);
                float y = map.SampleFacetedY(local.x, local.y, config.StrideForLod(0));
                // 30 m up so the crater floor and Mount Sharp are both in view at 1:1.
                camera.transform.position = terrain.transform.TransformPoint(new Vector3(local.x, y + config.ToUnits(30f), local.y));
                camera.transform.rotation = Quaternion.Euler(4f, 135f, 0f);
                camera.nearClipPlane = Mathf.Max(0.05f, config.ToUnits(0.1f));
                // Far clip, fog and sky colour are set from the render distance at run time.
                camera.farClipPlane = config.ToUnits(config.renderDistanceMeters + config.unloadMarginMeters) + map.MaxY;
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.Lerp(MarsPalette.PalePink, MarsPalette.WarmOrange, 0.35f);

            ApplySunsetLighting();

            Selection.activeGameObject = terrain.gameObject;
            EditorUtility.SetDirty(terrain);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Debug.Log("Mars terrain created. Press Play; F3 toggles the data overlay.", terrain);
        }

        /// <summary>
        /// Resets the existing config asset to the current script defaults (streaming,
        /// detail distances, colour thresholds) and the palette materials, keeping the
        /// data references. Needed because Unity keeps the old values stored in the asset
        /// when script defaults change.
        /// </summary>
        [MenuItem("Mars/Terrain/Reset Config To Recommended Settings")]
        public static void ResetConfig()
        {
            MarsTerrainConfig config = AssetDatabase.LoadAssetAtPath<MarsTerrainConfig>(ConfigPath);
            if (config == null)
            {
                CreateTerrain();
                return;
            }
            ApplyRecommendedSettings(config, config.heightData, config.metadata);
            Selection.activeObject = config;
            Debug.Log("MarsTerrainConfig reset to recommended settings. Press Play to see the result.", config);
        }

        private static void ApplyRecommendedSettings(MarsTerrainConfig config, TextAsset heights, TextAsset meta)
        {
            MarsTerrainConfig defaults = ScriptableObject.CreateInstance<MarsTerrainConfig>();
            Undo.RecordObject(config, "Reset Mars Terrain Config");
            EditorUtility.CopySerialized(defaults, config);
            Object.DestroyImmediate(defaults);

            config.heightData = heights;
            config.metadata = meta;
            config.baseMaterial = GetOrCreateMaterial("Terrain_Terracotta", MarsPalette.Terracotta);
            config.steepMaterial = GetOrCreateMaterial("Terrain_Mauve", MarsPalette.Mauve);
            // Dusty rose sits closer to terracotta than clay sand does, so flat ground reads as
            // a soft variation rather than pale patches. Terrain_ClaySand is kept for paths.
            config.flatMaterial = GetOrCreateMaterial("Terrain_DustyRose", MarsPalette.DustyRose);
            config.rockMaterial = GetOrCreateMaterial("Rock_MutedOlive", MarsPalette.MutedOlive);
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Terrain_ClaySand.mat") != null)
            {
                GetOrCreateMaterial("Terrain_ClaySand", MarsPalette.ClaySand); // converts it too
            }
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private const string WeatherPrefabPath = "Assets/MarsWeather/Prefabs/MarsWeather.prefab";

        /// <summary>
        /// Adds the weather module's prefab to the open terrain scene and links the two:
        /// the weather owns sky and fog, follows the camera, and uses fog scaled to the map.
        /// Follows Assets/MarsWeather/README.md: one prefab instance, sun assigned,
        /// post-processing on the camera, no preview components.
        /// </summary>
        [MenuItem("Mars/Add Weather To Terrain Scene")]
        public static void AddWeather()
        {
            MarsTerrain terrain = Object.FindObjectOfType<MarsTerrain>();
            if (terrain == null)
            {
                CreateTerrain();
                terrain = Object.FindObjectOfType<MarsTerrain>();
                if (terrain == null) return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeatherPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Mars weather", "The weather prefab was not found at " + WeatherPrefabPath + ".", "OK");
                return;
            }

            MarsWeather.WeatherController weather = Object.FindObjectOfType<MarsWeather.WeatherController>();
            if (weather == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, "Add Mars Weather");
                weather = instance.GetComponent<MarsWeather.WeatherController>();
            }

            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                ApplySunsetLighting();
                sun = RenderSettings.sun;
            }
            Undo.RecordObject(weather, "Assign Weather Sun");
            weather.sun = sun;
            PrefabUtility.RecordPrefabInstancePropertyModifications(weather);

            MarsSurvival.Integration.MarsWorldWeatherLink link = Object.FindObjectOfType<MarsSurvival.Integration.MarsWorldWeatherLink>();
            if (link == null)
            {
                GameObject linkObject = new GameObject("World Weather Link");
                Undo.RegisterCreatedObjectUndo(linkObject, "Add World Weather Link");
                link = linkObject.AddComponent<MarsSurvival.Integration.MarsWorldWeatherLink>();
            }
            link.terrain = terrain;
            link.weather = weather;

            Camera camera = terrain.viewer != null ? terrain.viewer.GetComponent<Camera>() : Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                EnablePostProcessing(camera);
            }

            Selection.activeGameObject = weather.gameObject;
            EditorUtility.SetDirty(link);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Debug.Log("Weather added. Press Play; keys F5-F9 switch Clear, Solar Flare, Dust Storm, Heat Storm, Cold Night.", weather);
        }

        /// <summary>
        /// Turns on URP post-processing for the camera (bloom and grading from the weather module).
        /// Done by reflection so this script does not need a compile-time URP reference.
        /// </summary>
        private static void EnablePostProcessing(Camera camera)
        {
            System.Type dataType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (dataType == null) return;
            Component data = camera.GetComponent(dataType);
            if (data == null) data = Undo.AddComponent(camera.gameObject, dataType);
            System.Reflection.PropertyInfo property = dataType.GetProperty("renderPostProcessing");
            if (property != null)
            {
                Undo.RecordObject(data, "Enable Post Processing");
                property.SetValue(data, true, null);
                EditorUtility.SetDirty(data);
            }
        }

        [MenuItem("Mars/Terrain/Apply Sunset Lighting")]
        public static void ApplySunsetLighting()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light light in Object.FindObjectsOfType<Light>())
                {
                    if (light.type == LightType.Directional)
                    {
                        sun = light;
                        break;
                    }
                }
            }
            if (sun == null)
            {
                GameObject sunObject = new GameObject("Sun");
                Undo.RegisterCreatedObjectUndo(sunObject, "Create Mars Sun");
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // About 20 degrees above the horizon, warm orange-pink, soft shadows.
            sun.transform.rotation = Quaternion.Euler(20f, -60f, 0f);
            sun.color = new Color(1f, 0.74f, 0.58f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            // Purple ambient fill.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = MarsPalette.Mauve;
            RenderSettings.ambientEquatorColor = Color.Lerp(MarsPalette.Mauve, MarsPalette.DustyRose, 0.5f) * 0.8f;
            RenderSettings.ambientGroundColor = MarsPalette.DeepPurple;

            // Light pink/purple distance fog that fades the far crater rim.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(MarsPalette.PalePink, MarsPalette.Mauve, 0.45f);
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 2200f;
        }

        private static Material GetOrCreateMaterial(string materialName, Color color)
        {
            string path = MaterialFolder + "/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                // Materials made before the project used URP are on the built-in Standard
                // shader and render pink; move them onto the active pipeline's shader.
                if (MarsPalette.ConvertToMatte(material, color)) EditorUtility.SetDirty(material);
                return material;
            }

            if (!Directory.Exists(MaterialFolder))
            {
                Directory.CreateDirectory(MaterialFolder);
                AssetDatabase.Refresh();
            }
            material = MarsPalette.CreateMatteMaterial(materialName, color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
