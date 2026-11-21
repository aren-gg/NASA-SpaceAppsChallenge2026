using System.Linq;
using UnityEditor;

namespace MarsSurvival.Base.EditorTools
{
    /// <summary>
    /// Import settings for the astronaut (Assets/Resources/Astronaut, from tools/blender/astronaut_nasa.py
    /// and astronaut_mocap.py): the body and the clip file are both Humanoid on the same skeleton, so
    /// the motion-capture clips play on the body as recorded. Clips play in place and loop.
    /// </summary>
    public sealed class AstronautImport : AssetPostprocessor
    {
        private static readonly string[] Looping = { "Idle", "Walk", "Run", "Sprint", "Fall" };

        /// <summary>Bumped when these settings change, so Unity re-imports the astronaut files.</summary>
        public override uint GetVersion() { return 6; }

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
    }
}
