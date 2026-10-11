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
        const int MaxEntries = 8;

        sealed class GuideEntry { public SkillGem gem; public Image bg, icon; public Text title, body; }
        readonly List<GuideEntry> guideEntries = new List<GuideEntry>();
        readonly Image[] guideTabBg = new Image[2];
        bool guideSkills; // false = 보조 젬, true = 스킬 젬

        void BuildGemGuide()
        {
            var panel = Panel(gemPage, "GemGuide", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(GuideX, 0f), new Vector2(GuideWidth, 538f), UiTheme.PanelDeep);
            Label(panel.transform, "Title", "<b>젬 안내</b>", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(GuideWidth - 28f, 28f));
            var intro = Label(panel.transform, "Intro",
                "<color=#ff9f43>스킬 젬</color>  키(Q W E R T)마다 하나씩 들어가는 기술. 레벨이 오르면 칸이 열리고, 전직하면 전직 기술도 넣을 수 있다.\n" +
                "<color=#8fe28f>보조 젬</color>  스킬 옆 보조 칸 2개에 끼워 <b>그 스킬만</b> 바꾼다(피해·범위·연속 발동·흡혈 등).\n" +
                "<color=#ffe066>끼우는 법</color>  왼쪽 보조 칸 클릭 = 다음 젬, 우클릭 = 이전 젬.\n" +
                "<color=#8c96a8>보조 젬의 MP 증가는 서로 곱해진다. 두 개를 끼우면 MP가 크게 늘어난다.</color>",
                14, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -38f), new Vector2(GuideWidth - 28f, 118f));
            intro.lineSpacing = 1.1f;
            string[] tabs = { "보조 젬 목록", "스킬 젬 목록" };
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var bg = Panel(panel.transform, "GuideTab" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + i * 192f, -160f), new Vector2(186f, 30f), Color.clear);
                bg.raycastTarget = true;
                bg.gameObject.AddComponent<PointerRelay>().onClick = _ => { guideSkills = index == 1; Game.Audio.PlaySfx("select"); Refresh(); };
                Label(bg.transform, "Text", tabs[i], 16, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(186f, 30f), TextAnchor.MiddleCenter);
                guideTabBg[i] = bg;
            }
            for (int i = 0; i < MaxEntries; i++)
            {
                var e = new GuideEntry();
                e.bg = Panel(panel.transform, "Gem" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -196f - i * (EntryHeight + 2f)), new Vector2(GuideWidth - 20f, EntryHeight), UiTheme.Panel);
                e.bg.raycastTarget = true;
                e.icon = UIFactory.SharpIcon(e.bg.transform, "Icon", Color.white);
                UIFactory.Place(e.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(42f, 42f));
                e.title = Label(e.bg.transform, "Title", "", 15, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, -3f), new Vector2(GuideWidth - 84f, 20f));
                e.body = Label(e.bg.transform, "Body", "", 13, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, -22f), new Vector2(GuideWidth - 84f, 34f));
                e.body.lineSpacing = 1f;
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
            if (g.moreDamage != 0) parts.Add(g.moreDamage > 0 ? $"피해 +{g.moreDamage}%" : $"피해 {g.moreDamage}%");
            if (g.moreAoe != 0) parts.Add($"범위 +{g.moreAoe}%");
            if (g.repeats > 0) parts.Add($"한 번 더 발동(+{g.repeats}회)");
            if (g.extraChains > 0) parts.Add($"연쇄 +{g.extraChains}");
            if (g.leechPct > 0) parts.Add($"준 피해의 {g.leechPct}% HP 회복");
            if (g.bossDamage > 0) parts.Add($"보스 피해 +{g.bossDamage}%");
            if (Mathf.Abs(g.manaMult - 1f) > 0.001f)
            {
                int pct = Mathf.RoundToInt((g.manaMult - 1f) * 100f);
                parts.Add(pct > 0 ? $"<color=#ff9f7a>MP 소모 +{pct}%</color>" : $"<color=#8fe28f>MP 소모 {pct}%</color>");
            }
            return string.Join(" · ", parts);
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
            return keys.Count > 0 ? "장착: " + string.Join(", ", keys) : "";
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
            for (int i = 0; i < guideEntries.Count; i++)
            {
                var e = guideEntries[i];
                var g = i < gems.Count ? gems[i] : null;
                e.gem = g;
                e.bg.gameObject.SetActive(g != null);
                if (g == null) continue;
                bool open = g.kind == GemKind.Active ? (g.IsUltimate ? prog.Awakened : prog.IsSlotOpen(g.slot)) : prog.IsUnlocked(g);
                e.icon.sprite = Game.Art.Get(g.icon);
                e.icon.color = open ? Color.white : new Color(.45f, .45f, .5f, 1f);
                string worn = WornOn(prog, g);
                string locked = g.IsUltimate ? "전직 각성 후" : $"Lv.{g.unlockLevel} 해금";
                string state = open ? (worn.Length > 0 ? $"<color=#8fe28f>{worn}</color>" : "<color=#b8c4d8>사용 가능</color>") : $"<color=#ff9f43>{locked}</color>";
                string key = g.kind == GemKind.Active ? (g.IsUltimate ? "각성" : $"{g.slot + 1}번") + " · " : "";
                e.title.text = $"<b>{g.name}</b>  <size=13>{key}{state}</size>";
                e.body.text = g.kind == GemKind.Support
                    ? $"{GemEffect(g)}\n<color=#8c96a8>{g.tip}</color>"
                    : $"<color=#b8c4d8>{Short(g.description, 46)}</color>";
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
                sb.Append($"<b>{g.name}</b>  <color=#8fe28f>보조 젬</color>\n\n{g.description}\n\n<color=#ffe066>효과</color>  {GemEffect(g)}\n");
                if (!string.IsNullOrEmpty(g.tip)) sb.Append($"<color=#ffe066>추천</color>  {g.tip}\n");
                sb.Append(prog.IsUnlocked(g) ? "\n<color=#8fe28f>사용 가능</color> · 스킬 옆 보조 칸을 클릭해 끼운다." : $"\n<color=#ff9f43>Lv.{g.unlockLevel}에 해금</color>");
            }
            else
            {
                sb.Append($"<b>{g.name}</b>  {(g.IsUltimate ? "<color=#ffd66e>각성 기술</color>" : $"<color=#ff9f43>{g.slot + 1}번 스킬</color>")}\n\n{g.description}\n\n");
                sb.Append($"기본 피해 배율 {g.damageMult * 100:0}%{(g.hits > 1 ? $" × {g.hits}회" : "")}   MP {g.manaCost}   재사용 {g.cooldown:0.#}초");
                if (g.stun > 0) sb.Append($"   기절 {g.stun:0.#}초");
                if (g.freeze > 0) sb.Append($"   빙결 {g.freeze:0.#}초");
                sb.Append(prog.IsSlotOpen(g.slot) ? "\n\n<color=#8fe28f>사용 가능</color>" : $"\n\n<color=#ff9f43>Lv.{g.unlockLevel}에 칸이 열린다</color>");
            }
            string worn = WornOn(prog, g);
            if (worn.Length > 0) sb.Append($"   <color=#8fe28f>{worn}</color>");
            SetTooltip(sb.ToString());
        }
    }
}
