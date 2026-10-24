using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [DAILY 14] 일일 의뢰 window (side menu), two tabs: 일일 (today's three jobs plus yesterday's unfinished ones) and
    /// 주간 (three a week, Thursday 06:00). Each row has its progress, reward and a take / collect button; everything
    /// comes from the server (DailyQuestClient). [DAILY CHEST] A strip under the tabs shows the completion chest of the
    /// tab (today's, or yesterday's while it can still be opened), its contents and the consecutive-day streak.
    /// </summary>
    public class DailyQuestScreen : OnlineWindow
    {
        public static DailyQuestScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        const float RowH = 88f, Top = -104f;
        const int Rows = 6;

        Text header, chestTitle, chestBody;
        Image chestBg;
        Button refresh, tabDaily, tabWeekly, chestOpen;
        bool weekly;
        sealed class Row { public Image bg; public Text title, text, progress, reward; public Button action; }
        readonly List<Row> rows = new List<Row>();

        public static DailyQuestScreen Create(Transform canvas)
        {
            var w = CreateWindow<DailyQuestScreen>(canvas, "DailyQuests", L.T("일일 의뢰"), "menuicon_quest");
            Instance = w;
            w.tabDaily = Button(w.content, "TabDaily", L.T("일일 의뢰"), "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(170f, 44f), () => w.SetTab(false), 20);
            w.tabWeekly = Button(w.content, "TabWeekly", L.T("주간 의뢰"), "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, 0f), new Vector2(170f, 44f), () => w.SetTab(true), 20);
            w.header = Label(w.content, "Header", "", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(362f, 0f), new Vector2(694f, 44f), TextAnchor.MiddleLeft);
            // [DAILY CHEST] completion chest strip (between the tabs and the job rows)
            w.chestBg = Panel(w.content, "Chest", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -50f), new Vector2(1200f, 50f), new Color32(70, 52, 24, 235));
            w.chestTitle = Label(w.chestBg.transform, "ChestTitle", "", 17, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(12f, 0f), new Vector2(470f, 46f), TextAnchor.MiddleLeft);
            w.chestBody = Label(w.chestBg.transform, "ChestBody", "", 15, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(490f, 0f), new Vector2(540f, 46f), TextAnchor.MiddleLeft);
            w.chestOpen = Button(w.chestBg.transform, "ChestOpen", L.T("상자 열기"), "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-8f, 0f), new Vector2(150f, 42f), w.OpenChest, 18);
            w.refresh = Button(w.content, "Refresh", L.T("새로고침"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(150f, 44f), () => { Game.Audio.PlaySfx("select", 0.5f); DailyQuestClient.Refresh(); }, 17);
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var r = new Row();
                r.bg = Panel(w.content, "Row" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, Top - i * RowH), new Vector2(1200f, RowH - 6f), i % 2 == 0 ? RowA : RowB);
                r.title = Label(r.bg.transform, "Title", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -6f), new Vector2(560f, 30f), TextAnchor.MiddleLeft);
                r.text = Label(r.bg.transform, "Text", "", 15, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -38f), new Vector2(620f, 44f), TextAnchor.UpperLeft);
                r.progress = Label(r.bg.transform, "Progress", "", 17, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(650f, 12f), new Vector2(360f, 30f), TextAnchor.MiddleLeft);
                r.reward = Label(r.bg.transform, "Reward", "", 15, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(650f, -18f), new Vector2(380f, 30f), TextAnchor.MiddleLeft);
                r.action = Button(r.bg.transform, "Action", L.T("수락"), "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-10f, 0f), UiSizes.ClaimButton, () => w.Act(idx), UiSizes.ClaimFont);
                w.rows.Add(r);
            }
            DailyQuestClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override void Show()
        {
            base.Show();
            DailyQuestClient.Refresh();
            Refresh();
        }

        List<DailyQuestClient.Slot> Current => weekly ? DailyQuestClient.WeeklySlots : DailyQuestClient.Slots;
        DailyQuestClient.Slot SlotAt(int row) => row < Current.Count ? Current[row] : null;

        void SetTab(bool toWeekly)
        {
            if (weekly == toWeekly) return;
            weekly = toWeekly;
            Game.Audio.PlaySfx("select", 0.5f);
            Refresh();
        }

        void Act(int row)
        {
            var s = SlotAt(row);
            if (s == null) return;
            if (DailyQuestClient.CanClaim(s)) DailyQuestClient.Claim(s, Answer);
            else if (DailyQuestClient.CanAccept(s)) DailyQuestClient.Accept(s, Answer);
        }

        /// <summary>The chest the strip shows: a ready one first (yesterday's before today's), else today's / this week's.</summary>
        DailyQuestClient.Chest ShownChest()
        {
            if (weekly) return DailyQuestClient.WeekChest;
            DailyQuestClient.Chest last = null;
            foreach (var c in DailyQuestClient.DayChests) { if (c != null && c.Ready) return c; last = c; }
            return last;
        }

        void OpenChest()
        {
            var c = ShownChest();
            if (DailyQuestClient.CanOpen(c)) DailyQuestClient.OpenChest(c, Answer);
        }

        static string Contents(DailyQuestClient.Chest c)
        {
            var parts = new List<string>();
            foreach (var r in c.rewards) parts.Add(L.F("{0} {1}", L.T(DungeonDatabase.ItemName(r.Key)), r.Value));
            if (c.skinGauge > 0) parts.Add(L.F("스킨 게이지 +{0}칸", c.skinGauge));
            if (c.bonusRolls > 0) parts.Add(L.F("<color=#ffd34a>보너스 {0}회</color>", c.bonusRolls));
            return string.Join(" · ", parts);
        }

        /// <summary>Streak pips for the 7-day cycle: filled up to today's step, gold on the last one.</summary>
        static string Pips(int step, int cycle, bool opened)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= cycle; i++)
            {
                bool lit = i < step || (i == step && opened);
                string col = lit ? (i == cycle ? "#ffd34a" : "#8fe28f") : i == step ? "#ffffff" : "#5a6070";
                sb.Append($"<color={col}>{(i == cycle ? "★" : "●")}</color>");
            }
            return sb.ToString();
        }

        void RefreshChest(bool show)
        {
            var c = show ? ShownChest() : null;
            chestBg.gameObject.SetActive(c != null);
            if (c == null) return;
            string name = L.T(c.name) + (c.maxLevel ? L.T(" <color=#ff9f5a>(만렙)</color>") : "");
            string progress = c.Claimed ? L.T("<color=#8fe28f>열었음</color>") : $"<color={(c.Ready ? "#8fe28f" : "#ffd34a")}>{c.done}/{c.need}</color>";
            if (c.period == "week") chestTitle.text = L.F("<b>주간 완료 상자</b>  {0}  {1}", name, progress);
            else
            {
                string when = c.day != DailyQuestClient.DayChests[DailyQuestClient.DayChests.Count - 1].day ? L.T("<color=#ffb347>어제</color> ") : "";
                string streak = DailyQuestClient.StreakGrace ? L.F("연속 {0}일 <color=#ffb347>(어제 의뢰를 오늘 끝내면 이어짐)</color>", DailyQuestClient.Streak) : L.F("연속 {0}일", DailyQuestClient.Streak);
                chestTitle.text = $"<b>{when}{name}</b>  {progress}  {Pips(c.step, DailyQuestClient.StreakCycle, c.Claimed)}  <size=14>{streak}</size>";
            }
            chestBody.text = $"<color=#e8dcc0>{Contents(c)}</color>";
            chestOpen.interactable = DailyQuestClient.CanOpen(c);
            TextOf(chestOpen).text = c.Claimed ? L.T("열었음") : c.Ready ? L.T("상자 열기") : L.T("의뢰 진행 중");
        }

        void Answer(bool ok, string msg)
        {
            Game.Audio.PlaySfx(ok ? "quest" : "cancel");
            GameEvents.RaiseToast(msg);
        }

        static string Progress(DailyQuestClient.Slot s)
        {
            int n = Mathf.Min(s.progress, s.count);
            string what = s.type switch
            {
                "dungeon" => L.T("요일 던전 클리어"),
                "raid" => string.IsNullOrEmpty(s.label) ? L.T("해골왕 레이드 클리어") : L.T(s.label),
                "daily" => L.T("일일 의뢰 완료"),
                _ => s.label,
            };
            string color = n >= s.count ? "#8fe28f" : "#ffd34a";
            return s.Offered ? $"<color=#8c96a8>{what} 0/{s.count}</color>" : $"{what} <color={color}>{n}/{s.count}</color>";
        }

        static string Reward(DailyQuestClient.Slot s)
        {
            var parts = new List<string>();
            if (s.xp > 0) parts.Add(L.F("경험치 {0:N0} <color=#8c96a8>(막대의 {1}%)</color>", s.xp, s.period == "week" ? 15 : 5));
            if (s.gold > 0) parts.Add(L.F("골드 {0:N0}", s.gold));
            return parts.Count == 0 ? "" : string.Join(" / ", parts);
        }

        protected override void Refresh()
        {
            bool online = OnlineSession.Playing;
            tabDaily.image.sprite = UiTheme.Tab(!weekly);
            tabWeekly.image.sprite = UiTheme.Tab(weekly);
            var next = weekly ? DailyQuestClient.NextWeeklyReset : DailyQuestClient.NextReset;
            string reset = next.HasValue ? L.F("다음 초기화 {0:MM/dd HH:mm}", next.Value) : weekly ? L.T("매주 목요일 06:00 초기화") : L.T("매일 06:00 초기화");
            string rule = weekly ? L.T("주 3개 · 목요일 06:00 초기화 · 이월 없음") : L.T("하루 3개 · 못 한 의뢰는 다음 날까지");
            header.text = !online ? L.T("<color=#8c96a8>온라인으로 접속하면 받을 수 있습니다.</color>")
                : !DailyQuestClient.Loaded ? L.T("<color=#8c96a8>불러오는 중입니다...</color>")
                : !DailyQuestClient.Unlocked ? L.T("<color=#8c96a8>1-15 '전직의 길'을 마치면 열립니다.</color>")
                : $"{(string.IsNullOrEmpty(DailyQuestClient.Giver) ? "" : DailyQuestClient.Giver + "  ·  ")}<color=#b8c4d8>{rule}</color>  ·  <color=#8c96a8>{reset}</color>";
            refresh.gameObject.SetActive(online);
            refresh.interactable = !DailyQuestClient.Busy;
            bool show = online && DailyQuestClient.Loaded && DailyQuestClient.Unlocked;
            RefreshChest(show);
            string today = null; // ISO dates: the latest one is today, the others were carried over
            foreach (var x in Current) if (today == null || string.CompareOrdinal(x.day, today) > 0) today = x.day;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var s = show ? SlotAt(i) : null;
                r.bg.gameObject.SetActive(s != null);
                if (s == null) continue;
                bool carried = !weekly && s.day != today;
                r.title.text = $"<b>{L.T(s.title)}</b>" + (carried ? L.T("  <size=14><color=#ffb347>어제 의뢰</color></size>") : "");
                r.text.text = $"<color=#b8c4d8>{s.text}</color>";
                r.progress.text = Progress(s);
                r.reward.text = Reward(s);
                r.action.gameObject.SetActive(true);
                r.action.interactable = DailyQuestClient.CanClaim(s) || DailyQuestClient.CanAccept(s);
                TextOf(r.action).text = s.Claimed ? L.T("완료됨") : s.Ready ? L.T("보상 받기") : s.Offered ? L.T("수락") : L.T("진행 중");
            }
        }
    }
}
