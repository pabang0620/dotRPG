using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [RAID] Reward numbers of one raid (Docs/server/phase13_raid_rewards.md §3): a sure gold payout, the raid's own
    /// material, the gear rule of the gear card and the weighted card table. Exported to server/data/dungeons.json.
    /// </summary>
    public sealed class RaidRewardDef
    {
        public int goldMin, goldMax, goldStep;
        public string materialItem;
        public EquipCategory[] gearCategories;
        /// <summary>Grade weights of a gear card (Epic / Unique / Legendary).</summary>
        public (ItemRarity rarity, int weight)[] gearRarityWeights;
        /// <summary>Weighted card table; <see cref="DungeonDatabase.GearReward"/> stands for one piece of gear.</summary>
        public RewardEntry[] cards;

        public int TotalCardWeight
        {
            get
            {
                int total = 0;
                foreach (var c in cards) total += c.weight;
                return total;
            }
        }
    }

    /// <summary>[RAID] The two open raids' reward tables and their own materials.</summary>
    public static class RaidRewards
    {
        public const string RaidMatKing = "mat_raid_king", RaidMatGrah = "mat_raid_grah";

        static readonly EquipCategory[] GearCategories =
            { EquipCategory.Weapon, EquipCategory.Top, EquipCategory.Bottom, EquipCategory.Necklace, EquipCategory.Ring };

        /// <summary>해골왕 (Lv.20 tier): 3,000-4,500 gold, king crown shards.</summary>
        public static readonly RaidRewardDef King = new RaidRewardDef
        {
            goldMin = 3000, goldMax = 4500, goldStep = 100, materialItem = RaidMatKing, gearCategories = GearCategories,
            gearRarityWeights = new[] { (ItemRarity.Epic, 85), (ItemRarity.Unique, 14), (ItemRarity.Legendary, 1) },
            cards = new[]
            {
                new RewardEntry(DungeonDatabase.GearReward, 1, 1, 6),
                new RewardEntry(RaidMatKing, 1, 1, 13),
                new RewardEntry(RaidMatKing, 2, 2, 30),
                new RewardEntry(RaidMatKing, 4, 4, 32),
                new RewardEntry(RaidMatKing, 8, 8, 8),
                new RewardEntry(EnhanceRules.Ore, 24, 24, 4),
                new RewardEntry(EnhanceRules.Essence, 12, 12, 5),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, 2),
            },
        };

        /// <summary>수호자 그라흐 (Lv.40 tier): 12,000-18,000 gold, guardian stone shards.</summary>
        public static readonly RaidRewardDef Grah = new RaidRewardDef
        {
            goldMin = 12000, goldMax = 18000, goldStep = 100, materialItem = RaidMatGrah, gearCategories = GearCategories,
            gearRarityWeights = new[] { (ItemRarity.Epic, 58), (ItemRarity.Unique, 41), (ItemRarity.Legendary, 1) },
            cards = new[]
            {
                new RewardEntry(DungeonDatabase.GearReward, 1, 1, 12),
                new RewardEntry(RaidMatGrah, 1, 1, 12),
                new RewardEntry(RaidMatGrah, 2, 2, 30),
                new RewardEntry(RaidMatGrah, 4, 4, 28),
                new RewardEntry(RaidMatGrah, 8, 8, 9),
                new RewardEntry(EnhanceRules.Essence, 16, 16, 6),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, 3),
            },
        };

        /// <summary>True for the two raid-only materials.</summary>
        public static bool IsRaidMaterial(string id) => id == RaidMatKing || id == RaidMatGrah;
    }

    /// <summary>One product of the raid shop: a weapon box paid with a raid's own material (opened on purchase).</summary>
    public sealed class RaidShopProduct
    {
        public string id, name, raidId, materialItem;
        public int price;
        /// <summary>Result grade weights (Epic+ box: Epic 88 / Unique 12; Legendary box: Legendary 100).</summary>
        public (ItemRarity rarity, int weight)[] rarityWeights;
        public EquipCategory category = EquipCategory.Weapon;

        public bool IsLegendary => rarityWeights.Length == 1 && rarityWeights[0].rarity == ItemRarity.Legendary;

        /// <summary>The gear tier of the raid this box belongs to (the server takes it from the raid's recommended level).</summary>
        public int Tier
        {
            get
            {
                var raid = DungeonDatabase.Get(raidId);
                return raid == null ? 0 : GearCatalog.TierOfLevel(DungeonDatabase.DifficultyFor(raid, DungeonDifficulty.Normal).recommendedLevel);
            }
        }

        public int TierLevel => GearCatalog.TierLevels[Tier];

        public int TotalWeight
        {
            get
            {
                int total = 0;
                foreach (var w in rarityWeights) total += w.weight;
                return total;
            }
        }
    }

    /// <summary>[RAID] 레이드 상점 (Docs/server/phase13_raid_rewards.md §5): the four products and their rolls.</summary>
    public static class RaidShop
    {
        public const string RatesVersion = "raidshop-1";

        static readonly (ItemRarity, int)[] EpicPlus = { (ItemRarity.Epic, 88), (ItemRarity.Unique, 12) };
        static readonly (ItemRarity, int)[] LegendOnly = { (ItemRarity.Legendary, 100) };

        public static readonly RaidShopProduct[] Products =
        {
            new RaidShopProduct { id = "king_epic_weapon", name = "해골왕 에픽+ 무기 상자", raidId = DungeonDatabase.Raid, materialItem = RaidRewards.RaidMatKing, price = 90, rarityWeights = EpicPlus },
            new RaidShopProduct { id = "king_legend_weapon", name = "해골왕 레전더리 무기 상자", raidId = DungeonDatabase.Raid, materialItem = RaidRewards.RaidMatKing, price = 320, rarityWeights = LegendOnly },
            new RaidShopProduct { id = "grah_epic_weapon", name = "그라흐 에픽+ 무기 상자", raidId = DungeonDatabase.RaidGrah, materialItem = RaidRewards.RaidMatGrah, price = 30, rarityWeights = EpicPlus },
            new RaidShopProduct { id = "grah_legend_weapon", name = "그라흐 레전더리 무기 상자", raidId = DungeonDatabase.RaidGrah, materialItem = RaidRewards.RaidMatGrah, price = 100, rarityWeights = LegendOnly },
        };

        public static RaidShopProduct Get(string id)
        {
            foreach (var p in Products) if (p.id == id) return p;
            return null;
        }

        /// <summary>The class's weapon of the raid's tier at a grade (+0 key), or null if the catalog has none.</summary>
        public static string ResultKey(RaidShopProduct product, CharacterClass cls, ItemRarity rarity) =>
            DungeonRewards.FindRaidGear(cls, product.Tier, product.category, rarity);

        /// <summary>Offline purchase roll: the grade by the product's weights, then the class's piece of that grade.</summary>
        public static (ItemRarity rarity, string key) Roll(RaidShopProduct product, CharacterClass cls, System.Random rng)
        {
            int roll = rng.Next(0, System.Math.Max(1, product.TotalWeight));
            var rarity = product.rarityWeights[0].rarity;
            foreach (var w in product.rarityWeights)
            {
                if (roll < w.weight) { rarity = w.rarity; break; }
                roll -= w.weight;
            }
            return (rarity, ResultKey(product, cls, rarity));
        }

        /// <summary>"에픽 88% / 유니크 12%" or "레전더리 확정".</summary>
        public static string RatesText(RaidShopProduct product)
        {
            if (product.IsLegendary) return EquipmentDatabase.RarityName(product.rarityWeights[0].rarity) + " 확정";
            var parts = new List<string>();
            int total = product.TotalWeight;
            foreach (var w in product.rarityWeights) parts.Add($"{EquipmentDatabase.RarityName(w.rarity)} {w.weight * 100 / total}%");
            return string.Join(" / ", parts);
        }
    }
}
