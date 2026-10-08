using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DotRPG
{
    /// <summary>One disclosed rate line: a rarity and each aura in it (percent).</summary>
    public sealed class StarRate
    {
        public string rarity, name;
        public double rate;
        public readonly List<(string id, string name, double rate, bool gear)> items = new List<(string, string, double, bool)>();
    }

    public sealed class StarPullResult
    {
        public string itemId, rarity;
        public bool byPity, duplicate, gear;
        public int refund;
        /// <summary>[CASH] A sealed-box reward (a bag item with a count; boosted = the 2x booster open).</summary>
        public bool cash, boosted;
        public int count = 1;
    }

    /// <summary>
    /// 캐시샵(별조각). The server keeps the wallet, the pity count, the owned auras and rolls every pull; this only
    /// shows what it says. 별조각 are account-wide. Rates and prices always come from the server's current table.
    /// </summary>
    public static partial class StarShopClient
    {
        public static long Balance { get; private set; }
        public static int Pity { get; private set; }
        /// <summary>The skin banner's own pity count.</summary>
        public static int SkinPity { get; private set; }
        /// <summary>Skin banner rate table (its 유니크 tier = my class's skins).</summary>
        public static readonly List<StarRate> SkinRates = new List<StarRate>();
        public static int PityMax { get; private set; } = 50;
        /// <summary>Selection gauge sizes: fill it with draws, then choose (aura: a 유니크 aura, skin: a skin).</summary>
        public static int AuraGaugeMax { get; private set; } = 50;
        public static int SkinGaugeMax { get; private set; } = 100;
        public static int PriceOne { get; private set; } = 100;
        public static int PriceTen { get; private set; } = 1000;
        public static int TenCount { get; private set; } = 11;
        public static string RatesVersion { get; private set; } = "";
        public static bool Loaded { get; private set; }
        public static readonly List<StarRate> Rates = new List<StarRate>();
        /// <summary>Equipment banners (weapon / armor / accessory): id, name and their rate tables for my class.</summary>
        public static readonly List<(string id, string name, List<StarRate> rates)> Banners = new List<(string, string, List<StarRate>)>();
        /// <summary>Gear banners by level tier: banner id -> (tier index, tier level, rate table), unlocked tiers only.</summary>
        public static readonly Dictionary<string, List<(int tier, int level, List<StarRate> rates)>> GearTiers = new Dictionary<string, List<(int, int, List<StarRate>)>>();
        /// <summary>The tier of my level (the gear banners' default).</summary>
        public static int MyTier { get; private set; }
        public static readonly Dictionary<string, int> Refund = new Dictionary<string, int>();
        public static readonly HashSet<string> Owned = new HashSet<string>();
        public static readonly Dictionary<string, int> ExchangePrice = new Dictionary<string, int>();
        public static event Action Changed;

        static ApiClient Api => ApiClient.Instance;
        static string Base => "/characters/" + OnlineSession.Current.ActiveCharacter + "/starshop";
        public static bool Available => OnlineSession.Playing && Api != null;

        public static string Stars(long n) => $"별조각 {n:N0}";

        public static Task<bool> RefreshAsync()
        {
            var done = new TaskCompletionSource<bool>();
            if (!Available) { Clear(); done.SetResult(false); return done.Task; }
            Api.Get(Base, r =>
            {
                if (r.ok && r.data != null) Read(r.data);
                done.TrySetResult(r.ok);
            });
            return done.Task;
        }

        /// <summary>1회(count 1) or the 10+1 bundle (count 10). The server takes the 별조각 and rolls.</summary>
        public static void Pull(string banner, int count, Action<bool, string, List<StarPullResult>> done, int tier = -1)
        {
            if (!Available) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다.", null); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["count"] = count, ["banner"] = banner };
            if (tier >= 0 && banner != "aura" && banner != "skin") body["tier"] = tier; // gear: the chosen level tier
            // [PAY 11] The rates the player was shown: the server refuses the pull if they changed meanwhile.
            if (!string.IsNullOrEmpty(RatesVersion)) body["rates_version"] = RatesVersion;
            Api.PostIdempotent(Base + "/pull", body, r =>
            {
                if (!r.ok && r.code == "RATES_CHANGED")
                {
                    // New rates: reload them so the window shows the current table, then ask again.
                    _ = RefreshAsync();
                    done?.Invoke(false, "확률표가 바뀌었습니다. 새 확률을 확인한 뒤 다시 뽑아 주세요.", null);
                    return;
                }
                if (!r.ok && (r.code == "STAR_DEBT" || r.code == "STAR_SPEND_CAP"))
                {
                    done?.Invoke(false, r.code == "STAR_DEBT" ? "환불된 결제로 갚아야 할 별조각이 있어 지금은 뽑을 수 없습니다." : "오늘 쓸 수 있는 별조각 한도를 넘었습니다. (06:00 초기화)", null);
                    return;
                }
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? "뽑기에 실패했습니다." : r.message, null); return; }
                var results = new List<StarPullResult>();
                foreach (var o in MiniJson.Arr(r.data, "results") ?? new List<object>())
                    results.Add(ReadPull(o as Dictionary<string, object>));
                Balance = (long)MiniJson.Num(r.data, "balance", Balance);
                if (banner == "skin") SkinPity = MiniJson.Int(r.data, "pity", SkinPity);
                else if (banner == "aura") Pity = MiniJson.Int(r.data, "pity", Pity);
                foreach (var p in results) if (!p.gear) Owned.Add(p.itemId);
                // Gear goes straight into the bag (same delta as every other server grant).
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                Changed?.Invoke();
                _ = Game.Cosmetics?.RefreshAsync();
                done?.Invoke(true, "", results);
            });
        }

        /// <summary>The gauge is full: take the chosen top item of that banner.</summary>
        public static void Claim(string banner, string itemId, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다."); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["banner"] = banner, ["item_id"] = itemId };
            Api.PostIdempotent(Base + "/claim", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? "선택하지 못했습니다." : r.message); return; }
                if (banner == "skin") SkinPity = MiniJson.Int(r.data, "pity", SkinPity); else Pity = MiniJson.Int(r.data, "pity", Pity);
                Owned.Add(itemId);
                Changed?.Invoke();
                _ = Game.Cosmetics?.RefreshAsync();
                done?.Invoke(true, "");
            });
        }

        /// <summary>Direct exchange of one aura (used by the wardrobe's purchase button).</summary>
        public static Task ExchangeAsync(string itemId)
        {
            var done = new TaskCompletionSource<bool>();
            if (!Available) { done.SetException(new InvalidOperationException("offline")); return done.Task; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["item_id"] = itemId };
            Api.PostIdempotent(Base + "/exchange", body, r =>
            {
                if (!r.ok) { done.TrySetException(new InvalidOperationException(r.message ?? "exchange failed")); return; }
                Balance = (long)MiniJson.Num(r.data, "balance", Balance);
                Owned.Add(itemId);
                Changed?.Invoke();
                done.TrySetResult(true);
            });
            return done.Task;
        }

        static void Clear()
        {
            Loaded = false;
            Balance = 0;
            Pity = 0;
            Owned.Clear();
            Copies.Clear();
            Collections.Clear();
            CollectionAttack = CollectionHealth = 0;
            Changed?.Invoke();
        }

        static StarPullResult ReadPull(Dictionary<string, object> o) => new StarPullResult
        {
            itemId = MiniJson.Str(o, "item_id", ""),
            rarity = MiniJson.Str(o, "rarity", "common"),
            byPity = Flag(o, "by_pity"),
            duplicate = Flag(o, "duplicate"),
            gear = Flag(o, "gear"),
            refund = MiniJson.Int(o, "refund"),
        };

        static List<StarRate> ReadRates(List<object> list, bool gear)
        {
            var rates = new List<StarRate>();
            foreach (var o in list ?? new List<object>())
            {
                var r = new StarRate { rarity = MiniJson.Str(o, "rarity", ""), name = MiniJson.Str(o, "name", ""), rate = MiniJson.Num(o, "rate") };
                foreach (var i in MiniJson.Arr(o, "items") ?? new List<object>())
                    r.items.Add((MiniJson.Str(i, "id", ""), MiniJson.Str(i, "name", ""), MiniJson.Num(i, "rate"), gear));
                rates.Add(r);
            }
            return rates;
        }

        static bool Flag(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) && v is bool b && b;

        static void Read(Dictionary<string, object> d)
        {
            Balance = (long)MiniJson.Num(d, "balance");
            Pity = MiniJson.Int(d, "pity");
            SkinPity = MiniJson.Int(d, "skin_pity");
            PityMax = MiniJson.Int(d, "pity_max", 50);
            AuraGaugeMax = MiniJson.Int(d, "aura_gauge_max", 50);
            SkinGaugeMax = MiniJson.Int(d, "skin_gauge_max", 100);
            PriceOne = MiniJson.Int(d, "price_one", 100);
            PriceTen = MiniJson.Int(d, "price_ten", 1000);
            TenCount = MiniJson.Int(d, "ten_count", 11);
            RatesVersion = MiniJson.Str(d, "rates_version", "");
            Rates.Clear();
            Rates.AddRange(ReadRates(MiniJson.Arr(d, "rates"), false));
            SkinRates.Clear();
            SkinRates.AddRange(ReadRates(MiniJson.Arr(d, "skin_rates"), false));
            Banners.Clear();
            GearTiers.Clear();
            MyTier = MiniJson.Int(d, "my_tier");
            foreach (var b in MiniJson.Arr(d, "banners") ?? new List<object>())
            {
                string bid = MiniJson.Str(b, "id", "");
                Banners.Add((bid, MiniJson.Str(b, "name", ""), ReadRates(MiniJson.Arr(b, "rates"), true)));
                var tiers = new List<(int, int, List<StarRate>)>();
                foreach (var t in MiniJson.Arr(b, "tiers") ?? new List<object>())
                    tiers.Add((MiniJson.Int(t, "tier"), MiniJson.Int(t, "level"), ReadRates(MiniJson.Arr(t, "rates"), true)));
                GearTiers[bid] = tiers;
            }
            Refund.Clear();
            var refund = MiniJson.Obj(d, "refund");
            if (refund != null) foreach (var kv in refund) Refund[kv.Key] = MiniJson.Int(refund, kv.Key);
            Owned.Clear();
            ExchangePrice.Clear();
            foreach (var o in MiniJson.Arr(d, "items") ?? new List<object>())
            {
                string id = MiniJson.Str(o, "id", "");
                if (Flag(o as Dictionary<string, object>, "owned")) Owned.Add(id);
                ExchangePrice[id] = MiniJson.Int(o, "exchange_price");
            }
            ReadSynth(d);
            Loaded = true;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The wardrobe's commerce provider: ownership and exchange offers come from the 별조각 cash shop on the server.
    /// The offer price is in 별조각 and the exchange is verified and granted by the server before ownership shows.
    /// </summary>
    public sealed class StarShopProvider : ICommerceProvider
    {
        public bool IsAvailable => StarShopClient.Available;

        public async Task<CommerceSnapshot> FetchAsync()
        {
            if (!await StarShopClient.RefreshAsync()) throw new InvalidOperationException("starshop fetch failed");
            var owned = new List<string>(StarShopClient.Owned);
            var offers = new List<CosmeticOffer>();
            foreach (var kv in StarShopClient.ExchangePrice)
                if (!StarShopClient.Owned.Contains(kv.Key) && kv.Value > 0)
                    offers.Add(new CosmeticOffer(kv.Key, StarShopClient.Stars(kv.Value), kv.Key + ":" + kv.Value));
            return new CommerceSnapshot { OwnedProductIds = owned.ToArray(), Offers = offers.ToArray() };
        }

        public Task<CommerceSnapshot> RestoreAsync() => FetchAsync();

        public Task PurchaseAsync(CosmeticOffer offer) => StarShopClient.ExchangeAsync(offer.ProductId);
    }
}
