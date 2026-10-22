using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] The local player's running buffs and debuffs, left-aligned just over the skill keys (Q W E R T): a small
    /// icon, the buff's name and the seconds left (stacks for 검기 연성, ON for a toggle). Every career's states show here.
    /// Touch play has no skill bar, so the row stays at the top-left under the currency line there.
    /// Clicking a chip opens its explanation above the row; clicking another chip switches it, clicking anywhere
    /// else (or the buff ending) closes it.
    /// </summary>
    public sealed class BuffBarView : MonoBehaviour
    {
        const float Icon = 20f, ChipWidth = 108f, Gap = 4f, Height = 24f;
        const int Slots = 10;
        readonly List<(Image icon, Text label, Image back)> slots = new List<(Image, Text, Image)>();
        readonly List<string> titles = new List<string>(), descs = new List<string>();
        RectTransform tip;
        Text tipText;
        int open = -1, downFrame = -1;
        string openTitle;

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
                back.raycastTarget = true; // [UI] click = explanation
                int k = i;
                back.gameObject.AddComponent<ChipPress>().press = () => v.Press(k);
                UIFactory.Place(back.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(i * (ChipWidth + Gap), 0f), new Vector2(ChipWidth, Height));
                var icon = UIFactory.SharpIcon(back.transform, "Icon", Color.white);
                icon.raycastTarget = false;
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(Icon, Icon));
                var label = UIFactory.Text(back.transform, "Label", "", 13, Color.white, TextAnchor.MiddleLeft, true);
                label.raycastTarget = false;
                // [UI] "방진 3회 120초" can be wider than the chip: shrink to fit instead of running past its edge.
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = label.fontSize;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                UIFactory.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Icon + 5f, 0f), new Vector2(ChipWidth - Icon - 7f, Height));
                back.gameObject.SetActive(false);
                v.slots.Add((icon, label, back));
                v.titles.Add("");
                v.descs.Add("");
            }
            // The explanation box: above the row, at the clicked chip.
            var tipBg = UIFactory.Image(root, "Tip", Game.Art.Get("ui_white"), new Color32(14, 20, 32, 235));
            tipBg.raycastTarget = false;
            v.tip = tipBg.rectTransform;
            v.tip.anchorMin = v.tip.anchorMax = Vector2.zero;
            v.tip.pivot = Vector2.zero;
            v.tipText = UIFactory.Text(v.tip, "Text", "", 15, Color.white, TextAnchor.UpperLeft, true);
            v.tipText.raycastTarget = false;
            v.tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            v.tipText.verticalOverflow = VerticalWrapMode.Overflow;
            v.tipText.resizeTextForBestFit = false;
            // The box grows with its text through Unity's layout (a hand-measured height came out short).
            var layout = tipBg.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fit = tipBg.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            v.tip.gameObject.SetActive(false);
            return v;
        }

        sealed class ChipPress : MonoBehaviour, IPointerDownHandler
        {
            public System.Action press;
            public void OnPointerDown(PointerEventData e) => press?.Invoke();
        }

        void Press(int i)
        {
            downFrame = Time.frameCount;
            if (i >= slots.Count || !slots[i].back.gameObject.activeSelf) return;
            if (open == i) { Close(); return; } // a second click on the same chip closes it
            open = i;
            openTitle = titles[i];
            SetTip($"<b>{titles[i]}</b>\n{descs[i]}");
            tip.anchoredPosition = new Vector2(i * (ChipWidth + Gap), Height + 6f);
            tip.gameObject.SetActive(true);
            tip.SetAsLastSibling();
        }

        /// <summary>Sets the box text and grows the background to cover every wrapped line.</summary>
        void SetTip(string text)
        {
            tipText.text = text;
            tip.sizeDelta = new Vector2(280f, tip.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip); // height follows the wrapped text this frame
        }

        void Close()
        {
            open = -1;
            openTitle = null;
            if (tip != null) tip.gameObject.SetActive(false);
        }

        static string Sec(float s) => L.F("{0}초", Mathf.CeilToInt(s));

        void LateUpdate()
        {
            // Any press that wasn't on a chip closes the explanation.
            if (open >= 0 && Input.GetMouseButtonDown(0) && downFrame != Time.frameCount) Close();
            var p = Game.Player;
            int n = 0;
            if (p != null && !p.IsDead)
            {
                var c = CareerCombat.For(p);
                if (p.IsStunned) Put(ref n, "buff_slow", L.F("기절 {0:0.0}초", p.StunnedLeft), L.T("기절"), L.T("이동·공격·스킬·물약 사용 불가."));
                if (Game.Cosmetics != null && Game.Cosmetics.LegendSet(p.Class)) Put(ref n, "buff_power", L.T("전설 세트"), L.T("전설 세트"), L.F("공격력 +{0}%, 최대 체력 +{1}%.", CosmeticStore.SetDamagePercent, CosmeticStore.SetHealthPercent));
                if (c.InvulnerableLeft > 0f) Put(ref n, "buff_shield", L.F("무적 {0:0.0}초", c.InvulnerableLeft), L.T("무적"), L.T("받는 피해 0."));
                if (c.StanceOn) Put(ref n, "buff_focus", L.T("검기 태세 ON"), L.T("검기 태세"), L.T("기본 공격마다 관통 검기 발사(3타째 3줄). 검기마다 MP 소모."));
                if (c.FrenzyLeft > 0f) Put(ref n, "buff_burn", L.F("검귀 {0}", Sec(c.FrenzyLeft)), L.T("검귀 해방"), L.F("모든 공격 피해 +{0}%.", c.FrenzyPct));
                // [ROGUE] 맹독 도포 · 은신 · 분신술 (existing icons)
                if (c.VenomLeft > 0f) Put(ref n, "buff_burn", L.F("맹독 {0}", Sec(c.VenomLeft)), L.T("맹독 도포"), L.F("직접 공격 피해의 {0}%만큼 독(3초, 방어 무시).", c.VenomPct));
                if (c.StealthLeft > 0f) Put(ref n, "buff_focus", L.F("은신 {0}", Sec(c.StealthLeft)), L.T("은신"), L.T("일반 몬스터가 추적을 멈춘다. 첫 공격은 피해 증가·등 뒤 판정, 공격하면 풀린다."));
                if (c.ClonesLeft > 0f) Put(ref n, "buff_power", L.F("분신 {0}", Sec(c.ClonesLeft)), L.T("분신술"), L.T("분신 둘이 쿠나이 투척을 따라 한다. 다시 누르면 분신과 자리 바꾸기."));
                if (c.ComboStacks > 0) Put(ref n, "buff_focus", L.F("검기 {0}/3", c.ComboStacks), L.T("검기 연성"), L.T("3개일 때 다음 전직 스킬 피해 +20%."));
                if (c.ShieldLeft > 0f) Put(ref n, "buff_shield", c.ShieldCareer == Career.Arcanist ? L.F("배리어 {0}", Sec(c.ShieldLeft)) : L.F("보호막 {0}", Sec(c.ShieldLeft)), c.ShieldCareer == Career.Arcanist ? L.T("매직 배리어") : L.T("보호막"), L.F("피해 흡수 {0} 남음.", c.Shield));
                if (c.GuardLeft > 0f) Put(ref n, "buff_guard", L.F("보루 {0}", Sec(c.GuardLeft)), L.T("강철의 보루"), L.T("받는 피해 -30%."));
                if (c.CounterLeft > 0f) Put(ref n, "buff_guard", L.F("방진 {0}회 {1}", c.CounterCharges, Sec(c.CounterLeft)), L.T("응보의 방진"), L.F("받는 피해 -40%. 방어 {0}회 남음, 막을 때 반격.", c.CounterCharges)); // blocks left + time
                if (c.OathLeft > 0f) Put(ref n, "buff_guard", L.F("맹세 {0}", Sec(c.OathLeft)), L.T("수호의 맹세"), L.T("결계 안 아군 받는 피해 -30%, 저주·둔화 해제."));
                if (c.RetaliationLeft > 0f) Put(ref n, "buff_power", L.F("응보 {0}", Sec(c.RetaliationLeft)), L.T("수호의 반향"), L.T("피격 후 다음 공격 피해 증가."));
                if (c.SanctumLeft > 0f) Put(ref n, "buff_bless", L.T("성역 +50%"), L.T("천상의 행진 성역"), L.T("공격 피해 +50%, 지속 회복, 모든 상태 이상 면역."));
                if (c.BlessLeft > 0f) Put(ref n, "buff_bless", L.F("축복 {0}", Sec(c.BlessLeft)), L.T("축복의 연결"), L.F("공격 피해 +{0}%.", c.BlessPercent));
                if (c.HotLeft > 0f) Put(ref n, "buff_regen", L.F("재생 {0}", Sec(c.HotLeft)), L.T("재생"), L.T("체력 지속 회복."));
                if (p.Data.ScrollLeft > 0f) Put(ref n, "buff_power", L.F("투지 {0}분", Mathf.CeilToInt(p.Data.ScrollLeft / 60f)), L.T("투지의 주문서"), L.T("공격 피해 +15%.")); // [CASH] 투지의 주문서
                if (c.Cursed) Put(ref n, "buff_curse", L.T("저주"), L.T("저주"), L.T("정화의 종·수호의 맹세로 해제."));
                if (Time.time < c.SlowUntil) Put(ref n, "buff_slow", L.F("둔화 {0}", Sec(c.SlowUntil - Time.time)), L.T("둔화"), L.T("이동 속도 -40%."));
            }
            for (int i = n; i < slots.Count; i++)
                if (slots[i].back.gameObject.activeSelf) slots[i].back.gameObject.SetActive(false);
            // The explained buff ended or moved to another chip: follow it, or close.
            if (open >= 0)
            {
                int at = titles.IndexOf(openTitle);
                if (at < 0 || at >= n) Close();
                else if (at != open) { open = at; tip.anchoredPosition = new Vector2(at * (ChipWidth + Gap), Height + 6f); }
                else if (tipText.text != $"<b>{titles[at]}</b>\n{descs[at]}") SetTip($"<b>{titles[at]}</b>\n{descs[at]}");
            }
        }

        void Put(ref int n, string sprite, string label, string title, string desc)
        {
            if (n >= slots.Count) return;
            int i = n++;
            var s = slots[i];
            if (!s.back.gameObject.activeSelf) s.back.gameObject.SetActive(true);
            s.icon.sprite = Game.Art.Get(sprite);
            if (s.label.text != label) s.label.text = label;
            titles[i] = title;
            descs[i] = desc;
        }
    }
}
