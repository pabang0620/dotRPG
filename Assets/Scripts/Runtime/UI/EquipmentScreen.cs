using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Full-screen bag (I / Tab), laid out like a mobile RPG inventory:
    /// header with back button and resources · left: character card, the character between two
    /// columns of worn slots, stat box · right: category tabs, icon grid, capacity + auto-equip / sort.
    /// Hovering any icon (or moving the keyboard/gamepad cursor onto it) shows a tooltip.
    /// Click / confirm: bag item → equip (carrot → eat), worn slot → take off.
    /// </summary>
    public partial class EquipmentScreen : MenuScreen
    {
        const int Columns = 6, Rows = 4, GridCells = Columns * Rows;
        const float Cell = 94f, Gap = 8f;

        enum Tab { All, Weapon, Armor, Accessory, Consumable, Etc }
        static readonly string[] TabNames = { "전체", "무기", "방어구", "장신구", "소모품", "기타" };
        static readonly EquipSlot[] LeftSlots = { EquipSlot.Weapon, EquipSlot.Top, EquipSlot.Bottom };
        static readonly EquipSlot[] RightSlots = { EquipSlot.Necklace, EquipSlot.Ring1, EquipSlot.Ring2 };

        /// <summary>One clickable icon square (bag cell or worn slot).</summary>
        sealed class Slot
        {
            public RectTransform rect;
            public Image bg, icon, frame, corner;
            public Text count, label, level;
            public string itemId;      // equipment or resource id shown here (null = empty)
            public EquipSlot? worn;    // set for the six character slots
        }

        UIRoot ui;
        readonly List<Slot> grid = new List<Slot>();
        readonly Dictionary<EquipSlot, Slot> wornSlots = new Dictionary<EquipSlot, Slot>();
        readonly Image[] tabBg = new Image[TabNames.Length];
        readonly Text[] tabText = new Text[TabNames.Length];
        readonly Dictionary<string, Text> currencies = new Dictionary<string, Text>();
        Image character, cursor, weaponPreview;
        Text className, power, stats, capacity, hint;
        readonly Text[] statCells = new Text[6];
        Button sortButton, autoButton;
        Text sortLabel, autoLabel;

        // Tooltip.
        RectTransform tooltip;
        Image tooltipIcon;
        Text tooltipText;
        Slot hovered;       // mouse
        Slot keyboardSlot;  // cursor
        bool mouseActive;
        /// <summary>The keyboard-cursor tooltip only appears once the keyboard/gamepad was used.</summary>
        bool keyboardUsed;

        Tab tab = Tab.All;
        /// <summary>Shown grid page per tab (each tab pages on its own).</summary>
        readonly int[] tabPage = new int[TabNames.Length];
        int pageCount = 1;
        Button pagePrev, pageNext;
        Text pageText;
        bool sortByRarity;
        bool dirty;
        float animTimer;

        // Keyboard cursor: row -1 = tabs, 0..Rows-1 grid; region 0 = worn slots, 1 = grid.
        int region = 1, cx, cy;

        public static EquipmentScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Equipment", false);
            var screen = root.gameObject.AddComponent<EquipmentScreen>();
            screen.ui = ui;
            // [UI] The bag is laid out on a fixed 1280x720 rect that shrinks to fit (UI scale 1.15 / 1.3), like other windows.
            var bg = UIFactory.Overlay(root, "FitBackground", UiTheme.Background);
            bg.raycastTarget = true;
            var layout = UIFactory.Place(UIFactory.Rect(root, "Layout"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, UIFactory.ReferenceResolution);
            layout.gameObject.AddComponent<FitToParent>().design = UIFactory.ReferenceResolution;
            screen.Build(layout);
            screen.BuildDragAndMenu(layout);
            return screen;
        }

        public override void Show()
        {
            base.Show();
            region = 1;
            cx = 0;
            cy = 0;
            Array.Clear(tabPage, 0, tabPage.Length);
            hovered = null;
            mouseActive = false;
            keyboardUsed = false;
            Game.Session.Equipment.Changed += MarkDirty;
            Game.Session.Inventory.Changed += OnInventoryChanged;
            Refresh();
        }

        public override void Hide()
        {
            CloseMenu();
            if (Game.Session != null)
            {
                Game.Session.Equipment.Changed -= MarkDirty;
                Game.Session.Inventory.Changed -= OnInventoryChanged;
            }
            base.Hide();
        }

        void MarkDirty() => dirty = true;
        void OnInventoryChanged(string id, int count, int delta) => dirty = true;
        void Close() => Game.Flow.CloseInventory();

        CharacterClass Class => Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;

        // ================= Content =================

        /// <summary>"기타" holds the enhancement materials and the protection ticket.</summary>
        static bool IsEtc(string id) => EquipmentDatabase.GetMaterial(id) != null || ConsumableDatabase.IsTicket(id);

        bool InTab(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            switch (tab)
            {
                case Tab.Weapon: return gear != null && gear.category == EquipCategory.Weapon;
                case Tab.Armor: return gear != null && (gear.category == EquipCategory.Top || gear.category == EquipCategory.Bottom);
                case Tab.Accessory: return gear != null && (gear.category == EquipCategory.Necklace || gear.category == EquipCategory.Ring);
                case Tab.Consumable: return gear == null && !IsEtc(id);
                case Tab.Etc: return IsEtc(id);
                default: return true;
            }
        }

        /// <summary>Gear keys (database order, higher +level first; or grade → power), consumables, resources, materials, tickets.</summary>
        List<string> BagContents()
        {
            var inventory = Game.Session.Inventory;
            var equipment = Game.Session.Equipment;
            var itemIds = EquipmentDatabase.GearKeys(inventory);
            if (sortByRarity)
                itemIds.Sort((leftKey, rightKey) =>
                {
                    int rarityComparison = EquipmentDatabase.Get(rightKey).rarity.CompareTo(EquipmentDatabase.Get(leftKey).rarity);
                    if (rarityComparison != 0) return rarityComparison;
                    int scoreComparison = equipment.ScoreOf(rightKey).CompareTo(equipment.ScoreOf(leftKey));
                    return scoreComparison != 0 ? scoreComparison : EquipmentDatabase.CompareKeys(leftKey, rightKey);
                });
            foreach (var consumable in ConsumableDatabase.Usable)
                if (inventory.Count(consumable.id) > 0) itemIds.Add(consumable.id);
            foreach (var item in Game.Config.items)
                if (inventory.Count(item.id) > 0) itemIds.Add(item.id);
            foreach (var material in EquipmentDatabase.AllMaterials)
                if (inventory.Count(material.id) > 0) itemIds.Add(material.id);
            foreach (var ticket in ConsumableDatabase.Tickets)
                if (inventory.Count(ticket.id) > 0) itemIds.Add(ticket.id);
            return itemIds;
        }

        void Refresh()
        {
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            var cls = Class;

            // Tabs.
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = (int)tab == i;
                tabBg[i].color = on ? new Color32(70, 96, 130, 255) : new Color(0f, 0f, 0f, 0f);
                tabText[i].color = on ? new Color32(120, 220, 255, 255) : new Color32(90, 170, 255, 255);
            }

            // Grid.
            var all = BagContents();
            var shown = all.FindAll(InTab);
            pageCount = Mathf.Max(1, (shown.Count + GridCells - 1) / GridCells);
            int page = tabPage[(int)tab] = Mathf.Clamp(tabPage[(int)tab], 0, pageCount - 1);
            pageText.text = $"{page + 1} / {pageCount}";
            pageText.gameObject.SetActive(pageCount > 1);
            pagePrev.gameObject.SetActive(pageCount > 1);
            pageNext.gameObject.SetActive(pageCount > 1);
            pagePrev.interactable = page > 0;
            pageNext.interactable = page < pageCount - 1;
            for (int i = 0; i < GridCells; i++)
            {
                var s = grid[i];
                int at = page * GridCells + i;
                s.itemId = at < shown.Count ? shown[at] : null;
                var gear = EquipmentDatabase.Get(s.itemId);
                FillIcon(s, s.itemId, gear);
                s.corner.enabled = gear != null && eq.IsUpgrade(s.itemId, cls);
                int n = s.itemId != null ? bag.Count(s.itemId) : 0;
                bool lowLevel = gear != null && gear.UsableBy(cls) && !eq.CanWear(gear, cls);
                // [UI] Gear above the character's level carries its required level in red instead of only going dark.
                s.count.text = lowLevel ? $"<color=#ff7070>Lv{gear.reqLevel}</color>" : n > 1 ? n.ToString() : "";
                s.icon.color = gear != null && !eq.CanWear(gear, cls) ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white;
            }

            // Worn slots.
            foreach (var pair in wornSlots)
            {
                var s = pair.Value;
                s.itemId = eq[pair.Key];
                FillIcon(s, s.itemId, EquipmentDatabase.Get(s.itemId));
                s.corner.enabled = false;
                s.count.text = "";
                s.label.enabled = s.itemId == null;
            }

            // Texts.
            var info = CharacterClassInfo.Get(cls);
            int atk = CharacterStats.AttackDamage(cls);
            int hp = CharacterStats.MaxHp;
            className.text = $"Lv.{Game.Session.Progression.Level}  {Game.Session.Progression.ClassLabel}";
            power.text = $"전투력 <color=#ffe066>{CharacterStats.Power(cls):N0}</color>";
            statCells[0].text = $"공격력 {atk}{Bonus(eq.AttackBonus)}";
            statCells[1].text = $"HP {hp}";
            statCells[2].text = $"MP {CharacterStats.MaxMp}";
            statCells[3].text = $"막기 {CharacterStats.Block}%";
            statCells[4].text = $"이동 {(CharacterStats.SpeedBonus >= 0 ? "+" : "")}{CharacterStats.SpeedBonus}%";
            statCells[5].text = $"장비 {WornCount(eq)}/{Equipment.SlotCount}";
            // Real count (the grid pages, so nothing is hidden): this tab / whole bag.
            capacity.text = tab == Tab.All ? $"{all.Count}종" : $"{shown.Count} / {all.Count}종";
            foreach (var pair in currencies) pair.Value.text = bag.Count(pair.Key).ToString("N0");
            var input = Game.Input;
            // [UX] Key of each bottom button (1 / Y 자동장착, 2 / L3 정렬), as small text after the label.
            sortLabel.text = (sortByRarity ? "등급순" : "정렬") + $" <size=18><color=#b8c4d8>[{input.GetBindingLabel(GameAction.UseMana)}]</color></size>";
            autoLabel.text = $"자동장착 <size=18><color=#b8c4d8>[{input.GetBindingLabel(GameAction.UseItem)}]</color></size>";
            hint.text = $"아이콘에 마우스를 올리면 설명 · 클릭: 장착/해제    방향키 이동  {input.GetBindingLabel(GameAction.Submit)} 선택  {input.GetBindingLabel(GameAction.Inventory)}/{input.GetBindingLabel(GameAction.Cancel)} 닫기";
            dirty = false;
        }

        static int WornCount(Equipment eq)
        {
            int n = 0;
            for (int i = 0; i < Equipment.SlotCount; i++) if (!string.IsNullOrEmpty(eq[(EquipSlot)i])) n++;
            return n;
        }

        /// <summary>전투력 = base attack·health + everything worn (see <see cref="Equipment.Score"/>).</summary>
        static int PowerScore(int baseAtk, int baseHp, Equipment eq) => baseAtk * 100 + baseHp * 50 + eq.GearScore;

        static string Bonus(int v) => v > 0 ? $"<color=#8fe28f>(+{v})</color>" : "";

        void FillIcon(Slot s, string id, EquipmentItem gear)
        {
            if (id == null)
            {
                s.icon.enabled = false;
                s.frame.color = Color.clear;
                s.level.text = "";
                return;
            }
            s.icon.enabled = true;
            s.icon.sprite = Game.Art.Get(gear != null ? gear.iconKey : Game.Config.GetItem(id).iconKey);
            s.frame.color = ItemText.Frame(id);
            // "+N" in the level colour of the piece's own key.
            int lv = gear != null ? EquipmentDatabase.LevelOfKey(id) : 0;
            s.level.text = lv > 0 ? $"+{lv}" : "";
            s.level.color = EquipmentDatabase.LevelTint(lv);
        }

        // ================= Actions =================

        void Use(Slot s)
        {
            if (s == null || s.itemId == null) return;
            var eq = Game.Session.Equipment;
            if (s.worn.HasValue)
            {
                if (OnlineEconomy.On && s.worn.Value == EquipSlot.Weapon && Class != CharacterClass.Warrior)
                {
                    // [SERVER] A mage always holds a staff (FixSlots); online the server refuses an empty weapon slot.
                    Game.Audio.PlaySfx("cancel");
                    GameEvents.RaiseToast("마법사는 무기를 뺄 수 없습니다. 다른 무기를 장착하면 바뀝니다.");
                    return;
                }
                var wornBefore = OnlineEconomy.WornSnapshot();
                if (eq.Unequip(s.worn.Value))
                {
                    OnlineEconomy.SyncWorn(wornBefore); // [SERVER]
                    Game.Audio.PlaySfx("select");
                    GameEvents.RaiseToast($"{EquipmentDatabase.SlotName(s.worn.Value)} 해제");
                }
                return;
            }
            var gear = EquipmentDatabase.Get(s.itemId);
            if (gear == null)
            {
                if (ConsumableDatabase.IsTicket(s.itemId))
                {
                    // The protection ticket only works on its own, from the enhancement window.
                    Game.Audio.PlaySfx("cancel");
                    GameEvents.RaiseToast("강화 실패로 장비가 파괴될 때 자동으로 사용됩니다.");
                }
                else if (ConsumableDatabase.IsUsable(s.itemId) && Game.Player != null) Game.Player.UseConsumable(s.itemId);
                else if (s.itemId == ItemIds.Carrot && Game.Player != null) Game.Player.TryEatCarrot();
                else Game.Audio.PlaySfx("cancel");
                return;
            }
            EquipGear(s.itemId);
        }

        /// <summary>
        /// Bag gear → worn (click, confirm, drag, context menu). <paramref name="into"/> picks the ring slot
        /// for a drop on 반지 1 / 반지 2; other gear always goes to its own slot.
        /// </summary>
        void EquipGear(string key, EquipSlot? into = null)
        {
            var gear = EquipmentDatabase.Get(key);
            if (gear == null) return;
            var eq = Game.Session.Equipment;
            if (!gear.UsableBy(Class))
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"{CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용 장비입니다.");
                return;
            }
            if (gear.reqLevel > Game.Session.Progression.Level)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"레벨 {gear.reqLevel}부터 착용할 수 있습니다. (지금 Lv.{Game.Session.Progression.Level})");
                return;
            }
            var before = OnlineEconomy.WornSnapshot();
            bool ok = into.HasValue && gear.category == EquipCategory.Ring ? EquipRingInto(key, into.Value) : eq.Equip(key, Class);
            OnlineEconomy.SyncWorn(before); // [SERVER] (no-op when nothing changed)
            if (ok) { Game.Audio.PlaySfx("confirm"); GameEvents.RaiseToast($"{EquipmentDatabase.NameOfKey(key)} 장착!"); }
        }

        void AutoEquip()
        {
            var before = OnlineEconomy.WornSnapshot();
            int n = Game.Session.Equipment.AutoEquip(Class);
            if (n > 0) OnlineEconomy.SyncWorn(before); // [SERVER]
            GameEvents.RaiseToast(n > 0 ? $"더 좋은 장비 {n}개를 장착했습니다." : "이미 가장 좋은 장비를 착용 중입니다.");
        }

        void ToggleSort()
        {
            sortByRarity = !sortByRarity;
            dirty = true;
        }

        void TurnPage(int d, bool sound = true)
        {
            int page = tabPage[(int)tab];
            int next = Mathf.Clamp(page + d, 0, pageCount - 1);
            if (next == page) return;
            tabPage[(int)tab] = next;
            hovered = null;
            keyboardSlot = null;
            if (sound) Game.Audio.PlaySfx("select", 0.5f);
            Refresh(); // right away, so the cursor and tooltip see the new page's items
        }

        // ---------- Developer automation (DevCapture) ----------

        public void DevSelectTab(int index) => SelectTab((Tab)Mathf.Clamp(index, 0, TabNames.Length - 1));
        public void DevPage(int d) => TurnPage(d);
        public string DevPageLabel { get { Refresh(); return pageCount > 1 ? pageText.text : "1 / 1"; } }
        public string DevCapacity { get { Refresh(); return capacity.text; } }

        /// <summary>Ids in the grid cells of the shown page.</summary>
        public List<string> DevShown()
        {
            Refresh();
            var list = new List<string>();
            foreach (var s in grid) if (s.itemId != null) list.Add(s.itemId);
            return list;
        }

        void SelectTab(Tab next)
        {
            if (tab == next) return;
            tab = next;
            Game.Audio.PlaySfx("select");
            dirty = true;
        }

        // ================= Per frame =================

        bool keyTagsPad;

        void Update()
        {
            if (Game.Session == null) return;
            // [UX] The 정렬 / 자동장착 key tags follow the device in use.
            bool pad = Game.Input != null && Game.Input.UsingGamepad;
            if (pad != keyTagsPad) { keyTagsPad = pad; dirty = true; }
            if (dirty) Refresh();

            animTimer += Time.unscaledDeltaTime;
            var eq = Game.Session.Equipment;
            var look = CharacterLook.WithGear(CharacterClassInfo.Get(Class).Look, eq[EquipSlot.Top], eq[EquipSlot.Bottom]);
            character.sprite = Game.Art.GetCharacter(look, "down", Mathf.FloorToInt(animTimer * 1.8f) % 2 == 0 ? "idle0" : "idle1");
            PlaceWeaponPreview();

            if (MenuOpen)
            {
                // [UX] The right-click menu is up: Esc / I only close it, the tooltip stays hidden.
                tooltip.gameObject.SetActive(false);
                cursor.enabled = false;
                if (Time.frameCount != shownFrame && (Game.Input.InventoryPressed || Game.Input.CancelPressed))
                {
                    Game.Audio.PlaySfx("cancel");
                    CloseMenu();
                }
                return;
            }
            HandleKeys();
            UpdateCursorAndTooltip();
        }

        /// <summary>Draws the equipped weapon in the preview character's hand, at the same pixel scale.</summary>
        void PlaceWeaponPreview()
        {
            if (Class == CharacterClass.Warrior)
            {
                SilverWarriorPresentation.Preview(character, weaponPreview, Game.Session.Equipment[EquipSlot.Weapon], Mathf.FloorToInt(animTimer * 1.8f) % 2 == 0 ? "idle0" : "idle1");
                return;
            }
            var body = character.sprite;
            var wpn = Game.Art.Get(EquipmentDatabase.WeaponSprite(Game.Session.Equipment[EquipSlot.Weapon], Class));
            weaponPreview.enabled = body != null && wpn != null;
            if (!weaponPreview.enabled) return;
            weaponPreview.sprite = wpn;
            var box = character.rectTransform.rect.size;
            float scale = Mathf.Min(box.x / body.rect.width, box.y / body.rect.height); // UI px per art px
            // The character image is anchored at its top centre; its sprite is centred in the box.
            Vector2 boxCenter = character.rectTransform.anchoredPosition + new Vector2(0f, -box.y * 0.5f);
            float feetY = boxCenter.y - body.rect.height * scale * 0.5f + body.pivot.y * scale;
            bool staff = Class == CharacterClass.Mage;
            Vector2 hand = staff ? new Vector2(0.36f, 0.04f) : new Vector2(0.3f, 0.34f); // same as the in-game rest pose
            float ppu = body.pixelsPerUnit;
            var rt = weaponPreview.rectTransform;
            rt.anchorMin = rt.anchorMax = character.rectTransform.anchorMin;
            rt.pivot = new Vector2(wpn.pivot.x / wpn.rect.width, wpn.pivot.y / wpn.rect.height);
            rt.sizeDelta = new Vector2(wpn.rect.width, wpn.rect.height) * scale * 0.85f;
            rt.anchoredPosition = new Vector2(boxCenter.x + hand.x * ppu * scale, feetY + hand.y * ppu * scale);
            rt.localRotation = Quaternion.Euler(0f, 0f, staff ? -6f : -160f);
        }

        void HandleKeys()
        {
            if (Time.frameCount == shownFrame || Game.State.ChangedThisFrame) return;
            var input = Game.Input;
            if (input.InventoryPressed || input.CancelPressed)
            {
                Game.Audio.PlaySfx("cancel");
                Close();
                return;
            }
            var nav = input.NavigateStep;
            if (nav != Vector2Int.zero)
            {
                mouseActive = false;
                keyboardUsed = true;
                MoveCursor(nav.x, -nav.y);
            }
            if (input.SubmitPressed)
            {
                mouseActive = false;
                keyboardUsed = true;
                if (region == 1 && cy < 0) return;
                Use(CursorSlot());
            }
            // [UX] The bottom buttons without the mouse: 1 / Y 자동장착, 2 / L3 정렬 (same as clicking them).
            if (input.UseItemPressed) { Game.Audio.PlaySfx("confirm"); AutoEquip(); }
            else if (input.UseManaPressed) { Game.Audio.PlaySfx("confirm"); ToggleSort(); }
        }

        void MoveCursor(int dx, int dy)
        {
            Game.Audio.PlaySfx("select", 0.5f);
            if (region == 1)
            {
                if (cy < 0)
                {
                    // On the tab bar: left/right switches tab, down enters the grid.
                    if (dx != 0) SelectTab((Tab)(((int)tab + dx + TabNames.Length) % TabNames.Length));
                    if (dy > 0) cy = 0;
                    return;
                }
                cx += dx;
                cy += dy;
                // Past the bottom / top row: the next / previous page of this tab (the tab bar only from page 1).
                int page = tabPage[(int)tab];
                if (cy >= Rows && page < pageCount - 1)
                {
                    TurnPage(1, false);
                    cy = 0;
                }
                else if (cy < 0 && page > 0)
                {
                    TurnPage(-1, false);
                    cy = Rows - 1;
                }
                if (cx < 0)
                {
                    region = 0;
                    cx = 1;
                    cy = Mathf.Clamp(cy, 0, 2);
                    return;
                }
                cx = Mathf.Clamp(cx, 0, Columns - 1);
                cy = Mathf.Clamp(cy, -1, Rows - 1);
            }
            else
            {
                cx += dx;
                cy = Mathf.Clamp(cy + dy, 0, 2);
                if (cx > 1)
                {
                    region = 1;
                    cx = 0;
                    cy = Mathf.Clamp(cy, 0, Rows - 1);
                    return;
                }
                cx = Mathf.Clamp(cx, 0, 1);
            }
        }

        Slot CursorSlot()
        {
            if (region == 0) return wornSlots[(cx == 0 ? LeftSlots : RightSlots)[Mathf.Clamp(cy, 0, 2)]];
            if (cy < 0) return null;
            return grid[cy * Columns + cx];
        }

        void UpdateCursorAndTooltip()
        {
            var target = CursorSlot();
            RectTransform cursorAt = target != null ? target.rect : (cy < 0 && region == 1 ? tabBg[(int)tab].rectTransform : null);
            cursor.enabled = !mouseActive && cursorAt != null;
            if (cursor.enabled)
            {
                cursor.rectTransform.position = cursorAt.TransformPoint(cursorAt.rect.center);
                cursor.rectTransform.sizeDelta = cursorAt.rect.size + new Vector2(10f, 10f);
                cursor.rectTransform.SetAsLastSibling();
                tooltip.SetAsLastSibling();
            }

            var shownSlot = mouseActive ? hovered : keyboardUsed ? target : null;
            if (shownSlot == null || shownSlot.itemId == null)
            {
                tooltip.gameObject.SetActive(false);
                return;
            }
            if (!tooltip.gameObject.activeSelf || keyboardSlot != shownSlot)
            {
                keyboardSlot = shownSlot;
                FillTooltip(shownSlot);
            }
            tooltip.gameObject.SetActive(true);
            PositionTooltip(shownSlot);
        }
    }

    /// <summary>Forwards pointer enter / exit / click to callbacks (bag cells, tabs).</summary>
    public class PointerRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action onEnter, onExit;
        public Action<PointerEventData.InputButton> onClick;

        public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
        public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
        public void OnPointerClick(PointerEventData e) => onClick?.Invoke(e.button);
    }

    /// <summary>[UI] Scales a fixed-size layout rect down to fit its parent (never up).</summary>
    public sealed class FitToParent : MonoBehaviour
    {
        public Vector2 design;
        Vector2 last;

        void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            var size = parent.rect.size;
            if (size == last) return;
            last = size;
            float k = Mathf.Min(1f, size.x / design.x, size.y / design.y);
            transform.localScale = new Vector3(k, k, 1f);
        }
    }
}
