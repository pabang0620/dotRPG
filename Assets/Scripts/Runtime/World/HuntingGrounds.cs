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
        public int KillXp
        {
            get
            {
                int ordinary = Math.Max(1, (HuntingGrounds.XpAt(monsterLevel, theme == MapTheme.Underground) + HuntingGrounds.PackSize / 2) / HuntingGrounds.PackSize);
                // Stronger underground roles earn 25% more per kill, without changing any surface field.
                return theme == MapTheme.Underground ? (ordinary * 5 + 3) / 4 : ordinary;
            }
        }
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
        /// <summary>Kills an hour of steady field hunting assumed by the balance model (15 a minute, 75% of the time).</summary>
        public const int KillsPerHourEstimate = 675;
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
            new HuntingZone("forest_crossing", "옛 수로 합류지", "village", MapTheme.Forest, 10, 13, 12, 3, "skel_warrior", "skel_archer", "skel_shield"),
            new HuntingZone("canyon_pass", "붉은 돌 고개", "canyon", MapTheme.Canyon, 13, 16, 14, 0, "skel_warrior", "skel_miner", "skel_archer"),
            new HuntingZone("canyon_mine", "버려진 채석장", "canyon", MapTheme.Canyon, 17, 20, 18, 1, "skel_miner", "skel_miner", "skel_shield"),
            new HuntingZone("canyon_ridge", "바람칼 능선", "canyon", MapTheme.Canyon, 21, 24, 22, 2, "skel_knight", "skel_archer", "skel_miner"),
            new HuntingZone("canyon_gate", "쌍벽 관문", "canyon", MapTheme.Canyon, 22, 25, 24, 3, "skel_warrior", "skel_shield", "skel_archer"),
            new HuntingZone("winter_edge", "서리 소나무 숲", "winter", MapTheme.Winter, 25, 28, 26, 0, "skel_warrior", "skel_archer", "skel_shield"),
            new HuntingZone("winter_lake", "얼어붙은 호숫가", "winter", MapTheme.Winter, 29, 33, 31, 1, "skel_shield", "skel_archer", "skel_knight"),
            new HuntingZone("winter_peak", "눈보라 봉우리", "winter", MapTheme.Winter, 34, 40, 36, 2, "skel_knight", "skel_knight", "skel_archer"),
            new HuntingZone("winter_reach", "해빙된 성소 입구", "winter", MapTheme.Winter, 36, 40, 39, 3, "skel_warrior", "skel_shield", "skel_archer"),
            new HuntingZone("sanctum_hall", "침수된 회랑", MapRegistry.Sanctum, MapTheme.SanctumField, 38, 40, 40, 0, "skel_warrior", "skel_shield", "skel_archer"),
            new HuntingZone("sanctum_archive", "잠긴 서고", MapRegistry.Sanctum, MapTheme.SanctumField, 40, 40, 40, 1, "skel_archer", "skel_warrior", "skel_shield"),
            new HuntingZone("sanctum_roots", "뿌리 잠식 지하묘", MapRegistry.Sanctum, MapTheme.SanctumField, 40, 40, 40, 2, "skel_warrior", "skel_shield", "skel_archer"),
            new HuntingZone("sanctum_court", "망각의 내전", MapRegistry.Sanctum, MapTheme.SanctumField, 40, 40, 40, 3, "skel_shield", "skel_archer", "skel_warrior"),
            new HuntingZone("hollow_descent", "B1 · 뿌리 아래 갱도", MapRegistry.Undergate, MapTheme.Underground, 40, 45, 42, 0, "hollow_scarab", "hollow_guard", "hollow_hexer"),
            new HuntingZone("hollow_roots", "B2 · 뒤엉킨 뿌리굴", MapRegistry.Undergate, MapTheme.Underground, 45, 50, 47, 1, "hollow_scarab", "hollow_guard", "hollow_hexer"),
            new HuntingZone("hollow_fungal", "B2 · 푸른 포자 동굴", MapRegistry.Undergate, MapTheme.Underground, 45, 50, 47, 2, "hollow_hexer", "hollow_scarab", "hollow_guard"),
            new HuntingZone("hollow_depths", "B3 · 반딧불 심연", MapRegistry.Undergate, MapTheme.Underground, 50, 55, 53, 3, "hollow_guard", "hollow_hexer", "hollow_scarab"),
        };
        public static HuntingZone Get(string id) => Array.Find(All, z => z.id == id);
        /// <summary>Enemy data may exceed the player cap in the underground world; ordinary fields keep the original cap.</summary>
        public const int MaxMonsterLevel = 60;
        public static int XpAt(int level, bool underground = false) => 20 + (Progression.BaseXpToNext(Math.Max(1, Math.Min(underground ? MaxMonsterLevel : Progression.MaxLevel, level))) - Progression.BaseXpToNext(1) + KillsPerLevel - 1) / KillsPerLevel;
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
            if (id == MapRegistry.Undergate || Get(id)?.theme == MapTheme.Underground) return UnderworldLayouts.Layout(id);
            var z = Get(id);
            return z == null ? null : z.theme==MapTheme.SanctumField ? SanctumHunting.Layout(z) : HuntingLayouts.Build(z);
        }
    }
}
