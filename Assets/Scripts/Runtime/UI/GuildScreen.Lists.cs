using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[GUILD] The one table of the guild window: guild search results, my guild's members or its applications.</summary>
    public sealed partial class GuildScreen
    {
        sealed class GuildRow
        {
            public RectTransform root;
            public Text c0, c1, c2, c3;
            public readonly Button[] buttons = new Button[3];
            public readonly Action[] actions = new Action[3];
        }

        readonly List<GuildRow> table = new List<GuildRow>();

        void BuildList()
        {
            for (int i = 0; i < PageSize; i++)
            {
                var r = new GuildRow { root = Row(content, i, -156f, RowH, TableW) };
                r.c0 = Cell(r.root, "C0", 12f, 250f);
                r.c1 = Cell(r.root, "C1", 270f, 150f, 17);
                r.c2 = Cell(r.root, "C2", 428f, 200f, 17);
                r.c3 = Cell(r.root, "C3", 636f, 190f, 16);
                for (int b = 0; b < 3; b++)
                {
                    var row = r; int k = b;
                    r.buttons[b] = Button(r.root, "B" + b, "", b == 0 ? "ui_btn" : "ui_btngray", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-8f - b * 128f, 0f), new Vector2(120f, 36f),
                        () => row.actions[k]?.Invoke(), 16);
                }
                table.Add(r);
            }
            emptyText = Label(content, "Empty", "", 20, TL, TL, new Vector2(0f, -220f), new Vector2(TableW, 40f), TextAnchor.MiddleCenter);
            emptyText.color = UiTheme.TextSecondary;
            prevBtn = Button(content, "Prev", "◀", "ui_btngray", BR, BR, new Vector2(-180f, 0f), UiSizes.PageButton, () => TurnList(-1), UiSizes.PageFont);
            pageText = Label(content, "Page", "", 18, BR, BR, new Vector2(-60f, 0f), new Vector2(110f, 34f), TextAnchor.MiddleCenter);
            nextBtn = Button(content, "Next", "▶", "ui_btngray", BR, BR, Vector2.zero, UiSizes.PageButton, () => TurnList(1), UiSizes.PageFont);
        }

        static string Safe(string s) => (s ?? "").Replace("<", "＜").Replace(">", "＞");

        void PlaceRows(float top)
        {
            for (int i = 0; i < table.Count; i++) table[i].root.anchoredPosition = new Vector2(0f, top - i * RowH);
            emptyText.rectTransform.anchoredPosition = new Vector2(0f, top - 60f);
        }

        void HideList(string empty)
        {
            foreach (var r in table) r.root.gameObject.SetActive(false);
            emptyText.text = empty;
            pageText.text = "";
            prevBtn.gameObject.SetActive(false);
            nextBtn.gameObject.SetActive(false);
        }

        void SetPaging(int pages)
        {
            pageText.text = pages > 1 ? $"{listPage + 1} / {pages}" : "";
            prevBtn.gameObject.SetActive(pages > 1);
            nextBtn.gameObject.SetActive(pages > 1);
            prevBtn.interactable = listPage > 0;
            nextBtn.interactable = listPage < pages - 1;
        }

        void TurnList(int delta)
        {
            if (page == Page.Find)
            {
                // The search is paged by the server (its page numbers start at 1).
                int pages = Math.Max(1, (GuildClient.FoundTotal + PageSize - 1) / PageSize);
                int to = Mathf.Clamp(listPage + delta, 0, pages - 1);
                if (to == listPage) return;
                listPage = to;
                Search(to + 1);
                return;
            }
            listPage = Math.Max(0, listPage + delta);
            Refresh();
        }

        void Fill(GuildRow r, string c0, string c1, string c2, string c3, params (string label, Action act, bool on)[] buttons)
        {
            r.root.gameObject.SetActive(true);
            r.c0.text = c0; r.c1.text = c1; r.c2.text = c2; r.c3.text = c3;
            for (int b = 0; b < 3; b++)
            {
                bool used = b < buttons.Length && buttons[b].label != null;
                r.buttons[b].gameObject.SetActive(used);
                r.actions[b] = used ? buttons[b].act : null;
                if (!used) continue;
                TextOf(r.buttons[b]).text = buttons[b].label;
                r.buttons[b].interactable = buttons[b].on && !GuildClient.Busy;
            }
        }

        // ---------------- search results ----------------

        void DrawListings()
        {
            PlaceRows(-56f);
            var list = GuildClient.Found;
            int pages = Math.Max(1, (GuildClient.FoundTotal + PageSize - 1) / PageSize);
            listPage = Mathf.Clamp(GuildClient.FoundPage - 1, 0, pages - 1);
            for (int i = 0; i < table.Count; i++)
            {
                var r = table[i];
                if (i >= list.Count) { r.root.gameObject.SetActive(false); continue; }
                var g = list[i];
                bool applied = GuildClient.MyApplications.Exists(a => a.guildId == g.id);
                bool can = !g.Full && !applied && !GuildClient.CoolingDown && GuildClient.MyApplications.Count < 3;
                string label = applied ? L.T("신청함") : g.Full ? L.T("인원 가득") : g.Open ? L.T("가입") : L.T("가입 신청");
                var target = g;
                Fill(r, $"<b>{Safe(g.name)}</b>", L.F("{0} / {1}명", g.memberCount, g.maxMembers), g.Open ? L.T("바로 가입") : L.T("승인 후 가입"),
                    L.F("길드장 {0}", Safe(g.leaderName)) + (string.IsNullOrEmpty(g.notice) ? "" : "  <size=14><color=#b8c4d8>" + Safe(g.notice) + "</color></size>"),
                    (label, () => GuildClient.Apply(target, (ok, msg) => Report(this, ok, msg)), can));
            }
            emptyText.text = list.Count == 0 ? L.T("찾은 길드가 없습니다. 이름으로 찾거나 직접 만들어 보세요.") : "";
            SetPaging(pages);
        }

        // ---------------- my guild ----------------

        void DrawMembers()
        {
            PlaceRows(-156f);
            var g = GuildClient.Guild;
            if (g == null) { HideList(L.T("불러오는 중...")); return; }
            int pages = Math.Max(1, (g.members.Count + PageSize - 1) / PageSize);
            listPage = Mathf.Clamp(listPage, 0, pages - 1);
            string me = OnlineSession.Current?.ActiveCharacter;
            bool leader = GuildClient.IsLeader, staff = GuildClient.IsStaff;
            for (int i = 0; i < table.Count; i++)
            {
                int idx = listPage * PageSize + i;
                var r = table[i];
                if (idx >= g.members.Count) { r.root.gameObject.SetActive(false); continue; }
                var m = g.members[idx];
                bool self = m.id == me;
                // Officers may kick plain members only; the leader may kick anyone but themself.
                bool canKick = !self && staff && !m.IsLeader && (leader || !m.IsOfficer);
                string role = m.IsLeader ? $"<color=#ffd34a>{GuildClient.RoleName(m.role)}</color>" : m.IsOfficer ? $"<color=#8fe28f>{GuildClient.RoleName(m.role)}</color>" : GuildClient.RoleName(m.role);
                string career = GuildClient.CareerName(m.career);
                var target = m;
                var buttons = new List<(string, Action, bool)>();
                if (canKick) buttons.Add((L.T("내보내기"), () => Ask(L.F("{0}님을 길드에서 내보낼까요?", target.name), () => GuildClient.Kick(target, (ok, msg) => Report(this, ok, msg))), true));
                if (leader && !self && !m.IsLeader)
                {
                    buttons.Add(m.IsOfficer
                        ? (L.T("직위 해제"), (Action)(() => GuildClient.Demote(target, (ok, msg) => Report(this, ok, msg))), true)
                        : (L.T("부길드장"), (Action)(() => GuildClient.Promote(target, (ok, msg) => Report(this, ok, msg))), true));
                    buttons.Add((L.T("길드장 위임"), () => Ask(L.F("{0}님에게 길드장을 넘길까요?\n<size=18>나는 부길드장이 됩니다.</size>", target.name), () => GuildClient.Transfer(target, (ok, msg) => Report(this, ok, msg))), true));
                }
                Fill(r, (self ? "<color=#ffe066>" + Safe(m.name) + "</color>" : Safe(m.name)), role,
                    string.IsNullOrEmpty(career) ? $"Lv.{m.level}" : $"Lv.{m.level} · {career}", GuildClient.SeenText(m.lastSeenAt), buttons.ToArray());
            }
            emptyText.text = "";
            SetPaging(pages);
        }

        void DrawApplications()
        {
            PlaceRows(-156f);
            var g = GuildClient.Guild;
            if (g == null) { HideList(""); return; }
            int pages = Math.Max(1, (g.applications.Count + PageSize - 1) / PageSize);
            listPage = Mathf.Clamp(listPage, 0, pages - 1);
            bool full = g.memberCount >= g.maxMembers;
            for (int i = 0; i < table.Count; i++)
            {
                int idx = listPage * PageSize + i;
                var r = table[i];
                if (idx >= g.applications.Count) { r.root.gameObject.SetActive(false); continue; }
                var a = g.applications[idx];
                string career = GuildClient.CareerName(a.career);
                var target = a;
                Fill(r, Safe(a.name), string.IsNullOrEmpty(career) ? $"Lv.{a.level}" : $"Lv.{a.level} · {career}", GuildClient.SeenText(a.appliedAt), "",
                    (L.T("받기"), () => GuildClient.Accept(target, (ok, msg) => Report(this, ok, msg)), !full),
                    (L.T("거절"), () => GuildClient.Reject(target, (ok, msg) => Report(this, ok, msg)), true));
            }
            emptyText.text = g.applications.Count == 0 ? L.T("들어온 가입 신청이 없습니다.") : full ? L.T("길드 인원이 가득 차 신청을 받을 수 없습니다.") : "";
            SetPaging(pages);
        }

        static void Ask(string text, Action yes) => Game.UI.Confirm(text, yes, overlay: true);
    }
}
