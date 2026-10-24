using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 캐시샵 > 별빛 교환소 (Docs/PLAN_CASH_ITEM_VALUE.md). Two tabs:
    /// 교환 품목: Star Crystals buy a chosen cosmetic, gauge fills, materials and the exchange-only cosmetics, some with a
    /// weekly stock (the exchange's stock, not a spending limit).
    /// 장비 분해: gacha gear (it cannot be sold to the shop) becomes Star Crystals and some enhancement materials. The first
    /// Unique / Legendary of each kind stays in the gear codex for account max HP even after it is dismantled.
    /// The server decides everything; this shows its state and sends the requests.
    /// </summary>
    public partial class ExchangeShopScreen : OnlineWindow
    {
        const int GoodRows = 6, GearRows = 6, PickRows = 8;
        const float GoodW = 602f, GoodH = 70f, GearH = 60f, BodyTop = -128f;
        protected override bool PadNavigation => !busy;

        sealed class GoodView { public RectTransform row; public Text name, info; public Button buy; public string id; }
        sealed class GearView { public RectTransform row; public Text name; public Button one, all; public string key; }

        public static ExchangeShopScreen Instance { get; private set; }

        RectTransform goodsTab, gearTab, pickPage;
        Button goodsTabBtn, gearTabBtn, codexBtn, dismantleAllBtn;
        Text balanceText, codexText, gearHead, pickTitle;
        readonly List<GoodView> goods = new List<GoodView>();
        readonly List<GearView> gears = new List<GearView>();
        readonly List<(Button btn, Text label)> pickRows = new List<(Button, Text)>();
        (Button prev, Text label, Button next) gearPager, pickPager;
        int gearPage, pickPageIndex;
        bool showGear, busy;
        ExchangeGood picking;

        public static ExchangeShopScreen Create(Transform canvas)
        {
            var w = CreateWindow<ExchangeShopScreen>(canvas, "ExchangeShop", L.T("별빛 교환소"), "icon_star_crystal");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            var tr = new Vector2(1f, 1f);
            w.goodsTabBtn = Button(w.content, "TabGoods", L.T("교환 품목"), "ui_btn", tl, tl, Vector2.zero, new Vector2(200f, 52f), () => w.ShowTab(false), 20);
            w.gearTabBtn = Button(w.content, "TabGear", L.T("장비 분해"), "ui_btngray", tl, tl, new Vector2(212f, 0f), new Vector2(200f, 52f), () => w.ShowTab(true), 20);

            var header = UIFactory.Image(w.content, "Header", Game.Art.Get("ui_exchange_header"), Color.white);
            header.preserveAspect = true; header.raycastTarget = false;
            UIFactory.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 6f), new Vector2(380f, 120f));

            var gem = UIFactory.Image(w.content, "Crystal", Game.Art.Get("icon_star_crystal"), Color.white);
            gem.preserveAspect = true; gem.raycastTarget = false;
            UIFactory.Place(gem.rectTransform, tr, tr, new Vector2(-372f, 0f), new Vector2(40f, 40f));
            w.balanceText = Label(w.content, "Balance", "", 21, tr, tr, Vector2.zero, new Vector2(366f, 40f), TextAnchor.MiddleLeft);
            var book = UIFactory.Image(w.content, "Codex", Game.Art.Get("icon_codex"), Color.white);
            book.preserveAspect = true; book.raycastTarget = false;
            UIFactory.Place(book.rectTransform, tr, tr, new Vector2(-372f, -48f), new Vector2(40f, 40f));
            w.codexText = Label(w.content, "CodexText", "", 17, tr, tr, new Vector2(-150f, -48f), new Vector2(216f, 40f), TextAnchor.MiddleLeft);
            w.codexBtn = Button(w.content, "CodexRecord", L.T("도감 기록"), "ui_btngray", tr, tr, new Vector2(0f, -48f), new Vector2(140f, 40f), w.RecordCodex, 16);

            // ---------- 교환 품목 (2 columns x 6) ----------
            w.goodsTab = UIFactory.Place(UIFactory.Rect(w.content, "Goods"), tl, tl, new Vector2(0f, BodyTop), new Vector2(1220f, GoodH * GoodRows));
            for (int col = 0; col < 2; col++)
            {
                var colRoot = UIFactory.Place(UIFactory.Rect(w.goodsTab, "Col" + col), tl, tl, new Vector2(col * (GoodW + 16f), 0f), new Vector2(GoodW, GoodH * GoodRows));
                for (int i = 0; i < GoodRows; i++)
                {
                    var v = new GoodView { row = Row(colRoot, i, 0f, GoodH, GoodW) };
                    v.name = Label(v.row, "Name", "", 19, tl, tl, new Vector2(14f, -6f), new Vector2(430f, 30f), TextAnchor.MiddleLeft);
                    v.info = Label(v.row, "Info", "", 15, tl, tl, new Vector2(14f, -36f), new Vector2(430f, 28f), TextAnchor.MiddleLeft);
                    var view = v;
                    v.buy = Button(v.row, "Buy", "", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(146f, 50f), () => w.AskBuy(view.id), 17);
                    w.goods.Add(v);
                }
            }

            // ---------- 장비 분해 ----------
            w.gearTab = UIFactory.Place(UIFactory.Rect(w.content, "Gear"), tl, tl, new Vector2(0f, BodyTop), new Vector2(1220f, 40f + GearRows * GearH));
            w.gearHead = Label(w.gearTab, "Head", "", 17, tl, tl, Vector2.zero, new Vector2(820f, 36f), TextAnchor.MiddleLeft);
            w.dismantleAllBtn = Button(w.gearTab, "DismantleAll", L.T("이 쪽 모두 분해"), "ui_btngray", tr, tr, new Vector2(-170f, 0f), new Vector2(170f, 36f), w.AskDismantlePage, 16);
            w.gearPager = Pager(w.gearTab, new Vector2(-350f, -2f), d => { w.gearPage += d; w.Refresh(); });
            for (int i = 0; i < GearRows; i++)
            {
                var v = new GearView { row = Row(w.gearTab, i, -42f, GearH, 1220f) };
                v.name = Cell(v.row, "Name", 16f, 820f, 18);
                var view = v;
                v.all = Button(v.row, "All", "", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(200f, 46f), () => w.AskDismantle(view.key, false), 16);
                v.one = Button(v.row, "One", L.T("1개 분해"), "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-218f, 0f), new Vector2(110f, 46f), () => w.AskDismantle(view.key, true), 16);
                w.gears.Add(v);
            }

            // ---------- cosmetic choice page (pick goods) ----------
            var page = Panel(w.content, "PickPage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 560f), new Color32(10, 14, 24, 250));
            page.raycastTarget = true;
            w.pickPage = page.rectTransform;
            w.pickTitle = Label(page.transform, "Title", "", 22, tl, tl, new Vector2(20f, -14f), new Vector2(560f, 34f), TextAnchor.MiddleLeft);
            w.pickPager = Pager(page.transform, new Vector2(-14f, -14f), d => { w.pickPageIndex += d; w.RefreshPick(); });
            for (int i = 0; i < PickRows; i++)
            {
                int at = i;
                var b = Button(page.transform, "Choice" + i, "", "ui_btngray", tl, tl, new Vector2(20f, -60f - i * 54f), new Vector2(720f, 48f), () => w.Pick(at), 18);
                var label = TextOf(b);
                label.alignment = TextAnchor.MiddleLeft;
                UIFactory.Stretch(label.rectTransform, 16f, 0f, 16f, 0f);
                w.pickRows.Add((b, label));
            }
            Button(page.transform, "Cancel", L.T("취소"), "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(200f, 48f), w.ClosePick, 18);
            w.pickPage.gameObject.SetActive(false);

            w.status = Label(w.content, "Status", "", 17, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(1200f, 30f), TextAnchor.MiddleLeft);
            StarShopClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            w.ShowTab(false);
            if (TouchUi.Enabled) FitTouch(w); // [TOUCH] ExchangeShopScreen.Mobile.cs
            return w;
        }

        public override void Show()
        {
            base.Show();
            ClosePick();
            if (!StarShopClient.Available) { Refresh(); return; }
            SetStatus(L.T("불러오는 중..."));
            StarShopClient.LoadExchange((ok, msg) => { if (!ok) SetStatus(msg, false); else SetStatus(""); Refresh(); });
        }

        void ShowTab(bool gear)
        {
            showGear = gear;
            ClosePick();
            goodsTab.gameObject.SetActive(!gear);
            gearTab.gameObject.SetActive(gear);
            goodsTabBtn.image.sprite = UiTheme.Tab(!gear);
            gearTabBtn.image.sprite = UiTheme.Tab(gear);
            Refresh();
        }

        static (Button, Text, Button) Pager(Transform parent, Vector2 topRight, System.Action<int> turn)
        {
            var tr = new Vector2(1f, 1f);
            var next = Button(parent, "PageNext", "▶", "ui_btngray", tr, tr, topRight, new Vector2(40f, 32f), () => turn(1), 16);
            var label = Label(parent, "PageText", "", 16, tr, tr, topRight + new Vector2(-44f, 0f), new Vector2(60f, 32f), TextAnchor.MiddleCenter);
            var prev = Button(parent, "PagePrev", "◀", "ui_btngray", tr, tr, topRight + new Vector2(-108f, 0f), new Vector2(40f, 32f), () => turn(-1), 16);
            return (prev, label, next);
        }

        static void SetPager((Button prev, Text label, Button next) p, int page, int pages)
        {
            bool show = pages > 1;
            p.prev.gameObject.SetActive(show); p.next.gameObject.SetActive(show); p.label.gameObject.SetActive(show);
            if (!show) return;
            p.label.text = $"{page + 1}/{pages}";
            p.prev.interactable = page > 0;
            p.next.interactable = page < pages - 1;
        }

        static string Hex(string rarity) =>
            rarity == "legend" ? "#ff6b5e" : rarity == "legendary" || rarity == "Legendary" ? "#ff8c3a" : rarity == "unique" || rarity == "Unique" ? "#ffb347"
            : rarity == "epic" || rarity == "Epic" ? "#c58cff" : rarity == "rare" || rarity == "Rare" ? "#9fc4ff"
            : rarity == "Uncommon" ? "#8fe28f" : "#cfd6e2";

        static string KindText(ExchangeGood g) =>
            g.kind == "cosmetic" ? (string.IsNullOrEmpty(g.itemId) ? L.T("원하는 외형을 골라 받습니다") : L.T("교환소 전용 외형 · 영구 소장"))
            : g.kind == "skin_gauge" ? L.F("스킨 선택 게이지 {0}칸을 채웁니다", g.count)
            : g.kind == "hidden_gauge" ? L.F("히든 게이지 {0}칸을 채웁니다", g.count)
            : g.kind == "gear_gauge" ? L.F("장비 뽑기 천장 {0}칸을 채웁니다 (확률은 그대로)", g.count)
            : L.F("{0} {1}개를 가방으로 받습니다", L.T(DungeonDatabase.ItemName(g.itemId)), g.count);

        bool Closed => StarShopClient.CrystalDebt > 0;

        protected override void Refresh()
        {
            balanceText.text = StarShopClient.Available
                ? L.F("<color=#d8b8ff>{0}</color>", StarShopClient.CrystalText(StarShopClient.Crystals)) + (Closed ? L.F("  <color=#ff9f7a>부채 {0:N0}</color>", StarShopClient.CrystalDebt) : "")
                : L.T("<color=#8c96a8>온라인 전용</color>");
            codexText.text = L.F("장비 도감 {0}종 · 최대 체력 <color=#8fe28f>+{1:0.#}%</color>", StarShopClient.CodexCount, StarShopClient.CodexHealth);
            codexBtn.interactable = StarShopClient.Available && !busy && StarShopClient.CodexHealth < StarShopClient.CodexHealthMax;
            if (showGear) RefreshGear(); else RefreshGoods();
        }

        void RefreshGoods()
        {
            var list = StarShopClient.Goods;
            for (int i = 0; i < goods.Count; i++)
            {
                var v = goods[i];
                bool on = i < list.Count;
                v.row.gameObject.SetActive(on);
                if (!on) { v.id = null; continue; }
                var g = list[i];
                v.id = g.id;
                bool ownedOnly = g.kind == "cosmetic" && !string.IsNullOrEmpty(g.itemId) && StarShopClient.Owned.Contains(g.itemId);
                bool noChoice = g.kind == "cosmetic" && string.IsNullOrEmpty(g.itemId) && g.choices.Count == 0;
                string stock = g.left < 0 ? L.T("재고 제한 없음") : L.F("이번 주 남은 재고 {0}/{1}", g.left, g.weekly);
                v.name.text = $"<b>{L.T(g.name)}</b>";
                v.info.text = $"<color=#b8c4d8>{KindText(g)} · {stock}</color>";
                TextOf(v.buy).text = ownedOnly ? L.T("보유 중") : noChoice ? L.T("모두 보유") : g.left == 0 ? L.T("이번 주 품절") : L.F("결정 {0:N0}", g.price);
                v.buy.interactable = !busy && !Closed && StarShopClient.ExchangeLoaded && !ownedOnly && !noChoice && g.left != 0 && StarShopClient.Crystals >= g.price;
            }
            if (Closed) SetStatus(L.T("환불된 결제분에서 나온 별의 결정을 먼저 갚아야 교환소를 쓸 수 있습니다. 분해하면 먼저 갚습니다."), false);
        }

        void RefreshGear()
        {
            var list = StarShopClient.GearSpares;
            int total = 0;
            foreach (var x in list) total += x.count;
            gearHead.text = list.Count == 0
                ? L.T("<color=#8c96a8>분해할 뽑기 장비가 없습니다. 뽑기·천장으로 받은 장비는 상점에 팔 수 없고 여기서 분해합니다.</color>")
                : L.F("뽑기 장비 {0}개 · <color=#b8c4d8>상점에는 팔 수 없고 분해하면 별의 결정과 재료를 받습니다. 강화 단계마다 결정 +5%</color>", total);
            int pages = Mathf.Max(1, (list.Count + GearRows - 1) / GearRows);
            gearPage = Mathf.Clamp(gearPage, 0, pages - 1);
            SetPager(gearPager, gearPage, pages);
            dismantleAllBtn.gameObject.SetActive(list.Count > 0);
            dismantleAllBtn.interactable = !busy;
            for (int i = 0; i < gears.Count; i++)
            {
                var v = gears[i];
                int at = gearPage * GearRows + i;
                bool on = at < list.Count;
                v.row.gameObject.SetActive(on);
                if (!on) { v.key = null; continue; }
                var g = list[at];
                v.key = g.itemKey;
                v.name.text = EquipmentDatabase.RichName(g.itemKey) + L.F("   <color=#b8c4d8>x{0} · 1개당 결정 {1:N0}</color>", g.count, g.crystalsEach);
                TextOf(v.all).text = L.F("모두 분해 · 결정 {0:N0}", g.crystalsEach * g.count);
                v.all.interactable = !busy;
                v.one.gameObject.SetActive(g.count > 1);
                v.one.interactable = !busy;
            }
        }

        void AskBuy(string id)
        {
            if (busy || string.IsNullOrEmpty(id)) return;
            var g = StarShopClient.Goods.Find(x => x.id == id);
            if (g == null) return;
            if (g.kind == "cosmetic" && string.IsNullOrEmpty(g.itemId)) { OpenPick(g); return; }
            Confirm(g, g.itemId);
        }

        void Confirm(ExchangeGood g, string itemId)
        {
            string what = g.kind == "cosmetic" ? L.T(CosmeticCatalog.Find(itemId)?.Name ?? g.name) : L.T(g.name);
            Game.UI.Confirm(L.F("{0}\n별의 결정 {1:N0}개로 교환합니다.\n<size=18>별의 결정은 별조각·골드로 바꿀 수 없고, 교환한 물건은 되돌릴 수 없습니다.</size>", what, g.price), () => Buy(g, itemId), true);
        }

        void Buy(ExchangeGood g, string itemId)
        {
            if (busy) return;
            busy = true;
            Refresh();
            StarShopClient.BuyGood(g.id, itemId, (ok, msg) =>
            {
                busy = false;
                if (!ok) { SetStatus(msg, false); Game.Audio.PlaySfx("cancel"); }
                else
                {
                    Game.Audio.PlaySfx("quest");
                    string what = g.kind == "cosmetic" ? L.T(CosmeticCatalog.Find(itemId)?.Name ?? g.name) : L.T(g.name);
                    SetStatus(L.F("교환 완료 · {0}", what));
                    GameEvents.RaiseToast(L.F("<color=#d8b8ff>별빛 교환소</color> {0}", what));
                }
                Refresh();
            });
        }

        void OpenPick(ExchangeGood g)
        {
            picking = g;
            pickPageIndex = 0;
            pickPage.gameObject.SetActive(true);
            pickPage.SetAsLastSibling();
            RefreshPick();
        }

        protected override void Update()
        {
            // The choice panel takes Esc (closes itself, not the exchange), as the shop's bulk sell panel does.
            if (picking != null && pickPage != null && pickPage.gameObject.activeSelf)
            {
                if (TakesInput && (Game.Input.CancelPressed || Game.Input.InventoryPressed)) { Game.Audio.PlaySfx("cancel"); ClosePick(); }
                return;
            }
            base.Update();
        }

        void ClosePick()
        {
            picking = null;
            if (pickPage != null) pickPage.gameObject.SetActive(false);
        }

        void RefreshPick()
        {
            if (picking == null) return;
            pickTitle.text = L.F("{0} · 결정 {1:N0}", L.T(picking.name), picking.price);
            int pages = Mathf.Max(1, (picking.choices.Count + PickRows - 1) / PickRows);
            pickPageIndex = Mathf.Clamp(pickPageIndex, 0, pages - 1);
            SetPager(pickPager, pickPageIndex, pages);
            for (int i = 0; i < pickRows.Count; i++)
            {
                int at = pickPageIndex * PickRows + i;
                bool on = at < picking.choices.Count;
                pickRows[i].btn.gameObject.SetActive(on);
                if (!on) continue;
                var c = picking.choices[at];
                var p = CosmeticCatalog.Find(c.id);
                string kind = string.IsNullOrEmpty(c.skin) ? L.T("오라") : L.F("{0} 스킨", (c.skin == "mage" ? L.T("마법사") : c.skin == "rogue" ? L.T("도적") : L.T("전사")));
                pickRows[i].label.text = $"<color={Hex(c.rarity)}>{L.T(p?.Name ?? c.name)}</color>  <size=15><color=#b8c4d8>{kind}</color></size>"
                    + (p != null && p.DamagePercent > 0 ? L.F("  <size=15>공격력 +{0}%</size>", p.DamagePercent) : "");
            }
        }

        void Pick(int row)
        {
            if (picking == null) return;
            int at = pickPageIndex * PickRows + row;
            if (at >= picking.choices.Count) return;
            var g = picking;
            string id = picking.choices[at].id;
            ClosePick();
            Confirm(g, id);
        }

        void AskDismantle(string key, bool one)
        {
            if (busy || string.IsNullOrEmpty(key)) return;
            var g = StarShopClient.GearSpares.Find(x => x.itemKey == key);
            if (g == null) return;
            int n = one ? 1 : g.count;
            AskDismantleList(new List<(string, int)> { (key, n) }, g.crystalsEach * n);
        }

        void AskDismantlePage()
        {
            if (busy) return;
            var list = StarShopClient.GearSpares;
            var items = new List<(string, int)>();
            int sum = 0;
            for (int i = gearPage * GearRows; i < Mathf.Min(list.Count, (gearPage + 1) * GearRows); i++)
            {
                items.Add((list[i].itemKey, list[i].count));
                sum += list[i].crystalsEach * list[i].count;
            }
            if (items.Count > 0) AskDismantleList(items, sum);
        }

        void AskDismantleList(List<(string itemKey, int count)> items, int crystals)
        {
            int n = 0;
            foreach (var it in items) n += it.count;
            Game.UI.Confirm(L.F("뽑기 장비 {0}개를 분해합니다.\n별의 결정 {1:N0}개와 일부 강화 재료를 받습니다.\n<size=18>분해한 장비는 돌아오지 않습니다. 유니크·레전더리는 장비 도감에 남습니다.</size>", n, crystals), () =>
            {
                busy = true;
                Refresh();
                StarShopClient.DismantleGear(items, (ok, msg, got) =>
                {
                    busy = false;
                    if (!ok) { SetStatus(msg, false); Game.Audio.PlaySfx("cancel"); }
                    else
                    {
                        Game.Audio.PlaySfx("confirm");
                        SetStatus(L.F("분해 완료 · 별의 결정 +{0:N0}", got));
                    }
                    Refresh();
                });
            }, true);
        }

        void RecordCodex()
        {
            if (busy) return;
            busy = true;
            Refresh();
            StarShopClient.RegisterCodex((ok, msg, added) =>
            {
                busy = false;
                if (!ok) { SetStatus(msg, false); Game.Audio.PlaySfx("cancel"); }
                else if (added > 0) { Game.Audio.PlaySfx("quest"); SetStatus(L.F("장비 도감에 {0}종을 새로 기록했습니다.", added)); }
                else SetStatus(L.T("새로 기록할 유니크·레전더리 장비가 없습니다."));
                Refresh();
            });
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
