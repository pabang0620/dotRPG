namespace DotRPG.EditorTools
{
    /// <summary>[SERVER] Raid reward tables (dungeons.json "raidReward") and the raid shop (raid_shop.json), Docs/server/phase13_raid_rewards.md §5.4, §6.1.</summary>
    public static partial class GameDataExport
    {
        /// <summary>The "raidReward" object of a raid in dungeons.json: sure gold, own material, gear rule and the weighted card table.</summary>
        static void RaidRewardJson(J o, RaidRewardDef r)
        {
            o.Key("raidReward").Obj()
                .Num("goldMin", r.goldMin).Num("goldMax", r.goldMax).Num("goldStep", r.goldStep).Str("materialItem", r.materialItem);
            o.Arr("gearCategories", r.gearCategories, (x, c) => x.Val(c.ToString()));
            o.Key("gearRarityWeights").Obj();
            foreach (var w in r.gearRarityWeights) o.Num(w.rarity.ToString(), w.weight);
            o.End();
            o.Arr("cards", r.cards, (x, c) => x.Obj().Str("itemId", c.itemId).Num("min", c.min).Num("max", c.max).Num("weight", c.weight).End());
            o.End();
        }

        /// <summary>raid_shop.json: { schema, ratesVersion, products: [{ id, raidId, materialItem, price, category, rarityWeights }] }.</summary>
        static string RaidShopJson()
        {
            var j = Doc().Str("ratesVersion", RaidShop.RatesVersion);
            j.Arr("products", RaidShop.Products, (o, p) =>
            {
                o.Obj().Str("id", p.id).Str("raidId", p.raidId).Str("materialItem", p.materialItem).Num("price", p.price).Str("category", p.category.ToString());
                o.Key("rarityWeights").Obj();
                foreach (var w in p.rarityWeights) o.Num(w.rarity.ToString(), w.weight);
                o.End();
                return o.End();
            });
            return j.End().ToString();
        }
    }
}
