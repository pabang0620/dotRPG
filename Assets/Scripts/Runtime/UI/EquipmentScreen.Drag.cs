using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UX] Bag drag and drop + right-click menu: bag gear dragged onto a worn slot is equipped there
    /// (반지 1 / 반지 2 pick the ring slot), worn gear dragged onto the bag is taken off, and a right click
    /// on bag gear opens a small menu. Everything goes through the same paths as a click
    /// (<see cref="EquipGear"/>, <see cref="Use"/>), so the rules and server sync stay the same.
    /// </summary>
    public partial class EquipmentScreen
    {
        /// <summary>Drag payload: a bag gear key (captured when the drag starts).</summary>
        sealed class BagGearDrag
        {
            public readonly string key;
            public BagGearDrag(string key) { this.key = key; }
        }

        /// <summary>Drag payload: the piece worn in a slot.</summary>
        sealed class WornGearDrag
        {
            public readonly EquipSlot slot;
            public WornGearDrag(EquipSlot slot) { this.slot = slot; }
        }

        RectTransform menu;
        Image menuBlocker;
        string menuKey;

        bool MenuOpen => menu != null && menu.gameObject.activeSelf;

        /// <summary>Called once after <see cref="Build"/>: drag components, the bag drop area, the menu and its hint.</summary>
        void BuildDragAndMenu(RectTransform root)
        {
            // Bag gear: draggable, right click opens the menu (other buttons and other items keep the click).
            foreach (var s in grid)
            {
                var cell = s;
                var source = cell.bg.gameObject.AddComponent<DragSource>();
                source.payload = () => EquipmentDatabase.Get(cell.itemId) != null ? new BagGearDrag(cell.itemId) : null;
                source.ghostSprite = () => cell.icon.sprite;
                var relay = cell.bg.GetComponent<PointerRelay>();
                relay.onClick = b =>
                {
                    if ((b == PointerEventData.InputButton.Right || TouchUi.Enabled) && EquipmentDatabase.Get(cell.itemId) != null) OpenMenu(cell); // touch: a tap opens the menu (장착 / 닫기) instead of equipping at once
                    else Use(cell);
                };
            }

            // Worn slots: drag the piece out, drop bag gear in.
            foreach (var pair in wornSlots)
            {
                var slot = pair.Key;
                var worn = pair.Value;
                var source = worn.bg.gameObject.AddComponent<DragSource>();
                source.payload = () => worn.itemId != null ? new WornGearDrag(slot) : null;
                source.ghostSprite = () => worn.icon.sprite;
                var target = worn.bg.gameObject.AddComponent<DropTarget>();
                target.highlight = worn.bg;
                target.accepts = obj => obj is BagGearDrag d && CanDropOn(d.key, slot);
                target.onDrop = obj => EquipGear(((BagGearDrag)obj).key, slot);
            }

            // Bag area: a clear panel behind the grid (gaps included) plus every cell; all light the panel.
            float gridW = Columns * Cell + (Columns - 1) * Gap;
            float gridH = Rows * Cell + (Rows - 1) * Gap;
            var bagArea = Tinted(root, "BagDrop", "ui_white", Color.clear);
            bagArea.raycastTarget = true;
            UIFactory.Place(bagArea.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-24f - gridW - 8f, -152f), new Vector2(gridW + 16f, gridH + 16f));
            bagArea.transform.SetSiblingIndex(grid[0].rect.GetSiblingIndex());
            AddBagDrop(bagArea.gameObject, bagArea);
            foreach (var s in grid) AddBagDrop(s.bg.gameObject, bagArea);

            // Context menu (mouse only): a clear blocker closes it on any outside click.
            menuBlocker = UIFactory.Overlay(root, "MenuBlocker", Color.clear);
            menuBlocker.raycastTarget = true;
            menuBlocker.gameObject.AddComponent<PointerRelay>().onClick = _ => CloseMenu();
            menuBlocker.gameObject.SetActive(false);
            menu = Tinted(root, "Menu", "ui_dark", Color.white).rectTransform;
            menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 0.5f);
            menu.pivot = new Vector2(1f, 1f);
            menu.sizeDelta = new Vector2(170f, 118f);
            MakeButton(menu, "Equip", "장착", "ui_btn", new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(150f, 46f), MenuEquip, 22);
            MakeButton(menu, "Cancel", "닫기", "ui_btngray", new Vector2(0f, 1f), new Vector2(10f, -62f), new Vector2(150f, 46f), CloseMenu, 22);
            menu.gameObject.SetActive(false);

            // Hint above the stat box (empty space under the character).
            var dragHint = UIFactory.Text(root, "DragHint", TouchUi.Enabled ? "드래그해서 장착 · 터치: 메뉴" : "드래그해서 장착 · 우클릭: 메뉴", UiTheme.FontCaption, new Color(1f, 1f, 1f, 0.82f), TextAnchor.MiddleCenter, true);
            UIFactory.Place(dragHint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120f, 120f), new Vector2(460f, 24f));
        }

        void AddBagDrop(GameObject go, Graphic highlight)
        {
            var target = go.AddComponent<DropTarget>();
            target.highlight = highlight;
            target.hoverColor = new Color(1f, 0.85f, 0.35f, 0.22f);
            target.accepts = obj => obj is WornGearDrag d && !string.IsNullOrEmpty(Game.Session.Equipment[d.slot]);
            target.onDrop = obj => Use(wornSlots[((WornGearDrag)obj).slot]);
        }

        /// <summary>Same checks as <see cref="Equipment.Equip"/> (wearable, in the bag) plus the slot fitting the piece.</summary>
        bool CanDropOn(string key, EquipSlot slot)
        {
            var gear = EquipmentDatabase.Get(key);
            return gear != null
                && Game.Session.Equipment.CanWear(gear, Class)
                && EquipmentDatabase.Fits(gear.category, slot)
                && Game.Session.Inventory.Count(key) > 0;
        }

        /// <summary>
        /// A ring into one chosen ring slot. <see cref="Equipment.Equip"/> picks the ring slot itself, so the
        /// chosen slot is emptied first; if the ring still lands in the other (empty) slot, the two are swapped.
        /// </summary>
        bool EquipRingInto(string key, EquipSlot target)
        {
            var eq = Game.Session.Equipment;
            if (!CanDropOn(key, target)) return false;
            if (!string.IsNullOrEmpty(eq[target])) eq.Unequip(target);
            if (!eq.Equip(key, Class)) return false;
            var other = target == EquipSlot.Ring1 ? EquipSlot.Ring2 : EquipSlot.Ring1;
            if (string.IsNullOrEmpty(eq[target]) && eq[other] == key)
            {
                eq.Set(target, key);
                eq.Set(other, null);
            }
            return true;
        }

        void OpenMenu(Slot s)
        {
            menuKey = s.itemId;
            hovered = null;
            Game.Audio.PlaySfx("select", 0.5f);
            menuBlocker.gameObject.SetActive(true);
            menuBlocker.transform.SetAsLastSibling();
            menu.gameObject.SetActive(true);
            menu.SetAsLastSibling();
            // Top-right corner of the menu at the cell's top-left corner (the grid sits at the right edge).
            menu.position = s.rect.TransformPoint(new Vector3(s.rect.rect.xMin, s.rect.rect.yMax, 0f));
        }

        void CloseMenu()
        {
            menuKey = null;
            if (menu != null) menu.gameObject.SetActive(false);
            if (menuBlocker != null) menuBlocker.gameObject.SetActive(false);
        }

        void MenuEquip()
        {
            string key = menuKey;
            CloseMenu();
            if (key != null) EquipGear(key);
        }
    }
}
