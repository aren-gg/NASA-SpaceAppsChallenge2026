using UnityEditor;

namespace MarsSurvival.Base.EditorTools
{
    /// <summary>
    /// Import settings for the Blender-made outpost modules (Assets/Resources/OutpostModules):
    /// keep the meshes readable so the solid meshes can get mesh colliders at runtime, keep the
    /// Blender normals (smooth, with hardened bevels), and import at the file's metre scale.
    /// </summary>
    public sealed class OutpostModuleImport : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.Contains("/OutpostModules/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }
}
