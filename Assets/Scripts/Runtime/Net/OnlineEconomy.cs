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
    public static class OnlineEconomy
    {
        public static bool On => OnlineSession.Playing;
        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        // ---------------- plumbing ----------------

        /// <summary>POST with a fresh request_id; a network failure is retried once with the same id (no double grant).</summary>
        static void Post(string path, Dictionary<string, object> body, Action<ApiResult> done, bool quiet = false)
        {
            if (!On) { done?.Invoke(ApiResult.Network("오프라인")); return; }
            body["request_id"] = ApiClient.NewRequestId();
            string full = Char + path;
            Api.Post(full, body, r =>
            {
                if (r.code == "NETWORK")
                {
                    Api.StartCoroutine(After(1f, () => Api.Post(full, body, r2 => Finish(r2, done, quiet))));
                    return;
                }
                Finish(r, done, quiet);
            });
        }

        static void Finish(ApiResult r, Action<ApiResult> done, bool quiet)
        {
            if (r.ok) ApplyDelta(MiniJson.Obj(r.data, "delta"));
            else if (!quiet) GameEvents.RaiseToast(string.IsNullOrEmpty(r.message) ? "서버 처리에 실패했습니다." : r.message);
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
        public static int RoomIndex;

        /// <summary>A monster died: the server grants XP and rolls its drops, which appear as claimable pickups.</summary>
        /// <summary>[PARTY 8] The field session this character hunts in (null = solo field).</summary>
        public static string FieldSessionId;

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
                if (field != null && MiniJson.Num(field, "xp_factor", 1) < 0.999) GameEvents.RaiseToast("<color=#8c96a8>레벨 차이로 경험치가 줄었다.</color>");
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

        /// <summary>A server drop was picked up: claims go out in batches (up to 50 ids) every quarter second.</summary>
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
                yield return new WaitForSecondsRealtime(0.25f);
                var ids = claimQueue.Take(50).ToList();
                claimQueue.RemoveRange(0, ids.Count);
                if (ids.Count > 0 && On) Post("/drops/claim", Body(("drop_ids", ids)), null, quiet: true);
            }
            claimRunning = false;
        }

        // ---------------- gathering, chests, deliveries ----------------

        public static string NodeId(Vector2 foot) => $"{Game.Session.MapId}:{Mathf.FloorToInt(foot.x)}:{Mathf.FloorToInt(foot.y)}";

        /// <summary>A tree / rock / crop gave its yield: the server grants it; the pickups shown are only visuals.</summary>
        public static void Gather(string nodeId, Vector2 at, Transform parent)
        {
            Post("/gathers", Body(("map_id", Game.Session.MapId), ("node_id", nodeId)), r =>
            {
                if (!r.ok || parent == null) return;
                var g = MiniJson.Obj(r.data, "granted");
                int count = MiniJson.Int(g, "count");
                for (int i = 0; i < count; i++) Pickup.Create(MiniJson.Str(g, "item_key"), 1, at, parent).ServerGranted = true;
            });
        }

        /// <summary>Nodes still regrowing on this map (the server counts real time); done(ids) is called once.</summary>
        public static void LoadCoolingNodes(string mapId, Action<HashSet<string>> done)
        {
            Api.Get($"{Char}/maps/{mapId}/nodes", r =>
            {
                var set = new HashSet<string>();
                foreach (var n in MiniJson.Arr(r.data, "cooling") ?? new List<object>()) set.Add(MiniJson.Str(n, "node_id"));
                done(set);
            });
        }

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

        /// <summary>Workshop delivery: the server moves what is missing from the bag. done(delivered wood, stone, complete).</summary>
        public static void Deliver(Action<int, int, bool> done)
        {
            Post("/deliveries", Body(("site_id", "workshop")), r =>
            {
                if (!r.ok) { done(-1, -1, false); return; }
                int wood = 0, stone = 0;
                foreach (var it in MiniJson.Arr(r.data, "items") ?? new List<object>())
                {
                    if (MiniJson.Str(it, "item_key") == ItemIds.Wood) wood = MiniJson.Int(it, "delivered");
                    if (MiniJson.Str(it, "item_key") == ItemIds.Stone) stone = MiniJson.Int(it, "delivered");
                }
                bool complete = r.data != null && r.data.TryGetValue("complete", out var c) && c is bool b && b;
                done(wood, stone, complete);
            });
        }

        // ---------------- quests ----------------

        /// <summary>Quest reward claim. done(ok, bonus max health total). Already-claimed counts as done.</summary>
        public static void ClaimQuest(string questId, Action<bool, int> done, bool quiet = false)
        {
            OnlineSession.ClaimedQuests.Add(questId);
            Post($"/quests/{questId}/claim", new Dictionary<string, object>(), r =>
            {
                bool ok = r.ok || r.code == "QUEST_ALREADY_CLAIMED";
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

        /// <summary>Worn keys right now (for <see cref="SyncWorn"/>).</summary>
        public static string[] WornSnapshot()
        {
            var eq = Game.Session.Equipment;
            var keys = new string[Equipment.SlotCount];
            for (int i = 0; i < keys.Length; i++) keys[i] = eq[(EquipSlot)i];
            return keys;
        }

        /// <summary>
        /// Sends the slot changes the local equip code just made (compared with <paramref name="before"/>) as
        /// equip / unequip requests, one after another (the server checks them in order). Each answer's delta
        /// puts back whatever the server refused.
        /// </summary>
        public static void SyncWorn(string[] before)
        {
            if (!On || before == null) return;
            var now = WornSnapshot();
            var steps = new List<Action<Action>>();
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] == before[i]) continue;
                int slot = i;
                string key = now[i];
                bool ring = slot == (int)EquipSlot.Ring1 || slot == (int)EquipSlot.Ring2;
                if (key == null) steps.Add(next => Unequip(slot, _ => next()));
                else steps.Add(next => Equip(key, ring ? slot : (int?)null, _ => next()));
            }
            RunInOrder(steps, 0);
        }

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

        // ---------------- solo dungeons ----------------

        /// <summary>Solo run (optionally with AI companions, phase 4). done(run object or null).</summary>
        public static void EnterDungeon(string dungeonId, int difficulty, int aiCount, Action<Dictionary<string, object>> done)
        {
            Post("/dungeon-runs", Body(("dungeon_id", dungeonId), ("difficulty", difficulty), ("ai_count", Mathf.Clamp(aiCount, 0, 3))), r =>
            {
                var run = r.ok ? MiniJson.Obj(r.data, "run") : null;
                if (run != null) { RunId = MiniJson.Str(run, "id"); RoomIndex = 0; }
                done?.Invoke(run);
            });
        }

        /// <summary>[PARTY] A party run's own dungeon_runs id (from begin / heartbeat).</summary>
        public static void SetRun(string runId)
        {
            RunId = runId;
            RoomIndex = 0;
        }

        /// <summary>Longest wait for a party result (other members' reports, phase4_api §7.4).</summary>
        public const float PartyResultWaitSeconds = 90f;

        /// <summary>Reports the run's end. done(result data: result / rank / granted_xp ...), or null.</summary>
        public static void FinishDungeon(bool cleared, float elapsedSeconds, int hitsTaken, int maxCombo, int revivesUsed, Action<Dictionary<string, object>> done)
        {
            if (RunId == null) { done?.Invoke(null); return; }
            string run = RunId;
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

        public static void LeaveDungeon() => RunId = null;

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
                    Post($"/dungeon-runs/{run}/cards/pick", Body(("index", UnityEngine.Random.Range(0, 4))), res =>
                    {
                        var card = MiniJson.Obj(res.data, "card");
                        if (res.ok && card != null)
                            GameEvents.RaiseToast($"확정된 던전 보상: {new RewardCard(MiniJson.Str(card, "item_key"), MiniJson.Int(card, "count")).Label}");
                    }, quiet: true);
                }
            });
        }

        static float nextCardCheck;

        static void OnMapEntered(string mapId)
        {
            if (!On || (Game.Dungeon != null && Game.Dungeon.InRun)) return;
            // Back from a party dungeon the server may have settled this character's clear later (left right after the
            // clear, dropped before the result window): flip the waiting card here instead of only at the next login.
            if (UnityEngine.Time.unscaledTime >= nextCardCheck)
            {
                nextCardCheck = UnityEngine.Time.unscaledTime + 60f;
                ClaimUnpickedCards();
            }
            LoadCoolingNodes(mapId, cooling =>
            {
                if (cooling.Count == 0 || Game.World == null || Game.World.MapId != mapId) return;
                foreach (var n in ResourceNode.Active.ToList()) if (n != null && cooling.Contains(n.NodeId)) n.StartRegrowing();
                foreach (var c in CropPlot.Active.ToList()) if (c != null && cooling.Contains(c.NodeId)) c.StartRegrowing();
            });
        }
    }
}
