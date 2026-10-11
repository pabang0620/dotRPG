using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class EquipmentScreen
    {
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
            // Same body as in the world: the class look or its worn costume skin (worn armour doesn't change it).
            var look = CharacterPreviewCard.LookFor(Class);
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
}
