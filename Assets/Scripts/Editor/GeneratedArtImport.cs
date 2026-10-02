using System;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// [ART] Import settings for the generated village props and story objects in Resources/Art (made by
    /// Tools/art/process_generated.py): sprite, point filtering, no compression, pixels-per-unit from the
    /// measured opaque size and the world size each prop should have, pivot at the feet (bottom centre of the
    /// opaque pixels). Also the generated stable buildings (VillageBuildingArt canvas: ppu 136, step at y 980).
    /// </summary>
    public static class GeneratedArtImport
    {
        /// <summary>(sprite key, world size, measure by height?) - widths for wide props, heights for tall ones.</summary>
        static readonly (string key, float size, bool byHeight)[] Props =
        {
            ("town_fountain_0", 3.0f, false), ("town_fountain_1", 3.0f, false), ("town_fountain_2", 3.0f, false),
            ("town_well", 2.0f, false), ("town_board", 2.0f, false), ("town_lamp", 2.3f, true),
            ("town_barrel", 1.0f, true), ("town_crate", 0.95f, true), ("town_sacks", 1.15f, false),
            ("town_hay", 1.05f, true), ("town_woodpile", 2.0f, false), ("town_ccrate", 1.0f, false),
            ("town_pot", 0.85f, true), ("town_planter", 1.0f, false), ("town_mailbox", 1.25f, true),
            ("town_bench", 2.0f, false), ("town_sign_0", 1.25f, true), ("town_anvil", 1.0f, true),
            ("story_horse", 2.1f, true), ("story_trough", 1.6f, false), ("story_grave", 1.1f, true),
            ("story_grave_1", 0.95f, true), ("story_lantern", 2.0f, true), ("story_ribbon", 1.0f, true),
            ("town_site_0", 3.0f, false), ("town_site_1", 3.0f, false), ("town_pile_0", 1.2f, false), ("town_pile_1", 1.2f, false),
            ("town_stump", 0.9f, false), ("town_grave_0", 1.0f, true), ("town_grave_1", 1.0f, true), ("town_ruin", 1.3f, true),
            ("town_bones", 0.9f, false),
        };

        static readonly System.Collections.Generic.HashSet<string> PoleProps =
            new System.Collections.Generic.HashSet<string> { "story_lantern", "town_sign_0", "town_mailbox", "town_lamp" };

        static readonly string[] Buildings = { "town_stable", "town_stable_burned" };

        [MenuItem("dotRPG/Art/Import Generated Village Art")]
        public static void Import()
        {
            foreach (var (key, size, byHeight) in Props)
            {
                string path = "Assets/Resources/Art/" + key + ".png";
                var importer = Prepare(path);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var px = texture.GetPixels32();
                int w = texture.width, h = texture.height, left = w, right = 0, bottom = h, top = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a < 128) continue;
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                    }
                if (right <= left || top <= bottom) throw new Exception("Empty generated sprite: " + key);
                // Feet: the opaque span of the lowest tenth of the prop.
                int baseLeft = w, baseRight = 0, band = Math.Max(1, (top - bottom) / 10);
                for (int y = bottom; y <= bottom + band; y++)
                    for (int x = left; x <= right; x++)
                        if (px[y * w + x].a >= 128) { baseLeft = Math.Min(baseLeft, x); baseRight = Math.Max(baseRight, x); }
                float measured = byHeight ? top - bottom + 1 : right - left + 1;
                importer.spritePixelsPerUnit = measured / size;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                // Pole props stand on their pole; everything else is centred on its whole width (a wide prop's
                // lowest pixels are often just one near leg in this camera).
                float footX = PoleProps.Contains(key) ? (baseLeft + baseRight + 1f) / 2f : (left + right + 1f) / 2f;
                settings.spritePivot = new Vector2(footX / w, bottom / (float)h);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                Debug.Log($"[GeneratedArt] {key}: ppu={importer.spritePixelsPerUnit:0.0} size={size} pivot={settings.spritePivot}");
            }
            foreach (var key in Buildings)
            {
                var def = VillageBuildingArt.Find(key);
                var importer = Prepare(def.FilePath);
                importer.spritePixelsPerUnit = def.Ppu;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = def.Pivot;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                Debug.Log($"[GeneratedArt] {key}: building ppu={def.Ppu} pivot={def.Pivot}");
            }
            // [ART] Generated character frames (Tools/art/process_characters.py): 128 px, feet 12 px up, ppu 72
            // so they stand as tall as the 64 px / ppu 36 warrior.
            foreach (var file in System.IO.Directory.GetFiles("Assets/Resources/Art", "char_*.png"))
            {
                string path = file.Replace('\\', '/');
                var importer = Prepare(path);
                // 64 px dot frames (warrior format, feet 7 px up) or 128 px frames (feet 12 px up).
                bool dot = AssetDatabase.LoadAssetAtPath<Texture2D>(path).width <= 64;
                importer.spritePixelsPerUnit = dot ? 36f : 72f;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = dot ? new Vector2(0.5f, 7f / 64f) : new Vector2(0.5f, 12f / 128f);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }

        static TextureImporter Prepare(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new Exception("Missing generated PNG: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return importer;
        }

        public static void ImportAndBuild() { Import(); BuildScript.BuildWindows(); }
    }
}
