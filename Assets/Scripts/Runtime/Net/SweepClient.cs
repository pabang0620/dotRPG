using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[SWEEP] One sweep result from the server: XP, the one card and which ticket was used.</summary>
    public sealed class SweepResult
    {
        public string dungeonId;
        public int difficulty;
        public long xp;
        public bool leveledUp;
        public string cardKey;
        public int cardCount;
        public string ticket;
    }

    /// <summary>[SWEEP] One dungeon difficulty as the server sees it for sweeping.</summary>
    public sealed class SweepSlot
    {
        public bool canSweep;
        /// <summary>null, CLOSED_TODAY, NOT_CLEARED, RANK_LOW or LEVEL_TOO_LOW.</summary>
        public string block;
        public int bestRank = -1, needLevel;
        public long xp;
    }

    /// <summary>
    /// [SWEEP] Dungeon clear tickets (Docs/server/phase10_sweep_mail.md 12): the server decides everything (tickets,
    /// entries, rewards); the client shows its answers and never computes amounts. Offline play has no sweeping.
    /// </summary>
    public static class SweepClient
    {
        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        public static bool Loaded { get; private set; }
        public static bool Busy { get; private set; }
        public static bool Hold { get; private set; }
        /// <summary>The server has sweeping switched off (503 FEATURE_DISABLED) or is older than phase 10.</summary>
        public static bool Unavailable { get; private set; }
        public static int TicketsTotal { get; private set; }
        public static int TicketsNormal { get; private set; }
        public static readonly List<(int count, string expiresAt)> EventTickets = new List<(int, string)>();
        public static int EntriesLeft { get; private set; }
        public static int EntriesLimit { get; private set; }
        public static long UnitPrice { get; private set; }
        public static int ShopWeeklyLeft { get; private set; }
        public static int ShopWeeklyLimit { get; private set; }
        public static int WeeklyProgress { get; private set; }
        public static int WeeklyGoal { get; private set; }
        public static int WeeklyReward { get; private set; }
        public static bool WeeklyClaimable { get; private set; }
        public static bool WeeklyClaimed { get; private set; }
        static readonly Dictionary<string, SweepSlot> slots = new Dictionary<string, SweepSlot>();
        public static event Action Changed;

        public static SweepSlot Slot(string dungeonId, DungeonDifficulty d) => slots.TryGetValue(dungeonId + ":" + (int)d, out var s) ? s : null;

        /// <summary>Asks the server for the current state (window opened, after a sweep or a purchase).</summary>
        public static void Refresh()
        {
            if (!OnlineSession.Playing) return;
            Api.Get(Char + "/sweep", r =>
            {
                if (!r.ok) { Unavailable = r.status == 404 || r.code == "FEATURE_DISABLED"; Changed?.Invoke(); return; }
                Unavailable = false;
                Read(r.data);
                Loaded = true;
                Changed?.Invoke();
            });
        }

        static void Read(Dictionary<string, object> d)
        {
            ReadTickets(MiniJson.Obj(d, "tickets"));
            ReadEntries(MiniJson.Obj(d, "entries"));
            Hold = d.TryGetValue("hold", out var h) && h is bool hb && hb;
            var shop = MiniJson.Obj(d, "shop");
            if (shop != null) ReadShop(shop);
            var weekly = MiniJson.Obj(d, "weekly_activity");
            if (weekly != null) ReadWeekly(weekly);
            slots.Clear();
            foreach (var dun in MiniJson.Arr(d, "dungeons") ?? new List<object>())
            {
                string id = MiniJson.Str(dun, "id");
                foreach (var diff in MiniJson.Arr(dun, "difficulties") ?? new List<object>())
                    slots[id + ":" + MiniJson.Int(diff, "difficulty")] = new SweepSlot
                    {
                        canSweep = Flag(diff, "can_sweep"),
                        block = MiniJson.Str(diff, "block"),
                        bestRank = MiniJson.Has(diff, "best_rank") ? MiniJson.Int(diff, "best_rank") : -1,
                        needLevel = MiniJson.Int(diff, "need_level"),
                        xp = (long)MiniJson.Num(diff, "xp"),
                    };
            }
        }

        /// <summary>Updates the ticket wallet from any response that carries a "tickets" block (sweep, purchase, mail).</summary>
        public static void ReadTickets(Dictionary<string, object> t)
        {
            if (t == null) return;
            TicketsTotal = MiniJson.Int(t, "total");
            TicketsNormal = MiniJson.Int(t, "normal");
            EventTickets.Clear();
            foreach (var e in MiniJson.Arr(t, "event") ?? new List<object>())
                EventTickets.Add((MiniJson.Int(e, "count"), MiniJson.Str(e, "expires_at")));
            Changed?.Invoke();
        }

        static void ReadEntries(Dictionary<string, object> e)
        {
            if (e == null) return;
            EntriesLeft = MiniJson.Int(e, "left");
            EntriesLimit = MiniJson.Int(e, "limit");
        }

        static void ReadShop(Dictionary<string, object> s)
        {
            UnitPrice = (long)MiniJson.Num(s, "unit_price");
            ShopWeeklyLeft = MiniJson.Int(s, "weekly_left");
            ShopWeeklyLimit = MiniJson.Int(s, "weekly_limit");
        }

        static void ReadWeekly(Dictionary<string, object> w)
        {
            WeeklyProgress = MiniJson.Int(w, "progress");
            WeeklyGoal = MiniJson.Int(w, "goal", DungeonSweep.WeeklyDirectClears);
            WeeklyReward = MiniJson.Int(w, "reward_tickets", DungeonSweep.WeeklyRewardTickets);
            WeeklyClaimable = Flag(w, "claimable");
            WeeklyClaimed = Flag(w, "claimed");
        }

        static bool Flag(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b && b;

        /// <summary>Sweeps once (<paramref name="all"/> false) or as many times as entries and tickets allow.</summary>
        public static void Run(string dungeonId, DungeonDifficulty difficulty, bool all, Action<bool, string, List<SweepResult>, string> done)
        {
            var body = new Dictionary<string, object> { ["dungeon_id"] = dungeonId, ["difficulty"] = (int)difficulty };
            Send(all ? "/sweep/run-all" : "/sweep/run", body, true, r =>
            {
                if (!r.ok) { done(false, Explain(r), null, null); return; }
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                ReadTickets(MiniJson.Obj(r.data, "tickets"));
                ReadEntries(MiniJson.Obj(r.data, "entries"));
                var list = new List<SweepResult>();
                foreach (var s in MiniJson.Arr(r.data, "sweeps") ?? new List<object>())
                {
                    var card = MiniJson.Obj(s, "card");
                    list.Add(new SweepResult
                    {
                        dungeonId = MiniJson.Str(s, "dungeon_id"), difficulty = MiniJson.Int(s, "difficulty"),
                        xp = (long)MiniJson.Num(s, "xp"), leveledUp = Flag(s, "leveled_up"),
                        cardKey = MiniJson.Str(card, "item_key"), cardCount = MiniJson.Int(card, "count", 1),
                        ticket = MiniJson.Str(s, "ticket"),
                    });
                    // The local entry count follows the server (a sweep spends one of today's entries).
                    Game.Session?.Dungeons?.UseEntry(ResetClock.Now);
                }
                Game.Flow.Autosave();
                done(true, null, list, MiniJson.Str(r.data, "limited_by"));
                Refresh();
            });
        }

        public static void Buy(int count, Action<bool, string> done)
        {
            Send("/sweep/tickets/buy", new Dictionary<string, object> { ["count"] = count }, false, r =>
            {
                if (!r.ok) { done(false, Explain(r)); return; }
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                ReadTickets(MiniJson.Obj(r.data, "tickets"));
                var shop = MiniJson.Obj(r.data, "shop");
                if (shop != null) ReadShop(shop);
                done(true, $"던전 클리어권 {MiniJson.Int(r.data, "count")}장을 샀습니다. (-{(long)MiniJson.Num(r.data, "total"):N0} G)");
                Changed?.Invoke();
            });
        }

        public static void ClaimWeekly(Action<bool, string> done)
        {
            Send("/sweep/weekly/claim", new Dictionary<string, object>(), false, r =>
            {
                if (!r.ok) { done(false, Explain(r)); return; }
                ReadTickets(MiniJson.Obj(r.data, "tickets"));
                var weekly = MiniJson.Obj(r.data, "weekly_activity");
                if (weekly != null) ReadWeekly(weekly);
                done(true, $"주간 활동 보상: 던전 클리어권 {MiniJson.Int(MiniJson.Obj(r.data, "claimed"), "tickets")}장을 받았습니다.");
                Changed?.Invoke();
            });
        }

        /// <summary>One request with a fresh request_id; a missing presence is sent first and the same request repeated.</summary>
        static void Send(string path, Dictionary<string, object> body, bool needsPresence, Action<ApiResult> done)
        {
            if (!OnlineSession.Playing) { done(new ApiResult { ok = false, code = "OFFLINE", message = "온라인 캐릭터로 접속해야 합니다." }); return; }
            if (Busy) { done(new ApiResult { ok = false, code = "BUSY", message = "이전 요청을 처리하는 중입니다." }); return; }
            Busy = true;
            body["request_id"] = ApiClient.NewRequestId();
            string full = Char + path;
            void Post(bool retried) => Api.Post(full, body, r =>
            {
                if (!r.ok && r.code == "PRESENCE_REQUIRED" && !retried) { PresenceClient.Ping(() => Post(true)); return; }
                Busy = false;
                done(r);
            });
            if (needsPresence) PresenceClient.Ping(() => Post(false)); else Post(false);
        }

        public static string Explain(ApiResult r)
        {
            switch (r.code)
            {
                case "SWEEP_NOT_CLEARED": return "직접 한 번 클리어해야 소탕할 수 있습니다.";
                case "SWEEP_RANK_LOW": return "이 난이도를 B등급 이상으로 클리어해야 합니다.";
                case "LEVEL_TOO_LOW": return $"권장 레벨 Lv.{MiniJson.Int(r.errors, "need")} 이상이어야 소탕할 수 있습니다.";
                case "NO_ENTRIES_LEFT": return "오늘 입장 횟수를 모두 사용했습니다. (06:00 초기화)";
                case "NO_TICKET": return "던전 클리어권이 없습니다.";
                case "DUNGEON_CLOSED_TODAY": return "오늘은 열리지 않는 던전입니다.";
                case "WEEKLY_LIMIT": return "이번 주 구매 한도를 모두 사용했습니다.";
                case "NOT_ENOUGH_GOLD": return "골드가 부족합니다.";
                case "WEEKLY_NOT_READY": return "주간 활동 목표를 아직 채우지 못했습니다.";
                case "WEEKLY_ALREADY_CLAIMED": return "이번 주 보상을 이미 받았습니다.";
                case "FEATURE_DISABLED": return "던전 소탕은 아직 준비 중입니다.";
                case "RUN_ACTIVE": case "IN_PARTY_RUN": return "던전 진행 중에는 소탕할 수 없습니다.";
                case "NETWORK": return "서버에 연결할 수 없습니다.";
                default: return string.IsNullOrEmpty(r.message) ? $"요청이 실패했습니다. ({r.status})" : r.message;
            }
        }

        /// <summary>Text for a dungeon difficulty that cannot be swept (block code from the server).</summary>
        public static string BlockText(string block, SweepSlot s) => block switch
        {
            "CLOSED_TODAY" => "오늘은 열리지 않는 던전",
            "NOT_CLEARED" => "직접 한 번 클리어하면 소탕 가능",
            "RANK_LOW" => "B등급 이상 클리어 필요",
            "LEVEL_TOO_LOW" => $"Lv.{s?.needLevel} 이상 필요",
            _ => "",
        };
    }
}
