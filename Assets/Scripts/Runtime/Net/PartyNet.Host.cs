using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY NET] Host side: accepts members, runs their copies, sends snapshots and events.</summary>
    public sealed partial class PartyNet
    {
        sealed class SlotStats
        {
            public int hits, combo, maxCombo, revives, damage;
            public float lastHitAt = -99f;
        }

        byte[] hostKey;
        int humans = 1;
        uint snapTick;
        int nextEnemyId = 1;
        readonly Dictionary<int, int> peerSlot = new Dictionary<int, int>();
        readonly Dictionary<int, NetworkInput> remoteInputs = new Dictionary<int, NetworkInput>();
        readonly Dictionary<int, MemberCard> humanCards = new Dictionary<int, MemberCard>();
        readonly Dictionary<int, EnemyController> enemies = new Dictionary<int, EnemyController>();
        readonly Dictionary<int, SlotStats> slotStats = new Dictionary<int, SlotStats>();
        readonly List<Action> unhooks = new List<Action>();
        readonly HashSet<PlayerController> hookedBodies = new HashSet<PlayerController>();

        /// <param name="key">The run key from the server (null = development, no token check).</param>
        /// <param name="humanCount">People in the run (their seats are 0..n-1; AI seats follow).</param>
        /// <summary>[AI] Party dungeon: how many of the host's own mercenaries take the free seats (the run's ai_count).</summary>
        public int AiSeats = -1;

        public static PartyNet BeginHost(ITransport t, string run, int slot, byte[] key, int humanCount)
        {
            var net = Create(t, true, run, slot);
            net.hostKey = key;
            net.humans = Mathf.Clamp(humanCount, 1, PartyManager.MaxMembers);
            // [8] After a handover the inherited monsters keep their numbers: new ones continue above them.
            foreach (var e in EnemyController.Active)
                if (e != null && e.NetId >= net.nextEnemyId) net.nextEnemyId = e.NetId + 1;
            net.RebuildHostSlots();
            return net;
        }

        /// <summary>After an inherited host role (phase4_api §6.7): the new key checks reconnecting members.</summary>
        public void SetHostKey(byte[] key) => hostKey = key;

        /// <summary>Own body, own AI companions (seats after the people) and the remote copies.</summary>
        void RebuildHostSlots()
        {
            var party = Game.Party;
            if (party == null) return;
            var keep = new Dictionary<int, PlayerController>();
            foreach (var kv in bySlot) if (kv.Value != null && party.IsNetMember(kv.Value)) keep[kv.Key] = kv.Value;
            bySlot.Clear();
            foreach (var kv in keep) bySlot[kv.Key] = kv.Value;
            bySlot[mySlot] = party.Local;
            // [AI] Field: people sit in the seats the session gave them, the AI take whatever is left.
            int aiSlot = FieldMode ? 0 : humans;
            foreach (var m in party.Members)
            {
                if (m == null || m == party.Local || party.IsNetMember(m)) continue;
                while (bySlot.ContainsKey(aiSlot) || (FieldMode && fieldHumanSeats.Contains(aiSlot))) aiSlot++;
                if (aiSlot >= PartyManager.MaxMembers) break;
                bySlot[aiSlot++] = m;
            }
            foreach (var kv in bySlot) HookBody(kv.Key, kv.Value);
        }

        // ---------------- [AI] field seats ----------------

        /// <summary>Seats the field session gave people (this PC's included). The host's AI fill the others.</summary>
        readonly HashSet<int> fieldHumanSeats = new HashSet<int>();

        /// <summary>Field host: the session's people. Called at start and whenever someone joins or leaves.</summary>
        public void SetFieldHumanSeats(IEnumerable<int> seats)
        {
            fieldHumanSeats.Clear();
            foreach (int s in seats) fieldHumanSeats.Add(s);
            fieldHumanSeats.Add(mySlot);
            ApplyFieldSeats();
        }

        /// <summary>
        /// Puts as many of the host's mercenaries out as there are free seats and tells the members which AI
        /// seats appeared (MemberJoined) or went (SeatFreed), so every screen shows the same party.
        /// </summary>
        void ApplyFieldSeats()
        {
            var party = Game.Party;
            if (!host || !FieldMode || party == null) return;
            var before = new Dictionary<int, PlayerController>();
            foreach (var kv in bySlot)
                if (kv.Value != null && kv.Value != party.Local && !party.IsNetMember(kv.Value)) before[kv.Key] = kv.Value;
            party.SetCompanionCap(Mathf.Max(0, PartyManager.MaxMembers - fieldHumanSeats.Count));
            RebuildHostSlots();
            foreach (var kv in before)
                if (!bySlot.TryGetValue(kv.Key, out var now) || now != kv.Value)
                    Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.SeatFreed); w.Write((byte)kv.Key); }));
            foreach (var kv in bySlot)
            {
                var m = kv.Value;
                if (m == null || m == party.Local || party.IsNetMember(m)) continue;
                if (before.TryGetValue(kv.Key, out var was) && was == m) continue;
                var card = MemberCard.Of(m, kv.Key, "");
                Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.MemberJoined); card.Write(w); }));
            }
        }

        // ---------------- [8] field credit ----------------

        /// <summary>A monster's kill credit: seats that hit it plus living seats within range at its death.</summary>
        public const float FieldCreditRange = 14f;
        readonly Dictionary<int, int> creditedKills = new Dictionary<int, int>();

        int FinalCredit(EnemyController e)
        {
            int mask = e.CreditBits;
            foreach (var kv in bySlot)
            {
                var m = kv.Value;
                if (m != null && !m.IsDead && Vector2.Distance(m.Position, e.Position) <= FieldCreditRange) mask |= 1 << kv.Key;
            }
            e.CreditBits = mask;
            for (int seat = 0; seat < PartyManager.MaxMembers; seat++)
                if ((mask & (1 << seat)) != 0) creditedKills[seat] = (creditedKills.TryGetValue(seat, out int n) ? n : 0) + 1;
            return mask;
        }

        /// <summary>Credited kills per seat since the last observe report (then reset).</summary>
        public Dictionary<int, int> TakeCredits()
        {
            var copy = new Dictionary<int, int>();
            // [AI] Only people earn field credit on the server; kills of the host's AI already count for the host nearby.
            foreach (var kv in creditedKills) if (!FieldMode || fieldHumanSeats.Contains(kv.Key)) copy[kv.Key] = kv.Value;
            creditedKills.Clear();
            return copy;
        }

        /// <summary>Counts hits taken by each seat (rank inputs for the member reports, §8).</summary>
        void HookBody(int slot, PlayerController m)
        {
            if (m == null || m.Health == null || !hookedBodies.Add(m)) return;
            Action<DamageInfo> onHit = _ => Stats(slot).hits++;
            m.Health.Damaged += onHit;
            var h = m.Health;
            unhooks.Add(() => { if (h != null) h.Damaged -= onHit; });
        }

        SlotStats Stats(int slot)
        {
            if (!slotStats.TryGetValue(slot, out var s)) slotStats[slot] = s = new SlotStats();
            return s;
        }

        void UnhookEnemies()
        {
            foreach (var u in unhooks) u();
            unhooks.Clear();
            hookedBodies.Clear();
        }

        // ---------------- receive ----------------

        void HostReceive(NetPacket p)
        {
            switch (p.channel)
            {
                case NetChannel.Control:
                    using (var r = PartyWire.Reader(p.data))
                        if (r.ReadByte() == PartyMsg.Hello) OnHello(p.from, r);
                    break;
                case NetChannel.Input:
                    if (p.data.Length >= 1 + NetCommand.Size && OwnsSlot(p.from, p.data[0]) && remoteInputs.TryGetValue(p.data[0], out var input))
                        input.Enqueue(NetCommand.Read(p.data, 1));
                    break;
                case NetChannel.MemberState:
                    using (var r = PartyWire.Reader(p.data))
                    {
                        int slot = r.ReadByte();
                        var pos = PartyWire.ReadVec(r);
                        var facing = (Facing)r.ReadByte();
                        float mana = r.ReadSingle();
                        var copy = OwnsSlot(p.from, slot) ? MemberAt(slot) : null;
                        if (copy == null) break;
                        copy.NetMoveTo(ClampMove(slot, copy, pos), facing);
                        copy.Data.Mana = Mathf.Clamp(mana, 0f, copy.Data.Stats.MaxMp);
                    }
                    break;
                case NetChannel.Event:
                    using (var r = PartyWire.Reader(p.data))
                    {
                        byte kind = r.ReadByte();
                        if (kind == PartyMsg.CardUpdate)
                        {
                            // [8] A member levelled up or changed gear: update its copy here and tell the others.
                            var card = MemberCard.Read(r);
                            if (!OwnsSlot(p.from, card.slot)) break;
                            if (humanCards.TryGetValue(card.slot, out var old)) card.characterId = old.characterId;
                            MemberCardCheck.Correct(card, false);
                            humanCards[card.slot] = card;
                            card.ApplyTo(MemberAt(card.slot));
                            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.CardUpdate); card.Write(w); }), p.from);
                            break;
                        }
                        if (kind != PartyMsg.Heal) break;
                        int slot = r.ReadByte();
                        int amount = r.ReadInt32();
                        var copy = OwnsSlot(p.from, slot) ? MemberAt(slot) : null;
                        if (copy != null && amount > 0) copy.Health.Heal(Mathf.Min(amount, copy.Health.Max));
                    }
                    break;
            }
        }

        readonly Dictionary<int, float> moveBudget = new Dictionary<int, float>();
        readonly Dictionary<int, float> moveAt = new Dictionary<int, float>();

        /// <summary>
        /// [ANTI-ABUSE] A member's reported position may move at most 1.5 x its top speed per second (phase9 19.6). A
        /// small banked allowance covers dashes and blinks; a teleport beyond it is cut to the allowed distance.
        /// </summary>
        Vector2 ClampMove(int slot, PlayerController copy, Vector2 pos)
        {
            const float Burst = 8f;
            float now = Time.unscaledTime;
            float dt = moveAt.TryGetValue(slot, out float last) ? Mathf.Clamp(now - last, 0f, 1f) : 1f;
            moveAt[slot] = now;
            float speed = copy.TopSpeed * 1.5f;
            float budget = Mathf.Min(Burst + speed * .5f, (moveBudget.TryGetValue(slot, out float b) ? b : Burst) + speed * dt);
            Vector2 delta = pos - copy.NetTarget;
            float step = delta.magnitude;
            if (step > budget) { pos = copy.NetTarget + delta / step * budget; step = budget; }
            moveBudget[slot] = budget - step;
            return pos;
        }

        bool OwnsSlot(int peer, int slot) => peerSlot.TryGetValue(peer, out int s) && s == slot;

        void OnHello(int peer, System.IO.BinaryReader r)
        {
            string run = r.ReadString();
            string characterId = r.ReadString();
            int slot = r.ReadByte();
            string token = r.ReadString();
            int wire = r.ReadInt32();
            var card = MemberCard.Read(r);
            if (wire != WireVersion) { GameEvents.RaiseToast("파티원과 게임 버전이 다릅니다."); return; }
            if (run != runId || slot == mySlot || slot >= humans) return;
            // Entry token: only someone the server put in this seat can take it (no server call needed).
            // On the relay the server already stamped the sender's seat, so the token is not needed.
            bool relay = transport.Kind == "relay";
            if (relay && peer != slot) return;
            if (!relay && hostKey != null && token != EntryToken(hostKey, run, characterId, slot)) return;
            foreach (var kv in peerSlot)
                if (kv.Value == slot && kv.Key != peer) return; // seat already taken by another connection
            card.slot = slot;
            card.characterId = characterId;
            MemberCardCheck.Correct(card, true); // [ANTI-ABUSE] level, gear and career as the server has them
            bool fresh = !peerSlot.ContainsKey(peer);
            peerSlot[peer] = slot;
            humanCards[slot] = card;

            // [AI] A person takes a seat an AI was holding: the AI steps out first.
            if (FieldMode && !fieldHumanSeats.Contains(slot)) { fieldHumanSeats.Add(slot); ApplyFieldSeats(); }
            var copy = MemberAt(slot);
            if (copy == null && Game.Party != null)
            {
                var input = new NetworkInput();
                remoteInputs[slot] = input;
                var local = Game.Party.Local;
                copy = Game.Party.AddNetMember(card.ToData(), input, false, local != null ? Game.Party.SpotFor(slot % 3) : Vector2.zero, Facing.Up);
                bySlot[slot] = copy;
                HookBody(slot, copy);
            }
            SendTo(peer, NetChannel.Control, WelcomeFor(slot));
            foreach (var kv in enemies)
                if (kv.Value != null && !kv.Value.IsDead) SendTo(peer, NetChannel.Event, SpawnMsg(kv.Key, kv.Value));
            if (fresh)
            {
                Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.MemberJoined); card.Write(w); }), peer);
                MemberConnected?.Invoke(slot);
                GameEvents.RaiseToast($"{card.name}님이 파티에 연결되었습니다.");
            }
        }

        byte[] WelcomeFor(int slot) => PartyWire.Build(w =>
        {
            w.Write(PartyMsg.Welcome);
            var d = Game.Dungeon;
            bool inRun = d != null && d.InRun && d.CurrentDungeon != null;
            w.Write(inRun);
            w.Write(inRun ? d.CurrentDungeon.id : "");
            w.Write((byte)(inRun ? (int)d.CurrentDifficulty : 0));
            w.Write((byte)(inRun ? d.CurrentRoom : 0));
            var cards = new List<MemberCard>();
            foreach (var kv in bySlot)
            {
                if (kv.Key == slot || kv.Value == null) continue;
                cards.Add(humanCards.TryGetValue(kv.Key, out var c) ? c
                    : MemberCard.Of(kv.Value, kv.Key, kv.Key == mySlot ? OnlineSession.Current?.ActiveCharacter : ""));
            }
            w.Write((byte)cards.Count);
            foreach (var c in cards) c.Write(w);
            w.Write(FieldMode); // [8] field session: no dungeon to follow
            w.Write(FieldMap ?? "");
        });

        // ---------------- tick ----------------

        readonly List<int> lostPeers = new List<int>();

        void HostTick()
        {
            // Dropped members: their copy stays (stands still) and the others are told.
            lostPeers.Clear();
            foreach (var kv in peerSlot)
            {
                bool connected = false;
                foreach (var p in transport.Peers) if (p == kv.Key) connected = true;
                if (!connected) lostPeers.Add(kv.Key);
            }
            foreach (var peer in lostPeers)
            {
                int slot = peerSlot[peer];
                peerSlot.Remove(peer);
                MemberLost?.Invoke(slot);
                // [8] In the field a member who left (other map, village) takes its body along; a dungeon keeps it.
                if (FieldMode && MemberAt(slot) is PlayerController gone)
                {
                    Game.Party?.RemoveNetMember(gone);
                    bySlot.Remove(slot);
                    remoteInputs.Remove(slot);
                    fieldHumanSeats.Remove(slot);
                    ApplyFieldSeats(); // [AI] an AI takes the seat back
                }
                string who = humanCards.TryGetValue(slot, out var c) ? c.name : "파티원";
                GameEvents.RaiseToast($"{who}님 연결 끊김");
                Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.MemberLeft); w.Write((byte)slot); }));
            }

            if (!SyncActive) return;
            AssignEnemyIds();
            snapshotTimer -= Time.unscaledDeltaTime;
            if (snapshotTimer > 0f) return;
            snapshotTimer = SnapshotInterval;
            if (transport.Peers.Count > 0) Broadcast(NetChannel.Snapshot, BuildSnapshot());
        }

    }
}
