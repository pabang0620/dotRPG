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
        public bool kept;
        public int roll;
    }

    /// <summary>
    /// Gear inheritance (계승, Docs/PLAN_GEAR_INHERIT_2026-10-22.md): a piece moves to the same class / slot / grade / line of
    /// the next level band (Lv.10 -> 20, 20 -> 30, 30 -> 40; the 40 -> 50 row belongs to chapter 3) and tries to carry its +level.
    /// Kept with <see cref="KeepPercent"/> (by the +level carried), else -1; a piece that was already inherited loses
    /// <see cref="StackPenalty"/> more either way. The +level never goes up. Each piece records how often it was inherited
    /// (the "^k" part of its key, <see cref="EquipmentDatabase.InheritOfKey"/>). Pure rules, the roll is passed in; the server
    /// rolls online and reads the same table from enhance.json ("inheritTiers").
    /// </summary>
    public static class InheritRules
    {
        /// <summary>Level band a piece inherits from -> the band it goes to. Chapter 3 appends 40 -> 50.</summary>
        public static readonly int[] FromLevels = { 10, 20, 30 };
        public static readonly int[] ToLevels = { 20, 30, 40 };
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

        /// <summary>Gold to inherit a +level piece into <paramref name="to"/> (0 up to +10).</summary>
        public static int GoldFor(EquipmentItem to, int level)
        {
            if (to == null || level <= FreeLevel) return 0;
            double x = EnhanceRules.TierGold(to.levelTier) * (to.category == EquipCategory.Weapon ? 1.0 : NonWeaponGoldPercent / 100.0);
            return (int)Math.Round(CostShare * x * (CostUnits(level) - CostUnits(FreeLevel)), MidpointRounding.AwayFromZero);
        }

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
            if (next == 0) return null;
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
            r.gold = GoldFor(r.to, r.oldLevel);
            r.kind = InheritOutcome.Ready;
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
