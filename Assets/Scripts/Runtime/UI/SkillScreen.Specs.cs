using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [CH3 SPEC] 특화 tab (Docs/ch3/SKILL_SPECIALIZATION.md 5 UI): points left, one row per skill the hero may specialize
    /// with its A (crimson) and B (silver) card; clicking the chosen side again clears it. Changes only in a town; the tab
    /// is hidden while chapter 3 is closed.
    /// </summary>
    public partial class SkillScreen
    {
        const int SpecRows = 16;
        const float SpecRowH = 31f;

        /// <summary>[CH3 step 5] Puts the window on the 특화 tab (the 전직 tab while chapter 3 is closed); the caller opens it.</summary>
        public void SelectSpecsTab() => tab = Chapter3.Enabled ? TabId.Specs : TabId.Tree;

        /// <summary>[CH3 step 5] Opens the skill window on the 특화 tab (이츠나·익시 "특화 조정", the 할 일 line).</summary>
        public void ShowSpecs()
        {
            SelectSpecsTab();
            Game.Flow.OpenWindow(this);
        }

        RectTransform specPage;
        RectTransform specList;
        Text specHeader, specInfo;
        sealed class SpecRow { public Image bg, icon, frame; public Text name; public Button a, b; public Text aText, bText, cost; public string skill; }
        readonly List<SpecRow> specRows = new List<SpecRow>();
        string specHover;

        void BuildSpecs()
        {
            specPage = UIFactory.Stretch(UIFactory.Rect(content, "SpecPage"), 0f, 0f, 0f, 56f);
            specHeader = Label(specPage, "Header", "", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(800f, 30f), TextAnchor.MiddleLeft);
            specList = SkillScroll(specPage, "SpecViewport", new Vector2(0f, -34f), new Vector2(800f, 500f), SpecRows * (SpecRowH + 2f));
            for (int i = 0; i < SpecRows; i++)
            {
                var row = new SpecRow();
                float y = -i * (SpecRowH + 2f);
                row.bg = Panel(specList, "SpecRow" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(800f, SpecRowH), UiTheme.Panel);
                // [CH3 SPEC] Skill icon, framed crimson (A) or silver (B) once a side is chosen.
                row.icon = UIFactory.Image(row.bg.transform, "Icon", null, Color.white);
                UIFactory.Place(row.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(SpecRowH - 6f, SpecRowH - 6f));
                row.frame = UIFactory.Image(row.bg.transform, "Frame", null, Color.white);
                UIFactory.Place(row.frame.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(SpecRowH - 2f, SpecRowH - 2f));
                row.name = Label(row.bg.transform, "Name", "", 14, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(120f, SpecRowH), TextAnchor.MiddleLeft);
                var r = row;
                row.a = Button(row.bg.transform, "A", "", "ui_btngray", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(162f, 0f), new Vector2(286f, SpecRowH - 3f), () => PickSpec(r, 'a'), 13);
                row.b = Button(row.bg.transform, "B", "", "ui_btngray", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(454f, 0f), new Vector2(286f, SpecRowH - 3f), () => PickSpec(r, 'b'), 13);
                row.aText = row.a.GetComponentInChildren<Text>();
                row.bText = row.b.GetComponentInChildren<Text>();
                foreach (var label in new[] { row.name, row.aText, row.bText })
                {
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    label.verticalOverflow = VerticalWrapMode.Truncate;
                    label.resizeTextForBestFit = true;
                    label.resizeTextMinSize = UIFactory.ReadableFloor; // [UI 2026-10-30] was 10
                    label.resizeTextMaxSize = label.fontSize;
                }
                HoverSpec(row.a.gameObject, r, 'a');
                HoverSpec(row.b.gameObject, r, 'b');
                row.cost = Label(row.bg.transform, "Cost", "", 13, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(50f, SpecRowH), TextAnchor.MiddleRight);
                specRows.Add(row);
            }
            var info = Panel(specPage, "SpecInfo", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(812f, 0f), new Vector2(408f, 534f), UiTheme.PanelDeep);
            var infoContent = SkillScroll(info.transform, "Viewport", new Vector2(14f, -14f), new Vector2(380f, 506f), 506f);
            specInfo = Label(infoContent, "Text", "", UiTheme.FontMin, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(380f, 506f), TextAnchor.UpperLeft);
            specInfo.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            infoContent.parent.GetComponent<ScrollRect>().content = specInfo.rectTransform;
        }

        void HoverSpec(GameObject go, SpecRow row, char side)
        {
            var relay = go.AddComponent<PointerRelay>();
            relay.onEnter = () => { var s = SkillSpecs.Find(row.skill, side); specHover = s == null ? null : $"<b>{s.name}</b>\n\n{s.text}"; RefreshSpecInfo(); };
            relay.onExit = () => { specHover = null; RefreshSpecInfo(); };
        }

        /// <summary>The skills this hero may specialize: the class's base five, then the career's (awakening once awakened).</summary>
        static List<string> SpecSkills(Progression prog)
        {
            var list = new List<string>();
            foreach (var id in SkillSpecs.BaseOf(prog.Class)) list.Add(id);
            if (prog.IsPromoted)
                foreach (var c in CareerCatalog.For(prog.Career))
                    if (prog.SpecAllowed(c.id)) list.Add(c.id);
            return list;
        }

        static string SkillName(string id) => CareerCatalog.Get(id)?.name ?? (Game.Session?.Progression?.Shown(SkillGems.Get(id)) ?? SkillGems.Get(id))?.name ?? id; // [NINJA] thrown names

        static string SkillIcon(string id) => CareerMoves.IconKey(CareerCatalog.Get(id)?.Icon ?? SkillGems.Get(id)?.icon);

        void PickSpec(SpecRow row, char side)
        {
            var prog = Game.Session.Progression;
            if (string.IsNullOrEmpty(row.skill)) return;
            char now = prog.SpecOf(row.skill);
            string why = prog.SetSpec(row.skill, now == side ? '\0' : side);
            if (why != null) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(why); }
            else Game.Audio.PlaySfx("select");
            RefreshSpecs();
        }

        void RefreshSpecs()
        {
            if (specPage == null) return;
            var prog = Game.Session.Progression;
            var skills = SpecSkills(prog);
            specList.sizeDelta = new Vector2(800f, Mathf.Min(skills.Count, SpecRows) * (SpecRowH + 2f));
            bool here = Progression.SpecEditPlace();
            specHeader.text = L.F("특화 포인트 <color=#ffe066>{0}</color> / {1}   ·   액티브 {2}점 · 패시브 {3}점", prog.SpecPointsLeft, prog.SpecPoints, SkillSpecs.ActiveCost, SkillSpecs.PassiveCost)
                + (here ? "" : L.T("   <color=#ff9f7a>마을에서 바꿀 수 있어요</color>"));
            for (int i = 0; i < specRows.Count; i++)
            {
                var row = specRows[i];
                bool on = i < skills.Count;
                row.bg.gameObject.SetActive(on);
                if (!on) { row.skill = null; continue; }
                row.skill = skills[i];
                row.name.text = SkillName(row.skill);
                char side = prog.SpecOf(row.skill);
                string icon = SkillIcon(row.skill);
                row.icon.sprite = icon != null ? Game.Art.Get(icon) : null;
                row.icon.enabled = icon != null;
                row.frame.sprite = SkillBarView.SpecFrame(side);
                row.frame.enabled = row.frame.sprite != null;
                var sa = SkillSpecs.Find(row.skill, 'a');
                var sb = SkillSpecs.Find(row.skill, 'b');
                row.aText.text = sa == null ? "-" : (side == 'a' ? "<b>A  " + sa.name + "</b>" : "A  " + sa.name);
                row.bText.text = sb == null ? "-" : (side == 'b' ? "<b>B  " + sb.name + "</b>" : "B  " + sb.name);
                row.aText.color = side == 'a' ? SkillSpecs.ColorA : new Color(0.75f, 0.75f, 0.8f);
                row.bText.color = side == 'b' ? SkillSpecs.ColorB : new Color(0.75f, 0.75f, 0.8f);
                row.a.interactable = here && sa != null;
                row.b.interactable = here && sb != null;
                row.cost.text = L.F("{0}점", SkillSpecs.Cost(row.skill));
            }
            RefreshSpecInfo();
        }

        void RefreshSpecInfo()
        {
            if (specInfo == null) return;
            specInfo.text = specHover ?? L.T("<color=#8c96a8>카드에 올리면 설명이 나옵니다. 고른 쪽을 한 번 더 누르면 해제됩니다. 진홍은 A, 은백은 B입니다.</color>");
        }
    }
}
