using UnityEditor;

namespace MarsSurvival.World.EditorTools
{
    /// <summary>
    /// Import settings for the outpost models in Assets/MarsBase (from Base_assets).
    /// Keeps the authored flat normals, metre scale, and readable meshes so the runtime
    /// MeshColliders also work in builds. Materials are swapped for the matte palette
    /// at run time by MarsOutpost, so the imported OBJ materials are only placeholders.
    /// </summary>
    public sealed class MarsBaseImporter : AssetPostprocessor
    {
        private const string ModelFolder = "Assets/MarsBase";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            ModelImporter importer = (ModelImporter)assetImporter;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importBlendShapes = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.isReadable = true;
            importer.globalScale = 1f;
        }

        [MenuItem("Mars/Base/Reimport Outpost Models")]
        private static void Reimport()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
        }
    }
}
