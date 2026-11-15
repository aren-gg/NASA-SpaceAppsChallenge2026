using UnityEditor;

namespace MarsSurvival.Base.EditorTools
{
    /// <summary>
    /// Import settings for the tool models in Assets/MarsTools (copied from Task_assets/models).
    /// Keeps the authored flat normals and metre scale. The outpost swaps the imported
    /// OBJ materials for the matte palette materials at run time, so the imported ones
    /// are only placeholders that carry the palette names.
    /// </summary>
    public sealed class MarsToolModelImporter : AssetPostprocessor
    {
        private const string ModelFolder = "Assets/MarsTools";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            ModelImporter importer = (ModelImporter)assetImporter;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importBlendShapes = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.globalScale = 1f;
        }

        [MenuItem("Mars/Base/Reimport Tool Models")]
        private static void Reimport()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
        }
    }
}
