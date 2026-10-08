using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [SERVER 6] 경매장 + 우편함 through the server (Docs/server/phase6_api.md). The window reads
    /// synchronously, so every list is a cache refreshed in the background (search when the query changes
    /// or goes stale, my listings, mail, prices, sellable rows) and each action answers "sent" at once
    /// while the server's verdict arrives as a toast plus <see cref="Changed"/>. Gold and items move only
    /// through the answers' delta.
    /// </summary>
    public sealed class ServerAuctionService : IAuctionService
    {
        const float StaleSeconds = 10f, SummaryOpenSeconds = 15f, SummaryClosedSeconds = 60f;

        List<AuctionListing> results = new List<AuctionListing>();
        List<AuctionListing> mine = new List<AuctionListing>();
        List<AuctionMail> mail = new List<AuctionMail>();
        readonly Dictionary<string, AuctionPrice> prices = new Dictionary<string, AuctionPrice>();
        readonly HashSet<string> priceLoading = new HashSet<string>();
        readonly Dictionary<string, (ItemBind bind, int max)> sellable = new Dictionary<string, (ItemBind, int)>();
        string lastQuery;
        float searchAt = -99f, mineAt = -99f, mailAt = -99f, sellAt = -99f, summaryAt = -99f;
        bool searching, loadingMine, loadingMail, loadingSell, busy;
        int unclaimed;

        public bool IsOnline => true;
        public int UnclaimedMail => unclaimed;
        public event Action Changed;

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;
        static float Now => Time.realtimeSinceStartup;

        public ServerAuctionService()
        {
            Summary();
        }

        // ---------------- reading ----------------

        public IReadOnlyList<AuctionListing> Search(AuctionQuery q)
        {
            q = q ?? new AuctionQuery();
            string qs = QueryString(q);
            if (qs != lastQuery || Now - searchAt > StaleSeconds)
            {
                lastQuery = qs;
                if (!searching)
                {
                    searching = true;
                    searchAt = Now;
                    Api.Get(Char + "/auction/search" + qs, r =>
                    {
                        searching = false;
                        if (r.ok) { results = ReadListings(MiniJson.Arr(r.data, "listings"), false); Changed?.Invoke(); }
                    });
                }
            }
            return results;
        }

        static string QueryString(AuctionQuery q)
        {
            var parts = new List<string> { "limit=20" };
            string text = (q.text ?? "").Trim();
            if (text.Length > 0) parts.Add("q=" + Uri.EscapeDataString(text.Length > 20 ? text.Substring(0, 20) : text));
            if (q.category != AuctionCategory.All) parts.Add("category=" + q.category.ToString().ToLowerInvariant());
            if (q.rarity >= 0) parts.Add("rarity=" + q.rarity);
            if (q.enhMin > 0) parts.Add("enh_min=" + q.enhMin);
            if (q.enhMax < EquipmentDatabase.MaxEnhance) parts.Add("enh_max=" + q.enhMax);
            if (q.priceMin > 0) parts.Add("price_min=" + q.priceMin);
            if (q.priceMax < long.MaxValue) parts.Add("price_max=" + q.priceMax);
            parts.Add("sort=" + (q.sort == AuctionSort.PriceDesc ? "price_desc" : q.sort == AuctionSort.TimeLeft ? "time_left"
                : q.sort == AuctionSort.EnhanceDesc ? "enhance_desc" : q.sort == AuctionSort.Newest ? "newest" : "price_asc"));
            return "?" + string.Join("&", parts);
        }

        static List<AuctionListing> ReadListings(List<object> list, bool mineFlag)
        {
            var outList = new List<AuctionListing>();
            int order = 0;
            foreach (var o in list ?? new List<object>())
            {
                string key = MiniJson.Str(o, "item_key");
                bool hasBid = MiniJson.Has(o, "current_bid");
                long start = (long)MiniJson.Num(o, "start_bid");
                int band = MiniJson.Int(o, "hours_left_band", 48);
                outList.Add(new AuctionListing
                {
                    id = MiniJson.Str(o, "id"),
                    itemKey = key,
                    count = MiniJson.Int(o, "count", 1),
                    seller = mineFlag ? "나" : MiniJson.Str(o, "seller_name", ""),
                    buyout = (long)MiniJson.Num(o, "buyout"),
                    startBid = start,
                    currentBid = (long)MiniJson.Num(o, "current_bid"),
                    hasBid = hasBid,
                    deposit = (long)MiniJson.Num(o, "deposit"),
                    hoursLeft = band - 0.01, // only the band is known; TimeBand shows the same band
                    order = order++,
                    mine = mineFlag,
                    myBid = o is Dictionary<string, object> d && d.TryGetValue("my_bid", out var mb) && mb is bool b && b,
                    category = CategoryOf(MiniJson.Str(o, "category")),
                    rarity = (ItemRarity)Mathf.Max(0, MiniJson.Int(o, "rarity")),
                    enhance = MiniJson.Int(o, "enhance"),
                    serverMinBid = (long)MiniJson.Num(o, "min_bid"),
                    willBind = MiniJson.Str(o, "will_bind") == "account",
                });
            }
            return outList;
        }

        static AuctionCategory CategoryOf(string c)
        {
            switch (c)
            {
                case "weapon": return AuctionCategory.Weapon;
                case "armor": return AuctionCategory.Armor;
                case "accessory": return AuctionCategory.Accessory;
                case "material": return AuctionCategory.Material;
                default: return AuctionCategory.Consumable;
            }
        }

        public IReadOnlyList<AuctionListing> MyListings()
        {
            if (!loadingMine && Now - mineAt > StaleSeconds)
            {
                loadingMine = true;
                mineAt = Now;
                Api.Get(Char + "/auction/mine", r =>
                {
                    loadingMine = false;
                    if (!r.ok) return;
                    var list = ReadListings(MiniJson.Arr(r.data, "listings"), true);
                    var bids = ReadListings(MiniJson.Arr(r.data, "bids"), false);
                    foreach (var b in bids) b.myBid = true;
                    list.AddRange(bids); // my top bids show under 내 등록 too, marked as my bid
                    mine = list;
                    Changed?.Invoke();
                });
            }
            return mine;
        }

        public IReadOnlyList<AuctionMail> Mailbox()
        {
            if (!loadingMail && Now - mailAt > StaleSeconds) LoadMail();
            return mail;
        }

        void LoadMail()
        {
            loadingMail = true;
            mailAt = Now;
            Api.Get(Char + "/mail?limit=50", r =>
            {
                loadingMail = false;
                if (!r.ok) return;
                mail = new List<AuctionMail>();
                foreach (var o in MiniJson.Arr(r.data, "mails") ?? new List<object>())
                {
                    var item = MiniJson.Obj(o, "item");
                    var m = new AuctionMail
                    {
                        id = MiniJson.Str(o, "id"),
                        itemKey = MiniJson.Str(item, "item_key"),
                        count = MiniJson.Int(item, "count"),
                        gold = (long)MiniJson.Num(o, "gold"),
                        kind = MiniJson.Str(o, "kind"),
                        systemCode = MiniJson.Str(o, "system_code"),
                        title = MiniJson.Str(o, "title"),
                        body = MiniJson.Str(o, "body"),
                        campaign = MiniJson.Has(o, "campaign") && o is Dictionary<string, object> od && od["campaign"] is bool cb && cb,
                        daysLeft = MiniJson.Int(o, "days_left"),
                        text = MailText(MiniJson.Str(o, "kind"), MiniJson.Str(o, "ref_item_key"), MiniJson.Int(o, "ref_count", 1), MiniJson.Int(o, "days_left"), MiniJson.Str(o, "system_code")),
                    };
                    // [MAIL 10] Attachments (the server also builds them for old mail); old servers: from item/gold.
                    var atts = MiniJson.Arr(o, "attachments");
                    if (atts != null)
                        foreach (var a in atts)
                            m.attachments.Add(new MailAttachment { kind = MiniJson.Str(a, "kind"), itemKey = MiniJson.Str(a, "item_key"), count = (long)MiniJson.Num(a, "count"), validDays = MiniJson.Int(a, "valid_days_after_claim") });
                    else
                    {
                        if (m.gold > 0) m.attachments.Add(new MailAttachment { kind = "gold", count = m.gold });
                        if (!string.IsNullOrEmpty(m.itemKey)) m.attachments.Add(new MailAttachment { kind = "item", itemKey = m.itemKey, count = m.count });
                    }
                    if (!string.IsNullOrEmpty(m.title)) m.text = m.title + (m.daysLeft > 0 ? $"  <color=#8c96a8>{m.daysLeft}일 남음</color>" : "");
                    mail.Add(m);
                }
                unclaimed = mail.Count;
                Changed?.Invoke();
            });
        }

        static string MailText(string kind, string key, int count, int daysLeft, string systemCode = null)
        {
            string name = string.IsNullOrEmpty(key) ? "" : DungeonDatabase.ItemName(key) + (count > 1 ? $" x{count}" : "");
            string head;
            switch (kind)
            {
                case "sold": head = $"[판매 대금] {name}"; break;
                case "bought": head = $"[구매] {name}"; break;
                case "outbid": head = $"[입찰 반환] {name}"; break;
                case "expired": head = $"[기간 만료] {name}"; break;
                case "cancelled": head = $"[등록 취소] {name} (보증금 미반환)"; break;
                case "system":
                    string tag = systemCode == "compensation" ? "[보상]" : systemCode == "event" ? "[이벤트]" : systemCode == "refund" ? "[환불]"
                        : systemCode == "maintenance" ? "[점검 보상]" : systemCode == "apology" ? "[사과 보상]" : systemCode == "attendance" ? "[출석]" : "[안내]";
                    head = string.IsNullOrEmpty(name) ? $"{tag} 운영팀이 보낸 우편" : $"{tag} {name}";
                    break;
                default: head = name; break;
            }
            return daysLeft > 0 ? $"{head}  <color=#8c96a8>{daysLeft}일 남음</color>" : head;
        }

        public AuctionPrice Price(string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey)) return new AuctionPrice();
            if (prices.TryGetValue(itemKey, out var p)) return p;
            if (priceLoading.Add(itemKey))
                Api.Get($"{Char}/auction/prices/{Uri.EscapeDataString(itemKey)}", r =>
                {
                    priceLoading.Remove(itemKey);
                    if (!r.ok) return;
                    // The client multiplies by the count itself, so keep per-item numbers.
                    int count = Mathf.Max(1, MiniJson.Int(r.data, "count", 1));
                    prices[itemKey] = new AuctionPrice
                    {
                        avg7d = (long)MiniJson.Num(r.data, "avg7d") / count,
                        min = (long)MiniJson.Num(r.data, "min") / count,
                        max = (long)MiniJson.Num(r.data, "max") / count,
                        volume = MiniJson.Int(r.data, "volume"),
                        limitMin = (long)MiniJson.Num(MiniJson.Obj(r.data, "limits"), "min") / count,
                        limitMax = (long)MiniJson.Num(MiniJson.Obj(r.data, "limits"), "max") / count,
                    };
                    Changed?.Invoke();
                });
            return new AuctionPrice();
        }

        public ItemBind BindOf(string key)
        {
            RefreshSellable();
            if (sellable.TryGetValue(key, out var s)) return s.bind;
            return AuctionRules.BindFloor(key) == ItemBind.CharacterBound ? ItemBind.CharacterBound : ItemBind.Tradable;
        }

        public int MaxCount(string key)
        {
            RefreshSellable();
            if (sellable.TryGetValue(key, out var s)) return s.max;
            return EquipmentDatabase.IsEquipment(key) ? 1 : Mathf.Min(AuctionRules.MaxStack, Game.Session?.Inventory.Count(key) ?? 0);
        }

        void RefreshSellable()
        {
            if (loadingSell || Now - sellAt < StaleSeconds) return;
            loadingSell = true;
            sellAt = Now;
            Api.Get(Char + "/auction/sellable", r =>
            {
                loadingSell = false;
                if (!r.ok) return;
                sellable.Clear();
                foreach (var o in MiniJson.Arr(r.data, "items") ?? new List<object>())
                {
                    string key = MiniJson.Str(o, "item_key");
                    bool listable = o is Dictionary<string, object> d && d.TryGetValue("listable", out var l) && l is bool b && b;
                    // A key with any listable row can be sold; otherwise show its strongest binding.
                    var bind = listable ? ItemBind.Tradable : MiniJson.Str(o, "bind") == "account" ? ItemBind.AccountBound : ItemBind.CharacterBound;
                    if (sellable.TryGetValue(key, out var have) && have.bind == ItemBind.Tradable) continue;
                    sellable[key] = (bind, MiniJson.Int(o, "max_count", 1));
                }
                Changed?.Invoke();
            });
        }

        // ---------------- actions ----------------

        AuctionResult Act(string path, Dictionary<string, object> body, Func<ApiResult, string> success, bool delete = false)
        {
            if (busy) return AuctionResult.Fail("이전 요청을 처리하는 중입니다.");
            busy = true;
            Action<ApiResult> done = r =>
            {
                busy = false;
                if (r.ok) OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                if (r.ok) SweepClient.ReadTickets(MiniJson.Obj(MiniJson.Obj(r.data, "claimed"), "tickets") ?? MiniJson.Obj(r.data, "tickets")); // [MAIL 10] event tickets
                GameEvents.RaiseToast(r.ok ? success(r) : Explain(r));
                searchAt = mineAt = mailAt = sellAt = -99f; // everything may have moved
                Summary();
                Changed?.Invoke();
            };
            if (delete) Api.Delete(Char + path + "?request_id=" + ApiClient.NewRequestId(), done); // ties the cancel to the ledger
            else
            {
                body = body ?? new Dictionary<string, object>();
                body["request_id"] = ApiClient.NewRequestId();
                Api.PostIdempotent(Char + path, body, done);
            }
            return AuctionResult.Ok("서버에 요청했습니다...");
        }

        static string Explain(ApiResult r)
        {
            switch (r.code)
            {
                case "BID_TOO_LOW":
                    long min = (long)MiniJson.Num(r.errors, "min_bid");
                    return min > 0 ? $"그 사이 더 높은 입찰이 있었습니다. 최소 입찰가는 {min:N0}G입니다." : r.message;
                case "PRICE_OUT_OF_RANGE":
                    long lo = (long)MiniJson.Num(r.errors, "min"), hi = (long)MiniJson.Num(r.errors, "max");
                    return hi > 0 ? $"가격은 {lo:N0}~{hi:N0}G 사이여야 합니다." : r.message;
                case "NETWORK": return "서버에 연결할 수 없습니다.";
                default: return string.IsNullOrEmpty(r.message) ? $"요청이 실패했습니다. ({r.status})" : r.message;
            }
        }

        public AuctionResult Buyout(string listingId) =>
            Act($"/auction/listings/{listingId}/buyout", null, r => "구매했습니다. 우편함에서 받으세요." + (MiniJson.Str(r.data, "bind") == "account" ? " (계정 귀속)" : ""));

        public AuctionResult Bid(string listingId, long amount) =>
            Act($"/auction/listings/{listingId}/bids", new Dictionary<string, object> { ["amount"] = amount },
                r => MiniJson.Str(r.data, "result") == "bought" ? "즉시 구매가 이상이라 바로 구매했습니다. 우편함을 확인하세요." : $"{amount:N0}G로 입찰했습니다. (골드 예치)");

        public AuctionResult Register(string itemKey, int count, long buyout, long startBid, int hours)
        {
            var body = new Dictionary<string, object> { ["item_key"] = itemKey, ["count"] = Mathf.Max(1, count), ["buyout"] = buyout, ["hours"] = hours };
            if (startBid > 0) body["start_bid"] = startBid;
            return Act("/auction/listings", body, r => $"{DungeonDatabase.ItemName(itemKey)} 등록 완료 (보증금 {(long)MiniJson.Num(r.data, "deposit"):N0}G)");
        }

        public AuctionResult Cancel(string listingId) =>
            Act($"/auction/listings/{listingId}", null, r => "등록을 취소했습니다. 아이템은 우편함으로 돌아갑니다.", delete: true);

        public AuctionResult Claim(string mailId) => Act($"/mail/{mailId}/claim", null, r => "우편을 받았습니다.");

        public AuctionResult ClaimAll() => Act("/mail/claim-all", null, r =>
        {
            int n = MiniJson.Int(r.data, "claimed_count"), left = MiniJson.Int(r.data, "remaining");
            return left > 0 ? $"우편 {n}통을 받았습니다. 남은 {left}통은 다시 '모두 받기'를 누르세요." : $"우편 {n}통을 받았습니다.";
        });

        // ---------------- mail summary (red dot, new mail toasts) ----------------

        string latestAt;

        /// <summary>Called every frame by the side menu badge; polls slowly (faster while the window is open).</summary>
        public void Tick(bool windowOpen)
        {
            if (Now - summaryAt < (windowOpen ? SummaryOpenSeconds : SummaryClosedSeconds)) return;
            Summary();
        }

        void Summary()
        {
            summaryAt = Now;
            string q = string.IsNullOrEmpty(latestAt) ? "" : "?since=" + Uri.EscapeDataString(latestAt);
            Api.Get(Char + "/mail/summary" + q, r =>
            {
                if (!r.ok) return;
                int before = unclaimed;
                unclaimed = MiniJson.Int(r.data, "unclaimed");
                bool first = latestAt == null;
                latestAt = MiniJson.Str(r.data, "latest_at", latestAt) ?? "";
                if (!first)
                    foreach (var n in MiniJson.Arr(r.data, "new") ?? new List<object>())
                    {
                        string title = MiniJson.Str(n, "title");
                        GameEvents.RaiseToast("<color=#ffd84a>[우편]</color> " + (!string.IsNullOrEmpty(title) ? title : MailText(MiniJson.Str(n, "kind"), MiniJson.Str(n, "ref_item_key"), 1, 0)));
                    }
                if (unclaimed != before) { mailAt = -99f; Changed?.Invoke(); }
            });
        }
    }
}
