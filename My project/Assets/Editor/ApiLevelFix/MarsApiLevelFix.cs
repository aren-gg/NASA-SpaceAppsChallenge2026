using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// The Arduino panel uses System.IO.Ports, which only exists with the .NET Framework API level.
/// This lives in its own editor-only assembly so it still compiles (and fixes the setting)
/// while the game scripts are failing for exactly that reason.
/// </summary>
[InitializeOnLoad]
public static class MarsApiLevelFix
{
    static MarsApiLevelFix()
    {
        EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        NamedBuildTarget target = NamedBuildTarget.Standalone;
        if (PlayerSettings.GetApiCompatibilityLevel(target) == ApiCompatibilityLevel.NET_Unity_4_8) return;
        PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Unity_4_8);
        AssetDatabase.SaveAssets();
        Debug.Log("Mars: set Api Compatibility Level to .NET Framework so the Arduino serial link (System.IO.Ports) compiles. Scripts will recompile now.");
    }
}
