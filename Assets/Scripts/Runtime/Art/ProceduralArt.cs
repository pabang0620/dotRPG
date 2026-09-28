using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Code-drawn placeholder pixel art in the spirit of the reference (bright 16px top-down,
    /// dark outlines, chibi characters). Nothing here is copied from the reference; every sprite is
    /// generated from simple shapes so the project is free of licensing questions.
    ///
    /// Replace any sprite by placing a PNG at Resources/Art/{key}.png — see <see cref="SpriteLibrary"/>.
    /// </summary>
    public static class ProceduralArt
    {
        // ---------- Palette ----------
        static readonly Color32 Outline = PixelCanvas.Hex("#2b1d16");
        static readonly Color32 GrassA = PixelCanvas.Hex("#63c74d");
        static readonly Color32 GrassB = PixelCanvas.Hex("#5bbd47");
        static readonly Color32 GrassDark = PixelCanvas.Hex("#3e9a3a");
        static readonly Color32 GrassLight = PixelCanvas.Hex("#8ad86a");
        static readonly Color32 Dirt = PixelCanvas.Hex("#d9a066");
        static readonly Color32 DirtSpot = PixelCanvas.Hex("#c68a52");
        static readonly Color32 Soil = PixelCanvas.Hex("#c4854f");
        static readonly Color32 SoilSpot = PixelCanvas.Hex("#ad7040");
        static readonly Color32 Water = PixelCanvas.Hex("#2f7fe0");
        static readonly Color32 WaterDark = PixelCanvas.Hex("#2468c9");
        static readonly Color32 WaterLight = PixelCanvas.Hex("#5ba8ff");
        static readonly Color32 Foam = PixelCanvas.Hex("#f4fbff");
        static readonly Color32 Bank = PixelCanvas.Hex("#c27e4a");
        static readonly Color32 BankLight = PixelCanvas.Hex("#e1a870");
        static readonly Color32 Wood = PixelCanvas.Hex("#c8783f");
        static readonly Color32 WoodLight = PixelCanvas.Hex("#e0a06a");
        static readonly Color32 WoodDark = PixelCanvas.Hex("#8e4e2a");
        static readonly Color32 Bark = PixelCanvas.Hex("#8a5a2e");
        static readonly Color32 BarkDark = PixelCanvas.Hex("#5e3a1e");
        static readonly Color32 LeafDark = PixelCanvas.Hex("#2f7d32");
        static readonly Color32 Leaf = PixelCanvas.Hex("#3e9b43");
        static readonly Color32 LeafLight = PixelCanvas.Hex("#5fbf4e");
        static readonly Color32 LeafShine = PixelCanvas.Hex("#8fdc6a");
        static readonly Color32 Stone = PixelCanvas.Hex("#9aa3ae");
        static readonly Color32 StoneLight = PixelCanvas.Hex("#c9d1d9");
        static readonly Color32 StoneDark = PixelCanvas.Hex("#6b7380");
        static readonly Color32 Carrot = PixelCanvas.Hex("#f28c28");
        static readonly Color32 CarrotLight = PixelCanvas.Hex("#ffb45a");
        static readonly Color32 Red = PixelCanvas.Hex("#e43b44");
        static readonly Color32 RedLight = PixelCanvas.Hex("#ff7a7f");
        static readonly Color32 White = PixelCanvas.Hex("#ffffff");
        static readonly Color32 Yellow = PixelCanvas.Hex("#ffd34a");
        static readonly Color32 Steel = PixelCanvas.Hex("#dde6ee");
        static readonly Color32 SteelDark = PixelCanvas.Hex("#94a3b3");
        static readonly Color32 Gold = PixelCanvas.Hex("#e0b040");
        static readonly Color32 Eye = PixelCanvas.Hex("#2b1d16");

        public static readonly string[] CharacterFrames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt" };

        /// <summary>Every non-character sprite key the game uses (for exporting / documentation).</summary>
        public static System.Collections.Generic.IEnumerable<string> AllKeys()
        {
            for (int v = 0; v < 2; v++) yield return $"tile_grass_{v}";
            for (int mask = 0; mask < 16; mask++)
                for (int v = 0; v < 3; v++)
                {
                    yield return $"tile_dirt_{mask}_{v}";
                    yield return $"tile_soil_{mask}_{v}";
                }
            for (int v = 0; v < 4; v++)
            {
                yield return $"tile_water_{v}";
                yield return $"tile_water_edge_{v}";
            }
            yield return "tile_dock";
            foreach (var k in new[] { "deco_tuft", "deco_flower_0", "deco_flower_1", "deco_pebble" }) yield return k;
            foreach (var k in new[] { "tree", "tree_fruit", "stump", "rock", "bush", "sign", "crate", "house", "site_blueprint", "site_built", "pile_wood", "pile_stone" }) yield return k;
            for (int mask = 0; mask < 16; mask++) yield return $"fence_{mask}";
            foreach (var k in new[] { "crop_carrot", "crop_sprout", "crop_hole" }) yield return k;
            foreach (var k in new[] { "tool_sword", "tool_axe", "tool_pickaxe", "tool_can", "tool_rod", "tool_hammer", "tool_crate" }) yield return k;
            foreach (var k in new[] { "fx_slash", "fx_sparkle", "fx_dust", "fx_leaf", "fx_chip", "fx_bone", "fx_water", "fx_alert", "shadow" }) yield return k;
            foreach (var k in new[] { "icon_wood", "icon_stone", "icon_carrot", "heart_full", "heart_half", "heart_empty" }) yield return k;
            foreach (var k in new[] { "ui_panel", "ui_dark", "ui_select", "ui_white" }) yield return k;
        }

        /// <summary>Draws a sprite by key. Returns null for unknown keys.</summary>
        public static PixelCanvas Draw(string key)
        {
            var parts = key.Split('_');
            try
            {
                switch (parts[0])
                {
                    case "tile": return DrawTile(parts);
                    case "deco": return DrawDeco(parts);
                    case "tree": return DrawTree(key == "tree_fruit");
                    case "stump": return DrawStump();
                    case "rock": return DrawRock();
                    case "bush": return DrawBush();
                    case "fence": return DrawFence(parts.Length > 1 ? int.Parse(parts[1]) : 0);
                    case "sign": return DrawSign();
                    case "crate": return DrawCrate(true);
                    case "house": return DrawHouse();
                    case "site": return parts[1] == "built" ? DrawWorkshop() : DrawBlueprint();
                    case "pile": return DrawPile(parts[1] == "stone");
                    case "crop": return DrawCrop(parts[1]);
                    case "tool": return DrawTool(parts[1]);
                    case "fx": return DrawFx(parts[1]);
                    case "icon": return DrawIcon(parts[1]);
                    case "heart": return DrawHeart(parts[1]);
                    case "ui": return DrawUi(parts[1]);
                    case "shadow": return DrawShadow();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[dotRPG] Could not draw '{key}': {e.Message}");
            }
            return null;
        }

        static int Hash(string s, int salt = 0)
        {
            unchecked
            {
                int h = 17 + salt * 31;
                foreach (char ch in s) h = h * 31 + ch;
                return h & 0x7fffffff;
            }
        }

        // ---------- Tiles ----------

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
                default:
                    c.Rect(0, 0, 16, 16, new Color32(255, 0, 255, 255));
                    break;
            }
            c.WithPivot(8, 8);
            return c;
        }

        static PixelCanvas DrawDeco(string[] p)
        {
            switch (p[1])
            {
                case "tuft":
                {
                    var c = new PixelCanvas(8, 6);
                    c.VLine(1, 3, 5, GrassDark); c.VLine(3, 1, 5, GrassDark); c.VLine(5, 2, 5, GrassDark); c.VLine(6, 4, 5, GrassDark);
                    c.Set(3, 0, GrassLight);
                    return c.WithBottomPivot();
                }
                case "flower":
                {
                    var c = new PixelCanvas(7, 7);
                    bool pink = p.Length > 2 && p[2] == "1";
                    var petal = pink ? PixelCanvas.Hex("#ffb3d1") : White;
                    c.Rect(2, 1, 3, 5, petal); c.Rect(1, 2, 5, 3, petal);
                    c.Rect(2, 2, 3, 3, Yellow);
                    c.Set(3, 3, PixelCanvas.Hex("#f0a020"));
                    c.Outline(PixelCanvas.WithAlpha(GrassDark, 160));
                    return c.WithBottomPivot();
                }
                case "pebble":
                {
                    var c = new PixelCanvas(6, 4);
                    c.Ellipse(3, 2, 2.5f, 1.6f, StoneDark);
                    c.PaintEllipse(2.6f, 1.6f, 1.5f, 0.9f, Stone);
                    return c.WithBottomPivot();
                }
            }
            return null;
        }

        // ---------- Props ----------

        static PixelCanvas DrawTree(bool fruit)
        {
            var c = new PixelCanvas(32, 38);
            // Trunk and roots.
            c.Rect(13, 26, 6, 9, Bark);
            c.VLine(13, 26, 34, BarkDark);
            c.VLine(18, 28, 34, BarkDark);
            c.Rect(11, 33, 3, 2, Bark);
            c.Rect(18, 33, 3, 2, Bark);
            c.Set(15, 29, BarkDark); c.Set(16, 31, BarkDark);
            // Canopy: overlapping blobs, dark first.
            c.Ellipse(16, 14, 13, 12, LeafDark);
            c.Ellipse(8, 19, 7, 7, LeafDark);
            c.Ellipse(24, 19, 7, 7, LeafDark);
            c.Ellipse(16, 21, 9, 6, LeafDark);
            c.PaintEllipse(15, 12, 11, 9.5f, Leaf);
            c.PaintEllipse(8, 17, 5, 5, Leaf);
            c.PaintEllipse(23, 17, 5.5f, 5, Leaf);
            c.PaintEllipse(12, 8, 6, 4.5f, LeafLight);
            c.PaintEllipse(21, 11, 4, 3.5f, LeafLight);
            c.PaintEllipse(7, 15, 3, 2.5f, LeafLight);
            c.Set(10, 6, LeafShine); c.Set(11, 6, LeafShine); c.Set(20, 9, LeafShine); c.Set(6, 13, LeafShine);
            // Leaf texture flecks.
            var rng = new System.Random(fruit ? 91 : 17);
            for (int i = 0; i < 14; i++)
            {
                int x = rng.Next(5, 27), y = rng.Next(5, 24);
                c.Paint(x, y, LeafDark);
            }
            if (fruit)
            {
                int[,] spots = { { 9, 10 }, { 17, 7 }, { 22, 14 }, { 13, 16 }, { 6, 19 }, { 25, 20 }, { 19, 20 } };
                for (int i = 0; i < spots.GetLength(0); i++)
                {
                    int x = spots[i, 0], y = spots[i, 1];
                    c.Rect(x, y, 2, 2, Carrot);
                    c.Set(x, y, CarrotLight);
                }
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3.5f);
        }

        static PixelCanvas DrawStump()
        {
            var c = new PixelCanvas(18, 14);
            c.Rect(3, 5, 12, 6, Bark);
            c.Rect(1, 9, 4, 3, Bark);
            c.Rect(13, 9, 4, 3, Bark);
            c.VLine(6, 6, 10, BarkDark); c.VLine(11, 6, 11, BarkDark);
            c.Ellipse(9, 5, 6, 3, WoodLight);
            c.PaintEllipse(9, 5, 3.5f, 1.6f, Wood);
            c.PaintEllipse(9, 5, 1.5f, 0.8f, WoodLight);
            c.Outline(Outline);
            return c.WithPivot(9, 2.5f);
        }

        static PixelCanvas DrawRock()
        {
            var c = new PixelCanvas(18, 14);
            c.Ellipse(9, 8, 8, 5.5f, StoneDark);
            c.Ellipse(7, 6, 5, 3.5f, StoneDark);
            c.PaintEllipse(8.5f, 7, 7, 4.2f, Stone);
            c.PaintEllipse(6.5f, 5, 3.5f, 2.2f, StoneLight);
            c.Set(12, 9, StoneDark); c.Set(13, 8, StoneDark); c.Set(4, 10, StoneDark);
            c.Outline(PixelCanvas.Hex("#3f454f"));
            return c.WithPivot(9, 2.5f);
        }

        static PixelCanvas DrawBush()
        {
            var c = new PixelCanvas(18, 16);
            c.Ellipse(9, 9, 8, 6.5f, LeafDark);
            c.PaintEllipse(8.5f, 8, 7, 5.5f, Leaf);
            c.PaintEllipse(6, 6, 3.5f, 2.5f, LeafLight);
            int[,] berries = { { 5, 9 }, { 10, 6 }, { 13, 10 }, { 8, 12 }, { 12, 3 } };
            for (int i = 0; i < berries.GetLength(0); i++)
            {
                int x = berries[i, 0], y = berries[i, 1];
                c.Rect(x, y, 2, 2, Red);
                c.Set(x, y, RedLight);
            }
            c.Outline(Outline);
            return c.WithPivot(9, 2.5f);
        }

        /// <summary>mask: 1 = connects left, 2 = right, 4 = up, 8 = down.</summary>
        static PixelCanvas DrawFence(int mask)
        {
            var c = new PixelCanvas(16, 18);
            bool left = (mask & 1) != 0, right = (mask & 2) != 0, up = (mask & 4) != 0;
            if (left) { c.Rect(0, 7, 7, 2, Wood); c.Rect(0, 12, 7, 2, Wood); c.HLine(0, 6, 7, WoodLight); }
            if (right) { c.Rect(9, 7, 7, 2, Wood); c.Rect(9, 12, 7, 2, Wood); c.HLine(9, 15, 7, WoodLight); }
            if (up) { c.Rect(7, 0, 2, 5, Wood); c.VLine(7, 0, 4, WoodLight); }
            // Post.
            c.Rect(6, 4, 4, 12, Wood);
            c.VLine(9, 5, 15, WoodDark);
            c.HLine(6, 9, 4, WoodLight);
            c.Set(7, 3, WoodLight); c.Set(8, 3, WoodLight);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawSign()
        {
            var c = new PixelCanvas(16, 18);
            c.Rect(7, 9, 2, 8, WoodDark);
            c.Rect(1, 2, 14, 8, Wood);
            c.HLine(1, 14, 2, WoodLight);
            c.HLine(1, 14, 9, WoodDark);
            c.HLine(3, 12, 5, WoodDark);
            c.HLine(3, 9, 7, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawCrate(bool carrots)
        {
            var c = new PixelCanvas(16, 18);
            if (carrots)
            {
                for (int i = 0; i < 4; i++)
                {
                    int x = 3 + i * 3;
                    c.Rect(x, 1, 1, 3, Leaf);
                    c.Set(x - 1, 2, LeafLight);
                    c.Rect(x - 1, 4, 3, 3, Carrot);
                    c.Set(x - 1, 4, CarrotLight);
                }
            }
            c.Rect(1, 6, 14, 11, Wood);
            c.HLine(1, 14, 6, WoodLight);
            c.HLine(1, 14, 11, WoodDark);
            c.HLine(1, 14, 16, WoodDark);
            c.VLine(1, 6, 16, WoodDark); c.VLine(14, 6, 16, WoodDark);
            c.Line(2, 7, 13, 10, WoodDark);
            c.Line(2, 12, 13, 15, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawHouse()
        {
            var c = new PixelCanvas(52, 52);
            var roof = PixelCanvas.Hex("#3b8fe3");
            var roofLight = PixelCanvas.Hex("#7cc4ff");
            var roofDark = PixelCanvas.Hex("#2a6fbf");
            var trim = PixelCanvas.Hex("#f2ede4");
            var trimRed = PixelCanvas.Hex("#c4553d");
            // Walls.
            c.Rect(6, 28, 40, 20, PixelCanvas.Hex("#d9955a"));
            for (int y = 31; y < 48; y += 3) c.HLine(6, 45, y, PixelCanvas.Hex("#a86a3a"));
            c.VLine(6, 28, 47, WoodDark); c.VLine(45, 28, 47, WoodDark);
            // Door (dark opening like a barn).
            c.Rect(17, 34, 18, 14, PixelCanvas.Hex("#3a2a2a"));
            c.Rect(18, 35, 16, 2, PixelCanvas.Hex("#2a1d1d"));
            c.HLine(16, 35, 33, trim);
            c.VLine(16, 33, 47, trim); c.VLine(35, 33, 47, trim);
            // Foundation.
            c.Rect(5, 48, 42, 2, StoneDark);
            // Roof.
            c.Rect(3, 6, 46, 23, roof);
            c.Rect(2, 26, 48, 3, trim);
            c.HLine(2, 49, 29, trimRed);
            c.Rect(3, 3, 46, 4, trim);
            c.HLine(3, 48, 3, trimRed);
            for (int x = 8; x < 46; x += 5) c.VLine(x, 8, 24, roofLight);
            c.HLine(3, 48, 25, roofDark);
            c.Rect(14, 9, 20, 13, PixelCanvas.Hex("#4aa2f0"));
            for (int x = 16; x < 33; x += 4) c.VLine(x, 10, 20, roofLight);
            // Roof vent box (like the reference's rooftop crate).
            c.Rect(8, 1, 8, 7, StoneDark);
            c.Rect(9, 2, 6, 5, SteelDark);
            c.HLine(9, 14, 2, StoneLight);
            c.Outline(Outline);
            return c.WithPivot(26, 3.5f);
        }

        static PixelCanvas DrawBlueprint()
        {
            var c = new PixelCanvas(52, 52);
            var ghost = PixelCanvas.Hex("#86e0a0", 120);
            var ghostLine = PixelCanvas.Hex("#c8ffd6", 170);
            var bracket = PixelCanvas.Hex("#f2e6c9");
            c.Rect(6, 8, 40, 38, ghost);
            for (int x = 10; x < 44; x += 5) c.VLine(x, 10, 26, ghostLine);
            c.HLine(6, 45, 27, ghostLine);
            for (int y = 31; y < 45; y += 4) c.HLine(8, 43, y, PixelCanvas.WithAlpha(ghostLine, 110));
            c.Rect(20, 34, 12, 12, PixelCanvas.Hex("#5ab87a", 150));
            // Corner brackets.
            c.Rect(2, 4, 6, 2, bracket); c.Rect(2, 4, 2, 6, bracket);
            c.Rect(44, 4, 6, 2, bracket); c.Rect(48, 4, 2, 6, bracket);
            c.Rect(2, 46, 6, 2, bracket); c.Rect(2, 42, 2, 6, bracket);
            c.Rect(44, 46, 6, 2, bracket); c.Rect(48, 42, 2, 6, bracket);
            return c.WithPivot(26, 3.5f);
        }

        static PixelCanvas DrawWorkshop()
        {
            var c = new PixelCanvas(52, 52);
            var roof = PixelCanvas.Hex("#3fae6b");
            var roofLight = PixelCanvas.Hex("#86e0a0");
            var roofDark = PixelCanvas.Hex("#2b8a52");
            var trim = PixelCanvas.Hex("#f2ede4");
            c.Rect(6, 28, 40, 20, PixelCanvas.Hex("#c98a55"));
            for (int y = 31; y < 48; y += 3) c.HLine(6, 45, y, PixelCanvas.Hex("#9a6236"));
            c.Rect(18, 34, 16, 14, PixelCanvas.Hex("#6b3f22"));
            c.VLine(25, 34, 47, PixelCanvas.Hex("#4a2a16"));
            c.Set(23, 41, Gold); c.Set(28, 41, Gold);
            c.Rect(9, 33, 6, 5, PixelCanvas.Hex("#9bd8ff"));
            c.Rect(37, 33, 6, 5, PixelCanvas.Hex("#9bd8ff"));
            c.Rect(5, 48, 42, 2, StoneDark);
            c.Rect(3, 6, 46, 23, roof);
            c.Rect(2, 26, 48, 3, trim);
            for (int x = 8; x < 46; x += 4) c.VLine(x, 7, 24, roofLight);
            c.HLine(3, 48, 25, roofDark);
            c.Rect(3, 3, 46, 4, trim);
            // Hammer sign on the roof.
            c.Rect(22, 12, 8, 3, SteelDark);
            c.Rect(25, 15, 2, 7, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(26, 3.5f);
        }

        static PixelCanvas DrawPile(bool stone)
        {
            var c = new PixelCanvas(18, 12);
            if (stone)
            {
                c.Ellipse(5, 8, 4, 3, Stone); c.Ellipse(12, 8, 4.5f, 3, Stone); c.Ellipse(8.5f, 4.5f, 4, 3, Stone);
                c.PaintEllipse(7.5f, 3.5f, 2, 1.2f, StoneLight);
                c.PaintEllipse(4, 7, 1.5f, 1, StoneLight);
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    int y = 8 - i * 3, x0 = 2 + i * 2, x1 = 15 - i * 2;
                    c.Rect(x0, y, x1 - x0 + 1, 3, Bark);
                    c.HLine(x0, x1, y, WoodLight);
                    c.Rect(x1 - 1, y, 2, 3, WoodLight);
                    c.Set(x1, y + 1, Wood);
                }
            }
            c.Outline(Outline);
            return c.WithPivot(9, 1.5f);
        }

        static PixelCanvas DrawCrop(string state)
        {
            var c = new PixelCanvas(16, 16);
            var hole = PixelCanvas.Hex("#8a5530");
            var holeDark = PixelCanvas.Hex("#5e3a1e");
            c.Ellipse(8, 12, 5, 3, hole);
            c.PaintEllipse(8, 11.5f, 3.5f, 1.8f, holeDark);
            if (state == "carrot")
            {
                c.Rect(5, 7, 6, 5, Carrot);
                c.Rect(6, 12, 4, 1, Carrot);
                c.HLine(5, 10, 7, CarrotLight);
                c.Set(7, 9, PixelCanvas.Shade(Carrot, 0.8f));
                c.Rect(7, 2, 2, 5, Leaf);
                c.Rect(5, 3, 2, 3, LeafLight);
                c.Rect(9, 3, 2, 3, LeafLight);
                c.Set(8, 1, LeafLight);
            }
            else if (state == "sprout")
            {
                c.Rect(7, 8, 2, 3, Leaf);
                c.Set(6, 8, LeafLight); c.Set(9, 8, LeafLight);
            }
            c.Outline(PixelCanvas.WithAlpha(Outline, 200));
            return c.WithPivot(8, 2.5f);
        }

        // ---------- Characters ----------

        /// <summary>
        /// Draws one frame of a chibi character. frame: idle0, idle1, walk0..walk3, attack, hurt.
        /// Side frames face right; the renderer flips them for left.
        /// </summary>
        public static PixelCanvas DrawCharacter(CharacterLook look, string dir, string frame)
        {
            var c = new PixelCanvas(16, 20);
            if (look.body == BodyKind.Skeleton) DrawSkeletonBody(c, look, dir, frame);
            else DrawHumanBody(c, look, dir, frame);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static void FrameInfo(string frame, out int bob, out int step)
        {
            bob = 0;
            step = 0; // -1 left foot forward, +1 right foot forward
            switch (frame)
            {
                case "idle1": bob = 1; break;
                case "walk0": step = -1; break;
                case "walk1": bob = 1; break;
                case "walk2": step = 1; break;
                case "walk3": bob = 1; break;
                case "attack": step = 1; break;
                case "hurt": bob = 1; break;
            }
        }

        static void DrawHumanBody(PixelCanvas c, CharacterLook L, string dir, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            bool side = dir == "side", up = dir == "up";
            var shoe = PixelCanvas.Hex("#4a3226");
            var pantsDark = PixelCanvas.Shade(L.pants, 0.8f);
            var shirtDark = PixelCanvas.Shade(L.shirt, 0.78f);
            var skinDark = PixelCanvas.Shade(L.skin, 0.85f);
            var hairDark = PixelCanvas.Shade(L.hair, 0.78f);

            // Legs (rows 16-18). Walking lifts one foot.
            if (side)
            {
                int back = step == 0 ? 6 : (step < 0 ? 5 : 7);
                int front = step == 0 ? 8 : (step < 0 ? 9 : 7);
                c.Rect(back, 16, 2, 2, pantsDark); c.Rect(back, 18, 2, 1, shoe);
                c.Rect(front, 16, 2, 2, L.pants); c.Rect(front, 18, 3, 1, shoe);
            }
            else
            {
                int lLift = step < 0 ? 1 : 0, rLift = step > 0 ? 1 : 0;
                c.Rect(5, 16, 2, 2 - lLift, L.pants); c.Rect(5, 18 - lLift, 2, 1, shoe);
                c.Rect(9, 16, 2, 2 - rLift, L.pants); c.Rect(9, 18 - rLift, 2, 1, shoe);
                c.Rect(7, 16, 2, 1, pantsDark);
            }

            int o = bob; // body/head vertical offset
            // Body (rows 12-15).
            if (side)
            {
                c.Rect(5, 12 + o, 6, 4, L.shirt);
                c.HLine(5, 10, 15 + o, L.pants);
                c.VLine(5, 12 + o, 14 + o, shirtDark);
                // Arm swings with steps.
                int armX = frame == "attack" ? 10 : 7 + step;
                if (frame == "attack")
                {
                    c.Rect(10, 12 + o, 3, 2, L.shirt);
                    c.Rect(13, 12 + o, 1, 2, L.skin);
                }
                else
                {
                    c.Rect(armX, 12 + o, 2, 2, shirtDark);
                    c.Rect(armX, 14 + o, 2, 1, L.skin);
                }
            }
            else
            {
                c.Rect(4, 12 + o, 8, 4, L.shirt);
                c.HLine(4, 11, 15 + o, L.pants);
                c.HLine(4, 11, 14 + o, shirtDark);
                if (!up) { c.Set(7, 12 + o, shirtDark); c.Set(8, 12 + o, shirtDark); }
                // Arms.
                bool attack = frame == "attack";
                c.Rect(3, 12 + o, 1, 2, L.shirt); c.Set(3, 14 + o, L.skin);
                if (attack && !up) { c.Rect(12, 12 + o, 1, 3, L.shirt); c.Rect(12, 15 + o, 1, 1, L.skin); }
                else if (attack) { c.Rect(12, 10 + o, 1, 3, L.shirt); c.Set(12, 9 + o, L.skin); }
                else { c.Rect(12, 12 + o, 1, 2, L.shirt); c.Set(12, 14 + o, L.skin); }
                // Walk arm swing (one shade darker arm moves).
                if (step != 0 && !attack) { c.Set(step < 0 ? 3 : 12, 15 + o, L.skin); }
            }

            // Head (rows 2-11), 10px wide rounded.
            int hx = 3; // head left edge
            int hy = 2 + o;
            c.HLine(hx + 2, hx + 7, hy, L.skin);
            c.HLine(hx + 1, hx + 8, hy + 1, L.skin);
            c.Rect(hx, hy + 2, 10, 6, L.skin);
            c.HLine(hx + 1, hx + 8, hy + 8, L.skin);
            c.HLine(hx + 2, hx + 7, hy + 9, skinDark);

            // Face.
            if (!up)
            {
                if (side)
                {
                    c.Rect(hx + 7, hy + 5, 1, 2, Eye);
                    c.Set(hx + 9, hy + 6, skinDark);
                    c.Set(hx + 6, hy + 7, PixelCanvas.Hex("#f29a8a"));
                }
                else
                {
                    c.Rect(hx + 2, hy + 5, 1, 2, Eye);
                    c.Rect(hx + 7, hy + 5, 1, 2, Eye);
                    c.Set(hx + 1, hy + 7, PixelCanvas.Hex("#f29a8a"));
                    c.Set(hx + 8, hy + 7, PixelCanvas.Hex("#f29a8a"));
                    if (frame == "hurt") { c.Set(hx + 2, hy + 5, L.skin); c.Set(hx + 7, hy + 5, L.skin); }
                }
            }

            DrawHair(c, L, dir, hx, hy, hairDark);
            DrawHat(c, L, dir, hx, hy);
        }

        static void DrawHair(PixelCanvas c, CharacterLook L, string dir, int hx, int hy, Color32 hairDark)
        {
            var h = L.hair;
            bool up = dir == "up", side = dir == "side";
            switch (L.hairStyle)
            {
                case HairStyle.Bald:
                    if (up) { c.Rect(hx, hy + 4, 10, 4, h); }
                    else if (side) { c.Rect(hx, hy + 3, 3, 4, h); }
                    else
                    {
                        c.Rect(hx, hy + 3, 1, 4, h); c.Rect(hx + 9, hy + 3, 1, 4, h);
                        // Beard for elders.
                        c.Rect(hx + 2, hy + 7, 6, 3, h);
                        c.HLine(hx + 3, hx + 6, hy + 10, h);
                        c.Set(hx + 4, hy + 7, PixelCanvas.Shade(L.skin, 0.8f));
                        c.Set(hx + 5, hy + 7, PixelCanvas.Shade(L.skin, 0.8f));
                    }
                    return;
            }

            // Common cap of hair over the top of the head.
            c.HLine(hx + 2, hx + 7, hy, h);
            c.HLine(hx + 1, hx + 8, hy + 1, h);
            c.Rect(hx, hy + 2, 10, 2, h);
            c.HLine(hx + 1, hx + 8, hy + 1, h);
            c.HLine(hx + 3, hx + 6, hy, PixelCanvas.Shade(h, 1.15f));

            if (up)
            {
                c.Rect(hx, hy + 2, 10, 7, h);
                c.HLine(hx + 1, hx + 8, hy + 8, hairDark);
            }
            else if (side)
            {
                c.Rect(hx, hy + 2, 5, 6, h);
                c.Rect(hx + 5, hy + 2, 5, 2, h);
                c.Set(hx + 8, hy + 4, h); c.Set(hx + 9, hy + 4, h);
                c.VLine(hx + 4, hy + 4, hy + 7, hairDark);
            }
            else
            {
                // Fringe.
                c.Set(hx, hy + 4, h); c.Set(hx + 1, hy + 4, h); c.Set(hx + 4, hy + 4, h);
                c.Set(hx + 5, hy + 4, h); c.Set(hx + 8, hy + 4, h); c.Set(hx + 9, hy + 4, h);
                c.VLine(hx, hy + 5, hy + 6, h);
                c.VLine(hx + 9, hy + 5, hy + 6, h);
                c.HLine(hx + 1, hx + 8, hy + 3, hairDark);
            }

            switch (L.hairStyle)
            {
                case HairStyle.Spiky:
                    c.Set(hx + 1, hy - 1, h); c.Set(hx + 4, hy - 1, h); c.Set(hx + 7, hy - 1, h);
                    c.Set(hx + 5, hy - 2, h);
                    if (!up && !side) { c.Set(hx + 2, hy + 5, h); c.Set(hx + 7, hy + 5, h); }
                    break;
                case HairStyle.Long:
                    if (side) c.Rect(hx - 1, hy + 3, 3, 8, h);
                    else { c.Rect(hx - 1, hy + 4, 2, 7, h); c.Rect(hx + 9, hy + 4, 2, 7, h); }
                    if (up) c.Rect(hx, hy + 8, 10, 3, h);
                    break;
                case HairStyle.Bun:
                    c.Ellipse(hx + 5, hy - 1, 2.5f, 2f, h);
                    if (side) c.Ellipse(hx + 1, hy + 1, 2.2f, 2f, h);
                    break;
                case HairStyle.Curly:
                    c.Set(hx - 1, hy + 2, h); c.Set(hx + 10, hy + 2, h);
                    c.Set(hx + 2, hy - 1, h); c.Set(hx + 5, hy - 1, h); c.Set(hx + 8, hy - 1, h);
                    if (!side) { c.VLine(hx - 1, hy + 3, hy + 6, h); c.VLine(hx + 10, hy + 3, hy + 6, h); }
                    else c.VLine(hx - 1, hy + 3, hy + 7, h);
                    break;
            }
        }

        static void DrawHat(PixelCanvas c, CharacterLook L, string dir, int hx, int hy)
        {
            var col = L.hatColor;
            var dark = PixelCanvas.Shade(col, 0.78f);
            switch (L.hat)
            {
                case HatKind.Straw:
                    c.Rect(hx - 2, hy + 2, 14, 2, col);
                    c.HLine(hx - 2, hx + 11, hy + 3, dark);
                    c.Rect(hx + 1, hy - 1, 8, 3, col);
                    c.HLine(hx + 1, hx + 8, hy + 1, PixelCanvas.Hex("#c4553d"));
                    break;
                case HatKind.Cap:
                    c.Rect(hx, hy - 1, 10, 4, col);
                    if (dir == "side") c.Rect(hx + 8, hy + 2, 4, 1, dark);
                    else if (dir == "down") c.HLine(hx + 1, hx + 8, hy + 3, dark);
                    break;
                case HatKind.Bandana:
                    c.Rect(hx, hy + 1, 10, 2, col);
                    c.HLine(hx, hx + 9, hy + 2, dark);
                    if (dir != "down") { c.Set(hx - 1, hy + 3, col); c.Set(hx - 1, hy + 4, dark); }
                    break;
            }
        }

        static void DrawSkeletonBody(PixelCanvas c, CharacterLook L, string dir, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            var bone = L.skin;
            var boneDark = PixelCanvas.Shade(bone, 0.78f);
            var socket = PixelCanvas.Hex("#2b1d16");
            bool side = dir == "side", up = dir == "up";
            int o = bob;

            // Legs.
            if (side)
            {
                int back = step == 0 ? 7 : (step < 0 ? 6 : 8);
                int front = step == 0 ? 8 : (step < 0 ? 9 : 7);
                c.VLine(back, 16, 18, boneDark);
                c.VLine(front, 16, 18, bone); c.Set(front + 1, 18, bone);
            }
            else
            {
                c.VLine(6, 16, 18 - (step < 0 ? 1 : 0), bone); c.Set(5, 18 - (step < 0 ? 1 : 0), bone);
                c.VLine(9, 16, 18 - (step > 0 ? 1 : 0), bone); c.Set(10, 18 - (step > 0 ? 1 : 0), bone);
            }
            // Pelvis + spine + ribs.
            c.HLine(6, 9, 15 + o, bone);
            c.VLine(7, 11 + o, 15 + o, bone);
            c.VLine(8, 11 + o, 15 + o, boneDark);
            if (!side)
            {
                c.HLine(5, 10, 12 + o, bone);
                c.HLine(5, 10, 14 + o, boneDark);
                c.Set(5, 13 + o, bone); c.Set(10, 13 + o, bone);
                // Arms.
                bool attack = frame == "attack";
                c.VLine(4, 12 + o, 15 + o, bone);
                if (attack) c.VLine(11, 9 + o, 12 + o, bone); else c.VLine(11, 12 + o, 15 + o, bone);
            }
            else
            {
                c.HLine(6, 9, 12 + o, bone);
                c.HLine(6, 9, 14 + o, boneDark);
                int armX = frame == "attack" ? 10 : 8 + step;
                if (frame == "attack") c.HLine(9, 12, 12 + o, bone); else c.VLine(armX, 12 + o, 15 + o, bone);
            }

            // Skull (rows 2-10).
            int hx = 4, hy = 3 + o;
            c.HLine(hx + 2, hx + 5, hy, bone);
            c.Rect(hx + 1, hy + 1, 6, 1, bone);
            c.Rect(hx, hy + 2, 8, 4, bone);
            c.Rect(hx + 1, hy + 6, 6, 2, bone);
            c.HLine(hx + 2, hx + 5, hy + 8, boneDark);
            if (!up)
            {
                if (side)
                {
                    c.Rect(hx + 4, hy + 3, 2, 2, socket);
                    c.Set(hx + 7, hy + 5, socket);
                    c.Set(hx + 5, hy + 7, socket);
                }
                else
                {
                    c.Rect(hx + 1, hy + 3, 2, 2, socket);
                    c.Rect(hx + 5, hy + 3, 2, 2, socket);
                    c.Set(hx + 3, hy + 5, socket); c.Set(hx + 4, hy + 5, socket);
                    c.Set(hx + 2, hy + 7, socket); c.Set(hx + 4, hy + 7, socket);
                    if (frame == "attack" || frame == "hurt")
                    {
                        c.Set(hx + 2, hy + 4, Red);
                        c.Set(hx + 6, hy + 4, Red);
                    }
                }
            }
            else
            {
                c.HLine(hx + 1, hx + 6, hy + 5, boneDark);
            }
        }

        // ---------- Tools & FX ----------

        static PixelCanvas DrawTool(string kind)
        {
            var c = new PixelCanvas(16, 16);
            var handle = PixelCanvas.Hex("#6b4226");
            switch (kind)
            {
                case "sword":
                    c.Rect(7, 1, 2, 10, Steel);
                    c.VLine(8, 2, 10, SteelDark);
                    c.Set(7, 0, Steel);
                    c.Rect(5, 11, 6, 1, Gold);
                    c.Rect(7, 12, 2, 3, handle);
                    c.Set(7, 15, Gold); c.Set(8, 15, Gold);
                    break;
                case "axe":
                    c.Rect(7, 3, 2, 12, handle);
                    c.Rect(3, 2, 5, 5, Steel);
                    c.VLine(3, 2, 6, SteelDark);
                    c.HLine(4, 7, 6, SteelDark);
                    break;
                case "pickaxe":
                    c.Rect(7, 3, 2, 12, handle);
                    c.HLine(3, 12, 3, Steel);
                    c.HLine(4, 11, 2, Steel);
                    c.Set(2, 4, SteelDark); c.Set(13, 4, SteelDark);
                    break;
                case "can":
                    c.Rect(4, 6, 8, 7, PixelCanvas.Hex("#4a90d9"));
                    c.HLine(4, 11, 6, PixelCanvas.Hex("#8cc4ff"));
                    c.Line(12, 8, 15, 5, PixelCanvas.Hex("#4a90d9"));
                    c.Rect(5, 3, 6, 1, SteelDark); c.VLine(5, 3, 5, SteelDark); c.VLine(10, 3, 5, SteelDark);
                    break;
                case "rod":
                    c.Line(8, 15, 13, 0, handle);
                    c.Set(10, 9, Red);
                    break;
                case "hammer":
                    c.Rect(7, 5, 2, 10, handle);
                    c.Rect(4, 2, 8, 4, SteelDark);
                    c.HLine(4, 11, 2, Steel);
                    break;
                case "crate":
                    return DrawCrate(true);
                default:
                    return null;
            }
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawFx(string kind)
        {
            switch (kind)
            {
                case "slash":
                {
                    // Crescent arc facing right; rotated at runtime.
                    var c = new PixelCanvas(20, 24);
                    c.Ellipse(8, 12, 11, 11, PixelCanvas.WithAlpha(White, 230));
                    // Carve out the inside to make a crescent.
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 20; x++)
                        {
                            float nx = (x + 0.5f - 4f) / 10f, ny = (y + 0.5f - 12f) / 10.5f;
                            if (nx * nx + ny * ny <= 1f) c.Pixels[y * 20 + x] = PixelCanvas.Clear;
                        }
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 20; x++)
                        {
                            var p = c.Pixels[y * 20 + x];
                            if (p.a > 0 && Math.Abs(y - 12) > 7) c.Pixels[y * 20 + x] = PixelCanvas.WithAlpha(Yellow, 200);
                        }
                    return c.WithPivot(4, 12);
                }
                case "sparkle":
                {
                    var c = new PixelCanvas(11, 11);
                    c.VLine(5, 0, 10, Yellow); c.HLine(0, 10, 5, Yellow);
                    c.Rect(4, 3, 3, 5, Yellow); c.Rect(3, 4, 5, 3, Yellow);
                    c.Set(5, 5, White); c.Set(5, 4, White); c.Set(4, 5, White);
                    c.Set(2, 2, Yellow); c.Set(8, 2, Yellow); c.Set(2, 8, Yellow); c.Set(8, 8, Yellow);
                    return c;
                }
                case "dust":
                {
                    var c = new PixelCanvas(6, 6);
                    c.Circle(3, 3, 2.6f, PixelCanvas.Hex("#fffaf0", 220));
                    c.PaintEllipse(3.6f, 3.8f, 1.6f, 1.2f, PixelCanvas.Hex("#e0d8c8", 220));
                    return c;
                }
                case "leaf":
                {
                    var c = new PixelCanvas(3, 3);
                    c.Set(1, 0, LeafLight); c.Set(0, 1, Leaf); c.Set(1, 1, LeafLight); c.Set(2, 1, Leaf); c.Set(1, 2, LeafDark);
                    return c;
                }
                case "chip":
                {
                    var c = new PixelCanvas(3, 3);
                    c.Rect(0, 0, 2, 2, Stone); c.Set(1, 1, StoneDark); c.Set(2, 2, StoneDark);
                    return c;
                }
                case "bone":
                {
                    var c = new PixelCanvas(6, 3);
                    c.HLine(1, 4, 1, PixelCanvas.Hex("#f0e8d4"));
                    c.Set(0, 0, PixelCanvas.Hex("#f0e8d4")); c.Set(0, 2, PixelCanvas.Hex("#f0e8d4"));
                    c.Set(5, 0, PixelCanvas.Hex("#f0e8d4")); c.Set(5, 2, PixelCanvas.Hex("#f0e8d4"));
                    return c;
                }
                case "water":
                {
                    var c = new PixelCanvas(2, 3);
                    c.Rect(0, 0, 2, 3, PixelCanvas.Hex("#6fc0ff"));
                    c.Set(0, 0, White);
                    return c;
                }
                case "alert":
                {
                    var c = new PixelCanvas(5, 9);
                    c.Rect(1, 0, 3, 5, Red); c.Rect(1, 6, 3, 2, Red);
                    c.VLine(2, 1, 3, RedLight);
                    c.Outline(Outline);
                    return c.WithBottomPivot();
                }
            }
            return null;
        }

        static PixelCanvas DrawShadow()
        {
            var c = new PixelCanvas(12, 5);
            c.Ellipse(6, 2.5f, 5.5f, 2.2f, new Color32(20, 40, 20, 70));
            return c;
        }

        // ---------- UI ----------

        static PixelCanvas DrawIcon(string kind)
        {
            var c = new PixelCanvas(12, 12);
            switch (kind)
            {
                case "wood":
                    c.Rect(1, 4, 9, 5, Bark);
                    c.HLine(1, 9, 4, WoodLight);
                    c.HLine(1, 9, 8, BarkDark);
                    c.Ellipse(10, 6.5f, 1.8f, 2.6f, WoodLight);
                    c.Set(10, 6, Wood);
                    break;
                case "stone":
                    c.Ellipse(6, 7, 5, 4, Stone);
                    c.PaintEllipse(4.5f, 5.5f, 2.5f, 1.8f, StoneLight);
                    c.Paint(8, 9, StoneDark); c.Paint(9, 8, StoneDark);
                    break;
                case "carrot":
                    c.Line(3, 10, 8, 4, Carrot);
                    c.Line(4, 10, 9, 5, Carrot);
                    c.Line(3, 9, 7, 4, Carrot);
                    c.Line(4, 9, 8, 5, CarrotLight);
                    c.Rect(8, 1, 1, 3, Leaf); c.Rect(9, 2, 2, 1, LeafLight); c.Set(10, 1, Leaf);
                    break;
                default:
                    c.Rect(3, 3, 6, 6, Red);
                    break;
            }
            c.Outline(Outline);
            return c;
        }

        static PixelCanvas DrawHeart(string kind)
        {
            var c = new PixelCanvas(11, 10);
            var empty = PixelCanvas.Hex("#5a2d35");
            // Heart silhouette.
            string[] shape =
            {
                ".xx...xx.",
                "xxxx.xxxx",
                "xxxxxxxxx",
                "xxxxxxxxx",
                ".xxxxxxx.",
                "..xxxxx..",
                "...xxx...",
                "....x....",
            };
            for (int y = 0; y < shape.Length; y++)
                for (int x = 0; x < shape[y].Length; x++)
                {
                    if (shape[y][x] != 'x') continue;
                    bool filled = kind == "full" || (kind == "half" && x < 5) ;
                    c.Set(x + 1, y + 1, filled ? Red : empty);
                }
            if (kind != "empty") { c.Set(2, 2, RedLight); c.Set(3, 2, RedLight); c.Set(2, 3, RedLight); }
            c.Outline(Outline);
            return c;
        }

        static PixelCanvas DrawUi(string kind)
        {
            switch (kind)
            {
                case "panel":
                {
                    // Cream parchment with a brown frame (9-slice).
                    var c = new PixelCanvas(16, 16);
                    var fill = PixelCanvas.Hex("#f6e7c8");
                    var frame = PixelCanvas.Hex("#6b3f22");
                    var frameLight = PixelCanvas.Hex("#b87b45");
                    c.Rect(1, 1, 14, 14, frame);
                    c.Rect(0, 2, 16, 12, frame);
                    c.Rect(2, 0, 12, 16, frame);
                    c.Rect(2, 2, 12, 12, frameLight);
                    c.Rect(3, 3, 10, 10, fill);
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 5;
                    return c;
                }
                case "dark":
                {
                    var c = new PixelCanvas(16, 16);
                    var fill = PixelCanvas.Hex("#1d1a26", 225);
                    var frame = PixelCanvas.Hex("#f6e7c8", 235);
                    c.Rect(1, 1, 14, 14, frame);
                    c.Rect(0, 2, 16, 12, frame);
                    c.Rect(2, 0, 12, 16, frame);
                    for (int y = 2; y < 14; y++)
                        for (int x = 2; x < 14; x++)
                            c.Pixels[y * 16 + x] = fill;
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 5;
                    return c;
                }
                case "select":
                {
                    var c = new PixelCanvas(8, 8);
                    c.Rect(0, 0, 8, 8, PixelCanvas.Hex("#ffd34a", 110));
                    c.Rect(1, 1, 6, 6, PixelCanvas.Hex("#ffe89a", 70));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 2;
                    return c;
                }
                case "white":
                {
                    var c = new PixelCanvas(4, 4);
                    c.Rect(0, 0, 4, 4, White);
                    return c;
                }
            }
            return null;
        }
    }
}
