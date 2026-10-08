using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
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
