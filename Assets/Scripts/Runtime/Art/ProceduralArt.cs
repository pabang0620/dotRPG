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
    public static partial class ProceduralArt
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

        // Canyon palette (second map): warm flagstone, dark brown cliffs, teal water.
        static readonly Color32 Flag = PixelCanvas.Hex("#c9a77c");
        static readonly Color32 FlagLight = PixelCanvas.Hex("#dcbd93");
        static readonly Color32 FlagGrout = PixelCanvas.Hex("#9c7d58");
        static readonly Color32 FlagSpot = PixelCanvas.Hex("#b8966c");
        static readonly Color32 Cliff = PixelCanvas.Hex("#5a463c");
        static readonly Color32 CliffLight = PixelCanvas.Hex("#77604f");
        static readonly Color32 CliffDark = PixelCanvas.Hex("#3d2f29");
        static readonly Color32 CliffDeep = PixelCanvas.Hex("#281e1a");
        static readonly Color32 CliffRim = PixelCanvas.Hex("#a88b68");
        static readonly Color32 Teal = PixelCanvas.Hex("#2f9fa6");
        static readonly Color32 TealDark = PixelCanvas.Hex("#237e87");
        static readonly Color32 TealLight = PixelCanvas.Hex("#63d1cb");
        static readonly Color32 TealFoam = PixelCanvas.Hex("#c4f4ec");
        static readonly Color32 Ledge = PixelCanvas.Hex("#8c7053");
        static readonly Color32 LedgeLight = PixelCanvas.Hex("#b0906a");
        static readonly Color32 MossA = PixelCanvas.Hex("#4f9a45");
        static readonly Color32 MossB = PixelCanvas.Hex("#4a9241");
        static readonly Color32 Magic = PixelCanvas.Hex("#9b7bff");
        static readonly Color32 MagicLight = PixelCanvas.Hex("#d9ccff");
        static readonly Color32 MagicCore = PixelCanvas.Hex("#6fe7ff");

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
            // Canyon map.
            for (int v = 0; v < 4; v++)
            {
                yield return $"tile_flag_{v}";
                yield return $"tile_flag_shade_{v}";
                yield return $"tile_cwater_{v}";
                yield return $"tile_cwater_edge_{v}";
            }
            for (int v = 0; v < 2; v++) yield return $"tile_mossgrass_{v}";
            foreach (int mask in new[] { 0, 1, 4, 5 })
                for (int v = 0; v < 3; v++) yield return $"tile_cliff_{mask}_{v}";
            foreach (var k in new[] { "tile_stairs", "deco_moss", "house_red", "house_green", "barrel", "box" }) yield return k;
            foreach (var k in new[] { "arrow_left", "arrow_right", "arrow_up", "arrow_down" }) yield return k;
            // Mage.
            foreach (var k in new[] { "tool_staff", "fx_bolt", "fx_magic" }) yield return k;
            // Held weapons (one per item tier).
            for (int t = 0; t < 4; t++) { yield return $"wpn_sword_{t}"; yield return $"wpn_staff_{t}"; }
            // Skill effects.
            foreach (var k in new[] { "fx_ring", "fx_glow", "fx_shock", "fx_rune", "fx_swoosh", "fx_blade", "fx_zap", "fx_frost", "fx_crack", "fx_spike", "fx_ice", "fx_shard", "fx_snow", "fx_spark", "fx_streak", "fx_cut" })
                yield return k;
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
                    case "house": return DrawHouse(parts.Length > 1 ? parts[1] : "blue");
                    case "barrel": return DrawBarrel();
                    case "inn": return DrawInn();
                    case "stall": return DrawStall();
                    case "well": return DrawWell();
                    case "bench": return DrawBench();
                    case "chest": return DrawChest(parts.Length > 1 && parts[1] == "open");
                    case "gate": return DrawGate();
                    case "maticon": return DrawMaterialIcon(parts[1]);
                    case "menuicon" when parts.Length > 1 && parts[1] == "party": return DrawPartyMenuIcon(); // [PARTY]
                    case "menuicon": return DrawMenuIcon(parts[1]);
                    case "anvil": return DrawAnvil();
                    case "gem": return DrawGem(key.Substring(4));
                    case "node": return DrawNodeGlyph(parts[1]);
                    case "wpn": return DrawWeapon(parts[1], parts.Length > 2 ? int.Parse(parts[2]) : 0);
                    case "snow": return DrawSnow(parts);
                    case "town": return DrawTown(parts);
                    case "num": return DrawDigit(int.Parse(parts[1]));
                    case "eqicon": return DrawEquipIcon(parts[1], parts.Length > 2 ? int.Parse(parts[2]) : 0);
                    case "hpbar": return DrawHpBar(parts[1]);
                    case "box": return DrawCrate(false);
                    case "arrow": return DrawArrow(parts.Length > 1 ? parts[1] : "right");
                    case "site": return parts[1] == "built" ? DrawWorkshop() : DrawBlueprint();
                    case "pile": return DrawPile(parts[1] == "stone");
                    case "crop": return DrawCrop(parts[1]);
                    case "tool": return DrawTool(parts[1]);
                    case "fx": return DrawFx(parts[1]);
                    case "icon": return DrawIcon(key.Substring(5));
                    case "heart": return DrawHeart(parts[1]);
                    case "ui": return DrawUi(parts[1]);
                    case "shadow": return DrawShadow();
                    case "dgn": return DrawDungeon(parts); // [DUNGEON] gates, room-map skull, reward cards (ProceduralArtDungeon.cs)
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
                case "grassedge": return DrawGrassEdge(int.Parse(p[2]));
                case "moss":
                {
                    var c = new PixelCanvas(8, 5);
                    c.Ellipse(4, 3, 3.5f, 1.8f, PixelCanvas.WithAlpha(MossA, 220));
                    c.PaintEllipse(3.5f, 2.5f, 1.8f, 1f, PixelCanvas.WithAlpha(GrassLight, 200));
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

        static PixelCanvas DrawHouse(string variant)
        {
            var c = new PixelCanvas(52, 52);
            var roof = PixelCanvas.Hex("#3b8fe3");
            var roofLight = PixelCanvas.Hex("#7cc4ff");
            var roofDark = PixelCanvas.Hex("#2a6fbf");
            var roofPanel = PixelCanvas.Hex("#4aa2f0");
            if (variant == "red")
            {
                roof = PixelCanvas.Hex("#c0392b"); roofLight = PixelCanvas.Hex("#e8674f");
                roofDark = PixelCanvas.Hex("#8e2a20"); roofPanel = PixelCanvas.Hex("#d24a36");
            }
            else if (variant == "green")
            {
                roof = PixelCanvas.Hex("#3f8f4f"); roofLight = PixelCanvas.Hex("#6cc070");
                roofDark = PixelCanvas.Hex("#2a6a38"); roofPanel = PixelCanvas.Hex("#4ea45c");
            }
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
            c.Rect(14, 9, 20, 13, roofPanel);
            for (int x = 16; x < 33; x += 4) c.VLine(x, 10, 20, roofLight);
            // Roof vent box (like the reference's rooftop crate).
            c.Rect(8, 1, 8, 7, StoneDark);
            c.Rect(9, 2, 6, 5, SteelDark);
            c.HLine(9, 14, 2, StoneLight);
            c.Outline(Outline);
            return c.WithPivot(26, 3.5f);
        }

        static readonly string[] DigitShapes =
        {
            "xxx|x.x|x.x|x.x|xxx", ".x.|xx.|.x.|.x.|xxx", "xxx|..x|xxx|x..|xxx", "xxx|..x|.xx|..x|xxx", "x.x|x.x|xxx|..x|..x",
            "xxx|x..|xxx|..x|xxx", "xxx|x..|xxx|x.x|xxx", "xxx|..x|.x.|.x.|.x.", "xxx|x.x|xxx|x.x|xxx", "xxx|x.x|xxx|..x|xxx",
        };

        /// <summary>3x5 pixel digit in white (tinted at runtime) with a dark outline, for damage numbers.</summary>
        static PixelCanvas DrawDigit(int digit)
        {
            var c = new PixelCanvas(5, 7);
            var rows = DigitShapes[Mathf.Clamp(digit, 0, 9)].Split('|');
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < 3; x++)
                    if (rows[y][x] == 'x') c.Set(x + 1, y + 1, White);
            c.Outline(Outline);
            return c;
        }

        /// <summary>Monster health bar: "bg" = dark frame, "fill" = white strip tinted by the bar (left pivot).</summary>
        static PixelCanvas DrawHpBar(string part)
        {
            if (part == "bg")
            {
                var c = new PixelCanvas(16, 4);
                c.Rect(0, 0, 16, 4, Outline);
                c.Rect(1, 1, 14, 2, PixelCanvas.Hex("#4a2a2a"));
                return c;
            }
            var fill = new PixelCanvas(14, 2);
            fill.Rect(0, 0, 14, 2, White);
            fill.HLine(0, 13, 1, PixelCanvas.Hex("#d8d8d8"));
            return fill.WithPivot(0, 1);
        }

        /// <summary>16x16 bag icon. kind: sword, staff, neck, ring, top, bot. tier 0/1/2 = common/rare/epic look.</summary>
        static PixelCanvas DrawEquipIcon(string kind, int tier)
        {
            var c = new PixelCanvas(16, 16);
            var handle = PixelCanvas.Hex("#6b4226");
            var bone = PixelCanvas.Hex("#f0e8d4");
            switch (kind)
            {
                case "sword":
                {
                    var blade = tier == 0 ? WoodLight : tier == 1 ? Steel : tier == 2 ? bone : PixelCanvas.Hex("#ffe08a");
                    var edge = tier == 0 ? Wood : tier == 1 ? SteelDark : tier == 2 ? Gold : Red;
                    c.Line(3, 12, 11, 4, blade); c.Line(4, 12, 12, 4, blade); c.Line(4, 11, 11, 4, blade);
                    c.Line(5, 12, 12, 5, edge);
                    c.Set(12, 3, blade); c.Set(13, 3, blade);
                    c.Line(2, 10, 6, 14, tier == 2 ? Red : Gold);
                    c.Line(1, 14, 3, 12, handle); c.Set(1, 15, Gold);
                    break;
                }
                case "staff":
                {
                    var orb = tier == 0 ? LeafLight : tier == 1 ? MagicCore : tier == 2 ? PixelCanvas.Hex("#fff3b0") : PixelCanvas.Hex("#ffd84a");
                    c.Line(3, 14, 10, 6, handle); c.Line(4, 14, 11, 6, BarkDark);
                    c.Circle(11.5f, 4.5f, 3.2f, tier >= 2 ? Gold : Magic);
                    c.Circle(11.5f, 4.5f, 2f, orb);
                    c.Set(11, 3, White);
                    if (tier >= 3) { c.Set(11, 0, Yellow); c.Set(15, 4, Yellow); c.Set(8, 4, Yellow); c.Set(11, 8, Yellow); }
                    break;
                }
                case "neck":
                {
                    var chain = tier == 0 ? LeafDark : tier == 1 ? StoneLight : Gold;
                    for (int i = 0; i <= 10; i++)
                    {
                        float a = Mathf.PI * i / 10f;
                        c.Set(Mathf.RoundToInt(8 + Mathf.Cos(a) * 6), Mathf.RoundToInt(3 + Mathf.Sin(a) * 6), chain);
                    }
                    if (tier == 0) { c.Ellipse(8, 11.5f, 2.5f, 3f, Leaf); c.VLine(8, 10, 13, LeafShine); }
                    else if (tier == 1) { c.Rect(7, 9, 2, 5, bone); c.Rect(6, 9, 4, 1, bone); c.Rect(6, 13, 4, 1, bone); }
                    else { c.Ellipse(8, 11.5f, 3f, 3f, Gold); c.Ellipse(8, 11.5f, 1.8f, 1.8f, Red); c.Set(7, 10, White); }
                    break;
                }
                case "ring":
                {
                    var band = tier == 0 ? PixelCanvas.Hex("#c87a4a") : tier == 1 ? PixelCanvas.Hex("#bfe8f0") : Gold;
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            float d = Mathf.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 9f) * (y - 9f));
                            if (d <= 5.5f && d >= 3.5f) c.Set(x, y, band);
                        }
                    var gem = tier == 0 ? PixelCanvas.Hex("#e0a06a") : tier == 1 ? PixelCanvas.Hex("#6fe7ff") : Red;
                    c.Rect(6, 2, 4, 3, gem); c.Set(6, 2, White);
                    break;
                }
                case "top":
                {
                    var cloth = tier == 0 ? PixelCanvas.Hex("#d9c7a0") : tier == 1 ? PixelCanvas.Hex("#a0643a") : Steel;
                    var dark = PixelCanvas.Shade(cloth, 0.75f);
                    c.Rect(4, 3, 8, 11, cloth);
                    c.Rect(1, 3, 3, 6, cloth); c.Rect(12, 3, 3, 6, cloth);
                    c.Rect(6, 2, 4, 2, PixelCanvas.Clear);
                    c.VLine(8, 4, 13, dark); c.HLine(4, 11, 13, dark);
                    if (tier == 2) { c.HLine(4, 11, 7, SteelDark); c.Set(8, 6, Gold); }
                    break;
                }
                default: // bottoms
                {
                    var cloth = tier == 0 ? PixelCanvas.Hex("#8aa0c8") : PixelCanvas.Hex("#7a4a2a");
                    var dark = PixelCanvas.Shade(cloth, 0.75f);
                    c.Rect(3, 2, 10, 4, cloth);
                    c.Rect(3, 6, 4, 8, cloth); c.Rect(9, 6, 4, 8, cloth);
                    c.HLine(3, 12, 3, dark);
                    c.VLine(8, 4, 6, dark);
                    break;
                }
            }
            c.Outline(Outline);
            return c;
        }

        static PixelCanvas DrawBarrel()
        {
            var c = new PixelCanvas(12, 15);
            c.Rect(2, 1, 8, 13, Wood);
            c.VLine(1, 3, 11, Wood); c.VLine(10, 3, 11, Wood);
            c.VLine(3, 2, 12, WoodLight);
            c.VLine(8, 2, 12, WoodDark);
            c.HLine(1, 10, 3, SteelDark); c.HLine(1, 10, 10, SteelDark);
            c.Ellipse(6, 1.5f, 4, 1.4f, WoodDark);
            c.PaintEllipse(6, 1.5f, 2.5f, 0.8f, Bark);
            c.Outline(Outline);
            return c.WithPivot(6, 1.5f);
        }

        /// <summary>Glowing ground arrow that marks a map exit.</summary>
        static PixelCanvas DrawArrow(string dir)
        {
            var c = new PixelCanvas(14, 14);
            string[] shape =
            {
                "......x.......",
                "......xx......",
                "......xxx.....",
                "xxxxxxxxxx....",
                "xxxxxxxxxxx...",
                "xxxxxxxxxxxx..",
                "xxxxxxxxxxx...",
                "xxxxxxxxxx....",
                "......xxx.....",
                "......xx......",
                "......x.......",
            };
            for (int y = 0; y < shape.Length; y++)
                for (int x = 0; x < shape[y].Length; x++)
                {
                    if (shape[y][x] != 'x') continue;
                    int px = x, py = y + 1;
                    switch (dir)
                    {
                        case "left": px = 13 - x; break;
                        case "up": px = y + 1; py = 13 - x; break;
                        case "down": px = y + 1; py = x; break;
                    }
                    c.Set(px, py, PixelCanvas.WithAlpha(Yellow, 210));
                }
            c.Outline(PixelCanvas.WithAlpha(Outline, 200));
            return c;
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
                case "staff":
                    c.Rect(7, 5, 2, 11, handle);
                    c.VLine(8, 6, 15, BarkDark);
                    c.Set(6, 5, Gold); c.Set(9, 5, Gold);
                    c.Set(5, 3, Gold); c.Set(10, 3, Gold);
                    c.Circle(7.5f, 2.5f, 2.5f, Magic);
                    c.Circle(7.5f, 2.5f, 1.5f, MagicCore);
                    c.Set(7, 1, White);
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
                case "bolt":
                {
                    var c = new PixelCanvas(12, 12);
                    c.Circle(6, 6, 5.5f, PixelCanvas.WithAlpha(Magic, 140));
                    c.Circle(6, 6, 4f, Magic);
                    c.Circle(6, 6, 2.8f, MagicLight);
                    c.Circle(6, 6, 1.5f, White);
                    c.Set(3, 2, MagicCore); c.Set(9, 3, MagicCore); c.Set(2, 8, MagicCore);
                    return c;
                }
                case "magic":
                {
                    var c = new PixelCanvas(5, 5);
                    c.VLine(2, 0, 4, Magic); c.HLine(0, 4, 2, Magic);
                    c.Set(2, 2, MagicLight);
                    return c;
                }
                case "ring":
                {
                    var c = new PixelCanvas(32, 32);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
                            if (d <= 15.5f && d >= 12.5f) c.Set(x, y, White);
                            else if (d < 12.5f && d >= 11f) c.Set(x, y, PixelCanvas.WithAlpha(White, 110));
                            else if (d < 11f) c.Set(x, y, PixelCanvas.WithAlpha(White, 30));
                        }
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
            return DrawSkillFx(kind);
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
            var item16 = DrawItemIcon16(kind);
            if (item16 != null) return item16;
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
                case "circle":
                {
                    // Solid disc used as the minimap's round mask.
                    var c = new PixelCanvas(64, 64);
                    c.Circle(31.5f, 31.5f, 32f, White);
                    return c;
                }
                case "ring":
                {
                    // Minimap frame: brown wood ring with a cream inner edge; centre transparent.
                    var c = new PixelCanvas(64, 64);
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
                            if (d > 32f || d < 28f) continue;
                            c.Set(x, y, d > 31f ? Outline : d > 29.5f ? PixelCanvas.Hex("#8e5a32") : PixelCanvas.Hex("#f6e7c8"));
                        }
                    return c;
                }
                case "dot":
                {
                    var c = new PixelCanvas(7, 7);
                    c.Circle(3f, 3f, 3.5f, Outline);
                    c.Circle(3f, 3f, 2.4f, White);
                    return c;
                }
                case "slot":
                {
                    // Bag cell: dark steel square with a lit top edge (9-slice).
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, PixelCanvas.Hex("#15181f"));
                    c.Rect(1, 1, 14, 14, PixelCanvas.Hex("#3b404b"));
                    c.Rect(2, 3, 12, 11, PixelCanvas.Hex("#454b58"));
                    c.HLine(1, 14, 1, PixelCanvas.Hex("#5c6373"));
                    c.HLine(1, 14, 14, PixelCanvas.Hex("#2a2e37"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
                case "slotblue":
                {
                    // Equipped slot: blue-tinted cell like the character panel's slots.
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, PixelCanvas.Hex("#0f1826"));
                    c.Rect(1, 1, 14, 14, PixelCanvas.Hex("#27456b"));
                    for (int y = 2; y < 14; y++) c.HLine(2, 13, y, PixelCanvas.Shade(PixelCanvas.Hex("#335a88"), 1.1f - y * 0.03f));
                    c.HLine(1, 14, 1, PixelCanvas.Hex("#5f8ec2"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
                case "frame":
                {
                    // Hollow frame, tinted per rarity / used as the cursor.
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, White);
                    c.Rect(2, 2, 12, 12, PixelCanvas.Clear);
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
                case "corner":
                {
                    // Green corner triangle = "better than what you wear".
                    var c = new PixelCanvas(8, 8);
                    for (int y = 0; y < 8; y++) c.HLine(0, 7 - y, y, PixelCanvas.Hex("#5ee04a"));
                    return c;
                }
                case "btn":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, PixelCanvas.Hex("#0d1420"));
                    for (int y = 1; y < 15; y++) c.HLine(1, 14, y, Color32.Lerp(PixelCanvas.Hex("#3f7fc7"), PixelCanvas.Hex("#1d3f6e"), (y - 1) / 13f));
                    c.HLine(1, 14, 1, PixelCanvas.Hex("#7fb9ff"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
                case "btngray":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, PixelCanvas.Hex("#0d1016"));
                    for (int y = 1; y < 15; y++) c.HLine(1, 14, y, Color32.Lerp(PixelCanvas.Hex("#4b5260"), PixelCanvas.Hex("#2b3039"), (y - 1) / 13f));
                    c.HLine(1, 14, 1, PixelCanvas.Hex("#7a8394"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
                case "tooltip":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, PixelCanvas.Hex("#c9b27a"));
                    c.Rect(1, 1, 14, 14, PixelCanvas.Hex("#10141c", 245));
                    c.HLine(2, 13, 2, PixelCanvas.Hex("#2a3242", 245));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
            }
            return null;
        }
    }
}
