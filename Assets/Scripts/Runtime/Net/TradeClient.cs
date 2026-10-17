using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TRADE] 1:1 교환 through the server (server/src/domains/trade). Polls GET trades/current every second while
    /// a trade is open or the window shows, every 3 s otherwise while online, so an incoming request is noticed.
    /// The confirmer gets the gold / bag delta in its answer; the other side applies the same swap from the closed
    /// trade (once per trade id). Every action answers with the trade view, which replaces <see cref="Current"/>.
    /// </summary>
    public sealed class TradeClient : MonoBehaviour
    {
        public const float OpenPoll = 1f, IdlePoll = 3f;

        public sealed class Item { public string uuid, key; public int count; }

        public sealed class Side
        {
            public string name;
            public long gold;
            public readonly List<Item> items = new List<Item>();
            public bool locked, confirmed;
        }

        public sealed class Trade
        {
            public string id, state, closeReason, role;
            public int version;
            public Side me, other;
            public bool Pending => state == "pending";
            public bool Active => state == "active";
            public bool IncomingRequest => Pending && role == "target";
        }

        public static TradeClient Instance { get; private set; }
        public static event Action Changed;

        /// <summary>The open trade (pending or active), or null.</summary>
        public static Trade Current { get; private set; }
        /// <summary>True while the trade window shows (faster polling).</summary>
        public static bool WindowOpen;
        public static bool Busy { get; private set; }

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter + "/trades";

        float nextPoll;
        bool polling;
        string polledFor;
        readonly HashSet<string> seenOpen = new HashSet<string>(), settled = new HashSet<string>(), announced = new HashSet<string>();

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("TradeClient");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TradeClient>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        void Update()
        {
            if (!OnlineSession.Playing || Api == null)
            {
                if (Current != null) { Current = null; Changed?.Invoke(); }
                polledFor = null;
                return;
            }
            string who = OnlineSession.Current.ActiveCharacter;
            if (who != polledFor) { polledFor = who; Current = null; seenOpen.Clear(); nextPoll = 0f; } // another character entered
            if (polling || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + (WindowOpen || Current != null ? OpenPoll : IdlePoll);
            polling = true;
            Api.Get(Char + "/current", r =>
            {
                polling = false;
                if (!r.ok) return;
                var prev = Current;
                Current = Parse(MiniJson.Obj(r.data, "trade"));
                if (Current != null) seenOpen.Add(Current.id);
                var closed = Parse(MiniJson.Obj(r.data, "last_closed"));
                if (closed != null && Current == null) Closed(closed);
                if (Current != null && Current.IncomingRequest && announced.Add(Current.id)) Incoming(Current);
                if (prev != null || Current != null) Changed?.Invoke();
            });
        }

        static Trade Parse(Dictionary<string, object> o)
        {
            if (o == null) return null;
            return new Trade
            {
                id = MiniJson.Str(o, "id"),
                state = MiniJson.Str(o, "state"),
                closeReason = MiniJson.Str(o, "close_reason"),
                role = MiniJson.Str(o, "role"),
                version = MiniJson.Int(o, "version"),
                me = ParseSide(MiniJson.Obj(o, "me")),
                other = ParseSide(MiniJson.Obj(o, "other")),
            };
        }

        static Side ParseSide(Dictionary<string, object> o)
        {
            var s = new Side();
            if (o == null) return s;
            s.name = MiniJson.Str(o, "name", "");
            s.gold = (long)MiniJson.Num(o, "gold");
            s.locked = o.TryGetValue("locked", out var l) && l is bool lb && lb;
            s.confirmed = o.TryGetValue("confirmed", out var c) && c is bool cb && cb;
            foreach (var i in MiniJson.Arr(o, "items") ?? new List<object>())
                s.items.Add(new Item { uuid = MiniJson.Str(i, "item_uuid"), key = MiniJson.Str(i, "item_key"), count = MiniJson.Int(i, "count") });
            return s;
        }

        void Incoming(Trade t)
        {
            Game.Audio?.PlaySfx("select");
            // Open the window right away when nothing else is going on; inside a dungeon or another window, a toast.
            bool calm = Game.State != null && Game.State.Current == GameState.Playing && (Game.Dungeon == null || !Game.Dungeon.InRun);
            if (calm && TradeScreen.Instance != null) Game.Flow.OpenWindow(TradeScreen.Instance);
            else GameEvents.RaiseToast(L.F("{0}님이 교환을 신청했습니다. 채팅에 /교환 을 입력하면 창이 열립니다.", t.other.name));
        }

        /// <summary>A trade this PC saw open has closed: say how, and on completion apply the swap once.</summary>
        void Closed(Trade t)
        {
            if (!seenOpen.Remove(t.id) || !settled.Add(t.id)) return;
            switch (t.state)
            {
                case "completed":
                    ApplySwap(t);
                    GameEvents.RaiseToast(L.F("{0}님과 교환을 마쳤습니다.", t.other.name));
                    Game.Audio?.PlaySfx("confirm");
                    break;
                case "declined": GameEvents.RaiseToast(L.F("{0}님이 교환을 거절했습니다.", t.other.name)); break;
                case "expired": GameEvents.RaiseToast(L.T("교환 요청 시간이 지났습니다.")); break;
                case "failed": GameEvents.RaiseToast(L.T("교환할 물건이 바뀌어 교환이 취소되었습니다.")); break;
                default: GameEvents.RaiseToast(L.T("교환이 취소되었습니다.")); break;
            }
        }

        /// <summary>The side that did not send the last confirm: gold and bag rows from the closed trade's offers.</summary>
        static void ApplySwap(Trade t)
        {
            var s = Game.Session;
            if (s == null) return;
            var delta = new Dictionary<string, object>();
            long gold = s.Inventory.Count(ConsumableDatabase.Gold) - t.me.gold + t.other.gold;
            delta["gold"] = (double)Math.Max(0, gold);
            var change = new Dictionary<string, int>();
            foreach (var i in t.me.items) change[i.key] = (change.TryGetValue(i.key, out int a) ? a : 0) - i.count;
            foreach (var i in t.other.items) change[i.key] = (change.TryGetValue(i.key, out int b) ? b : 0) + i.count;
            var stacks = new List<object>();
            foreach (var kv in change)
            {
                if (kv.Value == 0) continue;
                int row = Mathf.Max(0, OnlineEconomy.StackRow("bag", kv.Key, "none") + kv.Value);
                stacks.Add(new Dictionary<string, object> { ["item_key"] = kv.Key, ["location"] = "bag", ["bind"] = "none", ["count"] = (double)row });
            }
            delta["stacks"] = stacks;
            OnlineEconomy.ApplyDelta(delta);
        }

        // ---------------- actions ----------------

        static void Act(string method, string path, Dictionary<string, object> body, Action<bool> done = null)
        {
            if (!OnlineSession.Playing || Api == null) { GameEvents.RaiseToast(L.T("온라인 캐릭터로 접속해야 합니다.")); done?.Invoke(false); return; }
            if (Busy) { done?.Invoke(false); return; }
            Busy = true;
            body = body ?? new Dictionary<string, object>();
            Action<ApiResult> answer = r =>
            {
                Busy = false;
                if (!r.ok)
                {
                    GameEvents.RaiseToast(Explain(r));
                    Game.Audio?.PlaySfx("cancel");
                    if (Instance != null) Instance.nextPoll = 0f; // re-read the state the server has
                    done?.Invoke(false);
                    return;
                }
                var t = Parse(MiniJson.Obj(r.data, "trade"));
                var delta = MiniJson.Obj(r.data, "delta");
                if (t != null && Instance != null)
                {
                    if (t.Pending || t.Active) { Current = t; Instance.seenOpen.Add(t.id); }
                    else
                    {
                        Current = null;
                        if (delta != null && Instance.settled.Add(t.id))
                        {
                            Instance.seenOpen.Remove(t.id);
                            OnlineEconomy.ApplyDelta(delta); // this PC confirmed last: the server's own numbers
                            GameEvents.RaiseToast(L.F("{0}님과 교환을 마쳤습니다.", t.other.name));
                            Game.Audio?.PlaySfx("confirm");
                        }
                        else Instance.Closed(t);
                    }
                }
                Changed?.Invoke();
                done?.Invoke(true);
            };
            if (method == "PUT") { body["request_id"] = ApiClient.NewRequestId(); Api.Put(Char + path, body, answer); }
            else
            {
                if (!body.ContainsKey("request_id")) body["request_id"] = ApiClient.NewRequestId();
                Api.PostIdempotent(Char + path, body, answer);
            }
        }

        public static void Request(string targetName, Action<bool> done = null)
        {
            if (!ContentUnlocks.Check(ContentUnlocks.Feature.Trade)) { done?.Invoke(false); return; } // [UNLOCK]
            targetName = (targetName ?? "").Trim();
            if (targetName.Length == 0) { GameEvents.RaiseToast(L.T("교환할 상대의 이름을 입력하세요.")); done?.Invoke(false); return; }
            Act("POST", "", new Dictionary<string, object> { ["target_name"] = targetName }, ok =>
            {
                if (ok) GameEvents.RaiseToast(L.F("{0}님에게 교환을 신청했습니다.", targetName));
                done?.Invoke(ok);
            });
        }

        public static void Accept() { if (Current != null) Act("POST", $"/{Current.id}/accept", null); }
        public static void Decline() { if (Current != null) Act("POST", $"/{Current.id}/decline", null); }
        public static void Cancel() { if (Current != null) Act("POST", $"/{Current.id}/cancel", null); }
        public static void Lock() { if (Current != null) Act("POST", $"/{Current.id}/lock", new Dictionary<string, object> { ["version"] = Current.version }); }
        public static void Confirm() { if (Current != null) Act("POST", $"/{Current.id}/confirm", new Dictionary<string, object> { ["version"] = Current.version }); }

        /// <summary>Replaces my whole offer (the server unlocks both sides when it changes).</summary>
        public static void Offer(long gold, List<Item> items)
        {
            if (Current == null) return;
            var list = new List<object>();
            foreach (var i in items) list.Add(new Dictionary<string, object> { ["item_uuid"] = i.uuid, ["count"] = i.count });
            Act("PUT", $"/{Current.id}/offer", new Dictionary<string, object> { ["version"] = Current.version, ["gold"] = gold, ["items"] = list });
        }

        /// <summary>Bag rows with their server ids (GET /characters/:uuid), for the offer picker.</summary>
        public static void LoadBag(Action<List<Item>> done)
        {
            if (!OnlineSession.Playing || Api == null) { done(new List<Item>()); return; }
            Api.Get("/characters/" + OnlineSession.Current.ActiveCharacter, r =>
            {
                var list = new List<Item>();
                if (r.ok)
                    foreach (var o in MiniJson.Arr(MiniJson.Obj(r.data, "character"), "items") ?? new List<object>())
                    {
                        if (MiniJson.Str(o, "location") != "bag") continue;
                        if (MiniJson.Str(o, "bind", "none") != "none") continue; // bound items cannot be traded: not listed at all
                        string key = MiniJson.Str(o, "item_key");
                        if (string.IsNullOrEmpty(key) || key == ConsumableDatabase.Gold) continue;
                        list.Add(new Item { uuid = MiniJson.Str(o, "id"), key = key, count = MiniJson.Int(o, "count") });
                    }
                done(list);
            });
        }

        static string Explain(ApiResult r)
        {
            switch (r.code)
            {
                case "NETWORK": return L.T("서버에 연결할 수 없습니다.");
                case "TRADE_VERSION_MISMATCH": return L.T("상대가 제안을 바꿨습니다. 다시 확인해 주세요.");
                default: return string.IsNullOrEmpty(r.message) ? L.F("요청이 실패했습니다. ({0})", r.status) : r.message;
            }
        }
    }
}
