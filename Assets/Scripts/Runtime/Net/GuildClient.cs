using System;
using System.Collections.Generic;
using System.Globalization;

namespace DotRPG
{
    /// <summary>
    /// [GUILD 2026-10-20] 길드 (Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 1, 7): the server owns guilds, members, roles and
    /// applications; the client shows them and sends the player's choices. Offline play has no guilds.
    /// </summary>
    public static class GuildClient
    {
        public const int MaxMembers = 30, CreateLevel = 20, CreateGold = 100000, NameMin = 2, NameMax = 12, NoticeMax = 60;

        public sealed class Member
        {
            public string id, name, role, career, lastSeenAt, joinedAt;
            public int level;
            public bool IsLeader => role == "leader";
            public bool IsOfficer => role == "officer";
        }

        public sealed class Application
        {
            public string id, characterId, name, career, appliedAt;
            public int level;
        }

        public sealed class Info
        {
            public string id, name, notice, joinMode, myRole;
            public int memberCount, maxMembers = MaxMembers;
            public readonly List<Member> members = new List<Member>();
            public readonly List<Application> applications = new List<Application>();
            public bool Open => joinMode == "open";
        }

        public sealed class Listing
        {
            public string id, name, notice, joinMode, leaderName;
            public int memberCount, maxMembers = MaxMembers;
            public bool Open => joinMode == "open";
            public bool Full => memberCount >= maxMembers;
        }

        public sealed class MyApplication { public string guildId, guildName, appliedAt; }

        /// <summary>My guild (null = none or not loaded).</summary>
        public static Info Guild { get; private set; }
        /// <summary>After leaving or being kicked: no other guild until then (local time).</summary>
        public static DateTime? CooldownUntil { get; private set; }
        public static readonly List<MyApplication> MyApplications = new List<MyApplication>();
        public static readonly List<Listing> Found = new List<Listing>();
        public static int FoundPage { get; private set; }
        public static int FoundTotal { get; private set; }
        public static bool Loaded => loaded && CurrentContext;
        public static bool Busy { get; private set; }
        public static event Action Changed;

        public static bool InGuild => Loaded && Guild != null;
        public static string MyRole => Guild?.myRole ?? "";
        public static bool IsLeader => InGuild && MyRole == "leader";
        /// <summary>Leader or officer: may accept / reject applications and kick members.</summary>
        public static bool IsStaff => InGuild && (MyRole == "leader" || MyRole == "officer");
        public static bool CoolingDown => CooldownUntil.HasValue && CooldownUntil.Value > DateTime.Now;

        static OnlineSession owner;
        static string character;
        static int generation, readVersion;
        static bool loaded;
        static bool CurrentContext => OnlineSession.Playing && ReferenceEquals(owner, OnlineSession.Current) && character == owner.ActiveCharacter;
        static ApiClient Api => ApiClient.Instance;

        /// <summary>Forgets everything (account or character change).</summary>
        public static void Reset()
        {
            generation++;
            readVersion++;
            owner = null;
            character = null;
            loaded = Busy = false;
            Guild = null;
            CooldownUntil = null;
            MyApplications.Clear();
            Found.Clear();
            FoundPage = FoundTotal = 0;
            Changed?.Invoke();
        }

        static bool EnsureContext()
        {
            if (CurrentContext) return true;
            Reset();
            if (!OnlineSession.Playing) return false;
            owner = OnlineSession.Current;
            character = owner.ActiveCharacter;
            return true;
        }

        public static void Refresh()
        {
            if (!EnsureContext() || Busy) return;
            int context = generation, version = ++readVersion;
            Api.Get("/characters/" + character + "/guild", r =>
            {
                if (context != generation || version != readVersion || !CurrentContext) return;
                if (r.ok) Read(r.data);
                Changed?.Invoke();
            });
        }

