using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CASH] 봉인된 상자, luck boxes and enhancement tickets (Docs/PLAN_CASH_BOX_PASS.md, server phase 14). The server
    /// rolls every box, keeps the 봉인 해제 gauge per account and changes the gear; this only sends and shows.
    /// Online only: offline play has no cash items.
    /// </summary>
    public static class CashClient
    {
        public sealed class Reward { public string itemKey, tier; public int count; public bool boosted; }
        public sealed class Rate { public string itemKey, tier; public int count; public float rate; }

        public static readonly List<Rate> Table = new List<Rate>(), BoostedTable = new List<Rate>();
        public static int PriceOne { get; private set; } = 100;
        public static int PriceEleven { get; private set; } = 1000;
        /// <summary>Opens done since the last boosted one (0..9); the 11th open is boosted.</summary>
        public static int Gauge { get; private set; }
        public static bool NextBoosted { get; private set; }
        /// <summary>The rate table version the player last saw (sent with each pull; the server refuses a changed table).</summary>
        public static string RatesVersion { get; private set; }
        public static bool Loaded { get; private set; }
        public static bool Busy { get; private set; }
        public static event Action Changed;

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        public static void Refresh()
        {
            if (!OnlineSession.Playing) return;
            Api.Get("/starshop/sealed", r =>
            {
                if (r.ok)
                {
                    PriceOne = MiniJson.Int(r.data, "price_one", PriceOne);
                    PriceEleven = MiniJson.Int(r.data, "price_eleven", PriceEleven);
                    RatesVersion = MiniJson.Str(r.data, "rates_version");
                    ReadBooster(MiniJson.Obj(r.data, "booster"));
                    ReadTable(MiniJson.Arr(r.data, "table"), Table);
                    ReadTable(MiniJson.Arr(r.data, "boosted_table"), BoostedTable);
                    Loaded = true;
                }
                Changed?.Invoke();
            });
        }

        static void ReadTable(List<object> list, List<Rate> into)
        {
            if (list == null) return;
            into.Clear();
            foreach (var o in list)
                into.Add(new Rate { itemKey = MiniJson.Str(o, "item_key"), count = MiniJson.Int(o, "count", 1), rate = (float)MiniJson.Num(o, "rate"), tier = MiniJson.Str(o, "tier") });
        }

        static void ReadBooster(Dictionary<string, object> b)
        {
            if (b == null) return;
            Gauge = MiniJson.Int(b, "gauge");
            NextBoosted = b.TryGetValue("next_boosted", out var v) && v is bool nb && nb;
        }

        static List<Reward> ReadResults(Dictionary<string, object> d)
        {
            var list = new List<Reward>();
            foreach (var o in MiniJson.Arr(d, "results") ?? new List<object>())
                list.Add(new Reward
                {
                    itemKey = MiniJson.Str(o, "item_key"), count = MiniJson.Int(o, "count", 1), tier = MiniJson.Str(o, "tier"),
                    boosted = o is Dictionary<string, object> od && od.TryGetValue("boosted", out var v) && v is bool bb && bb,
                });
            return list;
        }

        static void Send(string path, Dictionary<string, object> body, Action<ApiResult> done)
        {
            if (!OnlineSession.Playing) { done(new ApiResult { ok = false, code = "OFFLINE", message = "온라인으로 접속해야 쓸 수 있습니다." }); return; }
            if (Busy) { done(new ApiResult { ok = false, code = "BUSY", message = "이전 요청을 처리하는 중입니다." }); return; }
            Busy = true;
            body["request_id"] = ApiClient.NewRequestId();
            Api.Post(path, body, r => { Busy = false; done(r); });
        }

        static string Explain(ApiResult r) => r.code switch
        {
            "NOT_ENOUGH_STARS" => "별조각이 모자랍니다.",
            "ALREADY_HIGHER" => "이미 그 단계 이상인 장비입니다.",
            "RATES_CHANGED" => "확률표가 바뀌었습니다. 확률을 다시 확인한 뒤 열어 주세요.",
            "OFFLINE" or "BUSY" => r.message,
            "NETWORK" => "서버에 연결할 수 없습니다.",
            _ => string.IsNullOrEmpty(r.message) ? $"요청이 실패했습니다. ({r.status})" : r.message,
        };

        /// <summary>Opens 1 or 11 sealed boxes from the shop.</summary>
        public static void Pull(int count, Action<bool, string, List<Reward>> done)
        {
            var body = new Dictionary<string, object> { ["count"] = count };
            if (!string.IsNullOrEmpty(RatesVersion)) body["rates_version"] = RatesVersion;
            Send(Char + "/starshop/sealed/pull", body, r =>
            {
                if (!r.ok) { if (r.code == "RATES_CHANGED") Refresh(); done(false, Explain(r), null); return; }
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                ReadBooster(MiniJson.Obj(r.data, "booster"));
                _ = StarShopClient.RefreshAsync();
                Game.Flow.Autosave();
                Changed?.Invoke();
                done(true, null, ReadResults(r.data));
            });
        }

        /// <summary>Opens a luck box or a sealed box from the bag and toasts what came out.</summary>
        public static void OpenItem(string itemKey)
        {
            Send(Char + "/items/open", new Dictionary<string, object> { ["item_key"] = itemKey }, r =>
            {
                if (!r.ok) { GameEvents.RaiseToast(Explain(r)); Game.Audio.PlaySfx("cancel"); return; }
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                ReadBooster(MiniJson.Obj(r.data, "booster"));
                Game.Flow.Autosave();
                Changed?.Invoke();
                var got = ReadResults(r.data);
                bool big = false;
                var parts = new List<string>();
                foreach (var g in got)
                {
                    var item = ConsumableDatabase.Get(g.itemKey);
                    if (item != null && item.kind == ConsumableKind.EnhanceTicket) big = true;
                    parts.Add($"{DungeonDatabase.ItemName(g.itemKey)} x{g.count}{(g.boosted ? " (부스터)" : "")}");
                }
                Game.Audio.PlaySfx(big ? "rank_reveal" : "pickup");
                GameEvents.RaiseToast((big ? "<color=#ffd34a>대박!</color> " : "") + string.Join(", ", parts));
            });
        }

        /// <summary>Uses a +N enhancement ticket on a piece of gear (bag or worn).</summary>
        public static void UseTicket(string ticketKey, string gearKey, Action<bool, string> done)
        {
            Send(Char + "/enhance/ticket", new Dictionary<string, object> { ["ticket_key"] = ticketKey, ["gear_key"] = gearKey }, r =>
            {
                if (!r.ok) { done(false, Explain(r)); return; }
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                Game.Flow.Autosave();
                Changed?.Invoke();
                done(true, null);
            });
        }
    }
}
