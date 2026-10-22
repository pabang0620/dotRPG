namespace DotRPG
{
    /// <summary>
    /// [CH3 SPEC] Number overrides of the warrior line (warrior base, 파이터, 수호자), Docs/ch3/SKILL_SPECIALIZATION.md 3.
    /// Base skills give absolute gem numbers (their catalog values are the design's raw values). Career skills are
    /// rebalanced when the catalog builds (power x0.85, cooldown x0.7...), so their damage changes are ratios applied in
    /// the cast functions (CareerCombat) and the numbers here are read off the built skill, never hard-coded.
    /// </summary>
    public static partial class SkillSpecs
    {
        static float Own(string id, System.Func<CareerSkill, float> pick)
        {
            var s = CareerCatalog.Get(id);
            return s != null ? pick(s) : float.NaN;
        }

        // ---------- Warrior base (gem units = the design's raw numbers) ----------
        static SpecParams CrushA() => new SpecParams { damageMult = 4.0f, extra = "crush_point" };
        static SpecParams CrushB() => new SpecParams { damageMult = .67f, radius = 1.3f * 1.6f, hits = 2, targets = 5, extra = "crush_waves" };
        static SpecParams WhirlA() => new SpecParams { damageMult = 2.9f / 3f, hits = 3, extra = "whirl_pull" };
        static SpecParams WhirlB() => new SpecParams { damageMult = .85f, hits = 2, extra = "whirl_move" };
        static SpecParams CryA() => new SpecParams { radius = 2.5f, duration = 2.4f, extra = "cry_daunt" };
        static SpecParams CryB() => new SpecParams { radius = 6f, duration = .8f, extra = "cry_rally" };
        static SpecParams ChargeA() => new SpecParams { range = 10f, damageMult = 1.0f, extra = "charge_pierce" };
        static SpecParams ChargeB() => new SpecParams { damageMult = 2.4f, extra = "charge_ward" };
        static SpecParams BladesA() => new SpecParams { damageMult = 28.8f, hits = 1, radius = .8f, extra = "blades_one" };
        static SpecParams BladesB() => new SpecParams { damageMult = 1.2f, hits = 12, radius = 3.2f, cooldown = 12f, extra = "blades_rain" };

        // ---------- 파이터 (ratios in CareerCombat.Fighter) ----------
        static SpecParams FRushA() => new SpecParams { range = 7f, extra = "rush_pierce" };
        static SpecParams FRushB() => new SpecParams { cooldown = System.Math.Max(.5f, Own("f_rush", s => s.cooldown) - 1f), extra = "rush_ward" };
        static SpecParams FFocusA() => new SpecParams { cooldown = Own("f_focus", s => s.cooldown) * 2f, extra = "focus_one" };
        static SpecParams FFocusB() => new SpecParams { projectiles = 3, spreadDeg = 40f, extra = "focus_three" };
        static SpecParams FAwakeA() => new SpecParams { radius = Own("f_awake", s => s.radius) * .4f, extra = "awake_point" };
        static SpecParams FAwakeB() => new SpecParams { radius = Own("f_awake", s => s.radius) * 1.4f, duration = 10f, extra = "awake_storm" };

        // ---------- 수호자 ----------
        static SpecParams GOathA() => new SpecParams { radius = Own("g_oath", s => s.radius) * .5f, extra = "oath_heart" };
        static SpecParams GOathB() => new SpecParams { radius = Own("g_oath", s => s.radius) * 1.625f, extra = "oath_field" };
        static SpecParams GTauntA() => new SpecParams { duration = 8f, targets = 1, extra = "taunt_mark" };
        static SpecParams GTauntB() => new SpecParams { radius = Own("g_taunt", s => s.radius) * 11f / 7f, duration = 2.7f, extra = "taunt_wide" };
        static SpecParams GAwakeA() => new SpecParams { radius = Own("g_awake", s => s.radius) * .5f, duration = 12f, extra = "aegis_point" };
        static SpecParams GAwakeB() => new SpecParams { radius = Own("g_awake", s => s.radius) * 1.6f, duration = 8f, extra = "aegis_wide" };
    }
}
