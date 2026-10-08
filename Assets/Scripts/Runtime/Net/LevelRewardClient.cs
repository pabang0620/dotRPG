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
        /// <summary>[CASH] 성장 패스 tier: its rewards (item key, count).</summary>
        public sealed class PassTier { public int level; public bool claimable, claimed; public readonly List<(string key, int count)> rewards = new List<(string, int)>(); }
        public static readonly List<PassTier> PassTiers = new List<PassTier>();
        public static bool PassOwned { get; private set; }
        public static int PassPrice { get; private set; } = 9000;

        public static readonly List<Tier> Tiers = new List<Tier>();
        public static bool Loaded { get; private set; }
        public static bool Busy { get; private set; }
        public static int AccountLevel { get; private set; }
        public static event Action Changed;

        public static bool AnyClaimable { get { foreach (var t in Tiers) if (t.claimable) return true; foreach (var t in PassTiers) if (t.claimable) return true; return false; } }

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
            if (d.TryGetValue("pass_owned", out var po) && po is bool pb) PassOwned = pb;
            PassPrice = MiniJson.Int(d, "pass_price", PassPrice);
            var passList = MiniJson.Arr(d, "pass_tiers");
            if (passList != null)
            {
                PassTiers.Clear();
                foreach (var o in passList)
                {
                    var t = new PassTier { level = MiniJson.Int(o, "level"), claimable = Flag(o, "claimable"), claimed = Flag(o, "claimed") };
                    foreach (var rw in MiniJson.Arr(o, "rewards") ?? new List<object>()) t.rewards.Add((MiniJson.Str(rw, "item_key"), MiniJson.Int(rw, "count", 1)));
                    PassTiers.Add(t);
                }
            }
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

        /// <summary>[CASH] Buys the growth pass with star shards (once per account).</summary>
        public static void BuyPass(Action<bool, string> done) => Post("/level-rewards/pass/buy", new Dictionary<string, object>(), r =>
            done(true, "성장 패스를 샀습니다! 지나간 단계의 패스 보상도 바로 받을 수 있습니다."), done);

        /// <summary>[CASH] Takes the pass reward of a level into the active character's bag.</summary>
        public static void ClaimPass(int level, Action<bool, string> done) =>
            Post("/characters/" + OnlineSession.Current.ActiveCharacter + "/level-rewards/pass/claim", new Dictionary<string, object> { ["level"] = level }, r =>
            {
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                Game.Flow.Autosave();
                done(true, $"Lv.{level} 패스 보상을 가방에 넣었습니다.");
            }, done);

        static void Post(string path, Dictionary<string, object> body, Action<ApiResult> ok, Action<bool, string> done)
        {
            if (!OnlineSession.Playing) { done(false, "온라인 캐릭터로 접속해야 합니다."); return; }
            if (Busy) { done(false, "이전 요청을 처리하는 중입니다."); return; }
            Busy = true;
            body["request_id"] = ApiClient.NewRequestId();
            Api.PostIdempotent(path, body, r =>
            {
                Busy = false;
                if (!r.ok)
                {
                    done(false, r.code switch
                    {
                        "NOT_ENOUGH_STARS" => $"별조각이 모자랍니다. (필요 {PassPrice:N0})",
                        "ALREADY_OWNED" => "이미 성장 패스를 가지고 있습니다.",
                        "PASS_REQUIRED" => "성장 패스가 있어야 받을 수 있습니다.",
                        "LEVEL_NOT_REACHED" => "아직 그 레벨에 도달한 캐릭터가 없습니다.",
                        "ALREADY_CLAIMED" => "이미 받은 보상입니다.",
                        "NETWORK" => "서버에 연결할 수 없습니다.",
                        _ => string.IsNullOrEmpty(r.message) ? "요청이 실패했습니다." : r.message,
                    });
                    Refresh();
                    return;
                }
                Read(r.data);
                _ = StarShopClient.RefreshAsync();
                ok(r);
                Refresh();
                Changed?.Invoke();
            });
        }

        public static void Claim(int level, Action<bool, string> done)
        {
            if (!OnlineSession.Playing) { done(false, "온라인 캐릭터로 접속해야 받을 수 있습니다."); return; }
            if (Busy) { done(false, "이전 요청을 처리하는 중입니다."); return; }
            Busy = true;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["level"] = level };
            Api.PostIdempotent("/level-rewards/claim", body, r =>
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
