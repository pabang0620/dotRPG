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
        bool amHost, entering, leaving;
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
            // A new partner joined while I hunt: the next map entry shares (monsters here are already mine).
        }

        void Enter(string map)
        {
            if (entering || !MayShare() || EnemySpawner.Current == null) return;
            entering = true;
            mapId = map;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["map_id"] = map };
            Api.Post(Char + "/field-sessions/enter", body, r =>
            {
                entering = false;
                var spawner = EnemySpawner.Current;
                var s = r.ok ? MiniJson.Obj(r.data, "session") : null;
                if (s == null || Game.Session == null || Game.Session.MapId != map)
                {
                    spawner?.SetMode(EnemySpawner.SpawnerMode.Local); // alone or no party: solo field
                    return;
                }
                // Answer came after the wait: this PC already spawned its own monsters; share from the next map.
                if (spawner == null || spawner.Mode != EnemySpawner.SpawnerMode.Pending)
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
            if (t == null) { GameEvents.RaiseToast("파티원과 연결할 수 없어 혼자 사냥한다."); Leave(); EnemySpawner.Current?.SetMode(EnemySpawner.SpawnerMode.Local); return; }
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
            GameEvents.RaiseToast("파티원과 같은 사냥터에 있다. 몬스터를 함께 잡는다.");
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
            if (PartyNet.Active && PartyNet.Current.FieldMode) PartyNet.End();
            amHost = false;
            if (OnlineSession.Playing && !leaving)
            {
                leaving = true;
                Api.Post($"{Char}/field-sessions/{id}/leave", new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() }, _ => leaving = false);
            }
        }

        // ---------------- running ----------------

        void Update()
        {
            if (sessionId == null || !OnlineSession.Playing) return;
            heartbeatTimer -= Time.unscaledDeltaTime;
            if (heartbeatTimer <= 0f && Time.unscaledTime - lastHeartbeatAt >= HeartbeatMinGap) { heartbeatTimer = HeartbeatSeconds; Heartbeat(); }
            if (amHost)
            {
                observeTimer -= Time.unscaledDeltaTime;
                if (observeTimer <= 0f) { observeTimer = ObserveSeconds; SendObserve(sessionId); }
            }
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
            var bodies = new List<PlayerController>();
            if (Game.Party != null)
                foreach (var m in Game.Party.Members) if (m != null && Game.Party.IsNetMember(m)) bodies.Add(m);
            PartyNet.End(keepTransport: keep != null);
            foreach (var b in bodies) Game.Party?.RemoveNetMember(b);
            var released = new List<EnemyController>();
            foreach (var e in new List<EnemyController>(EnemyController.Active))
                if (e != null && e.Puppet) { e.ReleasePuppet(); released.Add(e); }
            EnemySpawner.Current?.TakeOver(released);
            transportInfo.amHost = true;
            Game.Party?.SetRosterHidden(false);
            PartyNet.BeginFieldHost(keep ?? CombatTransportFactory.Create(transportInfo), sessionId, seat, mapId).SetFieldHumanSeats(transportInfo.seats);
            GameEvents.RaiseToast("이 사냥터의 몬스터 계산을 내가 이어받았다.");
        }

        /// <summary>Another PC became host (this one came back after a drop): draw its monsters instead.</summary>
        void Demote()
        {
            amHost = false;
            var old = PartyNet.Current;
            var keep = old != null && old.Transport.Kind == "relay" ? old.Transport : null;
            PartyNet.End(keepTransport: keep != null);
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
            if (PartyNet.Active && PartyNet.Current.FieldMode) PartyNet.End();
            amHost = false;
            GoSolo();
            GameEvents.RaiseToast("함께 사냥이 끝났다. 이제 혼자 사냥한다.");
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
