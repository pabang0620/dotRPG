using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY NET] Member side: own character runs here, everything else is drawn from the host.</summary>
    public sealed partial class PartyNet
    {
        string characterId, entryToken;
        bool welcomed, wasConnected, everWelcomed;
        float helloTimer;
        uint commandTick;
        int expectedRoom = -1;
        uint lastSnapshot;
        readonly List<byte[]> pendingSpawns = new List<byte[]>();
        readonly Dictionary<int, EnemyController> puppets = new Dictionary<int, EnemyController>();
        /// <summary>[PARTY 7/8] Seats the host said were left while the dungeon keeps the body.</summary>
        readonly HashSet<int> leftSlots = new HashSet<int>();

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
            if (wasConnected && !connected && !RunFinished) GameEvents.RaiseToast(L.T("방장과의 연결이 끊겼습니다.")); // [PARTY 9] not after the end
            // Back after a drop: the host forgot this seat's connection (it ignores our input until a Hello), so greet again.
            if (!wasConnected && connected && everWelcomed) Regreet();
            wasConnected = connected;
            DropKeptBodies(leftSlots); // [PARTY 7/8] no afterimage after the end

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
                        w.Write(WireVersion);
                        MemberCard.Of(Game.Player, mySlot, characterId).Write(w);
                    }));
                }
                return;
            }

            var me = Game.Player;
            if (me == null || !SyncActive) return;
            // Buttons go out the frame they are pressed; plain movement at 20 Hz.
            var cmd = Game.IsPlaying && !Game.State.ChangedThisFrame && !me.IsDead ? me.Command : ActorCommand.None;
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
            bool field = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean(); // [8]
            string fieldMap = field ? r.ReadString() : null;
            if (welcomed) return; // a repeated Hello got a second answer
            bool again = everWelcomed; // [8] reconnect or new host: bodies and the run are already here
            welcomed = true;
            everWelcomed = true;
            Game.Party?.SetRosterHidden(true);
            if (again)
            {
                // [PARTY 7/8] Reconnect or new host: bodies the (new) host does not have any more (the old host, its AI) go,
                // or they stand as a second copy next to the ones it draws now.
                var listed = new HashSet<int>();
                foreach (var c in cards) listed.Add(c.slot);
                foreach (var slot in new List<int>(bySlot.Keys))
                {
                    if (slot == mySlot || listed.Contains(slot)) continue;
                    if (MemberAt(slot) is PlayerController stale && Game.Party != null && Game.Party.IsNetMember(stale)) Game.Party.RemoveNetMember(stale);
                    bySlot.Remove(slot);
                }
                foreach (var c in cards) AddPuppet(c);
                return;
            }
            foreach (var c in cards) AddPuppet(c);
            GameEvents.RaiseToast(L.T("방장에게 연결되었습니다."));
            if (field) { FieldMode = true; FieldMap = fieldMap; expectedRoom = 0; return; }
            if (inRun) StartFollowing(dungeonId, difficulty, room);
        }

        void AddPuppet(MemberCard c)
        {
            if (c.slot == mySlot || Game.Party == null) return;
            leftSlots.Remove(c.slot); // [PARTY 7/8] someone sits there again
            if (MemberAt(c.slot) != null) return;
            // [PARTY 7/8] The same person drawn under another seat (seats moved with a new host): one body only.
            foreach (var kv in new List<KeyValuePair<int, PlayerController>>(bySlot))
            {
                if (kv.Key == mySlot || kv.Value == null || kv.Value.DisplayName != c.name || !Game.Party.IsNetMember(kv.Value)) continue;
                Game.Party.RemoveNetMember(kv.Value);
                bySlot.Remove(kv.Key);
            }
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
                        ClearWarnings();
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
                        int mask = r.ReadByte(); // [8] seats the host credited
                        FlushSpawns();
                        if (puppets.TryGetValue(id, out var e) && e != null) e.PuppetDie(gold, mask);
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
                    case PartyMsg.RaidRoar:
                    {
                        int id = r.ReadInt32();
                        PartyWire.ReadVec(r); // boss position (the puppet is already there)
                        float cast = r.ReadSingle();
                        var circles = new Vector2[r.ReadByte()];
                        for (int i = 0; i < circles.Length; i++) circles[i] = PartyWire.ReadVec(r);
                        FlushSpawns();
                        puppets.TryGetValue(id, out var boss);
                        RaidMechanics.ShowRoar(boss, cast, circles);
                        break;
                    }
                    case PartyMsg.RaidSealLine:
                    {
                        int id = r.ReadInt32();
                        string line = r.ReadString();
                        FlushSpawns();
                        puppets.TryGetValue(id, out var boss);
                        RaidSeal.ShowLine(boss, line);
                        break;
                    }
                    case PartyMsg.RaidSealOpen:
                    {
                        int id = r.ReadInt32();
                        float seconds = r.ReadSingle();
                        var circles = new Vector2[r.ReadByte()];
                        for (int i = 0; i < circles.Length; i++) circles[i] = PartyWire.ReadVec(r);
                        FlushSpawns();
                        puppets.TryGetValue(id, out var boss);
                        RaidSeal.ShowOpened(boss, seconds, circles);
                        break;
                    }
                    case PartyMsg.RaidSealEnd:
                    {
                        int id = r.ReadInt32();
                        bool broken = r.ReadBoolean();
                        puppets.TryGetValue(id, out var boss);
                        RaidSeal.ShowEnded(boss, broken);
                        break;
                    }
                    case PartyMsg.RaidLaunch: MemberLaunch(r); break;
                    case PartyMsg.RaidDrain: MemberDrain(r); break;
                    case PartyMsg.ReviveState: MemberReviveState(r); break;
                    case PartyMsg.VillageVote: MemberVillageVote(r); break;
                    case PartyMsg.RaidStun:
                    {
                        int slot = r.ReadByte();
                        float seconds = r.ReadSingle();
                        (slot == mySlot ? Game.Player : MemberAt(slot))?.Stun(seconds);
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
                        if (r.BaseStream.Position < r.BaseStream.Length) ReadHostMeter(r); // [PARTY 10]
                        Game.Dungeon?.FollowEnd(cleared, reason, elapsedMs / 1000f, mine);
                        break;
                    }
                    case PartyMsg.MemberJoined:
                        AddPuppet(MemberCard.Read(r));
                        break;
                    case PartyMsg.CardUpdate:
                    {
                        // [8] Host, its AI seats or another member changed level or gear.
                        var card = MemberCard.Read(r);
                        if (card.slot == mySlot) break;
                        var body = MemberAt(card.slot);
                        if (body == null) AddPuppet(card);
                        else card.ApplyTo(body);
                        break;
                    }
                    case PartyMsg.SeatFreed:
                    {
                        int slot = r.ReadByte();
                        if (slot != mySlot && MemberAt(slot) is PlayerController ai)
                        {
                            Game.Party?.RemoveNetMember(ai);
                            bySlot.Remove(slot);
                        }
                        break;
                    }
                    case PartyMsg.MemberLeft:
                    {
                        int slot = r.ReadByte();
                        var body = MemberAt(slot);
                        if (body != null && !RunFinished) GameEvents.RaiseToast(L.F("{0}님 연결 끊김", body.DisplayName)); // [PARTY 9]
                        if (!FieldMode && slot != mySlot) leftSlots.Add(slot); // [PARTY 7/8] kept until the run is over
                        // [8] In the field the host drops the body of someone who left; do the same here (a dungeon keeps it).
                        if (FieldMode && slot != mySlot && body is PlayerController gone)
                        {
                            Game.Party?.RemoveNetMember(gone);
                            bySlot.Remove(slot);
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// [PARTY 10] The host's damage meter per seat, read before the run ends here so the result window's 딜 기여도
        /// shows the host's numbers (puppets take no real hits on a member, its own meter stays near zero).
        /// </summary>
        void ReadHostMeter(BinaryReader r)
        {
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                int slot = r.ReadByte();
                int damage = r.ReadInt32();
                var body = slot == mySlot ? Game.Player : MemberAt(slot);
                if (body == null || Game.Party == null) continue;
                int missing = damage - Game.Party.DamageOf(body);
                if (missing > 0) Game.Party.RecordDamageDealt(body, missing);
            }
        }

        /// <summary>Spawns wait until this PC has loaded the room the host is in.</summary>
        void FlushSpawns()
        {
            if (pendingSpawns.Count == 0) return;
            if (Game.World == null) return;
            if (FieldMode)
            {
                if (Game.World.MapId != FieldMap) return; // [8] still loading the field
            }
            else
            {
                var d = Game.Dungeon;
                if (d == null || !d.InRun || !d.RoomIsReady || d.CurrentRoom != expectedRoom) return;
            }
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
                    int refEpoch = r.ReadInt32();
                    if (puppets.ContainsKey(id)) continue;
                    // [8] Field skeletons have no monster table entry: build them like the field spawner does.
                    var spawner = EnemySpawner.Current;
                    // [HUNT] Hunting zones mix kinds at the zone's level: draw them exactly as the zone spawns them.
                    var e = FieldMode && spawner != null && spawner.Zone != null
                        ? MonsterDatabase.SpawnField(monster, pos, spawner.transform, level, spawner.Zone.KillXp)
                        : FieldMode && MonsterDatabase.Get(monster) == null && spawner != null && spawner.Stats != null
                        ? EnemyController.Create(spawner.Stats, spawner.Look, pos, spawner.transform)
                        : summon
                            ? MonsterDatabase.SpawnMinion(monster, pos, Game.World.ObjectsRoot, null)
                            : MonsterDatabase.Spawn(monster, pos, Game.World.ObjectsRoot, hpMul, dmgMul, level);
                    if (e != null && FieldMode) e.Shared = true;
                    if (e == null) continue;
                    e.NetId = id;
                    e.RefEpoch = refEpoch;
                    e.MakePuppet();
                    puppets[id] = e;
                    if (!summon && !FieldMode && Game.Dungeon.Run != null) Game.Dungeon.Run.Monsters++; // kill score base, as on the host
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
            ReadWarnings(r);
            ReadProjectiles(r);
        }
    }
}