        static void Read(Dictionary<string, object> d)
        {
            if (d == null || !CurrentContext) return;
            var g = MiniJson.Obj(d, "guild");
            Guild = g != null ? ReadInfo(g) : null;
            CooldownUntil = ParseTime(MiniJson.Str(d, "cooldown_until"));
            MyApplications.Clear();
            foreach (var o in MiniJson.Arr(d, "my_applications") ?? new List<object>())
                MyApplications.Add(new MyApplication { guildId = MiniJson.Str(o, "guild_id"), guildName = MiniJson.Str(o, "guild_name", ""), appliedAt = MiniJson.Str(o, "applied_at") });
            loaded = true;
        }

        static Info ReadInfo(Dictionary<string, object> g)
        {
            var info = new Info
            {
                id = MiniJson.Str(g, "id"), name = MiniJson.Str(g, "name", ""), notice = MiniJson.Str(g, "notice", ""),
                joinMode = MiniJson.Str(g, "join_mode", "approval"), myRole = MiniJson.Str(g, "my_role", "member"),
                memberCount = MiniJson.Int(g, "member_count"), maxMembers = MiniJson.Int(g, "max_members", MaxMembers),
            };
            foreach (var o in MiniJson.Arr(g, "members") ?? new List<object>())
                info.members.Add(new Member
                {
                    id = MiniJson.Str(o, "character_id"), name = MiniJson.Str(o, "name", ""), level = MiniJson.Int(o, "level", 1),
                    career = MiniJson.Str(o, "career", ""), role = MiniJson.Str(o, "role", "member"),
                    lastSeenAt = MiniJson.Str(o, "last_seen_at"), joinedAt = MiniJson.Str(o, "joined_at"),
                });
            // Leader first, then officers, then by level.
            info.members.Sort((a, b) => RoleOrder(a.role) != RoleOrder(b.role) ? RoleOrder(a.role).CompareTo(RoleOrder(b.role)) : b.level.CompareTo(a.level));
            if (info.memberCount <= 0) info.memberCount = info.members.Count;
            foreach (var o in MiniJson.Arr(g, "applications") ?? new List<object>())
                info.applications.Add(new Application
                {
                    id = MiniJson.Str(o, "id"), characterId = MiniJson.Str(o, "character_id"), name = MiniJson.Str(o, "name", ""),
                    level = MiniJson.Int(o, "level", 1), career = MiniJson.Str(o, "career", ""), appliedAt = MiniJson.Str(o, "applied_at"),
                });
            return info;
        }

        static int RoleOrder(string role) => role == "leader" ? 0 : role == "officer" ? 1 : 2;

        public static DateTime? ParseTime(string iso) =>
            !string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime() : (DateTime?)null;

        public static string RoleName(string role) => role == "leader" ? L.T("길드장") : role == "officer" ? L.T("부길드장") : L.T("길드원");

        /// <summary>Career sent as a name ("fighter") or a number: its display name.</summary>
        public static string CareerName(string career)
        {
            if (string.IsNullOrEmpty(career)) return "";
            if (int.TryParse(career, out int n) && Enum.IsDefined(typeof(Career), n)) return CareerCatalog.Name((Career)n);
            return Enum.TryParse(career, true, out Career c) ? CareerCatalog.Name(c) : career;
        }

        /// <summary>"3분 전" / "접속 중" style last-seen text.</summary>
        public static string SeenText(string iso)
        {
            var at = ParseTime(iso);
            if (!at.HasValue) return "";
            var ago = DateTime.Now - at.Value;
            if (ago.TotalMinutes < 5) return L.T("접속 중");
            if (ago.TotalHours < 1) return L.F("{0}분 전", (int)ago.TotalMinutes);
            if (ago.TotalDays < 1) return L.F("{0}시간 전", (int)ago.TotalHours);
            return L.F("{0}일 전", (int)ago.TotalDays);
        }

        // =============================== search ===============================

