using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 던전 선택 window (side menu 요일던전 / 레이드, or the village dungeon guide), plan §6.0 입장 흐름:
    /// today's weekday, the five weekday dungeons (open / closed today), the chosen dungeon's details,
    /// difficulty buttons (locked ones grey), recommended level and 전투력 against the player's, the current
    /// party (with a button to the 파티 window), "오늘 남은 입장 N/3", a reward preview and 입장.
    /// The 레이드 tab shows 해골왕 and this week's reward status.
    /// Keys: ↑/↓ dungeon, ←/→ difficulty, Enter 입장, E switches the tab, Esc closes.
    /// </summary>
    public partial class DungeonSelectScreen : WindowScreen
    {
        const float ListW = 380f, RowH = 96f, RowGap = 8f, DetailX = 392f, DetailW = 828f, PanelTop = -52f, PanelH = 540f;
        const float DiffW = 150f, DiffH = 46f, DiffGap = 12f;
        const float TabW = 200f;
        const float RaidDiffX = 520f, RaidDiffW = 110f; // [RAID] 레이드 / 영웅 buttons on the schedule line

        sealed class Row
        {
            public DungeonDef def;
            public Image bg, stripe;
            public Text text, tag;
        }

        readonly List<Row> rows = new List<Row>();
        readonly Button[] diffButtons = new Button[4];
        readonly Text[] diffLabels = new Text[4];
        Button tabWeekday, tabNormal, tabRaid, enterButton, partyButton, resetButton;
        Image banner;
        GameObject trailerBadge;
        Text diffTitle;
        Text dayText, title, desc, info, recommend, partyText, rewards, raidLine, status, hint;
        const int SlotCount = 8;
        const float SlotW = 92f, SlotIcon = 56f;
        readonly List<(Image frame, Image icon, Text name)> slots = new List<(Image, Image, Text)>();
        RectTransform listRoot;
        /// <summary>0 요일던전, 1 일반 던전 (every day), 2 레이드.</summary>
        int tab, builtTab = -1;
        bool raidTab => tab == TabRaid;
        const int TabWeekday = 0, TabNormal = 1, TabRaid = 2;
        public bool IsRaidTab => raidTab;
        int selected;
        DungeonDifficulty difficulty;
        float lastMoveX, lastMoveY;

        static readonly Color RowColor = new Color32(24, 36, 54, 235);
        static readonly Color RowSelected = new Color32(44, 66, 96, 245);
        static readonly Color RowClosed = new Color32(20, 26, 36, 235);

        public static DungeonSelectScreen Create(Transform canvas)
        {
            var w = CreateWindow<DungeonSelectScreen>(canvas, "DungeonSelect", L.T("던전"), "menuicon_dungeon");
            // [UX 2026-10-14] Three kinds, told apart at a glance: the tab says what the kind is (요일 = today's dungeon with
            // daily entries, 일반 = every day without limit, 레이드), the rows and the detail title carry the same badge.
            w.tabWeekday = Button(w.content, "TabWeekday", L.T("요일 던전"), "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(TabW, 44f), () => w.SetTab(TabWeekday), 20);
            w.tabNormal = Button(w.content, "TabNormal", L.T("일반 던전"), "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(TabW + 10f, 0f), new Vector2(TabW, 44f), () => w.SetTab(TabNormal), 20);
            w.tabRaid = Button(w.content, "TabRaid", L.T("레이드"), "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(2f * (TabW + 10f), 0f), new Vector2(TabW - 40f, 44f), () => w.SetTab(TabRaid), 20);
            w.dayText = Label(w.content, "Day", "", 18, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(ContentSize.x - (3f * TabW - 10f) - 16f, 44f), TextAnchor.MiddleRight);
            w.dayText.resizeTextForBestFit = true;
            w.dayText.resizeTextMinSize = 12;
            w.dayText.resizeTextMaxSize = 18;

            w.listRoot = UIFactory.Place(UIFactory.Rect(w.content, "List"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, PanelTop), new Vector2(ListW, PanelH));

            var detail = Panel(w.content, "Detail", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(DetailX, PanelTop), new Vector2(DetailW, PanelH), new Color32(24, 36, 54, 235));
            var d = detail.transform;
            var tl = new Vector2(0f, 1f);
            w.title = Label(d, "Title", "", 28, tl, tl, new Vector2(24f, -14f), new Vector2(530f, 40f));
            w.desc = Label(d, "Desc", "", 17, tl, tl, new Vector2(24f, -56f), new Vector2(530f, 46f));
            // [E4] Dungeon banner art (Art/banner_<id>) in the top-right corner of the details.
            w.banner = UIFactory.Image(d, "Banner", null, Color.white);
            w.banner.preserveAspect = true;
            UIFactory.Place(w.banner.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -14f), new Vector2(240f, 80f));
            // [TRAILER] A raid banner with a boss trailer plays it on click (the 보스 영상 tag says so).
            var bannerClick = w.banner.gameObject.AddComponent<Button>();
            bannerClick.targetGraphic = w.banner;
            bannerClick.transition = Selectable.Transition.None;
            bannerClick.onClick.AddListener(() => { if (w.Selected != null && BossTrailer.Play(w.Selected)) Game.Audio.PlaySfx("select"); });
            var badge = Panel(w.banner.transform, "TrailerBadge", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-4f, 4f), new Vector2(104f, 26f), new Color32(10, 12, 20, 215));
            badge.raycastTarget = false;
            var badgeText = Label(badge.transform, "Text", L.T("보스 영상 보기"), 15, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104f, 26f), TextAnchor.MiddleCenter);
            badgeText.color = UIColors.Highlight;
            badgeText.resizeTextForBestFit = true; // [TOUCH] the phone font floor would spill out of the tag
            badgeText.resizeTextMinSize = 10;
            badgeText.resizeTextMaxSize = badgeText.fontSize;
            w.trailerBadge = badge.gameObject;
            w.info = Label(d, "Info", "", 17, tl, tl, new Vector2(24f, -106f), new Vector2(780f, 46f));
            w.diffTitle = Label(d, "DiffTitle", L.T("<b>난이도</b>"), 19, tl, tl, new Vector2(24f, -160f), new Vector2(780f, 26f));
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                diffButtonsInit(w, d, index);
            }
            w.raidLine = Label(d, "RaidLine", "", 17, tl, tl, new Vector2(24f, -188f), new Vector2(780f, 46f));
            w.recommend = Label(d, "Recommend", "", 17, tl, tl, new Vector2(24f, -246f), new Vector2(380f, 46f));
            w.partyText = Label(d, "Party", "", 17, tl, tl, new Vector2(420f, -246f), new Vector2(220f, 46f));
            w.partyButton = Button(d, "PartyButton", L.T("파티 편성"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -246f), new Vector2(150f, 44f),
                () => PartyHub.Open(null, w), 18);
            w.rewards = Label(d, "Rewards", "", 17, tl, tl, new Vector2(24f, -300f), new Vector2(780f, 26f));
            w.rewards.resizeTextForBestFit = true; // [UNIQUE] one line: the unique bonus shrinks it instead of running into the slots
            w.rewards.resizeTextMinSize = 11;
            w.rewards.resizeTextMaxSize = 17;
            w.rewards.horizontalOverflow = HorizontalWrapMode.Wrap;
            w.rewards.verticalOverflow = VerticalWrapMode.Truncate;
            for (int i = 0; i < SlotCount; i++)
            {
                var frame = Panel(d, "Slot" + i, tl, tl, new Vector2(24f + i * SlotW, -330f), new Vector2(SlotIcon + 8f, SlotIcon + 8f), new Color32(14, 20, 32, 255));
                var icon = UIFactory.Image(frame.transform, "Icon", null, Color.white);
                icon.preserveAspect = true;
                UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotIcon - 6f, SlotIcon - 6f));
                var name = Label(d, "SlotName" + i, "", 16, tl, tl, new Vector2(24f + i * SlotW - 14f, -398f), new Vector2(SlotW, 34f), TextAnchor.UpperCenter);
                // [REWARD 2026-10-14] A click on a reward slot shows this dungeon's Unique / Legendary chances.
                frame.raycastTarget = true;
                var click = frame.gameObject.AddComponent<Button>();
                click.targetGraphic = frame;
                click.transition = Selectable.Transition.None;
                click.onClick.AddListener(w.ShowRareChances);
                w.slots.Add((frame, icon, name));
            }
            w.status = Label(d, "Status", "", 17, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 22f), new Vector2(380f, 50f), TextAnchor.LowerLeft);
            w.enterButton = Button(d, "Enter", L.T("입장"), "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(240f, 62f), () => w.TryEnter(), 28);
            // [DUNGEON 2026-10-14] 요일 던전 초기화권: shown over the enter row when today's entries are used up and one is in the bag.
            w.resetButton = Button(d, "ResetEntries", L.T("초기화권 사용"), "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 90f), new Vector2(240f, 44f), () => w.UseResetTicket(), 20);
            w.resetButton.gameObject.SetActive(false);
            w.BuildSweep(d); // [SWEEP] "소탕" button and its panel over the detail
            w.BuildRaidShop(d); // [RAID] "레이드 상점" button (raid tab) and its panel
            w.hint = Label(w.content, "Hint", "", 16, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -4f), new Vector2(820f, 24f), TextAnchor.UpperRight);
            return w;
        }

        static void diffButtonsInit(DungeonSelectScreen w, Transform d, int index)
        {
            var b = Button(d, "Diff" + index, DungeonDatabase.Difficulty((DungeonDifficulty)index).name, "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f + index * (DiffW + DiffGap), -188f), new Vector2(DiffW, DiffH), () => w.PickDifficulty((DungeonDifficulty)index), 20);
            w.diffButtons[index] = b;
            w.diffLabels[index] = b.GetComponentInChildren<Text>();
        }

        /// <summary>Opens the window on the weekday tab or the raid tab.</summary>
        public void Open(bool raid, string keeperName = null, string keeperLine = null)
        {
            SetKeeper(keeperName, keeperLine);
            tab = raid ? TabRaid : TabWeekday;
            selected = -1;
            Game.Flow.OpenWindow(this);
        }

        /// <summary>[GROWTH] Opens the window on <paramref name="dungeonId"/>'s tab with that dungeon and difficulty chosen.</summary>
        public void OpenOn(string dungeonId, DungeonDifficulty d)
        {
            var def = DungeonDatabase.Get(dungeonId);
            if (def == null) { Open(false); return; }
            SetKeeper(null, null);
            tab = def.isRaid ? TabRaid : def.always ? TabNormal : TabWeekday;
            var list = Listed;
            selected = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].id == dungeonId) selected = i;
            difficulty = d;
            Game.Flow.OpenWindow(this);
        }

        void SetTab(int t)
        {
            if (tab == t) return;
            tab = t;
            selected = -1;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        /// <summary>Raids by entry level (low to high); weekday dungeons open today first, then the rest.</summary>
        IReadOnlyList<DungeonDef> Listed
        {
            get
            {
                var list = new List<DungeonDef>(raidTab ? DungeonDatabase.Raids : tab == TabNormal ? DungeonDatabase.Normal : DungeonDatabase.Weekday);
                var now = ResetClock.Now;
                if (raidTab) return list.OrderBy(d => DungeonDatabase.DifficultyFor(d, DungeonDifficulty.Normal).recommendedLevel).ThenBy(d => list.IndexOf(d)).ToList();
                return list.OrderBy(d => ResetClock.IsOpen(d, now) ? 0 : 1).ThenBy(d => list.IndexOf(d)).ToList();
            }
        }

        DungeonDef Selected
        {
            get
            {
                var list = Listed;
                return selected >= 0 && selected < list.Count ? list[selected] : null;
            }
        }

        /// <summary>[UX] The kind of a dungeon as a small coloured tag: 요일 던전 (orange), 일반 던전 (green), 레이드 (red).</summary>
        static string KindBadge(DungeonDef d) =>
            d.isRaid ? L.T("<size=16><color=#ff7a6b>[레이드]</color></size>")
            : d.always ? L.T("<size=16><color=#8fe28f>[일반 던전]</color></size>")
            : L.T("<size=16><color=#ffb347>[요일 던전]</color></size>");

        void BuildRows()
        {
            foreach (var r in rows) Destroy(r.bg.gameObject);
            rows.Clear();
            builtTab = tab;
            var list = Listed;
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                var r = new Row { def = list[i] };
                r.bg = Panel(listRoot, "Row_" + list[i].id, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * (RowH + RowGap)), new Vector2(ListW, RowH), RowColor);
                r.bg.raycastTarget = true;
                var b = r.bg.gameObject.AddComponent<Button>();
                b.targetGraphic = r.bg;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { selected = index; Game.Audio.PlaySfx("select"); Refresh(); });
                r.stripe = Panel(r.bg.transform, "Stripe", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(6f, RowH), Color.white);
                r.text = Label(r.bg.transform, "Text", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -8f), new Vector2(ListW - 30f, 84f));
                // [UI] Lines (name, level band, entries) shrink to fit the row instead of running past its right edge / the tag.
                r.text.resizeTextForBestFit = true;
                r.text.resizeTextMinSize = 12;
                r.text.resizeTextMaxSize = r.text.fontSize;
                r.text.verticalOverflow = VerticalWrapMode.Truncate;
                r.text.rectTransform.sizeDelta = new Vector2(ListW - 30f - 96f, r.text.rectTransform.sizeDelta.y); // clear of the tag (top-right)
                r.tag = Label(r.bg.transform, "Tag", "", 17, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(110f, 56f), TextAnchor.UpperRight);
                rows.Add(r);
            }
        }

        protected override void Refresh()
        {
            if (Game.Session == null) return;
            var now = ResetClock.Now;
            var progress = Game.Session.Dungeons;
            if (rows.Count == 0 || builtTab != tab) BuildRows();
            var list = Listed;
            if (selected < 0 || selected >= list.Count)
            {
                selected = 0;
                for (int i = 0; i < list.Count; i++)
                    if (ResetClock.IsOpen(list[i], now)) { selected = i; break; }
            }

            tabWeekday.image.sprite = UiTheme.Tab(tab == TabWeekday);
            tabNormal.image.sprite = UiTheme.Tab(tab == TabNormal);
            tabRaid.image.sprite = UiTheme.Tab(raidTab);
            TextOf(tabWeekday).text = L.F("요일 던전 <size=15><color=#ffe066>{0}/{1}</color></size>", progress.EntriesLeft(now), DungeonDatabase.DailyEntries);
            TextOf(tabNormal).text = L.T("일반 던전 <size=15><color=#8fe28f>무제한</color></size>");
            var day = ResetClock.GameDay(now);
            string weekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday ? L.T("  <color=#8fe28f>주말: 모든 요일던전 개방</color>") : "";
            dayText.text = tab == TabNormal
                ? L.T("<color=#8fe28f>일반 던전: 매일 개방 · 입장 횟수 제한 없음</color>   <color=#b8c4d8>사냥 대신 도는 성장용</color>")
                : raidTab
                ? L.F("오늘 <color=#ffe066>{0}</color>   봉인 열쇠 조각 <color=#ffe066>{1}</color> <color=#b8c4d8>(중간 레이드 보상)</color>   <color=#b8c4d8>초기화 매일 06:00 · 주간 목 06:00</color>", L.T(DungeonDatabase.DayName(day)), Game.Session.Inventory.Count(DungeonDatabase.SealKey))
                : L.F("오늘 <color=#ffe066>{0}</color>{1}   남은 입장 <color=#ffe066>{2}/{3}</color>   <color=#b8c4d8>초기화 06:00</color>", L.T(DungeonDatabase.DayName(day)), weekend, progress.EntriesLeft(now), DungeonDatabase.DailyEntries);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool open = ResetClock.IsOpen(r.def, now);
                bool sel = i == selected;
                r.bg.color = sel ? RowSelected : open ? RowColor : RowClosed;
                r.stripe.color = sel ? (Color)UIColors.Highlight : open ? new Color32(110, 200, 130, 255) : new Color32(70, 76, 90, 255);
                string nameColor = open ? "#ffffff" : "#8c96a8";
                // [UI] Weekday dungeons show their level span too (normal to the hardest difficulty), like the raids.
                string lv = r.def.isRaid ? $"Lv.{DungeonDatabase.DifficultyFor(r.def, DungeonDifficulty.Normal).recommendedLevel} · "
                    : $"Lv.{DungeonDatabase.DifficultyFor(r.def, DungeonDifficulty.Normal).recommendedLevel}~{DungeonDatabase.DifficultyFor(r.def, DungeonDifficulty.Hero).recommendedLevel} · ";
                r.text.text = $"<size=24><b><color={nameColor}>{L.T(r.def.name)}</color></b></size>\n<color=#b8c4d8>{lv}{L.T(r.def.themeName)} · {L.T(DungeonDatabase.OpenDaysLabel(r.def))}</color>\n<size=15><color=#ffe066>{L.T(r.def.specialty)}</color></size>";
                r.tag.text = KindBadge(r.def) + "\n" + (r.def.always ? L.T("<color=#8fe28f><b>매일</b></color>") : open ? L.T("<color=#8fe28f><b>오늘 개방</b></color>") : L.T("<color=#8c96a8>닫힘</color>"));
                // Open today: a warm glow on the row so it reads as the one to play.
                if (open && !sel) r.bg.color = new Color32(34, 58, 52, 245);
            }

            var def = Selected;
            if (def == null) return;
            // [RAID] A raid has 레이드 and, once cleared, 영웅 레이드.
            if (def.isRaid && (difficulty != DungeonDatabase.RaidHeroic || !progress.IsUnlocked(def, difficulty))) difficulty = DungeonDifficulty.Normal;
            var numbers = DungeonDatabase.DifficultyFor(def, difficulty);
            banner.sprite = Game.Art.Get("banner_" + def.id);
            banner.enabled = banner.sprite != null;
            bool trailer = banner.enabled && BossTrailer.Has(def);
            banner.raycastTarget = trailer;
            trailerBadge.SetActive(trailer);
            // [TRAILER] The first time a raid is picked here once it can be entered, its boss trailer plays (leader or member
            // alike, and never in the way of a departure). The banner plays it again.
            if (trailer && gameObject.activeInHierarchy && Game.Dungeon != null && Game.Dungeon.CannotEnterReason(def, difficulty, now) == null)
                BossTrailer.PlayOnce(def, null);
            title.text = $"{KindBadge(def)}  <b>{L.T(def.name)}</b>  <size=20><color=#b8c4d8>{L.T(def.themeName)}</color></size>";
            desc.text = L.T(def.description);
            info.text = L.F("<color=#ffe066>보스</color> {0}    <color=#ffe066>특징 몬스터</color> {1}", L.T(def.bossName), L.T(def.featureMonster)) + "\n" +
                        L.F("<color=#b8c4d8>방 {0}개 (보스 포함) · 최대 {1}인 · 부활 1인 {2}회 (파티 {3}회)</color>", def.RoomCount, def.maxParty, numbers.revives, numbers.revives + DungeonDatabase.PartyReviveBonus);

            for (int i = 0; i < diffButtons.Length; i++)
            {
                var d = (DungeonDifficulty)i;
                bool show = !def.isRaid || i <= (int)DungeonDatabase.RaidHeroic;
                diffButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                // Raid: two small buttons on the schedule line (the reward lines keep their place below).
                var rt = diffButtons[i].GetComponent<RectTransform>();
                rt.anchoredPosition = def.isRaid ? new Vector2(RaidDiffX + i * (RaidDiffW + DiffGap), -154f) : new Vector2(24f + i * (DiffW + DiffGap), -188f);
                rt.sizeDelta = def.isRaid ? new Vector2(RaidDiffW, 32f) : new Vector2(DiffW, DiffH);
                bool unlocked = progress.IsUnlocked(def, d);
                bool sel = d == difficulty;
                var best = progress.BestRank(def.id, d);
                string label = def.isRaid ? (d == DungeonDatabase.RaidHeroic ? L.T("영웅") : L.T("레이드")) : L.T(DungeonDatabase.Difficulty(d).name);
                diffLabels[i].text = unlocked ? (best.HasValue ? $"{label}  <size=16><color=#ffe066>{best.Value}</color></size>" : label) : L.F("<color=#9aa0aa>{0} (잠김)</color>", label);
                diffButtons[i].image.sprite = Game.Art.Get(sel ? "ui_btn" : "ui_btngray");
                diffButtons[i].image.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }

            var local = Game.Player;
            var cls = local != null ? local.Class : Game.Session.PlayerClass;
            int level = Game.Session.Progression.Level;
            int power = local != null && local.Data != null ? local.Data.Stats.Power(cls) : 0;
            string lvColor = level >= numbers.recommendedLevel ? "#8fe28f" : "#ff9f7a";
            string pwColor = power >= numbers.recommendedPower ? "#8fe28f" : "#ff9f7a";
            recommend.text = L.F("<color=#b8c4d8>{0}</color>  Lv.{1} · 전투력 {2:N0}", def.isRaid ? L.T("입장") : L.T("권장"), numbers.recommendedLevel, numbers.recommendedPower) + "\n" +
                             L.F("<color=#b8c4d8>내 캐릭터</color>  <color={0}>Lv.{1}</color> · <color={2}>전투력 {3:N0}</color>", lvColor, level, pwColor, power);

            // [UX] Who actually goes in: online party people first, then the AI 동료 that fill the free seats
            // (the same count DungeonDirector.EnterAsParty sends), so this line and the entry always agree.
            var names = new List<string> { L.T("나") };
            int ai;
            var pc = PartyClient.Instance;
            if (OnlineSession.Playing && pc != null && pc.InParty && pc.Members.Count >= 2)
            {
                foreach (var m in pc.Members) if (!m.me) names.Add(m.name);
                var roster = Game.Session.PartyRoster;
                ai = pc.IsLeader ? Mathf.Min(roster.Count, Mathf.Max(0, PartyManager.MaxMembers - pc.Members.Count)) : 0;
                for (int i = 0; i < ai; i++) { var merc = MercenaryDatabase.Get(roster[i]); names.Add(merc != null ? L.T(merc.name) : "AI"); }
            }
            else
            {
                var party = Game.Party;
                if (party != null)
                    for (int i = 1; i < party.Members.Count; i++) if (party.Members[i] != null) names.Add(party.Members[i].DisplayName);
                ai = party != null ? party.AiCount : 0;
            }
            int size = names.Count;
            partyText.text = L.F("<b>파티</b> <color=#ffe066>{0}/{1}</color> · 체력 ×{2:0.0#}", size, def.maxParty, DungeonDatabase.PartyScale(size - ai, ai, def.isRaid)) + $"\n<size=15>{string.Join(" · ", names)}</size>";

            // Raid: its own schedule and reward status on one line where the difficulty buttons would be.
            raidLine.gameObject.SetActive(def.isRaid);
            string schedule = def.isRaid ? (def.raidTier == RaidTier.Mid ? L.F("{0} 개방 · 하루 1회 보상", L.T(DungeonDatabase.OpenDaysLabel(def))) : L.F("{0} 개방 · 주 1회 보상", L.T(DungeonDatabase.OpenDaysLabel(def)))) : "";
            diffTitle.text = def.isRaid ? L.F("<b>레이드 일정</b>   <color=#b8c4d8>{0}</color>", schedule) : L.T("<b>난이도</b>");
            if (def.isRaid && difficulty == DungeonDatabase.RaidHeroic)
            {
                // [RAID] 영웅 레이드: harder numbers, its own weekly reward.
                string keys = def.raidTier == RaidTier.Final ? L.F(" · 봉인 열쇠 조각 {0}개 필요", def.keyCost) : "";
                raidLine.text = progress.RaidRewardAvailable(def, now, difficulty)
                    ? L.F("<color=#ffd34a>{0}</color>  체력 x{1:0.0} · 피해 x{2:0.0} · 패턴 빨라짐 · 보스전 {3:0}분 제한 · 확정 골드 x{4:0.0} · 고대의 핵 +1\n<color=#b8c4d8>보상은 주 1회(목 06:00 초기화), 레이드 보상과 따로 받습니다{5}.</color>", L.T(DungeonDatabase.RaidHeroicName), DungeonDatabase.RaidHeroicHpMul, DungeonDatabase.RaidHeroicDamageMul, DungeonDatabase.RaidHeroicTimeLimit / 60f, DungeonDatabase.RaidHeroicRewardMul, keys)
                    : L.T("<color=#ff9f7a>이번 주 심화 레이드 보상을 받았습니다 · 지금은 연습 입장</color>\n<color=#b8c4d8>보상은 매주 목요일 06:00에 다시 열립니다.</color>");
            }
            else if (def.isRaid)
            {
                // Two lines: what this entry gives now, then where its keys come from.
                string state, how;
                if (!progress.RaidRewardAvailable(def, now))
                {
                    state = def.raidTier == RaidTier.Mid ? L.T("<color=#ff9f7a>오늘 보상을 받았습니다 · 지금은 연습 입장</color>") : L.T("<color=#ff9f7a>이번 주 보상을 받았습니다 · 지금은 연습 입장</color>");
                    how = def.raidTier == RaidTier.Mid ? L.T("<color=#b8c4d8>보상은 매일 06:00에 다시 열립니다.</color>") : L.T("<color=#b8c4d8>보상은 매주 목요일 06:00에 다시 열립니다.</color>");
                }
                else if (def.raidTier == RaidTier.Mid)
                {
                    state = L.F("보상  봉인 열쇠 조각 <color=#ffe066>{0}~{1}개</color> · 이번 주 클리어 {2}/3", def.keyMin, def.keyMax, progress.RaidClearsThisWeek(def, now));
                    how = L.T("<color=#b8c4d8>모은 조각은 최종 레이드 보상 조건에 쓰입니다.</color>");
                }
                else
                {
                    int have = Game.Session.Inventory.Count(DungeonDatabase.SealKey);
                    state = L.F("보상 조건  봉인 열쇠 조각 <color=#ffe066>{0}개</color> <color={1}>(보유 {2})</color>", def.keyCost, have >= def.keyCost ? "#8fe28f" : "#ff9f7a", have);
                    how = L.F("<color=#b8c4d8>조각은 <color=#8fe28f>{0}</color> 클리어 보상으로 얻습니다 · 모자라면 연습 입장</color>", KeySources());
                }
                raidLine.text = $"{state}\n{how}";
            }
            string cardRule = def.raidReward != null ? L.T("확정 골드 + 카드 4장 모두") : def.sure != null ? L.T("확정 재료 + 확정 골드 + 행운 카드 2장 모두") : L.T("카드 4장 중 1장");
            rewards.text = L.F("<color=#ffe066>보상</color>  {0}   <color=#b8c4d8>클리어 경험치 {1} (내 레벨 기준) + 랭크 보너스(SSS +50%)</color>", cardRule, Progression.XpPercent(DungeonRewards.ClearXp(def, numbers, DungeonRank.C), level));
            // [UNIQUE 0043] Weekday dungeons: the unique bonus and how many clears until one is guaranteed.
            if (!def.isRaid && !def.always && def.sure != null && SweepClient.UniqueChancePct > 0)
                rewards.text += "   " + L.F("<color=#ffb347>유니크 보너스</color>  클리어마다 {0}% · <color=#ffe066>유니크 보장까지 {1}판</color>", SweepClient.UniqueChancePct, SweepClient.UniquePityLeft);
            var list2 = DungeonRewards.Slots(def, numbers, cls);
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                bool on = i < list2.Count;
                sl.frame.gameObject.SetActive(on);
                sl.name.gameObject.SetActive(on);
                if (!on) continue;
                var it = list2[i];
                sl.icon.sprite = Game.Art.Get(it.icon);
                sl.frame.color = it.special ? new Color32(96, 70, 22, 255) : new Color32(14, 20, 32, 255);
                sl.name.text = it.special ? $"<color=#ffd34a>{L.T(it.name)}</color>" : L.T(it.name);
            }

            string reason = Game.Dungeon != null ? Game.Dungeon.CannotEnterReason(def, difficulty, now) : L.T("준비 중");
            string practice = reason == null ? PracticeReason(def, now) : null;
            status.text = reason != null ? $"<color=#ff9f7a>{L.T(reason)}</color>" : practice != null ? L.F("<color=#ffe066>보상 없는 연습 입장입니다.</color> <color=#b8c4d8>{0}</color>", practice) : L.T("<color=#8fe28f>입장할 수 있습니다.</color>");
            enterButton.image.color = reason == null ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            enterButton.image.sprite = Game.Art.Get(practice != null ? "ui_btngray" : "ui_btn");
            TextOf(enterButton).text = practice != null ? L.T("연습 입장") : L.T("입장");
            // [UX] A party member does not enter by itself: say so before the button is pressed.
            if (OnlineSession.Playing && pc != null && pc.InParty && pc.Members.Count >= 2 && !pc.IsLeader)
            {
                status.text = L.T("<color=#ffe066>파티 중입니다. 방장이 입장하면 함께 들어갑니다.</color>");
                enterButton.image.color = new Color(1f, 1f, 1f, 0.45f);
                TextOf(enterButton).text = L.T("방장 대기");
            }
            int resetTickets = Game.Session.Inventory.Count(ConsumableDatabase.DungeonResetTicket);
            resetButton.gameObject.SetActive(DungeonDatabase.CountsEntry(def) && progress.EntriesLeft(now) <= 0 && resetTickets > 0);
            TextOf(resetButton).text = L.F("초기화권 사용 ({0})", resetTickets);
            RefreshSweep(); // sets whether 소탕 shows, which the hint below follows
            RefreshRaidShopButton(); // [RAID] 레이드 상점 takes 소탕's place on the raid tab
            var input = Game.Input;
            string sweepKey = sweepButton != null && sweepButton.gameObject.activeSelf ? L.F("   {0} 소탕", input.GetBindingLabel(GameAction.UseItem)) : "";
            sweepKey += RaidShopKeyHint();
            hint.text = L.F("<color=#b8c4d8>↑/↓ 던전   ←/→ 난이도   {0} 입장   Tab / LB·RB 탭 전환{1}   {2} 파티 편성   {3} 닫기</color>", input.GetBindingLabel(GameAction.Submit), sweepKey, input.GetBindingLabel(GameAction.UseMana), input.GetBindingLabel(GameAction.Cancel));
            TextOf(partyButton).text = L.F("파티 편성 <size=15><color=#b8c4d8>[{0}]</color></size>", input.GetBindingLabel(GameAction.UseMana));
        }

        /// <summary>The mid raids that drop seal key fragments, by name ("해골왕").</summary>
        static string KeySources()
        {
            var names = new List<string>();
            foreach (var r in DungeonDatabase.Raids) if (r.keyMax > 0) names.Add(L.T(r.name));
            return string.Join("·", names);
        }

        void PickDifficulty(DungeonDifficulty d)
        {
            var def = Selected;
            if (def == null || (def.isRaid && d > DungeonDatabase.RaidHeroic)) return;
            if (!Game.Session.Dungeons.IsUnlocked(def, d))
            {
                GameEvents.RaiseToast(def.isRaid ? L.F("{0} 레이드를 먼저 한 번 클리어하면 심화 레이드가 열립니다.", L.T(def.name)) : L.F("{0} 난이도를 먼저 클리어해야 합니다.", L.T(DungeonDatabase.Difficulty(d - 1).name)));
                Game.Audio.PlaySfx("cancel");
                return;
            }
            difficulty = d;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>Why a raid entry would give no reward (null = it gives the reward). Daily dungeons always reward.</summary>
        string PracticeReason(DungeonDef def, DateTime now) => PracticeReason(def, difficulty, now);

        static string PracticeReason(DungeonDef def, DungeonDifficulty difficulty, DateTime now)
        {
            if (def == null || !def.isRaid) return null;
            if (!Game.Session.Dungeons.RaidRewardAvailable(def, now, difficulty))
                return def.raidTier == RaidTier.Mid && difficulty != DungeonDatabase.RaidHeroic ? L.T("오늘 보상을 이미 받았습니다.") : L.T("이번 주 보상을 이미 받았습니다.");
            if (def.keyCost > 0 && Game.Session.Inventory.Count(DungeonDatabase.SealKey) < def.keyCost) return L.T("봉인 열쇠 조각이 모자랍니다.");
            return null;
        }

        bool TryEnter() => Enter(Selected, difficulty, Refresh);

        /// <summary>
        /// [UX] The one way into a dungeon, from this window or the party window's 출발: the practice-entry question, then DungeonDirector.Enter (with a party it takes everyone, the free seats get the
        /// AI companions). <paramref name="refresh"/> runs when the entry was refused.
        /// </summary>
        public static bool Enter(DungeonDef def, DungeonDifficulty difficulty, System.Action refresh = null)
        {
            if (def == null || Game.Dungeon == null) return false;
            string practice = Game.Dungeon.CannotEnterReason(def, difficulty, ResetClock.Now) == null ? PracticeReason(def, difficulty, ResetClock.Now) : null;
            // Automated runs (-dotrpgMeasure, captures) have nobody to answer the practice confirm: they enter straight away.
            if (practice != null && FindAnyObjectByType<DevCapture>() == null)
            {
                Game.UI.Confirm(practice + "\n" + L.T("<size=18>클리어해도 보상은 없고, 이야기 진행만 이어집니다.</size>\n연습으로 입장할까요?"), () => { if (!Game.Dungeon.Enter(def, difficulty)) refresh?.Invoke(); }, defaultYes: true, overlay: true); // [UX] nothing is lost: cursor on 예
                return false;
            }
            bool ok = Game.Dungeon.Enter(def, difficulty);
            if (!ok) refresh?.Invoke();
            return ok;
        }

        protected override bool UsesTabKey => true;
        protected override bool HasKeyTags => true;

        protected override void Update()
        {
            if (TouchUi.Enabled) FitTouch(); // [TOUCH] DungeonSelectScreen.Mobile.cs
            if (BossTrailer.Blocking) return; // [TRAILER] keys skip the trailer, they do not reach the window
            // [SWEEP] The sweep panel takes Esc (closes itself, not the window) and blocks the list keys under it.
            if (SweepOpen)
            {
                if (TakesInput && (Game.Input.CancelPressed || Game.Input.InventoryPressed)) CloseSweep();
                return;
            }
            if (RaidShopOpen) { RaidShopInput(); return; } // [RAID] the shop panel (and its box popup) takes the keys
            base.Update();
            if (!TakesInput || !gameObject.activeInHierarchy) return;
            var input = Game.Input;
            Vector2 move = input.Move;
            int dx = move.x > 0.5f && lastMoveX <= 0.5f ? 1 : move.x < -0.5f && lastMoveX >= -0.5f ? -1 : 0;
            int dy = move.y > 0.5f && lastMoveY <= 0.5f ? -1 : move.y < -0.5f && lastMoveY >= -0.5f ? 1 : 0;
            lastMoveX = move.x;
            lastMoveY = move.y;
            if (dy != 0 && rows.Count > 0)
            {
                selected = (selected + dy + rows.Count) % rows.Count;
                Game.Audio.PlaySfx("select");
                Refresh();
            }
            if (dx != 0 && Selected != null)
            {
                int top = Selected.isRaid ? (int)DungeonDatabase.RaidHeroic : DungeonDatabase.DifficultyCount - 1;
                var next = (DungeonDifficulty)Mathf.Clamp((int)difficulty + dx, 0, top);
                if (next != difficulty) PickDifficulty(next);
            }
            // [UX] Tab / LB · RB switch the tab; A (Submit) only enters (A was bound to both, so the pad could never enter).
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton4) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton5)) SetTab((tab + 1) % 3);
            else if (input.SubmitPressed) TryEnter();
            // [UX] The mouse-only buttons get keys too: 1 / Y 소탕 (weekday dungeons online), 2 / L3 파티 편성.
            else if (input.UseItemPressed && sweepButton != null && sweepButton.gameObject.activeInHierarchy) OpenSweep();
            else if (input.UseItemPressed && raidShopButton != null && raidShopButton.gameObject.activeInHierarchy) OpenRaidShop();
            else if (input.UseManaPressed) { Game.Audio.PlaySfx("select"); PartyHub.Open(null, this); }
        }

        void ShowRareChances()
        {
            var def = Selected;
            if (def == null || Game.Player == null) return;
            Game.Audio.PlaySfx("select");
            GameEvents.RaiseToast(DungeonRewards.RareChanceText(def, DungeonDatabase.DifficultyFor(def, difficulty), Game.Player.Class));
        }

        void UseResetTicket()
        {
            if (Game.Player != null && Game.Player.UseConsumable(ConsumableDatabase.DungeonResetTicket)) Refresh();
        }

        // ---------- Automated checks ----------

        public bool DevSelect(string dungeonId, DungeonDifficulty d)
        {
            var def = DungeonDatabase.Get(dungeonId);
            if (def == null) return false;
            tab = def.isRaid ? TabRaid : def.always ? TabNormal : TabWeekday;
            var list = Listed;
            for (int i = 0; i < list.Count; i++) if (list[i].id == dungeonId) selected = i;
            difficulty = d;
            Refresh();
            return Selected == def;
        }

        public bool DevEnter() => TryEnter();
        public string DevStatus => status != null ? status.text : "";
        public string DevDayText => dayText != null ? dayText.text : "";
        public bool DevRaidTab => raidTab;

        /// <summary>"개방" / "닫힘" tag of a listed dungeon row (null if not listed on this tab).</summary>
        public string DevRowTag(string dungeonId)
        {
            foreach (var r in rows) if (r.def.id == dungeonId) return r.tag.text;
            return null;
        }
    }
}
