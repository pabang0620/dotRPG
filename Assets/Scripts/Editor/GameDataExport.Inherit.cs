using System.Linq;

namespace DotRPG.EditorTools
{
    /// <summary>[INHERIT] enhance.json "inheritTiers": the gear inheritance table (InheritRules), read by the server's inherit domain.</summary>
    public static partial class GameDataExport
    {
        /// <summary>
        /// { maxCount, failDrop, keepPercent[+level], stackPenalty[k], rows: [{ from, to, gold[+level] }] }. Gold is precomputed per
        /// +level so the server never redoes the float sums. Named "inheritTiers" so it does not clash with chapter 3's promote.inherit.
        /// </summary>
        static void InheritJson(J j)
        {
            j.Key("inheritTiers").Obj().Num("maxCount", InheritRules.MaxCount).Num("failDrop", InheritRules.FailDrop);
            j.Arr("keepPercent", InheritRules.KeepPercents, (o, n) => o.Val(n));
            j.Arr("stackPenalty", InheritRules.Penalties, (o, n) => o.Val(n));
            j.Arr("rows", InheritRules.Rows(), (o, r) =>
            {
                o.Obj().Str("from", r.from.id).Str("to", r.to.id);
                o.Arr("gold", Enumerable.Range(0, EquipmentDatabase.MaxEnhance + 1), (g, lv) => g.Val(InheritRules.GoldFor(r.to, lv)));
                return o.End();
            });
            j.End();
        }
    }
}
