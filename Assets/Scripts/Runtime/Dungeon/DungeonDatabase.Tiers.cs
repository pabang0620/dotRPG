namespace DotRPG
{
    /// <summary>
    /// [TIERS 2026-10-29] The weekday dungeons' steps between 영웅 (Lv27) and 군주 (Lv45): 악몽 Lv32, 지옥 Lv37, 심연 Lv40.
    /// Each drops the gear of its recommended level's tier (Lv30 / 35 / 40), so every tier up to Lv40 has a weekday source.
    /// Numbers come from Tools/balance/theory_weekday_tiers.py (Docs/PLAN_WEEKDAY_TIERS_2026-10-29.md):
    /// - monsterLevel = recommended - 5, revives 2, minGearRarity Epic (as 영웅).
    /// - hpMul / damageMul: the 영웅 -> 군주 difficulty index (monster HP or damage at its level over a recommended-level
    ///   party's damage or HP) interpolated by recommended level, so clear times stay in the 영웅 window (reference times = 영웅).
    /// - recommendedPower: linear from 영웅 6,000 (Lv27) to the final raid 10,400 (Lv40).
    /// - rewardMul: the largest ladder that keeps the no-spend gold ratio at Lv40 within 0.85~0.90 (3.4 / 3.5 / 3.6).
    /// - ticketWeight / jackpotPerMille: 영웅 -> 군주 linear by recommended level, rounded.
    /// - Clear XP floor: the 영웅 benchmark level (36), so a run gives the same total XP as 영웅 and leveling is not faster.
    /// Weekday dungeons only: 일반 던전 keep their four difficulties and raids their two.
    /// </summary>
    public static partial class DungeonDatabase
    {
        /// <summary>First difficulty only the weekday dungeons have (악몽; 군주 too).</summary>
        public const DungeonDifficulty FirstWeekdayOnly = DungeonDifficulty.Nightmare;

        /// <summary>True for 악몽 and above: weekday dungeons only.</summary>
        public static bool IsWeekdayOnly(DungeonDifficulty d) => d >= FirstWeekdayOnly;

        /// <summary>This dungeon has that difficulty right now (raids: 레이드 and 심화; 일반 던전 up to 영웅; 군주 while chapter 3 is open).</summary>
        public static bool Offers(DungeonDef dungeon, DungeonDifficulty d)
        {
            if (dungeon == null || d < DungeonDifficulty.Normal || (int)d >= DifficultyCount) return false;
            if (dungeon.isRaid) return d <= RaidHeroic;
            if (d == DungeonDifficulty.Lord) return LordOpen(dungeon);
            return !IsWeekdayOnly(d) || CountsEntry(dungeon);
        }

        // Properties, not fields: the difficulty table in the other part reads them during type initialisation.
        static DifficultyDef NightmareNumbers => new DifficultyDef
        {
            id = DungeonDifficulty.Nightmare, name = L.T("악몽"), recommendedLevel = 32, recommendedPower = 7700, hpMul = 13.7f, damageMul = 2.8f,
            rewardMul = 3.4f, monsterLevel = 27, revives = 2, minGearRarity = ItemRarity.Epic, ticketWeight = 7, jackpotPerMille = 6,
        };

        static DifficultyDef InfernoNumbers => new DifficultyDef
        {
            id = DungeonDifficulty.Inferno, name = L.T("지옥"), recommendedLevel = 37, recommendedPower = 9400, hpMul = 15.7f, damageMul = 3.0f,
            rewardMul = 3.5f, monsterLevel = 32, revives = 2, minGearRarity = ItemRarity.Epic, ticketWeight = 7, jackpotPerMille = 7,
        };

        static DifficultyDef AbyssNumbers => new DifficultyDef
        {
            id = DungeonDifficulty.Abyss, name = L.T("심연"), recommendedLevel = 40, recommendedPower = 10400, hpMul = 18.1f, damageMul = 3.2f,
            rewardMul = 3.6f, monsterLevel = 35, revives = 2, minGearRarity = ItemRarity.Epic, ticketWeight = 7, jackpotPerMille = 7,
        };

        /// <summary>
        /// Clear XP floor benchmark level per difficulty (HuntingGrounds.DungeonClearFloor). 악몽·지옥·심연 keep 영웅's 36: the
        /// same total XP per run as 영웅, so the Lv1~40 play time (about 46 hours) does not get shorter.
        /// </summary>
        public static readonly int[] ClearFloorBenchmark = { 10, 18, 26, 36, 36, 36, 36, 45 };
    }
}
