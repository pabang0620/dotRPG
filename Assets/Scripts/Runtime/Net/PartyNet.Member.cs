using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY NET] Member side: own character runs here, everything else is drawn from the host.</summary>
    public sealed partial class PartyNet
    {
        string characterId, entryToken;
        bool welcomed, wasConnected;
        float helloTimer;
        uint commandTick;
        int expectedRoom = -1;
        uint lastSnapshot;
        readonly List<byte[]> pendingSpawns = new List<byte[]>();
        readonly Dictionary<int, EnemyController> puppets = new Dictionary<int, EnemyController>();

        public bool Welcomed => welcomed;

        public static PartyNet BeginMember(ITransport t, string run, int slot, string character, string token)
        {
            var net = Create(t, false, run, slot);
            net.characterId = character ?? "";
            net.entryToken = token ?? "";
            if (Game.Party != null) net.bySlot[slot] = Game.Party.Local;
            return net;
        }

        int HostPeer => transport.Peers.Count > 0 ? transport.Peers[0] : 0;

        // ---------------- tick ----------------

        void MemberTick()
        {
            bool connected = transport.IsConnected;
            if (wasConnected && !connected) GameEvents.RaiseToast("방장과의 연결이 끊겼다.");
            wasConnected = connected;

            if (!welcomed)
            {
                helloTimer -= Time.unscaledDeltaTime;
                if (helloTimer <= 0f && Game.Player != null)
                {
                    helloTimer = HelloInterval;
                    SendTo(HostPeer, NetChannel.Control, PartyWire.Build(w =>
                    {
                        w.Write(PartyMsg.Hello);
                        w.Write(runId);
                        w.Write(characterId);
                        w.Write((byte)mySlot);
                        w.Write(entryToken);
                        MemberCard.Of(Game.Player, mySlot, characterId).Write(w);
                    }));
                }
                return;
            }

            var me = Game.Player;
            if (me == null || Game.Dungeon == null || !Game.Dungeon.InRun) return;
            // Buttons go out the frame they are pressed; plain movement at 20 Hz.
            var cmd = me.Command;
            bool buttons = cmd.attack || cmd.skillSlot >= 0 || cmd.mobility || cmd.useHealing || cmd.useMana;
            stateTimer -= Time.unscaledDeltaTime;
            if (buttons || stateTimer <= 0f) NetCommandRouter.SendCommand(transport, HostPeer, mySlot, cmd, ++commandTick);
            if (stateTimer <= 0f)
            {
                stateTimer = StateInterval;
                SendTo(HostPeer, NetChannel.MemberState, PartyWire.Build(w =>
                {
                    w.Write((byte)mySlot);
                    PartyWire.WriteVec(w, me.Position);
                    w.Write((byte)me.Facing);
                    w.Write(me.Data.Mana);
                }));
            }
            FlushSpawns();
        }

        /// <summary>A potion or carrot healed the local character: the host's copy heals the same.</summary>
        public void SendHeal(int amount)
        {
            if (host || amount <= 0) return;
            SendTo(HostPeer, NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.Heal); w.Write((byte)mySlot); w.Write(amount); }));
        }

        // ---------------- receive ----------------

        void MemberReceive(NetPacket p)
        {
            switch (p.channel)
            {
                case NetChannel.Control:
                    using (var r = PartyWire.Reader(p.data))
                        if (r.ReadByte() == PartyMsg.Welcome) OnWelcome(r);
                    break;
                case NetChannel.Event:
                    OnEvent(p.data);
                    break;
                case NetChannel.Snapshot:
                    using (var r = PartyWire.Reader(p.data)) ApplySnapshot(r);
                    break;
            }
        }

        void OnWelcome(BinaryReader r)
        {
            bool inRun = r.ReadBoolean();
            string dungeonId = r.ReadString();
            var difficulty = (DungeonDifficulty)r.ReadByte();
            int room = r.ReadByte();
            int n = r.ReadByte();
            var cards = new List<MemberCard>();
            for (int i = 0; i < n; i++) cards.Add(MemberCard.Read(r));
            if (welcomed) return; // a repeated Hello got a second answer
            welcomed = true;
            Game.Party?.SetRosterHidden(true);
            foreach (var c in cards) AddPuppet(c);
            GameEvents.RaiseToast("방장에게 연결되었다.");
            if (inRun) StartFollowing(dungeonId, difficulty, room);
        }

        void AddPuppet(MemberCard c)
        {
            if (c.slot == mySlot || MemberAt(c.slot) != null || Game.Party == null) return;
            var local = Game.Party.Local;
            var body = Game.Party.AddNetMember(c.ToData(), null, true, local != null ? local.Position : Vector2.zero, Facing.Up);
            bySlot[c.slot] = body;
        }

        void StartFollowing(string dungeonId, DungeonDifficulty difficulty, int room)
        {
            var def = DungeonDatabase.Get(dungeonId);
            if (def == null || Game.Dungeon == null) return;
            expectedRoom = room;
            Game.Dungeon.EnterFollower(def, difficulty, room);
        }

        void OnEvent(byte[] data)
        {
            using (var r = PartyWire.Reader(data))
            {
                byte kind = r.ReadByte();
                switch (kind)
                {
                    case PartyMsg.RunStart:
                    {
                        string id = r.ReadString();
                        var diff = (DungeonDifficulty)r.ReadByte();
                        StartFollowing(id, diff, 0);
                        break;
                    }
                    case PartyMsg.RoomLoad:
                        expectedRoom = r.ReadByte();
                        puppets.Clear();
                        pendingSpawns.Clear();
                        Game.Dungeon?.FollowRoom(expectedRoom);
                        break;
                    case PartyMsg.EnemySpawn:
                        pendingSpawns.Add(data);
                        FlushSpawns();
                        break;
                    case PartyMsg.EnemyDie:
                    {
                        int id = r.ReadInt32();
                        int gold = r.ReadInt16();
                        FlushSpawns();
                        if (puppets.TryGetValue(id, out var e) && e != null) e.PuppetDie(gold);
                        puppets.Remove(id);
                        break;
                    }
                    case PartyMsg.Damage:
                    {
                        var at = PartyWire.ReadVec(r);
                        int amount = r.ReadInt32();
                        bool companion = r.ReadBoolean();
                        DamageNumber.Show(at, amount, false, companion);
                        break;
                    }
                    case PartyMsg.Act:
                    {
                        int slot = r.ReadByte();
                        int act = r.ReadByte();
                        int skill = r.ReadSByte();
                        var facing = (Facing)r.ReadByte();
                        if (slot != mySlot) MemberAt(slot)?.NetAct(act, skill, facing);
                        break;
                    }
                    case PartyMsg.RoomCleared:
                        Game.Dungeon?.FollowRoomCleared();
                        break;
                    case PartyMsg.RunEnd:
                    {
                        bool cleared = r.ReadBoolean();
                        string reason = r.ReadString();
                        int elapsedMs = r.ReadInt32();
                        int n = r.ReadByte();
                        var mine = new MemberRunStats { slot = mySlot };
                        for (int i = 0; i < n; i++)
                        {
                            var s = new MemberRunStats { slot = r.ReadByte(), hitsTaken = r.ReadInt16(), maxCombo = r.ReadInt16(), revives = r.ReadByte() };
                            if (s.slot == mySlot) mine = s;
                        }
                        Game.Dungeon?.FollowEnd(cleared, reason, elapsedMs / 1000f, mine);
                        break;
                    }
                    case PartyMsg.MemberJoined:
                        AddPuppet(MemberCard.Read(r));
                        break;
                    case PartyMsg.MemberLeft:
                    {
                        int slot = r.ReadByte();
                        var body = MemberAt(slot);
                        if (body != null) GameEvents.RaiseToast($"{body.DisplayName}님 연결 끊김");
                        break;
                    }
                }
            }
        }

        /// <summary>Spawns wait until this PC has loaded the room the host is in.</summary>
        void FlushSpawns()
        {
            if (pendingSpawns.Count == 0) return;
            var d = Game.Dungeon;
            if (d == null || !d.InRun || !d.RoomIsReady || d.CurrentRoom != expectedRoom || Game.World == null) return;
            foreach (var data in pendingSpawns)
            {
                using (var r = PartyWire.Reader(data))
                {
                    r.ReadByte();
                    int id = r.ReadInt32();
                    string monster = r.ReadString();
                    var pos = PartyWire.ReadVec(r);
                    int level = r.ReadInt16();
                    float hpMul = r.ReadSingle();
                    float dmgMul = r.ReadSingle();
                    bool summon = r.ReadBoolean();
                    if (puppets.ContainsKey(id)) continue;
                    var e = summon
                        ? MonsterDatabase.SpawnMinion(monster, pos, Game.World.ObjectsRoot, null)
                        : MonsterDatabase.Spawn(monster, pos, Game.World.ObjectsRoot, hpMul, dmgMul, level);
                    if (e == null) continue;
                    e.NetId = id;
                    e.MakePuppet();
                    puppets[id] = e;
                    if (!summon && Game.Dungeon.Run != null) Game.Dungeon.Run.Monsters++; // kill score base, as on the host
                }
            }
            pendingSpawns.Clear();
        }

        void ApplySnapshot(BinaryReader r)
        {
            uint tick = r.ReadUInt32();
            if (tick <= lastSnapshot) return; // late or duplicated (unreliable channel)
            lastSnapshot = tick;
            int room = r.ReadByte();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                int slot = r.ReadByte();
                var pos = PartyWire.ReadVec(r);
                var facing = (Facing)r.ReadByte();
                int hp = r.ReadInt32();
                int max = r.ReadInt32();
                var body = MemberAt(slot);
                if (body == null) continue;
                if (slot != mySlot) body.NetMoveTo(pos, facing);
                body.NetSetHealth(hp, max);
            }
            if (room != expectedRoom) return;
            int count = r.ReadInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                var pos = PartyWire.ReadVec(r);
                var facing = (Facing)r.ReadByte();
                var anim = (CharacterAnim)r.ReadByte();
                int hp = r.ReadInt32();
                int max = r.ReadInt32();
                bool frozen = r.ReadBoolean();
                if (puppets.TryGetValue(id, out var e) && e != null) e.ApplyNet(pos, facing, anim, hp, max, frozen);
            }
        }
    }
}
