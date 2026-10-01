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
