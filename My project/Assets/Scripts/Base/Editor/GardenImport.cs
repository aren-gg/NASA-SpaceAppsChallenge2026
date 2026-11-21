using UnityEditor;

namespace MarsSurvival.Base.EditorTools
{
    /// <summary>The crop icons in Resources/Garden/Icons (tools/blender/garden_assets.py) import as UI sprites.</summary>
    public sealed class GardenImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Garden/Icons/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
        }
    }
}
