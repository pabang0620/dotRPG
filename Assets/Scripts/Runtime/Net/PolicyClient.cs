using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [POLICY] Terms, privacy and the other policy pages, and the account's consent to the required ones.
    /// GET /policies lists the documents, GET /me/consents says whether this account still has to agree (a new
    /// version of a required document asks again), POST /me/consents records the agreement. The pages are served by
    /// the game server at {server}/policies/{ko|en}/{doc} and open in the browser.
    /// </summary>
    public static class PolicyClient
    {
        public const string Terms = "terms", Privacy = "privacy", Operation = "operation", Paid = "paid", Youth = "youth", AccountDeletion = "account-deletion", Probability = "probability";

        static ApiClient Api => ApiClient.Instance;

        /// <summary>The required documents and versions from the last /me/consents answer.</summary>
        static readonly List<(string id, string version)> required = new List<(string, string)>();

        /// <summary>True while the consent screen is open (a second CONSENT_REQUIRED does not open it again).</summary>
        public static bool Asking { get; private set; }

        /// <summary>The page of a policy document in the player's language (Korean pages for Korean, English otherwise).</summary>
        public static string Url(string doc) =>
            $"{Api.BaseUrl.TrimEnd('/')}/policies/{(L.Lang == L.Korean ? "ko" : "en")}/{doc}";

        public static void Open(string doc) => Application.OpenURL(Url(doc));

        /// <summary>The line every paid purchase confirmation shows (withdrawal of purchase rules).</summary>
        public static string RefundNotice =>
            L.T("<size=18>디지털 상품은 구매 후 7일 이내, 사용하지 않은 경우에만 청약철회할 수 있습니다. 사용·개봉한 상품은 청약철회가 제한됩니다.</size>");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookConsentRequired()
        {
            // A game call refused because a required document changed: ask again instead of failing quietly.
            ApiClient.ConsentRequired += () =>
            {
                if (OnlineSession.Current == null || Asking) return;
                EnsureConsent(r =>
                {
                    // A network error leaves things as they are: the next 428 asks again.
                    if (r.ok || r.code == "NETWORK") return;
                    bool playing = OnlineSession.Playing;
                    if (playing && Game.Flow != null) Game.Flow.ReturnToTitle();
                });
            };
        }

        /// <summary>
        /// After login: asks for consent when the server says so, then calls <paramref name="done"/> with ok, or with
        /// the error (network, or CONSENT_DECLINED after the player refused and was logged out). An older server
        /// without the endpoint (404) counts as agreed.
        /// </summary>
        public static void EnsureConsent(Action<ApiResult> done)
        {
            Api.Get("/me/consents", r =>
            {
                if (r.status == 404) { done(new ApiResult { ok = true, status = 200 }); return; }
                if (!r.ok) { done(r); return; }
                ReadRequired(r.data);
                if (!Needs(r.data)) { done(r); return; }
                Ask(done);
            });
        }

        static bool Needs(Dictionary<string, object> data) =>
            data != null && data.TryGetValue("needs", out var v) && v is bool b && b;

        static void ReadRequired(Dictionary<string, object> data)
        {
            required.Clear();
            foreach (var o in MiniJson.Arr(data, "required") ?? new List<object>())
            {
                string id = MiniJson.Str(o, "id"), version = MiniJson.Str(o, "version");
                if (!string.IsNullOrEmpty(id)) required.Add((id, version ?? ""));
            }
        }

        static void Ask(Action<ApiResult> done)
        {
            var screen = Game.UI != null ? Game.UI.Consent : null;
            if (screen == null) { done(new ApiResult { ok = true, status = 200 }); return; }
            Asking = true;
            screen.Open(
                agree: answer => Agree(r =>
                {
                    answer(r);
                    if (!r.ok) return; // stays open: the player can try again
                    Asking = false;
                    done(r);
                }),
                decline: () =>
                {
                    Asking = false;
                    bool playing = OnlineSession.Playing;
                    OnlineSession.Logout();
                    if (playing && Game.Flow != null) Game.Flow.ReturnToTitle();
                    string message = L.T("약관에 동의해야 온라인 플레이를 할 수 있습니다.");
                    GameEvents.RaiseToast(message);
                    done(new ApiResult { ok = false, code = "CONSENT_DECLINED", message = message });
                });
        }

        static void Agree(Action<ApiResult> done)
        {
            var list = new List<object>();
            foreach (var (id, version) in required)
                list.Add(new Dictionary<string, object> { ["id"] = id, ["version"] = version });
            Api.Post("/me/consents", new Dictionary<string, object> { ["agree"] = list }, r =>
            {
                if (r.ok) ReadRequired(r.data);
                done(r);
            });
        }
    }
}
