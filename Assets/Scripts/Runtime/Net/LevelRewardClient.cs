using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [LEVEL 13] Level milestone rewards (Docs/server/phase13_level_rewards.md): free star shards once per account when
    /// any of its characters reaches the level. The server owns the table and the claims; offline play has none.
    /// </summary>
    public static class LevelRewardClient
    {
        public sealed class Tier { public int level, stars; public bool claimable, claimed; }

        public static readonly List<Tier> Tiers = new List<Tier>();
        public static bool Loaded { get; private set; }
        public static bool Busy { get; private set; }
        public static int AccountLevel { get; private set; }
        public static event Action Changed;

        public static bool AnyClaimable { get { foreach (var t in Tiers) if (t.claimable) return true; return false; } }

        static ApiClient Api => ApiClient.Instance;

        public static void Refresh()
        {
            if (!OnlineSession.Playing) return;
            Api.Get("/level-rewards", r =>
            {
                if (r.ok) { Read(r.data); Loaded = true; }
                Changed?.Invoke();
            });
        }

        static void Read(Dictionary<string, object> d)
        {
            if (d == null) return;
            AccountLevel = MiniJson.Int(d, "max_level", AccountLevel);
            var list = MiniJson.Arr(d, "tiers");
            if (list == null) return;
            Tiers.Clear();
            foreach (var o in list)
                Tiers.Add(new Tier
                {
                    level = MiniJson.Int(o, "level"), stars = MiniJson.Int(o, "stars"),
                    claimable = Flag(o, "claimable"), claimed = Flag(o, "claimed"),
                });
        }

        static bool Flag(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b && b;

        public static void Claim(int level, Action<bool, string> done)
        {
            if (!OnlineSession.Playing) { done(false, "온라인 캐릭터로 접속해야 받을 수 있습니다."); return; }
            if (Busy) { done(false, "이전 요청을 처리하는 중입니다."); return; }
            Busy = true;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["level"] = level };
            Api.Post("/level-rewards/claim", body, r =>
            {
                Busy = false;
                if (!r.ok)
                {
                    done(false, r.code == "LEVEL_NOT_REACHED" ? $"Lv.{level}에 도달한 캐릭터가 있어야 합니다."
                        : r.code == "ALREADY_CLAIMED" ? "이미 받은 보상입니다."
                        : r.code == "NETWORK" ? "서버에 연결할 수 없습니다." : string.IsNullOrEmpty(r.message) ? "받지 못했습니다." : r.message);
                    Refresh();
                    return;
                }
                Read(r.data);
                Changed?.Invoke();
                _ = StarShopClient.RefreshAsync(); // the wallet shows the new shards
                done(true, $"Lv.{level} 달성 보상: 별조각 {MiniJson.Int(r.data, "stars"):N0}개를 받았습니다.");
            });
        }
    }
}
