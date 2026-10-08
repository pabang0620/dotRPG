using System;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        static PixelCanvas DrawTile(string[] p)
        {
            var c = new PixelCanvas(16, 16);
            switch (p[1])
            {
                case "grass":
                {
                    int variant = p.Length > 2 ? int.Parse(p[2]) : 0;
                    c.Rect(0, 0, 16, 16, variant % 2 == 0 ? GrassA : GrassB);
                    var rng = new System.Random(variant * 7919 + 3);
                    for (int i = 0; i < 3; i++)
                    {
                        int x = rng.Next(1, 15), y = rng.Next(1, 15);
                        c.Set(x, y, PixelCanvas.WithAlpha(GrassDark, 90));
                    }
                    break;
                }
                case "dirt":
                case "soil":
                {
                    bool soil = p[1] == "soil";
                    int mask = p.Length > 2 ? int.Parse(p[2]) : 0;
                    int variant = p.Length > 3 ? int.Parse(p[3]) : 0;
                    c.Rect(0, 0, 16, 16, soil ? Soil : Dirt);
                    var rng = new System.Random(variant * 104729 + (soil ? 11 : 5));
                    for (int i = 0; i < 2; i++)
                    {
                        float x = rng.Next(3, 13), y = rng.Next(3, 13);
                        c.Ellipse(x, y, 2.2f, 1.4f, soil ? SoilSpot : DirtSpot);
                    }
                    // Grass lip where the neighbour is grass. mask: 1=N 2=E 4=S 8=W
                    var lipDark = PixelCanvas.Hex("#4fa83e");
                    if ((mask & 1) != 0) { c.Rect(0, 0, 16, 2, GrassA); c.HLine(0, 15, 2, lipDark); }
                    if ((mask & 4) != 0) { c.Rect(0, 14, 16, 2, GrassA); c.HLine(0, 15, 13, PixelCanvas.Shade(soil ? Soil : Dirt, 0.85f)); }
                    if ((mask & 8) != 0) { c.Rect(0, 0, 2, 16, GrassA); c.VLine(2, 0, 15, lipDark); }
                    if ((mask & 2) != 0) { c.Rect(14, 0, 2, 16, GrassA); c.VLine(13, 0, 15, lipDark); }
                    break;
                }
                case "water":
                {
                    bool edge = p.Length > 2 && p[2] == "edge";
                    int variant = p.Length > 3 ? int.Parse(p[3]) : (p.Length > 2 && !edge ? int.Parse(p[2]) : 0);
                    c.Rect(0, 0, 16, 16, Water);
                    var rng = new System.Random(variant * 31337 + 7);
                    for (int i = 0; i < 2; i++)
                    {
                        int x = rng.Next(0, 12), y = rng.Next(edge ? 7 : 1, 15);
                        c.HLine(x, x + 3, y, WaterLight);
                    }
                    c.Set(rng.Next(0, 16), rng.Next(edge ? 7 : 0, 16), WaterDark);
                    if (edge)
                    {
                        c.Rect(0, 0, 16, 4, Bank);
                        c.HLine(0, 15, 0, BankLight);
                        c.HLine(0, 15, 3, PixelCanvas.Shade(Bank, 0.8f));
                        for (int x = 0; x < 16; x++)
                            if ((x + variant) % 5 != 0) c.Set(x, 4, Foam);
                        c.Set((variant * 3) % 16, 5, Foam);
                    }
                    break;
                }
                case "dock":
                {
                    c.Rect(0, 0, 16, 16, WoodLight);
                    for (int y = 0; y < 16; y += 4)
                    {
                        c.HLine(0, 15, y + 3, WoodDark);
                        c.HLine(0, 15, y, PixelCanvas.Shade(WoodLight, 1.08f));
                    }
                    c.Set(2, 1, WoodDark); c.Set(13, 5, WoodDark); c.Set(3, 9, WoodDark); c.Set(12, 13, WoodDark);
                    c.VLine(0, 0, 15, Wood);
                    c.VLine(15, 0, 15, Wood);
                    break;
                }
                case "pave":
                {
                    // One 16px window into a seamless 128x128 paving pattern, so stone joints run
                    // across tile borders instead of framing every tile.
                    int ix = int.Parse(p[2]), iy = int.Parse(p[3]);
                    bool shade = p.Length > 4 && p[4] == "s";
                    var pattern = PavingPattern();
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            var col = pattern[(iy * 16 + y) * PaveSize + ix * 16 + x];
                            if (shade) col = PixelCanvas.Shade(col, y < 7 ? 0.66f : y < 11 ? 0.76f : 0.86f);
                            c.Pixels[y * 16 + x] = col;
                        }
                    break;
                }
                case "cliff":
                {
                    // tile_cliff_{d}_{flags}_{v}: d = rows of wall down to lower ground (1 = bottom
                    // row of a face, 4 = deep rock mass); flags 1 = plateau above, 2 = open left, 4 = open right.
                    int d = int.Parse(p[2]), flags = int.Parse(p[3]), v = int.Parse(p[4]);
                    DrawCliffTile(c, d, flags, v);
                    break;
                }
                case "cw":
                {
                    // tile_cw_{mask}_{v}: canyon water. mask 1 = land above (rock face drops into the
                    // water), 2 = land right, 4 = land left, 8 = cliff above, 16 = land below, 32 = bridge above.
                    int mask = int.Parse(p[2]), v = int.Parse(p[3]);
                    DrawCanyonWater(c, mask, v);
                    break;
                }
                case "bridge":
                {
                    // Planks run north-south (the bridge is crossed east-west); rails on the outer rows.
                    int part = int.Parse(p[2]); // 1 = north edge, 2 = south edge, 3 = both, 0 = middle
                    c.Rect(0, 0, 16, 16, WoodLight);
                    for (int x = 0; x < 16; x += 4)
                    {
                        c.VLine(x + 3, 0, 15, WoodDark);
                        c.VLine(x, 0, 15, PixelCanvas.Shade(WoodLight, 1.06f));
                    }
                    c.Set(1, 5, WoodDark); c.Set(9, 11, WoodDark); c.Set(6, 2, WoodDark); c.Set(13, 8, WoodDark);
                    if ((part & 1) != 0) { c.Rect(0, 0, 16, 3, Wood); c.HLine(0, 15, 0, WoodLight); c.HLine(0, 15, 3, WoodDark); }
                    if ((part & 2) != 0) { c.Rect(0, 12, 16, 4, Wood); c.HLine(0, 15, 12, WoodLight); c.HLine(0, 15, 15, BarkDark); }
                    break;
                }
                case "mossgrass":
                {
                    int variant = p.Length > 2 ? int.Parse(p[2]) : 0;
                    c.Rect(0, 0, 16, 16, variant % 2 == 0 ? MossA : MossB);
                    var rng = new System.Random(variant * 6007 + 9);
                    for (int i = 0; i < 4; i++) c.Set(rng.Next(1, 15), rng.Next(1, 15), PixelCanvas.WithAlpha(LeafDark, 120));
                    c.Set(rng.Next(1, 15), rng.Next(1, 15), PixelCanvas.WithAlpha(GrassLight, 140));
                    break;
                }
                case "flag":
                {
                    bool shade = p.Length > 2 && p[2] == "shade";
                    int variant = int.Parse(p[p.Length - 1]);
                    c.Rect(0, 0, 16, 16, Flag);
                    // Two rows of staggered paving stones with dark grout.
                    int split = 7 + variant % 2;
                    c.HLine(0, 15, split, FlagGrout);
                    c.HLine(0, 15, 15, FlagGrout);
                    int a = 3 + variant * 3 % 7, b = 9 + variant * 5 % 5;
                    c.VLine(a, 0, split - 1, FlagGrout);
                    c.VLine(b, split + 1, 14, FlagGrout);
                    // Soft highlight on the top edge of each stone.
                    c.HLine(0, a - 1, 0, FlagLight); c.HLine(a + 1, 15, 0, FlagLight);
                    c.HLine(0, b - 1, split + 1, FlagLight); c.HLine(b + 1, 15, split + 1, FlagLight);
                    var rng = new System.Random(variant * 7717 + 1);
                    for (int i = 0; i < 3; i++) c.Set(rng.Next(1, 15), rng.Next(1, 15), FlagSpot);
                    if (shade)
                    {
                        // Shadow cast by the cliff above.
                        for (int y = 0; y < 16; y++)
                        {
                            float s = y < 6 ? 0.62f : y < 9 ? 0.75f : 0.88f;
                            for (int x = 0; x < 16; x++) c.Pixels[y * 16 + x] = PixelCanvas.Shade(c.Pixels[y * 16 + x], s);
                        }
                    }
                    break;
                }
                case "cliffold":
                {
                    int mask = p.Length > 2 ? int.Parse(p[2]) : 0;
                    int variant = p.Length > 3 ? int.Parse(p[3]) : 0;
                    c.Rect(0, 0, 16, 16, Cliff);
                    // Big rounded boulders like a stacked rock wall.
                    var rng = new System.Random(variant * 4241 + mask * 13 + 5);
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = rng.Next(2, 14), cy = rng.Next(3, 13);
                        float rx = rng.Next(4, 7), ry = rng.Next(3, 5);
                        c.Ellipse(cx, cy, rx, ry, CliffDark);
                        c.Ellipse(cx - 0.5f, cy - 0.5f, rx - 1f, ry - 1f, Cliff);
                        c.Ellipse(cx - 1.5f, cy - 1.5f, Mathf.Max(1f, rx - 3f), Mathf.Max(1f, ry - 2.5f), CliffLight);
                    }
                    c.Set(rng.Next(1, 15), rng.Next(1, 15), CliffDeep);
                    c.Set(rng.Next(1, 15), rng.Next(1, 15), CliffDeep);
                    if ((mask & 1) != 0)
                    {
                        // Plateau rim on top of the cliff.
                        c.Rect(0, 0, 16, 3, CliffRim);
                        c.HLine(0, 15, 0, FlagLight);
                        c.HLine(0, 15, 3, CliffDark);
                    }
                    if ((mask & 4) != 0)
                    {
                        // Dark foot where the wall meets the ground.
                        c.Rect(0, 13, 16, 3, CliffDeep);
                        for (int x = 0; x < 16; x += 3) c.Set(x, 12, CliffDeep);
                    }
                    break;
                }
                case "cwater":
                {
                    bool edge = p.Length > 2 && p[2] == "edge";
                    int variant = int.Parse(p[p.Length - 1]);
                    c.Rect(0, 0, 16, 16, Teal);
                    var rng = new System.Random(variant * 911 + 3);
                    for (int i = 0; i < 3; i++)
                    {
                        int x = rng.Next(0, 12), y = rng.Next(edge ? 7 : 1, 15);
                        c.HLine(x, x + 2, y, TealLight);
                        c.Set(x + 3, y - 1, TealLight);
                    }
                    c.Set(rng.Next(0, 16), rng.Next(edge ? 7 : 0, 16), TealDark);
                    c.HLine(rng.Next(0, 10), rng.Next(10, 16), rng.Next(edge ? 8 : 2, 15), TealDark);
                    if (edge)
                    {
                        // Stone ledge instead of a sandy bank.
                        c.Rect(0, 0, 16, 5, Ledge);
                        c.HLine(0, 15, 0, LedgeLight);
                        c.HLine(0, 15, 4, CliffDark);
                        for (int x = 3 + variant; x < 16; x += 6) c.VLine(x, 1, 3, CliffDark);
                        for (int x = 0; x < 16; x++)
                            if ((x + variant) % 4 != 0) c.Set(x, 5, TealFoam);
                    }
                    break;
                }
                case "stairs":
                {
                    c.Rect(0, 0, 16, 16, FlagGrout);
                    for (int y = 0; y < 16; y += 4)
                    {
                        c.Rect(1, y, 14, 3, Flag);
                        c.HLine(1, 14, y, FlagLight);
                        c.HLine(1, 14, y + 3, CliffDark);
                    }
                    c.VLine(0, 0, 15, CliffDark);
                    c.VLine(15, 0, 15, CliffDark);
                    break;
                }
                default:
                    c.Rect(0, 0, 16, 16, new Color32(255, 0, 255, 255));
                    break;
            }
            c.WithPivot(8, 8);
            return c;
        }
    }
}
