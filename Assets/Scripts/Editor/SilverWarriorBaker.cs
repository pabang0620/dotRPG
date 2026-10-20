using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    // Legacy CLI entry points now validate the approved art instead of rebaking retired bodies.
    public class SilverWarriorBaker : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/SilverWarrior/") &&
                !assetPath.StartsWith("Assets/Resources/FemaleWarrior/") &&
                !assetPath.StartsWith("Assets/ArtSources/FemaleWarrior/") &&
                !assetPath.StartsWith("Assets/ArtSources/SilverWarrior/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.isReadable = true;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = false;
            t.npotScale = TextureImporterNPOTScale.None;
            t.filterMode = FilterMode.Point;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.wrapMode = TextureWrapMode.Clamp;
            t.maxTextureSize = 4096;
        }

        public static void Validate() => FemaleWarriorValidation.Validate();
        public static void ValidateAndBuild() => FemaleWarriorValidation.ValidateAndBuild();
        public static void BakeAndBuild() => FemaleWarriorValidation.ValidateAndBuild();
    }
}
