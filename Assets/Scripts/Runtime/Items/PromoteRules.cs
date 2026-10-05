using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Gear promotion (승급) at the blacksmith: a piece becomes the next grade of the same slot for the same class
    /// (wooden sword -> iron sword -> bone sword -> dragon sword), keeping its +level. The cost is 고대의 핵, which only raid
    /// clears give (mid raids a few, the final raid more, higher difficulty more), plus gold. Starter gear is not
    /// promoted (it is free to get back). The server checks and applies the same table (exported to enhance.json).
    /// </summary>
    public static class PromoteRules
    {
        public const string CoreItem = DungeonDatabase.RaidCore;

        /// <summary>Cores per raid reward by difficulty (normal, hard, expert, master).</summary>
        public static readonly int[] RaidCoreMid = { 1, 1, 2, 3 };
        public static readonly int[] RaidCoreFinal = { 2, 3, 4, 6 };

        public static int CoreGain(DungeonDef raid, DungeonDifficulty difficulty)
        {
            if (raid == null || !raid.isRaid) return 0;
            var table = raid.raidTier == RaidTier.Final ? RaidCoreFinal : RaidCoreMid;
            int i = (int)difficulty;
            return i >= 0 && i < table.Length ? table[i] : 0;
        }

        /// <summary>Cores and gold to promote INTO a grade.</summary>
        public static (int cores, int gold) CostInto(ItemRarity to)
        {
            switch (to)
            {
                case ItemRarity.Uncommon: return (1, 2000);
                case ItemRarity.Rare: return (2, 5000);
                case ItemRarity.Epic: return (3, 10000);
                case ItemRarity.Unique: return (6, 30000);
                case ItemRarity.Legendary: return (12, 80000);
                default: return (1, 1000);
            }
        }

        /// <summary>The item this one promotes to (null = already the top of its line, or starter gear).</summary>
        public static EquipmentItem NextOf(EquipmentItem item)
        {
            if (item == null || item.starter) return null;
            EquipmentItem best = null;
            foreach (var other in EquipmentDatabase.All)
            {
                if (other.starter || other.category != item.category || other.classOnly != item.classOnly || other.rarity <= item.rarity) continue;
                if (best == null || other.rarity < best.rarity) best = other;
            }
            return best;
        }

        /// <summary>Every promotion row (exported for the server).</summary>
        public static IEnumerable<(EquipmentItem from, EquipmentItem to, int cores, int gold)> Rows()
        {
            foreach (var item in EquipmentDatabase.All)
            {
                var next = NextOf(item);
                if (next == null) continue;
                var (cores, gold) = CostInto(next.rarity);
                yield return (item, next, cores, gold);
            }
        }
    }
}
