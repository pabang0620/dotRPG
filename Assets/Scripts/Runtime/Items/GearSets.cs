using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [SET 2026-10-29] Gear set bonuses (Docs/PLAN_GEAR_SETS_2026-10-29.md). A set is the worn gear of one level tier
    /// (Lv.20 and up) over five parts: weapon, top, bottom, necklace, ring (two rings of the tier count once). Any grade
    /// counts; the bonus size follows the lowest grade among the counted parts (per part its best piece). Steps at 2, 3 and
    /// 5 parts stack. The % multiplies the worn gear's attack / HP totals (Equipment.AttackBonus / MaxHealthBonus), not the
    /// character's base. Inherited (^k) and hidden-option (~x) keys count with their item's own tier.
    /// Numbers match server/src/utils/gearSet.ts.
    /// </summary>
    public static class GearSets
    {
        /// <summary>Lowest level tier with a set (index into GearCatalog.TierLevels: 3 = Lv.20). Lv.1~15 keep their hand-tuned balance.</summary>
        public const int MinTier = 3;
        public const int Parts = 5;
        public static readonly int[] Steps = { 2, 3, 5 };

        /// <summary>Bonus groups by the lowest counted grade.</summary>
        public enum Group { Low, Epic, Unique, Legendary }

        // [group, step] in %. Common~rare (the hunting kits the playtime was measured with) only get HP, so kill speed and
        // the total playtime stay as measured; epic and above add attack.
        static readonly int[,] AttackPct = { { 0, 0, 0 }, { 0, 5, 5 }, { 0, 6, 6 }, { 0, 7, 7 } };
        static readonly int[,] HealthPct = { { 3, 3, 4 }, { 5, 0, 5 }, { 6, 0, 6 }, { 7, 0, 7 } };

        public static bool HasSet(int levelTier) => levelTier >= MinTier;

        public static Group GroupOf(ItemRarity lowest) =>
            lowest >= ItemRarity.Legendary ? Group.Legendary : lowest >= ItemRarity.Unique ? Group.Unique : lowest >= ItemRarity.Epic ? Group.Epic : Group.Low;

        public static int StepAttack(Group g, int step) => AttackPct[(int)g, step];
        public static int StepHealth(Group g, int step) => HealthPct[(int)g, step];

        /// <summary>One tier's set as worn: parts counted (0..5), their lowest grade and the summed bonus.</summary>
        public struct State
        {
            public int levelTier, count, attackPct, healthPct;
            public ItemRarity lowest;
            public Group Group => GroupOf(lowest);
        }

        static int PartOf(EquipCategory c)
        {
            switch (c)
            {
                case EquipCategory.Weapon: return 0;
                case EquipCategory.Top: return 1;
                case EquipCategory.Bottom: return 2;
                case EquipCategory.Necklace: return 3;
                default: return 4; // ring
            }
        }

        /// <summary>The set of one tier in the worn keys (count 0 when none is worn).</summary>
        public static State StateOf(IEnumerable<string> wornKeys, int levelTier)
        {
            var best = new int[Parts];
            for (int i = 0; i < Parts; i++) best[i] = -1;
            if (wornKeys != null)
                foreach (var key in wornKeys)
                {
                    var e = EquipmentDatabase.Get(key);
                    if (e == null || e.levelTier != levelTier) continue;
                    int p = PartOf(e.category);
                    if ((int)e.rarity > best[p]) best[p] = (int)e.rarity;
                }
            var s = new State { levelTier = levelTier, lowest = ItemRarity.Legendary };
            for (int i = 0; i < Parts; i++)
            {
                if (best[i] < 0) continue;
                s.count++;
                if (best[i] < (int)s.lowest) s.lowest = (ItemRarity)best[i];
            }
            if (s.count == 0 || !HasSet(levelTier)) { s.lowest = ItemRarity.Common; return s; }
            var g = GroupOf(s.lowest);
            for (int i = 0; i < Steps.Length; i++)
            {
                if (s.count < Steps[i]) break;
                s.attackPct += AttackPct[(int)g, i];
                s.healthPct += HealthPct[(int)g, i];
            }
            return s;
        }

        /// <summary>Every tier with at least one active step (2+ parts), lowest tier first.</summary>
        public static List<State> Active(IEnumerable<string> wornKeys)
        {
            var tiers = new SortedSet<int>();
            if (wornKeys != null)
                foreach (var key in wornKeys)
                {
                    var e = EquipmentDatabase.Get(key);
                    if (e != null && HasSet(e.levelTier)) tiers.Add(e.levelTier);
                }
            var list = new List<State>();
            foreach (int t in tiers)
            {
                var s = StateOf(wornKeys, t);
                if (s.count >= Steps[0]) list.Add(s);
            }
            return list;
        }

        /// <summary>Summed attack % and HP % of every active set in the worn keys.</summary>
        public static void Totals(IEnumerable<string> wornKeys, out int attackPct, out int healthPct)
        {
            attackPct = healthPct = 0;
            foreach (var s in Active(wornKeys))
            {
                attackPct += s.attackPct;
                healthPct += s.healthPct;
            }
        }

        /// <summary>A gear total with a set %: round(total x (100 + pct) / 100).</summary>
        public static int Apply(int total, int pct) => pct == 0 ? total : UnityEngine.Mathf.RoundToInt(total * (100 + pct) / 100f);

        /// <summary>"장비 공격력 +5%, 장비 체력 +5%" for one step of a group (or a summed bonus).</summary>
        public static string BonusText(int attackPct, int healthPct)
        {
            var parts = new List<string>();
            if (attackPct > 0) parts.Add(L.F("장비 공격력 +{0}%", attackPct));
            if (healthPct > 0) parts.Add(L.F("장비 체력 +{0}%", healthPct));
            return string.Join(", ", parts);
        }
    }
}