        public static void Search(string query, int page, Action<bool, string> done)
        {
            if (!EnsureContext()) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            int context = generation;
            string q = Uri.EscapeDataString((query ?? "").Trim());
            Api.Get($"/guilds?query={q}&page={Math.Max(1, page)}", r =>
            {
                if (context != generation || !CurrentContext) return;
                if (!r.ok) { done?.Invoke(false, ErrorText(r)); Changed?.Invoke(); return; }
                Found.Clear();
                foreach (var o in MiniJson.Arr(r.data, "guilds") ?? new List<object>())
                    Found.Add(new Listing
                    {
                        id = MiniJson.Str(o, "id"), name = MiniJson.Str(o, "name", ""), notice = MiniJson.Str(o, "notice", ""),
                        joinMode = MiniJson.Str(o, "join_mode", "approval"), leaderName = MiniJson.Str(o, "leader_name", ""),
                        memberCount = MiniJson.Int(o, "member_count"), maxMembers = MiniJson.Int(o, "max_members", MaxMembers),
                    });
                FoundPage = MiniJson.Int(r.data, "page", Math.Max(1, page));
                FoundTotal = MiniJson.Int(r.data, "total", Found.Count);
                done?.Invoke(true, null);
                Changed?.Invoke();
            });
        }

        // =============================== writes ===============================

        public static string ErrorText(ApiResult r) => r.code switch
        {
            "GUILD_NAME_TAKEN" => L.T("이미 쓰고 있는 길드 이름입니다."),
            "GUILD_FULL" => L.F("길드 인원이 가득 찼습니다 (최대 {0}명).", MaxMembers),
            "GUILD_COOLDOWN" => L.T("길드를 나온 뒤 24시간 동안은 다른 길드에 들어갈 수 없습니다."),
            "GUILD_FORBIDDEN" => L.T("이 일은 길드장이나 부길드장만 할 수 있습니다."),
            "ALREADY_IN_GUILD" => L.T("이미 길드에 들어가 있습니다."),
            "NOT_IN_GUILD" => L.T("길드에 들어가 있지 않습니다."),
            "LEVEL_TOO_LOW" => L.F("길드는 Lv.{0}부터 만들 수 있습니다.", CreateLevel),
            "GOLD_SHORT" => L.F("골드가 모자랍니다 ({0:N0} G 필요).", CreateGold),
            "NETWORK" => L.T("서버에 연결할 수 없습니다."),
            _ => string.IsNullOrEmpty(r.message) ? L.T("요청이 실패했습니다.") : r.message,
        };

        static void Send(string method, string path, Dictionary<string, object> body, string okText, Action<bool, string> done)
        {
            if (!EnsureContext()) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            if (Busy) { done?.Invoke(false, L.T("이전 요청을 처리하는 중입니다.")); return; }
            Busy = true;
            int context = generation;
            readVersion++; // a GET started before this write cannot bring back the old state
            Changed?.Invoke();
            body ??= new Dictionary<string, object>();
            body["request_id"] = ApiClient.NewRequestId();
            Action<ApiResult> reply = r =>
            {
                if (context != generation || !CurrentContext) return;
                Busy = false;
                if (!r.ok) { done?.Invoke(false, ErrorText(r)); Refresh(); Changed?.Invoke(); return; }
                // Gold spent (making a guild): the server's wallet, when it sends one.
                var delta = MiniJson.Obj(r.data, "delta");
                if (delta != null) OnlineEconomy.ApplyDelta(delta);
                else if (MiniJson.Has(r.data, "gold")) OnlineEconomy.ApplyDelta(new Dictionary<string, object> { ["gold"] = r.data["gold"] });
                bool joined = MiniJson.Has(r.data, "joined") && r.data["joined"] is bool j && j;
                // Writes answer with the guild view when they can; otherwise read it again.
                if (r.data != null && (r.data.ContainsKey("guild") || r.data.ContainsKey("my_applications"))) Read(r.data);
                else Refresh();
                done?.Invoke(true, joined ? L.T("길드에 들어갔습니다.") : okText);
                Changed?.Invoke();
            };
            string url = "/characters/" + character + path;
            if (method == "PATCH") Api.Patch(url, body, reply);
            else Api.PostIdempotent(url, body, reply);
        }

