using System;

namespace DotRPG
{
    /// <summary>What a failed enhancement attempt does to the gear.</summary>
    public enum EnhanceFailure
    {
        /// <summary>Attempts from +0..+9: the level stays.</summary>
        Keep,
        /// <summary>Weapon attempts from +10 / +11: the level drops by 3.</summary>
        Drop3,
        /// <summary>Weapon from +12, other gear from +10: the item is destroyed (a protection ticket resets it to +0 instead).</summary>
        Destroy,
    }

    /// <summary>How an enhancement attempt ended (see <see cref="Equipment.TryEnhance"/>).</summary>
    public enum EnhanceOutcome
    {
        Success,
        /// <summary>Failed, level kept.</summary>
        Keep,
        /// <summary>Failed, level −3.</summary>
        Drop3,
        /// <summary>Failed, the item is gone.</summary>
        Destroyed,
        /// <summary>Failed, a protection ticket was used up and the item is back at +0.</summary>
        Protected,
        /// <summary>Not enough gold or materials: nothing happened.</summary>
        NotEnough,
        /// <summary>Already at <see cref="EquipmentDatabase.MaxEnhance"/>: nothing happened.</summary>
        MaxLevel,
        /// <summary>No gear at the target (empty slot, key not in the bag): nothing happened.</summary>
        Invalid,
    }

    /// <summary>The piece of gear an attempt is aimed at: a worn slot or a key in the bag.</summary>
    public readonly struct EnhanceTarget
    {
        public readonly EquipSlot? slot;
        public readonly string bagKey;

        EnhanceTarget(EquipSlot? slot, string bagKey)
        {
            this.slot = slot;
            this.bagKey = bagKey;
        }

        public static EnhanceTarget Worn(EquipSlot slot) => new EnhanceTarget(slot, null);
        public static EnhanceTarget Bag(string key) => new EnhanceTarget(null, key);

        public bool IsWorn => slot.HasValue;

        public override string ToString() => slot.HasValue ? $"worn {slot.Value}" : $"bag {bagKey}";
    }

    /// <summary>Price, chance and risk of one attempt from +level to +level+1.</summary>
    public struct EnhanceCost
    {
        /// <summary>The level attempted from.</summary>
        public int level;
        public int gold, bone, ore, essence;
        /// <summary>Chance from the table, before pity.</summary>
        public int basePercent;
        /// <summary>Extra %p earned by earlier failures from this key (weapons at +10 / +11).</summary>
        public int pityBonus;
        /// <summary>basePercent + pityBonus, capped at 100.</summary>
        public int successPercent;
        /// <summary>What a failure would do.</summary>
        public EnhanceFailure failure;
        /// <summary>True when a failure would destroy the item but a protection ticket in the bag will save it (at +0). Never for starter gear.</summary>
        public bool usesTicket;

        /// <summary>Level after a <see cref="EnhanceFailure.Drop3"/> failure.</summary>
        public int DroppedLevel => Math.Max(0, level - 3);
    }

    /// <summary>What happened in one attempt.</summary>
    public struct EnhanceResult
    {
        public EnhanceOutcome kind;
        /// <summary>Key before the attempt, and after it (null = destroyed).</summary>
        public string oldKey, newKey;
        public int oldLevel, newLevel;
        /// <summary>Worn slot the gear was in (null = bag).</summary>
        public EquipSlot? slot;
        /// <summary>Price and risk of the attempt (paid unless NotEnough / MaxLevel / Invalid).</summary>
        public EnhanceCost cost;
        /// <summary>The 0..99 roll that decided it.</summary>
        public int roll;

        /// <summary>True when gold and materials were spent (the attempt really happened).</summary>
        public bool Attempted => kind <= EnhanceOutcome.Protected;
    }

    /// <summary>
    /// Enhancement rules. Pure functions with no randomness or game state: callers pass the 0..99 roll, so
    /// every rule can be tested. Execution (paying, applying, pity bookkeeping) is <see cref="Equipment.TryEnhance"/>.
    /// </summary>
    public static class EnhanceRules
    {
        public const string Bone = "mat_bone", Ore = "mat_ore", Essence = "mat_essence";
        /// <summary>Pity never grows past this many %p.</summary>
        public const int MaxPity = 100;

