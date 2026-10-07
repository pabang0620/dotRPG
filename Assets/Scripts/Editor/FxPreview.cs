using System.IO;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// Renders every skill effect sprite twice, classic 16px (scaled 4x) on the left and high-resolution on the right,
    /// at the same world size on a dark ground, into Logs/fx_preview.png. For judging the art, not part of the game.
    /// </summary>
    public static class FxPreview
    {
        static readonly string[] Keys =
        {
            "fx_glow", "fx_shock", "fx_ring", "fx_rune", "fx_swoosh", "fx_blade", "fx_zap", "fx_frost", "fx_crack", "fx_spike",
            "fx_ice", "fx_shard", "fx_snow", "fx_spark", "fx_streak", "fx_cut", "fx_slash", "fx_crescent", "fx_arc", "fx_bigsword",
            "fx_frostorb", "fx_flame", "fx_meteor", "fx_scorch", "fx_star", "fx_aegis", "fx_feather", "fx_holy", "fx_sparkle", "fx_bolt", "fx_magic", "fx_dust",
        };

        public static void Export()
        {
            const int cell = 240, pad = 8, cols = 4;
            int rows = (Keys.Length + cols - 1) / cols;
            int width = cols * (cell * 2 + pad * 3), height = rows * (cell + pad * 2);
            var sheet = new Color32[width * height];
            for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(28, 32, 40, 255);
            for (int i = 0; i < Keys.Length; i++)
            {
                int ox = (i % cols) * (cell * 2 + pad * 3) + pad, oy = (i / cols) * (cell + pad * 2) + pad;
                ProceduralArt.HdEffects = false;
                var old = ProceduralArt.Draw(Keys[i]);
                ProceduralArt.HdEffects = true;
                var hd = ProceduralArt.Draw(Keys[i]);
                Blit(sheet, width, height, old, ox, oy, cell, 4 / Mathf.Max(1, old.Density));
                Blit(sheet, width, height, hd, ox + cell + pad, oy, cell, 4 / Mathf.Max(1, hd.Density));
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(sheet);
            tex.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/fx_preview.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[dotRPG] fx preview written: Logs/fx_preview.png");
        }

        /// <summary>Draws a canvas (top row first) into the sheet (bottom row first), scaled so 1 classic pixel = 4 sheet pixels x zoom.</summary>
        static void Blit(Color32[] sheet, int w, int h, PixelCanvas c, int ox, int oy, int cell, int scale)
        {
            int zoom = Mathf.Max(1, Mathf.Min(cell / Mathf.Max(1, c.Width * scale), cell / Mathf.Max(1, c.Height * scale)));
            if (c.Width * scale > cell || c.Height * scale > cell) { zoom = 1; scale = Mathf.Max(1, Mathf.Min(cell / c.Width, cell / c.Height)); }
            int s = scale * zoom;
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    var p = c.Pixels[y * c.Width + x];
                    if (p.a == 0) continue;
                    for (int dy = 0; dy < s; dy++)
                        for (int dx = 0; dx < s; dx++)
                        {
                            int px = ox + x * s + dx, py = h - 1 - (oy + y * s + dy);
                            if (px < 0 || py < 0 || px >= w || py >= h) continue;
                            var b = sheet[py * w + px];
                            float a = p.a / 255f;
                            sheet[py * w + px] = new Color32((byte)(b.r + (p.r - b.r) * a), (byte)(b.g + (p.g - b.g) * a), (byte)(b.b + (p.b - b.b) * a), 255);
                        }
                }
        }
    }
}