        /// <summary>Client-side check of a guild name (the server decides; this only saves a round trip).</summary>
        public static string NameProblem(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length < NameMin || name.Length > NameMax) return L.F("길드 이름은 {0}~{1}자로 지어 주세요.", NameMin, NameMax);
            if (ChatRules.Filter(name) != name) return L.T("쓸 수 없는 낱말이 들어 있습니다.");
            return null;
        }

        public static void Create(string name, string notice, bool open, Action<bool, string> done)
        {
            string problem = NameProblem(name);
            if (problem != null) { done?.Invoke(false, problem); return; }
            if (Game.Session != null && Game.Session.Progression.Level < CreateLevel) { done?.Invoke(false, L.F("길드는 Lv.{0}부터 만들 수 있습니다.", CreateLevel)); return; }
            if (Game.Session != null && Game.Session.Gold < CreateGold) { done?.Invoke(false, L.F("골드가 모자랍니다 ({0:N0} G 필요).", CreateGold)); return; }
            var body = new Dictionary<string, object> { ["name"] = name.Trim(), ["join_mode"] = open ? "open" : "approval" };
            if (!string.IsNullOrEmpty(notice)) body["notice"] = Trim(notice, NoticeMax);
            Send("POST", "/guild", body, L.F("길드 '{0}'을(를) 만들었습니다.", name.Trim()), done);
        }

        public static void Apply(Listing g, Action<bool, string> done)
        {
            if (g == null) return;
            if (CoolingDown) { done?.Invoke(false, L.T("길드를 나온 뒤 24시간 동안은 다른 길드에 들어갈 수 없습니다.")); return; }
            Send("POST", "/guild/apply", new Dictionary<string, object> { ["guild_id"] = g.id }, L.F("'{0}'에 가입을 신청했습니다.", g.name), done);
        }

        public static void Accept(Application a, Action<bool, string> done) =>
            Send("POST", "/guild/applications/" + a.id + "/accept", null, L.F("{0}님이 길드에 들어왔습니다.", a.name), done);

        public static void Reject(Application a, Action<bool, string> done) =>
            Send("POST", "/guild/applications/" + a.id + "/reject", null, L.F("{0}님의 신청을 거절했습니다.", a.name), done);

        public static void Leave(Action<bool, string> done) => Send("POST", "/guild/leave", null, L.T("길드를 나왔습니다."), done);
        public static void Disband(Action<bool, string> done) => Send("POST", "/guild/disband", null, L.T("길드를 해체했습니다."), done);

        public static void Kick(Member m, Action<bool, string> done) =>
            Send("POST", "/guild/members/" + m.id + "/kick", null, L.F("{0}님을 내보냈습니다.", m.name), done);
        public static void Promote(Member m, Action<bool, string> done) =>
            Send("POST", "/guild/members/" + m.id + "/promote", null, L.F("{0}님을 부길드장으로 임명했습니다.", m.name), done);
        public static void Demote(Member m, Action<bool, string> done) =>
            Send("POST", "/guild/members/" + m.id + "/demote", null, L.F("{0}님을 길드원으로 바꿨습니다.", m.name), done);
        public static void Transfer(Member m, Action<bool, string> done) =>
            Send("POST", "/guild/members/" + m.id + "/transfer", null, L.F("{0}님에게 길드장을 넘겼습니다.", m.name), done);

        public static void UpdateSettings(string notice, bool? open, Action<bool, string> done)
        {
            var body = new Dictionary<string, object>();
            if (notice != null) body["notice"] = Trim(notice, NoticeMax);
            if (open.HasValue) body["join_mode"] = open.Value ? "open" : "approval";
            Send("PATCH", "/guild", body, L.T("길드 설정을 바꿨습니다."), done);
        }

        static string Trim(string s, int max) { s = (s ?? "").Trim(); return s.Length > max ? s.Substring(0, max) : s; }
    }
}
