using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        // -dotrpgOnlinePause <folder>: real party packet handlers/physics with an in-process peer.
        // Uses isolated saves and no account or server. ESC invokes the same Flow.Pause path.
        IEnumerator OnlinePauseRun()
        {
            dgnPassed = dgnFailed = 0;
            yield return Wait(1.5f);
            Game.Config.autosave = false;
            ResetClock.NowOverride = () => Monday;
            DungeonAuthority.Current = new LocalDungeonAuthority(20250303);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(2f);
            while (Game.Session.Progression.Level < 15)
                Game.Session.Progression.AddXp(Game.Session.Progression.XpNeeded);
            DCheck("enter test dungeon", Game.Dungeon.Enter(DungeonDatabase.Get("gold_vein"), DungeonDifficulty.Normal));
            yield return Wait(3f);
            DCheck("dungeon ready", Game.Dungeon.RoomIsReady);
            var player = Game.Player;
            player.Health.SetInvulnerable(120f);
            var input = new ScriptedInput();
            player.Input = input;
            var origin = MobilityTestOrigin();
            player.Place(origin, Facing.Down);
            yield return Wait(.2f);

            // Offline pause still stops the clock and simulation.
            Game.Flow.Pause();
            float clock = Time.time;
            yield return Wait(.25f);
            DCheck("offline ESC freezes world", Time.timeScale == 0f && !Game.IsWorldRunning && Mathf.Approximately(clock, Time.time));

            // Starting a party behind a menu must also release the offline pause clock.
            var pair = LoopbackTransport.CreatePair();
            var net = PartyNet.BeginHost(pair.host, "pause-test", 0, null, 2);
            DCheck("host menu keeps simulation live", Game.IsWorldRunning && !Game.IsPlaying && Time.timeScale == 1f);
            pair.client.Send(0, NetChannel.Control, PartyWire.Build(w =>
            {
                w.Write(PartyMsg.Hello); w.Write("pause-test"); w.Write("test-peer");
                w.Write((byte)1); w.Write(""); MemberCard.Of(player, 1, "test-peer").Write(w);
            }));
            yield return Wait(.2f);
            var remote = net.MemberAt(1);
            DCheck("peer connects while host menu open", remote != null);
            if (remote == null) yield break;
            remote.Health.SetInvulnerable(120f);
            remote.Place(origin + Vector2.up, Facing.Down);
            remote.NetMoveTo(remote.Position, Facing.Down);
            var start = remote.Position;
            var target = start + Vector2.right * 1.5f;
            while (pair.client.TryReceive(out _)) { }
            input.Move = Vector2.right; input.PressAttack(); input.PressMobility(); input.PressSkill(0);
            float elapsed = Game.Dungeon.Run.Elapsed;
            clock = Time.time;
            var enemies = new Dictionary<EnemyController, Vector2>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead) enemies[e] = e.Position;
            pair.client.Send(0, NetChannel.MemberState, PartyWire.Build(w =>
            {
                w.Write((byte)1); PartyWire.WriteVec(w, target); w.Write((byte)Facing.Right); w.Write(remote.Data.Mana);
            }));
            var command = ActorCommand.None; command.attack = true;
            int remoteAttacks = 0;
            System.Action<PlayerController> attacked = p => { if (p == remote) remoteAttacks++; };
            PlayerCombat.AttackPressed += attacked;
            NetCommandRouter.SendCommand(pair.client, 0, 1, command, 1);
            var bolt = MagicBolt.Fire(player.gameObject, origin + Vector2.down, Vector2.right, CharacterClassInfo.Get(CharacterClass.Mage));
            var boltStart = bolt.transform.position;
            yield return Wait(.12f);
            DCheck("projectile advances behind menu", bolt == null || Vector3.Distance(boltStart, bolt.transform.position) > .1f);
            yield return Wait(1.4f);
            PlayerCombat.AttackPressed -= attacked;
            DCheck("host clock and dungeon timer advance", Time.time - clock > 1f && Game.Dungeon.Run.Elapsed - elapsed > 1f);
            DCheck("remote position interpolates while host paused", Vector2.Distance(remote.Position, target) < .2f && Vector2.Distance(remote.Position, start) > 1f);
            DCheck("remote attack executes while host paused", remoteAttacks > 0);
            bool enemyMoved = false;
            foreach (var kv in enemies)
                if (kv.Key != null && Vector2.Distance(kv.Value, kv.Key.Position) > .05f) enemyMoved = true;
            DCheck("monster AI advances behind menu", enemyMoved);
            DCheck("local menu commands suppressed", player.Command.move == Vector2.zero && !player.Command.attack && !player.Command.mobility && player.Command.skillSlot == -1 && Vector2.Distance(player.Position, origin) < .2f);
            int snapshots = 0; bool movedSnapshot = false;
            while (pair.client.TryReceive(out var packet))
            {
                if (packet.channel != NetChannel.Snapshot) continue;
                snapshots++;
                using (var reader = PartyWire.Reader(packet.data))
                {
                    reader.ReadUInt32(); reader.ReadByte(); int count = reader.ReadByte();
                    for (int i = 0; i < count; i++)
                    {
                        int slot = reader.ReadByte(); var position = PartyWire.ReadVec(reader);
                        reader.ReadByte(); reader.ReadInt32(); reader.ReadInt32();
                        if (slot == 1 && Vector2.Distance(position, target) < .2f) movedSnapshot = true;
                    }
                }
            }
            DCheck("host broadcasts changing snapshots behind menu", snapshots >= 5 && movedSnapshot);
            Game.UI.Push(Game.UI.Settings);
            yield return Wait(.15f);
            DCheck("nested settings keep world running", Game.IsWorldRunning && Time.timeScale == 1f);
            Game.Flow.OpenWindow(null);
            yield return Wait(.15f);
            DCheck("online inventory keeps world running", Game.State.Current == GameState.Inventory && Game.IsWorldRunning && Time.timeScale == 1f);
            Game.Flow.CloseInventory();
            input.Move = Vector2.zero;
            yield return Wait(.2f);
            DCheck("resume restores local gameplay", Game.IsPlaying && Game.IsWorldRunning && !player.Command.attack && !player.Command.mobility);

            // Test the member's snapshot receiver and outgoing command suppression separately.
            PartyNet.End();
            Game.Party.RemoveNetMember(remote);
            var memberPair = LoopbackTransport.CreatePair();
            var member = PartyNet.BeginMember(memberPair.client, "pause-test", 1, "test-peer", "");
            memberPair.host.Send(1, NetChannel.Control, PartyWire.Build(w =>
            {
                w.Write(PartyMsg.Welcome); w.Write(false); w.Write(""); w.Write((byte)0); w.Write((byte)0);
                w.Write((byte)1); MemberCard.Of(player, 0, "test-host").Write(w);
            }));
            yield return Wait(.2f);
            var puppet = member.MemberAt(0);
            DCheck("member handshake completes", member.Welcomed && puppet != null);
            if (puppet == null) yield break;
            puppet.Place(origin + Vector2.up, Facing.Down);
            puppet.NetMoveTo(puppet.Position, Facing.Down);
            target = puppet.Position + Vector2.right * 1.5f;
            while (memberPair.host.TryReceive(out _)) { }
            Game.Flow.Pause();
            input.Move = Vector2.right; input.PressAttack(); input.PressMobility(); input.PressSkill(0);
            memberPair.host.Send(1, NetChannel.Snapshot, PartyWire.Build(w =>
            {
                w.Write((uint)1); w.Write((byte)Game.Dungeon.CurrentRoom); w.Write((byte)1);
                w.Write((byte)0); PartyWire.WriteVec(w, target); w.Write((byte)Facing.Right);
                w.Write(puppet.Health.Current); w.Write(puppet.Health.Max); w.Write((short)0);
            }));
            yield return Wait(.5f);
            DCheck("member puppet moves behind ESC menu", Vector2.Distance(puppet.Position, target) < .2f);
            int positions = 0, commands = 0; bool clean = true;
            while (memberPair.host.TryReceive(out var packet))
            {
                if (packet.channel == NetChannel.MemberState) positions++;
                if (packet.channel != NetChannel.Input) continue;
                commands++;
                var cmd = NetCommand.Read(packet.data, 1).Unpack();
                clean &= cmd.move == Vector2.zero && !cmd.attack && !cmd.mobility && cmd.skillSlot == -1 && !cmd.useHealing && !cmd.useMana;
            }
            DCheck("member keeps sending positions and neutral commands", positions >= 3 && commands >= 3 && clean);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) yield return Shot("online_pause_menu");
            PartyNet.End();
            DCheck("ending online session restores offline menu pause", Time.timeScale == 0f && !Game.IsWorldRunning);
            input.Move = Vector2.zero;
            Game.Flow.Resume();
            yield return Wait(.2f);
            DCheck("offline resume still works", Game.IsPlaying && Time.timeScale == 1f);
            Log($"ONLINE PAUSE summary: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
