using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [0045] Kakao / Google login and linking (Docs/server/OAUTH_LOGIN.md). No deep link or local listener:
    /// the server gives a login address, the game opens it in the browser and polls the server every 2 s
    /// until the browser step is done (10 min at most). PC and Android work the same way.
    /// </summary>
    public sealed partial class OnlineSession
    {
        /// <summary>One browser login in progress. Cancel() stops the polling (the server session just expires).</summary>
        public sealed class OauthFlow
        {
            public bool Cancelled { get; private set; }
            public void Cancel() => Cancelled = true;
        }

        /// <summary>Which providers this server has switched on (from /auth/oauth/providers).</summary>
        public static bool KakaoOn { get; private set; }
        public static bool GoogleOn { get; private set; }

        /// <summary>Login methods linked to this account (dev, steam, kakao, google), from /me.</summary>
        public readonly List<string> Providers = new List<string>();

        public static string ProviderName(string provider) => provider == "kakao" ? L.T("카카오") : "Google";

        public static void FetchOauthProviders(Action changed)
        {
            Api.Get("/auth/oauth/providers", r =>
            {
                if (r.ok && r.data != null)
                {
                    KakaoOn = r.data.TryGetValue("kakao", out var k) && k is bool kb && kb;
                    GoogleOn = r.data.TryGetValue("google", out var g) && g is bool gb && gb;
                }
                changed?.Invoke();
            }, auth: false);
        }

        public void LoadProviders(Action changed)
        {
            Api.Get("/me", r =>
            {
                if (r.ok)
                {
                    Providers.Clear();
                    foreach (var p in MiniJson.Arr(r.data, "providers") ?? new List<object>()) Providers.Add(p as string ?? "");
                }
                changed?.Invoke();
            });
        }

        /// <summary>Log in (or make an account) with Kakao / Google. progress gets the line to show while waiting.</summary>
        public static OauthFlow LoginOauth(string provider, Action<string> progress, Action<ApiResult> done)
        {
            var flow = new OauthFlow();
            Api.Get("/meta", meta =>
            {
                if (!meta.ok) { done(meta); return; }
                string blocked = MaintenanceBlock(MiniJson.Obj(meta.data, "maintenance"));
                if (blocked != null) { done(new ApiResult { code = "MAINTENANCE", status = 503, message = blocked }); return; }
                string serverData = MiniJson.Str(meta.data, "data_version");
                if (!string.IsNullOrEmpty(serverData) && serverData != ApiClient.DataVersion)
                {
                    done(new ApiResult { code = "DATA_OUTDATED", status = 426, message = L.T("게임 데이터가 서버와 다릅니다. 게임을 업데이트해 주세요.") });
                    return;
                }
                RunOauth(provider, "login", flow, progress, r =>
                {
                    if (!r.ok) { done(r); return; }
                    Api.SetTokens(MiniJson.Str(r.data, "access_token"), MiniJson.Str(r.data, "refresh_token"));
                    Current = new OnlineSession { AccountId = MiniJson.Str(MiniJson.Obj(r.data, "account"), "id"), LoginId = ProviderName(provider) };
                    Current.Providers.Add(provider);
                    LevelRewardClient.Reset();
                    DailyQuestClient.Reset();
                    Current.AfterLogin(done);
                });
            }, auth: false);
            return flow;
        }

        /// <summary>Attach Kakao / Google to the logged-in account so it can log in with it next time.</summary>
        public OauthFlow LinkOauth(string provider, Action<string> progress, Action<ApiResult> done)
        {
            var flow = new OauthFlow();
            RunOauth(provider, "link", flow, progress, r =>
            {
                if (r.ok && !Providers.Contains(provider)) Providers.Add(provider);
                done(r);
            });
            return flow;
        }

        static void RunOauth(string provider, string mode, OauthFlow flow, Action<string> progress, Action<ApiResult> done)
        {
            var body = new Dictionary<string, object> { ["provider"] = provider, ["mode"] = mode };
            Api.Post("/auth/oauth/start", body, r =>
            {
                if (!r.ok) { done(r); return; }
                if (flow.Cancelled) { done(Cancelled()); return; }
                string url = MiniJson.Str(r.data, "authorize_url");
                string state = MiniJson.Str(r.data, "state"), secret = MiniJson.Str(r.data, "poll_secret");
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(secret))
                {
                    done(new ApiResult { code = "OAUTH_START", message = L.T("로그인을 시작하지 못했습니다.") });
                    return;
                }
                Application.OpenURL(url);
                progress?.Invoke(L.F("브라우저에서 {0} 로그인을 마치면 자동으로 이어집니다.", ProviderName(provider)));
                Api.StartCoroutine(PollOauth(state, secret, flow, done));
            }, auth: mode == "link");
        }

        static ApiResult Cancelled() => new ApiResult { code = "OAUTH_CANCELLED", message = L.T("로그인을 취소했습니다.") };

        static IEnumerator PollOauth(string state, string secret, OauthFlow flow, Action<ApiResult> done)
        {
            float end = Time.realtimeSinceStartup + 600f;
            var body = new Dictionary<string, object> { ["state"] = state, ["poll_secret"] = secret, ["device"] = DeviceIdentity.Body() };
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);
                if (flow.Cancelled) { done(Cancelled()); yield break; }
                if (Time.realtimeSinceStartup > end) { done(OauthError("expired")); yield break; }
                ApiResult res = null;
                Api.Post("/auth/oauth/poll", body, r => res = r, auth: false);
                while (res == null) yield return null;
                if (flow.Cancelled) { done(Cancelled()); yield break; }
                // A dropped connection or a busy server is not the end: keep waiting for the browser step.
                if (!res.ok && (res.code == "NETWORK" || res.code == "RATE_LIMITED" || res.status >= 500)) continue;
                if (!res.ok) { done(res); yield break; }
                string status = MiniJson.Str(res.data, "status");
                if (status == "pending") continue;
                if (status == "error") { done(OauthError(MiniJson.Str(res.data, "code"))); yield break; }
                done(res);
                yield break;
            }
        }

        static ApiResult OauthError(string code)
        {
            string msg;
            switch (code)
            {
                case "cancelled": msg = L.T("로그인을 취소했습니다."); break;
                case "expired": msg = L.T("로그인 시간이 지났습니다. 다시 시도해 주세요."); break;
                default: msg = L.T("로그인 확인에 실패했습니다. 다시 시도해 주세요."); break;
            }
            return new ApiResult { code = "OAUTH_" + (code ?? "error").ToUpperInvariant(), message = msg };
        }
    }
}
