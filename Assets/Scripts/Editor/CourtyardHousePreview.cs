using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public static class CourtyardHousePreview
    {
        [MenuItem("dotRPG/Art/Preview Courtyard Houses")]
        public static void Export()
        {
            const string path = "Assets/Resources/Art/Town/courtyard_house.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 120f;
            importer.spritePivot = new Vector2(.5f, 42f / 1024f);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(.5f, 42f / 1024f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var library = new SpriteLibrary(16);
            for (int i = 0; i < 3; i++)
            {
                var sprite = library.Get("town_house_" + i);
                if (sprite == null || AssetDatabase.GetAssetPath(sprite) != path || sprite.pixelsPerUnit != 120f)
                    throw new Exception("Generated house is not connected to runtime key " + i);
                if (Vector2.Distance(sprite.pivot, new Vector2(768, 42)) > .01f)
                    throw new Exception("Generated house entrance pivot changed");
                var texture = sprite.texture;
                if (texture.width != 1536 || texture.height != 1024 || texture.filterMode != FilterMode.Point)
                    throw new Exception("Generated house texture was resized or filtered");
                for (int x = 0; x < texture.width; x++)
                    if (texture.GetPixel(x, 0).a > .01f || texture.GetPixel(x, texture.height - 1).a > .01f)
                        throw new Exception("Generated house touches vertical frame edge");
                for (int y = 0; y < texture.height; y++)
                    if (texture.GetPixel(0, y).a > .01f || texture.GetPixel(texture.width - 1, y).a > .01f)
                        throw new Exception("Generated house touches horizontal frame edge");
            }
            Debug.Log("[CourtyardHouse] PASS generated PNG: runtime keys 0/1/2, point sampling, transparent frame, base pivot (768,42)");
        }

        // Historical fallback only. The generated PNG is now the actual runtime house.
        public static void ExportProceduralFallback()
        {
            const string folder = "Docs/images/courtyard-houses";
            Directory.CreateDirectory(folder);
            var sheet = new PixelCanvas(330, 110);
            for (int variant = 0; variant < 3; variant++)
            {
                var house = ProceduralArt.Draw("town_house_" + variant);
                if (house.Width != 110 || house.Height != 110 || house.Density != 2 || house.PivotX != 55 || house.PivotY != 5)
                    throw new Exception("Courtyard house changed footprint or door pivot: " + variant);
                for (int y = 0; y < 110; y++) for (int x = 0; x < 110; x++)
                    if ((x == 0 || x == 109 || y == 0 || y == 109) && house.Get(x, y).a > 128)
                        throw new Exception("Courtyard house has a clipped silhouette: " + variant);
                Write(Path.Combine(folder, "house_" + variant + ".png"), house);
                sheet.Blit(house, variant * 110, 0);
            }
            Write(Path.Combine(folder, "houses.png"), sheet);
            Debug.Log("[CourtyardHouse] PASS 3 houses: 110x110, 32px/tile, original door pivot, unclipped silhouette");
        }
        public static void ExportAndBuild() { Export(); BuildScript.BuildWindows(); }
        static void Write(string path, PixelCanvas canvas)
        {
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.ToTexturePixels()); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
