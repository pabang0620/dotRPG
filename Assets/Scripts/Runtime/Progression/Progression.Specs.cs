using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CH3 SPEC] Specialization choices (Docs/ch3/SKILL_SPECIALIZATION.md 1, 5). Stored in <see cref="CareerSave.specs"/>
    /// and in every skill style. Points: one per level from Lv41 (<see cref="SkillSpecs.PointsAt"/>); an active side
    /// costs 2, a passive side 1. Changing them is free and only in a town. While chapter 3 is closed nothing applies.
    /// Server: careerRules.ts (validateCareer specs).
    /// </summary>
    public sealed partial class Progression
    {
        public CharacterClass Class => cls;

        /// <summary>True when specializations exist for this hero: chapter 3 open and past Lv40.</summary>
        public bool SpecsOpen => Chapter3.Enabled && Level > SkillSpecs.FromLevel;

        public int SpecPoints => Chapter3.Enabled ? SkillSpecs.PointsAt(Level) : 0;

        public int SpecPointsUsed
        {
            get
            {
                int used = 0;
                foreach (var c in careerState.specs) used += SkillSpecs.Cost(c.skill);
                return used;
            }
        }

        public int SpecPointsLeft => SpecPoints - SpecPointsUsed;

        /// <summary>'a', 'b', or '\0' (none or not in force).</summary>
        public char SpecOf(string skill)
        {
            if (!SpecsOpen || string.IsNullOrEmpty(skill)) return '\0';
            foreach (var c in careerState.specs)
                if (c.skill == skill) return !string.IsNullOrEmpty(c.side) ? c.side[0] : '\0';
            return '\0';
        }

        /// <summary>Base skills of the class always; career skills once promoted (the awakening once awakened).</summary>
        public bool SpecAllowed(string skill)
        {
            if (System.Array.IndexOf(SkillSpecs.BaseOf(cls), skill) >= 0) return true;
            var c = CareerCatalog.Get(skill);
            if (c == null || !IsPromoted || c.career != Career) return false;
            return c.kind != CareerSkillKind.Awakening || Awakened;
        }

        /// <summary>Only in a town (safe, not a dungeon room).</summary>
        public static bool SpecEditPlace()
        {
            var map = MapRegistry.Get(Game.Session?.MapId);
            return map != null && map.safe && !map.instanced && (Game.Dungeon == null || !Game.Dungeon.InRun);
        }

        /// <summary>
        /// Picks side 'a' / 'b' of a skill, or clears it with '\0'. Returns a Korean reason when it can't, else null.
        /// </summary>
        public string SetSpec(string skill, char side)
        {
            if (!SpecsOpen) return L.T("특화는 레벨 41부터 쓸 수 있습니다.");
            if (!SpecEditPlace()) return L.T("특화는 마을에서 바꿀 수 있어요.");
            if (!SpecAllowed(skill)) return L.T("아직 특화할 수 없는 스킬입니다.");
            int at = careerState.specs.FindIndex(c => c.skill == skill);
            if (side == '\0')
            {
                if (at >= 0) careerState.specs.RemoveAt(at);
                Changed?.Invoke();
                return null;
            }
            if (SkillSpecs.Find(skill, side) == null) return L.T("없는 특화입니다.");
            int left = SpecPointsLeft + (at >= 0 ? SkillSpecs.Cost(skill) : 0);
            if (left < SkillSpecs.Cost(skill)) return L.T("특화 포인트가 모자랍니다.");
            if (at >= 0) careerState.specs[at].side = side.ToString();
            else careerState.specs.Add(new SpecChoice { skill = skill, side = side.ToString() });
            Changed?.Invoke();
            return null;
        }

        /// <summary>Saved choices kept only when they still fit (skill allowed, sides known, within the points).</summary>
        static List<SpecChoice> CleanSpecs(Progression p, List<SpecChoice> from)
        {
            var list = new List<SpecChoice>();
            if (from == null) return list;
            var seen = new HashSet<string>();
            // The raw level, not the cap: a chapter closed again must not wipe the choices (they just stop applying).
            int budget = System.Math.Max(0, System.Math.Min(SkillSpecs.MaxPoints, p.Level - SkillSpecs.FromLevel)), used = 0;
            foreach (var c in from)
            {
                if (c == null || string.IsNullOrEmpty(c.side) || !seen.Add(c.skill ?? "")) continue;
                if (!p.SpecAllowed(c.skill) || SkillSpecs.Find(c.skill, c.side[0]) == null) continue;
                int cost = SkillSpecs.Cost(c.skill);
                if (used + cost > budget) continue;
                used += cost;
                list.Add(new SpecChoice { skill = c.skill, side = c.side.Substring(0, 1) });
            }
            return list;
        }

        static List<SpecChoice> CopySpecs(List<SpecChoice> from)
        {
            var list = new List<SpecChoice>();
            if (from != null) foreach (var c in from) if (c != null) list.Add(new SpecChoice { skill = c.skill, side = c.side });
            return list;
        }
    }
}
