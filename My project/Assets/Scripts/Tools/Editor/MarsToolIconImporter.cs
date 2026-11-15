using UnityEditor;
using UnityEngine;

namespace MarsSurvival.Tools.EditorTools
{
    /// <summary>
    /// Keeps the hotbar icons crisp: uncompressed, transparent, with mipmaps so they
    /// scale down smoothly. The icons are rendered from the Task_assets tool geometry.
    /// </summary>
    public sealed class MarsToolIconImporter : AssetPostprocessor
    {
        private const string IconFolder = "Assets/MarsTools/Resources/MarsTools/Icons";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(IconFolder)) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
