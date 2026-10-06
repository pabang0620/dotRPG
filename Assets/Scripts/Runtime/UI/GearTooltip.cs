using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] The one description of a piece of gear used everywhere it shows up (bag, auction, shop, storage, draw
    /// results, reward cards): grade, required level, stats against what is worn in that slot, and trade rule.
    /// </summary>
    public static class GearTooltip
    {
        static CharacterClass Class => Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
        static int Level => Game.Session?.Progression != null ? Game.Session.Progression.Level : 1;

        /// <summary>Appends the gear part; <paramref name="wornKey"/> is the worn piece it was compared with (or null).</summary>
        public static void Append(StringBuilder sb, string key, bool isWorn, out string wornKey)
        {
            wornKey = null;
            var gear = EquipmentDatabase.Get(key);
            if (gear == null) return;
            var eq = Game.Session.Equipment;
            var cls = Class;
            int lv = EquipmentDatabase.LevelOfKey(key);
            sb.Append($"<b>{EquipmentDatabase.RichName(key)}</b>\n");
            sb.Append($"<color=#b8c4d8>[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}");
            if (gear.classOnly.HasValue) sb.Append($" · {CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용");
            sb.Append("</color>\n");
            sb.Append(gear.reqLevel > Level ? $"<color=#ff7070>착용 Lv.{gear.reqLevel} (현재 Lv.{Level})</color>" : $"<color=#b8c4d8>착용 Lv.{gear.reqLevel}</color>");
            string lvText = lv > 0 ? EquipmentDatabase.LevelTag(lv) : "+0";
            sb.Append($"   <color=#ffe066>강화 {lvText} / +{EquipmentDatabase.MaxEnhance}</color>\n\n");

            wornKey = isWorn || !gear.UsableBy(cls) ? null : eq[eq.TargetSlotFor(gear)];
            if (wornKey == key || !EquipmentDatabase.IsEquipment(wornKey)) wornKey = null;
            var st = eq.StatsOfKey(key);
            GearStats? ws = wornKey != null ? eq.StatsOfKey(wornKey) : (GearStats?)null;
            var wornGear = wornKey != null ? EquipmentDatabase.Get(wornKey) : null;
            StatRow(sb, "공격력", st.attack * DamageNumber.DisplayScale, ws.HasValue ? ws.Value.attack * DamageNumber.DisplayScale : (int?)null, "");
            StatRow(sb, "체력", st.maxHealth, ws?.maxHealth, "hp");
            StatRow(sb, "막기 확률", st.block, ws?.block, "%");
            StatRow(sb, "이동 속도", st.speed, ws?.speed, "%");
            StatRow(sb, "경험치 획득", gear.xpBonus, wornGear?.xpBonus, "%");
            StatRow(sb, "범위 피해", gear.aoeBonus, wornGear?.aoeBonus, "%");
            int score = eq.ScoreOf(key);
            sb.Append($"<color=#ffe066>전투력 +{score}</color>");
            if (wornKey != null)
            {
                int diff = score - eq.ScoreOf(wornKey);
                if (diff > 0) sb.Append($"  <color=#8fe28f>▲{diff}</color>");
                else if (diff < 0) sb.Append($"  <color=#ff7070>▼{-diff}</color>");
            }
            sb.Append("\n\n");
            sb.Append($"<i>{gear.description}</i>\n");
            if (!gear.UsableBy(cls)) sb.Append("\n<color=#ff8080>이 캐릭터는 장착할 수 없습니다.</color>\n");
            else if (wornKey != null) sb.Append($"\n<color=#b8c4d8>비교: 착용 중인 {EquipmentDatabase.NameOfKey(wornKey)}</color>\n");
        }

        public static void AppendBind(StringBuilder sb, string key)
        {
            var bind = AuctionRules.BindOf(key);
            sb.Append('\n').Append(AuctionRules.BindLabel(bind));
            if (bind == ItemBind.Tradable && EquipmentDatabase.IsEquipment(key)) sb.Append(" <size=16><color=#b8c4d8>(경매장에서 한 번 팔리면 계정 귀속)</color></size>");
            else if (bind == ItemBind.AccountBound) sb.Append(" <size=16><color=#b8c4d8>(경매장 등록 불가)</color></size>");
            else if (bind == ItemBind.CharacterBound) sb.Append(" <size=16><color=#b8c4d8>(이 캐릭터만 사용)</color></size>");
        }

        public static void StatRow(StringBuilder sb, string name, int value, int? compare, string unit)
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

        /// <summary>Shows the gear tooltip while the pointer is over <paramref name="target"/> (no-op for non-gear keys).</summary>
        public static void Hook(Graphic target, Func<string> key)
        {
            if (target == null) return;
            target.raycastTarget = true;
            var relay = target.GetComponent<HoverRelay>() ?? target.gameObject.AddComponent<HoverRelay>();
            relay.onEnter = () => GearTip.Show(key(), target.rectTransform);
            relay.onExit = GearTip.Hide;
        }
    }

    /// <summary>Pointer enter/exit only: unlike PointerRelay it takes no clicks, so a button under it still gets them.</summary>
    public sealed class HoverRelay : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action onEnter, onExit;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => onEnter?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => onExit?.Invoke();
    }

    /// <summary>[UI] The floating box of <see cref="GearTooltip"/> for windows other than the bag (one, on top of everything).</summary>
    public sealed class GearTip : MonoBehaviour
    {
        static GearTip instance;
        RectTransform box, anchor;
        Image icon;
        Text text;

        // The hovered thing went away (window closed, page changed): the box goes with it.
        void Update()
        {
            if (anchor == null || !anchor.gameObject.activeInHierarchy) gameObject.SetActive(false);
        }

        static GearTip Instance
        {
            get
            {
                if (instance != null || Game.UI == null) return instance;
                var root = (RectTransform)Game.UI.transform;
                var rt = UIFactory.Rect(root, "GearTip");
                rt.pivot = new Vector2(0f, 1f);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                instance = rt.gameObject.AddComponent<GearTip>();
                instance.box = rt;
                var bg = UIFactory.Image(rt, "Bg", Game.Art.Get("ui_tooltip"), Color.white);
                bg.preserveAspect = false;
                bg.raycastTarget = false;
                UIFactory.Stretch(bg.rectTransform);
                instance.icon = UIFactory.SharpIcon(rt, "Icon", Color.white);
                instance.icon.raycastTarget = false;
                UIFactory.Place(instance.icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(64f, 64f));
                instance.text = UIFactory.Text(rt, "Text", "", 18, Color.white, TextAnchor.UpperLeft, true);
                instance.text.lineSpacing = 1.15f;
                instance.text.raycastTarget = false;
                UIFactory.Place(instance.text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -14f), new Vector2(280f, 400f));
                rt.gameObject.SetActive(false);
                return instance;
            }
        }

        public static void Show(string key, RectTransform anchor)
        {
            var t = Instance;
            if (t == null || anchor == null || !EquipmentDatabase.IsEquipment(key)) { Hide(); return; }
            var sb = new StringBuilder();
            GearTooltip.Append(sb, key, false, out _);
            GearTooltip.AppendBind(sb, key);
            t.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(key));
            t.text.text = sb.ToString();
            t.box.sizeDelta = new Vector2(390f, Mathf.Max(96f, t.text.preferredHeight + 32f));
            t.anchor = anchor;
            t.box.SetAsLastSibling();
            t.gameObject.SetActive(true);
            // Next to the hovered thing: right of it if it fits, else left; kept on screen.
            var canvas = (RectTransform)t.box.parent;
            var size = t.box.sizeDelta;
            float halfW = canvas.rect.width * 0.5f, halfH = canvas.rect.height * 0.5f;
            var r = anchor.rect;
            Vector2 tl = canvas.InverseTransformPoint(anchor.TransformPoint(new Vector3(r.xMin, r.yMax, 0f)));
            Vector2 tr = canvas.InverseTransformPoint(anchor.TransformPoint(new Vector3(r.xMax, r.yMax, 0f)));
            float x = tr.x + 10f + size.x <= halfW - 8f ? tr.x + 10f : tl.x - size.x - 10f;
            x = Mathf.Clamp(x, -halfW + 8f, halfW - size.x - 8f);
            float y = Mathf.Clamp(tl.y, -halfH + size.y + 8f, halfH - 8f);
            t.box.anchoredPosition = new Vector2(x, y);
        }

        public static void Hide()
        {
            if (instance != null) instance.gameObject.SetActive(false);
        }
    }
}
