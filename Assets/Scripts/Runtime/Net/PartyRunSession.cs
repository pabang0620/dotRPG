using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY] One party dungeon run from the server's point of view (phase4_api §6): gathering (members
    /// connect to the host PC and confirm with their entry token), playing (heartbeats every 5 s, host
    /// handover when the host disappears), the host's observation report at the end. The fight itself
    /// is <see cref="PartyNet"/>; rewards go through each member's own <see cref="OnlineEconomy"/> calls.
    /// Transport: development UDP (two windows on one PC) until Steam P2P is wired (PARTY_TRANSPORT=steam).
    /// </summary>
    public sealed class PartyRunSession : MonoBehaviour
    {
        const float PollSeconds = 1f, HeartbeatSeconds = 5f, HostLostSeconds = 3f;

        public static PartyRunSession Instance { get; private set; }
        public static bool Active => Instance != null;

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        string runId, state = "", dungeonId, myRunId, entryToken, myState = "";
        int difficulty, mySlot, epoch = 1, humans = 1, aiCount;
        string hostCharacterId, hostSteamId;
        /// <summary>[8] The combat transport the server chose for this run.</summary>
        readonly RoomTransportInfo transportInfo = new RoomTransportInfo { kind = "run" };
        byte[] hostKey;
        bool amHost, began, joinSent, claiming, reported;
        float pollTimer, heartbeatTimer, hostLostFor;
        /// <summary>Kills the host saw, per room (monster id -> count), for the host report.</summary>
        readonly Dictionary<int, Dictionary<string, int>> roomKills = new Dictionary<int, Dictionary<string, int>>();

        public string RunId => runId;
        public bool AmHost => amHost;
        public string State => state;

        /// <summary>
        /// A run object arrived (party poll, start answer, heartbeat). Starts the session for an active run
        /// I am in, and keeps its numbers current.
        /// </summary>
        public static void Observe(Dictionary<string, object> run, string key)
        {
            if (run == null) return;
            string id = MiniJson.Str(run, "id");
            string st = MiniJson.Str(run, "state");
            var me = MiniJson.Obj(run, "me");
            string mine = MiniJson.Str(me, "state");
            bool active = (st == "gathering" || st == "playing") && mine != "left" && mine != "done" && mine != null;
            if (Instance == null)
            {
                if (!active) return;
                var go = new GameObject("PartyRunSession");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<PartyRunSession>();
                Instance.runId = id;
                EnemyController.Killed += Instance.OnKilled;
                PartyNet.PromotedToHost += Instance.OnPromoted;
            }
            if (Instance.runId != id) return;
            if (!string.IsNullOrEmpty(key)) Instance.hostKey = PartyNet.FromBase64Url(key);
            Instance.Read(run);
        }

        void Read(Dictionary<string, object> run)
        {
            state = MiniJson.Str(run, "state", state);
            dungeonId = MiniJson.Str(run, "dungeon_id", dungeonId);
            difficulty = MiniJson.Int(run, "difficulty", difficulty);
            humans = MiniJson.Int(run, "humans", humans);
            aiCount = MiniJson.Int(run, "ai_count", aiCount);
            var host = MiniJson.Obj(run, "host");
            if (host != null)
            {
                hostCharacterId = MiniJson.Str(host, "character_id");
                hostSteamId = MiniJson.Str(host, "steam_id");
                epoch = MiniJson.Int(host, "epoch", epoch);
            }
            transportInfo.ReadMembers(MiniJson.Arr(run, "members"), hostCharacterId); // [8] seats and SteamIDs
            var transport = MiniJson.Obj(run, "transport");
            if (transport != null)
            {
                string current = MiniJson.Str(transport, "current", transportInfo.current);
                int tEpoch = MiniJson.Int(transport, "epoch", transportInfo.epoch);
                bool changed = PartyNet.Active && (current != transportInfo.current || tEpoch != transportInfo.epoch);
                transportInfo.current = current;
                transportInfo.epoch = tEpoch;
                if (changed) SwitchTransport();
            }
            var me = MiniJson.Obj(run, "me");
            if (me != null)
            {
                mySlot = MiniJson.Int(me, "slot", mySlot);
                myState = MiniJson.Str(me, "state", myState);
                entryToken = MiniJson.Str(me, "entry_token", entryToken);
                myRunId = MiniJson.Str(me, "run_id", myRunId);
            }
            amHost = hostCharacterId != null && hostCharacterId == OnlineSession.Current?.ActiveCharacter;
            EnsureNet();
            if (state == "playing" && !began && !string.IsNullOrEmpty(myRunId)) Begin();
            if (state == "ended" || state == "cancelled" || myState == "left") Finish(state == "cancelled" ? "출발이 취소되었다." : null);
        }

        /// <summary>The fight connection: the host listens, a member connects and says hello with its token.</summary>
        void EnsureNet()
        {
            if (PartyNet.Active || state != "gathering" && state != "playing") return;
            if (!amHost && string.IsNullOrEmpty(entryToken)) return;
            var t = BuildTransport();
            if (t == null) return;
            if (amHost) PartyNet.BeginHost(t, runId, mySlot, hostKey, Mathf.Max(1, humans));
            else PartyNet.BeginMember(t, runId, mySlot, OnlineSession.Current?.ActiveCharacter, entryToken);
        }

        /// <summary>[8] The server's transport for this run; if it cannot be built here, ask for the next one.</summary>
        ITransport BuildTransport()
        {
            transportInfo.roomId = runId;
            transportInfo.seat = mySlot;
            transportInfo.amHost = amHost;
            transportInfo.hostSteamId = hostSteamId;
            transportInfo.entryToken = entryToken;
            transportInfo.hostKey = hostKey;
            var t = CombatTransportFactory.Create(transportInfo);
            if (t == null) RequestNextTransport();
            return t;
        }

        bool switching;

        /// <summary>[8] The current transport failed (or cannot exist on this PC): the server picks the next one.</summary>
        void RequestNextTransport()
        {
            if (switching) return;
            switching = true;
            CombatTransportFactory.RequestSwitch(transportInfo, transportInfo.current, (current, tEpoch) =>
            {
                switching = false;
                if (string.IsNullOrEmpty(current)) { GameEvents.RaiseToast("파티원과 연결할 수 없습니다."); return; }
                transportInfo.current = current;
                transportInfo.epoch = tEpoch;
                SwitchTransport();
            });
        }

        /// <summary>[8] Replace the connection; the fight state stays (members greet the host again).</summary>
        void SwitchTransport()
        {
            var net = PartyNet.Current;
            if (net == null) { EnsureNet(); return; }
            var t = BuildTransport();
            if (t != null) net.ReplaceTransport(t);
        }

        /// <summary>begin happened: kills and results now go to my own dungeon_runs row.</summary>
        void Begin()
        {
            began = true;
            OnlineEconomy.SetRun(myRunId);
            roomKills.Clear();
            if (!amHost) return; // a member follows the host's RunStart
            var def = DungeonDatabase.Get(dungeonId);
            if (def != null && Game.Dungeon != null) Game.Dungeon.StartPartyRun(def, (DungeonDifficulty)difficulty);
        }

        void Update()
        {
            if (!OnlineSession.Playing) { Finish(null); return; }
            pollTimer -= Time.unscaledDeltaTime;
            heartbeatTimer -= Time.unscaledDeltaTime;
            if (state == "gathering")
            {
                // A member confirms once the host PC accepted its connection.
                // A member confirms once the host PC accepted its connection (the relay joins it by itself).
            if (!amHost && !joinSent && PartyNet.IsMember && PartyNet.Current.Welcomed && transportInfo.current != "relay") Join();
            if (PartyNet.Active && PartyNet.Current.Transport is ITransportHealth th && th.Failed && !switching) RequestNextTransport();
                if (pollTimer <= 0f) { pollTimer = PollSeconds; Fetch(); }
            }
            else if (state == "playing" && heartbeatTimer <= 0f)
            {
                heartbeatTimer = HeartbeatSeconds;
                Heartbeat();
            }
            // Host gone from the fight connection: ask the server whether I take over (§6.7).
            // On the relay the server decides by itself and says host.changed.
            if (state == "playing" && PartyNet.IsMember && transportInfo.current != "relay" && !PartyNet.Current.Transport.IsConnected && PartyNet.Current.Welcomed)
            {
                hostLostFor += Time.unscaledDeltaTime;
                if (hostLostFor >= HostLostSeconds && !claiming) ClaimHost();
            }
            else hostLostFor = 0f;
        }

        /// <summary>[SERVER 5] The chat socket said this run changed: read it now.</summary>
        public void FetchNow() => Fetch();

        void Fetch() => Api.Get($"{Char}/party-runs/{runId}", r => { if (r.ok) Read(MiniJson.Obj(r.data, "run")); else if (r.status == 404) Finish(null); });

        void Join()
        {
            joinSent = true;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["entry_token"] = entryToken };
            if (!string.IsNullOrEmpty(hostSteamId)) body["host_steam_id"] = hostSteamId; // Steam mode: the host this PC connected to
            Api.Post($"{Char}/party-runs/{runId}/join", body, r =>
            {
                if (r.ok) Read(MiniJson.Obj(r.data, "run"));
                else { joinSent = false; GameEvents.RaiseToast(PartyClient.Explain(r)); }
            });
        }

        /// <summary>Host: start with whoever is connected now (otherwise the server starts when the last one joins).</summary>
        public void BeginNow()
        {
            if (!amHost || state != "gathering") return;
            Api.Post($"{Char}/party-runs/{runId}/begin", new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() }, r =>
            {
                if (r.ok) Read(MiniJson.Obj(r.data, "run"));
                else GameEvents.RaiseToast(PartyClient.Explain(r));
            });
        }

        void Heartbeat()
        {
            Api.Post($"{Char}/party-runs/{runId}/heartbeat", new Dictionary<string, object> { ["seen_epoch"] = epoch }, r =>
            {
                if (!r.ok) { if (r.code == "RUN_NOT_FOUND") Finish(null); return; }
                var host = MiniJson.Obj(r.data, "host");
                bool changed = r.data.TryGetValue("host_changed", out var c) && c is bool b && b;
                state = MiniJson.Str(r.data, "state", state);
                myState = MiniJson.Str(MiniJson.Obj(r.data, "me"), "state", myState);
                if (changed && host != null)
                {
                    epoch = MiniJson.Int(host, "epoch", epoch);
                    hostCharacterId = MiniJson.Str(host, "character_id");
                    bool wasHost = amHost;
                    amHost = hostCharacterId == OnlineSession.Current?.ActiveCharacter;
                    if (wasHost && !amHost) GameEvents.RaiseToast("방장 권한이 다른 파티원에게 넘어갔다.");
                }
                if (state == "ended" || myState == "left") Finish(null);
            });
        }

        /// <summary>The host vanished: the server lets exactly one member take over (lowest live seat).</summary>
        void ClaimHost()
        {
            claiming = true;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["observed_epoch"] = epoch };
            Api.Post($"{Char}/party-runs/{runId}/host/claim", body, r =>
            {
                claiming = false;
                hostLostFor = 0f;
                if (!r.ok) return; // someone else took it, or the host is still alive for the server
                var host = MiniJson.Obj(r.data, "host");
                epoch = MiniJson.Int(host, "epoch", epoch + 1);
                hostCharacterId = MiniJson.Str(host, "character_id");
                hostKey = PartyNet.FromBase64Url(MiniJson.Str(r.data, "host_key"));
                amHost = true;
                TakeOver();
            });
        }

        /// <summary>[8] The relay server handed the host role to this PC.</summary>
        void OnPromoted()
        {
            if (Instance != this || amHost) return;
            TakeOver();
        }

        /// <summary>
        /// This PC continues the fight from the last snapshot: puppets become real monsters, the old host's
        /// body leaves, and the dungeon judges clears here from now on.
        /// </summary>
        void TakeOver()
        {
            var old = PartyNet.Current;
            var keep = old != null && old.Transport.Kind == "relay" ? old.Transport : null; // [8] the relay connection carries on
            var bodies = new List<PlayerController>();
            if (Game.Party != null)
                foreach (var m in Game.Party.Members) if (m != null && Game.Party.IsNetMember(m)) bodies.Add(m);
            PartyNet.End(keepTransport: keep != null);
            foreach (var b in bodies) Game.Party?.RemoveNetMember(b);
            foreach (var e in new List<EnemyController>(EnemyController.Active)) if (e != null && e.Puppet) e.ReleasePuppet();
            GameEvents.RaiseToast("방장이 떠나 내가 방장을 이어받았다.");
            amHost = true;
            transportInfo.amHost = true;
            PartyNet.BeginHost(keep ?? BuildTransport(), runId, mySlot, hostKey, Mathf.Max(1, humans));
        }

        // ---------------- host report (§6.8) ----------------

        void OnKilled(EnemyController e, int xp)
        {
            if (!amHost || e == null || e.Summoner != null || (e.Def != null && e.Def.noLoot) || Game.Dungeon == null || !Game.Dungeon.InRun) return;
            int room = Game.Dungeon.CurrentRoom;
            if (!roomKills.TryGetValue(room, out var counts)) roomKills[room] = counts = new Dictionary<string, int>();
            string id = e.Def != null ? e.Def.id : e.Stats.enemyId;
            counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
        }

        /// <summary>The host's whole-run observation, sent once when the run ends (before its own result).</summary>
        public void SendHostReport(bool cleared, float elapsedSeconds)
        {
            if (!amHost || reported || !PartyNet.IsHost) return;
            reported = true;
            var net = PartyNet.Current;
            var rooms = new List<object>();
            foreach (var kv in roomKills)
            {
                var kills = new List<object>();
                foreach (var k in kv.Value) kills.Add(new Dictionary<string, object> { ["monster_id"] = k.Key, ["count"] = Mathf.Min(99, k.Value) });
                rooms.Add(new Dictionary<string, object> { ["room_index"] = kv.Key, ["kills"] = kills });
            }
            var members = new List<object>();
            var ai = new List<object>();
            for (int slot = 0; slot < PartyManager.MaxMembers; slot++)
            {
                if (net.MemberAt(slot) == null) continue;
                var s = net.StatsFor(slot);
                if (slot < humans)
                {
                    string character = net.CharacterIdOf(slot);
                    if (string.IsNullOrEmpty(character)) continue;
                    members.Add(new Dictionary<string, object>
                    {
                        ["character_id"] = character, ["hits_taken"] = Mathf.Min(999, s.hitsTaken), ["max_combo"] = Mathf.Min(9999, s.maxCombo),
                        ["revives_used"] = Mathf.Min(9, s.revives), ["damage_dealt"] = s.damage,
                    });
                }
                else ai.Add(new Dictionary<string, object> { ["slot"] = slot, ["damage_dealt"] = s.damage });
            }
            var body = new Dictionary<string, object>
            {
                ["request_id"] = ApiClient.NewRequestId(), ["host_epoch"] = epoch, ["outcome"] = cleared ? "cleared" : "failed",
                ["elapsed_ms"] = Mathf.Clamp(Mathf.RoundToInt(elapsedSeconds * 1000f), 0, 3600000), ["rooms"] = rooms, ["members"] = members, ["ai"] = ai,
            };
            Api.Post($"{Char}/party-runs/{runId}/host-report", body, r => { if (!r.ok) Debug.LogWarning("[PARTY] host report: " + r.code); });
        }

        // ---------------- leaving ----------------

        /// <summary>Leave the run (back to the village mid-run, or cancel while gathering).</summary>
        public void Leave()
        {
            if (state == "gathering" || state == "playing")
                Api.Post($"{Char}/party-runs/{runId}/leave", new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() }, null);
            Finish(null);
        }

        void Finish(string toast)
        {
            if (Instance != this) return;
            Instance = null;
            EnemyController.Killed -= OnKilled;
            PartyNet.PromotedToHost -= OnPromoted;
            if (!string.IsNullOrEmpty(toast)) GameEvents.RaiseToast(toast);
            Destroy(gameObject);
        }

        /// <summary>The fight connection closes when this PC is back in the village.</summary>
        public static void OnBackInVillage()
        {
            if (Instance == null) return; // development party (-dotrpgParty) keeps its connection for the next run
            PartyNet.End();
            if (Instance != null && Instance.state != "gathering") Instance.Finish(null);
        }
    }
}
