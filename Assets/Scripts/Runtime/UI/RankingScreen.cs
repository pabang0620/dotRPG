using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [RANK 2026-10-20] 랭킹 window (key Y, side menu): 월드보스 (people, best single attempt), 길드보스 (guilds, this week's score),
    /// 전투력 (people, no reward); this week or last week, the top 100 ten rows a page and my own place under them.
    /// </summary>
    public sealed class RankingScreen : OnlineWindow
    {
        public static RankingScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        static readonly Vector2 TL = new Vector2(0f, 1f);
        const int PageSize = 10;
        const float RowH = 40f, TableW = 1220f, ListTop = -100f;

        static readonly string[] Boards = { RankingClient.WorldBoard, RankingClient.GuildBoard, RankingClient.PowerBoard };
        string board = RankingClient.WorldBoard, week = "current";
        int page;
        readonly Button[] tabs = new Button[3];
        Button weekBtn, prev, next;
        Text pageText, meText, emptyText, updated;
        readonly Text[] heads = new Text[5];
        readonly List<(RectTransform row, Text rank, Text name, Text guild, Text level, Text value)> rows = new List<(RectTransform, Text, Text, Text, Text, Text)>();
        string loading;

        public static RankingScreen Create(Transform canvas)
        {
            var w = CreateWindow<RankingScreen>(canvas, "Ranking", L.T("랭킹"), "menuicon_ranking");
            Instance = w;
            string[] names = { L.T("월드보스"), L.T("길드보스"), L.T("전투력") };
            for (int i = 0; i < 3; i++)
            {
                string b = Boards[i];
                w.tabs[i] = Button(w.content, "Tab" + i, names[i], "ui_btngray", TL, TL, new Vector2(i * 160f, 0f), new Vector2(150f, 42f), () => w.Pick(b, w.week), 20);
            }
            w.weekBtn = Button(w.content, "Week", "", "ui_btngray", TL, TL, new Vector2(500f, 0f), new Vector2(180f, 42f), () => w.Pick(w.board, w.week == "current" ? "previous" : "current"), 18);
            Button(w.content, "Refresh", L.T("새로고침"), "ui_btngray", TL, TL, new Vector2(690f, 0f), new Vector2(130f, 42f), () => w.Load(), 18);
            w.updated = Label(w.content, "Updated", "", 16, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(390f, 42f), TextAnchor.MiddleRight);
            w.updated.color = UiTheme.TextSecondary;
            var head = Panel(w.content, "Head", TL, TL, new Vector2(0f, -60f), new Vector2(TableW, 32f), HeadRow);
            float[] hx = { 16f, 106f, 446f, 756f, 966f }, hw = { 80f, 330f, 300f, 200f, 240f };
            for (int i = 0; i < hx.Length; i++)
            {
                w.heads[i] = Label(head.transform, "H" + i, "", 16, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(hx[i], 0f), new Vector2(hw[i], 30f), i == 4 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
                w.heads[i].color = UiTheme.TextSecondary;
            }
            for (int i = 0; i < PageSize; i++)
            {
                var r = Row(w.content, i, ListTop, RowH, TableW);
                w.rows.Add((r, Cell(r, "Rank", 16f, 80f), Cell(r, "Name", 106f, 330f), Cell(r, "Guild", 446f, 300f, 17), Cell(r, "Level", 756f, 200f, 17),
                    Cell(r, "Value", 966f, 240f, 18, TextAnchor.MiddleRight)));
            }
            w.emptyText = Label(w.content, "Empty", "", 20, TL, TL, new Vector2(0f, ListTop - 60f), new Vector2(TableW, 40f), TextAnchor.MiddleCenter);
            w.emptyText.color = UiTheme.TextSecondary;
            w.meText = Label(w.content, "Me", "", 20, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(820f, 40f), TextAnchor.MiddleLeft);
            w.prev = Button(w.content, "Prev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 0f), UiSizes.PageButton, () => w.Turn(-1), UiSizes.PageFont);
            w.pageText = Label(w.content, "Page", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 0f), new Vector2(110f, 34f), TextAnchor.MiddleCenter);
            w.next = Button(w.content, "Next", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, UiSizes.PageButton, () => w.Turn(1), UiSizes.PageFont);
            RankingClient.Changed += () => { if (w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        /// <summary>Opens the window on <paramref name="boardId"/> (this week).</summary>
        public static void OpenBoard(string boardId)
        {
            if (Instance == null || Game.Flow == null) return;
            Instance.board = boardId;
            Instance.week = "current";
            Instance.page = 0;
            Game.Flow.OpenWindow(Instance);
        }

        public override void Show()
        {
            base.Show();
            Load();
        }

        void Pick(string b, string wk)
        {
            if (b == RankingClient.PowerBoard) wk = "current"; // power is now, not a week
            if (b == board && wk == week) return;
            board = b;
            week = wk;
            page = 0;
            Game.Audio.PlaySfx("select", .5f);
            Load();
        }

        void Load()
        {
            if (!OnlineSession.Playing) { Refresh(); return; }
            loading = board + week;
            RankingClient.Load(board, week, error =>
            {
                loading = null;
                if (error != null) SetStatus(error, false);
                Refresh();
            });
            Refresh();
        }

        void Turn(int delta)
        {
            var b = RankingClient.Get(board, week);
            int pages = b == null ? 1 : Math.Max(1, (b.entries.Count + PageSize - 1) / PageSize);
            int n = Mathf.Clamp(page + delta, 0, pages - 1);
            if (n == page) return;
            page = n;
            Refresh();
        }

        protected override void Refresh()
        {
            for (int i = 0; i < tabs.Length; i++) tabs[i].GetComponent<Image>().sprite = Game.Art.Get(Boards[i] == board ? "ui_btn" : "ui_btngray");
            weekBtn.gameObject.SetActive(board != RankingClient.PowerBoard);
            TextOf(weekBtn).text = week == "current" ? L.T("이번 주 ▶ 지난주") : L.T("지난주 ▶ 이번 주");
            bool guild = board == RankingClient.GuildBoard;
            heads[0].text = L.T("순위");
            heads[1].text = guild ? L.T("길드") : L.T("이름");
            heads[2].text = guild ? "" : L.T("길드");
            heads[3].text = guild ? "" : L.T("레벨 · 직업");
            heads[4].text = board == RankingClient.WorldBoard ? L.T("최고 1회 피해") : guild ? L.T("이번 주 점수") : L.T("전투력");

            if (!OnlineSession.Playing)
            {
                foreach (var r in rows) r.row.gameObject.SetActive(false);
                emptyText.text = L.T("온라인에서 열려요.");
                meText.text = updated.text = pageText.text = "";
                prev.gameObject.SetActive(false); next.gameObject.SetActive(false);
                return;
            }
            var b = RankingClient.Get(board, week);
            int pages = b == null ? 1 : Math.Max(1, (b.entries.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            prev.gameObject.SetActive(pages > 1); next.gameObject.SetActive(pages > 1); pageText.gameObject.SetActive(pages > 1);
            updated.text = b?.updatedAt != null ? L.F("{0:HH:mm} 기준 · 1분마다 갱신", b.updatedAt.Value) : "";
            emptyText.text = b == null ? (loading != null ? L.T("불러오는 중...") : "") : b.entries.Count == 0 ? L.T("아직 기록이 없습니다.") : "";
            for (int i = 0; i < rows.Count; i++)
            {
                int idx = page * PageSize + i;
                var r = rows[i];
                bool used = b != null && idx < b.entries.Count;
                r.row.gameObject.SetActive(used);
                if (!used) continue;
                var e = b.entries[idx];
                string medal = e.rank == 1 ? "#ffd34a" : e.rank <= 3 ? "#d6dee9" : e.rank <= 10 ? "#ff9f4a" : "#f6f0e4";
                r.rank.text = $"<color={medal}><b>{e.rank}</b></color>";
                if (guild)
                {
                    r.name.text = RankingClient.Colored(e.guildName, e.guildColor); // gold 1st, silver 2nd~10th (the server's guild_color)
                    r.guild.text = "";
                    r.level.text = "";
                    r.value.text = $"{e.value:N0}";
                }
                else
                {
                    r.name.text = RankingClient.Colored(e.name, e.nameColor);
                    r.guild.text = string.IsNullOrEmpty(e.guildName) ? "" : $"<{e.guildName}>";
                    string career = GuildClient.CareerName(e.career);
                    r.level.text = string.IsNullOrEmpty(career) ? $"Lv.{e.level}" : $"Lv.{e.level} · {career}";
                    r.value.text = $"{e.value:N0}";
                }
            }
            if (b == null || !b.hasMe || b.myRank <= 0)
                meText.text = guild ? L.T("내 길드: 순위 없음") : L.T("내 순위: 기록 없음");
            else
                meText.text = guild ? L.F("내 길드 <color=#ffe066>{0}위</color> · {1:N0}", b.myRank, b.myValue) : L.F("내 순위 <color=#ffe066>{0}위</color> · {1:N0}", b.myRank, b.myValue);
        }
    }
}