        /// <summary>Success % for +L → +L+1, L = 0..19.</summary>
        static readonly int[] Chance = { 100, 100, 100, 100, 80, 70, 60, 50, 40, 30, 25, 15, 14, 13, 12, 11, 10, 10, 10, 10 };
        /// <summary>Gold multiplier per level attempted from.</summary>
        static readonly double[] GoldMul = { 1, 1, 1, 1, 2, 2.2, 2.4, 2.6, 2.8, 3, 3, 5, 16, 24, 34, 46, 60, 64, 68, 72 };
        /// <summary>Base gold per attempt by level tier (Lv.1, 10, 15 ... 40; weapons, other gear pays 80%).</summary>
        static readonly int[] GoldByTier = { 20, 30, 40, 55, 70, 85, 100, 120 };

        static int Clamp(int level) => Math.Max(0, Math.Min(EquipmentDatabase.MaxEnhance - 1, level));

        /// <summary>Table chance in % to go from +level to +level+1 (0 at or past the max level).</summary>
        public static int SuccessPercent(int level) => level >= 0 && level < Chance.Length ? Chance[level] : 0;

        /// <summary>A 0..99 roll succeeds when it is below the chance in %.</summary>
        public static bool Succeeds(int roll, int percent) => roll < percent;

        /// <summary>Weapons attempting from +10 or +11 gain +1%p per failure at that level (reset on success).</summary>
        public static bool HasPity(EquipmentItem item, int level) =>
            item != null && item.category == EquipCategory.Weapon && (level == 10 || level == 11);

        /// <summary>What a failed attempt from this level does.</summary>
        public static EnhanceFailure FailureFor(EquipmentItem item, int level)
        {
            if (level <= 9) return EnhanceFailure.Keep;
            if (item != null && item.category == EquipCategory.Weapon) return level <= 11 ? EnhanceFailure.Drop3 : EnhanceFailure.Destroy;
            return EnhanceFailure.Destroy;
        }

        /// <summary>Gold per attempt: round(x × multiplier[level]), x by the item's level tier (×0.8 for non-weapons).</summary>
        public static int GoldFor(EquipmentItem item, int level)
        {
            int tier = Math.Max(0, Math.Min(GoldByTier.Length - 1, item.levelTier));
            double x = GoldByTier[tier] * (item.category == EquipCategory.Weapon ? 1.0 : 0.8);
            return (int)Math.Round(x * GoldMul[Clamp(level)], MidpointRounding.AwayFromZero);
        }

        /// <summary>뼈 조각 per attempt: L + 1 up to +9, then 12 + 2 per level past +10.</summary>
        public static int BoneFor(int level) => level <= 9 ? level + 1 : 12 + 2 * (level - 10);

        /// <summary>강화석 per attempt: none below +4, (L − 2) / 2 up to +9, 4 at +10, 5 at +11, then 6 + (L − 12).</summary>
        public static int OreFor(int level) =>
            level < 4 ? 0 : level <= 9 ? (level - 2) / 2 : level == 10 ? 4 : level == 11 ? 5 : 6 + (level - 12);

        /// <summary>마력 정수 per attempt: L − 9 from +10 (the attempt for +11).</summary>
        public static int EssenceFor(int level) => level >= 10 ? level - 9 : 0;

        /// <summary>
        /// Everything about one attempt from +level: price, chance (table + <paramref name="pity"/> %p for
        /// weapons at +10 / +11, capped at 100) and the failure risk (<paramref name="hasTicket"/> = the bag
        /// holds a protection ticket). At the max level the cost is empty with a 0% chance.
        /// </summary>
        public static EnhanceCost CostFor(EquipmentItem item, int level, int pity, bool hasTicket)
        {
            level = Math.Max(0, Math.Min(EquipmentDatabase.MaxEnhance, level));
            var c = new EnhanceCost { level = level };
            if (item == null || level >= EquipmentDatabase.MaxEnhance) return c;
            c.gold = GoldFor(item, level);
            c.bone = BoneFor(level);
            c.ore = OreFor(level);
            c.essence = EssenceFor(level);
            c.basePercent = SuccessPercent(level);
            c.pityBonus = HasPity(item, level) ? Math.Max(0, Math.Min(MaxPity, pity)) : 0;
            c.successPercent = Math.Min(100, c.basePercent + c.pityBonus);
            c.failure = FailureFor(item, level);
            // Starter gear is handed out again for free, so a ticket is never spent on it.
            c.usesTicket = c.failure == EnhanceFailure.Destroy && hasTicket && !item.starter;
            return c;
        }
    }
}
