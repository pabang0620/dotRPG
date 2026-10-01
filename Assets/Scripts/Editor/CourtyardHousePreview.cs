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
            var library = new SpriteLibrary(16);
            foreach (var building in VillageBuildingArt.All)
            {
                string path = building.FilePath;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = building.Ppu;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = building.Pivot;
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
                var sprite = library.Get(building.Key);
                if (sprite == null || AssetDatabase.GetAssetPath(sprite) != path || sprite.pixelsPerUnit != building.Ppu)
                    throw new Exception("Building is not connected to runtime key " + building.Key);
                if (Vector2.Distance(sprite.pivot, new Vector2(768, building.Bottom)) > .01f)
                    throw new Exception("Building entrance pivot changed: " + building.Key);
                var texture = sprite.texture;
                if (texture.width != 1536 || texture.height != 1024 || texture.filterMode != FilterMode.Point)
                    throw new Exception("Building texture was resized or filtered: " + building.Key);
                // Generated alpha can contain 1-3/255 residual values in empty pixels.
                // Reject visible edge contact while tolerating this near-transparent encoding noise.
                const float edgeAlphaTolerance = 4f / 255f;
                for (int x = 0; x < texture.width; x++)
                    if (texture.GetPixel(x, 0).a > edgeAlphaTolerance || texture.GetPixel(x, texture.height - 1).a > edgeAlphaTolerance)
                        throw new Exception("Building touches vertical frame edge: " + building.Key);
                for (int y = 0; y < texture.height; y++)
                    if (texture.GetPixel(0, y).a > edgeAlphaTolerance || texture.GetPixel(texture.width - 1, y).a > edgeAlphaTolerance)
                        throw new Exception("Building touches horizontal frame edge: " + building.Key);
            }
            Debug.Log("[VillageBuildings] PASS 7 generated PNGs: runtime bindings, point filtering, transparent margins and aligned pivots");
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
