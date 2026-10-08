using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{

    // =====================================================================================

    /// <summary>
    /// 잡화점 (general store): buy potions, the town return scroll and enhancement materials with gold,
    /// sell loot the character does not need. Opened by talking to the village merchant.
    /// </summary>
    public class ShopScreen : WindowScreen
    {
        const int RowsPerPage = 7;
        // [UI] 7 rows of 60 end at -530, above the page buttons (rows of 68 ran under them).
        const float RowH = 60f, RowGap = 6f, ListW = 660f;

        sealed class Row
        {
            public Image bg, icon, frame;
            public Text name, sub, price;
            public string id;
        }

        readonly List<Row> rows = new List<Row>();
        readonly Image[] tabBg = new Image[2];
        readonly Text[] tabText = new Text[2];
        Text goldText, pageText, bigName, bigKind, bigDesc, bigPrice, result;
        Image bigIcon, bigFrame;
        Button buyOne, buyMany, buyMax, pagePrev, pageNext;
        Text buyOneLabel, buyManyLabel;
        bool selling;
        int selected, page;
        bool dirty;
        readonly List<string> entries = new List<string>();
        BulkSellPanel bulk;
        Button bulkBtn;

        public static ShopScreen Create(Transform canvas)
        {
            var w = CreateWindow<ShopScreen>(canvas, "Shop", "잡화점", "menuicon_shop");
            var left = Panel(w.content, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(ListW + 40f, 594f), UiTheme.Panel);
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var tb = Img(left.transform, "ShopTab" + i, "ui_white", Color.clear);
                tb.raycastTarget = true;
                UIFactory.Place(tb.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f + i * 170f, -12f), new Vector2(160f, 50f));
                tb.gameObject.AddComponent<PointerRelay>().onClick = _ => w.SetMode(index == 1);
                var tt = UIFactory.Text(tb.transform, "Text", i == 0 ? "구매" : "판매", 26, Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(tt.rectTransform);
                w.tabBg[i] = tb;
                w.tabText[i] = tt;
            }
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = i;
                var r = new Row();
                r.bg = Img(left.transform, "Row" + i, "ui_white", Color.white);
                r.bg.raycastTarget = true;
                UIFactory.Place(r.bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -74f - i * (RowH + RowGap)), new Vector2(ListW, RowH));
                var slot = Img(r.bg.transform, "Slot", "ui_slot", Color.white);
                UIFactory.Place(slot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(58f, 58f));
                r.icon = UIFactory.SharpIcon(slot.transform, "Icon", Color.white);
                UIFactory.Place(r.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
                GearTooltip.Hook(r.icon, () => r.id);
                r.frame = Img(slot.transform, "Frame", "ui_frame", Color.clear);
                UIFactory.Stretch(r.frame.rectTransform);
                r.name = UIFactory.Text(r.bg.transform, "Name", "", 23, Color.white, TextAnchor.UpperLeft, true);
                UIFactory.Place(r.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -4f), new Vector2(380f, 30f));
                r.sub = UIFactory.Text(r.bg.transform, "Sub", "", 16, UiTheme.TextSecondary, TextAnchor.UpperLeft, true);
                UIFactory.Place(r.sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -34f), new Vector2(400f, 22f));
                r.price = UIFactory.Text(r.bg.transform, "Price", "", 24, Color.white, TextAnchor.MiddleRight, true);
                UIFactory.Place(r.price.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(180f, 40f));
                var relay = r.bg.gameObject.AddComponent<PointerRelay>();
                relay.onClick = b =>
                {
                    if (w.selected == w.page * RowsPerPage + index && b == PointerEventData.InputButton.Left) { w.Trade(1); return; }
                    w.Select(w.page * RowsPerPage + index);
                };
                w.rows.Add(r);
            }
            w.pagePrev = Button(left.transform, "Prev", "◀", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ListW - 170f, 15f), UiSizes.PageButton, () => w.Page(-1), UiSizes.PageFont);
            w.pageNext = Button(left.transform, "Next", "▶", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ListW - 30f, 15f), UiSizes.PageButton, () => w.Page(1), UiSizes.PageFont);
            w.pageText = Label(left.transform, "Page", "", 20, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ListW - 120f, 12f), new Vector2(90f, 40f), TextAnchor.MiddleCenter);

            var right = Panel(w.content, "Detail", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(500f, 594f), UiTheme.Panel);
            var goldChip = Img(right.transform, "Gold", "ui_dark", new Color(1f, 1f, 1f, 0.95f));
            UIFactory.Place(goldChip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(230f, 50f));
            var coin = UIFactory.Image(goldChip.transform, "Coin", Game.Art.Get("icon_gold"), Color.white);
            UIFactory.Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(34f, 34f));
            w.goldText = UIFactory.Text(goldChip.transform, "Amount", "", 26, UiTheme.Accent, TextAnchor.MiddleRight, true);
            UIFactory.Stretch(w.goldText.rectTransform, 50f, 0f, 14f, 0f);
            var iconBg = Img(right.transform, "IconBg", "ui_slotblue", Color.white);
            UIFactory.Place(iconBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -80f), new Vector2(112f, 112f));
            w.bigIcon = UIFactory.SharpIcon(iconBg.transform, "Icon", Color.white);
            UIFactory.Place(w.bigIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            w.bigFrame = Img(iconBg.transform, "Frame", "ui_frame", Color.clear);
            UIFactory.Stretch(w.bigFrame.rectTransform);
            w.bigName = Label(right.transform, "Name", "", 26, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(152f, -84f), new Vector2(330f, 70f));
            w.bigKind = Label(right.transform, "Kind", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(152f, -158f), new Vector2(330f, 30f));
            w.bigKind.color = UiTheme.TextSecondary;
            w.bigDesc = Label(right.transform, "Desc", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -212f), new Vector2(452f, 170f));
            w.bigPrice = Label(right.transform, "PriceLine", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -392f), new Vector2(452f, 34f));
            w.result = Label(right.transform, "Result", "", 20, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(460f, 30f), TextAnchor.MiddleCenter);
            w.buyOne = Button(right.transform, "One", "1개 구매", "ui_btn", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-6f, 18f), new Vector2(214f, 62f), () => w.Trade(1), 26);
            w.buyMany = Button(right.transform, "Many", "10개 구매", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(6f, 18f), new Vector2(214f, 62f), () => w.Trade(w.selling ? int.MaxValue : 10), 26);
            // [UX] 최대: as many as the gold buys (buy tab only; the sell tab keeps its two buttons).
            w.buyMax = Button(right.transform, "Max", "최대", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(84f, 18f), new Vector2(140f, 62f), () => w.Trade(MaxBuyCount), 26);
            w.LayoutTradeButtons(false);
            w.buyOneLabel = w.buyOne.GetComponentInChildren<Text>();
            w.buyManyLabel = w.buyMany.GetComponentInChildren<Text>();
            // 일괄 판매 (sell tab): kinds ticked once are remembered.
            w.bulkBtn = Button(left.transform, "Bulk", "일괄 판매", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(360f, -12f), new Vector2(170f, 50f), () => w.bulk.Open(), 22);
            w.bulk = BulkSellPanel.Create(w.content, () => w.dirty = true);
            w.bulkBtn.gameObject.SetActive(false);
            return w;
        }

        public override void Show()
        {
            selling = false;
            LayoutTradeButtons(false);
            selected = 0;
            page = 0;
            result.text = "";
            Game.Session.Inventory.Changed += OnBagChanged;
            base.Show();
        }

        public override void Hide()
        {
            if (Game.Session != null) Game.Session.Inventory.Changed -= OnBagChanged;
            base.Hide();
        }

        void OnBagChanged(string id, int count, int delta) => dirty = true;

        // ---------- Developer automation (DevCapture) ----------

        public void DevMode(bool sell) { SetMode(sell); Refresh(); }
        public bool DevSelect(string id) { BuildEntries(); int i = entries.IndexOf(id); if (i < 0) return false; selected = i; Refresh(); return true; }
        public void DevTrade(int count) { Trade(count); Refresh(); }
        public string DevResult => result.text;
        /// <summary>The current list (buy stock or sell list) in display order.</summary>
        public List<string> DevEntries() { BuildEntries(); return new List<string>(entries); }

        /// <summary>Most of one item bought at once (the server's shop limit per purchase).</summary>
        const int MaxBuyCount = 999;

        /// <summary>[UX] Small "[key]" after a button label: the key that presses it (10개 / 모두 판매: 1, 최대 / 일괄 판매: 2).</summary>
        static string KeyTag(GameAction action) => $" <size=16><color=#b8c4d8>[{Game.Input.GetBindingLabel(action)}]</color></size>";

        /// <summary>Buy tab: 1 · 10 · 최대 side by side. Sell tab: 1 · 모두 (the original two wide buttons).</summary>
        void LayoutTradeButtons(bool sell)
        {
            var one = (RectTransform)buyOne.transform;
            var many = (RectTransform)buyMany.transform;
            buyMax.gameObject.SetActive(!sell);
            if (sell)
            {
                one.anchoredPosition = new Vector2(-6f, 18f); one.sizeDelta = new Vector2(214f, 62f);
                many.pivot = new Vector2(0f, 0f); many.anchoredPosition = new Vector2(6f, 18f); many.sizeDelta = new Vector2(214f, 62f);
            }
            else
            {
                one.anchoredPosition = new Vector2(-84f, 18f); one.sizeDelta = new Vector2(140f, 62f);
                many.pivot = new Vector2(0.5f, 0f); many.anchoredPosition = new Vector2(0f, 18f); many.sizeDelta = new Vector2(156f, 62f);
            }
        }

        void SetMode(bool sell)
        {
            if (selling == sell) return;
            selling = sell;
            bulkBtn.gameObject.SetActive(sell);
            if (!sell) bulk.gameObject.SetActive(false);
            LayoutTradeButtons(sell);
            selected = 0;
            page = 0;
            result.text = "";
            Game.Audio.PlaySfx("select");
            dirty = true;
        }

        void Select(int index)
        {
            if (index < 0 || index >= entries.Count) return;
            selected = index;
            Game.Audio.PlaySfx("select", 0.5f);
            dirty = true;
        }

        void Page(int d)
        {
            int pages = Mathf.Max(1, (entries.Count + RowsPerPage - 1) / RowsPerPage);
            page = Mathf.Clamp(page + d, 0, pages - 1);
            selected = Mathf.Clamp(page * RowsPerPage, 0, Mathf.Max(0, entries.Count - 1));
            dirty = true;
        }

        void BuildEntries()
        {
            entries.Clear();
            var bag = Game.Session.Inventory;
            if (!selling)
            {
                // Goods, then common gear of my class up to my level (the server checks the level too).
                var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
                int lv = Game.Session.Progression.Level;
                foreach (var id in ItemPrices.ShopStock)
                {
                    var g = EquipmentDatabase.Get(id);
                    if (g == null || (g.UsableBy(cls) && g.reqLevel <= lv)) entries.Add(id);
                }
                return;
            }
            // Everything at +0 first; enhanced gear (worth real effort) goes to the end of the list.
            var order = ItemText.BagOrder(bag);
            foreach (var id in order) if (ItemPrices.SellPrice(id) > 0 && EquipmentDatabase.LevelOfKey(id) == 0) entries.Add(id);
            foreach (var id in order) if (ItemPrices.SellPrice(id) > 0 && EquipmentDatabase.LevelOfKey(id) > 0) entries.Add(id);
        }

        protected override void Refresh()
        {
            BuildEntries();
            var bag = Game.Session.Inventory;
            int gold = Game.Session.Gold;
            goldText.text = $"{gold:N0} G";
            for (int i = 0; i < 2; i++)
            {
                bool on = (i == 1) == selling;
                tabBg[i].color = on ? UiTheme.Line : UiTheme.Background;
                tabText[i].color = on ? UiTheme.AccentLight : new Color32(150, 170, 200, 255);
            }
            int pages = Mathf.Max(1, (entries.Count + RowsPerPage - 1) / RowsPerPage);
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, entries.Count - 1));
            page = Mathf.Clamp(selected / RowsPerPage, 0, pages - 1);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int index = page * RowsPerPage + i;
                r.id = index < entries.Count ? entries[index] : null;
                r.bg.gameObject.SetActive(r.id != null);
                if (r.id == null) continue;
                bool sel = index == selected;
                r.bg.color = sel ? UiTheme.Line : UiTheme.Background;
                r.icon.sprite = ItemText.Icon(r.id);
                r.frame.color = ItemText.Frame(r.id);
                r.name.text = ItemText.Name(r.id);
                int have = bag.Count(r.id);
                if (selling)
                {
                    r.sub.text = $"{ItemText.Kind(r.id)}  ·  보유 {have}개";
                    r.price.text = ItemText.Gold(ItemPrices.SellPrice(r.id));
                }
                else
                {
                    r.sub.text = $"{ItemText.Kind(r.id)}  ·  보유 {have}개";
                    int price = ItemPrices.BuyPrice(r.id);
                    r.price.text = price <= gold ? ItemText.Gold(price) : $"<color=#ff7070>{price:N0} G</color>";
                }
            }
            pageText.text = $"{page + 1} / {pages}";
            pageText.gameObject.SetActive(pages > 1);
            pagePrev.gameObject.SetActive(pages > 1);
            pageNext.gameObject.SetActive(pages > 1);

            string cur = selected < entries.Count ? entries[selected] : null;
            bigIcon.enabled = cur != null;
            if (cur == null)
            {
                bigFrame.color = Color.clear;
                bigName.text = selling ? "팔 물건이 없습니다." : "";
                bigKind.text = bigDesc.text = bigPrice.text = "";
                buyOne.interactable = buyMany.interactable = buyMax.interactable = false;
                buyOneLabel.text = selling ? "1개 판매" : "1개 구매";
                buyManyLabel.text = (selling ? "모두 판매" : "10개 구매") + KeyTag(GameAction.UseItem);
                RefreshKeyTags();
                dirty = false;
                return;
            }
            bigIcon.sprite = ItemText.Icon(cur);
            bigFrame.color = ItemText.Frame(cur);
            bigName.text = $"<b>{ItemText.Name(cur)}</b>";
            bigKind.text = ItemText.Kind(cur);
            bigDesc.text = ItemText.Description(cur);
            int owned = bag.Count(cur);
            if (selling)
            {
                int each = ItemPrices.SellPrice(cur);
                bigPrice.text = $"판매가 {ItemText.Gold(each)}   ·   보유 {owned}개";
                buyOneLabel.text = "1개 판매";
                buyManyLabel.text = (owned > 1 ? $"모두 판매 ({owned})" : "모두 판매") + KeyTag(GameAction.UseItem);
                buyOne.interactable = buyMany.interactable = owned > 0;
            }
            else
            {
                int each = ItemPrices.BuyPrice(cur);
                bigPrice.text = $"가격 {ItemText.Gold(each)}   ·   보유 {owned}개";
                buyOneLabel.text = "1개 구매";
                buyManyLabel.text = "10개 구매" + KeyTag(GameAction.UseItem);
                buyOne.interactable = gold >= each;
                buyMany.interactable = gold >= each;
                buyMax.interactable = gold >= each;
            }
            RefreshKeyTags();
            dirty = false;
        }

        /// <summary>Key labels follow the device in use (keyboard 2 / gamepad L3).</summary>
        void RefreshKeyTags()
        {
            TextOf(buyMax).text = "최대" + KeyTag(GameAction.UseMana);
            TextOf(bulkBtn).text = "일괄 판매" + KeyTag(GameAction.UseMana);
        }

        static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>
        /// Buys or sells up to <paramref name="count"/> of the selected item. <paramref name="fromKey"/>: pressed with the
        /// 1 / Y key (also the HP potion key), so a sale always asks first.
        /// </summary>
        void Trade(int count, bool fromKey = false)
        {
            if (selected >= entries.Count) return;
            string id = entries[selected];
            var bag = Game.Session.Inventory;
            string name = Game.Config.GetItem(id).displayName;
            if (selling)
            {
                int n = Mathf.Min(count, bag.Count(id));
                int each = ItemPrices.SellPrice(id);
                if (n <= 0 || each <= 0) { Game.Audio.PlaySfx("cancel"); return; }
                if (NeedsSellConfirm(id) || fromKey)
                {
                    // Enhanced gear, rare-or-higher gear and any sale by the 1 / Y key are never sold on one press: ask first, drawn over the shop.
                    string what = n > 1 ? $"{name} {n}개를" : $"{name} 을(를)";
                    Game.Audio.PlaySfx("select");
                    Game.UI.Confirm($"{what} {each * n:N0} G에 판매할까요?", () => Sell(id, n), true);
                    return;
                }
                Sell(id, n);
            }
            else
            {
                int each = ItemPrices.BuyPrice(id);
                int n = each > 0 ? Mathf.Min(count, Game.Session.Gold / each) : 0;
                if (n <= 0)
                {
                    Game.Audio.PlaySfx("cancel");
                    result.text = "<color=#ff7070>골드가 부족합니다. 해골을 쓰러뜨리거나 물건을 팔아 모으세요.</color>";
                    return;
                }
                if (count == MaxBuyCount && n > 10)
                {
                    // [UX] 최대 can spend most of the gold at once: ask first (purchases default to "아니오").
                    int many = n;
                    Game.Audio.PlaySfx("select");
                    Game.UI.Confirm($"{name} {many}개를 {each * many:N0} G에 살까요?", () => Buy(id, many, many), true);
                    return;
                }
                Buy(id, n, count == MaxBuyCount ? n : count);
            }
            dirty = true;
        }

        /// <summary>Rare-or-higher gear and enhanced gear (+1 and up) are sold only after a yes.</summary>
        static bool NeedsSellConfirm(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            return gear != null && (EquipmentDatabase.LevelOfKey(id) > 0 || gear.rarity >= ItemRarity.Rare);
        }

        /// <summary>Buys <paramref name="n"/> of an id (<paramref name="count"/> = how many were asked for, for the message).</summary>
        void Buy(string id, int n, int count)
        {
            var bag = Game.Session.Inventory;
            string name = Game.Config.GetItem(id).displayName;
            int each = ItemPrices.BuyPrice(id);
            n = each > 0 ? Mathf.Min(n, Game.Session.Gold / each) : 0; // re-checked: gold may have changed while a confirm was open
            if (n <= 0) { Game.Audio.PlaySfx("cancel"); dirty = true; return; }
            if (OnlineEconomy.On)
            {
                // [SERVER] The server charges and delivers; its delta updates gold and the bag.
                int bought = n;
                OnlineEconomy.ShopBuy(id, bought, ok =>
                {
                    Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
                    if (ok) result.text = $"<color=#8fe28f>{name} {bought}개를 샀습니다.  -{each * bought:N0} G</color>";
                    dirty = true;
                });
                return;
            }
            bag.Remove(ConsumableDatabase.Gold, each * n);
            bag.Add(id, n);
            Game.Audio.PlaySfx("confirm");
            result.text = n < count && count > 1
                ? $"<color=#ffe066>골드가 모자라 {name} {n}개만 샀습니다.  -{each * n:N0} G</color>"
                : $"<color=#8fe28f>{name} {n}개를 샀습니다.  -{each * n:N0} G</color>";
            dirty = true;
        }

        /// <summary>Sells up to <paramref name="count"/> of an id (re-checked: the bag may have changed while a confirm was open).</summary>
        void Sell(string id, int count)
        {
            var bag = Game.Session.Inventory;
            int n = Mathf.Min(count, bag.Count(id));
            int each = ItemPrices.SellPrice(id);
            if (n <= 0 || each <= 0) { Game.Audio.PlaySfx("cancel"); return; }
            if (OnlineEconomy.On)
            {
                // [SERVER] The server prices and pays the sale.
                OnlineEconomy.ShopSell(id, n, ok =>
                {
                    Game.Audio.PlaySfx(ok ? "pickup" : "cancel");
                    if (ok) result.text = $"<color=#8fe28f>{Game.Config.GetItem(id).displayName} {n}개를 팔았습니다.  +{each * n:N0} G</color>";
                    dirty = true;
                });
                return;
            }
            if (!bag.Remove(id, n)) { Game.Audio.PlaySfx("cancel"); return; }
            bag.Add(ConsumableDatabase.Gold, each * n);
            Game.Audio.PlaySfx("pickup");
            result.text = $"<color=#8fe28f>{Game.Config.GetItem(id).displayName} {n}개를 팔았습니다.  +{each * n:N0} G</color>";
            dirty = true;
        }

        protected override bool HasKeyTags => true;

        protected override void Update()
        {
            // [UX] The bulk sell panel takes Esc (closes itself, not the shop) and blocks the list keys under it (as the sweep panel).
            if (bulk != null && bulk.gameObject.activeSelf)
            {
                if (TakesInput && (Game.Input.CancelPressed || Game.Input.InventoryPressed)) { Game.Audio.PlaySfx("cancel"); bulk.gameObject.SetActive(false); }
                if (dirty) Refresh();
                return;
            }
            base.Update();
            if (!gameObject.activeSelf) return;
            if (TakesInput)
            {
                var nav = Game.Input.NavigateStep;
                if (nav.y != 0 && entries.Count > 0) Select(Mathf.Clamp(selected - nav.y, 0, entries.Count - 1));
                if (nav.x != 0) SetMode(nav.x > 0);
                if (Game.Input.SubmitPressed) Trade(1);
                // [UX] The mouse-only buttons get keys too: 1 / Y = 10개 구매 or 모두 판매, 2 / L3 = 최대 or 일괄 판매.
                if (Game.Input.UseItemPressed && buyMany.interactable) Trade(selling ? int.MaxValue : 10, true);
                else if (Game.Input.UseManaPressed)
                {
                    if (selling) { Game.Audio.PlaySfx("select"); bulk.Open(); }
                    else if (buyMax.interactable) Trade(MaxBuyCount);
                }
            }
            if (dirty) Refresh();
        }
    }

    // =====================================================================================

}
