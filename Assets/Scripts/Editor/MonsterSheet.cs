using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// Renders the dungeon monster / boss art straight from ProceduralArt (no player build) into
    /// Logs/monhd_sheet.png (field skeleton, small monsters, totem) and Logs/monhd_sheet_bosses.png.
    /// Columns: down/up/side/downside/upside × idle0, attack. Batchmode:
    /// -executeMethod DotRPG.EditorTools.MonsterSheet.Render
    /// </summary>
    public static class MonsterSheet
    {
        static readonly string[] Views = { "down", "up", "side", "downside", "upside" };
        static readonly string[] Frames = { "idle0", "attack" };
        static readonly string[] SmallIds = { "skel_warrior", "skel_gold", "skel_miner", "skel_necro", "skel_archer", "skel_shield", "skel_knight", "totem" };
        static readonly string[] BossIds = { "boss_gold_foreman", "boss_mine_captain", "boss_lich", "boss_archer_chief", "boss_armory_warden", "boss_skeleton_king" };

        [MenuItem("dotRPG/Dev/Render Monster Sheet")]
        public static void Render()
        {
            Directory.CreateDirectory("Logs");
            var small = new CharacterLook[SmallIds.Length + 1];
            small[0] = CharacterLook.Skeleton;
            for (int i = 0; i < SmallIds.Length; i++) small[i + 1] = MonsterDatabase.Get(SmallIds[i]).look;
            Sheet("Logs/monhd_sheet.png", small, 36, 60, 4);
            var bosses = new CharacterLook[BossIds.Length + 1];
            bosses[0] = CharacterLook.Skeleton;
            for (int i = 0; i < BossIds.Length; i++) bosses[i + 1] = MonsterDatabase.Get(BossIds[i]).look;
            Sheet("Logs/monhd_sheet_bosses.png", bosses, 100, 116, 2);
            Debug.Log("MONHD sheet written");
        }

        static void Sheet(string path, CharacterLook[] looks, int cw, int ch, int scale)
        {
            int cols = Views.Length * Frames.Length;
            int w = cols * cw * scale, h = looks.Length * ch * scale;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                int cx = (i % w) / (cw * scale), cy = (i / w) / (ch * scale);
                px[i] = (cx + cy) % 2 == 0 ? new Color32(74, 80, 70, 255) : new Color32(92, 98, 86, 255);
            }
            for (int r = 0; r < looks.Length; r++)
                for (int vi = 0; vi < Views.Length; vi++)
                    for (int fi = 0; fi < Frames.Length; fi++)
                    {
                        var c = ProceduralArt.DrawCharacter(looks[r], Views[vi], Frames[fi]);
                        // Feet (pivot) aligned to a common baseline; 16px-grid art drawn at 2x so sizes compare.
                        int k = c.Density >= 2 ? 1 : 2;
                        int col = vi * Frames.Length + fi;
                        int ox = col * cw + (cw - c.Width * k) / 2;
                        int baseY = (r + 1) * ch - 4; // top-down row of the pivot
                        int oy = baseY - (int)((c.Height - c.PivotY) * k);
                        for (int y = 0; y < c.Height * k; y++)
                            for (int x = 0; x < c.Width * k; x++)
                            {
                                var p = c.Pixels[(y / k) * c.Width + x / k];
                                if (p.a == 0) continue;
                                for (int sy = 0; sy < scale; sy++)
                                    for (int sx = 0; sx < scale; sx++)
                                    {
                                        int X = (ox + x) * scale + sx, Y = (oy + y) * scale + sy;
                                        if (X < 0 || Y < 0 || X >= w || Y >= h) continue;
                                        int idx = (h - 1 - Y) * w + X;
                                        px[idx] = p.a == 255 ? p : Color32.Lerp(px[idx], new Color32(p.r, p.g, p.b, 255), p.a / 255f);
                                    }
                            }
                    }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
