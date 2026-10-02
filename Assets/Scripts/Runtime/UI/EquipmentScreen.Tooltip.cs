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
                    sb.Append($"<b><color={EquipmentDatabase.RarityColor(mat.rarity)}>{mat.name}</color></b>\n<color=#b8c4d8>{EquipmentDatabase.RarityName(mat.rarity)} · 강화 재료</color>\n\n");
                    sb.Append($"보유 {Game.Session.Inventory.Count(s.itemId)}개\n");
                    sb.Append($"<i>{mat.description}</i>\n\n<color=#b8c4d8>마을 대장장이(또는 모루)에서 장비 강화에 사용</color>");
                }
                else if (ConsumableDatabase.IsTicket(s.itemId))
                {
                    var ticket = ConsumableDatabase.Get(s.itemId);
                    sb.Append($"<b>{ItemText.Name(s.itemId)}</b>\n<color=#b8c4d8>{ItemText.Kind(s.itemId)}</color>\n\n");
                    sb.Append($"보유 {Game.Session.Inventory.Count(s.itemId)}개\n");
                    sb.Append($"<i>{ticket.description}</i>\n\n<color=#b8c4d8>가방에 있으면 장비가 파괴될 때 1장이 자동으로 사용된다.</color>");
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
                int lv = EquipmentDatabase.LevelOfKey(s.itemId);
                sb.Append($"<b>{EquipmentDatabase.RichName(s.itemId)}</b>\n");
                sb.Append($"<color=#b8c4d8>[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}");
                if (gear.classOnly.HasValue) sb.Append($" · {CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용");
                string lvText = lv > 0 ? EquipmentDatabase.LevelTag(lv) : "+0";
                sb.Append($"</color>\n<color=#ffe066>강화 {lvText} / +{EquipmentDatabase.MaxEnhance}</color>\n\n");

                // Stats (with this piece's enhancement) and a comparison against what is worn in that slot.
                string wornKey = s.worn.HasValue || !gear.UsableBy(Class) ? null : eq[eq.TargetSlotFor(gear)];
                if (wornKey == s.itemId || !EquipmentDatabase.IsEquipment(wornKey)) wornKey = null;
                var st = eq.StatsOfKey(s.itemId);
                GearStats? ws = wornKey != null ? eq.StatsOfKey(wornKey) : (GearStats?)null;
                StatRow(sb, "공격력", st.attack * DamageNumber.DisplayScale, ws.HasValue ? ws.Value.attack * DamageNumber.DisplayScale : (int?)null, "");
                StatRow(sb, "체력", st.maxHealth, ws?.maxHealth, "hp");
                StatRow(sb, "막기 확률", st.block, ws?.block, "%");
                StatRow(sb, "이동 속도", st.speed, ws?.speed, "%");
                sb.Append($"<color=#ffe066>전투력 +{eq.ScoreOf(s.itemId)}</color>\n\n");
                sb.Append($"<i>{gear.description}</i>\n\n");
                if (s.worn.HasValue) sb.Append("<color=#ffe066>클릭: 장비 해제</color>");
                else if (!gear.UsableBy(Class)) sb.Append("<color=#ff8080>이 캐릭터는 장착할 수 없다.</color>");
                else if (wornKey != null) sb.Append($"<color=#b8c4d8>착용 중: {EquipmentDatabase.NameOfKey(wornKey)}</color>\n<color=#ffe066>클릭: 교체 장착</color>");
                else sb.Append("<color=#ffe066>클릭: 장착</color>");
            }
            // Trade rule (PLAN_AUCTION §6): shown on every item so the auction house never surprises.
            var bind = AuctionRules.BindOf(s.itemId);
            sb.Append("\n\n").Append(AuctionRules.BindLabel(bind));
            if (bind == ItemBind.Tradable && gear != null) sb.Append(" <size=16><color=#b8c4d8>(경매장에서 한 번 팔리면 계정 귀속)</color></size>");
            else if (bind == ItemBind.AccountBound) sb.Append(" <size=16><color=#b8c4d8>(경매장 등록 불가)</color></size>");
            else if (bind == ItemBind.CharacterBound) sb.Append(" <size=16><color=#b8c4d8>(이 캐릭터만 사용)</color></size>");
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
}
