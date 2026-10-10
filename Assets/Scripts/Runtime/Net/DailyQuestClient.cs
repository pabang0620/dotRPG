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
        /// <summary>Something to do in the window: a job to take or a reward to collect.</summary>
        public static bool AnyActionable { get { if (!Loaded || !Unlocked) return false; foreach (var s in Slots) if (s.Offered || s.Ready) return true; foreach (var s in WeeklySlots) if (s.Offered || s.Ready) return true; return false; } }
        public static bool AnyReady { get { if (!Loaded) return false; foreach (var s in Slots) if (s.Ready) return true; foreach (var s in WeeklySlots) if (s.Ready) return true; return false; } }

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
            loaded = true;
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
            "DAILY_LOCKED" => "1-19 '강해져야 한다'를 마치면 열립니다.",
            "DAILY_EXPIRED" => "기한이 지난 의뢰입니다.",
            "DAILY_NOT_DONE" => "아직 목표를 채우지 못했습니다.",
            "DAILY_ALREADY_CLAIMED" => "이미 보상을 받은 의뢰입니다.",
            "NETWORK" => "서버에 연결할 수 없습니다.",
            _ => string.IsNullOrEmpty(r.message) ? "요청이 실패했습니다." : r.message,
        };

        static void Post(string path, Slot s, Action<ApiResult> ok, Action<bool, string> done)
        {
            if (!EnsureContext()) { done(false, "온라인 캐릭터로 접속해야 합니다."); return; }
            if (Busy) { done(false, "이전 요청을 처리하는 중입니다."); return; }
            Busy = true;
            int context = generation;
            readVersion++; // A GET started before this write cannot restore stale buttons.
            Changed?.Invoke();
            var body = new Dictionary<string, object> { ["period"] = s.period ?? "day", ["day"] = s.day, ["slot"] = s.slot, ["request_id"] = ApiClient.NewRequestId() };
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

        static string Kind(Slot s) => s.period == "week" ? "주간 의뢰" : "일일 의뢰";

        public static void Accept(Slot s, Action<bool, string> done)
        {
            if (!CanAccept(s)) { done(false, !Unlocked ? "1-19 '강해져야 한다'를 마치면 열립니다." : "지금은 수락할 수 없습니다."); return; }
            Post("/dailies/accept", s, r => done(true, $"{Kind(s)} '{s.title}'을(를) 수락했습니다."), done);
        }

        public static void Claim(Slot s, Action<bool, string> done)
        {
            if (!CanClaim(s)) { done(false, "아직 목표를 채우지 못했습니다."); return; }
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
                if (xp > 0) parts.Add($"경험치 {xp:N0}");
                if (gold > 0) parts.Add($"골드 {gold:N0}");
                done(true, $"{Kind(s)} '{s.title}' 완료!" + (parts.Count > 0 ? " " + string.Join(", ", parts) : ""));
            }, done);
        }
    }
}
