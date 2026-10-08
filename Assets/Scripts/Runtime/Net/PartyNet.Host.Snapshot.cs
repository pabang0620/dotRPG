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
