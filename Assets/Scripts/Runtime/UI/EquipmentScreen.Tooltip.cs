using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Bag tooltip: item text, stat comparison and placement next to the hovered slot.</summary>
    public partial class EquipmentScreen
    {
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
                    sb.Append($"<b><color={EquipmentDatabase.RarityColor(mat.rarity)}>{L.T(mat.name)}</color></b>\n" + L.F("<color=#b8c4d8>{0} · 강화 재료</color>", L.T(EquipmentDatabase.RarityName(mat.rarity))) + "\n\n");
                    sb.Append(L.F("보유 {0}개", Game.Session.Inventory.Count(s.itemId)) + "\n");
                    sb.Append($"<i>{L.T(mat.description)}</i>\n\n" + L.T("<color=#b8c4d8>마을 대장장이(또는 모루)에서 장비 강화에 사용</color>"));
                }
                else if (ConsumableDatabase.IsTicket(s.itemId))
                {
                    var ticket = ConsumableDatabase.Get(s.itemId);
                    sb.Append($"<b>{L.T(ItemText.Name(s.itemId))}</b>\n<color=#b8c4d8>{L.T(ItemText.Kind(s.itemId))}</color>\n\n");
                    sb.Append(L.F("보유 {0}개", Game.Session.Inventory.Count(s.itemId)) + "\n");
                    sb.Append($"<i>{L.T(ticket.description)}</i>\n\n" + L.T("<color=#b8c4d8>가방에 있으면 장비가 파괴될 때 1장이 자동으로 사용됩니다.</color>"));
                    sb.Append("\n" + L.T("<color=#ffe066>우클릭: 강화 창 열기</color>")); // [UX] 2026-10-29
                }
                else if (OpensForge(s.itemId))
                {
                    // [UX] 2026-10-29: +N 강화권 (used from the forge window, which a click opens anywhere).
                    var ticket = ConsumableDatabase.Get(s.itemId);
                    sb.Append($"<b>{ItemText.Name(s.itemId)}</b>\n\n");
                    sb.Append(L.F("보유 {0}개", Game.Session.Inventory.Count(s.itemId)) + "\n");
                    sb.Append($"<i>{L.T(ticket.description)}</i>\n\n" + L.T("<color=#ffe066>우클릭: 강화 창 열기</color>"));
                }
                else if (ConsumableDatabase.IsUsable(s.itemId))
                {
                    var use = ConsumableDatabase.Get(s.itemId);
                    sb.Append($"<b>{L.T(use.name)}</b>\n" + L.T("<color=#b8c4d8>소모품</color>") + "\n\n");
                    sb.Append(L.F("보유 {0}개", Game.Session.Inventory.Count(s.itemId)) + "\n");
                    sb.Append($"<i>{L.T(use.description)}</i>\n\n");
                    if (use.hotkey.HasValue) sb.Append(L.F("<color=#b8c4d8>빠른 사용 키: [{0}]</color>", Game.Input.GetBindingLabel(use.hotkey.Value)) + "\n");
                    sb.Append(L.T("<color=#ffe066>클릭: 사용</color>"));
                }
                else
                {
                    sb.Append($"<b>{L.T(def.displayName)}</b>\n" + L.T("<color=#b8c4d8>소모품 · 재료</color>") + "\n\n");
                    sb.Append(L.F("보유 {0}개", Game.Session.Inventory.Count(s.itemId)) + "\n");
                    sb.Append(L.T("제작에 쓰이는 재료."));
                }
            }
            else
            {
                tooltipIcon.sprite = Game.Art.Get(gear.iconKey);
                GearTooltip.Append(sb, s.itemId, s.worn.HasValue, out string wornKey);
                sb.Append('\n');
                if (s.worn.HasValue) sb.Append(L.T("<color=#ffe066>클릭: 장비 해제</color>"));
                else if (!gear.UsableBy(Class)) { }
                else if (!Game.Session.Equipment.CanWear(gear, Class)) sb.Append(L.F("<color=#ff8080>Lv.{0}부터 장착할 수 있습니다.</color>", gear.reqLevel));
                else if (wornKey != null) sb.Append(L.T("<color=#ffe066>클릭: 교체 장착</color>"));
                else sb.Append(L.T("<color=#ffe066>클릭: 장착</color>"));
            }
            // Trade rule (PLAN_AUCTION §6): shown on every item so the auction house never surprises.
            sb.Append('\n');
            GearTooltip.AppendBind(sb, s.itemId);
            tooltipText.text = sb.ToString();
            // Size the box to the text.
            float h = Mathf.Max(96f, tooltipText.preferredHeight + 32f);
            tooltip.sizeDelta = new Vector2(390f, h);
        }

        void PositionTooltip(Slot s)
        {
            var canvasRect = (RectTransform)tooltip.parent; // the fitted layout rect (it may be scaled down)
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
}
