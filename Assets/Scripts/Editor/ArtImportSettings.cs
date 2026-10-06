using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// [ART] Every new picture dropped into Assets/Resources/Art is a pixel-art sprite: single sprite, 64 pixels per
    /// unit, point filter, no mipmaps, no compression (SpriteLibrary loads them with Resources.Load&lt;Sprite&gt;, which
    /// finds nothing for a plain texture). Pictures already set up are left alone. FixExisting() repairs ones that
    /// came in as plain textures before this rule existed (batch: -executeMethod DotRPG.EditorTools.ArtImportSettings.FixExisting).
    /// </summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        const string ArtFolder = "Assets/Resources/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtFolder)) return;
            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing) return;
            Apply(importer);
        }

        static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
        }

        public static void FixExisting()
        {
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType == TextureImporterType.Default)
                {
                    Apply(importer);
                    importer.SaveAndReimport();
                    fixedCount++;
                }
            }
            Debug.Log($"[ART] import settings fixed: {fixedCount}");
        }
    }
}
