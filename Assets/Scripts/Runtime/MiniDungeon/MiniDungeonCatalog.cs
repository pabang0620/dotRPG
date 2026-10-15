using System;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    public sealed class MiniDungeonDef
    {
        public int Tier, MinLevel, MaxLevel;
        public string Name, MapId, MonsterId;
        public MapTheme Theme;
        public string Music;
    }

    /// <summary>Solo experience training rooms. Their allowance is shared, not three separate entry tickets.</summary>
    public static class MiniDungeonCatalog
    {
        public const int DailySeconds = 30 * 60;
        public const int SpawnCount = 6;
        public const float RespawnSeconds = 5f;
        public static readonly MiniDungeonDef[] All =
        {
            new MiniDungeonDef { Tier = 10, MinLevel = 10, MaxLevel = 19, Name = "이끼빛 수련의 숲", MapId = "mini_dungeon_10", MonsterId = "skel_warrior", Theme = MapTheme.Forest, Music = MapRegistry.MusicDgnForest },
            new MiniDungeonDef { Tier = 20, MinLevel = 20, MaxLevel = 29, Name = "수정빛 수련의 석실", MapId = "mini_dungeon_20", MonsterId = "skel_warrior", Theme = MapTheme.Canyon, Music = MapRegistry.MusicDgnCanyon },
            new MiniDungeonDef { Tier = 30, MinLevel = 30, MaxLevel = 39, Name = "서리빛 수련의 동굴", MapId = "mini_dungeon_30", MonsterId = "skel_warrior", Theme = MapTheme.Winter, Music = MapRegistry.MusicDgnWinter },
        };

        public static MiniDungeonDef Get(int tier) => Array.Find(All, d => d.Tier == tier);
        public static MiniDungeonDef ForMap(string map) => Array.Find(All, d => d.MapId == map);
        public static bool IsMap(string map) => ForMap(map) != null;
        public static int XpPerKill(int entryLevel) => Math.Max(1, (int)Math.Ceiling(Progression.XpToNext(entryLevel) / 240.0));

        /// <summary>Small open arena: six connected spawn courts, decorative water stays beyond the combat floor.</summary>
        public static string Layout(string map)
        {
            var def = ForMap(map);
            if (def == null) return null;
            const int w = 28, h = 22;
            var cells = new char[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                bool border = x < 2 || y < 2 || x >= w - 2 || y >= h - 2;
                bool bevel = (x < 4 || x >= w - 4) && (y < 4 || y >= h - 4);
                cells[x, y] = border || bevel ? 'W' : def.Theme == MapTheme.Winter ? '.' : ',';
                if (!border && !bevel && (x == 5 || x == 22 || y == 5 || y == 16)) cells[x, y] = '=';
            }
            // Peripheral pools and paired props leave the central routes unobstructed.
            for (int y = 17; y <= 18; y++) for (int x = 4; x <= 7; x++) cells[x, y] = '~';
            for (int y = 3; y <= 4; y++) for (int x = 20; x <= 23; x++) cells[x, y] = '~';
            char prop = def.Theme == MapTheme.Forest ? 'T' : def.Theme == MapTheme.Winter ? 'q' : 'R';
            cells[4, 10] = cells[23, 10] = prop;
            cells[4, 14] = cells[23, 7] = prop;
            cells[10, 17] = cells[17, 4] = def.Theme == MapTheme.Canyon ? 'p' : 't';
            cells[14, 10] = 'P';
            cells[9, 7] = '1'; cells[18, 7] = '2';
            cells[8, 11] = '3'; cells[19, 11] = '4';
            cells[10, 15] = '5'; cells[17, 15] = '6';
            var text = new StringBuilder();
            for (int y = h - 1; y >= 0; y--) { for (int x = 0; x < w; x++) text.Append(cells[x, y]); text.Append('\n'); }
            return text.ToString();
        }
    }

    public static partial class MapRegistry
    {
        static MapInfo GetMiniDungeon(string id)
        {
            var d = MiniDungeonCatalog.ForMap(id);
            return d == null ? null : new MapInfo { id = id, displayName = L.T(d.Name), resource = "", theme = d.Theme,
                music = d.Music, instanced = true, safe = false,
                hint = L.F("Lv.{0}~{1} 경험치 미니던전 · 하루 총 30분 · 자동 사냥", d.MinLevel, d.MaxLevel) };
        }
    }
}
