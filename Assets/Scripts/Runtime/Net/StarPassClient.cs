using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CASH] 별빛 정기권 (Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md B): a 30-day pass bought with real money (Steam). While it
    /// runs, the first check of each game day (06:00 KST) gives 30 star shards and mails one sealed box to the playing
    /// character. The server owns the period and the daily claim; this only asks once per day and shows the days left.
    /// </summary>
    public static class StarPassClient
    {
        public const int DailyStars = 30;

        public static bool Active { get; private set; }
        public static int DaysLeft { get; private set; }
        public static bool TodayClaimed { get; private set; }
        public static bool Loaded { get; private set; }
        public static event Action Changed;

        static OnlineSession owner;
        static string character;
        static int generation;
        static bool busy;
        static float nextCheck;
        /// <summary>The next game-day boundary from the server (UTC); the daily claim is asked again after it.</summary>
        static DateTime nextDailyUtc = DateTime.MinValue;

        static ApiClient Api => ApiClient.Instance;
        static bool CurrentContext => OnlineSession.Playing && ReferenceEquals(owner, OnlineSession.Current) && character == owner.ActiveCharacter;

        static void ResetContext()
        {
            generation++;
            owner = OnlineSession.Current;
            character = owner?.ActiveCharacter;
            Active = TodayClaimed = Loaded = busy = false;
            DaysLeft = 0;
            nextCheck = Time.unscaledTime + 5f; // let the session settle first
            nextDailyUtc = DateTime.MinValue;
            Changed?.Invoke();
        }

        /// <summary>Called every frame by GameFlow: checks the pass a few seconds after entering, then once a game day.</summary>
        public static void Tick()
        {
            if (!OnlineSession.Playing || Api == null) return;
            if (!CurrentContext) { ResetContext(); return; }
            if (busy || Time.unscaledTime < nextCheck) return;
            // Active and already claimed today: nothing to ask until the next game day (or a 10 minute status refresh).
            nextCheck = Time.unscaledTime + 600f;
            if (Loaded && Active && TodayClaimed && DateTime.UtcNow < nextDailyUtc) { Refresh(); return; }
            Refresh(claimIfDue: true);
        }

        /// <summary>Reads the pass status (shop screen); with claimIfDue, takes today's grant when the pass runs.</summary>
        public static void Refresh(bool claimIfDue = false)
        {
            if (!OnlineSession.Playing || Api == null || busy) return;
            if (!CurrentContext) ResetContext();
            int context = generation;
            busy = true;
            Api.Get("/star-pass", r =>
            {
                busy = false;
                if (context != generation || !CurrentContext) return;
                if (r.ok) Read(r.data);
                Changed?.Invoke();
                if (r.ok && claimIfDue && Active && !TodayClaimed) ClaimDaily();
            });
        }

        static void Read(Dictionary<string, object> d)
        {
            if (d == null) return;
            Active = d.TryGetValue("active", out var a) && a is bool ab && ab;
            TodayClaimed = d.TryGetValue("today_claimed", out var t) && t is bool tb && tb;
            DaysLeft = MiniJson.Int(d, "days_left");
            if (DateTime.TryParse(MiniJson.Str(d, "next_daily_at"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var next))
                nextDailyUtc = next;
            Loaded = true;
        }

        static void ClaimDaily()
        {
            if (!CurrentContext || busy) return;
            int context = generation;
            busy = true;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() };
            Api.PostIdempotent("/characters/" + character + "/star-pass/daily", body, r =>
            {
                busy = false;
                if (context != generation || !CurrentContext) return;
                if (!r.ok) return; // PASS_REQUIRED (expired meanwhile) or offline: the next check tries again
                Read(MiniJson.Obj(r.data, "pass"));
                if (r.data != null && r.data.TryGetValue("granted", out var g) && g is bool granted && granted)
                {
                    _ = StarShopClient.RefreshAsync();
                    GameEvents.RaiseToast($"별빛 정기권: 별조각 {DailyStars}개를 받았고, 봉인된 상자 1개를 우편으로 보냈습니다. (남은 {DaysLeft}일)");
                }
                Changed?.Invoke();
            });
        }

        /// <summary>Short status for the star shop header ("" when the pass is not running or not loaded).</summary>
        public static string Badge => Loaded && Active ? $"<color=#9fd4ff>별빛 정기권 {DaysLeft}일</color>   " : "";
    }
}
