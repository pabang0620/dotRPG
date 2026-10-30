using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>State of an inheritance preview or attempt.</summary>
    public enum InheritOutcome
    {
        /// <summary>Preview only: the piece can be inherited.</summary>
        Ready,
        /// <summary>The inheritance happened (see <see cref="InheritResult.kept"/>).</summary>
        Done,
        /// <summary>Not enough gold: nothing happened.</summary>
        NotEnough,
        /// <summary>A worn piece whose new band is above the wearer's level: nothing happened.</summary>
        LevelTooLow,
        /// <summary>[CH3] The new band is chapter 3 gear and the chapter is still closed: nothing happened.</summary>
        Locked,
        /// <summary>No inheritable gear at the target: nothing happened.</summary>
        Invalid,
    }

    /// <summary>Preview and result of one inheritance.</summary>
    public struct InheritResult
    {
        public InheritOutcome kind;
        public string oldKey, newKey;
        /// <summary>The piece it becomes.</summary>
        public EquipmentItem to;
        public int oldLevel, newLevel;
        /// <summary>Inheritance count before (k) and after (k + 1).</summary>
        public int oldCount, newCount;
        /// <summary>Keep chance in %, and the +level when kept / when it fails.</summary>
        public int keepPercent, keptLevel, failLevel;
        public int gold;
        /// <summary>[CH3] Extra materials of the Lv.40 -> 50 step (0 on the other steps).</summary>
        public int cores, shards, seals;
        public bool kept;
        public int roll;
    }

    /// <summary>
    /// Gear inheritance (계승, Docs/PLAN_GEAR_INHERIT_2026-10-22.md): a piece moves to the same class / slot / grade / line of
    /// the next level band (Lv.10 -> 20, 20 -> 30, 30 -> 40, and chapter 3's 40 -> 50) and tries to carry its +level.
    /// Kept with <see cref="KeepPercent"/> (by the +level carried), else -1; a piece that was already inherited loses
    /// <see cref="StackPenalty"/> more either way. The +level never goes up. Each piece records how often it was inherited
    /// (the "^k" part of its key, <see cref="EquipmentDatabase.InheritOfKey"/>). Pure rules, the roll is passed in; the server
    /// rolls online and reads the same table from enhance.json ("inheritTiers").
    /// [CH3] Lv.40 -> 50 (Docs/ch3/PROGRESSION_AND_GEAR.md 4): only unique and legendary pieces, same gold rule plus
    /// <see cref="MaterialCost"/>, and only while chapter 3 is open (the Lv.50 piece carries the chapter 3 tag).
    /// </summary>
    public static class InheritRules
    {
        /// <summary>Level band a piece inherits from -> the band it goes to (40 -> 50 is chapter 3).</summary>
        public static readonly int[] FromLevels = { 10, 20, 30, 40 };
        public static readonly int[] ToLevels = { 20, 30, 40, 50 };
        /// <summary>[CH3] The band whose step asks for chapter 3 materials and only takes unique / legendary pieces.</summary>
        public const int Chapter3ToLevel = 50;
        /// <summary>Highest inheritance count a key can carry ("^1".."^4"; four steps from Lv.10 reach Lv.50).</summary>
        public const int MaxCount = 4;
        /// <summary>A failed inheritance loses this many levels.</summary>
        public const int FailDrop = 1;
        /// <summary>Cost = share x new band's weapon base gold x (units(N) - units(free level)).</summary>
        public const double CostShare = 0.25;
        /// <summary>Levels up to this are carried for free and always kept.</summary>
        public const int FreeLevel = 10;
        /// <summary>Non-weapon gear pays this % (same as enhancement).</summary>
        public const int NonWeaponGoldPercent = 80;

        /// <summary>Keep chance in % by the +level carried (index = +level, 0..20).</summary>
        static readonly int[] KeepTable = { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 90, 80, 70, 55, 35, 30, 25, 22, 15, 10 };
        /// <summary>Extra levels lost by the inheritance count before this one (k = 0, 1 and up).</summary>
        static readonly int[] PenaltyTable = { 0, 2 };

        public static IReadOnlyList<int> KeepPercents => KeepTable;
        public static IReadOnlyList<int> Penalties => PenaltyTable;

        public static int KeepPercent(int level) => KeepTable[Math.Max(0, Math.Min(KeepTable.Length - 1, level))];

        /// <summary>Extra loss for a piece inherited k times before (the last table value repeats).</summary>
        public static int StackPenalty(int k) => PenaltyTable[Math.Max(0, Math.Min(PenaltyTable.Length - 1, k))];

        /// <summary>A 0..99 roll keeps the level when it is below the keep chance.</summary>
        public static bool Keeps(int roll, int level) => roll < KeepPercent(level);

        /// <summary>+level after inheriting +level with count k: kept or -1, then the stack penalty, never below 0.</summary>
        public static int ResultLevel(int level, int k, bool kept) => Math.Max(0, (kept ? level : level - FailDrop) - StackPenalty(k));

        /// <summary>Expected enhancement cost units up to +level: sum of gold multiplier x 100 / chance (attempts only).</summary>
        public static double CostUnits(int level)
        {
            double sum = 0;
            for (int i = 0; i < level && i < EquipmentDatabase.MaxEnhance; i++)
                sum += EnhanceRules.GoldMulAt(i) * 100.0 / EnhanceRules.SuccessPercent(i);
            return sum;
        }

        /// <summary>
        /// Gold to inherit a +level piece <paramref name="from"/> into <paramref name="to"/>: the enhancement share above (0 up to +10),
        /// but never less than the shop price gap of the two pieces. Without the floor a shop piece bought cheap at Lv.10 walked
        /// to a Lv.40 shop piece for free at low +levels (the gap is 0 for gear the shop does not sell).
        /// </summary>
        public static int GoldFor(EquipmentItem from, EquipmentItem to, int level)
        {
            if (to == null) return 0;
            int floor = ShopGap(from, to);
            if (level <= FreeLevel) return floor;
            double x = EnhanceRules.TierGold(to.levelTier) * (to.category == EquipCategory.Weapon ? 1.0 : NonWeaponGoldPercent / 100.0);
            int formula = (int)Math.Round(CostShare * x * (CostUnits(level) - CostUnits(FreeLevel)), MidpointRounding.AwayFromZero);
            return Math.Max(formula, floor);
        }

        /// <summary>Shop buy price of <paramref name="to"/> minus that of <paramref name="from"/> (0 when either is not sold).</summary>
        public static int ShopGap(EquipmentItem from, EquipmentItem to)
        {
            if (from == null || to == null) return 0;
            int a = ItemPrices.BuyPrice(from.id), b = ItemPrices.BuyPrice(to.id);
            return a > 0 && b > a ? b - a : 0;
        }

        /// <summary>
        /// [CH3] Extra materials of an inheritance into <paramref name="toLevel"/>: 고대의 핵, 침식 결정 (지바크) and 잿빛 인장
        /// (카마츠) for Lv.50 unique / legendary pieces, so a Lv.40 holder still runs both chapter 3 raids. Null when the grade
        /// does not inherit into that band; (0, 0, 0) on the launch steps.
        /// </summary>
        public static (int cores, int shards, int seals)? MaterialCost(int toLevel, ItemRarity rarity)
        {
            if (toLevel != Chapter3ToLevel) return (0, 0, 0);
            return rarity switch
            {
                ItemRarity.Unique => (3, 5, 10),
                ItemRarity.Legendary => (6, 10, 20),
                _ => null,
            };
        }

        /// <summary>[CH3] Item ids of the Lv.40 -> 50 materials (core, shard, seal).</summary>
        public static string CoreItem => PromoteRules.CoreItem;
        public static string ShardItem => RaidRewards.RaidMatXibak;
        public static string SealItem => RaidRewards.RaidMatKamatz;

        /// <summary>True when <paramref name="bag"/> holds the preview's gold and materials.</summary>
        public static bool Affords(InheritResult p, Inventory bag) =>
            bag.Count(ConsumableDatabase.Gold) >= p.gold && bag.Count(CoreItem) >= p.cores && bag.Count(ShardItem) >= p.shards && bag.Count(SealItem) >= p.seals;

        /// <summary>The band a level inherits into (0 = none).</summary>
        public static int NextLevel(int level)
        {
            int i = Array.IndexOf(FromLevels, level);
            return i >= 0 ? ToLevels[i] : 0;
        }

        /// <summary>
        /// The piece this one inherits into: same kind, grade and line ("b" = hunter legendary) one band up, found by the
        /// catalog id rule eq_{kind}_{level}_{grade}. Null for starter and field boss gear, other bands, or a missing grade
        /// (Lv.20 legendaries have no Lv.30 counterpart).
        /// </summary>
        public static EquipmentItem TargetOf(EquipmentItem item)
        {
            if (item == null || item.starter || item.bossOnly) return null;
            int next = NextLevel(item.reqLevel);
            if (next == 0 || MaterialCost(next, item.rarity) == null) return null;
            string from = "_" + item.reqLevel + "_", to = "_" + next + "_";
            int at = item.id.IndexOf(from, StringComparison.Ordinal);
            if (at < 0) return null;
            var target = EquipmentDatabase.Get(item.id.Substring(0, at) + to + item.id.Substring(at + from.Length));
            return target != null && !target.starter && !target.bossOnly && target.rarity == item.rarity && target.category == item.category && target.classOnly == item.classOnly ? target : null;
        }

        /// <summary>True when a key can be inherited (target exists and the count still fits in a key).</summary>
        public static bool CanInherit(string key) => TargetOf(EquipmentDatabase.Get(key)) != null && EquipmentDatabase.InheritOfKey(key) < MaxCount;

        /// <summary>Everything the forge shows before inheriting a key (kind Invalid when it cannot be inherited).</summary>
        public static InheritResult Preview(string key)
        {
            var r = new InheritResult { kind = InheritOutcome.Invalid, oldKey = key, newKey = key };
            var item = EquipmentDatabase.Get(key);
            r.to = TargetOf(item);
            r.oldLevel = r.newLevel = EquipmentDatabase.LevelOfKey(key);
            r.oldCount = EquipmentDatabase.InheritOfKey(key);
            if (r.to == null || r.oldCount >= MaxCount) return r;
            r.newCount = r.oldCount + 1;
            r.keepPercent = KeepPercent(r.oldLevel);
            r.keptLevel = ResultLevel(r.oldLevel, r.oldCount, true);
            r.failLevel = ResultLevel(r.oldLevel, r.oldCount, false);
            r.gold = GoldFor(item, r.to, r.oldLevel);
            var m = MaterialCost(r.to.reqLevel, r.to.rarity).Value;
            r.cores = m.cores;
            r.shards = m.shards;
            r.seals = m.seals;
            r.kind = r.to.chapter >= 3 && !Chapter3.Enabled ? InheritOutcome.Locked : InheritOutcome.Ready;
            return r;
        }

        /// <summary>Every inheritance row (exported for the server).</summary>
        public static IEnumerable<(EquipmentItem from, EquipmentItem to)> Rows()
        {
            foreach (var item in EquipmentDatabase.All)
            {
                var to = TargetOf(item);
                if (to != null) yield return (item, to);
            }
        }

        /// <summary>True for items a key with an inheritance count can name (results of an inheritance, and their promotions).</summary>
        public static bool CanCarryCount(EquipmentItem item) =>
            item != null && !item.starter && !item.bossOnly && Array.IndexOf(ToLevels, item.reqLevel) >= 0;
    }
}
