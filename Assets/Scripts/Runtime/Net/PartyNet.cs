using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY NET] Co-op dungeon sync (PLAN_ONLINE §3.2, Docs/server/phase4_api.md §12). Star topology: the
    /// host PC runs the whole fight (monster AI, hits, HP) exactly as offline, with each remote human as a
    /// party member driven by that player's commands and reported position. Members run their own
    /// character locally (movement feels immediate), draw every other body as a puppet from the host's
    /// 10 Hz snapshots and replay the host's events (spawns, deaths, attacks, damage numbers, rooms).
    /// Rewards never travel here: each member reports its own kills and result to the server.
    /// Host: PartyNet.Host.cs. Member: PartyNet.Member.cs.
    /// </summary>
    public sealed partial class PartyNet : MonoBehaviour
    {
        public const float SnapshotInterval = 0.1f, StateInterval = 0.05f, HelloInterval = 0.5f;
        /// <summary>[8] Byte layout version of these messages; both sides of a room must match (relay hello, Hello).</summary>
        public const int WireVersion = 7; // 7: costume skin on the member card // 5: SeatFreed + host AI seats in the field

        /// <summary>[8] The relay server made this PC the host (the session takes over the fight).</summary>
        public static event Action PromotedToHost;

        public static PartyNet Current { get; private set; }
        public static bool Active => Current != null;
        public static bool IsHost => Current != null && Current.host;
        public static bool IsMember => Current != null && !Current.host;

        ITransport transport;
        bool host;
        string runId;
        int mySlot;
        float snapshotTimer, stateTimer;

        /// <summary>Slot of every body on this PC (local player, companions, remote copies or puppets).</summary>
        readonly Dictionary<int, PlayerController> bySlot = new Dictionary<int, PlayerController>();

        public int MySlot => mySlot;
        /// <summary>[PARTY 8] Field party session (shared field monsters) instead of a dungeon run.</summary>
        public bool FieldMode { get; private set; }
        /// <summary>The map of the field session.</summary>
        public string FieldMap { get; private set; }
        /// <summary>Sync runs: a dungeon run is on, or this is a field session.</summary>
        bool SyncActive => FieldMode || (Game.Dungeon != null && Game.Dungeon.InRun);

        /// <summary>[PARTY 8] Field session on this map; the host runs the field spawner for the party.</summary>
        public static PartyNet BeginFieldHost(ITransport t, string session, int slot, string map)
        {
            var net = BeginHost(t, session, slot, null, PartyManager.MaxMembers);
            net.FieldMode = true;
            net.FieldMap = map;
            return net;
        }

        public static PartyNet BeginFieldMember(ITransport t, string session, int slot, string character, string map)
        {
            var net = BeginMember(t, session, slot, character, "");
            net.FieldMode = true;
            net.FieldMap = map;
            net.expectedRoom = 0;
            return net;
        }
        public string RunId => runId;
        public ITransport Transport => transport;
        /// <summary>A member joined (slot) or dropped.</summary>
        public static event Action<int> MemberConnected, MemberLost;

        static PartyNet Create(ITransport t, bool asHost, string run, int slot)
        {
            End();
            var go = new GameObject(asHost ? "PartyNetHost" : "PartyNetMember");
            DontDestroyOnLoad(go);
            var net = go.AddComponent<PartyNet>();
            net.transport = t;
            net.host = asHost;
            net.runId = run ?? "";
            net.mySlot = slot;
            Current = net;
            Game.State?.RefreshTimeScale();
            PlayerCombat.AttackPressed += net.OnAttackPressed;
            SkillCaster.Casted += net.OnCasted;
            if (t is RelayTransport relay) relay.HostChanged += net.OnRelayHostChanged;
            // [8] Level-ups and gear changes reach the other PCs (their copy / puppet of this character).
            if (Game.Session != null)
            {
                Game.Session.Progression.Changed += net.MarkCardDirty;
                Game.Session.Equipment.Changed += net.MarkCardDirty;
                if (Game.Cosmetics != null) Game.Cosmetics.Changed += net.MarkCardDirty;
            }
            return net;
        }

        bool cardDirty;
        float cardTimer;
        void MarkCardDirty() => cardDirty = true;

        /// <summary>[8] Sends this PC's card (and, as host, its AI seats' cards) once things settle (0.5 s).</summary>
        void FlushCard()
        {
            cardTimer -= Time.unscaledDeltaTime;
            if (!cardDirty || cardTimer > 0f || Game.Player == null) return;
            if (!host && !welcomed) return;
            cardDirty = false;
            cardTimer = 0.5f;
            if (host)
            {
                foreach (var kv in bySlot)
                {
                    var m = kv.Value;
                    if (m == null || (Game.Party != null && Game.Party.IsNetMember(m))) continue; // remote people send their own
                    var card = MemberCard.Of(m, kv.Key, m.IsLocal ? OnlineSession.Current?.ActiveCharacter : "");
                    Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.CardUpdate); card.Write(w); }));
                }
            }
            else
            {
                var card = MemberCard.Of(Game.Player, mySlot, characterId);
                SendTo(HostPeer, NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.CardUpdate); card.Write(w); }));
            }
        }

        /// <summary>
        /// [8] Swap the connection (the server moved the room to another transport). The fight stays; a
        /// member greets the host again on the new connection, the host waits for those greetings.
        /// </summary>
        public void ReplaceTransport(ITransport next)
        {
            if (next == null) return;
            if (transport is RelayTransport oldRelay) oldRelay.HostChanged -= OnRelayHostChanged;
            if (transport is IDisposable d) d.Dispose();
            transport = next;
            if (next is RelayTransport relay) relay.HostChanged += OnRelayHostChanged;
            if (host) peerSlot.Clear();
            else { welcomed = false; helloTimer = 0f; }
        }

        /// <summary>[8] Server-led host handover on the relay: a member re-greets the new host, or becomes it.</summary>
        void OnRelayHostChanged(int seat, int epoch)
        {
            if (seat == mySlot)
            {
                if (!host) PromotedToHost?.Invoke();
                return;
            }
            if (!host) { welcomed = false; helloTimer = 0f; } // say hello to the new host
        }

        /// <summary>Stops the sync (run over, title screen). Network members leave the party.</summary>
        /// <param name="keepTransport">[8] Host handover: the same connection carries on under a new PartyNet.</param>
        public static void End(bool keepTransport = false)
        {
            var net = Current;
            if (net == null) return;
            Current = null;
            Game.State?.RefreshTimeScale();
            PlayerCombat.AttackPressed -= net.OnAttackPressed;
            SkillCaster.Casted -= net.OnCasted;
            if (net.transport is RelayTransport relay) relay.HostChanged -= net.OnRelayHostChanged;
            if (Game.Session != null)
            {
                Game.Session.Progression.Changed -= net.MarkCardDirty;
                Game.Session.Equipment.Changed -= net.MarkCardDirty;
                if (Game.Cosmetics != null) Game.Cosmetics.Changed -= net.MarkCardDirty;
            }
            net.UnhookEnemies();
            if (!keepTransport)
            {
                Game.Party?.SetRosterHidden(false);
                Game.Party?.SetCompanionCap(-1); // [AI] the whole roster again
                if (net.transport is RelayTransport r) r.Leave();
                if (net.transport is IDisposable d) d.Dispose();
            }
            Destroy(net.gameObject);
        }

        void Update()
        {
            if (transport == null) return;
            transport.Tick();
            while (transport.TryReceive(out var p))
            {
                if (p.data == null) continue;
                try
                {
                    if (host) HostReceive(p);
                    else MemberReceive(p);
                }
                catch (EndOfStreamException) { } // truncated packet from another build: skip
                catch (IOException) { }
            }
            if (host) HostTick();
            else MemberTick();
            FlushCard();
        }

        // ---------------- sending ----------------

        void SendTo(int peer, byte channel, byte[] data) => transport.Send(peer, channel, data);

        void Broadcast(byte channel, byte[] data, int exceptPeer = -1)
        {
            foreach (var peer in transport.Peers)
                if (peer != exceptPeer) transport.Send(peer, channel, data);
        }

        // ---------------- slots ----------------

        public PlayerController MemberAt(int slot) => bySlot.TryGetValue(slot, out var m) && m != null ? m : null;

        int SlotOf(PlayerController m)
        {
            foreach (var kv in bySlot) if (kv.Value == m) return kv.Key;
            return -1;
        }

        // ---------------- entry token (phase4_api §6.2) ----------------

        /// <summary>base64url(HMAC-SHA256(run_key, "run.character.slot")[0..16]).</summary>
        public static string EntryToken(byte[] runKey, string run, string characterId, int slot)
        {
            using (var h = new HMACSHA256(runKey))
            {
                var mac = h.ComputeHash(Encoding.UTF8.GetBytes($"{run}.{characterId}.{slot}"));
                var head = new byte[16];
                Array.Copy(mac, head, 16);
                return Convert.ToBase64String(head).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        public static byte[] FromBase64Url(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string b = s.Replace('-', '+').Replace('_', '/');
            switch (b.Length % 4) { case 2: b += "=="; break; case 3: b += "="; break; }
            try { return Convert.FromBase64String(b); } catch (FormatException) { return null; }
        }

        // ---------------- development (-dotrpgParty host|join) ----------------

        /// <summary>Two windows on one PC without the server: plain UDP, no token check, offline rewards.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
#if !DOTRPG_RELEASE // [RELEASE] development party test only
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-dotrpgParty") continue;
                bool asHost = args[i + 1] == "host";
                var udp = new UdpTransport(asHost, UdpTransport.PartyPort);
                if (asHost) BeginHost(udp, "dev", 0, null, 2); // seats 0 (host) and 1 (the other window)
                else BeginMember(udp, "dev", 1, "", "");
                Debug.Log($"[PARTY] dev party net as {(asHost ? "host" : "member")}");
            }
#endif
        }
    }
}
