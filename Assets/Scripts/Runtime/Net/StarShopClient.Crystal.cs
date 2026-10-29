using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>One Starlight Exchange good (server EXCHANGE_GOODS). left = weekly stock left (-1 = no limit).</summary>
    public sealed class ExchangeGood
    {
        public string id, name, kind, itemId;
        public int price, weekly, left = -1, count;
        public readonly List<(string id, string name, string rarity, string skin)> choices = new List<(string, string, string, string)>();
    }

    /// <summary>[0056] One Star Shard good (server STAR_ITEM_GOODS): a bag item bought with Star Shards, no purchase limit.</summary>
    public sealed class StarItemGood
    {
        public string id, name, itemId;
        public int price, count;
    }

    /// <summary>Gacha gear in the bag that can be dismantled into Star Crystals (the gacha share of each base id).</summary>
    public sealed class DismantlableGear
    {
        public string itemKey, rarity;
        public int count, crystalsEach;
    }

    /// <summary>
    /// 별의 결정 and 별빛 교환소 (Docs/PLAN_CASH_ITEM_VALUE.md). Spare cosmetics and gacha gear are dismantled into Star
    /// Crystals, which buy chosen cosmetics, gauge fills and materials at the exchange. Crystals never turn into gold and cannot be
    /// traded (they become free Star Shards only through StarShopClient.Convert.cs); the gear codex (first Unique / Legendary of each kind) adds account max HP.
    /// The server decides everything; this keeps what it said.
    /// </summary>
    public static partial class StarShopClient
    {
        public static long Crystals { get; private set; }
        /// <summary>Crystals owed after a refund (the exchange is closed until it is repaid by later dismantling).</summary>
        public static long CrystalDebt { get; private set; }
        public static int CodexCount { get; private set; }
        /// <summary>Account max HP bonus of the gear codex (%), 0.5 per entry.</summary>
        public static float CodexHealth { get; private set; }
        public static readonly HashSet<string> ExchangeOnly = new HashSet<string>();
        public static readonly Dictionary<string, int> DismantleCrystals = new Dictionary<string, int>();

        public static readonly List<ExchangeGood> Goods = new List<ExchangeGood>();
        /// <summary>[0056] Star Shard goods (revive coin packs), listed with the exchange.</summary>
        public static readonly List<StarItemGood> StarGoods = new List<StarItemGood>();
        public static readonly List<DismantlableGear> GearSpares = new List<DismantlableGear>();
        public static readonly List<string> CodexEntries = new List<string>();
        public static float CodexHealthPer { get; private set; } = 0.5f;
        public static float CodexHealthMax { get; private set; } = 10f;
        public static string ExchangeResetAt { get; private set; } = "";
        public static bool ExchangeLoaded { get; private set; }

        public static string CrystalText(long n) => L.F("별의 결정 {0:N0}", n);

        static void ReadCrystal(Dictionary<string, object> d)
        {
            Crystals = (long)MiniJson.Num(d, "crystals");
            CrystalDebt = (long)MiniJson.Num(d, "crystal_debt");
            CodexCount = MiniJson.Int(d, "codex_count");
            CodexHealth = (float)MiniJson.Num(d, "codex_health");
            DismantleCrystals.Clear();
            var dis = MiniJson.Obj(d, "dismantle_crystals");
            if (dis != null) foreach (var kv in dis) DismantleCrystals[kv.Key] = MiniJson.Int(dis, kv.Key);
            ExchangeOnly.Clear();
            foreach (var o in MiniJson.Arr(d, "items") ?? new List<object>())
                if (Flag(o as Dictionary<string, object>, "exchange_only")) ExchangeOnly.Add(MiniJson.Str(o, "id", ""));
        }

        /// <summary>Loads the exchange: goods with this week's stock, the dismantlable gacha gear and the codex.</summary>
        public static void LoadExchange(Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            Api.Get(Base + "/exchange-shop", r =>
            {
                if (!r.ok || r.data == null) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? L.T("교환소를 불러오지 못했습니다.") : r.message); return; }
                var d = r.data;
                Crystals = (long)MiniJson.Num(d, "crystals", Crystals);
                CrystalDebt = (long)MiniJson.Num(d, "crystal_debt", CrystalDebt);
                ExchangeResetAt = MiniJson.Str(d, "next_reset_at", "");
                Goods.Clear();
                foreach (var o in MiniJson.Arr(d, "goods") ?? new List<object>())
                {
                    var g = new ExchangeGood
                    {
                        id = MiniJson.Str(o, "id", ""), name = MiniJson.Str(o, "name", ""), kind = MiniJson.Str(o, "kind", ""),
                        itemId = MiniJson.Str(o, "item_id", ""), price = MiniJson.Int(o, "price"), weekly = MiniJson.Int(o, "weekly"),
                        count = MiniJson.Int(o, "count"),
                    };
                    g.left = g.weekly > 0 ? MiniJson.Int(o, "left") : -1;
                    foreach (var c in MiniJson.Arr(o, "choices") ?? new List<object>())
                        g.choices.Add((MiniJson.Str(c, "id", ""), MiniJson.Str(c, "name", ""), MiniJson.Str(c, "rarity", ""), MiniJson.Str(c, "skin", "")));
                    Goods.Add(g);
                }
                StarGoods.Clear();
                foreach (var o in MiniJson.Arr(d, "star_goods") ?? new List<object>())
                    StarGoods.Add(new StarItemGood
                    {
                        id = MiniJson.Str(o, "id", ""), name = MiniJson.Str(o, "name", ""), itemId = MiniJson.Str(o, "item_id", ""),
                        price = MiniJson.Int(o, "price"), count = MiniJson.Int(o, "count"),
                    });
                if (MiniJson.Has(d, "star_balance")) Balance = (long)MiniJson.Num(d, "star_balance", Balance);
                GearSpares.Clear();
                foreach (var o in MiniJson.Arr(d, "dismantlable") ?? new List<object>())
                    GearSpares.Add(new DismantlableGear
                    {
                        itemKey = MiniJson.Str(o, "item_key", ""), rarity = MiniJson.Str(o, "rarity", ""),
                        count = MiniJson.Int(o, "count"), crystalsEach = MiniJson.Int(o, "crystals_each"),
                    });
                CodexEntries.Clear();
                var codex = MiniJson.Obj(d, "codex");
                if (codex != null)
                {
                    foreach (var e in MiniJson.Arr(codex, "entries") ?? new List<object>()) CodexEntries.Add(e as string ?? "");
                    CodexCount = CodexEntries.Count;
                    CodexHealth = (float)MiniJson.Num(codex, "health", CodexHealth);
                    CodexHealthPer = (float)MiniJson.Num(codex, "per", CodexHealthPer);
                    CodexHealthMax = (float)MiniJson.Num(codex, "max", CodexHealthMax);
                }
                ReadConvert(d); // StarShopClient.Convert.cs
                ExchangeLoaded = true;
                Changed?.Invoke();
                done?.Invoke(true, "");
            });
        }

        /// <summary>Buys one exchange good with Star Crystals (itemId = the chosen cosmetic for a pick good).</summary>
        public static void BuyGood(string goodId, string itemId, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["good_id"] = goodId };
            if (!string.IsNullOrEmpty(itemId)) body["item_id"] = itemId;
            Api.PostIdempotent(Base + "/exchange-shop/buy", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, ExchangeError(r.code, r.message, L.T("교환하지 못했습니다."))); return; }
                Crystals = (long)MiniJson.Num(r.data, "crystal_balance", Crystals);
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                _ = RefreshAfter(() => LoadExchange((_, __) => done?.Invoke(true, "")));
            });
        }

        /// <summary>[0056] Buys one Star Shard good (revive coin pack): the items arrive in the bag (account-bound).</summary>
        public static void BuyStarItem(string goodId, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["good_id"] = goodId };
            Api.PostIdempotent(Base + "/item-buy", body, r =>
            {
                if (!r.ok)
                {
                    done?.Invoke(false, r.code == "NOT_ENOUGH_STARS" ? L.T("별조각이 모자랍니다.") : string.IsNullOrEmpty(r.message) ? L.T("구매하지 못했습니다.") : r.message);
                    return;
                }
                Balance = (long)MiniJson.Num(r.data, "balance", Balance);
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                Changed?.Invoke();
                done?.Invoke(true, "");
            });
        }

        /// <summary>Dismantles gacha gear from the bag into Star Crystals (and some enhancement materials).</summary>
        public static void DismantleGear(List<(string itemKey, int count)> items, Action<bool, string, int> done)
        {
            if (!Available) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다."), 0); return; }
            var list = new List<object>();
            foreach (var (key, n) in items) list.Add(new Dictionary<string, object> { ["item_key"] = key, ["count"] = n });
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["items"] = list };
            Api.PostIdempotent(Base + "/gear-dismantle", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, ExchangeError(r.code, r.message, L.T("분해하지 못했습니다.")), 0); return; }
                int got = MiniJson.Int(r.data, "crystals");
                Crystals = (long)MiniJson.Num(r.data, "crystal_balance", Crystals);
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                LoadExchange((_, __) => done?.Invoke(true, "", got));
            });
        }

        /// <summary>Records every Unique / Legendary gear this character holds in the account codex.</summary>
        public static void RegisterCodex(Action<bool, string, int> done)
        {
            if (!Available) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다."), 0); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() };
            Api.PostIdempotent(Base + "/codex", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? L.T("도감에 기록하지 못했습니다.") : r.message, 0); return; }
                int added = MiniJson.Int(r.data, "added");
                CodexHealth = (float)MiniJson.Num(r.data, "health", CodexHealth);
                LoadExchange((_, __) => done?.Invoke(true, "", added));
            });
        }

        static string ExchangeError(string code, string message, string fallback) =>
            code == "CRYSTAL_DEBT" ? L.T("환불된 결제분에서 나온 별의 결정을 먼저 갚아야 교환소를 쓸 수 있습니다. 분해하면 먼저 갚습니다.")
            : string.IsNullOrEmpty(message) ? fallback : message;
    }
}
