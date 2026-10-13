using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [REVIVE 2026-10-13] Revives in a party run (Docs/PLAN_BOSS_DIFFICULTY_2026-10-13.md G10): every person revives in
    /// place with their own coin, up to the difficulty's revives each and the party's shared pool. The host counts and
    /// revives the body (members see it rise through the HP snapshot); members only ask. Also the 그라흐 mechanics
    /// that touch a member's own body or MP (파동 throw, 결계 MP loss), which that member's PC owns.
    /// </summary>
    public sealed partial class PartyNet
    {
        // ---------------- member ----------------

        /// <summary>Member: the coin is paid, ask the host to stand this seat up.</summary>
        public void MemberRequestRevive()
        {
            if (host) return;
            SendTo(HostPeer, NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.ReviveRequest); w.Write((byte)mySlot); }));
        }

        void MemberReviveState(System.IO.BinaryReader r)
        {
            int partyUsed = r.ReadByte();
            int slot = r.ReadByte();
            int used = r.ReadByte();
            var run = Game.Dungeon != null ? Game.Dungeon.Run : null;
            if (run == null) return;
            run.PartyRevivesUsed = partyUsed;
            if (slot != mySlot) return;
            bool granted = used > run.RevivesUsed;
            run.RevivesUsed = used;
            // The body stands up through the HP snapshot; MP is this PC's, so it fills here. A refused request
            // (the seat's or the party's revives ran out meanwhile) answers with the same count.
            if (granted) { if (Game.Player != null) Game.Player.Data.Mana = Game.Player.MaxMana; }
            else GameEvents.RaiseToast("남은 부활 횟수가 없어 부활하지 못했습니다.");
        }

        void MemberLaunch(System.IO.BinaryReader r)
        {
            int slot = r.ReadByte();
            var v = PartyWire.ReadVec(r);
            float seconds = r.ReadSingle();
            if (slot == mySlot) Game.Player?.Launch(v, seconds);
        }

        void MemberDrain(System.IO.BinaryReader r)
        {
            int slot = r.ReadByte();
            float share = r.ReadSingle();
            var me = Game.Player;
            if (slot == mySlot && me != null) me.Data.Mana = Mathf.Max(0f, me.Data.Mana - me.MaxMana * share);
        }

        // ---------------- host ----------------

        /// <summary>Host: a member asked to revive. Checked against the seat's and the party's revives.</summary>
        void HostReviveRequest(int peer, System.IO.BinaryReader r)
        {
            int slot = r.ReadByte();
            var run = Game.Dungeon != null ? Game.Dungeon.Run : null;
            var body = OwnsSlot(peer, slot) ? MemberAt(slot) : null;
            if (run == null || run.State != DungeonRunState.Playing || body == null || !body.IsDead) return;
            var s = Stats(slot);
            if (s.revives >= run.Numbers.revives || run.PartyRevivesLeft <= 0) { HostSendReviveState(slot); return; }
            s.revives++;
            run.PartyRevivesUsed++;
            Game.Party?.ReviveMember(body, 1f, DungeonDirector.ReviveInvulnerable);
            HostSendReviveState(slot);
        }

        /// <summary>Host: the counts after a revive of <paramref name="slot"/> (the host's own included).</summary>
        public void HostSendReviveState(int slot)
        {
            if (!host) return;
            var run = Game.Dungeon != null ? Game.Dungeon.Run : null;
            if (run == null) return;
            int used = Stats(slot).revives;
            Broadcast(NetChannel.Event, PartyWire.Build(w =>
            {
                w.Write(PartyMsg.ReviveState);
                w.Write((byte)Mathf.Clamp(run.PartyRevivesUsed, 0, 255));
                w.Write((byte)slot);
                w.Write((byte)Mathf.Clamp(used, 0, 255));
            }));
        }

        /// <summary>Host: true while any person's body in the run is up (AI seats do not count).</summary>
        public bool AnyHumanUp
        {
            get
            {
                foreach (var kv in bySlot) if (kv.Key < humans && kv.Value != null && !kv.Value.IsDead) return true;
                return false;
            }
        }

        void OnRaidLaunch(PlayerController m, Vector2 velocity, float seconds)
        {
            if (!host || m == null) return;
            int slot = SlotOf(m);
            if (slot < 0 || slot == mySlot) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RaidLaunch); w.Write((byte)slot); PartyWire.WriteVec(w, velocity); w.Write(seconds); }));
        }

        void OnRaidDrain(PlayerController m, float share)
        {
            if (!host || m == null) return;
            int slot = SlotOf(m);
            if (slot < 0 || slot == mySlot) return;
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.RaidDrain); w.Write((byte)slot); w.Write(share); }));
        }
    }
}
