using System.IO;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// Writes every vfx clip as a strip of frames (left to right) on a dark ground to Logs/vfx/&lt;clip&gt;.png, for judging the
    /// animation art. Not part of the game.
    /// </summary>
    public static class VfxPreview
    {
        public static readonly string[] Clips =
        {
            "f_arc", "f_x", "f_wave", "f_line", "f_cut", "f_shatter", "f_vslash", "f_spark", "impact",
            "g_dome", "g_domeloop", "g_hexburst", "g_spin", "g_clang", "g_ward", "g_roar", "g_bash", "g_chain",
            "m_fireball", "m_explode", "m_icebloom", "m_spark", "m_star", "m_starburst", "m_portal", "m_vortex", "m_collapse", "m_hit",
            "b_feather", "b_heal", "b_lotus", "b_bell", "b_spear", "b_cross", "b_wings", "b_pillar",
        };

        public static void Export()
        {
            Directory.CreateDirectory("Logs/vfx");
            foreach (var name in Clips)
            {
                var frames = VfxArt.Frames(name);
                if (frames == null) { Debug.LogError("[dotRPG] vfx clip missing: " + name); continue; }
                int w = 0, h = 0;
                foreach (var f in frames) { w += f.Width + 4; h = Mathf.Max(h, f.Height); }
                var px = new Color32[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 28, 36, 255);
                int ox = 0;
                foreach (var f in frames)
                {
                    for (int y = 0; y < f.Height; y++)
                        for (int x = 0; x < f.Width; x++)
                        {
                            var p = f.Pixels[y * f.Width + x];
                            if (p.a == 0) continue;
                            int sx = ox + x, sy = h - 1 - y;
                            var b = px[sy * w + sx];
                            float a = p.a / 255f;
                            px[sy * w + sx] = new Color32((byte)(b.r + (p.r - b.r) * a), (byte)(b.g + (p.g - b.g) * a), (byte)(b.b + (p.b - b.b) * a), 255);
                        }
                    ox += f.Width + 4;
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes($"Logs/vfx/{name}.png", tex.EncodeToPNG());
                File.WriteAllText($"Logs/vfx/{name}.txt", $"{frames.Length} {frames[0].Width} {frames[0].Height}");
                Object.DestroyImmediate(tex);
            }
            Debug.Log("[dotRPG] vfx preview written: Logs/vfx");
        }
    }
}
