using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY 8] Field party hunting (Docs/server/phase8_api.md §6). When two or more people of an online
    /// party stand in the same field map they share its monsters: one PC (the host, picked by the server)
    /// runs the field spawner and the fight, the others draw its monsters from snapshots, exactly like a
    /// party dungeon. Rewards stay per character: each member reports the kills the host credited to it.
    /// Lifecycle: map entered -> F1 enter -> transport (relay first) -> sync; heartbeat every 5 s, host
    /// observation every 10 s, F4 leave on map change; handover follows the server (host.changed).
    /// </summary>
    public sealed class FieldSession : MonoBehaviour
    {
        const float HeartbeatSeconds = 5f, ObserveSeconds = 10f;

        public static FieldSession Instance { get; private set; }
        public static bool Active => Instance != null && Instance.sessionId != null;
        /// <summary>Host generation of the current session (part of every field monster reference).</summary>
        public static int HostEpoch => Instance != null ? Instance.epoch : 0;

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        string sessionId, mapId, hostCharacterId;
        int seat, epoch = 1;
        bool amHost, entering;
        float heartbeatTimer, observeTimer, lastObserveAt, lastHeartbeatAt = -10f;
        /// <summary>The server takes one heartbeat per 2 s per character (RATE_HEARTBEAT_PER_2SEC); pushes that ask for one sooner wait.</summary>
        const float HeartbeatMinGap = 2.1f;
        readonly RoomTransportInfo transportInfo = new RoomTransportInfo { kind = "field" };

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("FieldSession");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FieldSession>();
            GameEvents.MapEntered += Instance.OnMapEntered;
            PartyClient.Changed += Instance.OnPartyChanged;
            PartyNet.PromotedToHost += Instance.OnPromoted;
        }

        public static void Shutdown()
        {
            if (Instance == null) return;
            Instance.Leave();
            GameEvents.MapEntered -= Instance.OnMapEntered;
            PartyClient.Changed -= Instance.OnPartyChanged;
            PartyNet.PromotedToHost -= Instance.OnPromoted;
            Destroy(Instance.gameObject);
            Instance = null;
        }

        /// <summary>Could this field be shared right now? (online, two or more people in my party, not in a run)</summary>
        public static bool MayShare()
        {
            if (!OnlineSession.Playing || Instance == null) return false;
            var party = PartyClient.Instance;
            if (party == null || !party.InParty || party.Members.Count < 2) return false;
            if (PartyRunSession.Active || (Game.Dungeon != null && Game.Dungeon.InRun)) return false;
            return Game.Session != null && IsHuntingMap(Game.Session.MapId);
        }

        /// <summary>Only hunting grounds are shared; towns (village, canyon, winter) have no monsters to share.</summary>
        static bool IsHuntingMap(string map) => MapRegistry.Get(map) is MapInfo m && !m.safe && !m.instanced;

        // ---------------- entering and leaving ----------------

        void OnMapEntered(string map)
        {
            if (sessionId != null && map != mapId) Leave();
            if (sessionId == null && IsHuntingMap(map)) Enter(map);
        }

        /// <summary>Someone joined or left my party: start or stop sharing the field I am in.</summary>
        void OnPartyChanged()
        {
            if (Game.Session == null || !OnlineSession.Playing) return;
            var party = PartyClient.Instance;
            bool shareable = party != null && party.InParty && party.Members.Count >= 2;
            if (!shareable && sessionId != null) { Leave(); GoSolo(); }
            // A partner joined or came online while I hunt here: share now (Update retries every few seconds too).
            if (shareable && sessionId == null) joinRetryAt = 0f;
        }

        void Enter(string map)
        {
            if (entering || !MayShare() || EnemySpawner.Current == null) return;
            entering = true;
            mapId = map;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["map_id"] = map };
            // [NET 2026-10-30] Same request_id resent on a lost answer or a 429 (2026-10-29: enter and leave refused by the
            // 1 per second limit left a member hunting alone next to the partner for over a minute).
            Api.PostIdempotent(Char + "/field-sessions/enter", body, r =>
            {
                entering = false;
                var spawner = EnemySpawner.Current;
                var s = r.ok ? MiniJson.Obj(r.data, "session") : null;
                if (s == null || Game.Session == null || Game.Session.MapId != map)
                {
                    spawner?.SetMode(EnemySpawner.SpawnerMode.Local); // alone or no party: solo field
                    return;
                }
                // The answer may come after this PC already spawned its own monsters (late answer, or joining mid-map):
                // Begin still shares - as host the local monsters become the shared ones, as follower they make way for the host's.
                if (spawner == null)
                {
                    sessionId = MiniJson.Str(s, "id");
                    Leave();
                    return;
                }
                Begin(s, MiniJson.Str(r.data, "host_key"));
            });
        }

        void Begin(Dictionary<string, object> s, string hostKey)
        {
            sessionId = MiniJson.Str(s, "id");
            ReadView(s);
            transportInfo.roomId = sessionId;
            transportInfo.seat = seat;
            transportInfo.amHost = amHost;
            transportInfo.hostKey = PartyNet.FromBase64Url(hostKey);
            var t = CombatTransportFactory.Create(transportInfo);
            if (t == null) { GameEvents.RaiseToast(L.T("파티원과 연결할 수 없어 혼자 사냥합니다.")); Leave(); EnemySpawner.Current?.SetMode(EnemySpawner.SpawnerMode.Local); joinRetryAt = Time.unscaledTime + 60f; return; }
            OnlineEconomy.FieldSessionId = sessionId;
            // [AI] The host's companions fill the seats people leave free; members show those, not their own.
            Game.Party?.SetRosterHidden(!amHost);
            if (amHost)
            {
                PartyNet.BeginFieldHost(t, sessionId, seat, mapId).SetFieldHumanSeats(transportInfo.seats);
                EnemySpawner.Current?.SetMode(EnemySpawner.SpawnerMode.Host);
            }
            else
            {
                EnemySpawner.Current?.SetMode(EnemySpawner.SpawnerMode.Follower);
                PartyNet.BeginFieldMember(t, sessionId, seat, OnlineSession.Current?.ActiveCharacter, mapId);
            }
            // Only when someone else is here: a party alone on the map still gets a session (others may come later).
            if (transportInfo.seats.Count >= 2) GameEvents.RaiseToast(L.T("파티원과 같은 사냥터에 있습니다. 몬스터를 함께 잡습니다."));
            heartbeatTimer = HeartbeatSeconds;
            observeTimer = ObserveSeconds;
        }

        void ReadView(Dictionary<string, object> s)
        {
            var host = MiniJson.Obj(s, "host");
            hostCharacterId = MiniJson.Str(host, "character_id");
            epoch = MiniJson.Int(host, "epoch", epoch);
            seat = MiniJson.Int(MiniJson.Obj(s, "me"), "seat", seat);
            amHost = hostCharacterId != null && hostCharacterId == OnlineSession.Current?.ActiveCharacter;
            transportInfo.ReadMembers(MiniJson.Arr(s, "members"), hostCharacterId);
            var transport = MiniJson.Obj(s, "transport");
            transportInfo.current = MiniJson.Str(transport, "current", transportInfo.current);
            transportInfo.epoch = MiniJson.Int(transport, "epoch", transportInfo.epoch);
            transportInfo.hostSteamId = MiniJson.Str(host, "steam_id");
            transportInfo.entryToken = MiniJson.Str(MiniJson.Obj(s, "me"), "entry_token");
        }

        /// <summary>The chat socket said the session changed: check it now.</summary>
        public void FetchNow() => heartbeatTimer = 0f;

        /// <summary>Leave the field session (map change, party gone, back to the title).</summary>
        public void Leave()
        {
            if (sessionId == null) return;
            string id = sessionId;
            sessionId = null;
            OnlineEconomy.FieldSessionId = null;
            if (amHost) SendObserve(id); // last credits before handing the monsters over
            if (PartyNet.Active && PartyNet.Current.FieldMode) EndNet(false);
            amHost = false;
            // sessionId is cleared above, so each session is left once; a slow earlier leave must not swallow this one.
            if (OnlineSession.Playing)
                Api.PostIdempotent($"{Char}/field-sessions/{id}/leave", new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() }, _ => { });
        }

        // ---------------- running ----------------

        const float JoinRetrySeconds = 5f;
        float joinRetryAt;

        /// <summary>A party member is online on the hunting map I am on (the server's presence, GET /party).</summary>
        static bool PartnerHere(string map)
        {
            var party = PartyClient.Instance;
            if (party == null) return false;
            foreach (var m in party.Members) if (!m.me && m.online && m.mapId == map) return true;
            return false;
        }

        void Update()
        {
            // Hunting alone while a party member is on the same field (I logged in or entered before the party list
            // loaded, or they arrived while I was here): share it now instead of waiting for someone's next map change.
            if (sessionId == null && !entering && OnlineSession.Playing && Game.Session != null && Time.unscaledTime >= joinRetryAt)
            {
                joinRetryAt = Time.unscaledTime + JoinRetrySeconds;
                string here = Game.Session.MapId;
                if (IsHuntingMap(here) && MayShare() && PartnerHere(here)) Enter(here);
            }
            if (sessionId == null || !OnlineSession.Playing) return;
            // [PARTY 43] The relay closed this room for good (room over, seat removed, transport moved) while the server's
            // session goes on. Nothing else reacts to that, so the partner stayed invisible for hours on the same map:
            // leave and enter again (a version mismatch or a closed account would only loop, those stay).
            if (PartyNet.Active && PartyNet.Current.FieldMode && PartyNet.Current.Transport is RelayTransport relay
                && relay.ClosedReason != null && relay.ClosedReason != "wire" && relay.ClosedReason != "account")
            {
                Debug.LogWarning("[FIELD] relay closed (" + relay.ClosedReason + "): entering the field session again");
                Leave();
                GoSolo();
                joinRetryAt = 0f;
                return;
            }
            WatchRoster();
            if (sessionId == null) return;
            heartbeatTimer -= Time.unscaledDeltaTime;
            if (heartbeatTimer <= 0f && Time.unscaledTime - lastHeartbeatAt >= HeartbeatMinGap) { heartbeatTimer = HeartbeatSeconds; Heartbeat(); }
            if (amHost)
            {
                observeTimer -= Time.unscaledDeltaTime;
                if (observeTimer <= 0f) { observeTimer = ObserveSeconds; SendObserve(sessionId); }
            }
        }

        // ---------------- recovery [NET 2026-10-30] ----------------

        /// <summary>The server says a partner is online on this map, but the sync has nobody this long: build it again.</summary>
        const float RosterMismatchSeconds = 20f;
        float mismatchFor, autoResyncGap = 60f, nextAutoResyncAt;

        /// <summary>
        /// A partner on the same field the sync does not see (no peer as host, never welcomed as member): the room or the
        /// session went stale without any close reaching us. Re-enter the field session (backoff 60 s doubling to 240 s).
        /// </summary>
        void WatchRoster()
        {
            bool alone = PartyNet.Active && PartyNet.Current.FieldMode
                && (PartyNet.IsHost ? PartyNet.Current.Transport.Peers.Count == 0 : !PartyNet.Current.Welcomed);
            if (!alone && PartyNet.Active && PartyNet.Current.FieldMode) autoResyncGap = 60f; // synced again: next time starts short
            if (!alone || !PartnerHere(mapId)) { mismatchFor = 0f; return; }
            mismatchFor += Time.unscaledDeltaTime;
            if (mismatchFor < RosterMismatchSeconds || Time.unscaledTime < nextAutoResyncAt) return;
            Debug.LogWarning("[FIELD] partner on this map but not in the sync for " + RosterMismatchSeconds + " s: entering the field session again");
            nextAutoResyncAt = Time.unscaledTime + autoResyncGap;
            autoResyncGap = Mathf.Min(autoResyncGap * 2f, 240f);
            Resync();
        }

        /// <summary>
        /// [NET 2026-10-30] Leave this field session and enter it again (manual "연결 다시 맞추기" or the roster check).
        /// The party stays; on a hunting map without a session it only asks to share again.
        /// </summary>
        public void Resync()
        {
            mismatchFor = 0f;
            if (sessionId != null)
            {
                Leave();
                GoSolo();
            }
            joinRetryAt = 0f;
        }

        void Heartbeat()
        {
            lastHeartbeatAt = Time.unscaledTime;
            bool synced = PartyNet.Active && (PartyNet.IsHost || PartyNet.Current.Welcomed);
            var body = new Dictionary<string, object> { ["seen_epoch"] = epoch, ["synced"] = synced };
            string id = sessionId;
            Api.Post($"{Char}/field-sessions/{id}/heartbeat", body, r =>
            {
                if (id != sessionId) return;
                if (!r.ok)
                {
                    if (r.code == "FIELD_SESSION_NOT_FOUND" || r.code == "FIELD_SESSION_ENDED") EndLocally();
                    else heartbeatTimer = Mathf.Min(heartbeatTimer, HeartbeatMinGap); // rate limit or a network blip: ask again soon
                    return;
                }
                if (MiniJson.Str(r.data, "state") == "ended") { EndLocally(); return; }
                foreach (var m in MiniJson.Arr(r.data, "members") ?? new List<object>()) MemberCardCheck.Read("field:" + id, m);
                var host = MiniJson.Obj(r.data, "host");
                bool changed = r.data.TryGetValue("host_changed", out var c) && c is bool b && b;
                if (!changed || host == null) return;
                epoch = MiniJson.Int(host, "epoch", epoch);
                hostCharacterId = MiniJson.Str(host, "character_id");
                bool nowHost = hostCharacterId == OnlineSession.Current?.ActiveCharacter;
                if (nowHost && !amHost) TakeOver();
                else if (!nowHost && amHost) Demote();
            });
        }

        /// <summary>Host: which seats earned which kills (the server's check against free-riding, §6.10).</summary>
        void SendObserve(string id)
        {
            if (!PartyNet.IsHost || !PartyNet.Current.FieldMode) return;
            var credits = new List<object>();
            foreach (var kv in PartyNet.Current.TakeCredits())
                credits.Add(new Dictionary<string, object> { ["seat"] = kv.Key, ["kills"] = Mathf.Min(99, kv.Value) });
            if (credits.Count == 0) return;
            // The real time these credits cover (the server checks them against what could have spawned).
            float now = Time.realtimeSinceStartup;
            int windowMs = Mathf.Clamp(Mathf.RoundToInt((now - (lastObserveAt > 0f ? lastObserveAt : now - ObserveSeconds)) * 1000f), 1000, 30000);
            lastObserveAt = now;
            var body = new Dictionary<string, object>
            {
                ["request_id"] = ApiClient.NewRequestId(), ["host_epoch"] = epoch, ["window_ms"] = windowMs, ["credits"] = credits,
            };
            Api.Post($"{Char}/field-sessions/{id}/observe", body, null);
        }

        /// <summary>The server made this PC the field host (relay push or heartbeat).</summary>
        void OnPromoted()
        {
            if (sessionId == null || amHost || !PartyNet.Active || !PartyNet.Current.FieldMode) return;
            epoch++;
            TakeOver();
        }

        /// <summary>Continue the field from the last snapshot: puppets become real, the spawner runs here.</summary>
        void TakeOver()
        {
            amHost = true;
            var old = PartyNet.Current;
            var keep = old != null && old.Transport.Kind == "relay" ? old.Transport : null;
            EndNet(keep != null);
            var released = new List<EnemyController>();
            foreach (var e in new List<EnemyController>(EnemyController.Active))
                if (e != null && e.Puppet) { e.ReleasePuppet(); released.Add(e); }
            EnemySpawner.Current?.TakeOver(released);
            transportInfo.amHost = true;
            Game.Party?.SetRosterHidden(false);
            PartyNet.BeginFieldHost(keep ?? CombatTransportFactory.Create(transportInfo), sessionId, seat, mapId).SetFieldHumanSeats(transportInfo.seats);
            GameEvents.RaiseToast(L.T("이 사냥터의 몬스터 계산을 내가 이어받았습니다."));
        }

        /// <summary>Another PC became host (this one came back after a drop): draw its monsters instead.</summary>
        void Demote()
        {
            amHost = false;
            var old = PartyNet.Current;
            var keep = old != null && old.Transport.Kind == "relay" ? old.Transport : null;
            EndNet(keep != null); // the new member sync draws everyone again as puppets
            transportInfo.amHost = false;
            EnemySpawner.Current?.SetMode(EnemySpawner.SpawnerMode.Follower);
            Game.Party?.SetRosterHidden(true);
            PartyNet.BeginFieldMember(keep ?? CombatTransportFactory.Create(transportInfo), sessionId, seat, OnlineSession.Current?.ActiveCharacter, mapId);
        }

        /// <summary>The session is over on the server: back to hunting alone on this map.</summary>
        void EndLocally()
        {
            sessionId = null;
            OnlineEconomy.FieldSessionId = null;
            if (PartyNet.Active && PartyNet.Current.FieldMode) EndNet(false);
            amHost = false;
            GoSolo();
            GameEvents.RaiseToast(L.T("함께 사냥이 끝났습니다. 이제 혼자 사냥합니다."));
        }

        /// <summary>
        /// Stops the field sync and removes the other members' bodies with it. PartyNet.End leaves them in the party
        /// (OnLocalSpawned keeps net members), so without this a partner stayed behind as a frozen copy after leaving the
        /// field, next to the same person drawn again by TownPeers or a new sync.
        /// </summary>
        static void EndNet(bool keepTransport)
        {
            var bodies = new List<PlayerController>();
            if (Game.Party != null)
                foreach (var m in Game.Party.Members) if (m != null && Game.Party.IsNetMember(m)) bodies.Add(m);
            PartyNet.End(keepTransport);
            foreach (var b in bodies) Game.Party?.RemoveNetMember(b);
        }

        /// <summary>Host's monsters disappear from this screen and the local spawner fills the field again.</summary>
        static void GoSolo()
        {
            foreach (var e in new List<EnemyController>(EnemyController.Active))
                if (e != null && e.Puppet) Destroy(e.gameObject);
            EnemySpawner.Current?.SetMode(EnemySpawner.SpawnerMode.Local);
        }
    }
}
