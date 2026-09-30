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
    public class EquipmentScreen : MenuScreen
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
        Button sortButton;
        Text sortLabel;

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
            screen.Build(root);
            return screen;
        }

        // ================= Building =================

        static Image Tinted(Transform parent, string name, string sprite, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            return img;
        }

        void Build(RectTransform root)
        {
            // Background: deep blue with a lighter band behind the character.
            var bg = UIFactory.Overlay(root, "Bg", new Color32(34, 52, 76, 255));
            bg.raycastTarget = true;
            var glow = Tinted(root, "Glow", "ui_white", new Color32(58, 86, 120, 150));
            UIFactory.Place(glow.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(120f, -40f), new Vector2(520f, 440f));

            // ----- Header -----
            var header = Tinted(root, "Header", "ui_white", new Color32(22, 31, 46, 255));
            UIFactory.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, 76f));
            var back = MakeButton(root, "Back", "◀", "ui_btngray", new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(56f, 52f), Close, 26);
            var title = UIFactory.Text(root, "Title", "가방", 40, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -12f), new Vector2(200f, 52f));
            float cxHead = -24f;
            var chipIds = new List<(string id, string icon)> { (ConsumableDatabase.Gold, "icon_gold") };
            for (int i = Game.Config.items.Count - 1; i >= 0; i--) chipIds.Add((Game.Config.items[i].id, Game.Config.items[i].iconKey));
            foreach (var (chipId, chipIcon) in chipIds)
            {
                bool gold = chipId == ConsumableDatabase.Gold;
                var chip = Tinted(root, "Chip_" + chipId, "ui_dark", new Color(1f, 1f, 1f, 0.95f));
                UIFactory.Place(chip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(cxHead, -14f), new Vector2(gold ? 200f : 150f, 48f));
                var icon = UIFactory.Image(chip.transform, "Icon", Game.Art.Get(chipIcon), Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(34f, 34f));
                var t = UIFactory.Text(chip.transform, "Count", "0", 24, gold ? new Color32(255, 216, 74, 255) : Color.white, TextAnchor.MiddleRight, true);
                UIFactory.Stretch(t.rectTransform, 50f, 0f, 16f, 0f);
                currencies[chipId] = t;
                cxHead -= (gold ? 200f : 150f) + 12f;
            }

            // ----- Left: character card -----
            var card = Tinted(root, "Card", "ui_white", new Color32(45, 78, 120, 235));
            UIFactory.Place(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -92f), new Vector2(250f, 96f));
            className = UIFactory.Text(card.transform, "Class", "", 30, Color.white, TextAnchor.UpperLeft, true);
            UIFactory.Place(className.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(230f, 40f));
            power = UIFactory.Text(card.transform, "Power", "", 22, Color.white, TextAnchor.UpperLeft, true);
            UIFactory.Place(power.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -52f), new Vector2(230f, 34f));

            // Character in the middle, worn slots in two columns.
            character = UIFactory.Image(root, "Character", null, Color.white);
            UIFactory.Place(character.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(330f, -200f), new Vector2(200f, 300f));
            weaponPreview = UIFactory.Image(root, "Weapon", null, Color.white);
            weaponPreview.preserveAspect = false;
            for (int i = 0; i < 3; i++)
            {
                wornSlots[LeftSlots[i]] = MakeSlot(root, new Vector2(40f, -210f - i * 104f), 88f, "ui_slotblue", LeftSlots[i]);
                wornSlots[RightSlots[i]] = MakeSlot(root, new Vector2(532f, -210f - i * 104f), 88f, "ui_slotblue", RightSlots[i]);
            }

            // Stat box: two rows of three evenly spaced columns, so values always line up.
            var statBox = Tinted(root, "Stats", "ui_white", new Color32(18, 28, 44, 235));
            UIFactory.Place(statBox.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120f, 34f), new Vector2(460f, 80f));
            for (int i = 0; i < statCells.Length; i++)
            {
                var t = UIFactory.Text(statBox.transform, "Stat" + i, "", 18, Color.white, TextAnchor.MiddleLeft, true);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f + (i % 3) * 148f, -6f - (i / 3) * 34f), new Vector2(144f, 34f));
                statCells[i] = t;
            }
            stats = statCells[0];

            // ----- Right: tabs + grid -----
            float gridW = Columns * Cell + (Columns - 1) * Gap;
            var tabsBar = Tinted(root, "Tabs", "ui_white", new Color32(24, 36, 54, 255));
            UIFactory.Place(tabsBar.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -92f), new Vector2(gridW, 54f));
            float tabW = gridW / TabNames.Length;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tb = Tinted(tabsBar.transform, "Tab" + i, "ui_white", Color.clear);
                UIFactory.Place(tb.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * tabW, 0f), new Vector2(tabW, 54f));
                tb.raycastTarget = true;
                var hit = tb.gameObject.AddComponent<PointerRelay>();
                hit.onClick = _ => SelectTab((Tab)index);
                var tt = UIFactory.Text(tb.transform, "Text", TabNames[i], 26, Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(tt.rectTransform);
                tabBg[i] = tb;
                tabText[i] = tt;
            }
            for (int i = 0; i < GridCells; i++)
            {
                int col = i % Columns, row = i / Columns;
                var pos = new Vector2(-24f - gridW + col * (Cell + Gap), -160f - row * (Cell + Gap));
                grid.Add(MakeSlot(root, pos, Cell, "ui_slot", null, true));
            }

            // Bottom bar.
            var gridIcon = UIFactory.Text(root, "GridIcon", "▦", 44, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Place(gridIcon.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f - gridW + 40f, 26f), new Vector2(60f, 60f));
            capacity = UIFactory.Text(root, "Capacity", "", 28, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(capacity.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f - gridW + 250f, 26f), new Vector2(190f, 60f));
            MakeButton(root, "Auto", "자동장착", "ui_btn", new Vector2(1f, 0f), new Vector2(-190f, 26f), new Vector2(168f, 60f), AutoEquip, 26, true);
            sortButton = MakeButton(root, "Sort", "정렬", "ui_btngray", new Vector2(1f, 0f), new Vector2(-24f, 26f), new Vector2(150f, 60f), ToggleSort, 26, true);
            sortLabel = sortButton.GetComponentInChildren<Text>();

            hint = UIFactory.Text(root, "Hint", "", 15, new Color(1f, 1f, 1f, 0.65f), TextAnchor.LowerLeft, true);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 6f), new Vector2(900f, 24f));

            // Keyboard / gamepad cursor.
            cursor = Tinted(root, "Cursor", "ui_frame", new Color32(255, 211, 74, 255));
            cursor.raycastTarget = false;

            // Tooltip (drawn last so it is on top).
            tooltip = UIFactory.Rect(root, "Tooltip");
            tooltip.pivot = new Vector2(0f, 1f);
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            var tbg = Tinted(tooltip, "Bg", "ui_tooltip", Color.white);
            UIFactory.Stretch(tbg.rectTransform);
            tooltipIcon = UIFactory.SharpIcon(tooltip, "Icon", Color.white);
            UIFactory.Place(tooltipIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(64f, 64f));
            tooltipText = UIFactory.Text(tooltip, "Text", "", 18, Color.white, TextAnchor.UpperLeft, true);
            tooltipText.lineSpacing = 1.15f;
            UIFactory.Place(tooltipText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -14f), new Vector2(280f, 400f));
            tooltip.gameObject.SetActive(false);
        }

        Slot MakeSlot(Transform parent, Vector2 topLeft, float size, string sprite, EquipSlot? worn, bool rightAnchored = false)
        {
            var s = new Slot { worn = worn };
            var anchor = rightAnchored ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            s.bg = Tinted(parent, worn.HasValue ? "Worn_" + worn : "Cell", sprite, Color.white);
            s.rect = s.bg.rectTransform;
            UIFactory.Place(s.rect, anchor, new Vector2(0f, 1f), topLeft, new Vector2(size, size));
            s.bg.raycastTarget = true;
            s.icon = UIFactory.SharpIcon(s.rect, "Icon", Color.white);
            UIFactory.Place(s.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.72f, size * 0.72f));
            s.frame = Tinted(s.rect, "Rarity", "ui_frame", Color.clear);
            UIFactory.Stretch(s.frame.rectTransform);
            s.corner = UIFactory.Image(s.rect, "New", Game.Art.Get("ui_corner"), Color.white);
            UIFactory.Place(s.corner.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(3f, -3f), new Vector2(22f, 22f));
            s.count = UIFactory.Text(s.rect, "Count", "", 22, Color.white, TextAnchor.LowerRight, true);
            UIFactory.Stretch(s.count.rectTransform, 4f, 2f, 6f, 2f);
            s.level = UIFactory.Text(s.rect, "Level", "", 20, new Color32(255, 224, 102, 255), TextAnchor.UpperRight, true);
            UIFactory.Stretch(s.level.rectTransform, 4f, 2f, 6f, 2f);
            if (worn.HasValue)
            {
                s.label = UIFactory.Text(s.rect, "SlotName", EquipmentDatabase.SlotName(worn.Value), 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(s.label.rectTransform);
            }
            var relay = s.bg.gameObject.AddComponent<PointerRelay>();
            relay.onEnter = () => { hovered = s; mouseActive = true; };
            relay.onExit = () => { if (hovered == s) hovered = null; };
            relay.onClick = b => Use(s);
            return s;
        }

        Button MakeButton(Transform parent, string name, string label, string sprite, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, int font, bool rightAnchored = false)
        {
            var img = Tinted(parent, name, sprite, Color.white);
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, anchor, rightAnchored ? new Vector2(1f, 0f) : new Vector2(0f, 1f), pos, size);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => { Game.Audio.PlaySfx("confirm"); onClick(); });
            var text = UIFactory.Text(img.transform, "Text", label, font, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(text.rectTransform);
            return button;
        }

        // ================= Lifecycle =================

        public override void Show()
        {
            base.Show();
            region = 1; cx = 0; cy = 0;
            hovered = null;
            mouseActive = false;
            keyboardUsed = false;
            Game.Session.Equipment.Changed += MarkDirty;
            Game.Session.Inventory.Changed += OnInventoryChanged;
            Refresh();
        }

        public override void Hide()
        {
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

        bool InTab(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            switch (tab)
            {
                case Tab.Weapon: return gear != null && gear.category == EquipCategory.Weapon;
                case Tab.Armor: return gear != null && (gear.category == EquipCategory.Top || gear.category == EquipCategory.Bottom);
                case Tab.Accessory: return gear != null && (gear.category == EquipCategory.Necklace || gear.category == EquipCategory.Ring);
                case Tab.Consumable: return gear == null && EquipmentDatabase.GetMaterial(id) == null;
                case Tab.Etc: return EquipmentDatabase.GetMaterial(id) != null;
                default: return true;
            }
        }

        List<string> BagContents()
        {
            var bag = Game.Session.Inventory;
            var ids = new List<string>();
            foreach (var item in EquipmentDatabase.All)
                if (bag.Count(item.id) > 0) ids.Add(item.id);
            if (sortByRarity)
                ids.Sort((a, b) =>
                {
                    int r = EquipmentDatabase.Get(b).rarity.CompareTo(EquipmentDatabase.Get(a).rarity);
                    return r != 0 ? r : Game.Session.Equipment.Score(EquipmentDatabase.Get(b)).CompareTo(Game.Session.Equipment.Score(EquipmentDatabase.Get(a)));
                });
            foreach (var use in ConsumableDatabase.Usable)
                if (bag.Count(use.id) > 0) ids.Add(use.id);
            foreach (var def in Game.Config.items)
                if (bag.Count(def.id) > 0) ids.Add(def.id);
            foreach (var mat in EquipmentDatabase.AllMaterials)
                if (bag.Count(mat.id) > 0) ids.Add(mat.id);
            return ids;
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
            for (int i = 0; i < GridCells; i++)
            {
                var s = grid[i];
                s.itemId = i < shown.Count ? shown[i] : null;
                var gear = EquipmentDatabase.Get(s.itemId);
                FillIcon(s, s.itemId, gear);
                s.corner.enabled = gear != null && eq.IsUpgrade(gear, cls);
                int n = s.itemId != null ? bag.Count(s.itemId) : 0;
                s.count.text = n > 1 ? n.ToString() : "";
                s.icon.color = gear != null && !gear.UsableBy(cls) ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white;
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
            className.text = $"Lv.{Game.Session.Progression.Level}  {info.displayName}";
            power.text = $"전투력 <color=#ffe066>{CharacterStats.Power(cls):N0}</color>";
            statCells[0].text = $"공격력 {atk}{Bonus(eq.AttackBonus)}";
            statCells[1].text = $"HP {hp}";
            statCells[2].text = $"MP {CharacterStats.MaxMp}";
            statCells[3].text = $"막기 {CharacterStats.Block}%";
            statCells[4].text = $"이동 {(CharacterStats.SpeedBonus >= 0 ? "+" : "")}{CharacterStats.SpeedBonus}%";
            statCells[5].text = $"장비 {WornCount(eq)}/{Equipment.SlotCount}";
            capacity.text = $"{Math.Min(all.Count, GridCells)}/{GridCells}";
            foreach (var pair in currencies) pair.Value.text = bag.Count(pair.Key).ToString("N0");
            sortLabel.text = sortByRarity ? "등급순" : "정렬";
            var input = Game.Input;
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
            var mat = EquipmentDatabase.GetMaterial(id);
            if (gear != null || mat != null)
            {
                var tint = EquipmentDatabase.RarityTint(gear != null ? gear.rarity : mat.rarity);
                tint.a = (gear != null ? gear.rarity : mat.rarity) == ItemRarity.Common ? 0.25f : 0.9f;
                s.frame.color = tint;
            }
            else s.frame.color = new Color(1f, 1f, 1f, 0.12f);
            int lv = gear != null ? Game.Session.Equipment.LevelOf(gear.id) : 0;
            s.level.text = lv > 0 ? $"+{lv}" : "";
        }

        // ================= Actions =================

        void Use(Slot s)
        {
            if (s == null || s.itemId == null) return;
            var eq = Game.Session.Equipment;
            if (s.worn.HasValue)
            {
                if (eq.Unequip(s.worn.Value)) { Game.Audio.PlaySfx("select"); GameEvents.RaiseToast($"{EquipmentDatabase.SlotName(s.worn.Value)} 해제"); }
                return;
            }
            var gear = EquipmentDatabase.Get(s.itemId);
            if (gear == null)
            {
                if (ConsumableDatabase.IsUsable(s.itemId) && Game.Player != null) Game.Player.UseConsumable(s.itemId);
                else if (s.itemId == ItemIds.Carrot && Game.Player != null) Game.Player.TryEatCarrot();
                else Game.Audio.PlaySfx("cancel");
                return;
            }
            if (!gear.UsableBy(Class))
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"{CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용 장비다.");
                return;
            }
            if (eq.Equip(gear.id, Class)) { Game.Audio.PlaySfx("confirm"); GameEvents.RaiseToast($"{gear.name} 장착!"); }
        }

        void AutoEquip()
        {
            int n = Game.Session.Equipment.AutoEquip(Class);
            GameEvents.RaiseToast(n > 0 ? $"더 좋은 장비 {n}개를 장착했다." : "이미 가장 좋은 장비를 착용 중이다.");
        }

        void ToggleSort()
        {
            sortByRarity = !sortByRarity;
            dirty = true;
        }

        void SelectTab(Tab next)
        {
            if (tab == next) return;
            tab = next;
            Game.Audio.PlaySfx("select");
            dirty = true;
        }

        // ================= Per frame =================

        void Update()
        {
            if (Game.Session == null) return;
            if (dirty) Refresh();

            animTimer += Time.unscaledDeltaTime;
            var eq = Game.Session.Equipment;
            var look = CharacterLook.WithGear(CharacterClassInfo.Get(Class).Look, eq[EquipSlot.Top], eq[EquipSlot.Bottom]);
            character.sprite = Game.Art.GetCharacter(look, "down", Mathf.FloorToInt(animTimer * 1.8f) % 2 == 0 ? "idle0" : "idle1");
            PlaceWeaponPreview();

            HandleKeys();
            UpdateCursorAndTooltip();
        }

        /// <summary>Draws the equipped weapon in the preview character's hand, at the same pixel scale.</summary>
        void PlaceWeaponPreview()
        {
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
                if (cx < 0) { region = 0; cx = 1; cy = Mathf.Clamp(cy, 0, 2); return; }
                cx = Mathf.Clamp(cx, 0, Columns - 1);
                cy = Mathf.Clamp(cy, -1, Rows - 1);
            }
            else
            {
                cx += dx;
                cy = Mathf.Clamp(cy + dy, 0, 2);
                if (cx > 1) { region = 1; cx = 0; cy = Mathf.Clamp(cy, 0, Rows - 1); return; }
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

        void FillTooltip(Slot s)
        {
            var gear = EquipmentDatabase.Get(s.itemId);
            var sb = new StringBuilder();
            if (gear == null)
            {
                var def = Game.Config.GetItem(s.itemId);
                var mat = EquipmentDatabase.GetMaterial(s.itemId);
                tooltipIcon.sprite = Game.Art.Get(def.iconKey);
                if (mat != null)
                {
                    sb.Append($"<b><color={EquipmentDatabase.RarityColor(mat.rarity)}>{mat.name}</color></b>\n<color=#b8c4d8>{EquipmentDatabase.RarityName(mat.rarity)} · 강화 재료</color>\n\n");
                    sb.Append($"보유 {Game.Session.Inventory.Count(s.itemId)}개\n");
                    sb.Append($"<i>{mat.description}</i>\n\n<color=#b8c4d8>마을 대장장이(또는 모루)에서 장비 강화에 사용</color>");
                }
                else if (ConsumableDatabase.IsUsable(s.itemId))
                {
                    var use = ConsumableDatabase.Get(s.itemId);
                    sb.Append($"<b>{use.name}</b>\n<color=#b8c4d8>소모품</color>\n\n");
                    sb.Append($"보유 {Game.Session.Inventory.Count(s.itemId)}개\n");
                    sb.Append($"<i>{use.description}</i>\n\n");
                    if (use.hotkey.HasValue) sb.Append($"<color=#b8c4d8>빠른 사용 키: [{Game.Input.GetBindingLabel(use.hotkey.Value)}]</color>\n");
                    sb.Append("<color=#ffe066>클릭: 사용</color>");
                }
                else
                {
                    sb.Append($"<b>{def.displayName}</b>\n<color=#b8c4d8>소모품 · 재료</color>\n\n");
                    sb.Append($"보유 {Game.Session.Inventory.Count(s.itemId)}개\n");
                    sb.Append(s.itemId == ItemIds.Carrot ? "<color=#8fe28f>먹으면 체력을 회복한다.</color>\n\n<color=#ffe066>클릭: 먹기</color>"
                        : "공방 재건과 제작에 쓰이는 재료.");
                }
            }
            else
            {
                tooltipIcon.sprite = Game.Art.Get(gear.iconKey);
                var eq = Game.Session.Equipment;
                int lv = eq.LevelOf(gear.id);
                sb.Append($"<b><color={EquipmentDatabase.RarityColor(gear.rarity)}>{gear.NameAt(lv)}</color></b>\n");
                sb.Append($"<color=#b8c4d8>[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}");
                if (gear.classOnly.HasValue) sb.Append($" · {CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용");
                sb.Append($"</color>\n<color=#ffe066>강화 +{lv} / +{EquipmentDatabase.MaxEnhance}</color>\n\n");

                // Stats (with enhancement) and a comparison against what is worn in that slot.
                EquipmentItem worn = s.worn.HasValue || !gear.UsableBy(Class) ? null : eq.ItemIn(eq.TargetSlotFor(gear));
                if (worn == gear) worn = null;
                var st = eq.StatsOf(gear);
                GearStats? ws = worn != null ? eq.StatsOf(worn) : (GearStats?)null;
                StatRow(sb, "공격력", st.attack * DamageNumber.DisplayScale, ws.HasValue ? ws.Value.attack * DamageNumber.DisplayScale : (int?)null, "");
                StatRow(sb, "체력", st.maxHealth, ws?.maxHealth, "hp");
                StatRow(sb, "막기 확률", st.block, ws?.block, "%");
                StatRow(sb, "이동 속도", st.speed, ws?.speed, "%");
                sb.Append($"<color=#ffe066>전투력 +{eq.Score(gear)}</color>\n\n");
                sb.Append($"<i>{gear.description}</i>\n\n");
                if (s.worn.HasValue) sb.Append("<color=#ffe066>클릭: 장비 해제</color>");
                else if (!gear.UsableBy(Class)) sb.Append("<color=#ff8080>이 캐릭터는 장착할 수 없다.</color>");
                else if (worn != null) sb.Append($"<color=#b8c4d8>착용 중: {worn.NameAt(eq.LevelOf(worn.id))}</color>\n<color=#ffe066>클릭: 교체 장착</color>");
                else sb.Append("<color=#ffe066>클릭: 장착</color>");
            }
            tooltipText.text = sb.ToString();
            // Size the box to the text.
            float h = Mathf.Max(96f, tooltipText.preferredHeight + 32f);
            tooltip.sizeDelta = new Vector2(390f, h);
        }

        static void StatRow(StringBuilder sb, string name, int value, int? compare, string unit)
        {
            if (value == 0 && (compare == null || compare == 0)) return;
            string Fmt(int v) => unit == "hp" ? EquipmentDatabase.Hearts(Math.Abs(v)) : $"{Math.Abs(v)}{unit}";
            string sign = value >= 0 ? "+" : "-";
            sb.Append($"{name}  {sign}{Fmt(value)}");
            if (compare.HasValue)
            {
                int diff = value - compare.Value;
                if (diff > 0) sb.Append($"  <color=#8fe28f>▲{Fmt(diff)}</color>");
                else if (diff < 0) sb.Append($"  <color=#ff7070>▼{Fmt(diff)}</color>");
            }
            sb.Append('\n');
        }

        void PositionTooltip(Slot s)
        {
            var canvasRect = (RectTransform)transform;
            var size = tooltip.sizeDelta;
            float halfW = canvasRect.rect.width * 0.5f, halfH = canvasRect.rect.height * 0.5f;
            // Bag cells (right half): show on the left so the grid stays visible; worn slots: on the right.
            bool left = !s.worn.HasValue;
            var r = s.rect.rect;
            Vector2 topLeft = canvasRect.InverseTransformPoint(s.rect.TransformPoint(new Vector3(r.xMin, r.yMax, 0f)));
            Vector2 topRight = canvasRect.InverseTransformPoint(s.rect.TransformPoint(new Vector3(r.xMax, r.yMax, 0f)));
            float x = left ? topLeft.x - size.x - 10f : topRight.x + 10f;
            x = Mathf.Clamp(x, -halfW + 8f, halfW - size.x - 8f);
            float y = Mathf.Clamp(topLeft.y, -halfH + size.y + 8f, halfH - 8f);
            tooltip.anchoredPosition = new Vector2(x, y);
            tooltip.SetAsLastSibling();
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
}
