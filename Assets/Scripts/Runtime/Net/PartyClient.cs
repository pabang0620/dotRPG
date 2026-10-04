using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY] One member of my party as the server lists it (phase4_api §4.1 PartyView).</summary>
    public sealed class PartyMemberView
    {
        public string characterId, name;
        public CharacterClass cls;
        public int level, power;
        public bool ready, leader, me;
    }

    /// <summary>[PARTY] Someone asking to join my party (the leader sees these).</summary>
    public sealed class PartyApplicationView
    {
        public string id, name;
        public CharacterClass cls;
        public int level, power;
    }

    /// <summary>
    /// [PARTY] Client of the server's party board, lobby and auto-match (phase4_api §4-5). Polls
    /// <c>GET /party</c> while an online character plays and keeps the last answer; every action is one
    /// request whose answer replaces the cached party. When the party's run appears the run session starts.
    /// </summary>
    public sealed class PartyClient : MonoBehaviour
    {
        const float PollSeconds = 1.5f, IdlePollSeconds = 5f, PushedPollSeconds = 15f;

        public static PartyClient Instance { get; private set; }
        public static event Action Changed;

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        float pollTimer;
        bool polling;
        int version = -1;
        string lastNoticeAt;

        // ---------------- cached state ----------------
        public bool InParty => PartyId != null;
        public string PartyId { get; private set; }
        public string State { get; private set; }
        public string DungeonId { get; private set; }
        public DungeonDifficulty Difficulty { get; private set; }
        public int MaxMembers { get; private set; } = 4;
        public int MinPower { get; private set; }
        public string Message { get; private set; }
        public bool Listed { get; private set; }
        public string Source { get; private set; }
        public readonly List<PartyMemberView> Members = new List<PartyMemberView>();
        public readonly List<PartyApplicationView> Applications = new List<PartyApplicationView>();
        public Dictionary<string, object> Run { get; private set; }
        public bool IsLeader { get { foreach (var m in Members) if (m.me) return m.leader; return false; } }
        public bool AllReady { get { foreach (var m in Members) if (!m.leader && !m.ready) return false; return true; } }
        public PartyMemberView Me { get { foreach (var m in Members) if (m.me) return m; return null; } }

        // auto-match
        public bool Queued { get; private set; }
        public string QueueDungeon { get; private set; }
        public DungeonDifficulty QueueDifficulty { get; private set; }
        public DateTime QueuedAt { get; private set; }
        public int HumansWaiting { get; private set; } = 1;
        /// <summary>My pending applications (party id -> state).</summary>
        public readonly Dictionary<string, string> MyApplications = new Dictionary<string, string>();

        /// <summary>An online character entered: the party board, lobby and matching use the server.</summary>
        public static void AttachOnline()
        {
            Ensure();
            FieldSession.Ensure(); // [PARTY 8] field party hunting
            if (!(OnlineServices.PartyFinder is ServerPartyFinderService)) OnlineServices.PartyFinder = new ServerPartyFinderService();
        }

        /// <summary>Back to offline play or the title: drop the run connection and the cached party.</summary>
        public static void DetachOnline()
        {
            if (OnlineServices.PartyFinder is ServerPartyFinderService s)
            {
                s.Detach();
                OnlineServices.PartyFinder = null; // the offline preview comes back
            }
            FieldSession.Shutdown(); // [PARTY 8]
            if (PartyRunSession.Active) PartyRunSession.Instance.Leave();
            PartyNet.End();
            Instance?.Clear();
        }

        public static PartyClient Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PartyClient");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PartyClient>();
            return Instance;
        }

        /// <summary>Back to the title: forget everything (the server keeps the party until it expires).</summary>
        public void Clear()
        {
            PartyId = null;
            Run = null;
            Members.Clear();
            Applications.Clear();
            MyApplications.Clear();
            Queued = false;
            version = -1;
            Changed?.Invoke();
        }

        void Update()
        {
            if (!OnlineSession.Playing) return;
            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer > 0f || polling) return;
            bool busy = InParty || Queued || MyApplications.Count > 0 || PartyRunSession.Active;
            pollTimer = busy ? PollSeconds : IdlePollSeconds;
            // [SERVER 5] With the chat socket up, changes are pushed; polling is only the safety net.
            if (OnlineServices.Chat is ServerChatService chat && chat.Connected && !PartyRunSession.Active) pollTimer = PushedPollSeconds;
            Poll();
        }

        public void PollNow() => pollTimer = 0f;

        void Poll()
        {
            polling = true;
            string q = version >= 0 && !Queued ? $"?after_version={version}" : "";
            Api.Get(Char + "/party" + q, r =>
            {
                polling = false;
                if (!r.ok) return;
                if (r.data.TryGetValue("changed", out var c) && c is bool changed && !changed) return;
                ReadPoll(r.data);
            });
        }

        void ReadPoll(Dictionary<string, object> d)
        {
            ReadParty(MiniJson.Obj(d, "party"));
            var queue = MiniJson.Obj(d, "queue");
            bool wasQueued = Queued;
            Queued = queue != null;
            if (queue != null)
            {
                QueueDungeon = MiniJson.Str(queue, "dungeon_id");
                QueueDifficulty = (DungeonDifficulty)MiniJson.Int(queue, "difficulty");
                QueuedAt = ParseTime(MiniJson.Str(queue, "queued_at"));
                HumansWaiting = Mathf.Max(1, MiniJson.Int(queue, "humans_waiting", 1));
            }
            MyApplications.Clear();
            foreach (var a in MiniJson.Arr(d, "applications_mine") ?? new List<object>())
            {
                string state = MiniJson.Str(a, "state");
                if (state == "pending") MyApplications[MiniJson.Str(a, "party_id")] = state;
            }
            foreach (var inv in MiniJson.Arr(d, "invites_incoming") ?? new List<object>()) OnInvite(inv as Dictionary<string, object>);
            var notice = MiniJson.Obj(d, "notice");
            string at = MiniJson.Str(notice, "at");
            if (notice != null && at != lastNoticeAt)
            {
                lastNoticeAt = at;
                switch (MiniJson.Str(notice, "code"))
                {
                    case "KICKED": GameEvents.RaiseToast("파티에서 내보내졌다."); break;
                    case "PARTY_CLOSED": GameEvents.RaiseToast("파티가 해산되었다."); break;
                    case "START_TIMEOUT": GameEvents.RaiseToast("매칭된 파티가 제때 출발하지 않아 해산되었다."); break;
                }
            }
            if (wasQueued && !Queued && InParty) GameEvents.RaiseToast("매칭 완료! 파티 창을 확인하자.");
            Changed?.Invoke();
        }

        void ReadParty(Dictionary<string, object> p)
        {
            Members.Clear();
            Applications.Clear();
            if (p == null)
            {
                PartyId = null;
                Run = null;
                version = -1;
                return;
            }
            PartyId = MiniJson.Str(p, "id");
            version = MiniJson.Int(p, "version", -1);
            State = MiniJson.Str(p, "state");
            DungeonId = MiniJson.Str(p, "dungeon_id");
            Difficulty = (DungeonDifficulty)MiniJson.Int(p, "difficulty");
            MaxMembers = MiniJson.Int(p, "max_members", 4);
            MinPower = MiniJson.Int(p, "min_power");
            Message = MiniJson.Str(p, "message", "");
            Listed = p.TryGetValue("listed", out var l) && l is bool b && b;
            Source = MiniJson.Str(p, "source", "board");
            foreach (var m in MiniJson.Arr(p, "members") ?? new List<object>())
                Members.Add(new PartyMemberView
                {
                    characterId = MiniJson.Str(m, "character_id"),
                    name = MiniJson.Str(m, "name", ""),
                    cls = MiniJson.Str(m, "class") == "mage" ? CharacterClass.Mage : CharacterClass.Warrior,
                    level = MiniJson.Int(m, "level", 1),
                    power = MiniJson.Int(m, "power"),
                    ready = Flag(m, "ready"),
                    leader = Flag(m, "is_leader"),
                    me = Flag(m, "is_me"),
                });
            foreach (var a in MiniJson.Arr(p, "applications") ?? new List<object>())
            {
                var c = MiniJson.Obj(a, "character");
                Applications.Add(new PartyApplicationView
                {
                    id = MiniJson.Str(a, "id"),
                    name = MiniJson.Str(c, "name", ""),
                    cls = MiniJson.Str(c, "class") == "mage" ? CharacterClass.Mage : CharacterClass.Warrior,
                    level = MiniJson.Int(c, "level", 1),
                    power = MiniJson.Int(c, "power"),
                });
            }
            Run = MiniJson.Obj(p, "run");
            if (Run != null) PartyRunSession.Observe(Run, null);
        }

        static bool Flag(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b && b;

        static DateTime ParseTime(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.UtcNow;

        // ---------------- actions ----------------

        /// <summary>Every action answers with the party (or null when I left); errors come back as a Korean line.</summary>
        void Act(string method, string path, Dictionary<string, object> body, Action<bool, string> done, bool withRequestId = true)
        {
            if (!OnlineSession.Playing) { done?.Invoke(false, "온라인 캐릭터로 접속해야 한다."); return; }
            if (body == null) body = new Dictionary<string, object>();
            if (withRequestId) body["request_id"] = ApiClient.NewRequestId();
            Action<ApiResult> handle = r =>
            {
                if (r.ok && r.data != null)
                {
                    if (r.data.ContainsKey("party")) ReadParty(MiniJson.Obj(r.data, "party"));
                    var queue = MiniJson.Obj(r.data, "queue");
                    if (queue != null)
                    {
                        Queued = true;
                        QueueDungeon = MiniJson.Str(queue, "dungeon_id");
                        QueueDifficulty = (DungeonDifficulty)MiniJson.Int(queue, "difficulty");
                        QueuedAt = ParseTime(MiniJson.Str(queue, "queued_at"));
                        HumansWaiting = Mathf.Max(1, MiniJson.Int(queue, "humans_waiting", 1));
                    }
                    Changed?.Invoke();
                }
                done?.Invoke(r.ok, r.ok ? "" : Explain(r));
                PollNow();
            };
            string full = Char + path;
            switch (method)
            {
                case "POST": Api.Post(full, body, handle); break;
                case "PATCH": Api.Patch(full, body, handle); break;
                case "DELETE": Api.Delete(full, handle); break;
            }
        }

        public void CreateParty(string dungeonId, DungeonDifficulty difficulty, int maxMembers, int minPower, string message, Action<bool, string> done) =>
            Act("POST", "/parties", new Dictionary<string, object>
            {
                ["dungeon_id"] = dungeonId, ["difficulty"] = (int)difficulty, ["max_members"] = Mathf.Clamp(maxMembers, 2, 4),
                ["min_power"] = Mathf.Max(0, minPower), ["message"] = message ?? "", ["listed"] = true,
            }, done);

        public void Apply(string partyId, Action<bool, string> done) =>
            Act("POST", $"/parties/{partyId}/apply", null, (ok, msg) => { if (ok) MyApplications[partyId] = "pending"; done?.Invoke(ok, msg); });

        public void Respond(string applicationId, bool accept, Action<bool, string> done) =>
            Act("POST", $"/party/applications/{applicationId}/respond", new Dictionary<string, object> { ["accept"] = accept }, done);

        public void SetReady(bool ready, Action<bool, string> done) =>
            Act("POST", "/party/ready", new Dictionary<string, object> { ["ready"] = ready }, done, withRequestId: false);

        public void Leave(Action<bool, string> done) => Act("POST", "/party/leave", null, done);

        public void Kick(string characterId, Action<bool, string> done) =>
            Act("POST", "/party/kick", new Dictionary<string, object> { ["target"] = characterId }, done);

        public void MakeLeader(string characterId, Action<bool, string> done) =>
            Act("POST", "/party/leader", new Dictionary<string, object> { ["target"] = characterId }, done);

        public void SetListed(bool listed, Action<bool, string> done) =>
            Act("PATCH", "/party", new Dictionary<string, object> { ["listed"] = listed }, done, withRequestId: false);

        public void Queue(string dungeonId, DungeonDifficulty difficulty, Action<bool, string> done) =>
            Act("POST", "/match/queue", new Dictionary<string, object> { ["dungeon_id"] = dungeonId, ["difficulty"] = (int)difficulty }, done);

        public void CancelQueue(Action<bool, string> done) =>
            Act("DELETE", "/match/queue", null, (ok, msg) => { if (ok) Queued = false; done?.Invoke(ok, msg); }, withRequestId: false);

        public void FillAi(Action<bool, string> done) => Act("POST", "/match/fill-ai", null, (ok, msg) => { if (ok) Queued = false; done?.Invoke(ok, msg); });

        /// <summary>Leader: everyone is ready, go (the server issues the run and entry tokens).</summary>
        public void StartRun(int aiCount, Action<bool, string> done)
        {
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["ai_count"] = Mathf.Clamp(aiCount, 0, 3) };
            Api.Post(Char + "/party/start", body, r =>
            {
                // Only the leader gets the run key (it checks the members' entry tokens on its own PC).
                if (r.ok) PartyRunSession.Observe(MiniJson.Obj(r.data, "run"), MiniJson.Str(r.data, "host_key"));
                done?.Invoke(r.ok, r.ok ? "" : Explain(r));
                PollNow();
            });
        }

        // ---------------- [SERVER 5] invites ----------------

        readonly HashSet<string> askedInvites = new HashSet<string>();

        /// <summary>Leader: invite someone seen in chat or in the friend list.</summary>
        public void Invite(string characterId, Action<bool, string> done)
        {
            Act("POST", "/party/invites", new Dictionary<string, object> { ["target"] = characterId }, done);
        }

        /// <summary>Invite by character name (typed in the party window). With no party yet the server makes a private one.</summary>
        public void InviteByName(string name, Action<bool, string> done)
        {
            Act("POST", "/party/invites", new Dictionary<string, object> { ["target_name"] = name }, done);
        }

        /// <summary>Leader: change the party's dungeon / difficulty (raids use difficulty 0).</summary>
        public void SetTarget(string dungeonId, DungeonDifficulty difficulty, Action<bool, string> done) =>
            Act("PATCH", "/party", new Dictionary<string, object> { ["dungeon_id"] = dungeonId, ["difficulty"] = (int)difficulty }, done, withRequestId: false);

        /// <summary>An invite arrived (socket push or the poll's invites_incoming): ask once.</summary>
        public void OnInvite(Dictionary<string, object> invite)
        {
            string id = MiniJson.Str(invite, "id");
            if (string.IsNullOrEmpty(id) || !askedInvites.Add(id) || Game.UI == null) return;
            var from = MiniJson.Obj(invite, "from");
            var party = MiniJson.Obj(invite, "party");
            var def = DungeonDatabase.Get(MiniJson.Str(party, "dungeon_id"));
            string where = def != null ? def.name : "던전";
            Game.UI.Confirm($"{MiniJson.Str(from, "name", "누군가")}님이 {where} 파티에 초대했습니다.\n<size=18>30초 안에 답하지 않으면 사라집니다.</size>",
                () => RespondInvite(id, true), true); // "아니오" lets it expire (the server closes it after 30 s)
        }

        void RespondInvite(string inviteId, bool accept) =>
            Act("POST", $"/party/invites/{inviteId}/respond", new Dictionary<string, object> { ["accept"] = accept },
                (ok, msg) => GameEvents.RaiseToast(!ok ? msg : accept ? "파티에 들어갔다." : "초대를 거절했다."));

        public static string Explain(ApiResult r)
        {
            switch (r.code)
            {
                case "NETWORK": return "서버에 연결할 수 없다.";
                case "PARTY_FULL": return "파티 인원이 가득 찼다.";
                case "POWER_TOO_LOW": return "전투력이 모자라다.";
                case "NOT_ALL_READY": return "모든 파티원이 준비를 마쳐야 출발할 수 있다.";
                case "ALREADY_IN_PARTY": return "이미 파티에 들어가 있다.";
                case "IN_QUEUE": return "자동 매칭 대기 중이다.";
                case "TOO_MANY_APPLICATIONS": return "동시에 신청할 수 있는 파티는 3곳까지다.";
                case "MEMBER_NOT_ELIGIBLE": return "입장할 수 없는 파티원이 있다. (입장 횟수, 레벨, 해금)";
                default: return string.IsNullOrEmpty(r.message) ? $"요청이 실패했다. ({r.status})" : r.message;
            }
        }
    }
}
