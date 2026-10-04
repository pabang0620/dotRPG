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
                        copy.NetMoveTo(pos, facing);
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

        void AssignEnemyIds()
        {
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.NetId != 0 || e.IsDead) continue;
                if (FieldMode && !e.Shared) continue; // [8] quest and story spawns stay on this PC
                int id = nextEnemyId++;
                e.NetId = id;
                if (e.RefEpoch <= 0) e.RefEpoch = Mathf.Max(1, FieldSession.HostEpoch); // [8] fixed for the monster's life
                enemies[id] = e;
                var enemy = e;
                Action<EnemyController> onDied = x =>
                {
                    enemies.Remove(id);
                    int gold = x.Behaviour is GoldRunnerBehaviour g ? g.GoldSpilled : 0;
                    int mask = FieldMode ? FinalCredit(x) : 0xFF; // [8] dungeon: everyone in the run reports it
                    Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.EnemyDie); w.Write(id); w.Write((short)gold); w.Write((byte)mask); }));
                };
                Action<DamageInfo> onDamaged = info =>
                {
                    if (enemy == null) return;
                    int hitter = info.AttackerMember != null ? SlotOf(info.AttackerMember) : -1;
                    if (hitter >= 0) enemy.CreditBits |= 1 << hitter; // [8] who helped kill it
                    var at = enemy.Position + new Vector2(0f, 1.55f);
                    bool companion = PartyManager.IsCompanionHit(info);
                    Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.Damage); PartyWire.WriteVec(w, at); w.Write(info.amount); w.Write(companion); }));
                };
                e.Died += onDied;
                e.Health.Damaged += onDamaged;
                unhooks.Add(() => { if (enemy != null) { enemy.Died -= onDied; if (enemy.Health != null) enemy.Health.Damaged -= onDamaged; } });
                Broadcast(NetChannel.Event, SpawnMsg(id, e));
            }
        }

        static byte[] SpawnMsg(int id, EnemyController e) => PartyWire.Build(w =>
        {
            w.Write(PartyMsg.EnemySpawn);
            w.Write(id);
            w.Write(e.Def != null ? e.Def.id : e.Stats != null ? e.Stats.enemyId : "");
            PartyWire.WriteVec(w, e.Position);
            w.Write((short)e.Level);
            w.Write(e.HpMultiplier);
            w.Write(e.DamageMultiplier);
            w.Write(e.Summoner != null);
            w.Write(e.RefEpoch); // [8] members report this monster with the same reference
        });

        byte[] BuildSnapshot() => PartyWire.Build(w =>
        {
            w.Write(++snapTick);
            w.Write((byte)(FieldMode ? 0 : Game.Dungeon.CurrentRoom));
            int n = 0;
            foreach (var kv in bySlot) if (kv.Value != null) n++;
            w.Write((byte)n);
            foreach (var kv in bySlot)
            {
                var m = kv.Value;
                if (m == null) continue;
                w.Write((byte)kv.Key);
                PartyWire.WriteVec(w, m.Position);
                w.Write((byte)m.Facing);
                w.Write(m.IsDead ? 0 : m.Health.Current);
                w.Write(m.Health.Max);
            }
            int count = 0;
            foreach (var kv in enemies) if (kv.Value != null && !kv.Value.IsDead) count++;
            w.Write((short)count);
            foreach (var kv in enemies)
            {
                var e = kv.Value;
                if (e == null || e.IsDead) continue;
                w.Write(kv.Key);
                PartyWire.WriteVec(w, e.Position);
                w.Write((byte)e.NetFacing);
                w.Write((byte)e.NetAnim);
                w.Write(e.Health.Current);
                w.Write(e.Health.Max);
                w.Write(e.NetFrozen);
            }
        });

        // ---------------- actions (puppets on member PCs replay them) ----------------

        void OnAttackPressed(PlayerController m) => SendAct(m, 0, -1);

        void OnCasted(PlayerController m, int slot) => SendAct(m, 1, slot);

        void SendAct(PlayerController m, int kind, int skillSlot)
        {
            if (!host || m == null) return;
            int slot = SlotOf(m);
            if (slot < 0) return;
            int ownerPeer = -1;
            foreach (var kv in peerSlot) if (kv.Value == slot) ownerPeer = kv.Key; // that member already did it locally
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.Act); w.Write((byte)slot); w.Write((byte)kind); w.Write((sbyte)skillSlot); w.Write((byte)m.Facing); }), ownerPeer);
        }

        // ---------------- called by DungeonDirector on the host ----------------

        public void HostRunStarted(string dungeonId, DungeonDifficulty difficulty)
        {
            Game.Party?.SetRosterHidden(false);
            // [AI] Exactly the run's AI seats from the host's own roster (the server checks the AI count).
            if (AiSeats >= 0) Game.Party?.SetCompanionCap(Mathf.Clamp(AiSeats, 0, PartyManager.MaxMembers - humans));
            RebuildHostSlots();
            slotStats.Clear();
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RunStart); w.Write(dungeonId); w.Write((byte)difficulty); }));
        }

        public void HostRoomLoaded(int index)
        {
            enemies.Clear(); // the old room's monsters are gone with the map
            RebuildHostSlots();
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RoomLoad); w.Write((byte)index); }));
        }

        public void HostRoomCleared() => Broadcast(NetChannel.Event, new[] { PartyMsg.RoomCleared });

        /// <summary>A hit by a party member landed (combo counting per seat).</summary>
        public void HostRegisterHit(PlayerController member, int amount)
        {
            int slot = SlotOf(member);
            if (slot < 0) return;
            var s = Stats(slot);
            float now = Time.time;
            if (now - s.lastHitAt > DungeonRanking.ComboWindow) s.combo = 0;
            s.combo++;
            s.lastHitAt = now;
            if (s.combo > s.maxCombo) s.maxCombo = s.combo;
            s.damage += Mathf.Max(0, amount);
        }

        public void HostRevived(PlayerController member)
        {
            int slot = SlotOf(member);
            if (slot >= 0) Stats(slot).revives++;
        }

        /// <summary>What the host counted for one seat.</summary>
        public MemberRunStats StatsFor(int slot)
        {
            var s = Stats(slot);
            return new MemberRunStats { slot = slot, hitsTaken = s.hits, maxCombo = s.maxCombo, revives = s.revives, damage = s.damage };
        }

        /// <summary>Human seats with a body on the host (for the host report).</summary>
        public IEnumerable<int> HumanSlots
        {
            get
            {
                foreach (var kv in bySlot) if (kv.Key < humans && kv.Value != null) yield return kv.Key;
            }
        }

        public string CharacterIdOf(int slot) =>
            slot == mySlot ? OnlineSession.Current?.ActiveCharacter : humanCards.TryGetValue(slot, out var c) ? c.characterId : null;

        public void HostRunEnded(bool cleared, string reason, float elapsedSeconds)
        {
            var list = new List<MemberRunStats>();
            foreach (var slot in HumanSlots) list.Add(StatsFor(slot));
            Broadcast(NetChannel.Event, PartyWire.Build(w =>
            {
                w.Write(PartyMsg.RunEnd);
                w.Write(cleared);
                w.Write(reason ?? "");
                w.Write(Mathf.RoundToInt(elapsedSeconds * 1000f));
                w.Write((byte)list.Count);
                foreach (var s in list)
                {
                    w.Write((byte)s.slot);
                    w.Write((short)s.hitsTaken);
                    w.Write((short)s.maxCombo);
                    w.Write((byte)s.revives);
                }
            }));
        }
    }
}
