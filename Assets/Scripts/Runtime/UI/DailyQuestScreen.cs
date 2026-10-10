using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [DAILY 14] 일일 의뢰 window (side menu), two tabs: 일일 (today's three jobs plus yesterday's unfinished ones) and
    /// 주간 (three a week, Thursday 06:00). Each row has its progress, reward and a take / collect button; everything
    /// comes from the server (DailyQuestClient).
    /// </summary>
    public class DailyQuestScreen : OnlineWindow
    {
        public static DailyQuestScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        const float RowH = 88f, Top = -104f;
        const int Rows = 6;

        Text header;
        Button refresh, tabDaily, tabWeekly;
        bool weekly;
        sealed class Row { public Image bg; public Text title, text, progress, reward; public Button action; }
        readonly List<Row> rows = new List<Row>();

        public static DailyQuestScreen Create(Transform canvas)
        {
            var w = CreateWindow<DailyQuestScreen>(canvas, "DailyQuests", "일일 의뢰", "menuicon_quest");
            Instance = w;
            w.tabDaily = Button(w.content, "TabDaily", "일일 의뢰", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(170f, 44f), () => w.SetTab(false), 20);
            w.tabWeekly = Button(w.content, "TabWeekly", "주간 의뢰", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, 0f), new Vector2(170f, 44f), () => w.SetTab(true), 20);
            w.header = Label(w.content, "Header", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -50f), new Vector2(1040f, 44f), TextAnchor.MiddleLeft);
            w.refresh = Button(w.content, "Refresh", "새로고침", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(150f, 44f), () => { Game.Audio.PlaySfx("select", 0.5f); DailyQuestClient.Refresh(); }, 17);
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var r = new Row();
                r.bg = Panel(w.content, "Row" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, Top - i * RowH), new Vector2(1200f, RowH - 6f), i % 2 == 0 ? RowA : RowB);
                r.title = Label(r.bg.transform, "Title", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -6f), new Vector2(560f, 30f), TextAnchor.MiddleLeft);
                r.text = Label(r.bg.transform, "Text", "", 15, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -38f), new Vector2(620f, 44f), TextAnchor.UpperLeft);
                r.progress = Label(r.bg.transform, "Progress", "", 17, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(650f, 12f), new Vector2(360f, 30f), TextAnchor.MiddleLeft);
                r.reward = Label(r.bg.transform, "Reward", "", 15, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(650f, -18f), new Vector2(380f, 30f), TextAnchor.MiddleLeft);
                r.action = Button(r.bg.transform, "Action", "수락", "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-10f, 0f), UiSizes.ClaimButton, () => w.Act(idx), UiSizes.ClaimFont);
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
                "dungeon" => "요일 던전 클리어",
                "raid" => string.IsNullOrEmpty(s.label) ? "해골왕 레이드 클리어" : s.label,
                "daily" => "일일 의뢰 완료",
                _ => s.label,
            };
            string color = n >= s.count ? "#8fe28f" : "#ffd34a";
            return s.Offered ? $"<color=#8c96a8>{what} 0/{s.count}</color>" : $"{what} <color={color}>{n}/{s.count}</color>";
        }

        static string Reward(DailyQuestClient.Slot s)
        {
            var parts = new List<string>();
            if (s.xp > 0) parts.Add($"경험치 {s.xp:N0} <color=#8c96a8>(막대의 {(s.period == "week" ? 15 : 5)}%)</color>");
            if (s.gold > 0) parts.Add($"골드 {s.gold:N0}");
            return parts.Count == 0 ? "" : string.Join(" / ", parts);
        }

        protected override void Refresh()
        {
            bool online = OnlineSession.Playing;
            tabDaily.image.sprite = UiTheme.Tab(!weekly);
            tabWeekly.image.sprite = UiTheme.Tab(weekly);
            var next = weekly ? DailyQuestClient.NextWeeklyReset : DailyQuestClient.NextReset;
            string reset = next.HasValue ? $"다음 초기화 {next.Value:MM/dd HH:mm}" : weekly ? "매주 목요일 06:00 초기화" : "매일 06:00 초기화";
            string rule = weekly ? "주 3개 · 목요일 06:00 초기화 · 이월 없음" : "하루 3개 · 못 한 의뢰는 다음 날까지";
            header.text = !online ? "<color=#8c96a8>온라인으로 접속하면 받을 수 있습니다.</color>"
                : !DailyQuestClient.Loaded ? "<color=#8c96a8>불러오는 중입니다...</color>"
                : !DailyQuestClient.Unlocked ? "<color=#8c96a8>1-19 '강해져야 한다'를 마치면 열립니다.</color>"
                : $"{(string.IsNullOrEmpty(DailyQuestClient.Giver) ? "" : DailyQuestClient.Giver + "  ·  ")}<color=#b8c4d8>{rule}</color>  ·  <color=#8c96a8>{reset}</color>";
            refresh.gameObject.SetActive(online);
            refresh.interactable = !DailyQuestClient.Busy;
            bool show = online && DailyQuestClient.Loaded && DailyQuestClient.Unlocked;
            string today = null; // ISO dates: the latest one is today, the others were carried over
            foreach (var x in Current) if (today == null || string.CompareOrdinal(x.day, today) > 0) today = x.day;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var s = show ? SlotAt(i) : null;
                r.bg.gameObject.SetActive(s != null);
                if (s == null) continue;
                bool carried = !weekly && s.day != today;
                r.title.text = $"<b>{s.title}</b>" + (carried ? "  <size=14><color=#ffb347>어제 의뢰</color></size>" : "");
                r.text.text = $"<color=#b8c4d8>{s.text}</color>";
                r.progress.text = Progress(s);
                r.reward.text = Reward(s);
                r.action.gameObject.SetActive(true);
                r.action.interactable = DailyQuestClient.CanClaim(s) || DailyQuestClient.CanAccept(s);
                TextOf(r.action).text = s.Claimed ? "완료됨" : s.Ready ? "보상 받기" : s.Offered ? "수락" : "진행 중";
            }
        }
    }
}
