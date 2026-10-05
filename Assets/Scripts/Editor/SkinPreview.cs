using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// dotRPG > Skin Preview Sheets: composes every costume skin exactly as the game draws it (warrior sheets +
    /// code-drawn legs and sword arm, mage frame sets) into one sheet per skin under Logs/SkinPreview, so the
    /// whole set (5 views x every frame) can be checked side by side with the base look.
    /// </summary>
    public static class SkinPreview
    {
        static readonly string[] Views = { "down", "downside", "side", "upside", "up" };
        static readonly string[] Frames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "hurt", "attack0", "attack2", "attack4" };

        [MenuItem("dotRPG/Skin Preview Sheets")]
        public static void Export()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "SkinPreview");
            Directory.CreateDirectory(dir);
            var art = new SpriteLibrary(36);
            Write(art, CharacterLook.Player, Path.Combine(dir, "base_warrior.png"));
            Write(art, CharacterLook.Mage, Path.Combine(dir, "base_mage.png"));
            foreach (var s in SkinCatalog.All)
                Write(art, SkinCatalog.LookFor(s.cls, s.id), Path.Combine(dir, s.id + ".png"));
            Debug.Log("[SkinPreview] wrote " + dir);
        }

        static void Write(SpriteLibrary art, CharacterLook look, string path)
        {
            const int cell = 64;
            var sheet = new Texture2D(cell * Frames.Length, cell * Views.Length, TextureFormat.RGBA32, false);
            var clear = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(60, 70, 60, 255);
            sheet.SetPixels32(clear);
            for (int r = 0; r < Views.Length; r++)
                for (int c = 0; c < Frames.Length; c++)
                {
                    string frame = Frames[c];
                    if (!SilverWarriorArt.Supports(look.id) && frame.StartsWith("attack")) frame = "attack";
                    var sprite = art.GetCharacter(look, Views[r], frame);
                    if (sprite == null) continue;
                    var tex = sprite.texture;
                    var rect = sprite.textureRect;
                    int w = Mathf.Min(cell, (int)rect.width), h = Mathf.Min(cell, (int)rect.height);
                    var px = tex.GetPixels((int)rect.x, (int)rect.y, w, h);
                    int ox = c * cell + (cell - w) / 2, oy = (Views.Length - 1 - r) * cell;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var p = px[y * w + x];
                            if (p.a > 0.5f) sheet.SetPixel(ox + x, oy + y, p);
                        }
                }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
        }
    }
}
