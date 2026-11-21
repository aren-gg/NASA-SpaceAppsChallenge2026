using System.Linq;
using UnityEditor;

namespace MarsSurvival.Base.EditorTools
{
    /// <summary>
    /// Import settings for the astronauts (Assets/Resources/Astronaut, from
    /// tools/blender/astronaut_vroid.py and astronaut_build.py): every model Humanoid, so the
    /// clips in Astronaut_Clips (a different skeleton) retarget onto the male and female astronauts.
    /// Clips play in place (root motion baked into the pose); locomotion and the fall loop.
    /// </summary>
    public sealed class AstronautImport : AssetPostprocessor
    {
        private static readonly string[] Looping = { "Idle", "Walk", "Run", "Sprint", "Fall" };

        /// <summary>Bumped when these settings change, so Unity re-imports the astronaut files.</summary>
        public override uint GetVersion() { return 3; }

        private void OnPreprocessModel()
        {
            if (!assetPath.Contains("/Resources/Astronaut/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.Contains("/Resources/Astronaut/")) return;
            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                string name = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
                clip.name = name;
                clip.loopTime = Looping.Contains(name);
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Astronaut/Portrait")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
        }
    }
}
