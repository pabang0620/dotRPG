using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Bag layout: header, character card with worn slots, stat box, tabs, grid and tooltip objects.</summary>
    public partial class EquipmentScreen
    {
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
            var back = MakeButton(root, "Back", "◀", "ui_btngray", new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(96f, 52f), Close, 24);
            back.GetComponentInChildren<Text>().text = "◀ <size=17>ESC</size>"; // [UI] same back button as every other window
            var title = UIFactory.Text(root, "Title", "가방", 40, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(132f, -12f), new Vector2(200f, 52f));
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

            // Paging under the grid (only shown when the tab holds more than one page).
            float gridMid = -24f - gridW * 0.5f;
            pagePrev = MakeButton(root, "BagPrev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(gridMid - 70f, 102f), new Vector2(56f, 44f), () => TurnPage(-1, false), 22, true);
            pageNext = MakeButton(root, "BagNext", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(gridMid + 126f, 102f), new Vector2(56f, 44f), () => TurnPage(1, false), 22, true);
            pageText = UIFactory.Text(root, "BagPage", "", 22, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Place(pageText.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(gridMid, 102f), new Vector2(130f, 44f));

            // Bottom bar.
            var gridIcon = UIFactory.Image(root, "GridIcon", Game.Art.Get("menuicon_capacity"), Color.white); // [ART] storage chest
            gridIcon.preserveAspect = true;
            UIFactory.Place(gridIcon.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f - gridW + 40f, 26f), new Vector2(56f, 56f));
            capacity = UIFactory.Text(root, "Capacity", "", 28, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(capacity.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f - gridW + 250f, 26f), new Vector2(190f, 60f));
            MakeButton(root, "Auto", "자동장착", "ui_btn", new Vector2(1f, 0f), new Vector2(-190f, 26f), new Vector2(168f, 60f), AutoEquip, 26, true);
            sortButton = MakeButton(root, "Sort", "정렬", "ui_btngray", new Vector2(1f, 0f), new Vector2(-24f, 26f), new Vector2(150f, 60f), ToggleSort, 26, true);
            sortLabel = sortButton.GetComponentInChildren<Text>();

            hint = UIFactory.Text(root, "Hint", "", UiTheme.FontCaption, new Color(1f, 1f, 1f, 0.82f), TextAnchor.LowerLeft, true);
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
                s.label = UIFactory.Text(s.rect, "SlotName", EquipmentDatabase.SlotName(worn.Value), 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, true);
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
    }
}
