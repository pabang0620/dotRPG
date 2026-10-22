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
        public static readonly int[] RaidCoreMid = { 1, 2, 2, 3 }; // [RAID] index 1 = 영웅 레이드 (+1 over the raid)
        public static readonly int[] RaidCoreFinal = { 2, 3, 4, 6 };

        public static int CoreGain(DungeonDef raid, DungeonDifficulty difficulty)
        {
            if (raid == null || !raid.isRaid) return 0;
            var table = raid.raidTier == RaidTier.Final ? RaidCoreFinal : RaidCoreMid;
            int i = (int)difficulty;
            return i >= 0 && i < table.Length ? table[i] : 0;
        }

        /// <summary>
        /// Cores and gold to promote INTO a grade. [BALANCE 2026-10-16] Unique and legendary cost a lot more: the raids hand
        /// out 1~2 cores per reward, so 12 cores made a legendary a sure thing in about a month. A legendary should be luck
        /// (raid card 1%) or a long, expensive project (24 cores = 8 weeks of skeleton king, 400,000 gold).
        /// </summary>
        public static (int cores, int gold) CostInto(ItemRarity to)
        {
            switch (to)
            {
                case ItemRarity.Uncommon: return (1, 3000);
                case ItemRarity.Rare: return (2, 8000);
                case ItemRarity.Epic: return (4, 30000);
                case ItemRarity.Unique: return (10, 120000);
                case ItemRarity.Legendary: return (24, 400000);
                default: return (1, 1000);
            }
        }

        /// <summary>The item this one promotes to (null = already the top of its line, or starter gear).</summary>
        public static EquipmentItem NextOf(EquipmentItem item)
        {
            if (item == null || item.starter || item.bossOnly) return null;
            EquipmentItem best = null;
            foreach (var other in EquipmentDatabase.All)
            {
                if (other.starter || other.bossOnly || other.levelTier != item.levelTier || other.category != item.category || other.classOnly != item.classOnly || other.rarity <= item.rarity) continue;
                if (best == null || other.rarity < best.rarity) best = other;
            }
            return best;
        }

        // ---------------- [CH3] 계승 (Docs/ch3/PROGRESSION_AND_GEAR.md 4절) ----------------

        /// <summary>Lv.40 tier (index 7): the only tier that inherits.</summary>
        public const int InheritFromTier = 7;
        /// <summary>Lv.50 tier (index 9): where an inherited piece lands. Lv.45 is neither source nor result.</summary>
        public const int InheritToTier = 9;

        /// <summary>
        /// Cost to carry a Lv.40 unique / legendary into the same piece at Lv.50 (+level, protection history and worn slot
        /// kept): 고대의 핵, 침식 결정 (시바크), 잿빛 인장 (카마츠) and gold. Common to epic do not inherit (Lv.50 monsters drop
        /// them). The materials make a holder still run both chapter 3 raids; the gold absorbs the Lv.40 savings (5절).
        /// </summary>
        public static (int cores, int shards, int seals, int gold)? InheritCost(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Unique => (3, 5, 10, 150000),
            ItemRarity.Legendary => (6, 10, 20, 400000),
            _ => null,
        };

        /// <summary>
        /// The Lv.50 piece a Lv.40 one inherits into: same category, class and grade (the 토벌형 "b" legendary keeps its
        /// archetype). Null when the piece cannot inherit.
        /// </summary>
        public static EquipmentItem InheritTargetOf(EquipmentItem item)
        {
            if (item == null || item.starter || item.bossOnly || item.levelTier != InheritFromTier || InheritCost(item.rarity) == null) return null;
            bool hunter = item.id.EndsWith("b");
            foreach (var other in EquipmentDatabase.All)
            {
                if (other.starter || other.bossOnly || other.levelTier != InheritToTier || other.category != item.category
                    || other.classOnly != item.classOnly || other.rarity != item.rarity || other.id.EndsWith("b") != hunter) continue;
                return other;
            }
            return null;
        }

        /// <summary>Every inheritance row (exported for the server's promote/inherit).</summary>
        public static IEnumerable<(EquipmentItem from, EquipmentItem to, int cores, int shards, int seals, int gold)> InheritRows()
        {
            foreach (var item in EquipmentDatabase.All)
            {
                var to = InheritTargetOf(item);
                if (to == null) continue;
                var c = InheritCost(item.rarity).Value;
                yield return (item, to, c.cores, c.shards, c.seals, c.gold);
            }
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
