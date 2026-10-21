using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [BOSS 2026-10-20] 월드보스 / 길드보스 (Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 2, 3, 7): the server keeps the shared HP,
    /// the attempts left today and the ranking; a 3-minute attempt is started and finished here, the fight itself runs in
    /// <see cref="BossRushDirector"/>. Online only.
    /// </summary>
    public static class BossRushClient
    {
        public const string World = "world", Guild = "guild";
        public const int WorldAttemptsPerDay = 3, GuildAttemptsPerDay = 2, DefaultAttemptSeconds = 180, EntryLevel = 40;

        public sealed class Boss
        {
            public string kind, seasonId, bossId, name;
            public double maxHp, hpLeft;
            public int attemptsLeft, attemptsPerDay, myRank, guildRank;
            /// <summary>World: my best single attempt. Guild: this week's guild score and my share of it (no HP, no stages).</summary>
            public long myBest, score, myContribution;
            public DateTime? defeatedAt, eligibleAt;
            public bool Defeated => defeatedAt.HasValue || maxHp > 0 && hpLeft <= 0;
            public float HpShare => maxHp > 0 ? (float)Math.Max(0, Math.Min(1, hpLeft / maxHp)) : 0f;
            /// <summary>Guild boss: joined less than 24 hours ago.</summary>
            public bool Waiting => eligibleAt.HasValue && eligibleAt.Value > DateTime.Now;
        }

        /// <summary>One started attempt (the server's numbers for the boss in this room).</summary>
        public sealed class Attempt
        {
            public string kind, id, bossId;
            public int level;
            public float hpMul = 1f, damageMul = 1f;
            public DateTime startedAt, endsAt;
        }

        public sealed class Result
        {
            public long accepted, best, score, myContribution;
            public int rank;
            /// <summary>Expired: the report came after the deadline (plus grace), nothing was counted.</summary>
            public bool capped, defeated, expired;
            public double hpLeft;
        }

        public static Boss WorldBoss { get; private set; }
        /// <summary>My guild's boss (null = no guild).</summary>
        public static Boss GuildBoss { get; private set; }
        public static DateTime? WeekEndsAt { get; private set; }
        public static int AttemptSeconds { get; private set; } = DefaultAttemptSeconds;
        public static bool Loaded => loaded && CurrentContext;
        public static bool Busy { get; private set; }
        public static event Action Changed;

        static OnlineSession owner;
        static string character;
        static int generation, readVersion;
        static bool loaded;
        /// <summary>A finish the server has not accepted yet (sending failed): the next start sends it before trying again.</summary>
        static (string kind, string id, long damage, int hits, float seconds)? pendingReport;
        const int FinishRetries = 3;
        const float FinishRetryDelay = 2f;
        static bool CurrentContext => OnlineSession.Playing && ReferenceEquals(owner, OnlineSession.Current) && character == owner.ActiveCharacter;
        static ApiClient Api => ApiClient.Instance;

        public static Boss Of(string kind) => kind == Guild ? GuildBoss : WorldBoss;
        public static string KindName(string kind) => kind == Guild ? L.T("길드보스") : L.T("월드보스");

        public static void Reset()
        {
            generation++;
            readVersion++;
            owner = null;
            character = null;
            loaded = Busy = false;
            WorldBoss = GuildBoss = null;
            WeekEndsAt = null;
            AttemptSeconds = DefaultAttemptSeconds;
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
            Api.Get("/characters/" + character + "/bosses", r =>
            {
                if (context != generation || version != readVersion || !CurrentContext) return;
                if (r.ok) Read(r.data);
                Changed?.Invoke();
            });
        }

        static void Read(Dictionary<string, object> d)
        {
            if (d == null || !CurrentContext) return;
            WorldBoss = ReadBoss(MiniJson.Obj(d, "world"), World);
            GuildBoss = ReadBoss(MiniJson.Obj(d, "guild"), Guild);
            WeekEndsAt = GuildClient.ParseTime(MiniJson.Str(d, "week_ends_at"));
            AttemptSeconds = Math.Max(30, MiniJson.Int(d, "attempt_seconds", DefaultAttemptSeconds));
            loaded = true;
        }

        static Boss ReadBoss(Dictionary<string, object> o, string kind)
        {
            if (o == null) return null;
            return new Boss
            {
                kind = kind, seasonId = MiniJson.Str(o, "season_id"), bossId = MiniJson.Str(o, "boss_id", BossRushCatalog.BossId),
                name = MiniJson.Str(o, "name", ""), maxHp = MiniJson.Num(o, "max_hp"), hpLeft = MiniJson.Num(o, "hp_left"),
                attemptsLeft = MiniJson.Int(o, "attempts_left"),
                attemptsPerDay = MiniJson.Int(o, "attempts_per_day", kind == Guild ? GuildAttemptsPerDay : WorldAttemptsPerDay),
                myBest = (long)MiniJson.Num(o, "my_best"), score = (long)MiniJson.Num(o, "score"), myContribution = (long)MiniJson.Num(o, "my_contribution"),
                myRank = MiniJson.Int(o, "my_rank"), guildRank = MiniJson.Int(o, "guild_rank"),
                defeatedAt = GuildClient.ParseTime(MiniJson.Str(o, "defeated_at")), eligibleAt = GuildClient.ParseTime(MiniJson.Str(o, "eligible_at")),
            };
        }

        /// <summary>Why this boss cannot be tried now (null = it can).</summary>
        public static string BlockReason(string kind)
        {
            if (!OnlineSession.Playing) return L.T("온라인에서 열려요.");
            int level = Game.Session != null ? Game.Session.Progression.Level : 1;
            if (level < EntryLevel) return L.F("Lv.{0}부터 도전할 수 있어요.", EntryLevel);
            if (!Loaded) return L.T("서버에서 보스 정보를 받는 중입니다.");
            if (kind == Guild && !GuildClient.InGuild) return L.T("길드에 들어가면 도전할 수 있어요.");
            var b = Of(kind);
            if (b == null) return kind == Guild ? L.T("길드에 들어가면 도전할 수 있어요.") : L.T("이번 주 월드보스 정보가 없습니다.");
            if (kind == Guild && b.Waiting) return L.F("길드 가입 24시간 뒤부터 도전할 수 있어요 ({0:MM-dd HH:mm}).", b.eligibleAt.Value);
            if (b.attemptsLeft <= 0) return L.T("오늘 도전 횟수를 모두 썼어요. 오전 6시에 다시 채워집니다.");
            return null;
        }

        static string ErrorText(ApiResult r) => r.code switch
        {
            "BOSS_NO_ATTEMPTS" => L.T("오늘 도전 횟수를 모두 썼어요. 오전 6시에 다시 채워집니다."),
            "LEVEL_TOO_LOW" => L.F("Lv.{0}부터 도전할 수 있어요.", EntryLevel),
            "NOT_IN_GUILD" => L.T("길드에 들어가면 도전할 수 있어요."),
            "GUILD_BOSS_NOT_YET" => L.T("길드 가입 24시간 뒤부터 도전할 수 있어요."),
            "BOSS_PARTY_NOT_ALLOWED" => L.T("파티를 나간 뒤 도전하세요."),
            "BOSS_ATTEMPT_OPEN" => L.T("끝나지 않은 도전이 있습니다. 잠시 뒤 다시 시도해 주세요."),
            "BOSS_ATTEMPT_CLOSED" => L.T("이미 끝난 도전입니다."),
            "BOSS_ATTEMPT_NOT_FOUND" => L.T("도전 기록을 찾을 수 없습니다."),
            "BOSS_SEASON_NOT_FOUND" => L.T("이번 주 보스 정보가 없습니다."),
            "NETWORK" => L.T("서버에 연결할 수 없습니다."),
            _ => string.IsNullOrEmpty(r.message) ? L.T("요청이 실패했습니다.") : r.message,
        };

        /// <summary>Starts an attempt (spends one of today's tries on the server).</summary>
        public static void Start(string kind, Action<Attempt, string> done) => Start(kind, done, false);

        /// <summary>
        /// <paramref name="recovered"/>: this is the second try after an attempt left open (app closed, report lost) was
        /// finished with the damage known here, so a second "still open" answer is reported instead of looping.
        /// </summary>
        static void Start(string kind, Action<Attempt, string> done, bool recovered)
        {
            if (!EnsureContext()) { done?.Invoke(null, L.T("온라인에서 열려요.")); return; }
            if (Busy) { done?.Invoke(null, L.T("이전 요청을 처리하는 중입니다.")); return; }
            Busy = true;
            int context = generation;
            readVersion++;
            Changed?.Invoke();
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() };
            Api.PostIdempotent("/characters/" + character + "/bosses/" + kind + "/attempts", body, r =>
            {
                if (context != generation || !CurrentContext) return;
                Busy = false;
                if (!r.ok && !recovered && r.code == "BOSS_ATTEMPT_OPEN" && !string.IsNullOrEmpty(MiniJson.Str(r.errors, "attempt_id")))
                {
                    RecoverOpen(kind, MiniJson.Str(r.errors, "attempt_id"), done);
                    return;
                }
                if (!r.ok) { done?.Invoke(null, ErrorText(r)); Refresh(); Changed?.Invoke(); return; }
                var d = r.data;
                var started = GuildClient.ParseTime(MiniJson.Str(d, "started_at")) ?? DateTime.Now;
                var a = new Attempt
                {
                    kind = kind, id = MiniJson.Str(d, "attempt_id"), bossId = MiniJson.Str(d, "boss_id", BossRushCatalog.BossId),
                    level = Math.Max(1, MiniJson.Int(d, "level", EntryLevel)),
                    hpMul = (float)Math.Max(0.01, MiniJson.Num(d, "hp_mul", 1)), damageMul = (float)Math.Max(0.01, MiniJson.Num(d, "damage_mul", 1)),
                    startedAt = started, endsAt = GuildClient.ParseTime(MiniJson.Str(d, "ends_at")) ?? started.AddSeconds(AttemptSeconds),
                };
                var b = Of(kind);
                if (b != null) b.attemptsLeft = Math.Max(0, b.attemptsLeft - 1);
                done?.Invoke(a, null);
                Changed?.Invoke();
            });
        }

        /// <summary>
        /// An attempt the server still has open (the app closed mid-fight or the report never arrived): finish it with the
        /// damage kept here (none when unknown), then start the new one. Its try was already spent.
        /// </summary>
        static void RecoverOpen(string kind, string attemptId, Action<Attempt, string> done)
        {
            var stale = new Attempt { kind = kind, id = attemptId };
            var p = pendingReport;
            bool known = p.HasValue && p.Value.id == attemptId;
            long damage = known ? p.Value.damage : 0;
            int hits = known ? p.Value.hits : 0;
            float seconds = known ? p.Value.seconds : 0f;
            Finish(stale, damage, hits, seconds, (_, error) =>
            {
                // Closed, expired or already finished all mean the same thing here: the way is clear for a new attempt.
                GameEvents.RaiseToast(L.T("끝나지 않은 도전을 정리했어요. 다시 시작합니다."));
                Start(kind, done, true);
            });
        }

        /// <summary>
        /// Reports my damage of a finished attempt. Sent again with the same request id (up to <see cref="FinishRetries"/>
        /// times) when the answer is lost; after that the report is kept for the next start.
        /// </summary>
        public static void Finish(Attempt a, long damage, int hits, float seconds, Action<Result, string> done)
        {
            if (a == null || !EnsureContext()) { done?.Invoke(null, L.T("온라인에서 열려요.")); return; }
            int context = generation;
            var body = new Dictionary<string, object>
            {
                ["request_id"] = ApiClient.NewRequestId(), ["damage_dealt"] = Math.Max(0, damage), ["hits_landed"] = Math.Max(0, hits),
                ["duration_s"] = Math.Round(Math.Max(0f, seconds), 1),
            };
            pendingReport = (a.kind, a.id, damage, hits, seconds);
            string path = "/characters/" + character + "/bosses/" + a.kind + "/attempts/" + a.id + "/finish";
            int tries = 0;
            Action send = null;
            send = () => Api.PostIdempotent(path, body, r =>
            {
                if (context != generation || !CurrentContext) return;
                tries++;
                // Only a lost answer is worth another try; a server "no" (closed, not found, expired) is final.
                if (!r.ok && r.code == "NETWORK" && tries < FinishRetries) { Api.StartCoroutine(After(FinishRetryDelay, send)); return; }
                if (!r.ok)
                {
                    if (r.code != "NETWORK") pendingReport = null; // the server has its own answer for this attempt
                    done?.Invoke(null, ErrorText(r));
                    Refresh();
                    return;
                }
                pendingReport = null;
                var d = r.data;
                var res = new Result
                {
                    accepted = (long)MiniJson.Num(d, "accepted_damage"), capped = MiniJson.Has(d, "capped") && d["capped"] is bool c && c,
                    best = (long)MiniJson.Num(d, "best"), rank = MiniJson.Int(d, "rank"), hpLeft = MiniJson.Num(d, "hp_left"),
                    score = (long)MiniJson.Num(d, "score"), myContribution = (long)MiniJson.Num(d, "my_contribution"),
                    defeated = MiniJson.Has(d, "defeated") && d["defeated"] is bool f && f,
                    expired = MiniJson.Has(d, "expired") && d["expired"] is bool x && x,
                };
                var b = Of(a.kind);
                if (b != null)
                {
                    b.myBest = Math.Max(b.myBest, res.best);
                    if (a.kind == Guild)
                    {
                        // The guild boss answers with the guild's score and rank (the rank is the guild's, not mine).
                        b.score = res.score;
                        b.myContribution = res.myContribution;
                        if (res.rank > 0) b.guildRank = res.rank;
                    }
                    else
                    {
                        b.hpLeft = res.hpLeft;
                        if (res.rank > 0) b.myRank = res.rank;
                    }
                }
                done?.Invoke(res, null);
                Refresh();
                Changed?.Invoke();
            });
            send();
        }

        static IEnumerator After(float seconds, Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
        }
    }
}
