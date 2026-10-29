using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// 별의 결정 -> 별조각 (server crystalConvert.ts, user decision 2026-10-29): Star Crystals become FREE Star Shards at the
    /// server's rate (CrystalsPerStar crystals = 1 shard, rounded down; the rest stays). No amount or count limit.
    /// </summary>
    public static partial class StarShopClient
    {
        /// <summary>Crystals per Star Shard (exchange-shop "convert.per_star"; 2 until the server says otherwise).</summary>
        public static int CrystalsPerStar { get; private set; } = 2;

        /// <summary>Called from LoadExchange with the exchange-shop reply.</summary>
        static void ReadConvert(Dictionary<string, object> d)
        {
            var c = MiniJson.Obj(d, "convert");
            if (c != null) CrystalsPerStar = Math.Max(1, MiniJson.Int(c, "per_star", CrystalsPerStar));
        }

        /// <summary>Converts <paramref name="crystals"/> Star Crystals (rounded down to whole shards) into free Star Shards.
        /// done(ok, message, shards received).</summary>
        public static void ConvertCrystals(long crystals, Action<bool, string, int> done)
        {
            if (!Available) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다."), 0); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["crystals"] = crystals };
            Api.PostIdempotent(Base + "/crystal-convert", body, r =>
            {
                if (!r.ok)
                {
                    done?.Invoke(false, r.code == "NOT_ENOUGH_CRYSTALS" ? L.T("별의 결정이 모자랍니다.") : ExchangeError(r.code, r.message, L.T("별조각으로 바꾸지 못했습니다.")), 0);
                    return;
                }
                int stars = MiniJson.Int(r.data, "stars");
                Crystals = (long)MiniJson.Num(r.data, "crystal_balance", Crystals);
                Balance = (long)MiniJson.Num(r.data, "balance", Balance);
                Changed?.Invoke();
                LoadExchange((_, __) => done?.Invoke(true, "", stars));
            });
        }
    }
}
