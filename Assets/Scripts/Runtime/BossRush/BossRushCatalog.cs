namespace DotRPG
{
    /// <summary>
    /// [BOSS 2026-10-20] Numbers of the 3-minute boss attempts (월드보스 / 길드보스, Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 2-3).
    /// One arena (the raid throne room layout) and one boss, 심연의 거신, for both kinds; the server's hp_mul / damage_mul
    /// (guild boss stages) multiply the base numbers here.
    /// </summary>
    public static class BossRushCatalog
    {
        public const string BossId = "boss_abyss_titan";
        public const string ArenaMap = "boss_arena";
        /// <summary>
        /// About ten 그라흐 raids of HP per attempt (그라흐 x4.6 at Lv36): a Lv40 hero with good gear takes roughly 5-15% of it
        /// in three minutes, so nobody fells it inside one attempt and the ranking measures damage, not kills.
        /// </summary>
        public const float BaseHpMul = 46f;
        /// <summary>Hits like the 그라흐 raid (x1.7): a mistake costs a third of a mage, standing in every warning ends the try.</summary>
        public const float BaseDamageMul = 1.7f;
        public const int DefaultLevel = 40;
        /// <summary>Waits before the boss wakes up (the hero gets into place, the HUD clock starts with the boss).</summary>
        public const float WakeSeconds = 2f;
    }

    public static partial class MapRegistry
    {
        static MapInfo GetBossArena(string id) => id != BossRushCatalog.ArenaMap ? null : new MapInfo
        {
            id = id, displayName = L.T("심연의 제단"), resource = "Maps/Dungeons/RaidBoss", music = MusicRaidEnrage, theme = MapTheme.Winter,
            safe = false, instanced = true, hint = L.T("월드보스 · 길드보스 도전 · 3분 동안 준 피해가 기록됩니다"),
        };
    }
}
