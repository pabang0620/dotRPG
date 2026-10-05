using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>One face-down reward card: an item id (gear key, material, gold...) and a count.</summary>
    public struct RewardCard
    {
        public string itemId;
        public int count;

        public RewardCard(string itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }

        public string Label => count > 1 || itemId == ConsumableDatabase.Gold ? $"{DungeonDatabase.ItemName(itemId)} ×{count:N0}" : DungeonDatabase.ItemName(itemId);
    }

    /// <summary>Rolls the result cards from a dungeon's reward table × difficulty (plan §6.0 결과 화면).</summary>
    public static class DungeonRewards
    {
        public const int CardCount = 4;
        /// <summary>Weight given to gear without a monster drop weight (uniques, legendaries) in dungeon rolls.</summary>
        public const int RareGearWeight = 3;

        public static List<RewardCard> RollCards(DungeonDef dungeon, DifficultyDef diff, CharacterClass cls, System.Random rng)
        {
            var table = new List<RewardEntry>(dungeon.rewards);
            if (diff.ticketWeight > 0) table.Add(new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, diff.ticketWeight));
            int totalWeight = 0;
            foreach (var entry in table) totalWeight += Mathf.Max(0, entry.weight);
            var cards = new List<RewardCard>();
            for (int i = 0; i < CardCount; i++)
            {
                int roll = rng.Next(0, Mathf.Max(1, totalWeight));
                RewardEntry selectedReward = table[0];
                foreach (var entry in table)
                {
                    if (roll < entry.weight)
                    {
                        selectedReward = entry;
                        break;
                    }
                    roll -= entry.weight;
                }
                var card = Resolve(selectedReward, diff, cls, rng);
                // Jackpot: a very small chance per card of a piece above Epic (same order on the server).
                if (diff.jackpotPerMille > 0 && rng.Next(0, 1000) < diff.jackpotPerMille)
                {
                    string jackpot = RollJackpot(cls, rng);
                    if (jackpot != null) card = new RewardCard(jackpot, 1);
                }
                cards.Add(card);
            }
            return cards;
        }

        /// <summary>Weight of a Legendary vs a Unique in the jackpot pool.</summary>
        public const int JackpotUniqueWeight = 3, JackpotLegendaryWeight = 1;

        /// <summary>A Unique or Legendary piece the class can use (+0 key), or null if there is none.</summary>
        public static string RollJackpot(CharacterClass cls, System.Random rng)
        {
            var pool = new List<(string id, int weight)>();
            int total = 0;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.starter || !item.UsableBy(cls) || item.rarity < ItemRarity.Unique) continue;
                int w = item.rarity >= ItemRarity.Legendary ? JackpotLegendaryWeight : JackpotUniqueWeight;
                pool.Add((item.id, w));
                total += w;
            }
            if (total == 0) return null;
            int roll = rng.Next(0, total);
            foreach (var (id, w) in pool)
            {
                if (roll < w) return EquipmentDatabase.KeyFor(id, 0);
                roll -= w;
            }
            return null;
        }

        /// <summary>True for a Unique or Legendary gear card (the result screen makes it glow before it flips).</summary>
        public static bool IsJackpot(RewardCard card)
        {
            if (string.IsNullOrEmpty(card.itemId)) return false;
            var item = EquipmentDatabase.Get(card.itemId) ?? EquipmentDatabase.Get(EquipmentDatabase.BaseId(card.itemId));
            return item != null && item.rarity >= ItemRarity.Unique;
        }

        static RewardCard Resolve(RewardEntry e, DifficultyDef diff, CharacterClass cls, System.Random rng)
        {
            if (e.itemId == DungeonDatabase.GearReward) return new RewardCard(RollGear(cls, diff.minGearRarity, rng), 1);
            int count = rng.Next(e.min, e.max + 1);
            // Tickets never multiply; everything else scales with the difficulty.
            if (e.itemId != ConsumableDatabase.ProtectTicket) count = Mathf.Max(1, Mathf.RoundToInt(count * diff.rewardMul));
            return new RewardCard(e.itemId, count);
        }

        /// <summary>Tries of the normal monster drop roll before falling back to the dungeon pool.</summary>
        public const int DropTries = 12;

        /// <summary>
        /// A piece of gear (item key, +0) the class can use, at least <paramref name="minRarity"/>: the normal
        /// monster drop table (<see cref="EquipmentDatabase.RollDrop"/>) first, then a weighted pool that also
        /// holds the uniques and legendaries (which never drop from monsters).
        /// </summary>
        public static string RollGear(CharacterClass cls, ItemRarity minRarity, System.Random rng)
        {
            // The dungeon pool on every other card keeps the rare gear reachable.
            if (rng.Next(0, 2) == 0)
            {
                for (int i = 0; i < DropTries; i++)
                {
                    string id = EquipmentDatabase.RollDrop(cls, 1f);
                    var item = EquipmentDatabase.Get(id);
                    if (item != null && item.rarity >= minRarity) return EquipmentDatabase.KeyFor(id, 0);
                }
            }
            var pool = new List<(string id, int weight)>();
            int totalWeight = 0;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.starter || !item.UsableBy(cls) || item.rarity < minRarity) continue;
                int weight = item.dropWeight > 0 ? item.dropWeight : RareGearWeight;
                pool.Add((item.id, weight));
                totalWeight += weight;
            }
            if (pool.Count == 0) return EquipmentDatabase.RollDrop(cls, 1f) ?? EquipmentDatabase.StarterWeapon(cls);
            int roll = rng.Next(0, totalWeight);
            foreach (var (id, weight) in pool)
            {
                if (roll < weight) return id;
                roll -= weight;
            }
            return pool[0].id;
        }

        /// <summary>Clear XP: base × difficulty × specialty, plus the rank bonus.</summary>
        public static int ClearXp(DungeonDef dungeon, DifficultyDef diff, DungeonRank rank)
        {
            float xp = Mathf.Max(dungeon.clearXp * diff.rewardMul, HuntingGrounds.DungeonClearFloor(dungeon, diff)) * dungeon.xpMul;
            return Mathf.RoundToInt(xp * (1f + DungeonRanking.XpBonusPercent(rank) / 100f));
        }

        /// <summary>Short text of what the table can give ("골드, 뼈 조각, 장비").</summary>
        /// <summary>What a reward card can be, as slots for the select window: icon key, short name, highlighted.</summary>
        public static List<(string icon, string name, bool special)> Slots(DungeonDef dungeon, DifficultyDef diff, CharacterClass cls)
        {
            var slots = new List<(string, string, bool)>();
            var seen = new HashSet<string>();
            foreach (var entry in dungeon.rewards)
            {
                if (!seen.Add(entry.itemId)) continue;
                if (entry.itemId == DungeonDatabase.GearReward)
                {
                    slots.Add((GearIcon(cls, diff.minGearRarity), $"{EquipmentDatabase.RarityName(diff.minGearRarity)}+ 장비", false));
                    continue;
                }
                slots.Add((DungeonDatabase.ItemIcon(entry.itemId), DungeonDatabase.ItemName(entry.itemId), false));
            }
            if (diff.ticketWeight > 0) slots.Add((DungeonDatabase.ItemIcon(ConsumableDatabase.ProtectTicket), DungeonDatabase.ItemName(ConsumableDatabase.ProtectTicket), true));
            if (diff.jackpotPerMille > 0) slots.Add((GearIcon(cls, ItemRarity.Legendary), "유니크·레전더리", true));
            return slots;
        }

        /// <summary>Icon of the class's weapon at (or nearest above) a grade, standing for "a piece of gear".</summary>
        static string GearIcon(CharacterClass cls, ItemRarity min)
        {
            EquipmentItem best = null;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.starter || !item.UsableBy(cls) || item.rarity < min) continue;
                if (best == null || item.rarity < best.rarity || (item.category == EquipCategory.Weapon && best.category != EquipCategory.Weapon && item.rarity == best.rarity)) best = item;
            }
            return best != null ? best.iconKey : "icon_chest";
        }

        public static string Preview(DungeonDef dungeon, DifficultyDef diff)
        {
            var names = new List<string>();
            foreach (var entry in dungeon.rewards)
            {
                string name = DungeonDatabase.ItemName(entry.itemId);
                if (!names.Contains(name)) names.Add(name);
            }
            if (diff.ticketWeight > 0) names.Add("장비 보호권");
            return string.Join(", ", names);
        }
    }
}
