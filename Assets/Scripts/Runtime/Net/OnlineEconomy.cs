using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] Phase 3 (Docs/server/phase3_api.md): while an online character plays, every gold / item / XP change is
    /// decided by the server. The game reports what happened (a kill, a gather, a shop click) and applies the
    /// answer's <c>delta</c>, which carries absolute values (new gold, new stack counts, worn slots, level / XP), so
    /// whatever the local code did meanwhile is overwritten with the server's truth.
    /// Call sites check <see cref="On"/> and skip their offline grant.
    /// </summary>
    public static partial class OnlineEconomy
    {
        public static bool On => OnlineSession.Playing;
        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        // ---------------- plumbing ----------------

        /// <summary>Resends after a lost answer: 1s, 2s, 4s (same request_id and body, so the server replays the stored result).</summary>
        static readonly float[] RetryDelays = { 1f, 2f, 4f };

        /// <summary>POST with a fresh request_id; a network failure is resent up to 3 times with the same id (no double grant).</summary>
        static void Post(string path, Dictionary<string, object> body, Action<ApiResult> done, bool quiet = false)
        {
            if (!On) { done?.Invoke(ApiResult.Network(L.T("오프라인"))); return; }
            body["request_id"] = ApiClient.NewRequestId();
            Send(Char + path, body, 0, done, quiet);
        }

        static void Send(string full, Dictionary<string, object> body, int retry, Action<ApiResult> done, bool quiet)
        {
            Api.Post(full, body, r =>
            {
                if (r.code == "NETWORK")
                {
                    if (retry < RetryDelays.Length)
                    {
                        Api.StartCoroutine(After(RetryDelays[retry], () => Send(full, body, retry + 1, done, quiet)));
                        return;
                    }
                    // The server may have processed it: the answer was lost, not necessarily the action.
                    r.message = L.T("결과를 확인하는 중 연결이 끊겼습니다. 다시 접속하면 반영됩니다.");
                    Finish(r, done, quiet);
                    return;
                }
                // Rate limit or maintenance with Retry-After: wait (at most 10 s) and resend the same body and request_id.
                if ((r.status == 429 || r.status == 503) && r.retryAfter > 0f && retry < RetryDelays.Length)
                {
                    Api.StartCoroutine(After(Mathf.Min(r.retryAfter, 10f), () => Send(full, body, retry + 1, done, quiet)));
                    return;
                }
                // [ANTI-ABUSE] The server has no fresh presence for this map: send it, then repeat with the same request_id.
                if (r.code == "PRESENCE_REQUIRED")
                {
                    PresenceClient.Ping(() => Api.Post(full, body, r2 => Finish(r2, done, quiet)));
                    return;
                }
                Finish(r, done, quiet);
            });
        }

        static void Finish(ApiResult r, Action<ApiResult> done, bool quiet)
        {
            if (r.ok) ApplyDelta(MiniJson.Obj(r.data, "delta"));
            else if (!quiet) GameEvents.RaiseToast(string.IsNullOrEmpty(r.message) ? L.T("서버 처리에 실패했습니다.") : r.message);
            done?.Invoke(r);
        }

        static IEnumerator After(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action();
        }

        /// <summary>Overwrites local gold, stacks, worn slots and level / XP with the server's values.</summary>
        static readonly Dictionary<string, int> stackRows = new Dictionary<string, int>();

        /// <summary>[SERVER 6] Forget the row counts (another character entered).</summary>
        public static void ClearStackRows() => stackRows.Clear();

        /// <summary>[TRADE] One stored server row (0 when unknown).</summary>
        public static int StackRow(string location, string key, string bind) =>
            stackRows.TryGetValue(location + "|" + key + "|" + (bind ?? "none"), out int n) ? n : 0;

        /// <summary>Stores one server row and returns the key's total at that location over every binding.</summary>
        public static int SetStackRow(string location, string key, string bind, int count)
        {
            stackRows[location + "|" + key + "|" + (bind ?? "none")] = Mathf.Max(0, count);
            string prefix = location + "|" + key + "|";
            int total = 0;
            foreach (var kv in stackRows) if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) total += kv.Value;
            return total;
        }

        public static void ApplyDelta(Dictionary<string, object> delta)
        {
            if (delta == null || Game.Session == null) return;
            var s = Game.Session;
            if (MiniJson.Has(delta, "gold")) s.Inventory.SetCount(ConsumableDatabase.Gold, MiniJson.Int(delta, "gold"));
            foreach (var o in MiniJson.Arr(delta, "stacks") ?? new List<object>())
            {
                string key = MiniJson.Str(o, "item_key");
                string location = MiniJson.Str(o, "location");
                // [SERVER 6] One key can be several rows (one per binding); the bag shows their sum.
                int total = SetStackRow(location, key, MiniJson.Str(o, "bind", "none"), MiniJson.Int(o, "count"));
                switch (location)
                {
                    case "bag": s.Inventory.SetCount(key, total); break;
                    case "storage": s.Storage.SetCount(key, total); break;
                }
            }
            foreach (var o in MiniJson.Arr(delta, "worn") ?? new List<object>())
                s.Equipment.SetSlotFromServer(MiniJson.Int(o, "slot", -1), MiniJson.Str(o, "item_key"));
            if (MiniJson.Has(delta, "level") || MiniJson.Has(delta, "xp"))
                s.Progression.SetFromServer(MiniJson.Int(delta, "level", s.Progression.Level), MiniJson.Int(delta, "xp", s.Progression.Xp));
        }

        static Dictionary<string, object> Body(params (string key, object value)[] items)
        {
            var d = new Dictionary<string, object>();
            foreach (var (key, value) in items) d[key] = value;
            return d;
        }

        // ---------------- kills and drops ----------------

        /// <summary>Dungeon context for kill reports (set by the dungeon flow; null in the field).</summary>
        public static string RunId;
        // A solo run whose end was not reported yet: leaving it mid-way closes it as failed (else the server keeps it
        // 'playing' for up to an hour and counts a field revive in that time as a dungeon revive).
        static bool soloRun, runReported;
        public static int RoomIndex;

        /// <summary>A monster died: the server grants XP and rolls its drops, which appear as claimable pickups.</summary>
        /// <summary>[PARTY 8] The field session this character hunts in (null = solo field).</summary>
        public static string FieldSessionId;

        /// <summary>[PARTY XP] The last shared-hunting kill's party size bonus from the server (1.15 / 1.3 / 1.5, 1 = none).</summary>
        public static float PartyXpBonus = 1f;

        public static void ReportKill(string monsterId, int hits, Vector2 at, Transform parent, long monsterRef = -1)
        {
            // The loaded map (in a dungeon Session.MapId is the village the run returns to).
            var body = Body(("map_id", Game.World != null ? Game.World.MapId : Game.Session.MapId), ("monster_id", monsterId), ("hits", Mathf.Clamp(hits, 0, 60)));
            if (RunId != null) { body["run_id"] = RunId; body["room_index"] = RoomIndex; }
            else if (FieldSessionId != null && monsterRef >= 0) { body["session_id"] = FieldSessionId; body["monster_ref"] = monsterRef; }
            Post("/kills", body, r =>
            {
                if (!r.ok) return;
                int xp = MiniJson.Int(r.data, "granted_xp");
                if (xp > 0) GameEvents.RaiseToast($"+{xp} EXP");
                var field = MiniJson.Obj(r.data, "field");
                // Field sessions report it inside "field", party dungeon runs at the top level.
                PartyXpBonus = Mathf.Max(1f, (float)(field != null ? MiniJson.Num(field, "party_bonus", 1) : MiniJson.Num(r.data, "party_bonus", 1)));
                if (field != null && MiniJson.Num(field, "xp_factor", 1) < 0.999) GameEvents.RaiseToast(L.T("<color=#8c96a8>레벨 차이로 경험치가 줄었습니다.</color>"));
                if (parent == null) return;
                foreach (var d in MiniJson.Arr(r.data, "drops") ?? new List<object>())
                {
                    var p = Pickup.Create(MiniJson.Str(d, "item_key"), MiniJson.Int(d, "count", 1), at, parent);
                    p.DropId = MiniJson.Str(d, "id");
                }
            }, quiet: true);
        }

        static readonly List<string> claimQueue = new List<string>();
        static bool claimRunning;

        /// <summary>[LOAD L7] Batch window: a quarter second rarely caught two pickups (392 kills, 467 claims), one second does.</summary>
        const float ClaimBatchSeconds = 1f;
        const int ClaimBatchEarly = 10;

        /// <summary>A server drop was picked up: claims go out in batches (up to 50 ids) every second, sooner once 10 are waiting.
        /// The pickup toast and sound play at once; only the bag count follows the server reply.</summary>
        public static void ClaimDrop(string dropId)
        {
            claimQueue.Add(dropId);
            if (!claimRunning) Api.StartCoroutine(FlushClaims());
        }

        static IEnumerator FlushClaims()
        {
            claimRunning = true;
            while (claimQueue.Count > 0)
            {
                for (float t = 0f; t < ClaimBatchSeconds && claimQueue.Count < ClaimBatchEarly; t += Time.unscaledDeltaTime) yield return null;
                var ids = claimQueue.Take(50).ToList();
                claimQueue.RemoveRange(0, ids.Count);
                if (ids.Count > 0 && On) Post("/drops/claim", Body(("drop_ids", ids)), null, quiet: true);
            }
            claimRunning = false;
        }

        // ---------------- chests ----------------

        public static void OpenChest(string chestId, Vector2 at, Transform parent, Action<bool> done)
        {
            Post("/chests/open", Body(("chest_id", chestId)), r =>
            {
                if (r.ok && parent != null)
                {
                    var g = MiniJson.Obj(r.data, "granted");
                    Pickup.Create(MiniJson.Str(g, "item_key"), MiniJson.Int(g, "count", 1), at, parent).ServerGranted = true;
                }
                done(r.ok || r.code == "CHEST_ALREADY_OPENED");
            });
        }

        // ---------------- quests ----------------

        /// <summary>Failed claims this session per quest (a quest the server keeps refusing is not resent forever).</summary>
        static readonly Dictionary<string, int> claimFailures = new Dictionary<string, int>();

        /// <summary>Quest reward claim. done(ok, bonus max health total). Already-claimed counts as done.</summary>
        public static void ClaimQuest(string questId, Action<bool, int> done, bool quiet = false)
        {
            OnlineSession.ClaimedQuests.Add(questId);
            Post($"/quests/{questId}/claim", new Dictionary<string, object>(), r =>
            {
                bool ok = r.ok || r.code == "QUEST_ALREADY_CLAIMED";
                // [AUDIT 2026-10-14] A failed claim is not "claimed": it is sent again on the next map entry instead of
                // waiting for the next login (the reward used to be silently skipped for the whole session).
                if (!ok) { OnlineSession.ClaimedQuests.Remove(questId); claimFailures[questId] = (claimFailures.TryGetValue(questId, out int n) ? n : 0) + 1; }
                done?.Invoke(ok, MiniJson.Int(r.data, "bonus_max_health", -1));
            }, quiet);
        }

        // ---------------- shop, items, storage, equipment ----------------

        public static void ShopBuy(string itemId, int count, Action<bool> done) =>
            Post("/shop/buy", Body(("item_id", itemId), ("count", count)), r => done?.Invoke(r.ok));

        public static void ShopSell(string itemKey, int count, Action<bool> done) =>
            Post("/shop/sell", Body(("item_key", itemKey), ("count", count)), r => done?.Invoke(r.ok));

        /// <summary>Consumes one usable item on the server (the effect itself is local; the server only counts).</summary>
        public static void UseItem(string itemId) => Post("/items/use", Body(("item_id", itemId)), null, quiet: true);

        public static void StorageMove(IEnumerable<(string key, bool toStorage, int count)> moves, Action<bool> done)
        {
            var list = moves.Select(m => (object)Body(("item_key", m.key), ("to", m.toStorage ? "storage" : "bag"), ("count", m.count))).ToList();
            if (list.Count == 0) { done?.Invoke(true); return; }
            // The server takes at most 60 moves per request.
            var steps = new List<Action<Action>>();
            for (int i = 0; i < list.Count; i += 60)
            {
                var chunk = list.GetRange(i, Math.Min(60, list.Count - i));
                bool last = i + 60 >= list.Count;
                steps.Add(next => Post("/storage/move", Body(("moves", chunk)), r => { if (last) done?.Invoke(r.ok); next(); }));
            }
            RunInOrder(steps, 0);
        }

        public static void Equip(string itemKey, int? ringSlot, Action<bool> done)
        {
            var body = Body(("item_key", itemKey));
            if (ringSlot.HasValue) body["slot"] = ringSlot.Value;
            Post("/equipment/equip", body, r => done?.Invoke(r.ok));
        }

        public static void Unequip(int slot, Action<bool> done) => Post("/equipment/unequip", Body(("slot", slot)), r => done?.Invoke(r.ok));

        static void RunInOrder(List<Action<Action>> steps, int i)
        {
            if (i < steps.Count) steps[i](() => RunInOrder(steps, i + 1));
        }

        /// <summary>Enhancement rolled on the server. done(result) with outcome / old_key / new_key, or null on failure.</summary>
        public static void Enhance(int? wornSlot, string bagKey, Action<Dictionary<string, object>> done)
        {
            var target = wornSlot.HasValue ? Body(("worn_slot", wornSlot.Value)) : Body(("bag_key", bagKey));
            Post("/enhance", Body(("target", target)), r => { done?.Invoke(r.ok ? r.data : null); AchievementClient.Check(); });
        }

        /// <summary>Gear promotion (next grade of the same slot, +level kept), paid with raid cores. done(data or null).</summary>
        public static void Promote(int? wornSlot, string bagKey, Action<Dictionary<string, object>> done)
        {
            var target = wornSlot.HasValue ? Body(("worn_slot", wornSlot.Value)) : Body(("bag_key", bagKey));
            Post("/promote", Body(("target", target)), r => done?.Invoke(r.ok ? r.data : null));
        }

        // ---------------- solo dungeons ----------------

        /// <summary>Solo run (optionally with AI companions, phase 4). done(run object or null).</summary>
        public static void EnterDungeon(string dungeonId, int difficulty, int aiCount, Action<Dictionary<string, object>> done)
        {
            Post("/dungeon-runs", Body(("dungeon_id", dungeonId), ("difficulty", difficulty), ("ai_count", Mathf.Clamp(aiCount, 0, 3))), r =>
            {
                var run = r.ok ? MiniJson.Obj(r.data, "run") : null;
                if (run != null) { RunId = MiniJson.Str(run, "id"); RoomIndex = 0; soloRun = true; runReported = false; }
                done?.Invoke(run);
            });
        }

        /// <summary>[PARTY] A party run's own dungeon_runs id (from begin / heartbeat).</summary>
        public static void SetRun(string runId)
        {
            RunId = runId;
            RoomIndex = 0;
            soloRun = false;
        }

        /// <summary>Longest wait for a party result (other members' reports, phase4_api §7.4).</summary>
        public const float PartyResultWaitSeconds = 90f;

        /// <summary>Reports the run's end. done(result data: result / rank / granted_xp ...), or null.</summary>
        public static void FinishDungeon(bool cleared, float elapsedSeconds, int hitsTaken, int maxCombo, int revivesUsed, Action<Dictionary<string, object>> done)
        {
            if (RunId == null) { done?.Invoke(null); return; }
            string run = RunId;
            runReported = true;
            var stats = Body(("elapsed_ms", Mathf.RoundToInt(elapsedSeconds * 1000f)), ("hits_taken", Mathf.Clamp(hitsTaken, 0, 999)),
                ("max_combo", Mathf.Clamp(maxCombo, 0, 9999)), ("revives_used", Mathf.Clamp(revivesUsed, 0, 9)));
            Post($"/dungeon-runs/{run}/result", Body(("outcome", cleared ? "cleared" : "failed"), ("stats", stats)), r =>
            {
                var data = r.ok ? r.data : null;
                // A party run waits for the other reports: settle again until it is decided.
                if (MiniJson.Str(data, "result") == "pending") Api.StartCoroutine(Settle(run, data, done));
                else done?.Invoke(data);
            });
        }

        static IEnumerator Settle(string run, Dictionary<string, object> first, Action<Dictionary<string, object>> done)
        {
            var data = first;
            float waited = 0f;
            while (MiniJson.Str(data, "result") == "pending" && waited < PartyResultWaitSeconds)
            {
                float after = Mathf.Clamp(MiniJson.Int(data, "settle_after_ms", 2000) / 1000f, 0.5f, 10f);
                yield return new WaitForSecondsRealtime(after);
                waited += after;
                bool answered = false;
                Post($"/dungeon-runs/{run}/settle", new Dictionary<string, object>(), r => { if (r.ok) data = r.data; answered = true; }, quiet: true);
                while (!answered) yield return null;
            }
            done?.Invoke(data);
        }

        /// <summary>[RAID] Unlock, reward and key state of every raid (phase4_api §10.5). done(raids list or null).</summary>
        public static void LoadRaids(Action<List<object>> done) =>
            Api.Get(Char + "/raids", r => done?.Invoke(r.ok ? MiniJson.Arr(r.data, "raids") : null));

        /// <summary>Picks one reward card. done(own card, all four revealed) or (null, null).</summary>
        public static void PickCard(int index, Action<RewardCard?, List<RewardCard>> done)
        {
            if (RunId == null) { done?.Invoke(null, null); return; }
            Post($"/dungeon-runs/{RunId}/cards/pick", Body(("index", index)), r =>
            {
                if (!r.ok) { done?.Invoke(null, null); return; }
                var own = MiniJson.Obj(r.data, "card");
                var all = new List<RewardCard>();
                var list = MiniJson.Arr(r.data, "cards");
                if (list != null) foreach (var c in list) all.Add(new RewardCard(MiniJson.Str(c, "item_key"), MiniJson.Int(c, "count")));
                done?.Invoke(new RewardCard(MiniJson.Str(own, "item_key"), MiniJson.Int(own, "count")), all);
            });
        }

        public static void LeaveDungeon()
        {
            if (RunId != null && soloRun && !runReported)
            {
                var stats = Body(("elapsed_ms", 0), ("hits_taken", 0), ("max_combo", 0), ("revives_used", 0));
                Post($"/dungeon-runs/{RunId}/result", Body(("outcome", "failed"), ("stats", stats)), null, quiet: true);
            }
            RunId = null;
            flipRequestIds.Clear();
            soloRun = runReported = false;
        }

        // ---------------- entering the world ----------------

        static bool hooked;

        /// <summary>
        /// After an online character enters: re-sends claims for quests finished locally whose claim answer was
        /// lost (phase3_api §5, the server pays each quest once), and from now on marks the nodes that are still
        /// regrowing on the server whenever a map is entered.
        /// </summary>
        public static void OnEnteredWorld()
        {
            if (!hooked) { GameEvents.MapEntered += OnMapEntered; hooked = true; }
            if (!On) return;
            foreach (var q in Game.Quest.WithStatus(QuestStatus.Completed))
                if (!OnlineSession.ClaimedQuests.Contains(q.id))
                    ClaimQuest(q.id, null, quiet: true);
            OnMapEntered(Game.Session.MapId); // also flips cards left unpicked
            AchievementClient.OnEnteredWorld();
            _ = Game.Cosmetics?.RefreshAsync(); // 별조각 외형 보유 내역
        }

        /// <summary>
        /// [SERVER 7] A held dungeon result an operator released after this character left: the four cards
        /// are face down and equal, so one is flipped for the player and the reward is shown.
        /// </summary>
        static void ClaimUnpickedCards()
        {
            Api.Get(Char + "/dungeons", r =>
            {
                if (!r.ok) return;
                foreach (var o in MiniJson.Arr(r.data, "unpicked_runs") ?? new List<object>())
                {
                    string run = MiniJson.Str(o, "run_id");
                    if (string.IsNullOrEmpty(run)) continue;
                    // [RAID] take-all runs: every card still face down is taken, one request each.
                    if (MiniJson.Str(o, "card_mode", "pick_one") == "take_all") { TakeRemainingRaidCards(run, 0, MiniJson.Int(o, "remaining", DungeonRewards.CardCount), new List<RewardCard>()); continue; }
                    Post($"/dungeon-runs/{run}/cards/pick", Body(("index", UnityEngine.Random.Range(0, 4))), res =>
                    {
                        var card = MiniJson.Obj(res.data, "card");
                        if (res.ok && card != null)
                            GameEvents.RaiseToast(L.F("확정된 던전 보상: {0}", new RewardCard(MiniJson.Str(card, "item_key"), MiniJson.Int(card, "count")).Label));
                    }, quiet: true);
                }
            });
        }

        static float nextCardCheck;

        static void OnMapEntered(string mapId)
        {
            if (!On || (Game.Dungeon != null && Game.Dungeon.InRun)) return;
            // Quests finished here whose claim failed earlier: send them again.
            if (Game.Quest != null)
                foreach (var q in Game.Quest.WithStatus(QuestStatus.Completed))
                    if (!OnlineSession.ClaimedQuests.Contains(q.id) && (!claimFailures.TryGetValue(q.id, out int fails) || fails < 3)) ClaimQuest(q.id, null, quiet: true);
            // Back from a party dungeon the server may have settled this character's clear later (left right after the
            // clear, dropped before the result window): flip the waiting card here instead of only at the next login.
            if (UnityEngine.Time.unscaledTime >= nextCardCheck)
            {
                nextCardCheck = UnityEngine.Time.unscaledTime + 60f;
                ClaimUnpickedCards();
            }
        }
    }
}
