using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MarsBuildTools
{
    private static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Artifacts"));
    static MarsBuildTools() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string request = Path.Combine(Folder, "unity-request.txt");
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "build") Build();
            else if (command == "play") EditorApplication.isPlaying = true;
            else if (command == "stop") EditorApplication.isPlaying = false;
            else if (command == "validate")
                File.WriteAllText(Path.Combine(Folder, "validation.txt"), "Unity compiled successfully: " + Application.unityVersion + "\n" + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception e) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "editor-error.txt"), e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Mars/Build Survival Game")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        const string scenePath = "Assets/Scenes/Mars.unity";
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        bool hasWeather = false;
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<MarsWeather.WeatherController>(true) != null) hasWeather = true;
        if (!hasWeather)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MarsWeather/Prefabs/MarsWeather.prefab");
            if (prefab == null) throw new Exception("Weather prefab is missing.");
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath }, locationPathName = Path.Combine(Folder, "MarsOutpost/MarsOutpost.exe"),
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        File.WriteAllText(Path.Combine(Folder, "build-result.txt"), report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nBytes: " + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Player build failed; inspect Unity console.");
    }
}
