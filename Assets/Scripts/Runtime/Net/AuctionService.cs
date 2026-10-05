using System;
using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    public enum AuctionCategory { All, Weapon, Armor, Accessory, Material, Consumable }
    public enum AuctionSort { PriceAsc, PriceDesc, TimeLeft, EnhanceDesc, Newest }
    /// <summary>[ONLINE] Trade flags (PLAN_AUCTION §6).</summary>
    public enum ItemBind { Tradable, AccountBound, CharacterBound }

    /// <summary>[ONLINE] 경매장 search filter.</summary>
    public sealed class AuctionQuery
    {
        public string text = "";
        public AuctionCategory category = AuctionCategory.All;
        /// <summary>-1 = every grade, else exactly this grade.</summary>
        public int rarity = -1;
        public int enhMin = 0, enhMax = EquipmentDatabase.MaxEnhance;
        public long priceMin = 0, priceMax = long.MaxValue;
        public AuctionSort sort = AuctionSort.PriceAsc;
    }

    public sealed class AuctionListing
    {
        public string id;
        public string itemKey;
        public int count = 1;
        public string seller;
        public long buyout;
        public long startBid;
        public long currentBid;
        public bool hasBid;
        public long deposit;
        public double hoursLeft;
        public int order;
        public bool mine;
        public bool myBid;
        public AuctionCategory category;
        public ItemRarity rarity;
        public int enhance;
        /// <summary>[SERVER 6] Next minimum bid as the server computed it (0 = compute locally).</summary>
        public long serverMinBid;
        /// <summary>[SERVER 6] Buying it binds the item to the account.</summary>
        public bool willBind;
        public string Name => DungeonDatabase.ItemName(itemKey);
        public bool Biddable => startBid > 0;
    }

    public sealed class AuctionMail
    {
        public string id;
        public string text;
        public string itemKey;
        public int count;
        public long gold;
        public bool claimed;
    }

    public sealed class AuctionPrice
    {
        public long avg7d, min, max;
        public int volume;
        /// <summary>[SERVER 6] Allowed buyout range for one item (0 = unknown / offline).</summary>
        public long limitMin, limitMax;
    }

    public struct AuctionResult
    {
        public bool ok;
        public string message;
        public static AuctionResult Ok(string m) => new AuctionResult { ok = true, message = m };
        public static AuctionResult Fail(string m) => new AuctionResult { ok = false, message = m };
    }

    /// <summary>[ONLINE] Numbers of PLAN_AUCTION §2, §3, §6, §8 (the server re-checks all of them).</summary>
    public static class AuctionRules
    {
        public static readonly int[] Durations = { 12, 24, 48 };
        public const int FeePctGear = 5, FeePctStack = 3;
        // [SERVER 6] Rates in basis points (1/10000) so the client and the server compute the same integers.
        public const int DepositBps = 100;
        public const long DepositMin = 10, DepositMax = 10000;
        public const int MaxListings = 20;
        public const int MinBidStepBps = 10500;
        /// <summary>Price limits against the reference price (bps: 2000 = 20%, 50000 = 5x).</summary>
        public const int PriceFloorBps = 2000, PriceCeilBps = 50000;
        public const float PriceFloor = PriceFloorBps / 10000f, PriceCeil = PriceCeilBps / 10000f;
        /// <summary>Largest stack in one listing (PLAN_AUCTION §2: materials 999).</summary>
        public const int MaxStack = 999;
        /// <summary>A bid in the last few minutes pushes the end back (at most a few times).</summary>
        public const int ExtendWindowMinutes = 5, ExtendMinutes = 5, ExtendMax = 6;
        /// <summary>Days a mail is kept before it is thrown away.</summary>
        public const int MailDays = 30;
        /// <summary>Upper hours of each remaining-time band.</summary>
        public static readonly int[] TimeBands = { 1, 6, 12, 24, 48 };

        /// <summary>Integer, half-up: clamp(floor((buyout x bps + 5000) / 10000), min, max).</summary>
        public static long Deposit(long buyout) => Math.Min(DepositMax, Math.Max(DepositMin, (buyout * DepositBps + 5000) / 10000));
        public static int FeePct(string key) => EquipmentDatabase.IsEquipment(key) ? FeePctGear : FeePctStack;
        /// <summary>Integer ceiling: (price x pct + 99) / 100.</summary>
        public static long Fee(string key, long price) => (price * FeePct(key) + 99) / 100;
        public static long Payout(string key, long price) => price - Fee(key, price);
        public static long MinBid(AuctionListing l) => l.serverMinBid > 0 ? l.serverMinBid : !l.hasBid ? l.startBid : Math.Max((l.currentBid * MinBidStepBps + 9999) / 10000, l.currentBid + 1);

        /// <summary>
        /// [SERVER 6] The least binding an item kind always has (online the row's own binding, set by how it
        /// was obtained, can only be stronger): gold, starter gear, protection tickets and raid keys.
        /// </summary>
        public static ItemBind BindFloor(string key)
        {
            if (string.IsNullOrEmpty(key) || key == ConsumableDatabase.Gold || key == ConsumableDatabase.ProtectTicket || key == DungeonDatabase.SealKey || key == DungeonDatabase.RaidCore)
                return ItemBind.CharacterBound;
            var gear = EquipmentDatabase.Get(key);
            return gear != null && gear.starter ? ItemBind.CharacterBound : ItemBind.Tradable;
        }

        public static ItemBind BindOf(string key)
        {
            if (string.IsNullOrEmpty(key) || key == ConsumableDatabase.Gold) return ItemBind.CharacterBound;
            if (key == ConsumableDatabase.ProtectTicket) return ItemBind.CharacterBound;
            var gear = EquipmentDatabase.Get(key);
            if (gear != null)
            {
                if (gear.starter) return ItemBind.CharacterBound;
                return gear.dropWeight > 0 ? ItemBind.Tradable : ItemBind.AccountBound; // drops trade; quest / shop gear is bound
            }
            return ItemBind.Tradable; // materials and potions
        }

        public static string BindLabel(ItemBind b) =>
            b == ItemBind.Tradable ? "<color=#8fe28f>거래 가능</color>" : b == ItemBind.AccountBound ? "<color=#78dcff>계정 귀속</color>" : "<color=#ff9f43>캐릭터 귀속</color>";

        public static AuctionCategory CategoryOf(string key)
        {
            var gear = EquipmentDatabase.Get(key);
            if (gear != null)
            {
                switch (gear.category)
                {
                    case EquipCategory.Weapon: return AuctionCategory.Weapon;
                    case EquipCategory.Ring:
                    case EquipCategory.Necklace: return AuctionCategory.Accessory;
                    default: return AuctionCategory.Armor;
                }
            }
            return EquipmentDatabase.GetMaterial(key) != null ? AuctionCategory.Material : AuctionCategory.Consumable;
        }

        public static string CategoryName(AuctionCategory c) =>
            c == AuctionCategory.All ? "전체" : c == AuctionCategory.Weapon ? "무기" : c == AuctionCategory.Armor ? "방어구"
            : c == AuctionCategory.Accessory ? "장신구" : c == AuctionCategory.Material ? "강화 재료" : "소모품";

        public static string SortName(AuctionSort s) =>
            s == AuctionSort.PriceAsc ? "가격 낮은 순" : s == AuctionSort.PriceDesc ? "가격 높은 순" : s == AuctionSort.TimeLeft ? "남은 시간 순"
            : s == AuctionSort.EnhanceDesc ? "강화 높은 순" : "최근 등록 순";

        /// <summary>Remaining time as a band (exact seconds are hidden, §4).</summary>
        public static string TimeBand(double hours) =>
            hours < 1 ? "1시간 미만" : hours < 6 ? "6시간 미만" : hours < 12 ? "12시간 미만" : hours < 24 ? "24시간 미만" : "48시간 미만";

        /// <summary>Reference price of an item key (mock market value; the server uses real history).</summary>
        public static long BasePrice(string key)
        {
            var gear = EquipmentDatabase.Get(key);
            if (gear != null)
            {
                long p = 200 + (long)EquipmentDatabase.StatsOfKey(key).Score * 3 + (long)gear.rarity * 600;
                int lv = EquipmentDatabase.LevelOfKey(key);
                if (lv >= 10) p *= 1 + (lv - 9) * 2;
                return p;
            }
            if (EquipmentDatabase.GetMaterial(key) != null) return 40;
            return 25;
        }
    }

    /// <summary>[ONLINE] 경매장 + 우편함 (PLAN_AUCTION). Server-backed later; <see cref="MockAuctionService"/> offline.</summary>
    public interface IAuctionService
    {
        bool IsOnline { get; }
        IReadOnlyList<AuctionListing> Search(AuctionQuery query);
        IReadOnlyList<AuctionListing> MyListings();
        IReadOnlyList<AuctionMail> Mailbox();
        int UnclaimedMail { get; }
        AuctionPrice Price(string itemKey);
        AuctionResult Buyout(string listingId);
        AuctionResult Bid(string listingId, long amount);
        AuctionResult Register(string itemKey, int count, long buyout, long startBid, int hours);
        AuctionResult Cancel(string listingId);
        AuctionResult Claim(string mailId);
        AuctionResult ClaimAll();
        /// <summary>[SERVER 6] Can this bag item be listed (online: the server's per-row binding).</summary>
        ItemBind BindOf(string key);
        /// <summary>[SERVER 6] Most of this bag item one listing can hold.</summary>
        int MaxCount(string key);
        event Action Changed;
    }

    /// <summary>
    /// [ONLINE] In-memory auction house. Purchases really move gold out of the offline bag and deliver the
    /// item through the mailbox, so the UI flow can be tried end to end without a server.
    /// </summary>
    public sealed class MockAuctionService : IAuctionService
    {
        static readonly string[] Sellers = { "검은뿔", "달빛마녀", "광산왕", "눈꽃여우", "카타나장인", "해골사냥꾼", "모험가A", "은빛기사" };

        readonly List<AuctionListing> listings = new List<AuctionListing>();
        readonly List<AuctionMail> mails = new List<AuctionMail>();
        int nextId = 1, nextMail = 1, order;

        public bool IsOnline => false;
        public event Action Changed;
        public ItemBind BindOf(string key) => AuctionRules.BindOf(key);
        public int MaxCount(string key) => EquipmentDatabase.IsEquipment(key) ? 1 : Math.Min(AuctionRules.MaxStack, Bag?.Count(key) ?? 0);

        Inventory Bag => Game.Session?.Inventory;

        public MockAuctionService(int seed = 7)
        {
            var rng = new Random(seed);
            var gear = EquipmentDatabase.All.Where(g => g.dropWeight > 0 && !g.starter).ToList();
            for (int i = 0; i < 26 && gear.Count > 0; i++)
            {
                var g = gear[rng.Next(gear.Count)];
                int lv = rng.Next(0, 100) < 55 ? 0 : rng.Next(1, 13);
                Add(EquipmentDatabase.KeyFor(g.id, lv), 1, Sellers[rng.Next(Sellers.Length)], rng, false);
            }
            foreach (var m in EquipmentDatabase.AllMaterials)
                for (int k = 0; k < 2; k++) Add(m.id, rng.Next(5, 41), Sellers[rng.Next(Sellers.Length)], rng, false);
        }

        AuctionListing Add(string key, int count, string seller, Random rng, bool mine)
        {
            long unit = AuctionRules.BasePrice(key);
            long price = (long)(unit * count * (0.8 + rng.NextDouble() * 0.6));
            price = Math.Max(10, price / 10 * 10);
            var l = new AuctionListing
            {
                id = "A" + nextId++,
                itemKey = key,
                count = count,
                seller = seller,
                buyout = price,
                startBid = rng.Next(0, 3) == 0 ? price * 6 / 10 : 0,
                hoursLeft = 0.5 + rng.NextDouble() * 47,
                order = order++,
                mine = mine,
                category = AuctionRules.CategoryOf(key),
                rarity = EquipmentDatabase.Get(key)?.rarity ?? ItemRarity.Common,
                enhance = EquipmentDatabase.LevelOfKey(key),
            };
            listings.Add(l);
            return l;
        }

        public IReadOnlyList<AuctionListing> Search(AuctionQuery q)
        {
            q = q ?? new AuctionQuery();
            IEnumerable<AuctionListing> r = listings.Where(l => !l.mine);
            if (!string.IsNullOrWhiteSpace(q.text)) r = r.Where(l => l.Name.IndexOf(q.text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            if (q.category != AuctionCategory.All) r = r.Where(l => l.category == q.category);
            if (q.rarity >= 0) r = r.Where(l => EquipmentDatabase.IsEquipment(l.itemKey) && (int)l.rarity == q.rarity);
            r = r.Where(l => l.enhance >= q.enhMin && l.enhance <= q.enhMax && l.buyout >= q.priceMin && l.buyout <= q.priceMax);
            switch (q.sort)
            {
                case AuctionSort.PriceDesc: r = r.OrderByDescending(l => l.buyout); break;
                case AuctionSort.TimeLeft: r = r.OrderBy(l => l.hoursLeft); break;
                case AuctionSort.EnhanceDesc: r = r.OrderByDescending(l => l.enhance).ThenBy(l => l.buyout); break;
                case AuctionSort.Newest: r = r.OrderByDescending(l => l.order); break;
                default: r = r.OrderBy(l => l.buyout); break;
            }
            return r.ToList();
        }

        public IReadOnlyList<AuctionListing> MyListings() => listings.Where(l => l.mine).OrderByDescending(l => l.order).ToList();
        public IReadOnlyList<AuctionMail> Mailbox() => mails.Where(m => !m.claimed).ToList();
        public int UnclaimedMail => mails.Count(m => !m.claimed);

        public AuctionPrice Price(string itemKey)
        {
            long b = AuctionRules.BasePrice(itemKey);
            return new AuctionPrice { avg7d = b, min = b * 8 / 10, max = b * 13 / 10, volume = 3 + (int)(b % 17) };
        }

        AuctionListing Find(string id) => listings.FirstOrDefault(l => l.id == id);

        void Mail(string text, string key, int count, long gold) =>
            mails.Add(new AuctionMail { id = "M" + nextMail++, text = text, itemKey = key, count = count, gold = gold });

        public AuctionResult Buyout(string listingId)
        {
            var l = Find(listingId);
            if (l == null) return AuctionResult.Fail("이미 팔렸거나 기간이 끝난 물건입니다.");
            if (l.mine) return AuctionResult.Fail("내가 등록한 물건은 살 수 없습니다.");
            var bag = Bag;
            if (bag == null || bag.Count(ConsumableDatabase.Gold) < l.buyout) return AuctionResult.Fail("골드가 부족합니다.");
            bag.Remove(ConsumableDatabase.Gold, (int)Authority.Current.ApplyGold("auction_buy", (int)l.buyout));
            listings.Remove(l);
            Mail($"[구매] {l.Name} x{l.count}", l.itemKey, l.count, 0);
            Changed?.Invoke();
            return AuctionResult.Ok($"{l.Name}을(를) {l.buyout:N0}G에 샀습니다. 우편함에서 받으세요.");
        }

        public AuctionResult Bid(string listingId, long amount)
        {
            var l = Find(listingId);
            if (l == null) return AuctionResult.Fail("이미 팔렸거나 기간이 끝난 물건입니다.");
            if (l.mine) return AuctionResult.Fail("내 물건에는 입찰할 수 없습니다.");
            if (!l.Biddable) return AuctionResult.Fail("즉시 구매만 가능한 물건입니다.");
            if (amount >= l.buyout) return Buyout(listingId);
            if (amount < AuctionRules.MinBid(l)) return AuctionResult.Fail($"최소 입찰가는 {AuctionRules.MinBid(l):N0}G입니다.");
            var bag = Bag;
            if (bag == null || bag.Count(ConsumableDatabase.Gold) < amount) return AuctionResult.Fail("골드가 부족합니다.");
            bag.Remove(ConsumableDatabase.Gold, (int)amount); // escrow
            l.currentBid = amount;
            l.hasBid = true;
            l.myBid = true;
            if (l.hoursLeft < 5.0 / 60.0) l.hoursLeft += 5.0 / 60.0;
            Changed?.Invoke();
            return AuctionResult.Ok($"{amount:N0}G로 입찰했습니다. (골드 예치)");
        }

        public AuctionResult Register(string itemKey, int count, long buyout, long startBid, int hours)
        {
            var bag = Bag;
            if (bag == null) return AuctionResult.Fail("가방이 없습니다.");
            if (AuctionRules.BindOf(itemKey) != ItemBind.Tradable) return AuctionResult.Fail("귀속 아이템은 등록할 수 없습니다.");
            if (count < 1 || bag.Count(itemKey) < count) return AuctionResult.Fail("가방에 아이템이 부족합니다.");
            if (Array.IndexOf(AuctionRules.Durations, hours) < 0) return AuctionResult.Fail("기간은 12/24/48시간입니다.");
            if (listings.Count(l => l.mine) >= AuctionRules.MaxListings) return AuctionResult.Fail("동시 등록은 20건까지입니다.");
            long avg = AuctionRules.BasePrice(itemKey) * count;
            if (buyout < avg * AuctionRules.PriceFloor || buyout > avg * AuctionRules.PriceCeil)
                return AuctionResult.Fail($"가격은 {(long)(avg * AuctionRules.PriceFloor):N0}~{(long)(avg * AuctionRules.PriceCeil):N0}G 사이여야 합니다.");
            if (startBid > 0 && startBid >= buyout) return AuctionResult.Fail("입찰 시작가는 즉시 구매가보다 낮아야 합니다.");
            long deposit = AuctionRules.Deposit(buyout);
            if (bag.Count(ConsumableDatabase.Gold) < deposit) return AuctionResult.Fail($"보증금 {deposit:N0}G가 부족합니다.");
            bag.Remove(ConsumableDatabase.Gold, (int)deposit);
            bag.Remove(itemKey, count);
            var l = new AuctionListing
            {
                id = "A" + nextId++, itemKey = itemKey, count = count, seller = "나", buyout = buyout, startBid = startBid,
                deposit = deposit, hoursLeft = hours, order = order++, mine = true,
                category = AuctionRules.CategoryOf(itemKey), rarity = EquipmentDatabase.Get(itemKey)?.rarity ?? ItemRarity.Common,
                enhance = EquipmentDatabase.LevelOfKey(itemKey),
            };
            listings.Add(l);
            Changed?.Invoke();
            return AuctionResult.Ok($"{l.Name} 등록 완료 (보증금 {deposit:N0}G)");
        }

        public AuctionResult Cancel(string listingId)
        {
            var l = Find(listingId);
            if (l == null || !l.mine) return AuctionResult.Fail("취소할 수 없습니다.");
            if (l.hasBid) return AuctionResult.Fail("입찰자가 있어 취소할 수 없습니다.");
            listings.Remove(l);
            Mail($"[등록 취소] {l.Name} x{l.count} (보증금 미반환)", l.itemKey, l.count, 0);
            Changed?.Invoke();
            return AuctionResult.Ok("등록을 취소했습니다. 아이템은 우편함으로 돌아갑니다.");
        }

        public AuctionResult Claim(string mailId)
        {
            var m = mails.FirstOrDefault(x => x.id == mailId && !x.claimed);
            var bag = Bag;
            if (m == null || bag == null) return AuctionResult.Fail("받을 우편이 없습니다.");
            if (!string.IsNullOrEmpty(m.itemKey) && m.count > 0) bag.Add(m.itemKey, m.count);
            if (m.gold > 0) bag.Add(ConsumableDatabase.Gold, (int)m.gold);
            m.claimed = true;
            Changed?.Invoke();
            return AuctionResult.Ok("우편을 받았습니다.");
        }

        public AuctionResult ClaimAll()
        {
            int n = 0;
            foreach (var m in mails.Where(x => !x.claimed).ToList()) if (Claim(m.id).ok) n++;
            return n > 0 ? AuctionResult.Ok($"우편 {n}통을 받았습니다.") : AuctionResult.Fail("받을 우편이 없습니다.");
        }

        /// <summary>Dev: someone buys my listing (seller side settlement: price − fee + deposit by mail).</summary>
        public bool DevSellMine(string listingId)
        {
            var l = Find(listingId);
            if (l == null || !l.mine) return false;
            listings.Remove(l);
            long payout = AuctionRules.Payout(l.itemKey, l.buyout) + l.deposit;
            Mail($"[판매 대금] {l.Name} x{l.count}", null, 0, payout);
            Changed?.Invoke();
            return true;
        }
    }
}
