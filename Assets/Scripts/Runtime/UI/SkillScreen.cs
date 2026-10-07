using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Skill window modelled on Path of Exile:
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

        static readonly Color32[] ClusterColors =
        {
            new Color32(215, 80, 64, 255),   // left  · skill 1
            new Color32(70, 130, 230, 255),  // right · skill 2
            new Color32(80, 175, 90, 255),   // down  · skill 3
            new Color32(165, 100, 220, 255), // up    · skill 4
            new Color32(235, 160, 60, 255),  // start / keystones
        };

        TabId tab = TabId.Tree;
        RectTransform treePage, gemPage;
        readonly Image[] tabBg = new Image[2];

        // Tree.
        sealed class NodeView { public PassiveNode node; public RectTransform rect; public Image fill, ring, glyph, halo, ornament; public bool owned, available; public Color accent; }
        sealed class LinkView { public PassiveNode a, b; public Image line, glow; }
        readonly List<NodeView> nodeViews = new List<NodeView>();
        readonly List<LinkView> linkViews = new List<LinkView>();
        Text pointsText, legendText, summaryText;
        NodeView selectedNode;

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

        // ================= Passive tree =================

        static float SizeOf(PassiveNode n) =>
            n.kind == PassiveKind.Keystone ? 54f : n.kind == PassiveKind.Mastery ? 48f : n.kind == PassiveKind.Notable ? 42f : n.kind == PassiveKind.Start ? 50f : 26f;

        void BuildTree()
        {
            var area = Panel(treePage, "TreeArea", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1010f, 530f), UiTheme.PanelDeep);
            var center = UIFactory.Place(UIFactory.Rect(area.transform, "Center"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            BuildConstellation(center);
            var done = new HashSet<string>();
            foreach (var n in PassiveTree.All)
                foreach (var l in n.links)
                {
                    string key = string.CompareOrdinal(n.id, l) < 0 ? n.id + "|" + l : l + "|" + n.id;
                    if (!done.Add(key)) continue;
                    var b = PassiveTree.Get(l);
                    var line = Img(center, "Link", "ui_white", Color.gray);
                    Vector2 pa = UiPos(n), pb = UiPos(b), d = pb - pa;
                    var rt = line.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = (pa + pb) * 0.5f;
                    rt.sizeDelta = new Vector2(d.magnitude, 2f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    var glow = Img(center, "LinkLight", "ui_white", Color.clear);
                    UIFactory.Place(glow.rectTransform, Vector2.one * .5f, Vector2.one * .5f, rt.anchoredPosition, new Vector2(d.magnitude, 8f));
                    glow.rectTransform.localRotation = rt.localRotation;
                    glow.transform.SetSiblingIndex(line.transform.GetSiblingIndex());
                    linkViews.Add(new LinkView { a = n, b = b, line = line, glow = glow });
                }
            foreach (var n in PassiveTree.All)
            {
                float size = SizeOf(n);
                var halo = Ornament(center, "NodeGlow", "halo", UiPos(n), Vector2.one * size * 2.5f, Color.clear);
                var ring = Ornament(center, "Node_" + n.id, "ring", UiPos(n), Vector2.one * size, Color.white);
                UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), UiPos(n), new Vector2(size, size));
                ring.raycastTarget = true;
                var fill = Img(ring.transform, "Fill", "ui_circle", Color.white);
                UIFactory.Stretch(fill.rectTransform, size * .18f, size * .18f, size * .18f, size * .18f);
                fill.sprite = ArcaneUiArt.Get("gem");
                var ornament = Ornament(ring.transform, "Engraving", "sigil", Vector2.zero, Vector2.one * size * 1.35f, Color.clear);
                if (n.kind == PassiveKind.Small) ornament.enabled = false;
                var glyph = UIFactory.Image(ring.transform, "Glyph", SkillNodeArt.For(n), Color.white);
                float glyphSize = n.kind == PassiveKind.Small ? 16 : n.kind == PassiveKind.Keystone ? 32 : n.kind == PassiveKind.Notable ? 24 : 28;
                UIFactory.Place(glyph.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, Vector2.one * glyphSize);
                var view = new NodeView { node = n, rect = ring.rectTransform, fill = fill, ring = ring, glyph = glyph, halo = halo, ornament = ornament };
                var relay = ring.gameObject.AddComponent<PointerRelay>();
                relay.onEnter = () => { hovered = view; mouse = true; };
                relay.onExit = () => { if (hovered == view) hovered = null; };
                relay.onClick = b => { selectedNode = view; if (b == PointerEventData.InputButton.Right) Refund(n); else Allocate(n); };
                nodeViews.Add(view);
            }

            var side = Panel(treePage, "Side", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(200f, 530f), UiTheme.Panel);
            pointsText = Label(side.transform, "Points", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(176f, 64f));
            legendText = Label(side.transform, "Legend", "", 14, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -92f), new Vector2(180f, 150f));
            summaryText = Label(side.transform, "Summary", "", 14, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -250f), new Vector2(180f, 190f));
            Button(side.transform, "Reset", "트리 초기화", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(170f, 50f),
                () => { Game.Session.Progression.ResetTree(); Game.Audio.PlaySfx("cancel"); Refresh(); }, 20);
        }

        static Vector2 UiPos(PassiveNode n) => new Vector2(n.pos.x * .98f, n.pos.y * .94f);

        void Allocate(PassiveNode n)
        {
            var prog = Game.Session.Progression;
            if (prog.Allocate(n)) { Game.Audio.PlaySfx("confirm"); Fx.Sparkle(Game.Player.Center, 2, 0.3f); }
            else
            {
                Game.Audio.PlaySfx("cancel");
                if (!prog.Allocated.Contains(n.id))
                    GameEvents.RaiseToast(prog.PointsLeft <= 0 ? "패시브 포인트가 없습니다. 사냥으로 레벨을 올리세요." : "이미 찍은 노드와 연결된 곳만 찍을 수 있습니다.");
            }
            Refresh();
        }

        void Refund(PassiveNode n)
        {
            if (Game.Session.Progression.Refund(n)) Game.Audio.PlaySfx("select");
            else { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast("다른 노드가 이 노드에 연결되어 있어 되돌릴 수 없습니다."); }
            Refresh();
        }

        static string Hex(Color32 c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}";

        void RefreshTree()
        {
            var prog = Game.Session.Progression;
            foreach (var v in nodeViews)
            {
                bool owned = prog.Allocated.Contains(v.node.id);
                bool can = prog.CanAllocate(v.node);
                bool reachable = !owned && v.node.links.Exists(prog.Allocated.Contains);
                Color cluster = ClusterColors[Mathf.Clamp(v.node.cluster, 0, ClusterColors.Length - 1)];
                v.owned = owned; v.available = can; v.accent = cluster;
                v.ornament.color = owned ? new Color(1f, .78f, .38f, .85f) : new Color(.38f, .53f, .72f, .45f);
                v.fill.color = Color.Lerp(UiTheme.PanelDeep, cluster, owned ? .27f : reachable ? .19f : .08f);
                v.ring.color = owned ? new Color32(255, 214, 90, 255) : can ? new Color32(240, 240, 240, 255) : new Color32(70, 76, 90, 255);
                if (v.node.kind == PassiveKind.Keystone && !owned) v.ring.color = can ? new Color32(255, 170, 90, 255) : new Color32(120, 80, 60, 255);
                if (v.glyph != null) v.glyph.color = new Color(1f, 1f, 1f, owned ? 1f : reachable ? .9f : .55f);
            }
            foreach (var l in linkViews)
            {
                bool a = prog.Allocated.Contains(l.a.id), b = prog.Allocated.Contains(l.b.id);
                l.glow.color = a && b ? new Color(.22f, .55f, .85f, .2f) : Color.clear;
                l.line.color = a && b ? new Color32(230, 190, 90, 255) : a || b ? UiTheme.TextMuted : new Color32(52, 58, 72, 255);
            }
            pointsText.text = $"<b>Lv.{prog.Level}</b>\n남은 포인트  <color=#ffe066><size=26>{prog.PointsLeft}</size></color>";

            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            string SkillName(int s) => SkillGems.ForSlot(cls, s)?.name ?? "?";
            var legend = new StringBuilder("<b>방향별 강화 스킬</b>\n");
            string[] dirs = { "왼쪽", "오른쪽", "아래", "위" };
            for (int s = 0; s < 4; s++) legend.Append($"<color={Hex(ClusterColors[s])}>●</color> {dirs[s]} · {SkillName(s)}\n");
            legend.Append($"<color={Hex(ClusterColors[4])}>●</color> 모서리 · 키스톤");
            legendText.text = legend.ToString();

            var sb = new StringBuilder("<b>현재 합계</b>\n");
            sb.Append($"공격 피해 {CharacterStats.AttackDamage(cls)}  (+{CharacterStats.IncDamage}%)\n");
            sb.Append($"HP {CharacterStats.MaxHp} · MP {CharacterStats.MaxMp}\n");
            sb.Append($"MP 재생 {CharacterStats.ManaRegen:0.#}/초\n");
            sb.Append($"막기 {CharacterStats.Block}% · 이동 {CharacterStats.SpeedBonus:+0;-0;0}%\n");
            sb.Append($"공격·스킬 속도 +{CharacterStats.AttackSpeed}%\n");
            sb.Append($"범위 +{CharacterStats.Aoe}% · MP 소모 -{CharacterStats.ManaCostReduction}%");
            summaryText.text = sb.ToString();
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
                if (n.chains > 0) sb.Append(gem.id == "thunder" ? $"   낙뢰 {1 + n.chains}회" : $"   연쇄 {n.chains}");
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
            if (selectedNode == null) selectedNode = nodeViews.Find(v => v.node.id == PassiveTree.Start);
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
                    if (tab == TabId.Tree && selectedNode != null) Allocate(selectedNode.node);
                    else if (tab == TabId.Gems) Cycle(sockets[selectedSocket], 1);
                }
            }
            AnimateTree();
            // Tooltip follows the hovered item (mouse) or the keyboard selection.
            object target = mouse ? hovered : !keyboardUsed ? null : tab == TabId.Tree ? (object)selectedNode : sockets[selectedSocket];
            if (target == null) { tooltip.gameObject.SetActive(false); return; }
            RectTransform anchor = target is NodeView nv ? nv.rect : ((SocketView)target).bg.rectTransform;
            if (!anchor.gameObject.activeInHierarchy) { tooltip.gameObject.SetActive(false); return; }
            if (target is NodeView n2) FillTooltip(n2); else FillTooltip((SocketView)target);
            tooltip.gameObject.SetActive(true);
            var root = (RectTransform)transform;
            Vector2 p = root.InverseTransformPoint(anchor.TransformPoint(new Vector3(anchor.rect.xMax, anchor.rect.yMax, 0f)));
            var size = tooltip.sizeDelta;
            float x = p.x + 12f;
            if (x + size.x > root.rect.width * 0.5f - 10f) x = p.x - anchor.rect.width - size.x - 12f;
            float y = Mathf.Clamp(p.y, -root.rect.height * 0.5f + size.y + 10f, root.rect.height * 0.5f - 10f);
            tooltip.anchoredPosition = new Vector2(x, y);
            tooltip.SetAsLastSibling();
            if (!mouse && tab == TabId.Tree && selectedNode != null) HighlightSelected();
        }

        void HighlightSelected()
        {
            AnimateTree();
        }

        void MoveNode(Vector2 dir)
        {
            if (selectedNode == null) return;
            NodeView best = null;
            float bestScore = float.MaxValue;
            Vector2 from = UiPos(selectedNode.node);
            foreach (var v in nodeViews)
            {
                if (v == selectedNode) continue;
                Vector2 d = UiPos(v.node) - from;
                float along = Vector2.Dot(d, dir.normalized);
                if (along <= 1f) continue;
                float score = d.magnitude + Mathf.Abs(Vector2.Dot(d, new Vector2(-dir.y, dir.x).normalized)) * 1.5f;
                if (score < bestScore) { bestScore = score; best = v; }
            }
            if (best != null) selectedNode = best;
            HighlightSelected();
        }

        void FillTooltip(NodeView v)
        {
            var n = v.node;
            var prog = Game.Session.Progression;
            string kind = n.kind == PassiveKind.Keystone ? "<color=#ff9f43>키스톤</color>" : n.kind == PassiveKind.Mastery ? "<color=#ffcf70>숙련 노드</color>"
                : n.kind == PassiveKind.Notable ? "<color=#ffe066>특화 노드</color>" : n.kind == PassiveKind.Start ? "<color=#8fe28f>시작점</color>" : "<color=#b8c4d8>일반 노드</color>";
            var sb = new StringBuilder($"<b>{n.name}</b>  {kind}\n\n{(n.kind == PassiveKind.Start ? "여기서부터 네 방향으로 트리를 뻗어 나간다.\n왼쪽·오른쪽·아래·위 = 스킬 1·2·3·4 강화" : n.StatText())}\n\n");
            if (prog.Allocated.Contains(n.id)) sb.Append(n.kind == PassiveKind.Start ? "" : prog.CanRefund(n) ? "<color=#8c96a8>우클릭: 되돌리기</color>" : "<color=#8c96a8>투자 완료</color>");
            else if (prog.CanAllocate(n)) sb.Append("<color=#ffe066>클릭: 투자 (포인트 1)</color>");
            else if (prog.PointsLeft <= 0) sb.Append("<color=#ff8080>남은 포인트가 없습니다</color>");
            else sb.Append("<color=#ff8080>연결된 노드를 먼저 찍어야 합니다</color>");
            SetTooltip(sb.ToString().TrimEnd('\n'));
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
