using System;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public static class VillageNatureImport
    {
        [MenuItem("dotRPG/Art/Import Village Nature")]
        public static void Import()
        {
            foreach (var art in VillageNatureArt.All)
            {
                AssetDatabase.ImportAsset(art.FilePath, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(art.FilePath) as TextureImporter;
                if (importer == null) throw new Exception("Missing generated nature PNG: " + art.FilePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.isReadable = true; // TreeFade samples the actual opaque canopy.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(art.FilePath);
                var pixels = texture.GetPixels32();
                int w = texture.width, h = texture.height, left = w, right = 0, bottom = h, top = 0;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    byte a = pixels[y * w + x].a;
                    if ((x == 0 || x == w - 1 || y == 0 || y == h - 1) && a > 4)
                        throw new Exception("Clipped nature sprite: " + art.Name);
                    if (a < 128) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
                if (top <= bottom || right <= left) throw new Exception("Empty alpha: " + art.Name);
                int visibleHeight = top - bottom + 1;
                // Locate the trunk / roots, not the (potentially asymmetric) crown's center.
                int baseLeft = w, baseRight = 0;
                for (int y = bottom; y <= bottom + visibleHeight / 10; y++) for (int x = left; x <= right; x++)
                    if (pixels[y * w + x].a >= 128) { baseLeft = Math.Min(baseLeft, x); baseRight = Math.Max(baseRight, x); }
                importer.spritePixelsPerUnit = visibleHeight / art.Height;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2((baseLeft + baseRight + 1f) / (2f * w), (bottom + visibleHeight * .035f) / h);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                var sprite = new SpriteLibrary(16).Get(art.Key);
                if (AssetDatabase.GetAssetPath(sprite) != art.FilePath || sprite.texture.filterMode != FilterMode.Point)
                    throw new Exception("Nature runtime binding / filtering failed: " + art.Key);
                Debug.Log($"[VillageNature] PASS {art.Name}: transparent margin, feet pivot, point sampling, height={art.Height}");
            }
        }
        public static void ImportAndBuild() { Import(); BuildScript.BuildWindows(); }
    }
}
