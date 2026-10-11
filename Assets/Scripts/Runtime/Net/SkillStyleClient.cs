using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [STYLE] Skill style 3 (Docs/PLAN_SKILL_STYLES.md): whether the account owns it and the one-time star purchase.
    /// Offline play has every style open; online, style 3 stays locked until the server says it is owned.
    /// </summary>
    public static class SkillStyleClient
    {
        public static int Price { get; private set; } = 1000;
        public static bool Busy { get; private set; }

        static ApiClient Api => ApiClient.Instance;

        /// <summary>Reads ownership (on entering an online character and when the skill window opens).</summary>
        public static void Refresh()
        {
            if (!OnlineSession.Playing) { Progression.Style3Owned = true; return; }
            Progression.Style3Owned = false;
            Api.Get("/skill-styles", r =>
            {
                if (!r.ok) return;
                Progression.Style3Owned = Flag(r.data, "style3_owned");
                Price = MiniJson.Int(r.data, "price", Price);
            });
        }

        /// <summary>Buys style 3 with star shards (once per account).</summary>
        public static void Buy(Action<bool, string> done)
        {
            if (!OnlineSession.Playing) { Progression.Style3Owned = true; done(true, "스타일 3을 열었습니다."); return; }
            if (Busy) { done(false, "이전 요청을 처리하는 중입니다."); return; }
            Busy = true;
            Api.PostIdempotent("/skill-styles/buy", new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() }, r =>
            {
                Busy = false;
                if (!r.ok)
                {
                    if (r.code == "ALREADY_OWNED") { Progression.Style3Owned = true; done(true, "이미 스타일 3을 가지고 있습니다."); return; }
                    done(false, r.code switch
                    {
                        "NOT_ENOUGH_STARS" => $"별조각이 모자랍니다. (필요 {Price:N0})",
                        "STAR_SPEND_CAP" => "오늘 별조각 사용 한도를 넘었습니다.",
                        "NETWORK" => "서버에 연결할 수 없습니다.",
                        _ => string.IsNullOrEmpty(r.message) ? "요청이 실패했습니다." : r.message,
                    });
                    return;
                }
                Progression.Style3Owned = true;
                _ = StarShopClient.RefreshAsync();
                done(true, "스타일 3을 열었습니다! 이 계정의 모든 캐릭터가 쓸 수 있습니다.");
            });
        }

        static bool Flag(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b && b;
    }
}
