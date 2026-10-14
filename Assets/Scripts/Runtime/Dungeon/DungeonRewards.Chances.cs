using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [REWARD 2026-10-14] Unique / Legendary gear chances shown when the player clicks the reward preview or a rare
    /// card. The same formula as the server's rareChances (server/src/domains/dungeons/dungeonRules.ts, checked there
    /// against samples of the real rolls): raids 4 cards from the raid table; dungeons the luck cards after the sure
    /// material and gold, each card a jackpot (Unique only) or a gear card whose roll can land on a Unique.
    /// </summary>
    public static partial class DungeonRewards
    {
        public struct RareChance
        {
            public float uniqueCard, uniqueClear, legendCard, legendClear;
            public int cards;
        }

        public static RareChance RareChances(DungeonDef dungeon, DifficultyDef diff, CharacterClass cls)
        {
            var rr = dungeon.raidReward;
            if (dungeon.isRaid && rr != null)
            {
                float gear = 0f;
                foreach (var e in rr.cards) if (e.itemId == DungeonDatabase.GearReward) gear += e.weight;
                gear /= Mathf.Max(1, rr.TotalCardWeight);
                float sum = 0f, unique = 0f, legend = 0f;
                foreach (var w in rr.gearRarityWeights)
                {
                    sum += w.weight;
                    if (w.rarity == ItemRarity.Unique) unique += w.weight;
                    if (w.rarity == ItemRarity.Legendary) legend += w.weight;
                }
                sum = Mathf.Max(1f, sum);
                return Build(gear * unique / sum, gear * legend / sum, CardCount);
            }
            int tier = GearCatalog.TierOfLevel(diff.recommendedLevel);
            float total = 0f, gearW = 0f;
            foreach (var e in dungeon.rewards)
            {
                total += Mathf.Max(0, e.weight);
                if (e.itemId == DungeonDatabase.GearReward) gearW += e.weight;
            }
            if (diff.ticketWeight > 0) total += diff.ticketWeight;
            float gearCard = gearW / Mathf.Max(1f, total);
            // RollGear: half the time the drop table (DropTries tries, the first piece at the minimum grade or above), else and on a miss the weighted pool.
            float dropTotal = 0f;
            var usable = new List<EquipmentItem>();
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.bossOnly || item.levelTier != tier || item.rarity >= ItemRarity.Legendary || !item.UsableBy(cls)) continue;
                usable.Add(item);
                if (item.dropWeight > 0) dropTotal += item.dropWeight;
            }
            float qOk = 0f, qUnique = 0f, poolTotal = 0f, poolUnique = 0f;
            foreach (var item in usable)
            {
                if (item.dropWeight > 0 && item.rarity >= diff.minGearRarity)
                {
                    qOk += item.dropWeight / dropTotal;
                    if (item.rarity == ItemRarity.Unique) qUnique += item.dropWeight / dropTotal;
                }
                if (!item.starter && item.rarity >= diff.minGearRarity)
                {
                    float w = item.dropWeight > 0 ? item.dropWeight : RareGearWeight;
                    poolTotal += w;
                    if (item.rarity == ItemRarity.Unique) poolUnique += w;
                }
            }
            float dropHit = dropTotal > 0f && qOk > 0f ? 1f - Mathf.Pow(1f - qOk, DropTries) : 0f;
            float fromDrop = qOk > 0f ? qUnique / qOk * dropHit : 0f;
            float fromPool = poolTotal > 0f ? poolUnique / poolTotal : 0f;
            float uniqueOfGear = 0.5f * fromDrop + (0.5f + 0.5f * (1f - dropHit)) * fromPool;
            bool jackpotPool = false;
            foreach (var item in EquipmentDatabase.All)
                if (!item.starter && !item.bossOnly && item.levelTier == tier && item.rarity == ItemRarity.Unique && item.UsableBy(cls)) { jackpotPool = true; break; }
            float pj = jackpotPool ? diff.jackpotPerMille / 1000f : 0f;
            return Build(pj + (1f - pj) * gearCard * uniqueOfGear, 0f, dungeon.sure != null ? DungeonDatabase.LuckCards : 1);
        }

        static RareChance Build(float unique, float legend, int cards) => new RareChance
        {
            uniqueCard = unique, uniqueClear = 1f - Mathf.Pow(1f - unique, cards),
            legendCard = legend, legendClear = 1f - Mathf.Pow(1f - legend, cards), cards = cards,
        };

        static string Pct(float p) => p <= 0f ? "0%" : (p * 100f).ToString(p * 100f < 0.01f ? "0.####" : "0.###") + "%";

        /// <summary>"유니크 확률: 클리어당 1.2% (카드당 0.6%)" and the Legendary line (dungeons: raids only).</summary>
        public static string RareChanceText(DungeonDef dungeon, DifficultyDef diff, CharacterClass cls)
        {
            var c = RareChances(dungeon, diff, cls);
            string u = L.F("유니크 확률: 클리어당 {0} (카드당 {1})", Pct(c.uniqueClear), Pct(c.uniqueCard));
            string l = dungeon.isRaid ? L.F("레전더리 확률: 클리어당 {0} (카드당 {1})", Pct(c.legendClear), Pct(c.legendCard)) : L.T("레전더리: 레이드에서만 나옵니다");
            return u + "\n" + l;
        }
    }
}
