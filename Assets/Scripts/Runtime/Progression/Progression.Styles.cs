using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [STYLE] Skill styles (Docs/PLAN_SKILL_STYLES.md): up to three saved builds, each with its own career nodes, skill
    /// keys + support gems and passive tree. Style 1 (party) and 2 (solo) are free; style 3 is bought once per account.
    /// The style in use also lives in the normal fields (nodes, slots, Allocated), so combat and the server checks see
    /// no difference; <see cref="SyncActiveStyle"/> copies it into its saved slot before every save.
    /// </summary>
    public sealed partial class Progression
    {
        public const int StyleCount = 3, PaidStyle = 2;

        public int ActiveStyle => careerState.activeStyle;

        public static string StyleName(int i) => i == 0 ? "스타일 1 · 파티" : i == 1 ? "스타일 2 · 솔로" : "스타일 3 · 자유";

        /// <summary>Set from the server (GET /skill-styles); offline play always has it.</summary>
        public static bool Style3Owned = true;

        public static bool StyleOpen(int i) => i >= 0 && i < StyleCount && (i != PaidStyle || Style3Owned);

        /// <summary>True when the style has something saved (an empty one starts from a blank build).</summary>
        public bool StyleUsed(int i) => i < careerState.styles.Count && careerState.styles[i] != null &&
            (careerState.styles[i].nodes.Count > 0 || careerState.styles[i].passives.Count > 0 || careerState.styles[i].gems.Exists(g => !string.IsNullOrEmpty(g)));

        void RestoreStyles(CareerSave old)
        {
            careerState.activeStyle = old.activeStyle >= 0 && old.activeStyle < StyleCount ? old.activeStyle : 0;
            careerState.styles = new List<CareerStyle>();
            if (old.styles != null)
                for (int i = 0; i < old.styles.Count && i < StyleCount; i++)
                    careerState.styles.Add(Copy(old.styles[i]));
        }

        static CareerStyle Copy(CareerStyle s)
        {
            var c = new CareerStyle();
            if (s == null) return c;
            if (s.nodes != null) foreach (var n in s.nodes) if (n != null) c.nodes.Add(new CareerRank { id = n.id, rank = n.rank });
            if (s.gems != null) c.gems.AddRange(s.gems);
            if (s.passives != null) c.passives.AddRange(s.passives);
            return c;
        }

        CareerStyle CaptureStyle()
        {
            var s = new CareerStyle();
            foreach (var n in careerState.nodes) s.nodes.Add(new CareerRank { id = n.id, rank = n.rank });
            for (int slot = 0; slot < SkillGems.Slots; slot++)
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++)
                    s.gems.Add(k == 0 ? slots[slot, 0] ?? "" : slots[slot, k] ?? "");
            foreach (var id in Allocated) if (id != PassiveTree.Start) s.passives.Add(id);
            return s;
        }

        /// <summary>Copies the build in use into its saved slot (only once styles are in use).</summary>
        void SyncActiveStyle()
        {
            if (careerState.styles.Count == 0 && careerState.activeStyle == 0) return;
            while (careerState.styles.Count <= careerState.activeStyle) careerState.styles.Add(new CareerStyle());
            careerState.styles[careerState.activeStyle] = CaptureStyle();
        }

        /// <summary>Loads a saved build into the live fields, with the same rules as normal allocation.</summary>
        void ApplyStyle(CareerStyle s)
        {
            // Passive tree: same check as a restored save (points and connection), else a fresh tree.
            Allocated.Clear();
            Allocated.Add(PassiveTree.Start);
            foreach (var id in s.passives) if (PassiveTree.Get(id) != null) Allocated.Add(id);
            if (PointsLeft < 0 || !AllConnected()) { Allocated.Clear(); Allocated.Add(PassiveTree.Start); }
            // Career nodes in tree order, through Learn (prerequisites and budget).
            careerState.nodes.Clear();
            if (IsPromoted)
                foreach (var skill in CareerCatalog.For(Career))
                {
                    var n = s.nodes.Find(x => x.id == skill.id);
                    for (int i = 0; i < Math.Min(3, n?.rank ?? 0); i++) if (!Learn(skill.id)) break;
                }
            // Skill keys and supports.
            for (int slot = 0; slot < SkillGems.Slots; slot++)
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++) slots[slot, k] = null;
            if (Awakened) slots[4, 0] = CareerCatalog.For(Career)[8].id;
            for (int i = 0; i < s.gems.Count && i < SkillGems.Slots * (1 + SkillGems.SupportsPerSlot); i++)
            {
                int slot = i / (1 + SkillGems.SupportsPerSlot), k = i % (1 + SkillGems.SupportsPerSlot);
                if (string.IsNullOrEmpty(s.gems[i])) continue;
                if (k == 0) { EquipSkill(slot, s.gems[i]); continue; }
                var g = SkillGems.Get(s.gems[i]);
                slots[slot, k] = g != null && g.kind == GemKind.Support && IsUnlocked(g) ? g.id : null;
            }
        }

        /// <summary>
        /// Switches to style <paramref name="i"/>: saves the build in use, then loads the other one (a style never used
        /// starts blank). Returns a Korean reason when it can't, else null.
        /// </summary>
        public string SwitchStyle(int i)
        {
            if (i < 0 || i >= StyleCount) return "없는 스타일입니다.";
            if (i == careerState.activeStyle) return null;
            if (!StyleOpen(i)) return "스타일 3은 별조각으로 열 수 있습니다.";
            if (Game.Dungeon != null && Game.Dungeon.InRun) return "던전·레이드 중에는 스타일을 바꿀 수 없습니다.";
            while (careerState.styles.Count < StyleCount) careerState.styles.Add(new CareerStyle());
            careerState.styles[careerState.activeStyle] = CaptureStyle();
            careerState.activeStyle = i;
            ApplyStyle(careerState.styles[i]);
            Changed?.Invoke();
            return null;
        }
    }
}
