using System;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public static class LivingWaterImport
    {
        public static void ImportAndBuild()
        {
            const string path = "Assets/Resources/Art/Nature/koi.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
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
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int min = tex.width, max = 0;
            var pixels = tex.GetPixels32();
            for (int y = 0; y < tex.height; y++) for (int x = 0; x < tex.width; x++)
            {
                if (pixels[y * tex.width + x].a < 128) continue;
                min = Math.Min(min, x); max = Math.Max(max, x);
            }
            if (min <= 0 || max >= tex.width - 1 || max <= min) throw new Exception("Koi alpha margin invalid");
            importer.spritePixelsPerUnit = (max - min + 1) / 1.4f;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            foreach (var name in new[] { "LivingWater", "SwimmingKoi" })
            {
                var shader = Resources.Load<Shader>("Shaders/" + name);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Invalid shader " + name);
            }
            Debug.Log("[LivingWater] PASS koi alpha, pixel filtering, scale, water and koi shaders");
            BuildScript.BuildWindows();
        }
    }
}
