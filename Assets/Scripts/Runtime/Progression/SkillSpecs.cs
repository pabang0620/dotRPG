using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[CH3 SPEC] Numbers a specialization overrides on its skill. NaN / -1 / "" = keep the skill's own value.</summary>
    public sealed class SpecParams
    {
        public float damageMult = float.NaN, cooldown = float.NaN, radius = float.NaN, range = float.NaN, duration = float.NaN, spreadDeg = float.NaN;
        public int projectiles = -1, hits = -1, manaCost = -1, targets = -1, pierce = -1;
        /// <summary>Skill-specific behaviour tag read by the cast function (e.g. "branch_25", "pierce_ramp_25_3").</summary>
        public string extra = "";
    }

    /// <summary>[CH3 SPEC] One specialization: side 'a' (crimson) or 'b' (silver) of one skill.</summary>
    public sealed class SkillSpec
    {
        public string skill, axis, name, text;
        public char side;
        public SpecParams p;
        public string Id => skill + "_" + side;
        /// <summary>Spread-axis B sides raise the server's multi-hit cap (Docs/ch3/SKILL_SPECIALIZATION.md 1).</summary>
        public bool IsSpread => axis == "spread";
    }

    /// <summary>[CH3 SPEC] One saved choice: which side of which skill.</summary>
    [System.Serializable] public sealed class SpecChoice { public string skill; public string side; }

    /// <summary>
    /// [CH3 SPEC] Specializations (Docs/ch3/SKILL_SPECIALIZATION.md): from Lv41 every level gives one point (10 at Lv50);
    /// an active skill's side costs 2, a passive's 1. <see cref="Apply"/> is the single place the numbers change:
    /// <see cref="CharacterStatsCalc.Skill"/> runs it, so ranks, supports and the passive tree still multiply on top, and
    /// the cast functions read the extra fields of <see cref="SkillNumbers"/> (spec, specExtra, projectiles...).
    /// </summary>
    public static partial class SkillSpecs
    {
        public const int FromLevel = 40, MaxPoints = 10, ActiveCost = 2, PassiveCost = 1;
        public static readonly Color ColorA = new Color32(0xff, 0x33, 0x44, 0xff);
        public static readonly Color ColorB = new Color32(0xe8, 0xec, 0xff, 0xff);
        public static readonly Color RimB = new Color32(0x6c, 0x7a, 0xa0, 0xff);

        public static IReadOnlyList<SkillSpec> All => all;
        public static readonly string[] WarriorBase = { "crush", "whirl", "cry", "charge", "blades" };
        public static readonly string[] MageBase = { "lance", "frostorb", "nova", "firefield", "meteor" };
        /// <summary>[ROGUE SPEC] The rogue's five base skills (Shadow Dash is the mobility key, not a gem: no specialization).</summary>
        public static readonly string[] RogueBase = { "stab", "leap", "backstab", "raid", "dance" };
        public static string[] BaseOf(CharacterClass cls) => cls == CharacterClass.Warrior ? WarriorBase : cls == CharacterClass.Rogue ? RogueBase : MageBase;

        public static SkillSpec Find(string skill, char side)
        {
            if (side != 'a' && side != 'b' || string.IsNullOrEmpty(skill)) return null;
            foreach (var s in all) if (s.side == side && s.skill == skill) return s;
            return null;
        }

        /// <summary>2 for an active skill (base or career, awakenings included), 1 for a career passive.</summary>
        public static int Cost(string skill)
        {
            var c = CareerCatalog.Get(skill);
            return c != null && c.kind == CareerSkillKind.Passive ? PassiveCost : ActiveCost;
        }

        /// <summary>Points owned at this level (capped by <see cref="Progression.LevelCap"/>).</summary>
        public static int PointsAt(int level) => Mathf.Clamp(Mathf.Min(level, Progression.LevelCap) - FromLevel, 0, MaxPoints);

        /// <summary>A copy of the gem with the spec's overrides (the catalog gem is never touched).</summary>
        public static SkillGem Apply(SkillGem gem, SkillSpec spec)
        {
            if (gem == null || spec == null) return gem;
            var g = gem.Clone();
            var p = spec.p;
            if (!float.IsNaN(p.damageMult)) g.damageMult = p.damageMult;
            if (!float.IsNaN(p.cooldown)) g.cooldown = p.cooldown;
            if (!float.IsNaN(p.radius)) g.radius = p.radius;
            if (!float.IsNaN(p.range)) g.range = p.range;
            if (p.hits >= 0) g.hits = p.hits;
            if (p.manaCost >= 0) g.manaCost = p.manaCost;
            return g;
        }

        /// <summary>Copies the spec's behaviour fields into the final numbers the cast functions get.</summary>
        public static void Stamp(ref SkillNumbers n, SkillSpec spec)
        {
            if (spec == null) return;
            n.spec = spec.side;
            n.specExtra = spec.p.extra ?? "";
            n.projectiles = spec.p.projectiles;
            n.targets = spec.p.targets;
            n.pierce = spec.p.pierce;
            n.spreadDeg = spec.p.spreadDeg;
            n.duration = spec.p.duration;
        }

        /// <summary>
        /// [CH3 SPEC] The effect colour of a cast: crimson for A, silver for B, else the skill's own colour. The career
        /// colour stays on rims and trails (<see cref="Rim"/>).
        /// </summary>
        public static Color Tint(SkillNumbers n, Color own) => n.spec == 'a' ? ColorA : n.spec == 'b' ? ColorB : own;
        public static Color Rim(SkillNumbers n, Color own) => n.spec == 'b' ? RimB : own;
    }
}
