using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class PartyNet
    {
        void AssignEnemyIds()
        {
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || e.Puppet) continue;
                // [8] Monsters inherited at a host handover keep their numbers but were never registered here: without
                // this, members who (re)join after the handover never get them (invisible monsters that still hit).
                if (e.NetId != 0 && enemies.ContainsKey(e.NetId)) continue;
                if (FieldMode && !e.Shared) continue; // [8] quest and story spawns stay on this PC
                int id = e.NetId != 0 ? e.NetId : nextEnemyId++;
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
            WriteWarnings(w);
            WriteProjectiles(w);
            WriteGroggy(w); // [PARTY 19] trailing: an older member build stops before it
        });

        /// <summary>
        /// [PARTY 19] The 무력화 gauge as the host has it (every member's hit lands on the host's monster, so this is the
        /// whole party's gauge). Members draw it from here instead of their own puppet, which takes no hits.
        /// </summary>
        void WriteGroggy(System.IO.BinaryWriter w)
        {
            int count = 0;
            foreach (var kv in enemies) if (kv.Value != null && !kv.Value.IsDead && kv.Value.GroggyMax > 0f) count++;
            w.Write((byte)Mathf.Min(count, 32));
            int sent = 0;
            foreach (var kv in enemies)
            {
                var e = kv.Value;
                if (e == null || e.IsDead || e.GroggyMax <= 0f) continue;
                if (sent++ >= 32) break;
                w.Write(kv.Key);
                w.Write(e.Groggy);
                w.Write(e.GroggyMax);
                w.Write(e.GroggyRemaining);
                w.Write(e.GroggyDuration);
            }
        }

        // ---------------- actions (puppets on member PCs replay them) ----------------

        void OnAttackPressed(PlayerController m) => SendAct(m, 0, -1);

        void OnCasted(PlayerController m, int slot) => SendAct(m, 1, slot);

        /// <summary>[NINJA] Second press of the same slot (move to the kunai / swap with a clone): act kind 2.</summary>
        void OnFollowedUp(PlayerController m, int slot) => SendAct(m, 2, slot);

        /// <summary>[CANCEL] Shift cut a cast (the host's own, or a member's copy here): act kind 3, members stop the look.</summary>
        void OnCastCancelled(PlayerController m) => SendAct(m, 3, -1);

        /// <summary>[RAID] The roar started on the host: members draw the same line, count, edge and safe circles.</summary>
        void OnRaidRoar(EnemyController boss, float cast, Vector2[] circles)
        {
            if (!host || boss == null) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w =>
            {
                w.Write(PartyMsg.RaidRoar);
                w.Write(boss.NetId);
                PartyWire.WriteVec(w, boss.Position);
                w.Write(cast);
                w.Write((byte)circles.Length);
                foreach (var c in circles) PartyWire.WriteVec(w, c);
            }));
        }

        /// <summary>[RAID] 결계 의식 on the host: members show the line, the circles and the result.</summary>
        void OnSealLine(EnemyController boss, string line)
        {
            if (!host || boss == null) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RaidSealLine); w.Write(boss.NetId); w.Write(line ?? ""); }));
        }

        void OnSealOpen(EnemyController boss, float seconds, Vector2[] circles)
        {
            if (!host || boss == null) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w =>
            {
                w.Write(PartyMsg.RaidSealOpen);
                w.Write(boss.NetId);
                w.Write(seconds);
                w.Write((byte)circles.Length);
                foreach (var c in circles) PartyWire.WriteVec(w, c);
            }));
        }

        void OnSealEnd(EnemyController boss, bool broken)
        {
            if (!host || boss == null) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RaidSealEnd); w.Write(boss.NetId); w.Write(broken); }));
        }

        /// <summary>[RAID] A member was stunned on the host: that seat's PC (and everyone's copy) stuns too.</summary>
        void OnRaidStun(PlayerController m, float seconds)
        {
            if (!host || m == null) return;
            int slot = SlotOf(m);
            if (slot < 0) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RaidStun); w.Write((byte)slot); w.Write(seconds); }));
        }

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
            // [PARTY 10] The damage meter this PC counted for every seat (its own result window shows the same), so
            // members see the 딜 기여도 too: their puppets never take the hits here, the host's numbers are the real ones.
            var meter = new List<KeyValuePair<int, int>>();
            if (Game.Party != null)
                foreach (var kv in bySlot) if (kv.Value != null) meter.Add(new KeyValuePair<int, int>(kv.Key, Game.Party.DamageOf(kv.Value)));
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
                // [PARTY 10] Trailing block: an older member build stops reading before it.
                w.Write((byte)meter.Count);
                foreach (var m in meter)
                {
                    w.Write((byte)m.Key);
                    w.Write(m.Value);
                }
            }));
        }
    }
}
