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

    /// <summary>[RAID] The open raids' reward tables and their own materials.</summary>
    public static class RaidRewards
    {
        public const string RaidMatKing = "mat_raid_king", RaidMatGrah = "mat_raid_grah";
        /// <summary>[CH3] 침식 결정 (지바크) and 잿빛 인장 (카마츠): chapter 3 raid materials, also paid for the Lv.40 -> 50 계승 (InheritRules.MaterialCost).</summary>
        public const string RaidMatXibak = "mat_raid_xibak", RaidMatKamatz = "mat_raid_kamatz";

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

        /// <summary>[CH3] 공허 침식체 지바크 (Lv.45 tier): 10,000-15,000 gold, 침식 결정 (Docs/ch3/RAIDS.md 1). No Lv.45 legendary: that card falls back to unique.</summary>
        public static readonly RaidRewardDef Xibak = new RaidRewardDef
        {
            goldMin = 10000, goldMax = 15000, goldStep = 100, materialItem = RaidMatXibak, gearCategories = GearCategories,
            gearRarityWeights = new[] { (ItemRarity.Epic, 58), (ItemRarity.Unique, 41), (ItemRarity.Legendary, 1) },
            cards = new[]
            {
                new RewardEntry(DungeonDatabase.GearReward, 1, 1, 12),
                new RewardEntry(RaidMatXibak, 1, 1, 12),
                new RewardEntry(RaidMatXibak, 2, 2, 30),
                new RewardEntry(RaidMatXibak, 4, 4, 28),
                new RewardEntry(RaidMatXibak, 8, 8, 9),
                new RewardEntry(EnhanceRules.Essence, 16, 16, 6),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, 3),
            },
        };

        /// <summary>[CH3] 잿빛 손 지휘관 카마츠 (Lv.50 tier): 16,000-24,000 gold, 잿빛 인장 (Docs/ch3/RAIDS.md 2).</summary>
        public static readonly RaidRewardDef Kamatz = new RaidRewardDef
        {
            goldMin = 16000, goldMax = 24000, goldStep = 100, materialItem = RaidMatKamatz, gearCategories = GearCategories,
            gearRarityWeights = new[] { (ItemRarity.Epic, 58), (ItemRarity.Unique, 41), (ItemRarity.Legendary, 1) },
            cards = new[]
            {
                new RewardEntry(DungeonDatabase.GearReward, 1, 1, 12),
                new RewardEntry(RaidMatKamatz, 1, 1, 12),
                new RewardEntry(RaidMatKamatz, 2, 2, 30),
                new RewardEntry(RaidMatKamatz, 4, 4, 28),
                new RewardEntry(RaidMatKamatz, 8, 8, 9),
                new RewardEntry(EnhanceRules.Essence, 16, 16, 6),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, 3),
            },
        };

        /// <summary>True for the raid-only materials.</summary>
        public static bool IsRaidMaterial(string id) => id == RaidMatKing || id == RaidMatGrah || id == RaidMatXibak || id == RaidMatKamatz;
    }

    /// <summary>One product of the raid shop: a weapon box paid with a raid's own material (opened on purchase).</summary>
    public sealed class RaidShopProduct
    {
        public string id, name, raidId, materialItem;
        public int price;
        /// <summary>Result grade weights (Epic+ box: Epic 88 / Unique 12; Legendary box: Legendary 100).</summary>
        public (ItemRarity rarity, int weight)[] rarityWeights;
        public EquipCategory category = EquipCategory.Weapon;
        /// <summary>[CH3] Gear level of the box when it is not the raid's own (지바크's legendary box gives the Lv.50 legendary). 0 = the raid's level.</summary>
        public int tierLevel;

        public bool IsLegendary => rarityWeights.Length == 1 && rarityWeights[0].rarity == ItemRarity.Legendary;

        /// <summary>The gear tier of the raid this box belongs to (the server takes it from the raid's recommended level).</summary>
        public int Tier
        {
            get
            {
                var raid = DungeonDatabase.Get(raidId);
                if (raid == null) return 0;
                return GearCatalog.TierOfLevel(tierLevel > 0 ? tierLevel : DungeonDatabase.DifficultyFor(raid, DungeonDifficulty.Normal).recommendedLevel);
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

    /// <summary>[RAID] 레이드 상점 (Docs/server/phase13_raid_rewards.md §5): two boxes per raid and their rolls (chapter 3's four shown only while it is open).</summary>
    public static class RaidShop
    {
        public const string RatesVersion = "raidshop-1";

        static readonly (ItemRarity, int)[] EpicPlus = { (ItemRarity.Epic, 88), (ItemRarity.Unique, 12) };
        static readonly (ItemRarity, int)[] LegendOnly = { (ItemRarity.Legendary, 100) };

        public static readonly RaidShopProduct[] Products =
        {
            // [BALANCE 2026-10-16] Prices follow the content window (next content about 8 weeks after launch): the epic+ box
            // in 1.5~2 weeks of rewards, the guaranteed legendary in about 5~6 weeks, after the luck path (raid card 1%).
            new RaidShopProduct { id = "king_epic_weapon", name = L.T("해골왕 에픽+ 무기 상자"), raidId = DungeonDatabase.Raid, materialItem = RaidRewards.RaidMatKing, price = 45, rarityWeights = EpicPlus },
            new RaidShopProduct { id = "king_legend_weapon", name = L.T("해골왕 레전더리 무기 상자"), raidId = DungeonDatabase.Raid, materialItem = RaidRewards.RaidMatKing, price = 120, rarityWeights = LegendOnly },
            new RaidShopProduct { id = "grah_epic_weapon", name = L.T("그라흐 에픽+ 무기 상자"), raidId = DungeonDatabase.RaidGrah, materialItem = RaidRewards.RaidMatGrah, price = 20, rarityWeights = EpicPlus },
            new RaidShopProduct { id = "grah_legend_weapon", name = L.T("그라흐 레전더리 무기 상자"), raidId = DungeonDatabase.RaidGrah, materialItem = RaidRewards.RaidMatGrah, price = 60, rarityWeights = LegendOnly },
            // [CH3] Docs/ch3/RAIDS.md 3, same price rule as the others. 지바크 is a mid raid (3 rewarded clears a week, 10.24
            // 침식 결정 a clear): the 해골왕 prices keep it at ~1.5 / ~3.9 weeks (25 / 70 from the doc would be 0.8 / 2.3 weeks).
            new RaidShopProduct { id = "xibak_epic_weapon", name = L.T("지바크 에픽+ 무기 상자"), raidId = DungeonDatabase.RaidXibak, materialItem = RaidRewards.RaidMatXibak, price = 45, rarityWeights = EpicPlus },
            new RaidShopProduct { id = "xibak_legend_weapon", name = L.T("지바크 레전더리 무기 상자"), raidId = DungeonDatabase.RaidXibak, materialItem = RaidRewards.RaidMatXibak, price = 120, rarityWeights = LegendOnly, tierLevel = 50 },
            new RaidShopProduct { id = "kamatz_epic_weapon", name = L.T("카마츠 에픽+ 무기 상자"), raidId = DungeonDatabase.RaidKamatz, materialItem = RaidRewards.RaidMatKamatz, price = 20, rarityWeights = EpicPlus },
            new RaidShopProduct { id = "kamatz_legend_weapon", name = L.T("카마츠 레전더리 무기 상자"), raidId = DungeonDatabase.RaidKamatz, materialItem = RaidRewards.RaidMatKamatz, price = 60, rarityWeights = LegendOnly },
        };

        public static RaidShopProduct Get(string id)
        {
            foreach (var p in Products) if (p.id == id) return p;
            return null;
        }

        /// <summary>The class's weapon of the raid's tier at a grade (+0 key), or null if the catalog has none.</summary>
        public static string ResultKey(RaidShopProduct product, CharacterClass cls, ItemRarity rarity, System.Random rng = null) =>
            DungeonRewards.FindRaidGear(cls, product.Tier, product.category, rarity, rng); // [LEGEND] two legendary weapons: even pick

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
            return (rarity, ResultKey(product, cls, rarity, rng));
        }

        /// <summary>"에픽 88% / 유니크 12%" or "레전더리 확정".</summary>
        public static string RatesText(RaidShopProduct product)
        {
            if (product.IsLegendary) return L.F("{0} 확정", EquipmentDatabase.RarityName(product.rarityWeights[0].rarity));
            var parts = new List<string>();
            int total = product.TotalWeight;
            foreach (var w in product.rarityWeights) parts.Add($"{EquipmentDatabase.RarityName(w.rarity)} {w.weight * 100 / total}%");
            return string.Join(" / ", parts);
        }
    }
}
