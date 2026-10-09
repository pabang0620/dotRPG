using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Final, editable pixel artwork for the four equipped warrior weapon silhouettes.
    /// Coordinates and grip pivots deliberately match the shipped katana rig.
    /// This file is also compiled by Tools/art/preview_player_weapons.ps1: no editor is needed.
    /// </summary>
    public static partial class PlayerWeaponArt
    {
        public const string ResourceFolder = "Art/PlayerWeapons/";
        static Color32 C(string hex) => PixelCanvas.Hex(hex);
        static readonly Color32 Ink = C("202632"), Edge = C("f1f5e9");
        static readonly Color32 GoldDark = C("69472d"), Gold = C("b99051"), GoldLight = C("e8ce89");

        public static bool TryKey(string key, out int tier)
        {
            const string stem = "wpn_sword_";
            tier = -1;
            if (key == null || key.Length != stem.Length + 1 || !key.StartsWith(stem, StringComparison.Ordinal)) return false;
            tier = key[key.Length - 1] - '0';
            return tier >= 0 && tier <= 3;
        }

        public static PixelCanvas Draw(string key)
        {
            if (!TryKey(key, out int tier)) return null;
            var c = Katana(tier);
            // User-requested reflection of the weapon artwork only, about its x=32 grip
            // pivot. The player's anatomical hand and existing directional flips stay intact.
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width / 2; x++)
                {
                    int a = y * c.Width + x, b = y * c.Width + c.Width - 1 - x;
                    var pixel = c.Pixels[a]; c.Pixels[a] = c.Pixels[b]; c.Pixels[b] = pixel;
                }
            return c;
        }

        static void Poly(PixelCanvas c, Color32 color, params int[] p)
        {
            int minX = c.Width, maxX = 0, minY = c.Height, maxY = 0;
            for (int i = 0; i < p.Length; i += 2)
            { minX = Math.Min(minX, p[i]); maxX = Math.Max(maxX, p[i]); minY = Math.Min(minY, p[i + 1]); maxY = Math.Max(maxY, p[i + 1]); }
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    bool inside = false;
                    for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
                        if ((p[i + 1] > y + .5f) != (p[j + 1] > y + .5f)
                            && x + .5f < (p[j] - p[i]) * (y + .5f - p[i + 1]) / (p[j + 1] - p[i + 1]) + p[i]) inside = !inside;
                    if (inside) c.Set(x, y, color);
                }
        }

        static PixelCanvas Katana(int tier)
        {
            var c = new PixelCanvas(64, 64).WithPivot(32, 11.5f);
            // Author in the original lateral orientation; Draw mirrors the finished image.
            // Handedness, pose and hitbox calculations remain entirely outside artwork.
            Poly(c, Ink, 40, 6, 38, 18, 36, 32, 35, 46, 29, 46, 30, 30, 33, 15);
            if (tier == 0)
            {
                Poly(c, C("745037"), 39, 8, 37, 19, 35, 34, 34, 46, 30, 46, 31, 30, 34, 16);
                Poly(c, C("bb8d59"), 39, 8, 36, 23, 33, 45, 30, 45, 32, 28, 35, 15);
                c.Line(36, 16, 32, 38, C("e1ba7d")); c.VLine(31, 39, 44, C("e1ba7d"));
                c.Line(36, 24, 34, 42, C("8d5d3a"));
            }
            else
            {
                var dark = tier == 3 ? C("4c5064") : C("5a7083");
                var mid = tier == 2 ? C("aebfc4") : C("a6bccb");
                Poly(c, dark, 39, 8, 37, 20, 35, 34, 34, 46, 30, 46, 31, 30, 34, 16);
                Poly(c, mid, 39, 8, 36, 25, 33, 45, 30, 45, 32, 28, 35, 15);
                // Crisp edge, longitudinal bevel and a broken temper line (hamon), not glow.
                c.Line(38, 10, 35, 20, Edge); c.Line(35, 20, 32, 36, Edge); c.VLine(31, 37, 44, Edge);
                c.Line(37, 16, 34, 32, C("dae6e4")); c.VLine(33, 33, 42, C("dae6e4"));
                c.Set(34, 23, C("6b899e")); c.Set(33, 29, C("6b899e")); c.Set(32, 35, C("6b899e"));
                c.Line(35, 16, 37, 16, C("7d97aa"));
                if (tier == 3) { c.VLine(34, 36, 42, Gold); c.Set(34, 33, GoldLight); }
            }
            // Habaki and fitted tsuba: broad enough to read at normal play zoom, with empty
            // space in the ornate guards rather than a solid block hiding the player's hand.
            c.Rect(30, 44, 5, 3, GoldDark); c.Rect(30, 44, 4, 2, Gold); c.HLine(30, 33, 44, GoldLight);
            if (tier == 0)
            {
                c.Rect(27, 46, 11, 3, Ink); c.HLine(28, 36, 46, C("c09259")); c.HLine(29, 35, 47, C("6d4633"));
            }
            else if (tier == 1)
            {
                c.Ellipse(32, 47, 7, 2, Ink); c.HLine(27, 36, 46, GoldLight);
                c.HLine(27, 36, 47, Gold); c.Set(25, 47, GoldDark); c.Set(38, 47, GoldDark);
                c.Set(28, 47, Ink); c.Set(35, 47, Ink);
            }
            else if (tier == 2)
            {
                Poly(c, Ink, 24, 43, 29, 46, 36, 46, 40, 42, 39, 47, 35, 50, 28, 49, 24, 47);
                Poly(c, C("bdbaa6"), 25, 44, 30, 47, 35, 47, 39, 44, 37, 48, 28, 48);
                c.Line(26, 45, 29, 47, Edge); c.Line(36, 47, 38, 45, Edge);
                c.Set(31, 47, C("5a688c")); c.Set(32, 47, C("a6cce1"));
            }
            else
            {
                Poly(c, Ink, 23, 45, 28, 44, 32, 46, 36, 44, 41, 45, 38, 48, 35, 50, 28, 49);
                Poly(c, Gold, 24, 45, 28, 45, 32, 47, 36, 45, 40, 45, 36, 48, 28, 48);
                c.Line(25, 45, 29, 46, GoldLight); c.Line(35, 46, 38, 45, GoldLight);
                c.Rect(30, 46, 4, 3, GoldDark); c.Rect(31, 46, 2, 2, C("b1464c")); c.Set(31, 46, C("f0a182"));
            }
            // Tsuka centre remains x32,y52.5 = the unchanged 11.5px bottom-up hand pivot.
            c.Rect(29, 49, 6, 10, Ink);
            var wrap = tier == 0 ? C("68452e") : tier == 2 ? C("434c70") : tier == 3 ? C("6f3043") : C("343745");
            c.Rect(30, 49, 4, 9, wrap);
            for (int y = 50; y <= 56; y += 3)
            {
                c.Set(31, y, tier == 0 ? C("b99365") : C("d3c6a7")); c.Set(32, y + 1, tier == 0 ? C("96734e") : C("a89677"));
                c.Set(30, y + 1, tier == 3 ? C("ad5260") : C("65707c"));
            }
            c.HLine(30, 33, 58, Gold); c.HLine(30, 32, 58, GoldLight);
            c.HLine(30, 33, 49, GoldDark); c.Set(30, 49, Gold);
            return c;
        }

    }
}
