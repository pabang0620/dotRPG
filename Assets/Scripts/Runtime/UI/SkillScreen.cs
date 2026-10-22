using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Skill window (passive tree + skill gems):
    /// • 패시브 트리 - one point per level. The start sits in the middle; the left / right / down / up
    ///   arms strengthen skills 1-4 (area, damage and cooldown notables with stat nodes in between,
    ///   a mastery at the end). Keystones in the corners. Left click allocates, right click refunds.
    /// • 스킬 젬 - the five skill slots (the fifth is the awakening skill). Each slot opens at a level
    ///   with its skill; two support gems link into it. Click a support socket to cycle gems.
    /// </summary>
    public partial class SkillScreen : WindowScreen
    {
        enum TabId { Tree, Gems }

        // [SKILL 8] Eight compact rows (Q W E R A S D T) fit where five tall ones were (was 92 / 6).
        const float RowHeight = 58f, RowGap = 4f;

        TabId tab = TabId.Tree;
        RectTransform treePage, gemPage;
        readonly Image[] tabBg = new Image[2];

        // Gems.
        sealed class SocketView { public int slot, socket; public Image bg, icon; public Text label; }
        readonly List<SocketView> sockets = new List<SocketView>();
        readonly Text[] slotTitle = new Text[SkillGems.Slots];
        readonly Text[] slotInfo = new Text[SkillGems.Slots];
        readonly Image[] slotLock = new Image[SkillGems.Slots];
        readonly Text[] slotLockText = new Text[SkillGems.Slots];
        Text gemListText;
        int selectedSocket;

        // Tooltip.
        RectTransform tooltip;
        Text tooltipText;
        object hovered;
        bool mouse, keyboardUsed;
        Text tabKeyHint;

        public override void Show()
        {
            keyboardUsed = false;
            mouse = false;
            hovered = null;
            SkillStyleClient.Refresh(); // [STYLE] style 3 ownership
            base.Show();
        }

        public static SkillScreen Create(Transform canvas)
        {
            var w = CreateWindow<SkillScreen>(canvas, "Skills", L.T("스킬"), "menuicon_skill");
            w.BuildTabs();
            w.BuildCareer();
            w.BuildGems();
            w.BuildGemGuide();
            w.BuildTooltip();
            return w;
        }

        void BuildTabs()
        {
            string[] names = { L.T("전직 · 스킬 노드"), L.T("기본 · 장착 스킬") };
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var bg = Panel(content, "Tab" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * 210f, 0f), new Vector2(200f, 48f), Color.clear);
                bg.raycastTarget = true;
                bg.gameObject.AddComponent<PointerRelay>().onClick = _ => { tab = (TabId)index; Game.Audio.PlaySfx("select"); Refresh(); };
                Label(bg.transform, "Text", names[i], 24, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 48f), TextAnchor.MiddleCenter);
                tabBg[i] = bg;
            }
            tabKeyHint = Label(content, "TabHint", "", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(430f, 0f), new Vector2(500f, 48f), TextAnchor.MiddleLeft);

            treePage = UIFactory.Stretch(UIFactory.Rect(content, "TreePage"), 0f, 0f, 0f, 56f);
            gemPage = UIFactory.Stretch(UIFactory.Rect(content, "GemPage"), 0f, 0f, 0f, 56f);
        }

        // ================= Skill slots =================

        void BuildGems()
        {
            for (int s = 0; s < SkillGems.Slots; s++)
            {
                bool ult = s == SkillGems.UltimateSlot;
                int rowIndex = System.Array.IndexOf(SkillGems.BarOrder, s); // rows in key order: Q W E R A S D, then T
                var row = Panel(gemPage, "Slot" + s, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -rowIndex * (RowHeight + RowGap)), new Vector2(800f, RowHeight),
                    ult ? new Color32(52, 44, 30, 240) : UiTheme.Panel);
                slotTitle[s] = Label(row.transform, "Title", "", 16, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(84f, RowHeight - 4f), TextAnchor.MiddleCenter);
                // Link bar behind the three sockets.
                var link = Img(row.transform, "Link", "ui_white", new Color32(150, 120, 70, 255));
                UIFactory.Place(link.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(130f, 0f), new Vector2(130f, 5f));
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++)
                {
                    float size = k == 0 ? 52f : 44f;
                    float x = k == 0 ? 100f : 168f + (k - 1) * 52f;
                    var bg = Img(row.transform, $"Socket{s}_{k}", k == 0 ? "ui_slotblue" : "ui_slot", Color.white);
                    UIFactory.Place(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(size, size));
                    bg.raycastTarget = true;
                    var icon = UIFactory.SharpIcon(bg.transform, "Icon", Color.white);
                    UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.7f, size * 0.7f));
                    var label = UIFactory.Text(bg.transform, "Label", k == 0 ? L.T("스킬") : L.T("보조"), UiTheme.FontMin, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, true);
                    UIFactory.Stretch(label.rectTransform);
                    var view = new SocketView { slot = s, socket = k, bg = bg, icon = icon, label = label };
                    int index = sockets.Count;
                    var relay = bg.gameObject.AddComponent<PointerRelay>();
                    relay.onEnter = () => { hovered = view; mouse = true; };
                    relay.onExit = () => { if (hovered == view) hovered = null; };
                    relay.onClick = b => { selectedSocket = index; Cycle(view, b == PointerEventData.InputButton.Right ? -1 : 1); };
                    LongPress.Add(bg.gameObject, () => { selectedSocket = index; Cycle(view, -1); }); // touch: hold = right click
                    sockets.Add(view);
                }
                slotInfo[s] = Label(row.transform, "Info", "", 13, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(278f, 0f), new Vector2(512f, RowHeight - 2f), TextAnchor.MiddleLeft);
                slotInfo[s].lineSpacing = 1.0f;
                slotLock[s] = Img(row.transform, "Lock", "ui_white", new Color(0.02f, 0.03f, 0.06f, 0.55f));
                UIFactory.Stretch(slotLock[s].rectTransform);
                slotLockText[s] = Label(row.transform, "LockText", "", 16, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(360f, 34f), TextAnchor.MiddleRight);
            }
            // [UI] Under the rows: a one-line how-to (the full gem list and explanations are in the guide on the right).
            float listY = -SkillGems.Slots * (RowHeight + RowGap);
            var list = Panel(gemPage, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, listY), new Vector2(800f, 38f), UiTheme.PanelDeep);
            gemListText = Label(list.transform, "Text", "", UiTheme.FontMin, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(776f, 34f), TextAnchor.MiddleLeft);
        }

        void Cycle(SocketView v, int dir)
        {
            var prog = Game.Session.Progression;
            if (!prog.IsSlotOpen(v.slot))
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast(L.F("이 슬롯은 Lv.{0}에 열립니다.", Progression.SlotLevel(v.slot)));
                return;
            }

            if (v.socket > 0 && !ContentUnlocks.Check(ContentUnlocks.Feature.Gem)) return; // [UNLOCK] support gems (the skill socket stays open)
            var options = prog.OptionsFor(v.slot, v.socket);
            int i = options.IndexOf(prog.SlotGem(v.slot, v.socket));
            if (i < 0) i = 0;
            if (options.Count <= 1)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast(v.socket == 0 ? L.T("이 칸에 넣을 수 있는 다른 스킬이 없습니다.") : L.T("사용할 수 있는 보조 젬이 없습니다. (Lv.3부터 해금)"));
                return;
            }
            prog.SetGem(v.slot, v.socket, options[(i + dir + options.Count) % options.Count]);
            Game.Flow.Autosave();
            Game.Audio.PlaySfx("select");
            Refresh();
            FillTooltip(v);
        }

        /// <summary>"피해 +30% · 범위 +25%" from the passive tree for one slot (empty if nothing).</summary>
        static string TreeBonus(int slot)
        {
            var parts = new List<string>();
            int dmg = CharacterStats.SlotStat(PassiveStat.SkillDamage, slot);
            int area = CharacterStats.SlotStat(PassiveStat.SkillArea, slot);
            int cd = CharacterStats.SlotStat(PassiveStat.SkillCooldown, slot);
            int rep = CharacterStats.SlotStat(PassiveStat.SkillRepeat, slot);
            if (dmg > 0) parts.Add(L.F("피해 +{0}%", dmg));
            if (area > 0) parts.Add(L.F("범위 +{0}%", area));
            if (cd > 0) parts.Add(L.F("재사용 -{0}%", cd));
            if (rep > 0) parts.Add(L.F("추가 발동 +{0}", rep));
            return string.Join(" · ", parts);
        }

        void RefreshGems()
        {
            var prog = Game.Session.Progression;
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            foreach (var v in sockets)
            {
                var gem = v.socket == 0 ? prog.Active(v.slot) : SkillGems.Get(prog.SlotGem(v.slot, v.socket));
                v.icon.enabled = gem != null;
                if (gem != null) v.icon.sprite = Game.Art.Get(gem.icon);
                v.label.enabled = gem == null;
                bool sel = !mouse && keyboardUsed && sockets.IndexOf(v) == selectedSocket && tab == TabId.Gems;
                v.bg.color = sel ? new Color32(255, 230, 150, 255) : Color.white;
            }
            for (int s = 0; s < SkillGems.Slots; s++)
            {
                bool ult = s == SkillGems.UltimateSlot;
                bool open = prog.IsSlotOpen(s) && (s!=4 || prog.Awakened);
                var gem = prog.Active(s);
                string key = Game.Input.GetBindingLabel(SkillGems.ActionFor(s));
                int number = System.Array.IndexOf(SkillGems.BarOrder, s) + 1; // [SKILL 8] numbered in key order
                slotTitle[s].text = (ult ? L.T("<color=#ffd66e><b>각성</b></color>") : L.F("<b>스킬 {0}</b>", number)) + $"\n<color=#ffe066><size=20>{key}</size></color>";
                slotLock[s].enabled = !open;
                slotLockText[s].enabled = !open;
                slotLockText[s].text = s==4 ? L.T("전직 후 각성 시련을 완료하세요") : L.F("Lv.{0}에 슬롯 개방", Progression.SlotLevel(s));
                if (gem == null)
                {
                    // [SKILL 8] An open A/S/D key with nothing on it says how to fill it.
                    slotInfo[s].text = open && !ult ? L.T("<color=#8c96a8>빈 칸: 전직 스킬 창에서 스킬을 이 키로 끌어다 놓거나, 스킬 칸을 클릭해 고르세요.</color>") : "";
                    continue;
                }
                var n = CharacterStats.Skill(cls, s, gem, prog.Supports(s));
                var career=CareerCatalog.Get(gem.id);
                if(career!=null){slotInfo[s].text=L.F("<b>{0}</b>  <color=#ff9f43>전직 기술</color>\n{1}\n<color=#8c96a8>설명은 스킬 칸에 마우스를 올리면 보인다.</color>", L.T(gem.name), L.T(CareerNumbers.Summary(career,CharacterData.Session,n)));continue;}
                var sb = new StringBuilder(L.F("<b>{0}</b>{1}\n", L.T(gem.name), (ult ? L.T("  <color=#ffd66e>[각성 기술]</color>") : "")));
                // [SKILL v2] Defence skills show what they block; single-target ones have no area to show.
                bool single = gem.id == "crush" || gem.id == "lance" || gem.id == "stab" || gem.id == "backstab" || gem.id == "raid";
                if (n.guardPct > 0) sb.Append(L.F("받는 피해 <color=#ffe066>-{0}%</color> ({1:0}초)   {2} 소모 {3}   재사용 {4:0.##}초", n.guardPct, n.guardTime, (n.usesLife ? "HP" : "MP"), n.manaCost, n.cooldown));
                else sb.Append(L.F("피해 <color=#ffe066>{0}</color>{1}   {2} 소모 {3}   재사용 {4:0.##}초", n.damage, (n.hits > 1 ? L.F(" × {0}회", n.hits) : ""), (n.usesLife ? "HP" : "MP"), n.manaCost, n.cooldown) + (single ? L.T("   단일 대상") : L.F("   범위 {0:0.#}", n.radius)));
                if (n.chains > 0) sb.Append(L.F("   연쇄 {0}", n.chains));
                if (n.repeats > 0) sb.Append(L.F("   반복 +{0}", n.repeats));
                if (n.freeze > 0) sb.Append(L.F("   빙결 {0:0.#}초", n.freeze));
                if (n.stun > 0) sb.Append(L.F("   기절 {0:0.#}초", n.stun));
                if (n.buffPct > 0) sb.Append(L.F("   피해 +{0}% ({1:0}초)", n.buffPct, n.buffTime));
                if (n.leechPct > 0) sb.Append(L.F("   흡혈 {0}%", n.leechPct));
                if (n.bossPct > 0) sb.Append(L.F("   보스 피해 +{0}%", n.bossPct));
                var sup = new List<string>();
                foreach (var g in prog.Supports(s)) sup.Add(L.T(g.name));
                string bonus = TreeBonus(s);
                sb.Append("\n");
                sb.Append(sup.Count > 0 ? L.F("<color=#8fe28f>보조: {0}</color>", string.Join(", ", sup)) : L.T("<color=#8c96a8>보조 칸이 비었다. 클릭해서 보조 젬을 끼우자.</color>"));
                if (bonus.Length > 0) sb.Append(L.F("   <color=#ffcf70>트리: {0}</color>", bonus));
                slotInfo[s].text = sb.ToString();
            }
            int unlocked = 0, total = 0;
            foreach (var g in SkillGems.All) if (g.kind == GemKind.Support) { total++; if (prog.IsUnlocked(g)) unlocked++; }
            gemListText.text = L.F("<b>보조 젬 {0}/{1} 해금</b>   <color=#8c96a8>보조 칸 클릭: 다음 젬 · 우클릭: 이전 젬 · 칸에 마우스를 올리면 자세한 설명</color>", unlocked, total);
            RefreshGemGuide();
        }

        // ================= Common =================

        void BuildTooltip()
        {
            tooltip = UIFactory.Rect(transform, "Tooltip");
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.pivot = new Vector2(0f, 1f);
            var bg = Img(tooltip, "Bg", "ui_tooltip", Color.white);
            UIFactory.Stretch(bg.rectTransform);
            tooltipText = Label(tooltip, "Text", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(332f, 300f));
            tooltip.gameObject.SetActive(false);
        }

        // The career tree page is plain buttons: the gamepad moves between them (the gem page keeps its own cursor).
        protected override bool PadNavigation => tab == TabId.Tree;

        protected override void Refresh()
        {
            for (int i = 0; i < 2; i++)
            {
                bool on = (int)tab == i;
                tabBg[i].sprite = UiTheme.Tab(on);
                tabBg[i].type = Image.Type.Sliced;
                tabBg[i].color = Color.white;
            }
            treePage.gameObject.SetActive(tab == TabId.Tree);
            gemPage.gameObject.SetActive(tab == TabId.Gems);
            RefreshCareer();
            RefreshGems();
        }

        protected override bool UsesTabKey => true;

        protected override void Update()
        {
            // [UX] Cancel / UseItem on a selected key box clears that key (before the base closes the window on Cancel).
            if (tab == TabId.Tree && TakesInput && ClearSelectedKey()) return;
            base.Update();
            if (!gameObject.activeSelf) return;
            var input = Game.Input;
            tabKeyHint.text = L.T("<color=#8c96a8>Tab / LB · RB : 탭 전환</color>");
            if (TakesInput) // [UX] not under a confirm (전직 기술 초기화, 추천 배치, 전직 선택) drawn over this window
            {
                // [UX] Tab (or the shoulder buttons) switches pages; the skill keys stay free in this window.
                if (UnityEngine.Input.GetKeyDown(KeyCode.Tab) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton4) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton5)) { tab = tab == TabId.Tree ? TabId.Gems : TabId.Tree; Game.Audio.PlaySfx("select"); Refresh(); }
                var nav = input.NavigateStep;
                if (nav != Vector2Int.zero)
                {
                    mouse = false;
                    keyboardUsed = true;
                    // The career page is plain buttons: the UI navigation moves the selection there, so only the gem
                    // page moves its own cursor and redraws (a rebuild would drop the selected button).
                    if (tab == TabId.Gems)
                    {
                        selectedSocket = (selectedSocket + (nav.x != 0 ? nav.x : nav.y * -3) + sockets.Count * 3) % sockets.Count;
                        Game.Audio.PlaySfx("select", 0.5f);
                        Refresh();
                    }
                }
                if (input.SubmitPressed)
                {
                    mouse = false;
                    keyboardUsed = true;
                    if (tab == TabId.Gems) Cycle(sockets[selectedSocket], 1);
                }
            }
            // Tooltip follows the hovered item (mouse) or the keyboard selection.
            object target = mouse ? hovered : !keyboardUsed ? null : tab == TabId.Tree ? null : sockets[selectedSocket];
            if (target == null) { tooltip.gameObject.SetActive(false); return; }
            RectTransform anchor = target is GuideEntry ge ? ge.bg.rectTransform : ((SocketView)target).bg.rectTransform;
            if (!anchor.gameObject.activeInHierarchy) { tooltip.gameObject.SetActive(false); return; }
            if (target is GuideEntry entry) FillGuideTooltip(entry); else FillTooltip((SocketView)target);
            tooltip.gameObject.SetActive(true);
            var root = (RectTransform)transform;
            Vector2 p = root.InverseTransformPoint(anchor.TransformPoint(new Vector3(anchor.rect.xMax, anchor.rect.yMax, 0f)));
            var size = tooltip.sizeDelta;
            float x = p.x + 12f;
            if (x + size.x > root.rect.width * 0.5f - 10f) x = p.x - anchor.rect.width - size.x - 12f;
            float y = Mathf.Clamp(p.y, -root.rect.height * 0.5f + size.y + 10f, root.rect.height * 0.5f - 10f);
            tooltip.anchoredPosition = new Vector2(x, y);
            tooltip.SetAsLastSibling();
        }

        void FillTooltip(SocketView v)
        {
            var prog = Game.Session.Progression;
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            if (v.socket == 0)
            {
                var skill = prog.Active(v.slot);
                if (skill == null) { SetTooltip(L.T("빈 슬롯")); return; }
                string state = prog.IsSlotOpen(v.slot) ? L.T("<color=#8fe28f>사용 가능</color>") : L.F("<color=#ff9f43>Lv.{0}에 열림</color>", Progression.SlotLevel(v.slot));
                var sup = new List<string>();
                foreach (var g in prog.Supports(v.slot)) sup.Add($"{L.T(g.name)} ({GemEffect(g)})");
                SetTooltip(L.F("<b>{0}</b>  {1}\n\n{2}\n\n", L.T(skill.name), (skill.IsUltimate ? L.T("<color=#ffd66e>각성 기술</color>") : L.T("<color=#ff9f43>스킬 젬</color>")), L.T(skill.description)) +
                           L.F("기본 피해 배율 {0:0}%{1}   MP {2}   재사용 {3:0.#}초\n", skill.damageMult * 100, (skill.hits > 1 ? L.F(" × {0}회", skill.hits) : ""), skill.manaCost, skill.cooldown) +
                           (sup.Count > 0 ? L.F("\n<color=#8fe28f>연결된 보조 젬</color>\n{0}\n", string.Join("\n", sup)) : "") +
                           L.F("\n{0}\n<color=#8c96a8>클릭: 이 칸에 넣을 수 있는 다른 스킬로 바꾸기</color>", state));
                return;
            }
            var gem = SkillGems.Get(prog.SlotGem(v.slot, v.socket));
            if (gem == null)
            {
                SetTooltip(L.T("<b>빈 보조 칸</b>\n\n보조 젬을 끼우면 왼쪽 스킬이 바뀐다(피해·범위·연속 발동·흡혈 등).\n클릭: 다음 젬 · 우클릭: 이전 젬\n\n<color=#8c96a8>어떤 젬이 있는지는 오른쪽 '젬 안내'에서 볼 수 있다. 첫 보조 젬은 Lv.3에 해금.</color>"));
                return;
            }
            SetTooltip(L.F("<b>{0}</b>  <color=#8fe28f>보조 젬</color>\n\n{1}\n\n<color=#ffe066>효과</color>  {2}\n", L.T(gem.name), L.T(gem.description), GemEffect(gem)) +
                       (string.IsNullOrEmpty(gem.tip) ? "" : L.F("<color=#ffe066>추천</color>  {0}\n", L.T(gem.tip))) + L.T("\n<color=#8c96a8>클릭: 다음 젬 · 우클릭: 이전 젬</color>"));
        }

        void SetTooltip(string text)
        {
            tooltipText.text = text;
            // [UI 2026-10-14] The text box grows with the text (it was a fixed 300 px and long gem notes were cut off).
            float h = Mathf.Max(52f, tooltipText.preferredHeight + 4f);
            tooltipText.rectTransform.sizeDelta = new Vector2(tooltipText.rectTransform.sizeDelta.x, h);
            tooltip.sizeDelta = new Vector2(360f, h + 24f);
        }
    }
}
