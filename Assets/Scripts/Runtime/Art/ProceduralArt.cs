using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Code-drawn placeholder pixel art in the spirit of the reference (bright 16px top-down,
    /// dark outlines, chibi characters). Nothing here is copied from the reference; every sprite is
    /// generated from simple shapes so the project is free of licensing questions.
    ///
    /// Replace any sprite by placing a PNG at Resources/Art/{key}.png - see <see cref="SpriteLibrary"/>.
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
                    case "menuicon" when parts.Length > 1 && parts[1] == "party": return DrawPartyMenuIcon(); // [PARTY]
                    case "maticon":
                    case "menuicon":
                    case "gem":
                    case "node":
                    case "eqicon":
                    case "icon":
                    case "heart":
                    case "ui":
                        return DrawUiFamily(key, parts);        // windows, HUD and icons (ProceduralArtUi.cs)
                    case "wpn":
                    case "tool":
                    case "num":
                    case "hpbar":
                    case "shadow":
                        return DrawCharacterFamily(key, parts); // held weapons/tools, damage digits, HP bars, shadow (ProceduralArtCharacters.cs)
                    case "cyn": return DrawCanyonHd(parts);     // 32px canyon town (ProceduralArtCanyonHd.cs)
                    case "wnt": return DrawWinterHd(parts);     // 32px winter village (ProceduralArtWinterHd.cs)
                    case "anvil": return DrawAnvil();
                    case "snow": return DrawSnow(parts);
                    case "town": return DrawTown(parts);
                    case "box": return DrawCrate(false);
                    case "arrow": return DrawArrow(parts.Length > 1 ? parts[1] : "right");
                    case "site": return parts[1] == "built" ? DrawWorkshop() : DrawBlueprint();
                    case "pile": return DrawPile(parts[1] == "stone");
                    case "crop": return DrawCrop(parts[1]);
                    case "fx": return DrawFx(parts[1]);
                    case "dgn": return DrawDungeon(parts); // [DUNGEON] gates, room-map skull, reward cards (ProceduralArtDungeon.cs)
                    // [MONSTER] Projectiles, telegraph textures, summon circles (ProceduralArtMonsters).
                    case "mon": return DrawMonsterKey(parts);
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
    }
}
