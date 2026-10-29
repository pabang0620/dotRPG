using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [GEM AUTO 2026-10-29] 간편 젬 세팅 on the right of the gem page (the 젬 안내 shares the column; the button in the
    /// bar under the rows switches them). Three presets, three sliders that always sum to 100%, 자동 구성, and a summary.
    /// The math is GemAutoSetup (Docs/PLAN_GEM_AUTOSETUP_2026-10-29.md); the gems go in through Progression.SetSupports
    /// and the usual autosave, so the server checks them like any other socket change.
    /// </summary>
    public partial class SkillScreen
    {
        const string AutoWeightsKey = "gem_auto_weights";
        static readonly string[] NoDamageEffects = { "guard", "oath", "bless", "counter", "frenzy", "barrier", "venom", "stealth", "clone", "sanctuary" };
        static readonly Color32[] AxisColors = { new Color32(232, 92, 92, 255), new Color32(255, 159, 67, 255), new Color32(110, 180, 255, 255) };

        RectTransform autoPanel, guidePanel;
        readonly Slider[] autoSliders = new Slider[3];
        readonly Text[] autoValues = new Text[3];
        Text autoSummary, autoToggleText;
        int[] autoWeights = { 30, 40, 30 };
        bool autoSyncing, showGuide;

        void BuildAutoGem()
        {
            LoadAutoWeights();
            guidePanel = gemPage.Find("GemGuide") as RectTransform;
            autoPanel = Panel(gemPage, "GemAuto", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(GuideX, 0f), new Vector2(GuideWidth, 538f), UiTheme.PanelDeep).rectTransform;
            var t = autoPanel.transform;
            Label(t, "Title", L.T("<b>간편 젬 세팅</b>"), 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(GuideWidth - 28f, 28f));
            var intro = Label(t, "Intro", L.T("원하는 스타일만 고르면 장착한 스킬에 보조 젬을 계산해서 가장 좋게 끼웁니다."), 14, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -38f), new Vector2(GuideWidth - 28f, 40f));
            intro.color = new Color32(184, 196, 216, 255);
            string[] presets = { L.T("흡혈 세팅"), L.T("범위 세팅"), L.T("타격 세팅") };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Button(t, "Preset" + i, presets[i], "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + i * 128f, -84f), new Vector2(122f, 40f), () => ApplyPreset(index), 16);
            }
            string[] axes = { L.T("흡혈"), L.T("범위"), L.T("타격") };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                float y = -138f - i * 44f;
                var name = Label(t, "AxisName" + i, $"<b>{axes[i]}</b>", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, y), new Vector2(70f, 30f), TextAnchor.MiddleLeft);
                name.color = AxisColors[i];
                autoSliders[i] = AutoSlider(t, "Slider" + i, new Vector2(88f, y - 3f), 236f, AxisColors[i]);
                autoSliders[i].onValueChanged.AddListener(v => OnAutoSlider(index, Mathf.RoundToInt(v)));
                autoValues[i] = Label(t, "AxisValue" + i, "", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(332f, y), new Vector2(62f, 30f), TextAnchor.MiddleRight);
            }
            Button(t, "AutoApply", L.T("자동 구성"), "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -276f), new Vector2(GuideWidth - 28f, 46f), ApplyAutoGem, 20);
            autoSummary = Label(t, "Summary", "", 15, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -332f), new Vector2(GuideWidth - 28f, 84f));
            autoSummary.horizontalOverflow = HorizontalWrapMode.Wrap;
            autoSummary.verticalOverflow = VerticalWrapMode.Truncate;
            autoSummary.resizeTextForBestFit = true;
            autoSummary.resizeTextMinSize = 11;
            autoSummary.resizeTextMaxSize = 15;
            var note = Label(t, "Note", L.T("수치는 지금 스킬로 낼 수 있는 최대치 대비입니다. 흡혈·연속 시전·보스 사냥은 기본 스킬에만 효과가 있고, 전직 스킬은 피해·범위·효율만 적용됩니다. 초당 MP는 기본 자동 장착(피해 + 범위)보다 더 쓰지 않습니다. 보조 칸은 왼쪽에서 직접 바꿀 수도 있습니다."),
                13, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -422f), new Vector2(GuideWidth - 28f, 108f));
            note.color = new Color32(140, 150, 168, 255);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            note.verticalOverflow = VerticalWrapMode.Truncate;
            note.resizeTextForBestFit = true;
            note.resizeTextMinSize = 10;
            note.resizeTextMaxSize = 13;
            // The switch between this panel and the 젬 안내 sits at the right end of the how-to bar under the rows.
            var bar = gemPage.Find("List");
            if (bar != null)
            {
                gemListText.rectTransform.sizeDelta = new Vector2(612f, gemListText.rectTransform.sizeDelta.y);
                var toggle = Button(bar, "GuideToggle", "", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(160f, 32f), () => { showGuide = !showGuide; Game.Audio.PlaySfx("select"); Refresh(); }, 15);
                autoToggleText = toggle.GetComponentInChildren<Text>();
            }
            SyncAutoSliders();
        }

        static Slider AutoSlider(Transform parent, string name, Vector2 pos, float width, Color fillColor)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, name), new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(width, 24f));
            var track = UIFactory.Image(root, "Track", Game.Art.Get("ui_white"), new Color(1f, 1f, 1f, .15f));
            UIFactory.Stretch(track.rectTransform, 0f, 8f, 0f, 8f);
            track.raycastTarget = true;
            var fillArea = UIFactory.Stretch(UIFactory.Rect(root, "FillArea"), 0f, 8f, 0f, 8f);
            var fill = UIFactory.Image(fillArea, "Fill", Game.Art.Get("ui_white"), fillColor);
            UIFactory.Stretch(fill.rectTransform);
            fill.raycastTarget = false;
            var handleArea = UIFactory.Stretch(UIFactory.Rect(root, "HandleArea"), 8f, 0f, 8f, 0f);
            var handle = UIFactory.Image(handleArea, "Handle", Game.Art.Get("ui_white"), Color.white);
            UIFactory.Stretch(handle.rectTransform);
            handle.rectTransform.sizeDelta = new Vector2(16f, 0f);
            handle.raycastTarget = true;
            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            return slider;
        }

        void OnAutoSlider(int index, int value)
        {
            if (autoSyncing || value == autoWeights[index]) return;
            autoWeights = GemAutoSetup.Rebalance(autoWeights, index, value);
            SaveAutoWeights();
            SyncAutoSliders();
            RefreshAutoGem();
        }

        void SyncAutoSliders()
        {
            autoSyncing = true;
            for (int i = 0; i < 3; i++)
            {
                if (autoSliders[i] != null) autoSliders[i].SetValueWithoutNotify(autoWeights[i]);
                if (autoValues[i] != null) autoValues[i].text = $"{autoWeights[i]}%";
            }
            autoSyncing = false;
        }

        void ApplyPreset(int index)
        {
            autoWeights = (int[])GemAutoSetup.Presets[index].Clone();
            SaveAutoWeights();
            SyncAutoSliders();
            ApplyAutoGem();
        }

        // ---------- Inputs from the real skill numbers ----------

        static List<GemAutoSetup.Gem> AutoGems(Progression p)
        {
            var list = new List<GemAutoSetup.Gem>();
            foreach (var g in SkillGems.All)
                if (g.kind == GemKind.Support && p.IsUnlocked(g))
                    list.Add(new GemAutoSetup.Gem { id = g.id, moreDamage = g.moreDamage, moreAoe = g.moreAoe, manaMult = g.manaMult, repeats = g.repeats, leechPct = g.leechPct, bossPct = g.bossDamage });
            return list;
        }

        /// <summary>Every open key with a skill, from CharacterStatsCalc.Skill with no supports (rank, spec, tree and gear included).</summary>
        static List<GemAutoSetup.Skill> AutoSkills(Progression p, List<GemAutoSetup.Gem> gems)
        {
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            var stats = CharacterData.Session.Stats;
            var list = new List<GemAutoSetup.Skill>();
            for (int slot = 0; slot < SkillGems.Slots; slot++)
            {
                var active = p.IsSlotOpen(slot) ? p.Active(slot) : null;
                if (active == null) continue;
                var n = stats.Skill(cls, slot, active, System.Array.Empty<SkillGem>());
                var career = CareerCatalog.Get(active.id);
                var s = new GemAutoSetup.Skill
                {
                    slot = slot, damage = n.damage, hits = Mathf.Max(1, n.hits), repeats = n.repeats, cooldown = n.cooldown, radius = n.radius,
                    mpCost = n.usesLife ? 0f : n.manaCost, current1 = p.SlotGem(slot, 1), current2 = p.SlotGem(slot, 2),
                };
                if (career != null)
                {
                    // Progression.Supports: a career skill takes only 피해 / 범위 / 효율 (흡혈, 연속 시전, 보스 사냥 do nothing there).
                    s.repeats = 0;
                    s.heal = career.effect == "heal" || career.effect == "bloom" || career.effect == "cleanse";
                    s.noDamage = !s.heal && (career.power <= 0f || System.Array.IndexOf(NoDamageEffects, career.effect) >= 0);
                    if (career.effect == "storm") { s.maxTargets = s.hits; s.hits = 1; } // 연쇄전격: one hit on each of up to N monsters
                }
                else
                {
                    s.noDamage = active.damageMult <= 0f || n.guardPct > 0;
                    if (active.id == "leap") s.maxTargets = SkillCaster.LeapMaxTargets;
                }
                foreach (var g in gems)
                    if (career == null || g.id == "sup_dmg" || g.id == "sup_aoe" || g.id == "sup_eff") s.allowed.Add(g.id);
                list.Add(s);
            }
            return list;
        }

        static GemAutoSetup.Result SolveAuto(Progression p, int[] weights)
        {
            var gems = AutoGems(p);
            var skills = AutoSkills(p, gems);
            if (skills.Count == 0) return null;
            bool fillAll = Game.Settings != null && Game.Settings.Data.autoGem; // auto-socket would fill an empty socket anyway
            return GemAutoSetup.Solve(skills, gems, weights[0], weights[1], weights[2], fillAll);
        }

        void ApplyAutoGem()
        {
            if (!ContentUnlocks.Check(ContentUnlocks.Feature.Gem)) return;
            var p = Game.Session.Progression;
            var r = SolveAuto(p, autoWeights);
            if (r == null || AutoGems(p).Count == 0)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast(r == null ? L.T("장착한 스킬이 없습니다.") : L.T("사용할 수 있는 보조 젬이 없습니다. (Lv.3부터 해금)"));
                return;
            }
            int changed = 0;
            foreach (var plan in r.plans)
            {
                if (p.SlotGem(plan.slot, 1) == plan.gem1 && p.SlotGem(plan.slot, 2) == plan.gem2) continue;
                if (p.SetSupports(plan.slot, plan.gem1, plan.gem2)) changed++;
            }
            Game.Flow.Autosave();
            Game.Audio.PlaySfx(changed > 0 ? "confirm" : "select");
            GameEvents.RaiseToast(changed > 0 ? L.F("간편 젬 세팅: 스킬 {0}개의 보조 젬을 바꿨습니다.", changed) : L.T("간편 젬 세팅: 이미 가장 좋은 구성입니다."));
            Refresh();
        }

        // ---------- Summary ----------

        static string AxisLine(System.Func<GemAutoSetup.Axis, int> pct)
        {
            int s = pct(GemAutoSetup.Axis.Sustain), a = pct(GemAutoSetup.Axis.Aoe), t = pct(GemAutoSetup.Axis.Single);
            var parts = new List<string>
            {
                s < 0 ? L.T("흡혈 -") : L.F("흡혈 {0}%", s),
                a < 0 ? L.T("범위 -") : L.F("범위 {0}%", a),
                t < 0 ? L.T("타격 -") : L.F("타격 {0}%", t),
            };
            return string.Join(" · ", parts);
        }

        void RefreshAutoGem()
        {
            if (autoPanel == null) return;
            autoPanel.gameObject.SetActive(!showGuide);
            if (guidePanel != null) guidePanel.gameObject.SetActive(showGuide);
            if (autoToggleText != null) autoToggleText.text = showGuide ? L.T("간편 젬 세팅") : L.T("젬 안내 보기");
            if (showGuide) return;
            var p = Game.Session.Progression;
            var r = SolveAuto(p, autoWeights);
            if (r == null) { autoSummary.text = L.T("<color=#8c96a8>장착한 스킬이 없습니다.</color>"); return; }
            bool same = true;
            foreach (var plan in r.plans)
                if (!(p.SlotGem(plan.slot, 1) == plan.gem1 && p.SlotGem(plan.slot, 2) == plan.gem2) && !(p.SlotGem(plan.slot, 1) == plan.gem2 && p.SlotGem(plan.slot, 2) == plan.gem1)) same = false;
            string now = AxisLine(r.CurrentPercent);
            string best = AxisLine(r.Percent);
            string mp = r.mpBudget > 0f ? L.F("\n초당 MP: 기본 자동 장착의 {0}%", Mathf.RoundToInt(100f * r.mpUsed / r.mpBudget)) : "";
            autoSummary.text = same
                ? L.F("<color=#8fe28f><b>{0}</b> (최적)</color>{1}", best, mp)
                : L.F("지금: {0}\n<color=#ffe066>자동 구성하면: <b>{1}</b> (최적)</color>{2}", now, best, mp);
        }

        // ---------- Saved slider mix (this device) ----------

        void LoadAutoWeights()
        {
            try
            {
                var parts = PlayerPrefs.GetString(AutoWeightsKey, "").Split(',');
                if (parts.Length != 3) return;
                var w = new int[3];
                for (int i = 0; i < 3; i++) if (!int.TryParse(parts[i], out w[i]) || w[i] < 0) return;
                if (w[0] + w[1] + w[2] == 100) autoWeights = w;
            }
            catch (System.Exception) { }
        }

        void SaveAutoWeights()
        {
            try { PlayerPrefs.SetString(AutoWeightsKey, string.Join(",", autoWeights)); PlayerPrefs.Save(); }
            catch (System.Exception) { }
        }
    }
}
