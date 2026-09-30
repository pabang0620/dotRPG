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
        const int RareGearWeight = 3;

        public static List<RewardCard> RollCards(DungeonDef dungeon, DifficultyDef diff, CharacterClass cls, System.Random rng)
        {
            var table = new List<RewardEntry>(dungeon.rewards);
            if (diff.ticketWeight > 0) table.Add(new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, diff.ticketWeight));
            int total = 0;
            foreach (var e in table) total += Mathf.Max(0, e.weight);
            var cards = new List<RewardCard>();
            for (int i = 0; i < CardCount; i++)
            {
                int roll = rng.Next(0, Mathf.Max(1, total));
                RewardEntry pick = table[0];
                foreach (var e in table)
                {
                    if (roll < e.weight) { pick = e; break; }
                    roll -= e.weight;
                }
                cards.Add(Resolve(pick, diff, cls, rng));
            }
            return cards;
        }

        static RewardCard Resolve(RewardEntry e, DifficultyDef diff, CharacterClass cls, System.Random rng)
        {
            if (e.itemId == DungeonDatabase.GearReward) return new RewardCard(RollGear(cls, diff.minGearRarity, rng), 1);
            int n = rng.Next(e.min, e.max + 1);
            // Tickets never multiply; everything else scales with the difficulty.
            if (e.itemId != ConsumableDatabase.ProtectTicket) n = Mathf.Max(1, Mathf.RoundToInt(n * diff.rewardMul));
            return new RewardCard(e.itemId, n);
        }

        /// <summary>A piece of gear the class can use, at least <paramref name="minRarity"/> (weighted by drop weight).</summary>
        public static string RollGear(CharacterClass cls, ItemRarity minRarity, System.Random rng)
        {
            var pool = new List<(string id, int w)>();
            int total = 0;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.starter || !item.UsableBy(cls) || item.rarity < minRarity) continue;
                int w = item.dropWeight > 0 ? item.dropWeight : RareGearWeight;
                pool.Add((item.id, w));
                total += w;
            }
            if (pool.Count == 0) return EquipmentDatabase.RollDrop(cls, 1f) ?? EquipmentDatabase.StarterWeapon(cls);
            int roll = rng.Next(0, total);
            foreach (var (id, w) in pool)
            {
                if (roll < w) return id;
                roll -= w;
            }
            return pool[0].id;
        }

        /// <summary>Clear XP: base × difficulty × specialty, plus the rank bonus.</summary>
        public static int ClearXp(DungeonDef dungeon, DifficultyDef diff, DungeonRank rank)
        {
            float xp = dungeon.clearXp * diff.rewardMul * dungeon.xpMul;
            return Mathf.RoundToInt(xp * (1f + DungeonRanking.XpBonusPercent(rank) / 100f));
        }

        /// <summary>Short text of what the table can give ("골드, 뼈 조각, 장비").</summary>
        public static string Preview(DungeonDef dungeon, DifficultyDef diff)
        {
            var names = new List<string>();
            foreach (var e in dungeon.rewards)
            {
                string n = DungeonDatabase.ItemName(e.itemId);
                if (!names.Contains(n)) names.Add(n);
            }
            if (diff.ticketWeight > 0) names.Add("<color=#ffd84a>장비 보호권(낮은 확률)</color>");
            return string.Join(", ", names);
        }
    }
}
