using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] The local player's buffs and debuffs (top-left, under the currency line): shield, guard, blessing, regen,
    /// combo stacks, curse and slow. Combo shows its stack count.
    /// </summary>
    public sealed class BuffBarView : MonoBehaviour
    {
        const float Size = 28f, Gap = 4f;
        const int Slots = 8;
        readonly List<(Image icon, Text label, Image back)> slots = new List<(Image, Text, Image)>();

        public static BuffBarView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Buffs"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -150f), new Vector2(Slots * (Size + Gap), Size));
            var v = root.gameObject.AddComponent<BuffBarView>();
            for (int i = 0; i < Slots; i++)
            {
                var back = UIFactory.Image(root, "Back" + i, Game.Art.Get("ui_white"), new Color32(10, 14, 22, 170));
                back.raycastTarget = false;
                UIFactory.Place(back.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * (Size + Gap), 0f), new Vector2(Size, Size));
                var icon = UIFactory.SharpIcon(back.transform, "Icon", Color.white);
                icon.raycastTarget = false;
                UIFactory.Stretch(icon.rectTransform, 2f, 2f, 2f, 2f);
                var label = UIFactory.Text(back.transform, "Label", "", 14, Color.white, TextAnchor.LowerRight, true);
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow; // "60분" is wider than the 28 px slot: never wrap "분" onto a second line
                UIFactory.Stretch(label.rectTransform, 0f, -3f, -1f, 0f);
                back.gameObject.SetActive(false);
                v.slots.Add((icon, label, back));
            }
            return v;
        }

        void LateUpdate()
        {
            var p = Game.Player;
            int n = 0;
            if (p != null && !p.IsDead)
            {
                var c = CareerCombat.For(p);
                if (c.Shield > 0) Put(ref n, "buff_shield", "");
                if (c.GuardVisible) Put(ref n, "buff_guard", "");
                if (c.BlessVisible) Put(ref n, "buff_bless", "");
                if (c.FrenzyLeft > 0f) Put(ref n, "buff_burn", Mathf.CeilToInt(c.FrenzyLeft).ToString());
                if (p.Data.ScrollLeft > 0f) Put(ref n, "buff_focus", Mathf.CeilToInt(p.Data.ScrollLeft / 60f) + "분"); // [CASH] 투지의 주문서
                if (c.HotVisible) Put(ref n, "buff_regen", "");
                if (c.ComboStacks > 0) Put(ref n, "buff_focus", c.ComboStacks.ToString());
                if (c.Cursed) Put(ref n, "buff_curse", "");
                if (Time.time < c.SlowUntil) Put(ref n, "buff_slow", "");
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
            s.label.text = label;
        }
    }
}
