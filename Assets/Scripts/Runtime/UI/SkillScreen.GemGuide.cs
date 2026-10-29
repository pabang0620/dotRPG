using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] Gem guide on the right of the gem page: what skill gems and support gems are, how to socket them, and a
    /// catalog of every gem (icon, unlock level, effect from its numbers, where it pays off). Hover = full tooltip.
    /// </summary>
    public partial class SkillScreen
    {
        const float GuideX = 812f, GuideWidth = 408f, EntryHeight = 58f;
        const int MaxEntries = 32; // [UI 2026-10-29] was 8: longer lists (rogue base + career skill gems) were cut off

        sealed class GuideEntry { public SkillGem gem; public Image bg, icon; public Text title, body; }
        readonly List<GuideEntry> guideEntries = new List<GuideEntry>();
        readonly Image[] guideTabBg = new Image[2];
        bool guideSkills; // false = 보조 젬, true = 스킬 젬
        RectTransform guideList;

        static RectTransform SkillScroll(Transform parent, string name, Vector2 position, Vector2 size, float height)
        {
            var viewport = UIFactory.Place(UIFactory.Rect(parent, name), new Vector2(0, 1), new Vector2(0, 1), position, size);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var items = UIFactory.Place(UIFactory.Rect(viewport, "Content"), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(size.x, height));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = items; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
            // [UI 2026-10-29] A visible bar on the right edge while the list is longer than its view.
            var track = UIFactory.Image(viewport, "ScrollTrack", null, new Color(1f, 1f, 1f, .15f));
            UIFactory.Place(track.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(6f, size.y));
            var handle = UIFactory.Image(track.transform, "Handle", null, new Color32(184, 196, 216, 255));
            UIFactory.Stretch(handle.rectTransform);
            track.raycastTarget = true; handle.raycastTarget = true;
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return items;
        }

        void BuildGemGuide()
        {
            var panel = Panel(gemPage, "GemGuide", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(GuideX, 0f), new Vector2(GuideWidth, 538f), UiTheme.PanelDeep);
            Label(panel.transform, "Title", L.T("<b>젬 안내</b>"), 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(GuideWidth - 28f, 28f));
            var intro = Label(panel.transform, "Intro",
                L.T("<color=#ff9f43>스킬 젬</color>  키(Q W E R T)마다 하나씩 들어가는 기술. 레벨이 오르면 칸이 열리고, 전직하면 전직 기술도 넣을 수 있다.\n") +
                L.T("<color=#8fe28f>보조 젬</color>  스킬 옆 보조 칸 2개에 끼워 <b>그 스킬만</b> 바꾼다(피해·범위·연속 발동·흡혈 등).\n") +
                L.T("<color=#ffe066>끼우는 법</color>  왼쪽 보조 칸 클릭 = 다음 젬, 우클릭 = 이전 젬.\n") +
                L.T("<color=#8c96a8>보조 젬의 MP 증가는 서로 곱해진다. 두 개를 끼우면 MP가 크게 늘어난다.</color>"),
                14, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -38f), new Vector2(GuideWidth - 28f, 118f));
            intro.lineSpacing = 1.1f;
            // Localized instructions may need more lines; scroll them inside their own region.
            var introContent = SkillScroll(panel.transform, "IntroViewport", new Vector2(14f, -38f), new Vector2(GuideWidth - 28f, 142f), 142f);
            intro.transform.SetParent(introContent, false);
            UIFactory.Place(intro.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(GuideWidth - 28f, 142f));
            intro.horizontalOverflow = HorizontalWrapMode.Wrap;
            intro.verticalOverflow = VerticalWrapMode.Overflow;
            intro.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            introContent.parent.GetComponent<ScrollRect>().content = intro.rectTransform;
            string[] tabs = { L.T("보조 젬 목록"), L.T("스킬 젬 목록") };
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var bg = Panel(panel.transform, "GuideTab" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + i * 192f, -186f), new Vector2(186f, 30f), Color.clear);
                bg.raycastTarget = true;
                bg.gameObject.AddComponent<PointerRelay>().onClick = _ => { guideSkills = index == 1; Game.Audio.PlaySfx("select"); Refresh(); };
                Label(bg.transform, "Text", tabs[i], 16, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(186f, 30f), TextAnchor.MiddleCenter);
                guideTabBg[i] = bg;
            }
            guideList = SkillScroll(panel.transform, "GuideViewport", new Vector2(10f, -222f), new Vector2(GuideWidth - 20f, 306f), MaxEntries * (EntryHeight + 2f));
            for (int i = 0; i < MaxEntries; i++)
            {
                var e = new GuideEntry();
                e.bg = Panel(guideList, "Gem" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * (EntryHeight + 2f)), new Vector2(GuideWidth - 20f, EntryHeight), UiTheme.Panel);
                e.bg.raycastTarget = true;
                e.icon = UIFactory.SharpIcon(e.bg.transform, "Icon", Color.white);
                UIFactory.Place(e.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(42f, 42f));
                e.title = Label(e.bg.transform, "Title", "", 15, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, -3f), new Vector2(GuideWidth - 80f, 20f));
                e.body = Label(e.bg.transform, "Body", "", 13, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, -22f), new Vector2(GuideWidth - 80f, EntryHeight - 24f));
                e.body.lineSpacing = 1f;
                // [UI 2026-10-14] One line each: a long name or effect shrinks to fit instead of running into the next row.
                foreach (var t in new[] { e.title, e.body })
                {
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    t.verticalOverflow = VerticalWrapMode.Truncate;
                    t.resizeTextForBestFit = true;
                    t.resizeTextMinSize = 10;
                    t.resizeTextMaxSize = t.fontSize;
                }
                var view = e;
                var relay = e.bg.gameObject.AddComponent<PointerRelay>();
                relay.onEnter = () => { hovered = view; mouse = true; };
                relay.onExit = () => { if (hovered == view) hovered = null; };
                guideEntries.Add(e);
            }
        }

        /// <summary>Korean effect line from a support gem's numbers ("피해 +35% · MP 소모 +30%").</summary>
        static string GemEffect(SkillGem g)
        {
            var parts = new List<string>();
            if (g.moreDamage != 0) parts.Add(g.moreDamage > 0 ? L.F("피해 +{0}%", g.moreDamage) : L.F("피해 {0}%", g.moreDamage));
            if (g.moreAoe != 0) parts.Add(L.F("범위 +{0}%", g.moreAoe));
            if (g.repeats > 0) parts.Add(L.F("한 번 더 발동(+{0}회)", g.repeats));
            if (g.extraChains > 0) parts.Add(L.F("연쇄 +{0}", g.extraChains));
            if (g.leechPct > 0) parts.Add(L.F("준 피해의 {0}% HP 회복", g.leechPct));
            if (g.bossDamage > 0) parts.Add(L.F("보스 피해 +{0}%", g.bossDamage));
            if (Mathf.Abs(g.manaMult - 1f) > 0.001f)
            {
                int pct = Mathf.RoundToInt((g.manaMult - 1f) * 100f);
                parts.Add(pct > 0 ? L.F("<color=#ff9f7a>MP 소모 +{0}%</color>", pct) : L.F("<color=#8fe28f>MP 소모 {0}%</color>", pct));
            }
            return string.Join(" · ", parts);
        }

        /// <summary>[UI] The equipped skills this support gem can go next to ("Q 회전 베기, W 파쇄 일격"), or empty.</summary>
        static string FitsOn(Progression prog, SkillGem g)
        {
            var list = new List<string>();
            for (int s = 0; s < SkillGems.Slots; s++)
            {
                var active = prog.IsSlotOpen(s) ? prog.Active(s) : null;
                if (active == null) continue;
                if (prog.OptionsFor(s, 1).Contains(g.id)) list.Add($"{Game.Input.GetBindingLabel(SkillGems.ActionFor(s))} {L.T(active.name)}");
            }
            return string.Join(", ", list);
        }

        /// <summary>The key a gem sits in right now ("Q 회전 베기"), or empty.</summary>
        static string WornOn(Progression prog, SkillGem g)
        {
            var keys = new List<string>();
            for (int s = 0; s < SkillGems.Slots; s++)
            {
                bool on = g.kind == GemKind.Active ? prog.Active(s)?.id == g.id : prog.SlotGem(s, 1) == g.id || prog.SlotGem(s, 2) == g.id;
                if (on) keys.Add(Game.Input.GetBindingLabel(SkillGems.ActionFor(s)));
            }
            return keys.Count > 0 ? L.F("장착: {0}", string.Join(", ", keys)) : "";
        }

        List<SkillGem> GuideGems(CharacterClass cls)
        {
            var list = new List<SkillGem>();
            foreach (var g in SkillGems.All)
                if (guideSkills ? g.kind == GemKind.Active && g.UsableBy(cls) : g.kind == GemKind.Support) list.Add(g);
            list.Sort((a, b) => guideSkills ? a.slot.CompareTo(b.slot) : a.unlockLevel.CompareTo(b.unlockLevel));
            return list;
        }

        void RefreshGemGuide()
        {
            if (guideEntries.Count == 0) return;
            var prog = Game.Session.Progression;
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            for (int i = 0; i < 2; i++)
            {
                guideTabBg[i].sprite = UiTheme.Tab(guideSkills == (i == 1));
                guideTabBg[i].type = Image.Type.Sliced;
                guideTabBg[i].color = Color.white;
            }
            var gems = GuideGems(cls);
            guideList.sizeDelta = new Vector2(guideList.sizeDelta.x, Mathf.Min(gems.Count, MaxEntries) * (EntryHeight + 2f));
            for (int i = 0; i < guideEntries.Count; i++)
            {
                var e = guideEntries[i];
                var g = i < gems.Count ? gems[i] : null;
                e.gem = g;
                e.bg.gameObject.SetActive(g != null);
                if (g == null) continue;
                bool open = g.kind == GemKind.Active ? (g.IsUltimate ? prog.Awakened : prog.IsUnlocked(g)) : prog.IsUnlocked(g); // [ROGUE] 허수아비 opens (Lv12) before its default key (A, Lv20)
                e.icon.sprite = Game.Art.Get(CareerMoves.IconKey(g.icon));
                e.icon.color = open ? Color.white : new Color(.45f, .45f, .5f, 1f);
                string worn = WornOn(prog, g);
                string locked = g.IsUltimate ? L.T("전직 각성 후") : L.F("Lv.{0} 해금", g.unlockLevel);
                string state = open ? (worn.Length > 0 ? $"<color=#8fe28f>{worn}</color>" : L.T("<color=#b8c4d8>사용 가능</color>")) : $"<color=#ff9f43>{locked}</color>";
                string key = g.kind == GemKind.Active ? (g.IsUltimate ? L.T("각성") : L.F("{0}번", g.slot + 1)) + " · " : "";
                e.title.text = $"<b>{L.T(g.name)}</b>  <size=13>{key}{state}</size>";
                e.body.text = g.kind == GemKind.Support
                    ? $"{GemEffect(g)}\n<color=#8c96a8>{Short(L.T(g.tip), 40)}</color>"
                    : $"<color=#b8c4d8>{Short(L.T(g.description), 46)}</color>";
            }
        }

        static string Short(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";

        void FillGuideTooltip(GuideEntry e)
        {
            var g = e.gem;
            if (g == null) { SetTooltip(""); return; }
            var prog = Game.Session.Progression;
            var sb = new StringBuilder();
            if (g.kind == GemKind.Support)
            {
                sb.Append(L.F("<b>{0}</b>  <color=#8fe28f>보조 젬</color>\n\n{1}\n\n<color=#ffe066>효과</color>  {2}\n", L.T(g.name), L.T(g.description), GemEffect(g)));
                if (!string.IsNullOrEmpty(g.tip)) sb.Append(L.F("<color=#ffe066>추천</color>  {0}\n", L.T(g.tip)));
                string fits = FitsOn(prog, g);
                if (fits.Length > 0) sb.Append(L.F("<color=#ffe066>끼울 수 있는 스킬</color>  {0}\n", fits));
                sb.Append(prog.IsUnlocked(g) ? L.T("\n<color=#8fe28f>사용 가능</color> · 스킬 옆 보조 칸을 클릭해 끼운다.") : L.F("\n<color=#ff9f43>Lv.{0}에 해금</color>", g.unlockLevel));
            }
            else
            {
                sb.Append(L.F("<b>{0}</b>  {1}\n\n{2}\n\n", L.T(g.name), (g.IsUltimate ? L.T("<color=#ffd66e>각성 기술</color>") : L.F("<color=#ff9f43>{0}번 스킬</color>", g.slot + 1)), L.T(g.description)));
                sb.Append(L.F("기본 피해 배율 {0:0}%{1}   MP {2}   재사용 {3:0.#}초", g.damageMult * 100, (g.hits > 1 ? L.F(" × {0}회", g.hits) : ""), g.manaCost, g.cooldown));
                if (g.stun > 0) sb.Append(L.F("   기절 {0:0.#}초", g.stun));
                if (g.freeze > 0) sb.Append(L.F("   빙결 {0:0.#}초", g.freeze));
                sb.Append(prog.IsUnlocked(g) ? L.T("\n\n<color=#8fe28f>사용 가능</color>") : L.F("\n\n<color=#ff9f43>Lv.{0}에 칸이 열린다</color>", g.unlockLevel));
            }
            string worn = WornOn(prog, g);
            if (worn.Length > 0) sb.Append($"   <color=#8fe28f>{worn}</color>");
            SetTooltip(sb.ToString());
        }
    }
}
