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
            string file = System.IO.Path.GetFileNameWithoutExtension(importer.assetPath);
            // Generated skill effect pictures are drawn at twice the pixel density of the procedural fx.
            if (importer.assetPath.Contains("/FxImg/"))
            {
                importer.spritePixelsPerUnit = 128f;
                // Falling swords stand on their tip, like the procedural fx_bigsword (pivot 2 px above the bottom of 152).
                if (file.Contains("bigsword"))
                {
                    var s = new TextureImporterSettings();
                    importer.ReadTextureSettings(s);
                    s.spriteAlignment = (int)SpriteAlignment.Custom;
                    importer.SetTextureSettings(s);
                    importer.spritePivot = new Vector2(0.5f, 2f / 152f);
                }
            }
            // 9-slice UI frames: a 12 px rim, scaled to the same on-screen thickness as ui_btn (8 px at 32 ppu).
            if (file.StartsWith("ui_tab_") || file == "ui_btn_disabled")
            {
                importer.spritePixelsPerUnit = 48f;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.spriteBorder = new Vector4(12f, 12f, 12f, 12f);
            }
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            // The painted world map is shown smaller than drawn: smooth sampling keeps it from shimmering.
            if (System.IO.Path.GetFileNameWithoutExtension(importer.assetPath) == "worldmap_atlas") importer.filterMode = FilterMode.Bilinear;
        }

        public static void FixExisting()
        {
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && (importer.textureType == TextureImporterType.Default
                    || (System.IO.Path.GetFileNameWithoutExtension(path) == "worldmap_atlas" && importer.filterMode != FilterMode.Bilinear)))
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
