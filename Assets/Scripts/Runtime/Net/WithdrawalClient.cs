using System;
using System.Collections.Generic;
using System.Globalization;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] Account withdrawal (Docs/server/phase12_withdrawal.md): W1 notice, W2 request, W3 cancel from the login screen.
    /// The server decides the grace days, the loss list and the confirm phrase; the client only shows them and sends what the player typed.
    /// </summary>
    public static class WithdrawalClient
    {
        public sealed class Info
        {
            public string confirmPhrase = "";
            public int graceDays;
            public string dueAtIfNow;
            /// <summary>"steam" (a Steam ticket is the proof) or "dev" (the account password).</summary>
            public string reauth = "dev";
            public bool canRequest;
            public readonly List<string> blockers = new List<string>();
            public readonly List<string> notices = new List<string>();
            public int characters;
            public long goldTotal, paidStars, freeStars, starDebt;
            public int unclaimedMails, activeListings, topBids;
        }

        static ApiClient Api => ApiClient.Instance;

        /// <summary>W1.</summary>
        public static void LoadInfo(Action<ApiResult, Info> done)
        {
            Api.Get("/me/withdrawal", r =>
            {
                if (!r.ok) { done(r, null); return; }
                var info = new Info
                {
                    confirmPhrase = MiniJson.Str(r.data, "confirm_phrase", ""),
                    graceDays = MiniJson.Int(r.data, "grace_days", 30),
                    dueAtIfNow = MiniJson.Str(r.data, "due_at_if_now", ""),
                    reauth = MiniJson.Str(r.data, "reauth", "dev"),
                    canRequest = r.data != null && r.data.TryGetValue("can_request", out var c) && c is bool b && b,
                };
                foreach (var o in MiniJson.Arr(r.data, "blockers") ?? new List<object>()) info.blockers.Add(o as string);
                foreach (var o in MiniJson.Arr(r.data, "notices") ?? new List<object>()) info.notices.Add(o as string);
                var losses = MiniJson.Obj(r.data, "losses");
                info.characters = (MiniJson.Arr(losses, "characters") ?? new List<object>()).Count;
                info.goldTotal = (long)MiniJson.Num(losses, "gold_total");
                info.paidStars = (long)MiniJson.Num(losses, "paid_stars");
                info.freeStars = (long)MiniJson.Num(losses, "free_stars");
                info.starDebt = (long)MiniJson.Num(losses, "star_debt");
                info.unclaimedMails = MiniJson.Int(losses, "unclaimed_mails");
                info.activeListings = MiniJson.Int(losses, "active_listings");
                info.topBids = MiniJson.Int(losses, "top_bids");
                done(r, info);
            });
        }

        /// <summary>
        /// W2. <paramref name="password"/> null = prove with a Steam ticket. The same request_id is resent after a lost answer
        /// (the server replays the first result); when the resend meets a dead token (401) the request went through already,
        /// which a login attempt confirms (403 ACCOUNT_WITHDRAWAL_PENDING).
        /// </summary>
        public static void Request(string requestId, string confirm, bool ackPaidLoss, string loginId, string password, Action<ApiResult> done)
        {
            Reauth(loginId, password, false, (reauth, error) =>
            {
                if (reauth == null) { done(new ApiResult { code = "REAUTH_UNAVAILABLE", message = error }); return; }
                var body = new Dictionary<string, object>
                {
                    ["request_id"] = requestId, ["confirm"] = confirm, ["ack_progress_loss"] = true, ["reauth"] = reauth,
                };
                if (ackPaidLoss) body["ack_paid_loss"] = true;
                Api.PostIdempotent("/me/withdrawal", body, r =>
                {
                    if (r.ok || r.status != 401) { done(r); return; }
                    VerifyPending(loginId, password, pending => done(pending ? new ApiResult { ok = true, status = 201, message = "" } : r));
                });
            });
        }

        /// <summary>W3: no token (the login is blocked). loginId null = Steam.</summary>
        public static void Cancel(string loginId, string password, Action<ApiResult> done)
        {
            Reauth(loginId, password, true, (reauth, error) =>
            {
                if (reauth == null) { done(new ApiResult { code = "REAUTH_UNAVAILABLE", message = error }); return; }
                var body = new Dictionary<string, object>
                {
                    ["request_id"] = ApiClient.NewRequestId(), ["reauth"] = reauth, ["device"] = DeviceIdentity.Body(),
                };
                Api.Post("/auth/withdrawal/cancel", body, done, auth: false);
            });
        }

        /// <summary>Logging in again tells whether the account is waiting for withdrawal (the answer is 403 ACCOUNT_WITHDRAWAL_PENDING).</summary>
        static void VerifyPending(string loginId, string password, Action<bool> done)
        {
            if (password != null)
            {
                var body = new Dictionary<string, object> { ["login_id"] = loginId, ["password"] = password, ["device"] = DeviceIdentity.Body() };
                Api.Post("/auth/dev/login", body, r => done(r.code == "ACCOUNT_WITHDRAWAL_PENDING"), auth: false);
                return;
            }
            SteamTicket((ticket, error) =>
            {
                if (ticket == null) { done(false); return; }
                var body = new Dictionary<string, object> { ["ticket"] = ticket, ["device"] = DeviceIdentity.Body() };
                Api.Post("/auth/steam", body, r => done(r.code == "ACCOUNT_WITHDRAWAL_PENDING"), auth: false);
            });
        }

        static void Reauth(string loginId, string password, bool withLoginId, Action<Dictionary<string, object>, string> done)
        {
            if (password != null)
            {
                var dev = new Dictionary<string, object> { ["provider"] = "dev", ["password"] = password };
                if (withLoginId) dev["login_id"] = loginId;
                done(dev, null);
                return;
            }
            SteamTicket((ticket, error) =>
                done(ticket == null ? null : new Dictionary<string, object> { ["provider"] = "steam", ["ticket"] = ticket }, error));
        }

        /// <summary>A one-use Steam web ticket for the server (identity from /meta). done(ticket, null) or done(null, Korean reason).</summary>
        public static void SteamTicket(Action<string, string> done)
        {
            var steam = SteamBridge.Current;
            if (steam == null || !steam.Ready) { done(null, "Steam을 켠 뒤 다시 시도해 주세요."); return; }
            Api.Get("/meta", meta =>
            {
                if (!meta.ok) { done(null, string.IsNullOrEmpty(meta.message) ? "서버에 연결할 수 없습니다." : meta.message); return; }
                string identity = MiniJson.Str(MiniJson.Obj(meta.data, "steam"), "identity");
                if (string.IsNullOrEmpty(identity)) { done(null, "이 서버는 Steam 확인을 받지 않습니다."); return; }
                steam.GetAuthTicket(identity, ticket =>
                    done(string.IsNullOrEmpty(ticket) ? null : ticket, string.IsNullOrEmpty(ticket) ? "Steam 인증 티켓을 받지 못했습니다." : null));
            }, auth: false);
        }

        /// <summary>Korean line for a refused withdrawal call (null = no special text, use the server's message).</summary>
        public static string Explain(ApiResult r)
        {
            switch (r.code)
            {
                case "REAUTH_FAILED": return "본인 확인에 실패했습니다.";
                case "CONFIRM_MISMATCH": return "확인 문구가 일치하지 않습니다.";
                case "ACK_REQUIRED": return "유료 별조각 소멸에 동의해야 합니다.";
                case "WITHDRAW_BLOCKED": return "결제 처리 중이라 지금은 탈퇴할 수 없습니다.";
                case "WITHDRAW_LIMIT": return "30일 안에 요청할 수 있는 횟수를 넘었습니다.";
                case "WITHDRAWAL_ALREADY_REQUESTED": return "이미 탈퇴를 요청한 계정입니다.";
                case "FEATURE_DISABLED": return "지금은 탈퇴 기능을 쓸 수 없습니다.";
                case "CANCEL_NOT_ALLOWED": return "운영자가 처리한 탈퇴입니다. 고객 지원에 문의하세요.";
                case "WITHDRAWAL_DUE": return "철회 기한이 지났습니다.";
                case "NO_PENDING_WITHDRAWAL": return "철회할 탈퇴 요청이 없습니다.";
                case "NETWORK": return "서버에 연결할 수 없습니다.";
                case "REAUTH_UNAVAILABLE": return r.message;
                default: return string.IsNullOrEmpty(r.message) ? $"요청이 실패했습니다. ({r.status})" : r.message;
            }
        }

        /// <summary>Local time text for a server UTC ISO time.</summary>
        public static string LocalTime(string iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
    }
}
