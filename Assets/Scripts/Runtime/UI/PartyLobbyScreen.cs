using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [PARTY] 파티 로비 (PLAN_ONLINE §2.1-4): my server party. Members with class, level, power and ready
    /// marks; the leader accepts applications, hands over the lead, kicks, sets the AI seats and departs;
    /// a member toggles ready. While the run gathers it shows who has connected to the host.
    /// </summary>
    public class PartyLobbyScreen : OnlineWindow
    {
        public static PartyLobbyScreen Instance { get; private set; }
        const float RowH = 56f, Width = 1180f;

        static PartyClient Client => PartyClient.Instance;

        Text header, runText;
        int aiCount = -1;
        readonly List<(RectTransform row, Text who, Text info, Button lead, Button kick)> memberRows = new List<(RectTransform, Text, Text, Button, Button)>();
        readonly List<(RectTransform row, Text who, Button yes, Button no)> appRows = new List<(RectTransform, Text, Button, Button)>();
        Button readyBtn, startBtn, aiBtn, listBtn, leaveBtn, beginBtn;
        Button dungeonBtn, diffBtn, inviteBtn, friendsBtn, rosterBtn;
        InputField inviteField;

        public static PartyLobbyScreen Create(Transform canvas)
        {
            var w = CreateWindow<PartyLobbyScreen>(canvas, "PartyLobby", "파티", "menuicon_party");
            Instance = w;
            w.header = Label(w.content, "Header", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Width - 340f, 40f), TextAnchor.MiddleLeft);
            // [PARTY] Leader: change the party's dungeon / difficulty after it was made.
            w.diffBtn = Button(w.content, "Diff", "난이도 ▶", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(150f, 40f), w.NextDifficulty, 17);
            w.dungeonBtn = Button(w.content, "Dungeon", "던전 변경 ▶", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-160f, 0f), new Vector2(170f, 40f), w.NextDungeon, 17);
            // [PARTY] Invite by name (no recruiting post needed: the server makes a private party), friends list, AI roster.
            w.friendsBtn = Button(w.content, "Friends", "친구 목록", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(150f, 38f), () => Game.Flow.OpenWindow(SocialScreen.Instance), 17);
            w.inviteBtn = Button(w.content, "Invite", "초대", "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-160f, -44f), new Vector2(110f, 38f), w.InviteTyped, 17);
            w.inviteField = w.Field(w.content, "InviteName", "초대할 캐릭터 이름", new Vector2(Width - 540f, -44f), new Vector2(260f, 38f));
            w.inviteField.characterLimit = 16;
            w.inviteField.onEndEdit.AddListener(_ => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) w.InviteTyped(); });
            w.focusField = w.inviteField;
            w.rosterBtn = Button(w.content, "Roster", "AI 편성", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-560f, -44f), new Vector2(130f, 38f), () => Game.Flow.OpenWindow(Game.UI.Party), 17);
            Label(w.content, "MembersHead", "<color=#b8c4d8>파티원</color>", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -48f), new Vector2(300f, 28f), TextAnchor.MiddleLeft);
            for (int i = 0; i < PartyManager.MaxMembers; i++)
            {
                var r = Row(w.content, i, -80f, RowH, Width);
                int idx = i;
                var lead = Button(r, "Lead", "방장 위임", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-140f, 0f), new Vector2(120f, 40f), () => w.MakeLeader(idx), 17);
                var kick = Button(r, "Kick", "내보내기", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(120f, 40f), () => w.Kick(idx), 17);
                w.memberRows.Add((r, Cell(r, "Who", 14f, 420f, 20), Cell(r, "Info", 440f, 460f, 18), lead, kick));
            }
            float appTop = -80f - PartyManager.MaxMembers * RowH - 20f;
            Label(w.content, "AppsHead", "<color=#b8c4d8>참가 신청</color>", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, appTop + 4f), new Vector2(300f, 28f), TextAnchor.MiddleLeft);
            for (int i = 0; i < 3; i++)
            {
                var r = Row(w.content, i, appTop - 28f, 48f, Width);
                int idx = i;
                var yes = Button(r, "Yes", "수락", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-140f, 0f), new Vector2(120f, 38f), () => w.Respond(idx, true), 17);
                var no = Button(r, "No", "거절", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(120f, 38f), () => w.Respond(idx, false), 17);
                w.appRows.Add((r, Cell(r, "Who", 14f, 700f, 19), yes, no));
            }
            w.runText = Label(w.content, "Run", "", 19, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 64f), new Vector2(Width, 34f), TextAnchor.MiddleLeft);
            float bx = 0f;
            Button Add(string name, string label, System.Action a, float width = 170f)
            {
                var b = Button(w.content, name, label, "ui_btn", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(bx, 8f), new Vector2(width, 48f), a, 19);
                bx += width + 12f;
                return b;
            }
            w.startBtn = Add("Start", "출발", w.Depart);
            w.beginBtn = Add("Begin", "지금 출발", () => PartyRunSession.Instance?.BeginNow());
            w.readyBtn = Add("Ready", "준비", w.ToggleReady);
            w.aiBtn = Add("Ai", "AI 용병", w.CycleAi, 190f);
            w.listBtn = Add("List", "모집 공개", w.ToggleListed, 190f);
            w.leaveBtn = Add("Leave", "파티 나가기", w.LeaveParty, 190f);
            w.status = Label(w.content, "Status", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 100f), new Vector2(Width, 30f), TextAnchor.MiddleLeft);
            PartyClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        void Done(bool ok, string msg, string success)
        {
            SetStatus(ok ? success : msg, ok);
            Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
            Refresh();
        }

        PartyMemberView MemberAt(int i) => Client != null && i < Client.Members.Count ? Client.Members[i] : null;

        void MakeLeader(int i)
        {
            var m = MemberAt(i);
            if (m != null) Client.MakeLeader(m.characterId, (ok, msg) => Done(ok, msg, $"{m.name}님이 방장이 되었습니다."));
        }

        void Kick(int i)
        {
            var m = MemberAt(i);
            if (m != null) Game.UI.Confirm($"{m.name}님을 파티에서 내보낼까요?", () => Client.Kick(m.characterId, (ok, msg) => Done(ok, msg, $"{m.name}님을 내보냈습니다.")), true);
        }

        void Respond(int i, bool accept)
        {
            if (Client == null || i >= Client.Applications.Count) return;
            var a = Client.Applications[i];
            Client.Respond(a.id, accept, (ok, msg) => Done(ok, msg, accept ? $"{a.name}님이 파티에 들어왔습니다." : "신청을 거절했습니다."));
        }

        void ToggleReady()
        {
            var me = Client?.Me;
            if (me != null) Client.SetReady(!me.ready, (ok, msg) => Done(ok, msg, me.ready ? "준비를 취소했습니다." : "준비 완료!"));
        }

        int FreeSeats => Client != null ? Mathf.Max(0, PartyManager.MaxMembers - Client.Members.Count) : 0;

        void CycleAi()
        {
            int max = AiAvailable;
            if (max == 0) { SetStatus("편성된 AI 동료가 없습니다. [AI 편성]에서 동료를 넣으세요.", false); return; }
            aiCount = AiShown >= max ? 0 : AiShown + 1;
            Refresh();
        }

        void InviteTyped()
        {
            string name = inviteField.text.Trim();
            if (name.Length == 0) { SetStatus("초대할 캐릭터 이름을 입력하세요.", false); return; }
            if (Client == null) return;
            Client.InviteByName(name, (ok, msg) => { if (ok) inviteField.text = ""; Done(ok, msg, $"{name}님에게 파티 초대를 보냈습니다."); });
        }

        static List<DungeonDef> Targets => new List<DungeonDef>(DungeonDatabase.Weekday) { DungeonDatabase.SkeletonKing, DungeonDatabase.Get(DungeonDatabase.RaidBargas) };

        void NextDungeon()
        {
            if (Client == null || !Client.InParty) return;
            var list = Targets.FindAll(d => d != null);
            int i = list.FindIndex(d => d.id == Client.DungeonId);
            var next = list[(i + 1) % list.Count];
            Client.SetTarget(next.id, next.isRaid ? DungeonDifficulty.Normal : Client.Difficulty,
                (ok, msg) => Done(ok, msg, $"목적지를 {next.name}(으)로 바꿨습니다."));
        }

        void NextDifficulty()
        {
            if (Client == null || !Client.InParty) return;
            var d = DungeonDatabase.Get(Client.DungeonId);
            if (d != null && d.isRaid) { SetStatus("레이드는 난이도가 하나입니다.", false); return; }
            var next = (DungeonDifficulty)(((int)Client.Difficulty + 1) % DungeonDatabase.DifficultyCount);
            Client.SetTarget(Client.DungeonId, next, (ok, msg) => Done(ok, msg, $"난이도를 {PartyFinderRules.DifficultyName(next)}(으)로 바꿨습니다."));
        }

        /// <summary>[AI] The leader's own roster fills the free seats: at most the hired mercenaries that fit.</summary>
        int AiAvailable => Mathf.Min(FreeSeats, Game.Session != null ? Game.Session.PartyRoster.Count : 0);
        int AiShown => aiCount < 0 ? AiAvailable : Mathf.Min(aiCount, AiAvailable);

        void ToggleListed() => Client?.SetListed(!Client.Listed, (ok, msg) => Done(ok, msg, Client.Listed ? "모집 글을 다시 올렸습니다." : "모집 글을 내렸습니다."));

        void Depart()
        {
            if (Client == null) return;
            int ai = AiShown;
            Client.StartRun(ai, (ok, msg) => Done(ok, msg, "출발! 파티원이 방장에게 연결되는 중입니다."));
        }

        void LeaveParty()
        {
            if (PartyRunSession.Active) { SetStatus("던전 진행 중에는 파티를 나갈 수 없습니다.", false); return; }
            Game.UI.Confirm("파티에서 나갈까요?", () => Client?.Leave((ok, msg) => { Done(ok, msg, "파티에서 나왔습니다."); if (ok) Close(); }), true);
        }

        float refreshAcc;
        protected override void Update()
        {
            base.Update();
            refreshAcc += Time.unscaledDeltaTime;
            if (refreshAcc >= 1f) { refreshAcc = 0f; Refresh(); }
        }

        protected override void Refresh()
        {
            var c = Client;
            bool inParty = c != null && c.InParty;
            var dungeon = inParty ? DungeonDatabase.Get(c.DungeonId) : null;
            header.text = !inParty ? "<color=#8c96a8>파티가 없습니다. 이름을 넣고 [초대]하면 파티가 만들어집니다(모집 글은 선택).</color>"
                : $"{dungeon?.name ?? c.DungeonId}  <color={PartyFinderRules.DifficultyColor(c.Difficulty)}>{(dungeon != null && dungeon.isRaid ? "레이드" : PartyFinderRules.DifficultyName(c.Difficulty))}</color>   {c.Members.Count}/{c.MaxMembers}명   <color=#b8c4d8>{PartyFinderRules.Recommended(c.DungeonId, c.Difficulty)}</color>   최소 전투력 {c.MinPower:N0}   <color=#b8c4d8>“{c.Message}”</color>";
            bool leader = inParty && c.IsLeader;
            int humansCount = inParty ? c.Members.Count : 1;
            var roster = Game.Session != null ? Game.Session.PartyRoster : null;
            for (int i = 0; i < memberRows.Count; i++)
            {
                var row = memberRows[i];
                var m = MemberAt(i);
                // [AI] Seats after the people: the leader's mercenaries who will fill them (shared field and dungeon alike).
                int aiIndex = i - humansCount;
                bool aiRow = m == null && (!inParty || leader) && roster != null && aiIndex >= 0 && aiIndex < (inParty ? AiShown : Mathf.Min(roster.Count, PartyManager.MaxMembers - 1));
                if (aiRow)
                {
                    var def = MercenaryDatabase.Get(roster[aiIndex]);
                    row.row.gameObject.SetActive(def != null);
                    if (def == null) continue;
                    row.who.text = $"<color=#78d6ff>[AI · 던전 전용]</color> {def.name}";
                    row.info.text = $"{def.roleName}  Lv{(Game.Session != null ? Game.Session.Progression.Level : 1)}";
                    row.lead.gameObject.SetActive(false);
                    row.kick.gameObject.SetActive(false);
                    continue;
                }
                if (!inParty && i == 0 && Game.Player != null && Game.Session != null)
                {
                    row.row.gameObject.SetActive(true);
                    row.who.text = $"<b>{Game.Session.Journal.PlayerName}</b> (나)";
                    row.info.text = $"{CharacterClassInfo.Get(Game.Player.Class).displayName}  Lv{Game.Session.Progression.Level}";
                    row.lead.gameObject.SetActive(false);
                    row.kick.gameObject.SetActive(false);
                    continue;
                }
                row.row.gameObject.SetActive(m != null);
                if (m == null) continue;
                string mark = m.leader ? "<color=#ffd34a>[방장]</color> " : m.ready ? "<color=#8fe28f>[준비]</color> " : "<color=#8c96a8>[대기]</color> ";
                row.who.text = mark + (m.me ? $"<b>{m.name}</b> (나)" : m.name);
                row.info.text = $"{CharacterClassInfo.Get(m.cls).displayName}  Lv{m.level}  전투력 {m.power:N0}";
                bool canManage = leader && !m.me && !PartyRunSession.Active;
                row.lead.gameObject.SetActive(canManage);
                row.kick.gameObject.SetActive(canManage);
            }
            for (int i = 0; i < appRows.Count; i++)
            {
                var row = appRows[i];
                var a = leader && i < c.Applications.Count ? c.Applications[i] : null;
                row.row.gameObject.SetActive(a != null);
                if (a != null) row.who.text = $"{a.name}  {CharacterClassInfo.Get(a.cls).displayName} Lv{a.level}  전투력 {a.power:N0}";
            }
            var session = PartyRunSession.Instance;
            // [8] Say where "출발" goes: the dungeon and difficulty picked when the post was made.
            string where = dungeon == null ? "" : $"{dungeon.name} {(dungeon.isRaid ? "레이드" : PartyFinderRules.DifficultyName(c.Difficulty))}";
            runText.text = session == null
                ? (inParty ? $"<color=#b8c4d8>출발하면 파티 전원이 <b>{where}</b> 첫 방으로 함께 들어갑니다(빈자리는 방장의 AI 동료). 사냥은 출발 없이 같은 사냥터로 가면 함께 합니다(AI 없이).</color>" : "")
                : session.State == "gathering"
                ? (session.AmHost ? "<color=#ffe066>파티원이 연결되는 중... 모두 들어오면 자동으로 출발합니다.</color>" : "<color=#ffe066>방장에게 연결하는 중...</color>")
                : "<color=#8fe28f>던전 진행 중</color>";
            int ai = AiShown;
            bool idle = inParty && session == null;
            dungeonBtn.gameObject.SetActive(leader && idle);
            diffBtn.gameObject.SetActive(leader && idle);
            bool canInvite = !inParty || (leader && idle);
            inviteBtn.gameObject.SetActive(canInvite);
            inviteField.gameObject.SetActive(canInvite);
            friendsBtn.gameObject.SetActive(canInvite);
            rosterBtn.gameObject.SetActive(!inParty || leader);
            startBtn.gameObject.SetActive(leader && idle);
            startBtn.interactable = c != null && c.AllReady;
            beginBtn.gameObject.SetActive(session != null && session.AmHost && session.State == "gathering");
            readyBtn.gameObject.SetActive(inParty && !leader && idle);
            if (inParty && c.Me != null) TextOf(readyBtn).text = c.Me.ready ? "준비 취소" : "준비";
            aiBtn.gameObject.SetActive(leader && idle);
            TextOf(aiBtn).text = $"AI 동료 {ai}명";
            listBtn.gameObject.SetActive(leader && idle && c.Source == "board");
            if (inParty) TextOf(listBtn).text = c.Listed ? "모집 내리기" : "모집 공개";
            leaveBtn.gameObject.SetActive(inParty && session == null);
        }
    }
}
