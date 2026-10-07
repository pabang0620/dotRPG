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

        const float RowHeight = 92f, RowGap = 6f;

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
            base.Show();
        }

        public static SkillScreen Create(Transform canvas)
        {
            var w = CreateWindow<SkillScreen>(canvas, "Skills", "스킬", "menuicon_skill");
            w.BuildTabs();
            w.BuildCareer();
            w.BuildGems();
            w.BuildTooltip();
            return w;
        }

        void BuildTabs()
        {
            string[] names = { "전직 · 스킬 노드", "기본 · 장착 스킬" };
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
                var row = Panel(gemPage, "Slot" + s, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -s * (RowHeight + RowGap)), new Vector2(1220f, RowHeight),
                    ult ? new Color32(52, 44, 30, 240) : UiTheme.Panel);
                slotTitle[s] = Label(row.transform, "Title", "", 18, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(92f, 80f), TextAnchor.MiddleCenter);
                // Link bar behind the three sockets.
                var link = Img(row.transform, "Link", "ui_white", new Color32(150, 120, 70, 255));
                UIFactory.Place(link.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(180f, 6f));
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++)
                {
                    float size = k == 0 ? 76f : 62f;
                    float x = k == 0 ? 112f : 206f + (k - 1) * 76f;
                    var bg = Img(row.transform, $"Socket{s}_{k}", k == 0 ? "ui_slotblue" : "ui_slot", Color.white);
                    UIFactory.Place(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(size, size));
                    bg.raycastTarget = true;
                    var icon = UIFactory.SharpIcon(bg.transform, "Icon", Color.white);
                    UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.7f, size * 0.7f));
                    var label = UIFactory.Text(bg.transform, "Label", k == 0 ? "스킬" : "보조", UiTheme.FontMin, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, true);
                    UIFactory.Stretch(label.rectTransform);
                    var view = new SocketView { slot = s, socket = k, bg = bg, icon = icon, label = label };
                    int index = sockets.Count;
                    var relay = bg.gameObject.AddComponent<PointerRelay>();
                    relay.onEnter = () => { hovered = view; mouse = true; };
                    relay.onExit = () => { if (hovered == view) hovered = null; };
                    relay.onClick = b => { selectedSocket = index; Cycle(view, b == PointerEventData.InputButton.Right ? -1 : 1); };
                    sockets.Add(view);
                }
                slotInfo[s] = Label(row.transform, "Info", "", 16, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(368f, 0f), new Vector2(840f, 84f), TextAnchor.MiddleLeft);
                slotInfo[s].lineSpacing = 1.1f;
                slotLock[s] = Img(row.transform, "Lock", "ui_white", new Color(0.02f, 0.03f, 0.06f, 0.55f));
                UIFactory.Stretch(slotLock[s].rectTransform);
                slotLockText[s] = Label(row.transform, "LockText", "", 20, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(360f, 40f), TextAnchor.MiddleRight);
            }
            float listY = -SkillGems.Slots * (RowHeight + RowGap);
            var list = Panel(gemPage, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, listY), new Vector2(1220f, 38f), UiTheme.PanelDeep);
            gemListText = Label(list.transform, "Text", "", UiTheme.FontMin, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(1190f, 34f), TextAnchor.MiddleLeft);
        }

        void Cycle(SocketView v, int dir)
        {
            var prog = Game.Session.Progression;
            if (!prog.IsSlotOpen(v.slot))
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"이 슬롯은 Lv.{Progression.SlotLevel(v.slot)}에 열립니다.");
                return;
            }

            var options = prog.OptionsFor(v.slot, v.socket);
            int i = options.IndexOf(prog.SlotGem(v.slot, v.socket));
            if (i < 0) i = 0;
            if (options.Count <= 1)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast(v.socket == 0 ? "이 칸에 넣을 수 있는 다른 스킬이 없습니다." : "사용할 수 있는 보조 젬이 없습니다. (Lv.3부터 해금)");
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
            if (dmg > 0) parts.Add($"피해 +{dmg}%");
            if (area > 0) parts.Add($"범위 +{area}%");
            if (cd > 0) parts.Add($"재사용 -{cd}%");
            if (rep > 0) parts.Add($"추가 발동 +{rep}");
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
                slotTitle[s].text = (ult ? "<color=#ffd66e><b>각성</b></color>" : $"<b>스킬 {s + 1}</b>") + $"\n<color=#ffe066><size=26>{key}</size></color>";
                slotLock[s].enabled = !open;
                slotLockText[s].enabled = !open;
                slotLockText[s].text = s==4 ? "전직 후 각성 시련을 완료하세요" : $"Lv.{Progression.SlotLevel(s)}에 슬롯 개방";
                if (gem == null) { slotInfo[s].text = ""; continue; }
                var n = CharacterStats.Skill(cls, s, gem, prog.Supports(s));
                var career=CareerCatalog.Get(gem.id);
                if(career!=null){slotInfo[s].text=$"<b>{gem.name}</b> · {gem.description}\n{CareerNumbers.Summary(career,CharacterData.Session,n)}";continue;}
                var sb = new StringBuilder($"<b>{gem.name}</b>{(ult ? "  <color=#ffd66e>[각성 기술]</color>" : "")}   <color=#b8c4d8>{gem.description}</color>\n");
                // [SKILL v2] Defence skills show what they block; single-target ones have no area to show.
                bool single = gem.id == "crush" || gem.id == "lance";
                if (n.guardPct > 0) sb.Append($"받는 피해 <color=#ffe066>-{n.guardPct}%</color> ({n.guardTime:0}초)   {(n.usesLife ? "HP" : "MP")} 소모 {n.manaCost}   재사용 {n.cooldown:0.##}초");
                else sb.Append($"피해 <color=#ffe066>{n.damage}</color>{(n.hits > 1 ? $" × {n.hits}회" : "")}   {(n.usesLife ? "HP" : "MP")} 소모 {n.manaCost}   재사용 {n.cooldown:0.##}초" + (single ? "   단일 대상" : $"   범위 {n.radius:0.#}"));
                if (n.chains > 0) sb.Append($"   연쇄 {n.chains}");
                if (n.repeats > 0) sb.Append($"   반복 +{n.repeats}");
                if (n.freeze > 0) sb.Append($"   빙결 {n.freeze:0.#}초");
                if (n.stun > 0) sb.Append($"   기절 {n.stun:0.#}초");
                if (n.buffPct > 0) sb.Append($"   피해 +{n.buffPct}% ({n.buffTime:0}초)");
                if (n.leechPct > 0) sb.Append($"   흡혈 {n.leechPct}%");
                if (n.bossPct > 0) sb.Append($"   보스 피해 +{n.bossPct}%");
                var sup = new List<string>();
                foreach (var g in prog.Supports(s)) sup.Add(g.name);
                string bonus = TreeBonus(s);
                sb.Append("\n");
                sb.Append(sup.Count > 0 ? $"<color=#8fe28f>보조: {string.Join(", ", sup)}</color>" : "<color=#8c96a8>보조 젬을 연결하면 스킬이 강해집니다.</color>");
                if (bonus.Length > 0) sb.Append($"   <color=#ffcf70>트리: {bonus}</color>");
                slotInfo[s].text = sb.ToString();
            }
            var list = new StringBuilder("<b>보조 젬</b>   ");
            foreach (var g in SkillGems.All)
            {
                if (g.kind != GemKind.Support) continue;
                bool ok = prog.IsUnlocked(g);
                list.Append(ok ? $"<color=#ffffff>{g.name}</color>    " : $"<color=#6c7486>{g.name} (Lv.{g.unlockLevel})</color>    ");
            }
            gemListText.text = list.ToString();
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
            tabKeyHint.text = "<color=#8c96a8>Tab / LB · RB : 탭 전환</color>";
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
            RectTransform anchor = ((SocketView)target).bg.rectTransform;
            if (!anchor.gameObject.activeInHierarchy) { tooltip.gameObject.SetActive(false); return; }
            FillTooltip((SocketView)target);
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
                if (skill == null) { SetTooltip("빈 슬롯"); return; }
                string state = prog.IsSlotOpen(v.slot) ? "<color=#8fe28f>사용 가능</color>" : $"<color=#ff9f43>Lv.{Progression.SlotLevel(v.slot)}에 열림</color>";
                SetTooltip($"<b>{skill.name}</b>  {(skill.IsUltimate ? "<color=#ffd66e>각성 기술</color>" : "<color=#ff9f43>액티브 스킬</color>")}\n\n{skill.description}\n\n" +
                           $"기본 피해 배율 {skill.damageMult * 100:0}%{(skill.hits > 1 ? $" × {skill.hits}회" : "")}   MP {skill.manaCost}   재사용 {skill.cooldown:0.#}초\n\n{state}");
                return;
            }
            var gem = SkillGems.Get(prog.SlotGem(v.slot, v.socket));
            if (gem == null)
            {
                SetTooltip($"<b>빈 보조 소켓</b>\n\n클릭해서 보조 젬을 끼웁니다.\n<color=#8c96a8>보조 젬: Lv.3부터 해금</color>");
                return;
            }
            SetTooltip($"<b>{gem.name}</b>  <color=#8fe28f>보조 젬</color>\n\n{gem.description}\n\n<color=#8c96a8>클릭: 다음 젬 · 우클릭: 이전 젬</color>");
        }

        void SetTooltip(string text)
        {
            tooltipText.text = text;
            tooltip.sizeDelta = new Vector2(360f, Mathf.Max(80f, tooltipText.preferredHeight + 28f));
        }
    }
}
