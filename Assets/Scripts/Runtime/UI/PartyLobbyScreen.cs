using System.Collections.Generic;
using System.Linq;
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
        /// <summary>"로" after a vowel or ㄹ, "으로" after any other final consonant.</summary>
        static string Ro(string word)
        {
            if (!L.Ko) return ""; // [I18N] Korean particle only
            char c = string.IsNullOrEmpty(word) ? 'a' : word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return "로"; // i18n-ignore particle
            int jong = (c - 0xAC00) % 28;
            return jong == 0 || jong == 8 ? "로" : "으로"; // i18n-ignore particle
        }

        public static PartyLobbyScreen Instance { get; private set; }
        protected override bool PadNavigation => focusField == null || !focusField.isFocused;
        // [UI] Member rows of 50 and applicant rows of 44 keep the third applicant above the status line (bottom 130).
        const float RowH = 50f, AppH = 44f, Width = 1180f;

        static PartyClient Client => PartyClient.Instance;

        Text header, runText;
        readonly List<(RectTransform row, Text who, Text info, Button lead, Button kick)> memberRows = new List<(RectTransform, Text, Text, Button, Button)>();
        readonly List<(RectTransform row, Text who, Button yes, Button no)> appRows = new List<(RectTransform, Text, Button, Button)>();
        Button readyBtn, startBtn, listBtn, leaveBtn, beginBtn;
        Button dungeonBtn, diffBtn, inviteBtn, friendsBtn, rosterBtn;
        InputField inviteField;

        public static PartyLobbyScreen Create(Transform canvas)
        {
            var w = CreateWindow<PartyLobbyScreen>(canvas, "PartyLobby", L.T("파티"), "menuicon_party");
            Instance = w;
            PartyHub.Attach(w, w.content, PartyHub.Page.MyParty); // [UX] one party window: 내 파티 | 파티 찾기 | AI 동료
            w.header = Label(w.content, "Header", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Width - 340f, 40f), TextAnchor.MiddleLeft);
            // [PARTY] Leader: change the party's dungeon / difficulty after it was made.
            w.diffBtn = Button(w.content, "Diff", L.T("난이도 ▼"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(150f, 40f), w.TogglePicker, 17);
            w.dungeonBtn = Button(w.content, "Dungeon", L.T("던전 변경 ▼"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-160f, 0f), new Vector2(170f, 40f), w.TogglePicker, 17);
            // [PARTY] Invite by name (no recruiting post needed: the server makes a private party), friends list, AI roster.
            w.friendsBtn = Button(w.content, "Friends", L.T("친구 목록"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(150f, 38f), () => Game.Flow.OpenWindow(SocialScreen.Instance), 17);
            w.inviteBtn = Button(w.content, "Invite", L.T("초대"), "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-160f, -44f), new Vector2(110f, 38f), w.InviteTyped, 17);
            w.inviteField = w.Field(w.content, "InviteName", L.T("초대할 캐릭터 이름"), Vector2.zero, new Vector2(260f, 38f));
            // One right-aligned row (the content stretches with the screen): [AI 편성] [이름] [초대] [친구 목록].
            var fieldRt = (RectTransform)w.inviteField.transform;
            fieldRt.anchorMin = fieldRt.anchorMax = fieldRt.pivot = new Vector2(1f, 1f);
            fieldRt.anchoredPosition = new Vector2(-280f, -44f);
            w.inviteField.characterLimit = 16;
            w.inviteField.onEndEdit.AddListener(_ => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) w.InviteTyped(); });
            w.focusField = w.inviteField;
            w.rosterBtn = Button(w.content, "Roster", L.T("AI 동료 편성"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-550f, -44f), new Vector2(150f, 38f), () => PartyHub.Switch(PartyHub.Page.Companions), 17);
            Label(w.content, "MembersHead", L.T("<color=#b8c4d8>파티원</color>"), 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -48f), new Vector2(300f, 28f), TextAnchor.MiddleLeft);
            for (int i = 0; i < PartyManager.MaxMembers; i++)
            {
                var r = Row(w.content, i, -80f, RowH, Width);
                int idx = i;
                var lead = Button(r, "Lead", L.T("방장 위임"), "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-140f, 0f), new Vector2(120f, 40f), () => w.MakeLeader(idx), 17);
                var kick = Button(r, "Kick", L.T("내보내기"), "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(120f, 40f), () => w.Kick(idx), 17);
                w.memberRows.Add((r, Cell(r, "Who", 14f, 420f, 20), Cell(r, "Info", 440f, 460f, 18), lead, kick));
            }
            float appTop = -80f - PartyManager.MaxMembers * RowH - 20f;
            Label(w.content, "AppsHead", L.T("<color=#b8c4d8>참가 신청</color>"), 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, appTop + 4f), new Vector2(300f, 28f), TextAnchor.MiddleLeft);
            for (int i = 0; i < 3; i++)
            {
                var r = Row(w.content, i, appTop - 28f, AppH, Width);
                int idx = i;
                var yes = Button(r, "Yes", L.T("수락"), "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-140f, 0f), new Vector2(120f, 38f), () => w.Respond(idx, true), 17);
                var no = Button(r, "No", L.T("거절"), "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(120f, 38f), () => w.Respond(idx, false), 17);
                w.appRows.Add((r, Cell(r, "Who", 14f, 700f, 19), yes, no));
            }
            w.runText = Label(w.content, "Run", "", 19, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 64f), new Vector2(Width, 34f), TextAnchor.MiddleLeft);
            // [UI] Button weight: settings on the left (gray), the one main action big at the right (출발 / 지금 출발 /
            // 준비 share the spot, only one shows), leaving the party apart in the far corner.
            float bx = 0f;
            Button Side(string name, string label, System.Action a)
            {
                var b = Button(w.content, name, label, "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(bx, 8f), new Vector2(190f, 48f), a, 18);
                bx += 202f;
                return b;
            }
            Button Main(string name, string label, System.Action a) =>
                Button(w.content, name, label, "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-206f, 8f), new Vector2(240f, 56f), a, 22);
            w.listBtn = Side("List", L.T("모집 공개"), w.ToggleListed);
            w.startBtn = Main("Start", L.T("출발"), w.Depart);
            w.beginBtn = Main("Begin", L.T("지금 출발"), () => PartyRunSession.Instance?.BeginNow());
            w.readyBtn = Main("Ready", L.T("준비"), w.ToggleReady);
            w.leaveBtn = Button(w.content, "Leave", L.T("<color=#ff9f7a>나가기</color>"), "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(190f, 48f), w.LeaveParty, 18);
            w.status = Label(w.content, "Status", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 100f), new Vector2(Width, 30f), TextAnchor.MiddleLeft);
            PartyClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override void Close()
        {
            if (PartyHub.BackOnePage()) return; // [UX] Esc: the hub page visited before this one
            if (!PartyHub.CloseToOrigin()) base.Close();
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
            if (m != null) Client.MakeLeader(m.characterId, (ok, msg) => Done(ok, msg, L.F("{0}님이 방장이 되었습니다.", m.name)));
        }

        void Kick(int i)
        {
            var m = MemberAt(i);
            if (m != null) Game.UI.Confirm(L.F("{0}님을 파티에서 내보낼까요?", m.name), () => Client.Kick(m.characterId, (ok, msg) => Done(ok, msg, L.F("{0}님을 내보냈습니다.", m.name))), true);
        }

        void Respond(int i, bool accept)
        {
            if (Client == null || i >= Client.Applications.Count) return;
            var a = Client.Applications[i];
            Client.Respond(a.id, accept, (ok, msg) => Done(ok, msg, accept ? L.F("{0}님이 파티에 들어왔습니다.", a.name) : L.T("신청을 거절했습니다.")));
        }

        void ToggleReady()
        {
            var me = Client?.Me;
            if (me != null) Client.SetReady(!me.ready, (ok, msg) => Done(ok, msg, me.ready ? L.T("준비를 취소했습니다.") : L.T("준비 완료!")));
        }

        int FreeSeats => Client != null ? Mathf.Max(0, PartyManager.MaxMembers - Client.Members.Count) : 0;

        void InviteTyped()
        {
            string name = inviteField.text.Trim();
            if (name.Length == 0) { SetStatus(L.T("초대할 캐릭터 이름을 입력하세요."), false); return; }
            if (Client == null) return;
            Client.InviteByName(name, (ok, msg) => { if (ok) inviteField.text = ""; Done(ok, msg, L.F("{0}님에게 파티 초대를 보냈습니다.", name)); });
        }

        static List<DungeonDef> Targets => new List<DungeonDef>(DungeonDatabase.Weekday.Concat(DungeonDatabase.Normal)) { DungeonDatabase.SkeletonKing, DungeonDatabase.Get(DungeonDatabase.RaidGrah) };

        // [PARTY] Leader's destination picker: every dungeon / raid as a button, and the difficulty below (not raids).
        RectTransform picker;
        readonly List<(Button btn, string id)> pickDungeons = new List<(Button, string)>();
        readonly List<(Button btn, DungeonDifficulty d)> pickDiffs = new List<(Button, DungeonDifficulty)>();

        void BuildPicker()
        {
            const float BtnW = 250f, BtnH = 42f, Pad = 12f;
            var list = Targets.FindAll(d => d != null);
            int rows = (list.Count + 1) / 2;
            float height = 44f + rows * (BtnH + 6f) + 40f + BtnH + Pad * 2f;
            var bg = Panel(content, "Picker", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(BtnW * 2f + Pad * 3f, height), new Color32(16, 24, 38, 250));
            bg.raycastTarget = true;
            picker = bg.rectTransform;
            var tl = new Vector2(0f, 1f);
            Label(picker, "Title", L.T("<color=#b8c4d8>목적지 선택</color>"), 18, tl, tl, new Vector2(Pad, -10f), new Vector2(300f, 28f), TextAnchor.MiddleLeft);
            Button(picker, "Close", L.T("닫기"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -6f), new Vector2(90f, 34f), () => picker.gameObject.SetActive(false), 16);
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var b = Button(picker, "D_" + d.id, d.name, "ui_btngray", tl, tl,
                    new Vector2(Pad + (i % 2) * (BtnW + Pad), -44f - (i / 2) * (BtnH + 6f)), new Vector2(BtnW, BtnH), () => PickDungeon(d), 17);
                pickDungeons.Add((b, d.id));
            }
            float diffTop = -44f - rows * (BtnH + 6f) - 8f;
            Label(picker, "DiffHead", L.T("<color=#b8c4d8>난이도</color>"), 17, tl, tl, new Vector2(Pad, diffTop), new Vector2(200f, 28f), TextAnchor.MiddleLeft);
            float diffW = (BtnW * 2f + Pad - (DungeonDatabase.DifficultyCount - 1) * 6f) / DungeonDatabase.DifficultyCount;
            for (int i = 0; i < DungeonDatabase.DifficultyCount; i++)
            {
                var diff = (DungeonDifficulty)i;
                var b = Button(picker, "Diff_" + i, PartyFinderRules.DifficultyName(diff), "ui_btngray", tl, tl,
                    new Vector2(Pad + i * (diffW + 6f), diffTop - 32f), new Vector2(diffW, BtnH), () => PickDifficulty(diff), 17);
                pickDiffs.Add((b, diff));
            }
            picker.gameObject.SetActive(false);
        }

        void TogglePicker()
        {
            if (Client == null || !Client.InParty || !Client.IsLeader) return;
            if (picker == null) BuildPicker();
            bool open = !picker.gameObject.activeSelf;
            picker.gameObject.SetActive(open);
            if (open) { picker.SetAsLastSibling(); RefreshPicker(); }
        }

        void RefreshPicker()
        {
            if (picker == null || !picker.gameObject.activeSelf || Client == null) return;
            var cur = DungeonDatabase.Get(Client.DungeonId);
            foreach (var (btn, id) in pickDungeons)
            {
                var d = DungeonDatabase.Get(id);
                bool on = id == Client.DungeonId;
                TextOf(btn).text = on ? $"<color=#ffd34a>▶ {L.T(d.name)}</color>" : L.T(d.name) + (d.isRaid ? L.T(" <color=#ff9f7a>레이드</color>") : "");
            }
            bool raid = cur != null && cur.isRaid;
            foreach (var (btn, diff) in pickDiffs)
            {
                btn.interactable = !raid;
                string name = PartyFinderRules.DifficultyName(diff);
                TextOf(btn).text = !raid && diff == Client.Difficulty ? $"<color=#ffd34a>▶ {name}</color>" : name;
            }
        }

        void PickDungeon(DungeonDef next)
        {
            if (Client == null || !Client.InParty) return;
            if (next.id == Client.DungeonId) { picker.gameObject.SetActive(false); return; }
            Client.SetTarget(next.id, next.isRaid ? DungeonDifficulty.Normal : Client.Difficulty,
                (ok, msg) => { Done(ok, msg, L.F("목적지를 {0}{1} 바꿨습니다.", L.T(next.name), Ro(next.name))); RefreshPicker(); });
        }

        void PickDifficulty(DungeonDifficulty next)
        {
            if (Client == null || !Client.InParty) return;
            var d = DungeonDatabase.Get(Client.DungeonId);
            if (d != null && d.isRaid) { SetStatus(L.T("레이드는 난이도가 하나입니다."), false); return; }
            if (next == Client.Difficulty) return;
            Client.SetTarget(Client.DungeonId, next, (ok, msg) => { Done(ok, msg, L.F("난이도를 {0}{1} 바꿨습니다.", L.T(PartyFinderRules.DifficultyName(next)), Ro(PartyFinderRules.DifficultyName(next)))); RefreshPicker(); });
        }

        /// <summary>[AI] The leader's AI 동료 (the AI tab's roster) fill the free seats, as DungeonDirector.EnterAsParty does.</summary>
        int AiShown => Mathf.Min(FreeSeats, Game.Session != null ? Game.Session.PartyRoster.Count : 0);

        void ToggleListed() => Client?.SetListed(!Client.Listed, (ok, msg) => Done(ok, msg, Client.Listed ? L.T("모집 글을 다시 올렸습니다.") : L.T("모집 글을 내렸습니다.")));

        // [UX] Same way in as the dungeon window's 입장 (trailer, practice question, everyone + AI seats).
        void Depart()
        {
            if (Client == null || !Client.InParty) return;
            var target = DungeonDatabase.Get(Client.DungeonId);
            if (target == null) { SetStatus(L.T("목적지를 먼저 고르세요. [던전 변경]"), false); return; }
            PartyHub.ForgetTrail();
            DungeonSelectScreen.Enter(target, Client.Difficulty, Refresh);
        }

        void LeaveParty()
        {
            if (PartyRunSession.Active) { SetStatus(L.T("던전 진행 중에는 파티를 나갈 수 없습니다."), false); return; }
            Game.UI.Confirm(L.T("파티에서 나갈까요?"), () => Client?.Leave((ok, msg) => { Done(ok, msg, L.T("파티에서 나왔습니다.")); if (ok) { PartyHub.ForgetTrail(); Close(); } }), true);
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
            header.text = !inParty ? L.T("<color=#8c96a8>파티가 없습니다. 이름을 넣고 [초대]하면 파티가 만들어집니다(모집 글은 선택).</color>")
                : L.F("{0}  <color={1}>{2}</color>   {3}/{4}명   <color=#b8c4d8>{5}</color>   최소 전투력 {6:N0}   <color=#b8c4d8>“{7}”</color>", L.T(dungeon?.name ?? c.DungeonId), PartyFinderRules.DifficultyColor(c.Difficulty), (dungeon != null && dungeon.isRaid ? L.T("레이드") : L.T(PartyFinderRules.DifficultyName(c.Difficulty))), c.Members.Count, c.MaxMembers, PartyFinderRules.Recommended(c.DungeonId, c.Difficulty), c.MinPower, c.Message);
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
                    row.who.text = L.F("<color=#78d6ff>[AI · 던전 전용]</color> {0}", L.T(def.name));
                    row.info.text = $"{def.roleName}  Lv{(Game.Session != null ? Game.Session.Progression.Level : 1)}";
                    row.lead.gameObject.SetActive(false);
                    row.kick.gameObject.SetActive(false);
                    continue;
                }
                if (!inParty && i == 0 && Game.Player != null && Game.Session != null)
                {
                    row.row.gameObject.SetActive(true);
                    row.who.text = L.F("<b>{0}</b> (나)", Game.Session.Journal.PlayerName);
                    row.info.text = $"{L.T(CharacterClassInfo.Get(Game.Player.Class).displayName)}  Lv{Game.Session.Progression.Level}";
                    row.lead.gameObject.SetActive(false);
                    row.kick.gameObject.SetActive(false);
                    continue;
                }
                row.row.gameObject.SetActive(m != null);
                if (m == null) continue;
                string mark = m.leader ? L.T("<color=#ffd34a>[방장]</color> ") : m.ready ? L.T("<color=#8fe28f>[준비]</color> ") : L.T("<color=#8c96a8>[대기]</color> ");
                row.who.text = mark + (m.me ? L.F("<b>{0}</b> (나)", m.name) : m.name);
                row.info.text = L.F("{0}  Lv{1}  전투력 {2:N0}", L.T(CharacterClassInfo.Get(m.cls).displayName), m.level, m.power) + (m.me ? "" : "  ·  " + PartyFramesView.WhereText(m));
                bool canManage = leader && !m.me && !PartyRunSession.Active;
                row.lead.gameObject.SetActive(canManage);
                row.kick.gameObject.SetActive(canManage);
            }
            for (int i = 0; i < appRows.Count; i++)
            {
                var row = appRows[i];
                var a = leader && i < c.Applications.Count ? c.Applications[i] : null;
                row.row.gameObject.SetActive(a != null);
                if (a != null) row.who.text = L.F("{0}  {1} Lv{2}  전투력 {3:N0}", a.name, L.T(CharacterClassInfo.Get(a.cls).displayName), a.level, a.power);
            }
            var session = PartyRunSession.Instance;
            // [8] Say where "출발" goes: the dungeon and difficulty picked when the post was made.
            string where = dungeon == null ? "" : L.F("{0} {1}", L.T(dungeon.name), (dungeon.isRaid ? L.T("레이드") : L.T(PartyFinderRules.DifficultyName(c.Difficulty))));
            runText.text = session == null
                ? (inParty ? L.F("<color=#b8c4d8>출발하면 파티 전원이 <b>{0}</b> 첫 방으로 함께 들어갑니다(빈자리는 방장의 AI 동료). 사냥은 출발 없이 같은 사냥터로 가면 함께 합니다(AI 없이).</color>", where) : "")
                : session.State == "gathering"
                ? (session.AmHost ? L.T("<color=#ffe066>파티원이 연결되는 중... 모두 들어오면 자동으로 출발합니다.</color>") : L.T("<color=#ffe066>방장에게 연결하는 중...</color>"))
                : L.T("<color=#8fe28f>던전 진행 중</color>");
            bool idle = inParty && session == null;
            dungeonBtn.gameObject.SetActive(leader && idle);
            if (picker != null && picker.gameObject.activeSelf && !(leader && idle)) picker.gameObject.SetActive(false);
            RefreshPicker();
            diffBtn.gameObject.SetActive(leader && idle);
            bool canInvite = !inParty || (leader && idle);
            inviteBtn.gameObject.SetActive(canInvite);
            inviteField.gameObject.SetActive(canInvite);
            friendsBtn.gameObject.SetActive(canInvite);
            rosterBtn.gameObject.SetActive(!inParty || leader);
            startBtn.gameObject.SetActive(leader && idle);
            startBtn.interactable = c != null; // no ready check: online members are pulled in, missing seats get AI
            beginBtn.gameObject.SetActive(session != null && session.AmHost && session.State == "gathering");
            readyBtn.gameObject.SetActive(inParty && !leader && idle);
            if (inParty && c.Me != null) TextOf(readyBtn).text = c.Me.ready ? L.T("준비 취소") : L.T("준비");
            listBtn.gameObject.SetActive(leader && idle && c.Source == "board");
            if (inParty) TextOf(listBtn).text = c.Listed ? L.T("모집 내리기") : L.T("모집 공개");
            leaveBtn.gameObject.SetActive(inParty && session == null);
        }
    }
}
