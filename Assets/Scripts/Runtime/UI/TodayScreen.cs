using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TODAY] 할 일 window (Docs/PLAN_WEAKNESS_2026-10-18.md 3): "what do I do now" as one list, each line with a 가기 button
    /// that reuses an existing route (a window, auto-progress, a trip to the forge). Order: the main quest, content that just
    /// opened, mail and rewards waiting, weekday dungeons left today, raid rewards open, daily / weekly requests, gear that can
    /// be enhanced now, then the 성장 추천 lines. Empty lines are left out, locked content is never listed.
    /// Esc closes it; a window opened from here goes back to it on Esc (<see cref="BackFrom"/>).
    /// </summary>
    public sealed class TodayScreen : OnlineWindow
    {
        public static TodayScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        const float RowH = 58f, Top = -54f;
        const int Rows = 9;

        sealed class Line { public string text, button; public Action go; }
        sealed class Row { public Image bg; public Text text; public Button go; }

        readonly List<Row> rows = new List<Row>();
        readonly List<Line> lines = new List<Line>();
        Text header, pageText, emptyText;
        Button pagePrev, pageNext;
        int page;
        float refreshAt;

        /// <summary>The window a 가기 opened: Esc / back there returns to this list instead of the game (one step back).</summary>
        static MenuScreen returnFrom;

        public static TodayScreen Create(Transform canvas)
        {
            var w = CreateWindow<TodayScreen>(canvas, "Today", L.T("할 일"), "menuicon_quest");
            Instance = w;
            w.header = Label(w.content, "Header", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1200f, 44f), TextAnchor.MiddleLeft);
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var r = new Row();
                r.bg = Panel(w.content, "Row" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, Top - i * RowH), new Vector2(1200f, RowH - 4f), i % 2 == 0 ? RowA : RowB);
                r.text = Label(r.bg.transform, "Text", "", 18, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(16f, 0f), new Vector2(940f, RowH - 8f), TextAnchor.MiddleLeft);
                r.go = Button(r.bg.transform, "Go", L.T("가기"), "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-10f, 0f), new Vector2(210f, 44f), () => w.Go(idx), 18);
                w.rows.Add(r);
            }
            w.emptyText = Label(w.content, "Empty", L.T("지금 바로 할 일이 없습니다. 사냥터에서 경험치를 모아 보세요."), 20, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -200f), new Vector2(1000f, 40f), TextAnchor.MiddleCenter);
            var br = new Vector2(1f, 0f);
            w.pagePrev = Button(w.content, "Prev", "◀", "ui_btngray", br, br, new Vector2(-140f, 0f), UiSizes.PageButton, () => w.Turn(-1), UiSizes.PageFont);
            w.pageText = Label(w.content, "Page", "", 18, br, br, new Vector2(-54f, 0f), new Vector2(80f, 34f), TextAnchor.MiddleCenter);
            w.pageNext = Button(w.content, "Next", "▶", "ui_btngray", br, br, new Vector2(0f, 0f), UiSizes.PageButton, () => w.Turn(1), UiSizes.PageFont);
            return w;
        }

        /// <summary>Opens the list from anywhere (HUD button, its key, the phone menu sheet).</summary>
        public static void Open()
        {
            if (Instance == null || Game.Flow == null) return;
            returnFrom = null;
            Game.Flow.OpenWindow(Instance);
        }

        /// <summary>
        /// Called by <see cref="GameFlow.CloseInventory"/>: the window being closed was opened from this list and the player
        /// backed out of it (Esc, the bag key or its back button), so the list comes back. Any other close (entering a
        /// dungeon, travelling) goes to the game as before.
        /// </summary>
        public static bool BackFrom(MenuScreen closing)
        {
            if (Instance == null || closing == null || closing != returnFrom) return false;
            returnFrom = null;
            var input = Game.Input;
            var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            bool backed = input != null && (input.CancelPressed || input.InventoryPressed) || selected != null && selected.name == "Back";
            if (!backed) return false;
            Game.UI.ShowWindow(Instance);
            return true;
        }

        public override void Show()
        {
            base.Show();
            if (OnlineSession.Playing) { DailyQuestClient.Refresh(); BossRushClient.Refresh(); }
            page = 0;
            Refresh();
        }

        protected override void Update()
        {
            base.Update();
            // Counts change while the window is open (a dungeon finished elsewhere, mail arrived): redraw now and then.
            if (gameObject.activeInHierarchy && Time.unscaledTime >= refreshAt) Refresh();
        }

        void Turn(int delta)
        {
            int pages = Pages;
            int next = Mathf.Clamp(page + delta, 0, pages - 1);
            if (next == page) return;
            page = next;
            Game.Audio.PlaySfx("select", 0.5f);
            Refresh();
        }

        int Pages => Mathf.Max(1, (lines.Count + Rows - 1) / Rows);

        void Go(int row)
        {
            int i = page * Rows + row;
            if (i >= lines.Count || lines[i].go == null) return;
            Game.Audio.PlaySfx("select");
            lines[i].go();
        }

        protected override void Refresh()
        {
            refreshAt = Time.unscaledTime + 2f;
            Build(lines);
            int level = Game.Session != null ? Game.Session.Progression.Level : 1;
            header.text = L.F("<b>Lv.{0}</b>  ·  <color=#b8c4d8>가기를 누르면 그곳으로 바로 갑니다 · Esc로 돌아옵니다</color>", level);
            int pages = Pages;
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            foreach (var go in new Component[] { pageText, pagePrev, pageNext }) go.gameObject.SetActive(pages > 1);
            pagePrev.interactable = page > 0;
            pageNext.interactable = page < pages - 1;
            emptyText.gameObject.SetActive(lines.Count == 0);
            for (int row = 0; row < rows.Count; row++)
            {
                var r = rows[row];
                int i = page * Rows + row;
                bool used = i < lines.Count;
                r.bg.gameObject.SetActive(used);
                if (!used) continue;
                var l = lines[i];
                r.text.text = l.text;
                r.go.gameObject.SetActive(l.go != null);
                if (l.go != null) TextOf(r.go).text = l.button ?? L.T("가기");
            }
        }

        // =============================== the list ===============================

        static bool Open(ContentUnlocks.Feature f) => ContentUnlocks.IsOpen(f);

        static void Add(List<Line> list, string text, string button, Action go) => list.Add(new Line { text = text, button = button, go = go });

        /// <summary>Opens <paramref name="window"/> from this list (Esc there comes back here).</summary>
        static void OpenWindowFrom(MenuScreen window)
        {
            if (window == null) return;
            returnFrom = window;
            Game.Flow.OpenWindow(window);
        }

        static void OpenDungeonFrom(string dungeonId, DungeonDifficulty difficulty)
        {
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast(L.T("이미 던전 안에 있습니다.")); return; }
            GrowthGuide.OpenDungeon(dungeonId, difficulty);
            returnFrom = Game.UI.Dungeon;
        }

        static void OpenDungeonTab(bool raid)
        {
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast(L.T("이미 던전 안에 있습니다.")); return; }
            Game.UI.Dungeon.Open(raid);
            returnFrom = Game.UI.Dungeon;
        }

        /// <summary>To the forge of the town this map belongs to (the same trip the world map's 대장간 실내 makes).</summary>
        static void GoToForge()
        {
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast(L.T("던전에서 나간 뒤에 갈 수 있습니다.")); return; }
            string map = Game.Session != null ? Game.Session.MapId : MapRegistry.Village;
            var info = MapRegistry.Get(map);
            if (info != null && info.interiorService == NpcService.Blacksmith) { Game.Flow.CloseInventory(); GameEvents.RaiseToast(L.T("대장장이에게 말을 걸어 강화하세요.")); return; }
            string forge = MapRegistry.InteriorFor(HuntingGrounds.HomeOf(map), NpcService.Blacksmith);
            if (forge == null || !MapRegistry.Exists(forge)) forge = MapRegistry.InteriorFor(MapRegistry.Village, NpcService.Blacksmith);
            Game.Flow.CloseInventory();
            Game.Flow.TravelTo(forge);
        }

        static void Build(List<Line> list)
        {
            list.Clear();
            if (Game.Session == null || Game.Quest == null) return;
            var now = ResetClock.Now;
            int level = Game.Session.Progression.Level;
            bool online = OnlineSession.Playing;

            // 1. The main quest: go on with it (auto-progress), or, while it waits for a level, where to grow.
            var main = Game.Quest.CurrentMain();
            GrowthGuide.Invalidate();
            // 성장 추천 lines (catch-up, weekday / normal dungeon, hunting ground, requests, party): placed by kind below.
            var growth = GrowthGuide.Build();
            if (GrowthGuide.Active)
            {
                int next = GrowthGuide.NextQuestLevel();
                if (next > 0) Add(list, L.F("<color=#ffe066>메인 퀘스트</color> · Lv{0}에 다음 퀘스트가 열립니다 (지금 Lv{1})", next, level), null, null);
            }
            else if (main != null)
                Add(list, L.F("<color=#ffe066>메인 퀘스트</color> · {0}", L.T(main.DisplayTitle)), L.T("자동 진행"), () =>
                {
                    if (!QuestAutoPilot.Active) QuestAutoPilot.Toggle();
                    Game.Flow.CloseInventory();
                });

            // 1b. Mini dungeon catch-up: the fastest way to the level the main quest waits on.
            foreach (var e in growth) if (e.order == GrowthGuide.OrderCatchUp && e.action != null) Add(list, e.text, e.button, Wrap(e.action, true));

            // 2. Content that just opened (the main quest that unlocks it was just finished).
            foreach (var f in ContentUnlocks.RecentlyOpened)
            {
                var (_, go) = Route(f);
                if (go != null) Add(list, L.F("<color=#8fe28f>새로 열림</color> · {0}", ContentUnlocks.NameOf(f)), L.T("해 보기"), go);
            }

            // 3. Mail and rewards waiting.
            if (online)
            {
                int mail = OnlineServices.Auction.UnclaimedMail;
                if (mail > 0 && MailScreen.Instance != null) Add(list, L.F("받을 우편 <color=#ffd34a>{0}</color>통", mail), L.T("우편함 열기"), () => OpenWindowFrom(MailScreen.Instance));
                if (LevelRewardClient.AnyClaimable && LevelRewardScreen.Instance != null) Add(list, L.T("레벨 달성 보상을 받을 수 있습니다"), L.T("보상 받기"), () => OpenWindowFrom(LevelRewardScreen.Instance));
            }

            // 4. Weekday dungeons left today.
            foreach (var e in growth) if (e.order == GrowthGuide.OrderWeekday && e.action != null) Add(list, e.text, e.button, Wrap(e.action, true));

            // 5. Raid rewards open now (today's / this week's, the heroic one too).
            if (Open(ContentUnlocks.Feature.Raid) && Game.Dungeon != null)
                foreach (var raid in DungeonDatabase.Raids)
                {
                    foreach (var diff in new[] { DungeonDifficulty.Normal, DungeonDatabase.RaidHeroic })
                    {
                        if (Game.Dungeon.CannotEnterReason(raid, diff, now) != null) continue;
                        if (!Game.Session.Dungeons.RaidRewardAvailable(raid, now, diff)) continue;
                        var def = raid; var d = diff;
                        string label = diff == DungeonDifficulty.Normal ? L.T(raid.name) : L.F("{0} (심화)", L.T(raid.name));
                        Add(list, L.F("레이드 <color=#ff9f6b>{0}</color> · 보상을 받을 수 있습니다", label), L.T("레이드 창 열기"), () => OpenDungeonFrom(def.id, d));
                    }
                }

            // 5b. [BOSS 2026-10-20] World / guild boss tries left today (Lv.40, online).
            if (online && level >= BossRushClient.EntryLevel && BossRushClient.Loaded)
            {
                var wb = BossRushClient.WorldBoss;
                if (wb != null && wb.attemptsLeft > 0)
                    Add(list, L.F("<color=#c9a2ff>월드보스</color> · 오늘 남은 도전 {0} / {1}", wb.attemptsLeft, wb.attemptsPerDay), L.T("보스 창 열기"), () => OpenWindowFrom(OpenBoss(BossRushClient.World)));
                var gb = BossRushClient.GuildBoss;
                if (gb != null && gb.attemptsLeft > 0 && !gb.Waiting)
                    Add(list, L.F("<color=#c9a2ff>길드보스</color> · 길드 점수 {0:N0} · 오늘 남은 도전 {1} / {2}", gb.score, gb.attemptsLeft, gb.attemptsPerDay), L.T("보스 창 열기"), () => OpenWindowFrom(OpenBoss(BossRushClient.Guild)));
            }

            // 6. Daily / weekly requests.
            foreach (var e in growth) if (e.order == GrowthGuide.OrderDaily && e.action != null) Add(list, e.text, e.button, Wrap(e.action, true));

            // 7. Gear that can be enhanced right now (gold and materials for at least one attempt).
            if (Open(ContentUnlocks.Feature.Enhance))
            {
                var eq = Game.Session.Equipment;
                int ready = 0;
                foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
                {
                    string key = eq[slot];
                    if (string.IsNullOrEmpty(key)) continue;
                    var cost = eq.CostFor(key);
                    if (cost.gold > 0 && eq.CanAfford(cost)) ready++;
                }
                if (ready > 0) Add(list, L.F("지금 강화할 수 있는 장비 <color=#ffd34a>{0}</color>개 (골드·재료 충분)", ready), L.T("대장간으로"), GoToForge);
            }

            // 8. The other 성장 추천 lines (normal dungeon, hunting ground, party bonus).
            foreach (var e in growth)
            {
                if (e.order == GrowthGuide.OrderCatchUp || e.order == GrowthGuide.OrderWeekday || e.order == GrowthGuide.OrderDaily) continue;
                bool window = e.order == GrowthGuide.OrderNormal;
                Add(list, e.text, e.action != null ? e.button : null, e.action != null ? Wrap(e.action, window) : null);
            }
        }

        /// <summary>The boss window on <paramref name="kind"/>'s tab (opened by the caller).</summary>
        static MenuScreen OpenBoss(string kind)
        {
            BossRushScreen.SetTab(kind);
            return BossRushScreen.Instance;
        }

        /// <summary>A 성장 추천 action; <paramref name="window"/>: it opens a window, so Esc there comes back to this list.</summary>
        static Action Wrap(Action action, bool window) => () =>
        {
            action();
            if (window && Game.UI != null && Game.UI.Top != null && Game.UI.Top != Instance) returnFrom = Game.UI.Top;
        };

        /// <summary>Name and route of a content that just opened (the same window its menu button opens).</summary>
        static (string name, Action go) Route(ContentUnlocks.Feature f)
        {
            switch (f)
            {
                case ContentUnlocks.Feature.Enhance: return (L.T("장비 강화 (대장간)"), GoToForge);
                case ContentUnlocks.Feature.Promote: return (L.T("장비 승급 (대장간)"), GoToForge);
                case ContentUnlocks.Feature.Gem: return (L.T("보조 젬 (스킬 창)"), () => OpenWindowFrom(Game.UI.Skills));
                case ContentUnlocks.Feature.SkillStyle: return (L.T("스킬 스타일 (스킬 창)"), () => OpenWindowFrom(Game.UI.Skills));
                case ContentUnlocks.Feature.AutoHunt: return (L.T("자동 사냥"), () => { QuestAutoPilot.StartGrowth(); Game.Flow.CloseInventory(); });
                case ContentUnlocks.Feature.WeekdayDungeon: return (L.T("요일 던전"), () => OpenDungeonTab(false));
                case ContentUnlocks.Feature.NormalDungeon: return (L.T("일반 던전"), () => OpenDungeonTab(false));
                case ContentUnlocks.Feature.Raid: return (L.T("레이드"), () => OpenDungeonTab(true));
                case ContentUnlocks.Feature.PartyFinder: return (L.T("파티 찾기"), () => PartyHub.Open(PartyHub.Page.Finder, Instance));
                case ContentUnlocks.Feature.Auction: return (L.T("경매장"), () => OpenWindowFrom(AuctionScreen.Instance));
                case ContentUnlocks.Feature.Trade: return (L.T("1:1 교환 (친구 창)"), () => OpenWindowFrom(SocialScreen.Instance));
                case ContentUnlocks.Feature.Daily: return (L.T("일일·주간 의뢰"), () => OpenWindowFrom(DailyQuestScreen.Instance));
                case ContentUnlocks.Feature.Guild: return (L.T("길드"), () => OpenWindowFrom(GuildScreen.Instance));
                default: return (f.ToString(), null);
            }
        }
    }
}
