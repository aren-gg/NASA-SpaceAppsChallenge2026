using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MarsTrainer.Gameplay.World.Editor
{
    public static class WorldSceneSetup
    {
        [MenuItem("Mars Trainer/P9/Create World Scene")]
        public static void CreateScene()
        {
            const string path = "Assets/Data/mars_height.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var platform = importer.GetDefaultPlatformTextureSettings();
                platform.format = TextureImporterFormat.R16;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = new GameObject("Mars P9 World").AddComponent<TerrainGenerator>();
            world.SurfaceShader = Shader.Find("Universal Render Pipeline/Lit");
            world.Heightmap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/P9World.unity");
        }
    }
}
