using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] The local player's running buffs and debuffs, left-aligned just over the skill keys (Q W E R T): a small
    /// icon, the buff's name and the seconds left (stacks for 검기 연성, ON for a toggle). Every career's states show here.
    /// Touch play has no skill bar, so the row stays at the top-left under the currency line there.
    /// </summary>
    public sealed class BuffBarView : MonoBehaviour
    {
        const float Icon = 20f, ChipWidth = 92f, Gap = 4f, Height = 24f;
        const int Slots = 10;
        readonly List<(Image icon, Text label, Image back)> slots = new List<(Image, Text, Image)>();

        public static BuffBarView Create(Transform parent, RectTransform skillBar)
        {
            RectTransform root;
            if (skillBar != null)
            {
                // Same bottom-centre anchor as the skill bar; the row starts at its left edge, a little above it.
                var bar = skillBar.sizeDelta;
                root = UIFactory.Place(UIFactory.Rect(parent, "Buffs"), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                    new Vector2(skillBar.anchoredPosition.x - bar.x * 0.5f, skillBar.anchoredPosition.y + bar.y + 6f), new Vector2(Slots * (ChipWidth + Gap), Height));
            }
            else root = UIFactory.Place(UIFactory.Rect(parent, "Buffs"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -150f), new Vector2(Slots * (ChipWidth + Gap), Height));
            var v = root.gameObject.AddComponent<BuffBarView>();
            for (int i = 0; i < Slots; i++)
            {
                var back = UIFactory.Image(root, "Back" + i, Game.Art.Get("ui_white"), new Color32(10, 14, 22, 180));
                back.raycastTarget = false;
                UIFactory.Place(back.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(i * (ChipWidth + Gap), 0f), new Vector2(ChipWidth, Height));
                var icon = UIFactory.SharpIcon(back.transform, "Icon", Color.white);
                icon.raycastTarget = false;
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(Icon, Icon));
                var label = UIFactory.Text(back.transform, "Label", "", 13, Color.white, TextAnchor.MiddleLeft, true);
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Icon + 5f, 0f), new Vector2(ChipWidth - Icon - 7f, Height));
                back.gameObject.SetActive(false);
                v.slots.Add((icon, label, back));
            }
            return v;
        }

        static string Sec(float s) => Mathf.CeilToInt(s) + "초";

        void LateUpdate()
        {
            var p = Game.Player;
            int n = 0;
            if (p != null && !p.IsDead)
            {
                var c = CareerCombat.For(p);
                if (c.InvulnerableLeft > 0f) Put(ref n, "buff_shield", "무적 " + c.InvulnerableLeft.ToString("0.0") + "초");
                if (c.StanceOn) Put(ref n, "buff_focus", "검기 태세 ON");
                if (c.FrenzyLeft > 0f) Put(ref n, "buff_burn", "검귀 " + Sec(c.FrenzyLeft));
                if (c.ComboStacks > 0) Put(ref n, "buff_focus", $"검기 {c.ComboStacks}/3");
                if (c.ShieldLeft > 0f) Put(ref n, "buff_shield", "보호막 " + Sec(c.ShieldLeft));
                if (c.GuardLeft > 0f) Put(ref n, "buff_guard", "보루 " + Sec(c.GuardLeft));
                if (c.CounterLeft > 0f) Put(ref n, "buff_guard", "방진 " + Sec(c.CounterLeft));
                if (c.OathLeft > 0f) Put(ref n, "buff_guard", "맹세 " + Sec(c.OathLeft));
                if (c.RetaliationLeft > 0f) Put(ref n, "buff_power", "응보 " + Sec(c.RetaliationLeft));
                if (c.SanctumLeft > 0f) Put(ref n, "buff_bless", "성역 +50%");
                if (c.BlessLeft > 0f) Put(ref n, "buff_bless", "축복 " + Sec(c.BlessLeft));
                if (c.HotLeft > 0f) Put(ref n, "buff_regen", "재생 " + Sec(c.HotLeft));
                if (p.Data.ScrollLeft > 0f) Put(ref n, "buff_power", "투지 " + Mathf.CeilToInt(p.Data.ScrollLeft / 60f) + "분"); // [CASH] 투지의 주문서
                if (c.Cursed) Put(ref n, "buff_curse", "저주");
                if (Time.time < c.SlowUntil) Put(ref n, "buff_slow", "둔화 " + Sec(c.SlowUntil - Time.time));
            }
            for (int i = n; i < slots.Count; i++)
                if (slots[i].back.gameObject.activeSelf) slots[i].back.gameObject.SetActive(false);
        }

        void Put(ref int n, string sprite, string label)
        {
            if (n >= slots.Count) return;
            var s = slots[n++];
            if (!s.back.gameObject.activeSelf) s.back.gameObject.SetActive(true);
            s.icon.sprite = Game.Art.Get(sprite);
            if (s.label.text != label) s.label.text = label;
        }
    }
}
