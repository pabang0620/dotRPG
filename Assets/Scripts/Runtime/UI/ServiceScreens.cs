using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Names, grade colours and descriptions of any item id (gear, material, consumable, resource).</summary>
    public static class ItemText
    {
        public static string Name(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return $"<color={EquipmentDatabase.RarityColor(gear.rarity)}>{gear.NameAt(Game.Session.Equipment.LevelOf(id))}</color>";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return $"<color={EquipmentDatabase.RarityColor(mat.rarity)}>{mat.name}</color>";
            return Game.Config.GetItem(id).displayName;
        }

        public static string Kind(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null)
                return $"[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}" + (gear.classOnly.HasValue ? $" · {CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용" : "");
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return $"[{EquipmentDatabase.RarityName(mat.rarity)}] 강화 재료";
            if (ConsumableDatabase.IsUsable(id)) return "소모품";
            return "재료";
        }

        public static string Description(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return $"{gear.StatLine(Game.Session.Equipment.LevelOf(id))}\n{gear.description}";
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
                case ItemIds.Wood: return "해골 숲의 나무를 베어 얻는다. 공방 재건에 쓰인다.";
                case ItemIds.Stone: return "해골 숲의 바위를 깨서 얻는다. 공방 재건에 쓰인다.";
                case ItemIds.Carrot: return "마을 밭에서 뽑은 당근. 먹으면 체력을 조금 회복한다.";
            }
            return "";
        }

        public static Sprite Icon(string id) => Game.Art.Get(Game.Config.GetItem(id).iconKey);

        /// <summary>Grade frame colour for an icon cell.</summary>
        public static Color Frame(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            var mat = gear == null ? EquipmentDatabase.GetMaterial(id) : null;
            if (gear == null && mat == null) return new Color(1f, 1f, 1f, 0.12f);
            var rarity = gear != null ? gear.rarity : mat.rarity;
            var tint = EquipmentDatabase.RarityTint(rarity);
            tint.a = rarity == ItemRarity.Common ? 0.25f : 0.9f;
            return tint;
        }

        /// <summary>Everything in the bag in bag order, money excluded.</summary>
        public static List<string> BagOrder(Inventory bag)
        {
            var ids = new List<string>();
            foreach (var item in EquipmentDatabase.All) if (bag.Count(item.id) > 0) ids.Add(item.id);
            foreach (var use in ConsumableDatabase.Usable) if (bag.Count(use.id) > 0) ids.Add(use.id);
            foreach (var def in Game.Config.items) if (bag.Count(def.id) > 0 && !ids.Contains(def.id)) ids.Add(def.id);
            foreach (var mat in EquipmentDatabase.AllMaterials) if (bag.Count(mat.id) > 0) ids.Add(mat.id);
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
        const float RowH = 68f, RowGap = 6f, ListW = 660f;

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
        Button buyOne, buyMany, pagePrev, pageNext;
        Text buyOneLabel, buyManyLabel;
        bool selling;
        int selected, page;
        bool dirty;
        readonly List<string> entries = new List<string>();

        public static ShopScreen Create(Transform canvas)
        {
            var w = CreateWindow<ShopScreen>(canvas, "Shop", "잡화점", "icon_potion_hp");
            var left = Panel(w.content, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(ListW + 40f, 594f), new Color32(24, 36, 54, 235));
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
                r.icon = UIFactory.Image(slot.transform, "Icon", null, Color.white);
                UIFactory.Place(r.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
                r.frame = Img(slot.transform, "Frame", "ui_frame", Color.clear);
                UIFactory.Stretch(r.frame.rectTransform);
                r.name = UIFactory.Text(r.bg.transform, "Name", "", 23, Color.white, TextAnchor.UpperLeft, true);
                UIFactory.Place(r.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -8f), new Vector2(380f, 30f));
                r.sub = UIFactory.Text(r.bg.transform, "Sub", "", 16, new Color32(184, 196, 216, 255), TextAnchor.UpperLeft, true);
                UIFactory.Place(r.sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -40f), new Vector2(400f, 22f));
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
            w.pagePrev = Button(left.transform, "Prev", "◀", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ListW - 150f, 12f), new Vector2(50f, 40f), () => w.Page(-1), 22);
            w.pageNext = Button(left.transform, "Next", "▶", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ListW - 10f, 12f), new Vector2(50f, 40f), () => w.Page(1), 22);
            w.pageText = Label(left.transform, "Page", "", 20, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ListW - 100f, 12f), new Vector2(90f, 40f), TextAnchor.MiddleCenter);

            var right = Panel(w.content, "Detail", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(500f, 594f), new Color32(24, 36, 54, 235));
            var goldChip = Img(right.transform, "Gold", "ui_dark", new Color(1f, 1f, 1f, 0.95f));
            UIFactory.Place(goldChip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(230f, 50f));
            var coin = UIFactory.Image(goldChip.transform, "Coin", Game.Art.Get("icon_gold"), Color.white);
            UIFactory.Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(34f, 34f));
            w.goldText = UIFactory.Text(goldChip.transform, "Amount", "", 26, new Color32(255, 216, 74, 255), TextAnchor.MiddleRight, true);
            UIFactory.Stretch(w.goldText.rectTransform, 50f, 0f, 14f, 0f);
            var iconBg = Img(right.transform, "IconBg", "ui_slotblue", Color.white);
            UIFactory.Place(iconBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -80f), new Vector2(112f, 112f));
            w.bigIcon = UIFactory.Image(iconBg.transform, "Icon", null, Color.white);
            UIFactory.Place(w.bigIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            w.bigFrame = Img(iconBg.transform, "Frame", "ui_frame", Color.clear);
            UIFactory.Stretch(w.bigFrame.rectTransform);
            w.bigName = Label(right.transform, "Name", "", 26, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(152f, -84f), new Vector2(330f, 70f));
            w.bigKind = Label(right.transform, "Kind", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(152f, -158f), new Vector2(330f, 30f));
            w.bigKind.color = new Color32(184, 196, 216, 255);
            w.bigDesc = Label(right.transform, "Desc", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -212f), new Vector2(452f, 170f));
            w.bigPrice = Label(right.transform, "PriceLine", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -392f), new Vector2(452f, 34f));
            w.result = Label(right.transform, "Result", "", 20, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(460f, 30f), TextAnchor.MiddleCenter);
            w.buyOne = Button(right.transform, "One", "1개 구매", "ui_btn", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-6f, 18f), new Vector2(214f, 62f), () => w.Trade(1), 26);
            w.buyMany = Button(right.transform, "Many", "10개 구매", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(6f, 18f), new Vector2(214f, 62f), () => w.Trade(w.selling ? int.MaxValue : 10), 26);
            w.buyOneLabel = w.buyOne.GetComponentInChildren<Text>();
            w.buyManyLabel = w.buyMany.GetComponentInChildren<Text>();
            return w;
        }

        public override void Show()
        {
            selling = false;
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

        void SetMode(bool sell)
        {
            if (selling == sell) return;
            selling = sell;
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
            if (!selling) entries.AddRange(ItemPrices.ShopStock);
            else foreach (var id in ItemText.BagOrder(bag)) if (ItemPrices.SellPrice(id) > 0) entries.Add(id);
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
                tabBg[i].color = on ? new Color32(70, 96, 130, 255) : new Color32(36, 50, 70, 255);
                tabText[i].color = on ? new Color32(255, 224, 102, 255) : new Color32(150, 170, 200, 255);
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
                r.bg.color = sel ? new Color32(64, 92, 128, 255) : new Color32(34, 48, 68, 255);
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
                bigName.text = selling ? "팔 물건이 없다." : "";
                bigKind.text = bigDesc.text = bigPrice.text = "";
                buyOne.interactable = buyMany.interactable = false;
                buyOneLabel.text = selling ? "1개 판매" : "1개 구매";
                buyManyLabel.text = selling ? "모두 판매" : "10개 구매";
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
                buyManyLabel.text = owned > 1 ? $"모두 판매 ({owned})" : "모두 판매";
                buyOne.interactable = buyMany.interactable = owned > 0;
            }
            else
            {
                int each = ItemPrices.BuyPrice(cur);
                bigPrice.text = $"가격 {ItemText.Gold(each)}   ·   보유 {owned}개";
                buyOneLabel.text = "1개 구매";
                buyManyLabel.text = "10개 구매";
                buyOne.interactable = gold >= each;
                buyMany.interactable = gold >= each;
            }
            dirty = false;
        }

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
                bag.Remove(id, n);
                bag.Add(ConsumableDatabase.Gold, each * n);
                Game.Audio.PlaySfx("pickup");
                result.text = $"<color=#8fe28f>{name} {n}개를 팔았다.  +{each * n:N0} G</color>";
            }
            else
            {
                int each = ItemPrices.BuyPrice(id);
                int n = each > 0 ? Mathf.Min(count, Game.Session.Gold / each) : 0;
                if (n <= 0)
                {
                    Game.Audio.PlaySfx("cancel");
                    result.text = "<color=#ff7070>골드가 부족하다. 해골을 쓰러뜨리거나 물건을 팔아 모으자.</color>";
                    return;
                }
                bag.Remove(ConsumableDatabase.Gold, each * n);
                bag.Add(id, n);
                Game.Audio.PlaySfx("confirm");
                result.text = n < count && count > 1
                    ? $"<color=#ffe066>골드가 모자라 {name} {n}개만 샀다.  -{each * n:N0} G</color>"
                    : $"<color=#8fe28f>{name} {n}개를 샀다.  -{each * n:N0} G</color>";
            }
            dirty = true;
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf) return;
            if (Time.frameCount != shownFrame && !Game.State.ChangedThisFrame)
            {
                var nav = Game.Input.NavigateStep;
                if (nav.y != 0 && entries.Count > 0) Select(Mathf.Clamp(selected - nav.y, 0, entries.Count - 1));
                if (nav.x != 0) SetMode(nav.x > 0);
                if (Game.Input.SubmitPressed) Trade(1);
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
        public const int Capacity = CellsPer;

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
        int cursorIndex; // 0..CellsPer-1 bag, CellsPer.. storage
        bool dirty;

        public static StorageScreen Create(Transform canvas)
        {
            var w = CreateWindow<StorageScreen>(canvas, "Storage", "창고", "icon_chest");
            float gridW = Cols * Cell + (Cols - 1) * Gap;
            var left = Panel(w.content, "Bag", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(gridW + 40f, 470f), new Color32(24, 36, 54, 235));
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

            var bottom = Panel(w.content, "Info", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1220f, 110f), new Color32(18, 28, 44, 235));
            w.info = Label(bottom.transform, "Text", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(1180f, 64f));
            w.message = Label(bottom.transform, "Message", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 8f), new Vector2(1180f, 26f));
            w.cursor = Img(w.content, "Cursor", "ui_frame", new Color32(255, 211, 74, 255));
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
            c.icon = UIFactory.Image(c.bg.transform, "Icon", null, Color.white);
            UIFactory.Place(c.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Cell * 0.7f, Cell * 0.7f));
            c.frame = Img(c.bg.transform, "Frame", "ui_frame", Color.clear);
            UIFactory.Stretch(c.frame.rectTransform);
            c.count = UIFactory.Text(c.bg.transform, "Count", "", 20, Color.white, TextAnchor.LowerRight, true);
            UIFactory.Stretch(c.count.rectTransform, 4f, 2f, 6f, 2f);
            c.level = UIFactory.Text(c.bg.transform, "Level", "", 18, new Color32(255, 224, 102, 255), TextAnchor.UpperRight, true);
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
            foreach (var c in fromBag ? bagCells : storeCells)
                if (c.id == id) { Move(c, all); Refresh(); return true; }
            return false;
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
            var bagIds = ItemText.BagOrder(bag);
            var storeIds = StorageOrder(store);
            Fill(bagCells, bagIds, bag);
            Fill(storeCells, storeIds, store);
            bagTitle.text = $"<b>가방</b>   <color=#b8c4d8>{bagIds.Count}종</color>      {ItemText.Gold(Game.Session.Gold)}";
            storeTitle.text = $"<b>창고</b>   <color=#b8c4d8>{storeIds.Count} / {Capacity}칸</color>";

            var target = hovered ?? CursorCell();
            if (target != null && target.id != null)
            {
                var from = target.inStorage ? store : bag;
                info.text = $"<b>{ItemText.Name(target.id)}</b>  <color=#b8c4d8>{ItemText.Kind(target.id)}  ·  {(target.inStorage ? "창고" : "가방")} {from.Count(target.id)}개</color>\n" +
                            $"<color=#dfe6f2>{ItemText.Description(target.id).Replace('\n', ' ')}</color>";
            }
            else info.text = "<color=#b8c4d8>클릭: 1개 옮기기   ·   우클릭: 전부 옮기기   ·   방향키로 고르고 " +
                             $"{Game.Input.GetBindingLabel(GameAction.Submit)} 키로 옮기기</color>\n<color=#8c96a8>창고에 맡긴 물건은 죽거나 다른 지역에 가도 그대로 남는다.</color>";

            var cell = CursorCell();
            var rt = cell.bg.rectTransform;
            cursor.rectTransform.position = rt.TransformPoint(rt.rect.center);
            cursor.rectTransform.sizeDelta = new Vector2(Cell + 10f, Cell + 10f);
            cursor.transform.SetAsLastSibling();
            dirty = false;
        }

        void Fill(List<Cell_> cells, List<string> ids, Inventory from)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                c.id = i < ids.Count ? ids[i] : null;
                c.icon.enabled = c.id != null;
                c.frame.color = c.id != null ? ItemText.Frame(c.id) : Color.clear;
                if (c.id == null) { c.count.text = c.level.text = ""; continue; }
                c.icon.sprite = ItemText.Icon(c.id);
                int n = from.Count(c.id);
                c.count.text = n > 1 ? n.ToString("N0") : "";
                int lv = EquipmentDatabase.Get(c.id) != null ? Game.Session.Equipment.LevelOf(c.id) : 0;
                c.level.text = lv > 0 ? $"+{lv}" : "";
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
                message.text = "<color=#ff7070>창고가 가득 찼다. 다른 물건을 먼저 꺼내자.</color>";
                dirty = true;
                return;
            }
            int n = all ? from.Count(id) : 1;
            if (n <= 0 || !from.Remove(id, n)) return;
            to.Add(id, n);
            Game.Audio.PlaySfx("select");
            string name = Game.Config.GetItem(id).displayName;
            message.text = c.inStorage ? $"<color=#8fe28f>{name} {n}개를 꺼냈다.</color>" : $"<color=#8fe28f>{name} {n}개를 맡겼다.</color>";
            dirty = true;
        }

        void DepositMaterials()
        {
            var bag = Game.Session.Inventory;
            var store = Game.Session.Storage;
            int moved = 0;
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
            }
            Game.Audio.PlaySfx(moved > 0 ? "confirm" : "cancel");
            message.text = moved > 0 ? $"<color=#8fe28f>재료 {moved}개를 창고에 맡겼다.</color>" : "<color=#b8c4d8>맡길 재료가 없다.</color>";
            dirty = true;
        }

        void WithdrawAll()
        {
            var bag = Game.Session.Inventory;
            var store = Game.Session.Storage;
            int moved = 0;
            foreach (var s in store.ToList())
            {
                if (s.count <= 0) continue;
                store.Remove(s.id, s.count);
                bag.Add(s.id, s.count);
                moved += s.count;
            }
            Game.Audio.PlaySfx(moved > 0 ? "confirm" : "cancel");
            message.text = moved > 0 ? $"<color=#8fe28f>창고의 물건 {moved}개를 모두 꺼냈다.</color>" : "<color=#b8c4d8>창고가 비어 있다.</color>";
            dirty = true;
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf) return;
            if (Time.frameCount != shownFrame && !Game.State.ChangedThisFrame)
            {
                var nav = Game.Input.NavigateStep;
                if (nav != Vector2Int.zero)
                {
                    bool store = cursorIndex >= CellsPer;
                    int local = store ? cursorIndex - CellsPer : cursorIndex;
                    int col = local % Cols + (store ? Cols : 0), row = local / Cols;
                    col = Mathf.Clamp(col + nav.x, 0, Cols * 2 - 1);
                    row = Mathf.Clamp(row - nav.y, 0, RowsN - 1);
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
