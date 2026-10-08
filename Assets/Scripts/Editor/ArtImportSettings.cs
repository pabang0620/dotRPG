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
        const string ResourcesFolder = "Assets/Resources/";
        const string AndroidPlatform = "Android";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(ResourcesFolder)) ApplyAndroid((TextureImporter)assetImporter);
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

        /// <summary>
        /// [ANDROID] Per-platform override only (PC Default/Standalone settings are untouched). Pictures the code reads back
        /// with GetPixels (isReadable) stay RGBA32 at full size: a compressed texture cannot be read. The rest are ASTC:
        /// 4x4 up to 64 px (icons, dot sprites), else 6x6, at most 2048 px. Filter mode is not platform specific and stays as imported.
        /// </summary>
        static bool ApplyAndroid(TextureImporter importer)
        {
            var current = importer.GetPlatformTextureSettings(AndroidPlatform);
            var wanted = importer.GetPlatformTextureSettings(AndroidPlatform);
            wanted.name = AndroidPlatform;
            wanted.overridden = true;
            if (importer.isReadable)
            {
                wanted.format = TextureImporterFormat.RGBA32;
                wanted.maxTextureSize = 8192;
            }
            else
            {
                wanted.format = PngLongSide(importer.assetPath) <= 64 ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
                wanted.maxTextureSize = 2048;
                wanted.compressionQuality = (int)TextureCompressionQuality.Normal;
            }
            if (current.overridden && current.format == wanted.format && current.maxTextureSize == wanted.maxTextureSize
                && (importer.isReadable || current.compressionQuality == wanted.compressionQuality)) return false;
            importer.SetPlatformTextureSettings(wanted);
            return true;
        }

        /// <summary>Longer side of a PNG read from its header; a huge value when it is not a readable PNG (-> the 6x6 branch).</summary>
        static int PngLongSide(string path)
        {
            try
            {
                using (var stream = System.IO.File.OpenRead(path))
                {
                    var head = new byte[24];
                    if (stream.Read(head, 0, 24) < 24 || head[1] != (byte)'P' || head[2] != (byte)'N' || head[3] != (byte)'G') return int.MaxValue;
                    int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                    int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                    return Mathf.Max(w, h);
                }
            }
            catch (System.Exception) { return int.MaxValue; }
        }

        /// <summary>[ANDROID] Brings the pictures already in Assets/Resources up to the Android override (only the ones that differ are reimported).</summary>
        public static void ApplyAndroidToExisting()
        {
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ResourcesFolder.TrimEnd('/') }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is TextureImporter importer && ApplyAndroid(importer))
                    {
                        importer.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"[ART] Android texture overrides updated: {changed}");
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
