using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    public sealed class HuntingZone
    {
        public readonly string id, name, village;
        public readonly MapTheme theme;
        public readonly int minLevel, maxLevel, monsterLevel, variant;
        public readonly string[] monsters;
        /// <summary>One kill's XP: the per-level rate split over the pack (three times the monsters, a third the XP each).</summary>
        public int KillXp => Math.Max(1, (HuntingGrounds.XpAt(monsterLevel) + HuntingGrounds.PackSize / 2) / HuntingGrounds.PackSize);
        public HuntingZone(string id, string name, string village, MapTheme theme, int min, int max, int level, int variant, params string[] monsters)
        {
            this.id = id; this.name = name; this.village = village; this.theme = theme;
            minLevel = min; maxLevel = max; monsterLevel = level; this.variant = variant; this.monsters = monsters;
        }
    }

    /// <summary>Repeatable fields. Recommendations never gate entry behind a level, quest or dungeon clear.</summary>
    public static class HuntingGrounds
    {
        public const float RespawnSeconds = 25f, RespawnSafeRadius = 4f;
        /// <summary>Monsters per map spawn point (playtest: meet many more monsters, each worth less).</summary>
        public const int PackSize = 3;
        static readonly Vector2[] PackOffsets = { Vector2.zero, new Vector2(0.75f, 0.35f), new Vector2(-0.7f, 0.45f) };

        /// <summary>Each map spawn point becomes a small pack. The same order is used by the data export (server supply limits).</summary>
        public static List<Vector2> PackPoints(IReadOnlyList<Vector2> spawnPoints)
        {
            var list = new List<Vector2>(spawnPoints.Count * PackSize);
            foreach (var p in spawnPoints)
                for (int k = 0; k < PackSize; k++) list.Add(p + PackOffsets[k % PackOffsets.Length]);
            return list;
        }
        public const int KillsPerMinute = 12, KillsPerLevel = 40;
        public const float DungeonEfficiency = 1.8f;
        public static readonly HuntingZone[] All =
        {
            new HuntingZone("forest", "해골 숲", "village", MapTheme.Forest, 1, 4, 1, 0, "skeleton"),
            new HuntingZone("forest_ruins", "이끼 낀 유적", "village", MapTheme.Forest, 5, 8, 6, 1, "skel_warrior", "skel_warrior", "skel_archer"),
            new HuntingZone("forest_depths", "검은 뿌리 숲", "village", MapTheme.Forest, 9, 12, 10, 2, "skel_warrior", "skel_archer", "skel_shield"),
            new HuntingZone("canyon_pass", "붉은 돌 고개", "canyon", MapTheme.Canyon, 13, 16, 14, 0, "skel_warrior", "skel_miner", "skel_archer"),
            new HuntingZone("canyon_mine", "버려진 채석장", "canyon", MapTheme.Canyon, 17, 20, 18, 1, "skel_miner", "skel_miner", "skel_shield"),
            new HuntingZone("canyon_ridge", "바람칼 능선", "canyon", MapTheme.Canyon, 21, 24, 22, 2, "skel_knight", "skel_archer", "skel_miner"),
            new HuntingZone("winter_edge", "서리 소나무 숲", "winter", MapTheme.Winter, 25, 28, 26, 0, "skel_warrior", "skel_archer", "skel_shield"),
            new HuntingZone("winter_lake", "얼어붙은 호숫가", "winter", MapTheme.Winter, 29, 33, 31, 1, "skel_shield", "skel_archer", "skel_knight"),
            new HuntingZone("winter_peak", "눈보라 봉우리", "winter", MapTheme.Winter, 34, 40, 36, 2, "skel_knight", "skel_knight", "skel_archer"),
        };
        public static HuntingZone Get(string id) => Array.Find(All, z => z.id == id);
        public static int XpAt(int level) => 20 + (Progression.BaseXpToNext(Math.Max(1, Math.Min(Progression.MaxLevel, level))) - Progression.BaseXpToNext(1) + KillsPerLevel - 1) / KillsPerLevel;
        public static string HomeOf(string mapId) => MapRegistry.Get(mapId)?.exteriorMap ?? Get(mapId)?.village ?? (MapRegistry.Get(mapId)?.safe == true ? mapId : MapRegistry.Village);

        // Equal-level gear / 12 field kills per minute, normal route at its reference time.
        // Floor applies to TOTAL dungeon XP (kills + clear); rank/specialty rewards then add value.
        public static int DungeonClearFloor(DungeonDef dungeon, DifficultyDef diff)
        {
            int tier = Mathf.Clamp((int)diff.id, 0, 3);
            int benchmarkLevel = dungeon.isRaid ? diff.recommendedLevel : new[] { 10, 18, 26, 36 }[tier];
            float seconds = dungeon.referenceSeconds[tier];
            int target = Mathf.CeilToInt(XpAt(benchmarkLevel) * KillsPerMinute * seconds / 60f * DungeonEfficiency);
            int kills = 0;
            foreach (var room in dungeon.rooms)
                foreach (var group in room.groups)
                {
                    var monster = MonsterDatabase.Get(group.monsterId);
                    if (monster == null || monster.noLoot) continue;
                    int level = DungeonMonsters.LevelFor(diff, group);
                    kills += group.count * Mathf.RoundToInt(monster.xp * (1f + MonsterDatabase.XpPerLevel * (level - 1)));
                }
            return Math.Max(0, target - kills);
        }

        public static string AdaptTownLayout(string id, string source)
        {
            if (id != MapRegistry.Winter) return source;
            var rows = new List<string>();
            foreach (var raw in source.Split('\n'))
            {
                var row = raw.TrimEnd('\r');
                if (row.Length > 0 && !row.StartsWith("//")) rows.Add(row);
            }
            int width = 0, startRow = -1, startX = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                width = Math.Max(width, rows[i].Length);
                int x = rows[i].IndexOf('P'); if (x >= 0) { startRow = i; startX = x; }
            }
            if (startRow < 1 || startRow >= rows.Count - 1) return source;
            // Extend the existing southern plaza east across a bridge to the first snow field.
            for (int row = startRow - 1; row <= startRow + 1; row++)
            {
                var line = rows[row].PadRight(width, '.').ToCharArray();
                for (int x = startX + 2; x < width; x++) line[x] = line[x] == '~' ? 'd' : ',';
                line[width - 1] = '>'; rows[row] = new string(line);
            }
            return string.Join("\n", rows);
        }

        /// <summary>Deterministic, distinct outdoor layouts using the existing forest/canyon/snow art.</summary>
        public static string Layout(string id)
        {
            var z = Get(id);
            if (z == null || id == MapRegistry.Forest) return null; // preserve the original hand-painted forest
            const int w = 56, h = 40;
            char floor = z.theme == MapTheme.Canyon ? ',' : '.';
            char border = z.theme == MapTheme.Forest ? '%' : 'W';
            var cells = new char[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                cells[x, y] = x < 2 || x >= w - 2 || y < 2 || y >= h - 2 ? border : floor;
            // Rivers, quarry islands and frozen pools give each route a different silhouette.
            for (int y = 3; y < h - 3; y++) for (int x = 4; x < w - 4; x++)
            {
                bool feature = z.variant == 0 ? (x >= 26 && x <= 28) : z.variant == 1
                    ? ((x-28)*(x-28)/2 + (y-20)*(y-20) < 54)
                    : ((x > 15 && x < 21 && y > 11 && y < 28) || (x > 36 && x < 41 && y > 5 && y < 22));
                if (feature) cells[x,y] = z.theme == MapTheme.Canyon && z.variant != 0 ? 'W' : '~';
            }
            // Wide connecting lanes and a complete patrol loop; bridges replace water in lanes.
            void Lane(int x, int y)
            {
                if (cells[x,y] == '~') cells[x,y] = 'd';
                else cells[x,y] = z.theme == MapTheme.Forest ? '=' : ',';
            }
            foreach (int y in new[] { 8, 20, 32 })
                for (int x = 0; x < w; x++) for (int dy = -1; dy <= 1; dy++) Lane(x, y + dy);
            foreach (int x in new[] { 8, 18, 38, 47 })
                for (int y = 5; y < h - 4; y++) for (int dx = -1; dx <= 1; dx++) Lane(x + dx, y);
            // Keep the map border closed except for the two intentional exits.
            for (int y = 0; y < h; y++) { cells[0,y] = border; cells[w-1,y] = border; }
            for (int y = 19; y <= 21; y++) { cells[0,y] = '<'; cells[w-1,y] = '>'; }
            cells[3,20] = 'P';
            var rng = new System.Random(4701 + Array.IndexOf(All, z) * 113);
            for (int y = 4; y < h - 4; y += 3) for (int x = 4; x < w - 4; x += 3)
            {
                if (cells[x,y] != floor || x < 7 || x > 49) continue;
                char prop = z.theme == MapTheme.Forest ? (rng.Next(3) == 0 ? 'q' : 'Y')
                    : z.theme == MapTheme.Winter ? (rng.Next(3) == 0 ? 'R' : 'y')
                    : (rng.Next(3) == 0 ? 'T' : 'R');
                if (rng.NextDouble() < .48) cells[x,y] = prop;
            }
            if (z.theme == MapTheme.Forest && z.variant == 1)
                foreach (var p in new[] { new Vector2Int(11,26), new Vector2Int(14,26), new Vector2Int(43,14), new Vector2Int(46,14) })
                    cells[p.x,p.y] = p.x % 2 == 0 ? 'g' : 'i';
            // Twenty-four spawns, spread into six camps; no spawns in the portal safety areas.
            foreach (int y in new[] { 8, 20, 32 })
                foreach (int x in new[] { 10, 14, 19, 23, 33, 37, 42, 46 })
                {
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) Lane(x+dx,y+dy);
                    cells[x,y] = 'k';
                }
            var text = new StringBuilder();
            for (int y = h - 1; y >= 0; y--) { for (int x = 0; x < w; x++) text.Append(cells[x,y]); text.Append('\n'); }
            return text.ToString();
        }
    }
}
