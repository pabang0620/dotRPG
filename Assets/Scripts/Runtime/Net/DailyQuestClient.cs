using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [DAILY 14] 일일 의뢰 (Docs/server/phase14_daily_quests.md): three short jobs a day from 1-19 on. The server owns the
    /// list, the progress and the rewards; the client only shows them. Offline play has none.
    /// </summary>
    public static class DailyQuestClient
    {
        public sealed class Slot
        {
            public string day, templateId, type, title, text, label, map, target, state;
            /// <summary>"day" or "week" (sent back on accept / claim).</summary>
            public string period = "day";
            public int slot, count, progress, xp, gold;
            public bool Offered => state == "offered";
            public bool Ready => state == "ready";
            public bool Claimed => state == "claimed";
        }

        /// <summary>[DAILY CHEST] Docs/PLAN_DAILY_REWARDS_2026-10-24.md: one chest per day / week once every job of it is collected.</summary>
        public sealed class Chest
        {
            public string period = "day", day, state, name, grade;
            public int done, need, streak, step, bonusRolls, skinGauge;
            public bool maxLevel;
            public readonly List<KeyValuePair<string, int>> rewards = new List<KeyValuePair<string, int>>();
            public bool Ready => state == "ready";
            public bool Claimed => state == "claimed";
        }

        /// <summary>Daily chests to show: yesterday's (when it still matters) first, today's last.</summary>
        public static readonly List<Chest> DayChests = new List<Chest>();
        public static Chest WeekChest { get; private set; }
        /// <summary>Consecutive days with the daily chest opened (0 = broken); Grace = finishing yesterday's jobs today keeps it.</summary>
        public static int Streak { get; private set; }
        public static int StreakCycle { get; private set; } = 7;
        public static bool StreakGrace { get; private set; }

        public static readonly List<Slot> Slots = new List<Slot>();
        /// <summary>[WEEKLY] This week's jobs (Thursday 06:00 reset, no carry-over).</summary>
        public static readonly List<Slot> WeeklySlots = new List<Slot>();
        public static bool Loaded => loaded && CurrentContext;
        public static bool Busy { get; private set; }
        public static bool Unlocked { get; private set; }
        public static int Level { get; private set; }
        public static string Giver { get; private set; } = "";
        /// <summary>Next 06:00 reset in local time, or null when unknown.</summary>
        public static DateTime? NextReset { get; private set; }
        public static DateTime? NextWeeklyReset { get; private set; }
        public static event Action Changed;

        static OnlineSession owner;
        static string character;
        static int generation, readVersion;
        static bool loaded;
        static bool CurrentContext => OnlineSession.Playing && ReferenceEquals(owner, OnlineSession.Current) && character == owner.ActiveCharacter;

        public static bool CanAccept(Slot s) => Loaded && !Busy && Unlocked && s != null && s.Offered;
        public static bool CanClaim(Slot s) => Loaded && !Busy && s != null && s.Ready;
        public static bool CanOpen(Chest c) => Loaded && !Busy && Unlocked && c != null && c.Ready;
        static bool AnyChestReady { get { if (WeekChest != null && WeekChest.Ready) return true; foreach (var c in DayChests) if (c.Ready) return true; return false; } }
        /// <summary>Something to do in the window: a job to take or a reward to collect.</summary>
        public static bool AnyActionable { get { if (!Loaded || !Unlocked) return false; foreach (var s in Slots) if (s.Offered || s.Ready) return true; foreach (var s in WeeklySlots) if (s.Offered || s.Ready) return true; return AnyChestReady; } }
        public static bool AnyReady { get { if (!Loaded) return false; foreach (var s in Slots) if (s.Ready) return true; foreach (var s in WeeklySlots) if (s.Ready) return true; return AnyChestReady; } }

        static ApiClient Api => ApiClient.Instance;

        /// <summary>Invalidate cached slots and in-flight replies on character/account changes.</summary>
        public static void Reset()
        {
            generation++;
            readVersion++;
            owner = null;
            character = null;
            loaded = Busy = Unlocked = false;
            Level = 0;
            Giver = "";
            NextReset = NextWeeklyReset = null;
            Slots.Clear();
            WeeklySlots.Clear();
            DayChests.Clear();
            WeekChest = null;
            Streak = 0;
            StreakGrace = false;
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
            Api.Get("/characters/" + character + "/dailies", r =>
            {
                if (context != generation || version != readVersion || !CurrentContext) return;
                if (r.ok) Read(r.data);
                Changed?.Invoke();
            });
        }

        static void Read(Dictionary<string, object> d)
        {
            if (d == null || MiniJson.Str(d, "character_id") != character || !CurrentContext) return;
            Unlocked = d.TryGetValue("unlocked", out var u) && u is bool ub && ub;
            Level = MiniJson.Int(d, "level", Level);
            Giver = MiniJson.Str(d, "giver", "") ?? "";
            NextReset = ParseTime(MiniJson.Str(d, "next_reset_at"));
            var list = MiniJson.Arr(d, "slots");
            if (list == null) return;
            ReadSlots(list, Slots, "day");
            var weekly = MiniJson.Obj(d, "weekly");
            NextWeeklyReset = ParseTime(MiniJson.Str(weekly, "next_reset_at"));
            ReadSlots(MiniJson.Arr(weekly, "slots"), WeeklySlots, "week");
            ReadChests(MiniJson.Obj(d, "chests"));
            loaded = true;
        }

        static void ReadChests(Dictionary<string, object> c)
        {
            DayChests.Clear();
            WeekChest = null;
            Streak = 0;
            StreakGrace = false;
            if (c == null) return;
            foreach (var o in MiniJson.Arr(c, "day") ?? new List<object>()) DayChests.Add(ReadChest(o as Dictionary<string, object>));
            WeekChest = ReadChest(MiniJson.Obj(c, "week"));
            var st = MiniJson.Obj(c, "streak");
            Streak = MiniJson.Int(st, "current");
            StreakCycle = Math.Max(1, MiniJson.Int(st, "cycle", 7));
            StreakGrace = st != null && st.TryGetValue("grace", out var g) && g is bool gb && gb;
        }

        static Chest ReadChest(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var c = new Chest
            {
                period = MiniJson.Str(o, "period", "day"), day = MiniJson.Str(o, "day"), state = MiniJson.Str(o, "state", "locked"),
                name = MiniJson.Str(o, "name", ""), grade = MiniJson.Str(o, "grade", ""), done = MiniJson.Int(o, "done"), need = MiniJson.Int(o, "need"),
                streak = MiniJson.Int(o, "streak"), step = MiniJson.Int(o, "step"), bonusRolls = MiniJson.Int(o, "bonus_rolls"), skinGauge = MiniJson.Int(o, "skin_gauge"),
                maxLevel = o.TryGetValue("max_level", out var m) && m is bool mb && mb,
            };
            foreach (var r in MiniJson.Arr(o, "rewards") ?? new List<object>()) c.rewards.Add(new KeyValuePair<string, int>(MiniJson.Str(r, "item_key"), MiniJson.Int(r, "count")));
            return c;
        }

        static DateTime? ParseTime(string iso) =>
            !string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime() : (DateTime?)null;

        static void ReadSlots(List<object> list, List<Slot> into, string period)
        {
            into.Clear();
            if (list == null) return;
            foreach (var o in list)
            {
                var reward = MiniJson.Obj(o, "reward");
                into.Add(new Slot
                {
                    period = MiniJson.Str(o, "period", period), day = MiniJson.Str(o, "day"), slot = MiniJson.Int(o, "slot"),
                    templateId = MiniJson.Str(o, "template_id"), type = MiniJson.Str(o, "type"), title = MiniJson.Str(o, "title", ""),
                    text = MiniJson.Str(o, "text", ""), label = MiniJson.Str(o, "label", ""), map = MiniJson.Str(o, "map", ""),
                    target = MiniJson.Str(o, "target", ""), state = MiniJson.Str(o, "state", "offered"),
                    count = MiniJson.Int(o, "count"), progress = MiniJson.Int(o, "progress"),
                    xp = MiniJson.Int(reward, "xp"), gold = MiniJson.Int(reward, "gold"),
                });
            }
        }

        static string ErrorText(ApiResult r) => r.code switch
        {
            "DAILY_LOCKED" => L.T("1-15 '전직의 길'을 마치면 열립니다."),
            "DAILY_EXPIRED" => L.T("기한이 지난 의뢰입니다."),
            "DAILY_NOT_DONE" => L.T("아직 목표를 채우지 못했습니다."),
            "DAILY_ALREADY_CLAIMED" => L.T("이미 보상을 받은 의뢰입니다."),
            "DAILY_CHEST_CLAIMED" => L.T("이미 연 상자입니다."),
            "DAILY_CHEST_LOCKED" => L.T("의뢰를 모두 끝내야 상자를 열 수 있습니다."),
            "NETWORK" => L.T("서버에 연결할 수 없습니다."),
            _ => string.IsNullOrEmpty(r.message) ? L.T("요청이 실패했습니다.") : r.message,
        };

        static void Post(string path, Slot s, Action<ApiResult> ok, Action<bool, string> done) =>
            Post(path, new Dictionary<string, object> { ["period"] = s.period ?? "day", ["day"] = s.day, ["slot"] = s.slot }, ok, done);

        static void Post(string path, Dictionary<string, object> body, Action<ApiResult> ok, Action<bool, string> done)
        {
            if (!EnsureContext()) { done(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            if (Busy) { done(false, L.T("이전 요청을 처리하는 중입니다.")); return; }
            Busy = true;
            int context = generation;
            readVersion++; // A GET started before this write cannot restore stale buttons.
            Changed?.Invoke();
            body["request_id"] = ApiClient.NewRequestId();
            Api.PostIdempotent("/characters/" + character + path, body, r =>
            {
                if (context != generation || !CurrentContext) return;
                Busy = false;
                if (!r.ok)
                {
                    done(false, ErrorText(r));
                    Refresh();
                    Changed?.Invoke();
                    return;
                }
                Read(r.data);
                ok(r);
                Changed?.Invoke();
            });
        }

        static string Kind(Slot s) => s.period == "week" ? L.T("주간 의뢰") : L.T("일일 의뢰");

        public static void Accept(Slot s, Action<bool, string> done)
        {
            if (!CanAccept(s)) { done(false, !Unlocked ? L.T("1-15 '전직의 길'을 마치면 열립니다.") : L.T("지금은 수락할 수 없습니다.")); return; }
            Post("/dailies/accept", s, r => { GuideFlags.Mark(GuideFlags.Daily); done(true, L.F("{0} '{1}'을(를) 수락했습니다.", Kind(s), L.T(s.title))); }, done);
        }

        public static void Claim(Slot s, Action<bool, string> done)
        {
            if (!CanClaim(s)) { done(false, L.T("아직 목표를 채우지 못했습니다.")); return; }
            Post("/dailies/claim", s, r =>
            {
                // Level, XP and gold from the server, the same path as quest rewards (delta when sent, else the top-level values).
                var delta = MiniJson.Obj(r.data, "delta");
                if (delta != null) OnlineEconomy.ApplyDelta(delta);
                else
                {
                    var d = new Dictionary<string, object>();
                    if (MiniJson.Has(r.data, "level")) d["level"] = r.data["level"];
                    if (MiniJson.Has(r.data, "xp")) d["xp"] = r.data["xp"];
                    if (MiniJson.Has(r.data, "gold")) d["gold"] = r.data["gold"];
                    OnlineEconomy.ApplyDelta(d);
                }
                Game.Flow.Autosave();
                var granted = MiniJson.Obj(r.data, "granted");
                int xp = MiniJson.Int(granted, "xp"), gold = MiniJson.Int(granted, "gold");
                var parts = new List<string>();
                if (xp > 0) parts.Add(L.F("경험치 {0:N0}", xp));
                if (gold > 0) parts.Add(L.F("골드 {0:N0}", gold));
                done(true, L.F("{0} '{1}' 완료!", Kind(s), L.T(s.title)) + (parts.Count > 0 ? " " + string.Join(", ", parts) : ""));
            }, done);
        }

        /// <summary>[DAILY CHEST] Opens a completion chest: the server rolls the bonus, merges fragments and fills the skin gauge.</summary>
        public static void OpenChest(Chest c, Action<bool, string> done)
        {
            if (!CanOpen(c)) { done(false, L.T("의뢰를 모두 끝내야 상자를 열 수 있습니다.")); return; }
            Post("/dailies/chest", new Dictionary<string, object> { ["period"] = c.period ?? "day", ["day"] = c.day }, r =>
            {
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                Game.Flow.Autosave();
                var opened = MiniJson.Obj(r.data, "opened");
                var sum = new Dictionary<string, int>();
                var order = new List<string>();
                bool big = false;
                foreach (var o in MiniJson.Arr(opened, "rewards") ?? new List<object>())
                {
                    string key = MiniJson.Str(o, "item_key");
                    if (!sum.ContainsKey(key)) { sum[key] = 0; order.Add(key); }
                    sum[key] += MiniJson.Int(o, "count");
                    if (key == ConsumableDatabase.ProtectTicket || key == DungeonDatabase.RaidCore) big = true;
                }
                var parts = new List<string>();
                foreach (var k in order) parts.Add(L.F("{0} x{1}", L.T(DungeonDatabase.ItemName(k)), sum[k]));
                var merged = MiniJson.Obj(opened, "merged");
                int tickets = MiniJson.Int(merged, "tickets");
                if (tickets > 0) { parts.Add(L.F("보호권 조각이 모여 {0} x{1}", L.T(DungeonDatabase.ItemName(ConsumableDatabase.ProtectTicket)), tickets)); big = true; }
                int gauge = MiniJson.Int(opened, "skin_gauge");
                if (gauge > 0) parts.Add(L.F("스킨 선택 게이지 +{0}칸", gauge));
                // Same reveal as opening a box from the bag (CashClient.OpenItem): fanfare on a big pull, then the toast.
                Game.Audio.PlaySfx(big ? "rank_reveal" : "pickup");
                string title = L.T(MiniJson.Str(opened, "name", ""));
                int streak = MiniJson.Int(opened, "streak");
                string head = c.period == "week" ? title : L.F("{0} (연속 {1}일)", title, streak);
                done(true, (big ? L.T("<color=#ffd34a>대박!</color> ") : "") + head + ": " + string.Join(", ", parts));
            }, done);
        }
    }
}
