using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Names, grade colours and descriptions of any item id (gear key, material, consumable, resource).
    /// Gear keys ("eq_sword_iron+12") show their own +level.
    /// </summary>
    public static class ItemText
    {
        public static string Name(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return $"<color={EquipmentDatabase.RarityColor(gear.rarity)}>{gear.NameAt(EquipmentDatabase.LevelOfKey(id))}</color>";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return $"<color={EquipmentDatabase.RarityColor(mat.rarity)}>{mat.name}</color>";
            var use = ConsumableDatabase.Get(id);
            if (use != null && use.grade.HasValue) return $"<color={EquipmentDatabase.RarityColor(use.grade.Value)}>{use.name}</color>";
            return Game.Config.GetItem(id).displayName;
        }

        public static string Kind(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null)
                return $"[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}" + (gear.classOnly.HasValue ? $" · {CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용" : "");
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return $"[{EquipmentDatabase.RarityName(mat.rarity)}] 강화 재료";
            if (ConsumableDatabase.IsTicket(id)) return $"[{EquipmentDatabase.RarityName(Grade(id) ?? ItemRarity.Common)}] 기타 · 강화 보호";
            if (ConsumableDatabase.IsUsable(id)) return "소모품";
            return "재료";
        }

        /// <summary>Grade of gear, materials and graded items such as the protection ticket (null for plain items).</summary>
        public static ItemRarity? Grade(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return gear.rarity;
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return mat.rarity;
            return ConsumableDatabase.Get(id)?.grade;
        }

        public static string Description(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return $"{gear.StatLine(EquipmentDatabase.LevelOfKey(id))}\n{gear.description}";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return mat.description;
            var use = ConsumableDatabase.Get(id);
            if (use != null)
            {
                string key = use.hotkey.HasValue ? $"\n<color=#ffe066>빠른 사용: [{Game.Input.GetBindingLabel(use.hotkey.Value)}]</color>" : "";
                return use.description + key;
            }
            switch (id)
            {
                case ItemIds.Wood: return "해골 숲의 나무를 베어 얻습니다. 공방 재건에 쓰입니다.";
                case ItemIds.Stone: return "해골 숲의 바위를 깨서 얻습니다. 공방 재건에 쓰입니다.";
                case ItemIds.Carrot: return "마을 밭에서 뽑은 당근. 먹으면 체력을 조금 회복합니다.";
            }
            return "";
        }

        public static Sprite Icon(string id) => Game.Art.Get(Game.Config.GetItem(id).iconKey);

        /// <summary>Grade frame colour for an icon cell.</summary>
        public static Color Frame(string id)
        {
            var grade = Grade(id);
            if (!grade.HasValue) return new Color(1f, 1f, 1f, 0.12f);
            var tint = EquipmentDatabase.RarityTint(grade.Value);
            tint.a = grade.Value == ItemRarity.Common ? 0.25f : 0.9f;
            return tint;
        }

        /// <summary>
        /// Everything in the bag in bag order, money excluded: gear keys (database order, higher +level
        /// first), consumables, resources, materials, then the protection ticket.
        /// </summary>
        public static List<string> BagOrder(Inventory bag)
        {
            var ids = EquipmentDatabase.GearKeys(bag);
            foreach (var use in ConsumableDatabase.Usable) if (bag.Count(use.id) > 0) ids.Add(use.id);
            foreach (var def in Game.Config.items) if (bag.Count(def.id) > 0 && !ids.Contains(def.id)) ids.Add(def.id);
            foreach (var mat in EquipmentDatabase.AllMaterials) if (bag.Count(mat.id) > 0) ids.Add(mat.id);
            foreach (var ticket in ConsumableDatabase.Tickets) if (bag.Count(ticket.id) > 0) ids.Add(ticket.id);
            return ids;
        }

        public static string Gold(int amount) => $"<color=#ffd84a>{amount:N0} G</color>";
    }

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

        /// <summary>Buys or sells up to <paramref name="count"/> of the selected item.</summary>
        void Trade(int count)
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
                if (NeedsSellConfirm(id))
                {
                    // Enhanced gear and rare-or-higher gear are never sold on one key press: ask first, drawn over the shop.
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
                if (Game.Input.UseItemPressed && buyMany.interactable) Trade(selling ? int.MaxValue : 10);
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

    /// <summary>
    /// 창고 (item storage): two grids side by side, the bag on the left and the storage on the right.
    /// Click moves one item across, right-click moves the whole stack. Opened at the storage keeper.
    /// </summary>
    public class StorageScreen : WindowScreen
    {
        const int Cols = 6, RowsN = 4, CellsPer = Cols * RowsN;
        const float Cell = 76f, Gap = 8f;
        /// <summary>Distinct ids (each +level of a piece counts on its own) the storage holds: two pages of the grid.</summary>
        public const int Capacity = 48;

        sealed class Cell_
        {
            public Image bg, icon, frame;
            public Text count, level;
            public string id;
            public bool inStorage;
        }

        readonly List<Cell_> bagCells = new List<Cell_>();
        readonly List<Cell_> storeCells = new List<Cell_>();
        Text bagTitle, storeTitle, info, message;
        Image cursor;
        Cell_ hovered;
        int cursorIndex; // 0..CellsPer-1 bag, CellsPer.. storage (cell on the shown page)
        bool dirty;
        // Paging: each grid shows CellsPer ids of its list.
        int bagPage, storePage;
        List<string> bagIds = new List<string>(), storeIds = new List<string>();
        Button bagPrev, bagNext, storePrev, storeNext;
        Text bagPageText, storePageText;

        static int PagesFor(int count, int atLeast = 1) => Mathf.Max(atLeast, (count + CellsPer - 1) / CellsPer);
        int BagPages => PagesFor(bagIds.Count);
        int StorePages => PagesFor(storeIds.Count, PagesFor(Capacity));

        public static StorageScreen Create(Transform canvas)
        {
            var w = CreateWindow<StorageScreen>(canvas, "Storage", "창고", "menuicon_storage");
            float gridW = Cols * Cell + (Cols - 1) * Gap;
            var left = Panel(w.content, "Bag", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(gridW + 40f, 470f), UiTheme.Panel);
            var right = Panel(w.content, "Store", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(gridW + 40f, 470f), new Color32(30, 44, 40, 235));
            w.bagTitle = Label(left.transform, "Title", "", 24, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(gridW, 34f));
            w.storeTitle = Label(right.transform, "Title", "", 24, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(gridW, 34f));
            for (int i = 0; i < CellsPer; i++)
            {
                w.bagCells.Add(w.MakeCell(left.transform, i, false));
                w.storeCells.Add(w.MakeCell(right.transform, i, true));
            }
            Button(left.transform, "DepositMats", "재료 모두 맡기기", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(300f, 52f), w.DepositMaterials, 22);
            Button(right.transform, "WithdrawAll", "모두 꺼내기", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(300f, 52f), w.WithdrawAll, 22);
            // Page buttons in the bottom corners, the page number top right.
            w.bagPrev = Button(left.transform, "BagPrev", "◀", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 21f), UiSizes.PageButton, () => w.Page(false, -1), UiSizes.PageFont);
            w.bagNext = Button(left.transform, "BagNext", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 21f), UiSizes.PageButton, () => w.Page(false, 1), UiSizes.PageFont);
            w.storePrev = Button(right.transform, "StorePrev", "◀", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 21f), UiSizes.PageButton, () => w.Page(true, -1), UiSizes.PageFont);
            w.storeNext = Button(right.transform, "StoreNext", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 21f), UiSizes.PageButton, () => w.Page(true, 1), UiSizes.PageFont);
            w.bagPageText = Label(left.transform, "Page", "", 20, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -14f), new Vector2(110f, 30f), TextAnchor.MiddleRight);
            w.storePageText = Label(right.transform, "Page", "", 20, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -14f), new Vector2(110f, 30f), TextAnchor.MiddleRight);

            var bottom = Panel(w.content, "Info", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1220f, 110f), UiTheme.Panel);
            w.info = Label(bottom.transform, "Text", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(1180f, 64f));
            w.message = Label(bottom.transform, "Message", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 8f), new Vector2(800f, 26f));
            // [UX] Always-visible controls hint (the info line above turns into the item's details on hover).
            var hint = Label(bottom.transform, "Hint", "클릭: 1개 이동 · 우클릭: 전부 이동", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 8f), new Vector2(380f, 26f), TextAnchor.MiddleRight);
            hint.color = UiTheme.TextSecondary;
            w.cursor = Img(w.content, "Cursor", "ui_frame", UiTheme.Accent);
            w.cursor.raycastTarget = false;
            return w;
        }

        Cell_ MakeCell(Transform parent, int i, bool storage)
        {
            var c = new Cell_ { inStorage = storage };
            c.bg = Img(parent, (storage ? "S" : "B") + i, "ui_slot", Color.white);
            c.bg.raycastTarget = true;
            UIFactory.Place(c.bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f + (i % Cols) * (Cell + Gap), -56f - (i / Cols) * (Cell + Gap)), new Vector2(Cell, Cell));
            c.icon = UIFactory.SharpIcon(c.bg.transform, "Icon", Color.white);
            UIFactory.Place(c.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Cell * 0.7f, Cell * 0.7f));
            GearTooltip.Hook(c.icon, () => c.icon.enabled ? c.id : null);
            c.frame = Img(c.bg.transform, "Frame", "ui_frame", Color.clear);
            UIFactory.Stretch(c.frame.rectTransform);
            c.count = UIFactory.Text(c.bg.transform, "Count", "", 20, Color.white, TextAnchor.LowerRight, true);
            UIFactory.Stretch(c.count.rectTransform, 4f, 2f, 6f, 2f);
            c.level = UIFactory.Text(c.bg.transform, "Level", "", 18, UiTheme.AccentLight, TextAnchor.UpperRight, true);
            UIFactory.Stretch(c.level.rectTransform, 4f, 2f, 6f, 2f);
            var relay = c.bg.gameObject.AddComponent<PointerRelay>();
            int index = storage ? CellsPer + i : i;
            relay.onEnter = () => { hovered = c; dirty = true; };
            relay.onExit = () => { if (hovered == c) { hovered = null; dirty = true; } };
            relay.onClick = b => { cursorIndex = index; Move(c, b == PointerEventData.InputButton.Right); };
            return c;
        }

        public override void Show()
        {
            cursorIndex = 0;
            bagPage = storePage = 0;
            hovered = null;
            message.text = "";
            Game.Session.Inventory.Changed += OnChanged;
            Game.Session.Storage.Changed += OnChanged;
            base.Show();
        }

        public override void Hide()
        {
            if (Game.Session != null)
            {
                Game.Session.Inventory.Changed -= OnChanged;
                Game.Session.Storage.Changed -= OnChanged;
            }
            base.Hide();
        }

        void OnChanged(string id, int count, int delta) => dirty = true;

        // ---------- Developer automation (DevCapture) ----------

        public void DevDepositMaterials() { DepositMaterials(); Refresh(); }
        public void DevWithdrawAll() { WithdrawAll(); Refresh(); }

        /// <summary>Moves an item as if its cell were clicked (bag → storage when <paramref name="fromBag"/>).</summary>
        public bool DevMove(string id, bool fromBag, bool all)
        {
            Refresh();
            // Turn to the page holding the id first, as a player would.
            int at = (fromBag ? bagIds : storeIds).IndexOf(id);
            if (at < 0) return false;
            if (fromBag) bagPage = at / CellsPer;
            else storePage = at / CellsPer;
            Refresh();
            foreach (var c in fromBag ? bagCells : storeCells)
                if (c.id == id) { Move(c, all); Refresh(); return true; }
            return false;
        }

        public void DevPage(bool storage, int d) { Page(storage, d); Refresh(); }
        public string DevPages => $"bag {bagPage + 1}/{BagPages} storage {storePage + 1}/{StorePages}";
        /// <summary>Ids in the cells of the shown pages (bag, storage).</summary>
        public List<string> DevShown(bool storage)
        {
            Refresh();
            var list = new List<string>();
            foreach (var c in storage ? storeCells : bagCells) if (c.id != null) list.Add(c.id);
            return list;
        }

        void Page(bool storage, int d)
        {
            int pages = storage ? StorePages : BagPages;
            int page = storage ? storePage : bagPage;
            int next = Mathf.Clamp(page + d, 0, pages - 1);
            if (next == page) return;
            if (storage) storePage = next;
            else bagPage = next;
            hovered = null;
            Game.Audio.PlaySfx("select", 0.5f);
            dirty = true;
        }

        static List<string> StorageOrder(Inventory store)
        {
            var list = ItemText.BagOrder(store);
            // Anything else that was stored (ids from newer versions) still shows.
            foreach (var s in store.ToList()) if (!list.Contains(s.id) && s.id != ConsumableDatabase.Gold) list.Add(s.id);
            return list;
        }

        protected override void Refresh()
        {
            var bag = Game.Session.Inventory;
            var store = Game.Session.Storage;
            bagIds = ItemText.BagOrder(bag);
            storeIds = StorageOrder(store);
            bagPage = Mathf.Clamp(bagPage, 0, BagPages - 1);
            storePage = Mathf.Clamp(storePage, 0, StorePages - 1);
            Fill(bagCells, bagIds, bag, bagPage);
            Fill(storeCells, storeIds, store, storePage);
            bagTitle.text = $"<b>가방</b>   <color=#b8c4d8>{bagIds.Count}종</color>      {ItemText.Gold(Game.Session.Gold)}";
            storeTitle.text = $"<b>창고</b>   <color=#b8c4d8>{storeIds.Count} / {Capacity}칸</color>";
            ShowPaging(bagPrev, bagNext, bagPageText, bagPage, BagPages);
            ShowPaging(storePrev, storeNext, storePageText, storePage, StorePages);

            var target = hovered ?? CursorCell();
            if (target != null && target.id != null)
            {
                var from = target.inStorage ? store : bag;
                info.text = $"<b>{ItemText.Name(target.id)}</b>  <color=#b8c4d8>{ItemText.Kind(target.id)}  ·  {(target.inStorage ? "창고" : "가방")} {from.Count(target.id)}개</color>\n" +
                            $"<color=#dfe6f2>{ItemText.Description(target.id).Replace('\n', ' ')}</color>";
            }
            else info.text = "<color=#b8c4d8>클릭: 1개 옮기기   ·   우클릭: 전부 옮기기   ·   방향키로 고르고 " +
                             $"{Game.Input.GetBindingLabel(GameAction.Submit)} 키로 옮기기</color>\n<color=#8c96a8>창고에 맡긴 물건은 죽거나 다른 지역에 가도 그대로 남습니다.</color>";

            var cell = CursorCell();
            var rt = cell.bg.rectTransform;
            cursor.rectTransform.position = rt.TransformPoint(rt.rect.center);
            cursor.rectTransform.sizeDelta = new Vector2(Cell + 10f, Cell + 10f);
            cursor.transform.SetAsLastSibling();
            dirty = false;
        }

        static void ShowPaging(Button prev, Button next, Text label, int page, int pages)
        {
            bool paged = pages > 1;
            prev.gameObject.SetActive(paged);
            next.gameObject.SetActive(paged);
            label.gameObject.SetActive(paged);
            prev.interactable = page > 0;
            next.interactable = page < pages - 1;
            label.text = $"{page + 1} / {pages}";
        }

        void Fill(List<Cell_> cells, List<string> ids, Inventory from, int page)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                int at = page * CellsPer + i;
                c.id = at < ids.Count ? ids[at] : null;
                c.icon.enabled = c.id != null;
                c.frame.color = c.id != null ? ItemText.Frame(c.id) : Color.clear;
                if (c.id == null) { c.count.text = c.level.text = ""; continue; }
                c.icon.sprite = ItemText.Icon(c.id);
                int n = from.Count(c.id);
                c.count.text = n > 1 ? n.ToString("N0") : "";
                int lv = EquipmentDatabase.LevelOfKey(c.id);
                c.level.text = lv > 0 ? $"+{lv}" : "";
                c.level.color = EquipmentDatabase.LevelTint(lv);
            }
        }

        Cell_ CursorCell() => cursorIndex < CellsPer ? bagCells[cursorIndex] : storeCells[cursorIndex - CellsPer];

        /// <summary>Moves one (or all) of a cell's item to the other side.</summary>
        void Move(Cell_ c, bool all)
        {
            if (c == null || c.id == null) { Game.Audio.PlaySfx("cancel"); return; }
            var from = c.inStorage ? Game.Session.Storage : Game.Session.Inventory;
            var to = c.inStorage ? Game.Session.Inventory : Game.Session.Storage;
            string id = c.id;
            if (!c.inStorage && to.Count(id) == 0 && StorageOrder(to).Count >= Capacity)
            {
                Game.Audio.PlaySfx("cancel");
                message.text = "<color=#ff7070>창고가 가득 찼습니다. 다른 물건을 먼저 꺼내세요.</color>";
                dirty = true;
                return;
            }
            int n = all ? from.Count(id) : 1;
            if (n <= 0 || !from.Remove(id, n)) return;
            to.Add(id, n);
            // [SERVER] Same move on the server; its delta (absolute counts) corrects anything it refused.
            if (OnlineEconomy.On) OnlineEconomy.StorageMove(new[] { (id, !c.inStorage, n) }, _ => dirty = true);
            Game.Audio.PlaySfx("select");
            string name = Game.Config.GetItem(id).displayName;
            message.text = c.inStorage ? $"<color=#8fe28f>{name} {n}개를 꺼냈습니다.</color>" : $"<color=#8fe28f>{name} {n}개를 맡겼습니다.</color>";
            dirty = true;
        }

        void DepositMaterials()
        {
            var bag = Game.Session.Inventory;
            var store = Game.Session.Storage;
            int moved = 0;
            var serverMoves = new List<(string, bool, int)>();
            var ids = new List<string>();
            foreach (var m in EquipmentDatabase.AllMaterials) ids.Add(m.id);
            ids.Add(ItemIds.Wood);
            ids.Add(ItemIds.Stone);
            foreach (var id in ids)
            {
                int n = bag.Count(id);
                if (n <= 0) continue;
                if (store.Count(id) == 0 && StorageOrder(store).Count >= Capacity) continue;
                bag.Remove(id, n);
                store.Add(id, n);
                moved += n;
                serverMoves.Add((id, true, n));
            }
            if (OnlineEconomy.On) OnlineEconomy.StorageMove(serverMoves, _ => dirty = true); // [SERVER]
            Game.Audio.PlaySfx(moved > 0 ? "confirm" : "cancel");
            message.text = moved > 0 ? $"<color=#8fe28f>재료 {moved}개를 창고에 맡겼습니다.</color>" : "<color=#b8c4d8>맡길 재료가 없습니다.</color>";
            dirty = true;
        }

        void WithdrawAll()
        {
            var bag = Game.Session.Inventory;
            var store = Game.Session.Storage;
            int moved = 0;
            var serverMoves = new List<(string, bool, int)>();
            foreach (var s in store.ToList())
            {
                if (s.count <= 0) continue;
                store.Remove(s.id, s.count);
                bag.Add(s.id, s.count);
                moved += s.count;
                serverMoves.Add((s.id, false, s.count));
            }
            if (OnlineEconomy.On) OnlineEconomy.StorageMove(serverMoves, _ => dirty = true); // [SERVER]
            Game.Audio.PlaySfx(moved > 0 ? "confirm" : "cancel");
            message.text = moved > 0 ? $"<color=#8fe28f>창고의 물건 {moved}개를 모두 꺼냈습니다.</color>" : "<color=#b8c4d8>창고가 비어 있습니다.</color>";
            dirty = true;
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf) return;
            if (TakesInput)
            {
                var nav = Game.Input.NavigateStep;
                if (nav != Vector2Int.zero)
                {
                    bool store = cursorIndex >= CellsPer;
                    int local = store ? cursorIndex - CellsPer : cursorIndex;
                    int col = local % Cols + (store ? Cols : 0), row = local / Cols;
                    col = Mathf.Clamp(col + nav.x, 0, Cols * 2 - 1);
                    row -= nav.y;
                    // Moving off the top / bottom row turns that grid's page (as in the blacksmith).
                    bool inStore = col >= Cols;
                    int page = inStore ? storePage : bagPage, pages = inStore ? StorePages : BagPages;
                    if (row < 0 && page > 0) { Page(inStore, -1); row = RowsN - 1; }
                    else if (row >= RowsN && page < pages - 1) { Page(inStore, 1); row = 0; }
                    row = Mathf.Clamp(row, 0, RowsN - 1);
                    cursorIndex = col >= Cols ? CellsPer + row * Cols + (col - Cols) : row * Cols + col;
                    hovered = null;
                    Game.Audio.PlaySfx("select", 0.5f);
                    dirty = true;
                }
                if (Game.Input.SubmitPressed) Move(CursorCell(), false);
            }
            if (dirty) Refresh();
        }
    }
}
