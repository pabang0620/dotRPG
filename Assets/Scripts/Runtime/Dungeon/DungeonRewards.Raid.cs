using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[RAID] Raid rewards (Docs/server/phase13_raid_rewards.md §3): a sure gold payout and four independent cards that are all taken.</summary>
    public static partial class DungeonRewards
    {
        /// <summary>Sure gold of a cleared raid: <c>goldMin + step * k</c>, k uniform over the steps (the difficulty multiplier is not applied).</summary>
        public static int RollRaidGold(RaidRewardDef def, System.Random rng) =>
            def.goldMin + def.goldStep * rng.Next(0, (def.goldMax - def.goldMin) / def.goldStep + 1);

        /// <summary>
        /// Four independent cards from the raid's weighted table (no gold, no difficulty multiplier). A gear card rolls
        /// its slot, then its grade, and is the class's +0 piece of the raid's tier.
        /// </summary>
        public static List<RewardCard> RollRaidCards(DungeonDef dungeon, CharacterClass cls, System.Random rng)
        {
            var def = dungeon.raidReward;
            int tier = GearCatalog.TierOfLevel(DungeonDatabase.DifficultyFor(dungeon, DungeonDifficulty.Normal).recommendedLevel);
            int total = Mathf.Max(1, def.TotalCardWeight);
            var cards = new List<RewardCard>();
            for (int i = 0; i < CardCount; i++)
            {
                int roll = rng.Next(0, total);
                var picked = def.cards[0];
                foreach (var entry in def.cards)
                {
                    if (roll < entry.weight) { picked = entry; break; }
                    roll -= entry.weight;
                }
                if (picked.itemId == DungeonDatabase.GearReward)
                {
                    string key = RollRaidGear(def, cls, tier, rng);
                    // No piece in the catalog for that roll: the raid's own material instead of an empty card.
                    cards.Add(key != null ? new RewardCard(key, 1) : new RewardCard(def.materialItem, 4));
                    continue;
                }
                cards.Add(new RewardCard(picked.itemId, rng.Next(picked.min, picked.max + 1)));
            }
            return cards;
        }

        /// <summary>A gear card: slot (even over the raid's slots), then grade by weight. Null if the catalog has no such piece.</summary>
        public static string RollRaidGear(RaidRewardDef def, CharacterClass cls, int tier, System.Random rng)
        {
            var category = def.gearCategories[rng.Next(0, def.gearCategories.Length)];
            int total = 0;
            foreach (var w in def.gearRarityWeights) total += w.weight;
            int roll = rng.Next(0, Mathf.Max(1, total));
            var rarity = def.gearRarityWeights[0].rarity;
            foreach (var w in def.gearRarityWeights)
            {
                if (roll < w.weight) { rarity = w.rarity; break; }
                roll -= w.weight;
            }
            return FindRaidGear(cls, tier, category, rarity);
        }

        /// <summary>The one piece of (tier, slot, class, grade) as a +0 key; a lower grade (down to Epic) if that one does not exist, else null.</summary>
        public static string FindRaidGear(CharacterClass cls, int tier, EquipCategory category, ItemRarity rarity)
        {
            for (var r = rarity; r >= ItemRarity.Epic; r--)
                foreach (var item in EquipmentDatabase.All)
                    if (!item.starter && !item.bossOnly && item.levelTier == tier && item.category == category && item.rarity == r && item.UsableBy(cls))
                        return EquipmentDatabase.KeyFor(item.id, 0);
            return null;
        }

        /// <summary>Chips of the select window for a raid: the sure gold, its material, gear, then what the cards can also give.</summary>
        static List<(string icon, string name, bool special)> RaidSlots(DungeonDef dungeon, CharacterClass cls)
        {
            var def = dungeon.raidReward;
            var slots = new List<(string, string, bool)>
            {
                (DungeonDatabase.ItemIcon(ConsumableDatabase.Gold), $"확정 골드\n{def.goldMin:N0}~{def.goldMax:N0}", true),
                (DungeonDatabase.ItemIcon(def.materialItem), DungeonDatabase.ItemName(def.materialItem), true),
                (GearIcon(cls, ItemRarity.Epic), $"{EquipmentDatabase.RarityName(ItemRarity.Epic)} 이상 장비", true),
            };
            var seen = new HashSet<string> { def.materialItem, DungeonDatabase.GearReward };
            foreach (var entry in def.cards)
                if (seen.Add(entry.itemId)) slots.Add((DungeonDatabase.ItemIcon(entry.itemId), DungeonDatabase.ItemName(entry.itemId), false));
            int cores = PromoteRules.CoreGain(dungeon, DungeonDifficulty.Normal);
            if (cores > 0) slots.Add((DungeonDatabase.ItemIcon(DungeonDatabase.RaidCore), $"고대의 핵 x{cores}", true));
            return slots;
        }

        static string RaidPreview(DungeonDef dungeon)
        {
            var def = dungeon.raidReward;
            var names = new List<string> { $"확정 골드 {def.goldMin:N0}~{def.goldMax:N0}", DungeonDatabase.ItemName(def.materialItem), "에픽 이상 장비" };
            foreach (var entry in def.cards)
            {
                string name = DungeonDatabase.ItemName(entry.itemId);
                if (entry.itemId != DungeonDatabase.GearReward && !names.Contains(name)) names.Add(name);
            }
            return string.Join(", ", names);
        }
    }
}
