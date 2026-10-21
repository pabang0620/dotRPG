namespace DotRPG
{
    /// <summary>
    /// [BOSS 2026-10-20] Numbers of the 3-minute boss attempts (월드보스 / 길드보스, Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 2-3).
    /// One arena (the raid throne room layout) and one boss, 심연의 거신, for both kinds at the same strength (the guild boss
    /// is a weekly score, not a kill); the server's hp_mul / damage_mul (1 today) multiply the base numbers here.
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
        /// <summary>
        /// [BALANCE 2026-10-20] x1.0 (was x1.7 like the 그라흐 raid): measured in the arena, an arcanist in the clear kit fell in
        /// 9-42 s while the other careers lasted the three minutes, so one career could not compete in a damage ranking.
        /// </summary>
        public const float BaseDamageMul = 1f;
        public const int DefaultLevel = 40;

        /// <summary>
        /// [BALANCE 2026-10-21] Career multiplier on the hero's own hits to the arena boss, so every career can compete in a
        /// damage ranking. Measured 3-minute damage (Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 8): fighter 205k, guardian 112k,
        /// arcanist 131k, bishop 65k. With these the others land at 70~85% of a fighter (157k / 170k / 143k); the server's
        /// damage cap (attackCap x 0.6, 500k+ at Lv40) still sits far above. Arena only: AI companions and other content unchanged.
        /// </summary>
        public static float CareerDamageMul(Career career) => career switch
        {
            Career.Guardian => 1.4f,
            Career.Arcanist => 1.3f,
            Career.Bishop => 2.2f,
            _ => 1f,
        };
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
