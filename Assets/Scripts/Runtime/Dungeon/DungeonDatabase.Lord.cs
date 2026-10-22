using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[CH3] 군주 clear bonus: a material put straight in the bag (min..max), not a reward card.</summary>
    public sealed class LordBonus
    {
        public string material;
        public int min, max;

        public LordBonus(string material, int min, int max)
        {
            this.material = material;
            this.min = min;
            this.max = max;
        }
    }

    /// <summary>
    /// [CH3] The weekday dungeons' third hard difficulty, 군주 (Docs/ch3/WORLD_AND_STORY.md 8절): the same five dungeons at
    /// Lv45, each boss with one raid pattern of its own from phase 2, and an extra sure 침식 결정 (Mon, Wed, Fri) or
    /// 잿빛 인장 (Tue, Thu) that feeds the raid shop and 계승. Open only on a chapter 3 server and from Lv41; on a
    /// closed server the button, the party lists and the sweep window do not show it (the server refuses difficulty 4).
    /// </summary>
    public static partial class DungeonDatabase
    {
        /// <summary>Entry level of 군주 (the chapter 3 band starts here; the recommended level stays 45).</summary>
        public const int LordMinLevel = 41;

        /// <summary>A property, not a field: the difficulty table in the other part reads it during type initialisation.</summary>
        static DifficultyDef LordNumbers => new DifficultyDef
        {
            id = DungeonDifficulty.Lord, name = L.T("군주"), recommendedLevel = 45, recommendedPower = 13000, hpMul = 18f, damageMul = 3.2f,
            rewardMul = 4.6f, monsterLevel = 40, revives = 2, minGearRarity = ItemRarity.Unique, ticketWeight = 8, jackpotPerMille = 7,
        };

        /// <summary>True when this dungeon has a 군주 difficulty at all (the five weekday dungeons).</summary>
        public static bool HasLord(DungeonDef d) => d != null && !d.isRaid && !d.always && d.lordSure != null;

        /// <summary>군주 can be picked here right now (chapter 3 open). The level is checked on entry.</summary>
        public static bool LordOpen(DungeonDef d) => HasLord(d) && Chapter3.Enabled;

        /// <summary>Difficulties shown for this dungeon: the four old ones, plus 군주 while chapter 3 is open.</summary>
        public static int ShownDifficultyCount(DungeonDef d) => LordOpen(d) ? DifficultyCount : (int)DungeonDifficulty.Lord;

        /// <summary>Difficulties shown when no dungeon is picked yet (party lists): 군주 only while chapter 3 is open.</summary>
        public static int ShownDifficultyCountAny => Chapter3.Enabled ? DifficultyCount : (int)DungeonDifficulty.Lord;

        // Boss pattern of each dungeon's 군주 fight (phase 2 on) and the extra sure material. A property for the same reason
        // as LordNumbers (WithLord runs while the weekday table initialises).
        static Dictionary<string, (BossPattern pattern, LordBonus bonus)> LordTable => new Dictionary<string, (BossPattern, LordBonus)>
        {
            ["gold_vein"] = (new BossPattern(PatternKind.Fields, 7f, 1.1f, 5, minPhase: 2), new LordBonus(RaidRewards.RaidMatXibak, 1, 2)),
            ["smelter"] = (new BossPattern(PatternKind.Donut, 10f, 1.4f, minPhase: 2), new LordBonus(RaidRewards.RaidMatKamatz, 1, 1)),
            ["mana_graveyard"] = (new BossPattern(PatternKind.BoltRing, 6f, 1.0f, 10, minPhase: 2), new LordBonus(RaidRewards.RaidMatXibak, 1, 2)),
            ["training_forest"] = (new BossPattern(PatternKind.ArrowVolley, 8f, 1.0f, 9, minPhase: 2), new LordBonus(RaidRewards.RaidMatKamatz, 1, 1)),
            ["armory"] = (new BossPattern(PatternKind.Charge, 7f, 1.3f, minPhase: 2), new LordBonus(RaidRewards.RaidMatXibak, 1, 2)),
        };

        /// <summary>
        /// Adds the 군주 data to the weekday dungeon table: the pattern, the bonus, and a fifth reference time
        /// (the 영웅 one: 군주 is tuned to the same 120~300 s window with Lv45 heroes).
        /// </summary>
        static DungeonDef[] WithLord(DungeonDef[] defs)
        {
            var table = LordTable;
            foreach (var d in defs)
            {
                if (d == null || !table.TryGetValue(d.id, out var lord)) continue;
                d.lordPatterns = new List<BossPattern> { lord.pattern };
                d.lordSure = lord.bonus;
                if (d.referenceSeconds != null && d.referenceSeconds.Length == (int)DungeonDifficulty.Lord)
                {
                    var times = new float[d.referenceSeconds.Length + 1];
                    Array.Copy(d.referenceSeconds, times, d.referenceSeconds.Length);
                    times[(int)DungeonDifficulty.Lord] = d.referenceSeconds[(int)DungeonDifficulty.Hero];
                    d.referenceSeconds = times;
                }
            }
            return defs;
        }

        /// <summary>
        /// A boss spawned in a 군주 run gains its dungeon's pattern and a phase change at half HP (the weekday bosses have no
        /// phases of their own), so the new pattern joins the fight halfway.
        /// </summary>
        public static void ApplyLord(EnemyController boss, DungeonDef d, DungeonDifficulty difficulty)
        {
            if (boss == null || difficulty != DungeonDifficulty.Lord || d?.lordPatterns == null || boss.Def == null) return;
            if (boss.Def.phases == null || boss.Def.phases.Length == 0) boss.Def.phases = new[] { 0.5f };
            foreach (var p in d.lordPatterns) if (!boss.Def.patterns.Contains(p)) boss.Def.patterns.Add(p);
        }

        /// <summary>Rolls the 군주 bonus of a clear (null: not a 군주 run or no bonus). Same order as the server's rollLordBonus.</summary>
        public static (string item, int count)? RollLordBonus(DungeonDef d, DungeonDifficulty difficulty, System.Random rng)
        {
            if (difficulty != DungeonDifficulty.Lord || d == null || d.isRaid || d.lordSure == null) return null;
            return (d.lordSure.material, rng.Next(d.lordSure.min, d.lordSure.max + 1));
        }
    }
}
