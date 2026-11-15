using System;
using System.IO;
using MarsTrainer.Gameplay.World;
using MarsTrainer.UI;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MarsTrainer.Shipping.Editor
{
    public static class P12Build
    {
        private const string Scene = "Assets/Scenes/Main.unity";

        [InitializeOnLoadMethod]
        private static void SetPlayableEntryPoint()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                var entry = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
                if (entry != null) EditorSceneManager.playModeStartScene = entry;
            };
        }

        [MenuItem("Mars Trainer/Open Playable Game")]
        public static void OpenPlayableGame()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Scene);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
        }

        [MenuItem("Mars Trainer/P12/Prepare ship scene")]
        public static void Prepare()
        {
            HudView.BuildPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = new GameObject("Mars Outpost").AddComponent<TerrainGenerator>();
            world.OpenWorldConfig = AssetDatabase.LoadAssetAtPath<MarsSurvival.World.MarsTerrainConfig>("Assets/Data/Terrain/MarsTerrainConfig.asset");
            world.SurfaceShader = Shader.Find("Universal Render Pipeline/Lit");
            world.Heightmap = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/mars_height.png");
            world.ElevationData = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Terrain/gale_crater_200m.bytes");
            world.ElevationMetadata = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Terrain/gale_crater_200m.metadata.json");
            EditorSceneManager.SaveScene(scene, Scene);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/P12Main.unity", true);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Mars Trainer/P12/Build macOS")]
        public static void BuildMac() { Build(BuildTarget.StandaloneOSX, "MarsTrainer.app"); }

        [MenuItem("Mars Trainer/P12/Build WebGL")]
        public static void BuildWebGL()
        {
            PlayerSettings.WebGL.template = "PROJECT:MarsTrainer";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            Build(BuildTarget.WebGL, "WebGL");
        }

        private static void Build(BuildTarget target, string name)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new InvalidOperationException("Install Unity " + Application.unityVersion + " build support for " + target);
            Prepare();
            string root = Environment.GetEnvironmentVariable("MARS_BUILD_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../P12Builds"));
            Directory.CreateDirectory(root);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene }, target = target,
                locationPathName = Path.Combine(root, name), options = BuildOptions.None
            });
            File.WriteAllText(Path.Combine(root, target + ".txt"), report.summary.result + " errors=" + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("P12 build failed: " + target);
        }
    }
}
