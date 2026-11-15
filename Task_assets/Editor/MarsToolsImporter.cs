// Mars expedition tools: import settings that follow game-design-references.md section 5.
// Put this file in any Editor folder. Put the .obj/.mtl files in Assets/MarsTools/Models.
// Run  Tools > Mars Tools > Create Palette Materials  once; models then remap to those materials.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public class MarsToolsImporter : AssetPostprocessor
{
    const string ModelFolder = "Assets/MarsTools/Models";
    const string MaterialFolder = "Assets/MarsTools/Materials";

    static readonly (string name, string hex)[] Palette =
    {
        ("terracotta", "#B96D55"), ("dusty_rose", "#C78D87"), ("muted_olive", "#7D8865"),
        ("warm_orange", "#E6A06E"), ("deep_purple", "#40334F"), ("pale_pink", "#E9B9BC"),
        ("mauve", "#806886"), ("clay_sand", "#D8AC8F"),
    };

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ModelFolder)) return;
        var importer = (ModelImporter)assetImporter;
        importer.importNormals = ModelImporterNormals.Import;   // keep authored flat normals
        importer.importTangents = ModelImporterTangents.None;
        importer.importBlendShapes = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.isReadable = false;
        importer.globalScale = 1f;                               // OBJ is authored in metres
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName,
                                         ModelImporterMaterialSearch.RecursiveUp);
    }

    [MenuItem("Tools/Mars Tools/Create Palette Materials")]
    static void CreatePaletteMaterials()
    {
        Directory.CreateDirectory(MaterialFolder);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        foreach (var (name, hex) in Palette)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            ColorUtility.TryParseHtmlString(hex, out var c);
            mat.shader = shader;
            mat.SetColor("_BaseColor", c);
            mat.SetColor("_Color", c);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Glossiness", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_GlossyReflections", 0f);
            mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();

        // Re-import the models so they pick up the new materials.
        if (AssetDatabase.IsValidFolder(ModelFolder))
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);

        Debug.Log($"Mars Tools: {Palette.Length} palette materials ready in {MaterialFolder}");
    }
}
#endif
