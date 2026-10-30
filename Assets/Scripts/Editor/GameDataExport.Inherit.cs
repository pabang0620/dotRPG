using System.Linq;

namespace DotRPG.EditorTools
{
    /// <summary>[INHERIT] enhance.json "inheritTiers": the gear inheritance table (InheritRules), read by the server's inherit domain.</summary>
    public static partial class GameDataExport
    {
        /// <summary>
        /// { maxCount, failDrop, keepPercent[+level], stackPenalty[k], coreItem, shardItem, sealItem,
        /// rows: [{ from, to, gold[+level], cores?, shards?, seals? }] }. Gold is precomputed per +level so the server never redoes
        /// the float sums. Material counts are written only on the chapter 3 rows (Lv.40 -> 50).
        /// </summary>
        static void InheritJson(J j)
        {
            j.Key("inheritTiers").Obj().Num("maxCount", InheritRules.MaxCount).Num("failDrop", InheritRules.FailDrop);
            j.Arr("keepPercent", InheritRules.KeepPercents, (o, n) => o.Val(n));
            j.Arr("stackPenalty", InheritRules.Penalties, (o, n) => o.Val(n));
            j.Str("coreItem", InheritRules.CoreItem).Str("shardItem", InheritRules.ShardItem).Str("sealItem", InheritRules.SealItem);
            j.Arr("rows", InheritRules.Rows(), (o, r) =>
            {
                o.Obj().Str("from", r.from.id).Str("to", r.to.id);
                o.Arr("gold", Enumerable.Range(0, EquipmentDatabase.MaxEnhance + 1), (g, lv) => g.Val(InheritRules.GoldFor(r.from, r.to, lv)));
                var m = InheritRules.MaterialCost(r.to.reqLevel, r.to.rarity).Value;
                if (m.cores + m.shards + m.seals > 0) o.Num("cores", m.cores).Num("shards", m.shards).Num("seals", m.seals);
                return o.End();
            });
            j.End();
        }
    }
}
