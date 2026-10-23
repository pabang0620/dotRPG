namespace DotRPG
{
    /// <summary>
    /// [ROGUE SPEC] Number overrides of the rogue line (rogue base, 어쌔신, 닌자), Docs/ch3/SKILL_SPECIALIZATION.md 3.6.
    /// Same split as the warrior line: base skills give absolute gem numbers, career skills keep their built numbers and
    /// the cast functions (CareerCombat.SpecRogue) apply the damage ratios. The user's skills (연쇄 도약, 백어택, 천인난무,
    /// 허수아비, 쿠나이 순간이동 (assassin since 2026-10-23), 분신술) keep their own numbers when no side is chosen; a side is an option on top.
    /// </summary>
    public static partial class SkillSpecs
    {
        // ---------- Rogue base (gem units = the design's raw numbers) ----------
        static SpecParams StabA() => new SpecParams { damageMult = 3.6f, extra = "stab_heart" };
        static SpecParams StabB() => new SpecParams { damageMult = 1.2f, targets = 4, spreadDeg = 90f, extra = "stab_fan" };
        // [ROGUE 2026-10-23] 연쇄 도약 is spammable now (1.0 a monster, 0.5 s): A = cooldown x6 and damage x6, B = 0.7 + 2 x 0.15.
        static SpecParams LeapA() => new SpecParams { damageMult = 6.0f, cooldown = 3f, extra = "leap_heavy" };
        static SpecParams LeapB() => new SpecParams { damageMult = 0.7f, extra = "leap_trail" };
        static SpecParams BackstabA() => new SpecParams { duration = 1.6f, targets = 1, extra = "back_heart" };
        static SpecParams BackstabB() => new SpecParams { duration = .55f, targets = 3, extra = "back_spin" };
        static SpecParams RaidA() => new SpecParams { damageMult = 4.6f, extra = "raid_open" }; // [ROGUE 2026-10-23] x1.15 with the base
        static SpecParams RaidB() => new SpecParams { damageMult = 2.76f, extra = "raid_return" };
        /// <summary>[ROGUE 2026-10-23] 허수아비: A one monster 6 s (x HP 2), B up to 3 in 1.5x the radius for 2 s.</summary>
        static SpecParams ScarecrowA() => new SpecParams { targets = 1, duration = 6f, extra = "crow_tough" };
        static SpecParams ScarecrowB() => new SpecParams { targets = 3, duration = 2f, radius = 4.2f * 1.5f, extra = "crow_wide" };
        static SpecParams DanceA() => new SpecParams { damageMult = 8.8f, hits = 3, extra = "dance_three" };
        static SpecParams DanceB() => new SpecParams { damageMult = 1.1f, hits = 12, cooldown = 12f, extra = "dance_quick" };

        // ---------- 어쌔신 (ratios in CareerCombat.SpecRogue) ----------
        // [ROGUE 2026-10-23] 환영 삼격 (replaced 쌍인참): A three phantoms on one line, B five on a ±50° fan, one cut per monster.
        static SpecParams APhantomA() => new SpecParams { targets = 1, extra = "phantom_line" };
        static SpecParams APhantomB() => new SpecParams { projectiles = 5, spreadDeg = 50f, extra = "phantom_fan" };
        static SpecParams ARendA() => new SpecParams { targets = 1, extra = "rend_point" };
        static SpecParams ARendB() => new SpecParams { targets = 5, range = Own("a_rend", s => s.range) * 1.2f, extra = "rend_whirl" };
        static SpecParams AKunaiA() => new SpecParams { extra = "port_pierce" };
        static SpecParams AKunaiB() => new SpecParams { duration = 6f, extra = "port_anchor" };
        static SpecParams AVenomA() => new SpecParams { duration = 90f, extra = "venom_boss" };
        static SpecParams AVenomB() => new SpecParams { targets = 2, extra = "venom_spread" };
        static SpecParams ARuptureA() => new SpecParams { cooldown = Own("a_rupture", s => s.cooldown) * 2f, extra = "rupture_big" };
        static SpecParams ARuptureB() => new SpecParams { duration = 4f, extra = "rupture_bleed" };
        static SpecParams AAssassinateA() => new SpecParams { extra = "assassin_exec" };
        static SpecParams AAssassinateB() => new SpecParams { extra = "assassin_blink" };
        // [ROGUE 2026-10-23] 변신술 (was 천영참): A 2 s at +70% (blast 480%), B 12 s at +9% and +15% speed (blast 240%). CareerCombat.ShadowForm.
        static SpecParams AAwakeA() => new SpecParams { duration = 2f, extra = "form_short" };
        static SpecParams AAwakeB() => new SpecParams { duration = 12f, extra = "form_long" };
        static SpecParams AStealthA() => new SpecParams { duration = 4f, extra = "stealth_deep" };
        static SpecParams AStealthB() => new SpecParams { extra = "stealth_three" };

        // ---------- 닌자 ----------
        static SpecParams NVeilA() => new SpecParams { extra = "veil_pierce" }; // [NINJA 2026-10-23] ranged: a piercing smoke kunai
        static SpecParams NVeilB() => new SpecParams { extra = "veil_double" };
        // [NINJA 2026-10-23] Clones copy at 60% (user, fixed): A one clone twice as long, B three for two thirds of the time.
        static SpecParams NCloneA() => new SpecParams { targets = 1, extra = "clone_one" };
        static SpecParams NCloneB() => new SpecParams { targets = 3, extra = "clone_three" };
        static SpecParams NRecallA() => new SpecParams { targets = 1, extra = "recall_target" };
        static SpecParams NRecallB() => new SpecParams { radius = 1.2f, extra = "recall_wide" };
        static SpecParams NVolleyA() => new SpecParams { projectiles = 3, extra = "volley_line" };
        static SpecParams NVolleyB() => new SpecParams { projectiles = 3, spreadDeg = 15f, pierce = 9, extra = "volley_fan" };
        static SpecParams NFumaA() => new SpecParams { cooldown = Own("n_fuma", s => s.cooldown) * 2f, radius = Own("n_fuma", s => s.radius) * 1.3f, hits = 3, extra = "fuma_big" };
        static SpecParams NFumaB() => new SpecParams { hits = 12, duration = 2f, extra = "fuma_hover" };
        static SpecParams NBombA() => new SpecParams { projectiles = 1, radius = Own("n_bomb", s => s.radius) * 1.3f, duration = 3f, extra = "bomb_one" };
        static SpecParams NBombB() => new SpecParams { projectiles = 8, duration = 1f, extra = "bomb_field" };
        // [NINJA 2026-10-23] 표창 난사 (was 분신 난무): A 12 stars in 1 s, B 36 in 9 s, the same total (each star 24 / count).
        static SpecParams NAwakeA() => new SpecParams { hits = 12, duration = 1f, extra = "stars_burst" };
        static SpecParams NAwakeB() => new SpecParams { hits = 36, duration = 9f, extra = "stars_rain" };
    }
}
