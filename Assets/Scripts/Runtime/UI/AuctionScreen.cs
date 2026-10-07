using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[ONLINE] 경매장: 검색 / 내 등록 / 등록하기 / 우편함 (PLAN_AUCTION §10). Mock data offline.</summary>
    public class AuctionScreen : OnlineWindow
    {
        public static AuctionScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        enum Tab { Search, Mine, Register, Mail }
        const int PageSize = 9;
        const float RowH = 42f, TableW = 1220f, TableTop = -150f;

        IAuctionService Service => OnlineServices.Auction;
        Tab tab;
        int page;
        readonly AuctionQuery query = new AuctionQuery();
        int enhBand, priceBand;
        static readonly (int min, int max, string name)[] EnhBands = { (0, 20, "전체"), (0, 0, "+0"), (1, 6, "+1~6"), (7, 9, "+7~9"), (10, 12, "+10~12"), (13, 20, "+13 이상") };
        static readonly (long min, long max, string name)[] PriceBands = { (0, long.MaxValue, "전체"), (0, 999, "1천 미만"), (1000, 9999, "1천~1만"), (10000, long.MaxValue, "1만 이상") };

        readonly Dictionary<Tab, Button> tabs = new Dictionary<Tab, Button>();
        RectTransform tableRoot, filterRoot, registerRoot;
        Button catBtn, rarBtn, enhBtn, priceBtn, sortBtn, claimAllBtn;
        InputField search;
        Text pageText, goldText, headText, emptyText, regEmptyText;
        // [UI] Column headings placed over the row cells (a space-padded heading line drifted off the columns).
        Text[] headCols;
        readonly List<(RectTransform row, Image icon, Text name, Text grade, Text price, Text bid, Text seller, Text time, Button a, Button b)> rows =
            new List<(RectTransform, Image, Text, Text, Text, Text, Text, Text, Button, Button)>();
        List<AuctionListing> listings = new List<AuctionListing>();
        List<AuctionMail> mails = new List<AuctionMail>();
        // register tab
        List<string> bagKeys = new List<string>();
        int regPage, regSel = -1, regHoursIdx = 1;
        long regPrice;
        readonly List<(RectTransform row, Image icon, Text name, Text bind, Button pick)> regRows = new List<(RectTransform, Image, Text, Text, Button)>();
        Text regInfo, regPageText;

        public static AuctionScreen Create(Transform canvas)
        {
            var w = CreateWindow<AuctionScreen>(canvas, "Auction", "경매장", "menuicon_auction");
            Instance = w;
            w.PreviewBanner();
            string[] names = { "검색", "내 등록", "등록하기", "우편함" };
            for (int i = 0; i < 4; i++)
            {
                var t = (Tab)i;
                w.tabs[t] = Button(w.content, "Tab" + i, names[i], "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * 140f, 0f), new Vector2(132f, 42f), () => { w.tab = t; w.page = 0; w.Refresh(); }, 20);
            }
            w.goldText = Label(w.content, "Gold", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(580f, 0f), new Vector2(290f, 42f), TextAnchor.MiddleRight);

            // ----- search filters -----
            w.filterRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Filters"));
            w.search = w.Field(w.filterRoot, "Search", "아이템 이름 검색", new Vector2(0f, -54f), new Vector2(250f, 40f));
            w.focusField = w.search;
            w.search.onEndEdit.AddListener(_ => { w.page = 0; w.Refresh(); });
            float x = 260f;
            Button Filter(string n, Action a, float width) { var b = Button(w.filterRoot, n, "", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -54f), new Vector2(width, 40f), () => { a(); w.page = 0; w.Refresh(); }, 17); x += width + 8f; return b; }
            w.catBtn = Filter("Cat", () => w.query.category = (AuctionCategory)(((int)w.query.category + 1) % 6), 150f);
            w.rarBtn = Filter("Rar", () => w.query.rarity = w.query.rarity >= (int)ItemRarity.Legendary ? -1 : w.query.rarity + 1, 150f);
            w.enhBtn = Filter("Enh", () => w.enhBand = (w.enhBand + 1) % EnhBands.Length, 150f);
            w.priceBtn = Filter("Price", () => w.priceBand = (w.priceBand + 1) % PriceBands.Length, 160f);
            w.sortBtn = Filter("Sort", () => w.query.sort = (AuctionSort)(((int)w.query.sort + 1) % 5), 190f);
            Button(w.filterRoot, "Go", "검색", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -54f), new Vector2(TableW - x, 40f), () => { w.page = 0; w.Refresh(); }, 19);

            // ----- table (search / mine / mail) -----
            w.tableRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Table"));
            var head = Panel(w.tableRoot, "Head", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, TableTop + 46f), new Vector2(TableW, 30f), HeadRow);
            w.headText = Label(head.transform, "Text", "", 16, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(50f, 0f), new Vector2(330f, 28f), TextAnchor.MiddleLeft);
            Text HeadCol(string n, float hx, float hw, TextAnchor align) => Label(head.transform, n, "", 16, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(hx, 0f), new Vector2(hw, 28f), align);
            w.headCols = new[] { HeadCol("HGrade", 385f, 95f, TextAnchor.MiddleLeft), HeadCol("HPrice", 485f, 140f, TextAnchor.MiddleRight), HeadCol("HBid", 635f, 125f, TextAnchor.MiddleRight),
                HeadCol("HSeller", 780f, 120f, TextAnchor.MiddleLeft), HeadCol("HTime", 905f, 120f, TextAnchor.MiddleLeft) };
            for (int i = 0; i < PageSize; i++)
            {
                var r = Row(w.tableRoot, i, TableTop + 14f, RowH, TableW);
                var icon = UIFactory.Image(r, "Icon", null, Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(34f, 34f));
                int idx = i;
                GearTooltip.Hook(icon, () => w.TableKey(idx));
                var a = Button(r, "A", "즉시 구매", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-82f, 0f), new Vector2(104f, 34f), () => w.RowAction(idx, true), 16);
                var b = Button(r, "B", "입찰", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(72f, 34f), () => w.RowAction(idx, false), 16);
                w.rows.Add((r, icon, Cell(r, "Name", 50f, 330f), Cell(r, "Grade", 385f, 95f, 17), Cell(r, "Price", 485f, 140f, 18, TextAnchor.MiddleRight),
                    Cell(r, "Bid", 635f, 125f, 17, TextAnchor.MiddleRight), Cell(r, "Seller", 780f, 120f, 17), Cell(r, "Time", 905f, 120f, 16), a, b));
            }
            w.emptyText = Label(w.tableRoot, "Empty", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, TableTop - 10f), new Vector2(TableW, 40f), TextAnchor.MiddleCenter);
            w.emptyText.color = new Color32(184, 196, 216, 255);
            // 모두 받기 shows on the mail tab only, where the table sits 50 higher: the bottom-left corner is free (status line is below it).
            w.claimAllBtn = Button(w.tableRoot, "ClaimAll", "모두 받기", "ui_btn", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), UiSizes.ClaimAllButton, () => w.Report(w.Service.ClaimAll()), UiSizes.ClaimAllFont);
            Button(w.tableRoot, "Prev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-140f, 0f), UiSizes.PageButton, () => { w.page = Math.Max(0, w.page - 1); w.Refresh(); }, UiSizes.PageFont);
            w.pageText = Label(w.tableRoot, "Page", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-54f, 0f), new Vector2(80f, 34f), TextAnchor.MiddleCenter);
            Button(w.tableRoot, "Next", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), UiSizes.PageButton, () => { w.page++; w.Refresh(); }, UiSizes.PageFont);

            // ----- register -----
            w.registerRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Register"));
            var left = Panel(w.registerRoot, "Bag", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -54f), new Vector2(620f, 470f), new Color32(24, 36, 54, 235));
            Label(left.transform, "Title", "<b>가방</b>  <color=#8c96a8>(거래 가능한 아이템만 등록할 수 있습니다)</color>", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(600f, 30f));
            for (int i = 0; i < 8; i++)
            {
                var r = Row(left.transform, i, -44f, 46f, 600f);
                r.anchoredPosition += new Vector2(10f, 0f);
                var icon = UIFactory.Image(r, "Icon", null, Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(34f, 34f));
                int idx = i;
                GearTooltip.Hook(icon, () => { int at = w.regPage * 8 + idx; return at < w.bagKeys.Count ? w.bagKeys[at] : null; });
                var pick = Button(r, "Pick", "선택", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(80f, 36f), () => { w.regSel = w.regPage * 8 + idx; w.regPrice = 0; w.Refresh(); }, 16);
                w.regRows.Add((r, icon, Cell(r, "Name", 50f, 340f), Cell(r, "Bind", 395f, 110f, 16), pick));
            }
            w.regEmptyText = Label(left.transform, "Empty", "가방에 등록할 수 있는 아이템이 없습니다.", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -60f), new Vector2(600f, 40f), TextAnchor.MiddleCenter);
            w.regEmptyText.color = new Color32(184, 196, 216, 255);
            Button(left.transform, "RPrev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-140f, 8f), UiSizes.PageButton, () => { w.regPage = Math.Max(0, w.regPage - 1); w.Refresh(); }, UiSizes.PageFont);
            w.regPageText = Label(left.transform, "RPage", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-64f, 8f), new Vector2(70f, 34f), TextAnchor.MiddleCenter);
            Button(left.transform, "RNext", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 8f), UiSizes.PageButton, () => { w.regPage++; w.Refresh(); }, UiSizes.PageFont);
            var right = Panel(w.registerRoot, "Form", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -54f), new Vector2(590f, 470f), new Color32(24, 36, 54, 235));
            w.regInfo = Label(right.transform, "Info", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(550f, 270f));
            long[] steps = { -1000, -100, 100, 1000 };
            for (int i = 0; i < steps.Length; i++)
            {
                long s = steps[i];
                Button(right.transform, "P" + i, (s > 0 ? "+" : "") + s.ToString("N0"), "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f + i * 112f, 140f), new Vector2(104f, 40f), () => { w.regPrice = Math.Max(10, w.regPrice + s); w.Refresh(); }, 17);
            }
            for (int i = 0; i < AuctionRules.Durations.Length; i++)
            {
                int k = i;
                Button(right.transform, "H" + i, AuctionRules.Durations[i] + "시간", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f + i * 112f, 90f), new Vector2(104f, 40f), () => { w.regHoursIdx = k; w.Refresh(); }, 17);
            }
            Button(right.transform, "Submit", "등록", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(240f, 52f), w.SubmitRegister, 22);
            return w;
        }

        void Report(AuctionResult r) { SetStatus(r.message, r.ok); Refresh(); }

        AuctionListing RowListing(int idx)
        {
            int i = page * PageSize + idx;
            return i < listings.Count ? listings[i] : null;
        }

        /// <summary>The item key shown on table row <paramref name="idx"/> of this page (for the gear tooltip).</summary>
        string TableKey(int idx)
        {
            int i = page * PageSize + idx;
            if (tab == Tab.Mail) return i < mails.Count ? mails[i].itemKey : null;
            return i < listings.Count ? listings[i].itemKey : null;
        }

        void RowAction(int idx, bool primary)
        {
            if (tab == Tab.Mail)
            {
                int i = page * PageSize + idx;
                if (i < mails.Count) Report(Service.Claim(mails[i].id));
                return;
            }
            var l = RowListing(idx);
            if (l == null) return;
            if (tab == Tab.Mine) { Report(Service.Cancel(l.id)); return; }
            var price = Service.Price(l.itemKey);
            if (primary)
            {
                int pct = price.avg7d > 0 ? (int)Math.Round((l.buyout / (double)(price.avg7d * l.count) - 1) * 100) : 0;
                string cmp = pct >= 100 ? $"<color=#ff7a7a>평균보다 +{pct}%</color>" : pct >= 0 ? $"평균 대비 +{pct}%" : $"평균 대비 {pct}%";
                Game.UI.Confirm($"{RichKey(l.itemKey)} x{l.count}\n{l.buyout:N0}G에 즉시 구매할까요?\n<size=18>최근 7일 평균 {price.avg7d * l.count:N0}G ({cmp})</size>", () => Report(Service.Buyout(l.id)), overlay: true);
            }
            else
            {
                if (!l.Biddable) { SetStatus("즉시 구매만 가능한 물건입니다.", false); return; }
                long bid = AuctionRules.MinBid(l);
                Game.UI.Confirm($"{RichKey(l.itemKey)} x{l.count}\n최소 입찰가 {bid:N0}G로 입찰할까요?\n<size=18>입찰 골드는 예치되고, 더 높은 입찰이 오면 우편으로 돌아옵니다.</size>", () => Report(Service.Bid(l.id, bid)), overlay: true);
            }
        }

        static string RichKey(string key) => EquipmentDatabase.IsEquipment(key) ? EquipmentDatabase.RichName(key) : DungeonDatabase.ItemName(key);

        void SubmitRegister()
        {
            if (regSel < 0 || regSel >= bagKeys.Count) { SetStatus("등록할 아이템을 고르세요.", false); return; }
            string key = bagKeys[regSel];
            int count = Service.MaxCount(key); // [SERVER 6] equipment 1, stacks up to the listing maximum
            var r = Service.Register(key, count, regPrice, 0, AuctionRules.Durations[regHoursIdx]);
            if (r.ok) { regSel = -1; regPrice = 0; }
            Report(r);
        }

        static List<string> TradableBag()
        {
            var bag = Game.Session?.Inventory;
            if (bag == null) return new List<string>();
            return bag.Ids.Where(id => id != ConsumableDatabase.Gold && bag.Count(id) > 0)
                .OrderBy(id => OnlineServices.Auction.BindOf(id) == ItemBind.Tradable ? 0 : 1).ThenBy(id => id).ToList();
        }

        // [SERVER 6] Online the lists arrive later: redraw when the service says something changed.
        IAuctionService bound;
        void OnEnable() { bound = Service; bound.Changed += OnServiceChanged; }
        void OnDisable() { if (bound != null) bound.Changed -= OnServiceChanged; bound = null; }
        void OnServiceChanged() { if (gameObject.activeInHierarchy) Refresh(); }

        protected override void Refresh()
        {
            var banner = content.Find("Preview");
            if (banner != null) banner.gameObject.SetActive(!Service.IsOnline);
            if (Service is ServerAuctionService server) server.Tick(true);
            foreach (var kv in tabs) kv.Value.image.sprite = UiTheme.Tab(kv.Key == tab);
            int mailN = Service.UnclaimedMail;
            TextOf(tabs[Tab.Mail]).text = mailN > 0 ? $"우편함 <color=#ff6b6b>●{mailN}</color>" : "우편함";
            goldText.text = $"<color=#ffd34a>{Game.Session?.Gold ?? 0:N0} G</color>";
            filterRoot.gameObject.SetActive(tab == Tab.Search);
            tableRoot.gameObject.SetActive(tab != Tab.Register);
            registerRoot.gameObject.SetActive(tab == Tab.Register);
            claimAllBtn.gameObject.SetActive(tab == Tab.Mail);
            if (tab == Tab.Register) { RefreshRegister(); return; }

            // Search / mine tables start right under the tabs when there is no filter row.
            tableRoot.anchoredPosition = tab == Tab.Search ? Vector2.zero : new Vector2(0f, 50f);
            TextOf(catBtn).text = "분류: " + AuctionRules.CategoryName(query.category);
            TextOf(rarBtn).text = "등급: " + (query.rarity < 0 ? "전체" : EquipmentDatabase.RarityName((ItemRarity)query.rarity));
            TextOf(enhBtn).text = "강화: " + EnhBands[enhBand].name;
            TextOf(priceBtn).text = "가격: " + PriceBands[priceBand].name;
            TextOf(sortBtn).text = "정렬: " + AuctionRules.SortName(query.sort);
            query.text = search.text;
            query.enhMin = EnhBands[enhBand].min;
            query.enhMax = EnhBands[enhBand].max;
            query.priceMin = PriceBands[priceBand].min;
            query.priceMax = PriceBands[priceBand].max;

            int total;
            if (tab == Tab.Mail)
            {
                mails = Service.Mailbox().ToList();
                total = mails.Count;
                SetHead("내용", "", "", "", "", "보관 30일");
            }
            else
            {
                listings = (tab == Tab.Search ? Service.Search(query) : Service.MyListings()).ToList();
                total = listings.Count;
                SetHead("아이템", "등급", "즉시 구매가", "입찰가", "판매자", "남은 시간");
            }
            int pages = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            emptyText.gameObject.SetActive(total == 0);
            emptyText.text = tab == Tab.Mail ? "받을 우편이 없습니다."
                : tab == Tab.Mine ? "등록한 물건이 없습니다. 등록하기 탭에서 가방의 아이템을 올릴 수 있습니다."
                : "조건에 맞는 물건이 없습니다.";
            if (tab == Tab.Mail) claimAllBtn.interactable = total > 0;
            for (int r = 0; r < rows.Count; r++)
            {
                int i = page * PageSize + r;
                var row = rows[r];
                bool on = i < total;
                row.row.gameObject.SetActive(on);
                if (!on) continue;
                if (tab == Tab.Mail)
                {
                    var m = mails[i];
                    string key = m.itemKey ?? ConsumableDatabase.Gold;
                    row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(key));
                    row.name.text = m.text;
                    row.grade.text = "";
                    row.price.text = m.gold > 0 ? $"<color=#ffd34a>{m.gold:N0}G</color>" : "";
                    row.bid.text = row.seller.text = row.time.text = "";
                    TextOf(row.a).text = "받기";
                    row.a.interactable = true;
                    row.b.gameObject.SetActive(false);
                    continue;
                }
                var l = listings[i];
                row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(l.itemKey));
                row.name.text = RichKey(l.itemKey) + (l.count > 1 ? $" x{l.count}" : "");
                bool gear = EquipmentDatabase.IsEquipment(l.itemKey);
                row.grade.text = gear ? $"<color={EquipmentDatabase.RarityColor(l.rarity)}>{EquipmentDatabase.RarityName(l.rarity)}</color>" : $"<color=#8c96a8>{AuctionRules.CategoryName(l.category)}</color>";
                row.price.text = $"<color=#ffd34a>{l.buyout:N0}G</color>";
                row.bid.text = !l.Biddable ? "<color=#8c96a8>-</color>" : l.hasBid ? (l.myBid ? $"<color=#8fe28f>{l.currentBid:N0}G</color>" : $"{l.currentBid:N0}G") : $"<color=#b8c4d8>{l.startBid:N0}G~</color>";
                row.seller.text = l.seller;
                row.time.text = AuctionRules.TimeBand(l.hoursLeft);
                if (tab == Tab.Mine)
                {
                    TextOf(row.a).text = "등록 취소";
                    row.a.interactable = !l.hasBid;
                    row.b.gameObject.SetActive(false);
                }
                else
                {
                    TextOf(row.a).text = "즉시 구매";
                    row.a.interactable = true;
                    row.b.gameObject.SetActive(true);
                    row.b.interactable = l.Biddable;
                }
            }
        }

        void SetHead(string name, params string[] cols)
        {
            headText.text = $"<color=#b8c4d8>{name}</color>";
            for (int i = 0; i < headCols.Length; i++) headCols[i].text = i < cols.Length && cols[i] != "" ? $"<color=#b8c4d8>{cols[i]}</color>" : "";
        }

        void RefreshRegister()
        {
            bagKeys = TradableBag();
            int pages = Math.Max(1, (bagKeys.Count + 7) / 8);
            regPage = Mathf.Clamp(regPage, 0, pages - 1);
            regPageText.text = $"{regPage + 1} / {pages}";
            regEmptyText.gameObject.SetActive(bagKeys.Count == 0);
            var bag = Game.Session.Inventory;
            for (int r = 0; r < regRows.Count; r++)
            {
                int i = regPage * 8 + r;
                var row = regRows[r];
                bool on = i < bagKeys.Count;
                row.row.gameObject.SetActive(on);
                if (!on) continue;
                string key = bagKeys[i];
                var bind = Service.BindOf(key);
                row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(key));
                int n = bag.Count(key);
                row.name.text = (i == regSel ? "<color=#ffe066>▶</color> " : "") + RichKey(key) + (n > 1 ? $" x{n}" : "");
                row.bind.text = AuctionRules.BindLabel(bind);
                row.pick.interactable = bind == ItemBind.Tradable;
            }
            if (regSel < 0 || regSel >= bagKeys.Count)
            {
                regInfo.text = "<b>등록하기</b>\n\n왼쪽 가방에서 팔 아이템을 고르세요.\n\n· 보증금: 즉시 구매가의 1% (최소 10G, 판매 시 반환)\n· 수수료: 장비 5%, 재료·소모품 3% (판매 시)\n· 기간: 12 / 24 / 48시간\n· 장비 보호권·퀘스트 보상은 귀속이라 팔 수 없습니다.";
                return;
            }
            string k = bagKeys[regSel];
            int count = Service.MaxCount(k);
            var price = Service.Price(k);
            if (regPrice <= 0) regPrice = Math.Max(10, price.avg7d * count / 10 * 10);
            long deposit = AuctionRules.Deposit(regPrice), fee = AuctionRules.Fee(k, regPrice);
            regInfo.text = $"<b>{RichKey(k)}</b> x{count}\n" +
                           $"<color=#b8c4d8>최근 7일 평균 {price.avg7d * count:N0}G  (최저 {price.min * count:N0} · 최고 {price.max * count:N0})</color>\n\n" +
                           $"즉시 구매가  <color=#ffd34a>{regPrice:N0}G</color>\n" +
                           (price.limitMax > 0 ? $"<color=#8c96a8>등록 가능 {price.limitMin * count:N0}~{price.limitMax * count:N0}G</color>\n" : "") +
                           $"기간  {AuctionRules.Durations[regHoursIdx]}시간\n" +
                           $"보증금  {deposit:N0}G <color=#8c96a8>(판매되면 반환)</color>\n" +
                           $"수수료  {fee:N0}G ({AuctionRules.FeePct(k)}%)\n" +
                           $"예상 수령액  <color=#8fe28f>{AuctionRules.Payout(k, regPrice) + deposit:N0}G</color>";
        }

        // ---------- dev hooks ----------
        public void DevTab(int t) { tab = (Tab)t; page = 0; Refresh(); }
        public int DevVisibleRows => rows.Count(r => r.row.gameObject.activeSelf);
        public string DevRowName(int r) => r < rows.Count ? rows[r].name.text : "";
        public AuctionListing DevListing(int r) => RowListing(r);
        public void DevBuyRow(int r) => RowAction(r, true);
        public void DevSelectRegister(string key, long price, int hoursIdx)
        {
            tab = Tab.Register;
            bagKeys = TradableBag();
            regSel = bagKeys.IndexOf(key);
            regPage = Math.Max(0, regSel) / 8;
            regPrice = price;
            regHoursIdx = hoursIdx;
            Refresh();
        }
        public void DevSubmitRegister() => SubmitRegister();
        public string DevStatus => status.text;
        public void DevSearch(string text) { search.text = text; page = 0; Refresh(); }
    }
}
