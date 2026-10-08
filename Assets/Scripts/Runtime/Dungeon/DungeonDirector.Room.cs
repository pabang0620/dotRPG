using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class DungeonDirector
    {
        // =============================== Rooms ===============================

        void LoadRoom(int index, bool first)
        {
            roomReady = false;
            roomCleared = false;
            bosses.Clear();
            door = null;
            run.RoomIndex = index;
            OnlineEconomy.RoomIndex = index; // [SERVER] kill reports carry the room
            var room = run.Room;
            Game.Dialogue.Abort();
            CompanionBrain.DangerZones.Clear();
            CompanionBrain.PriorityTarget = null;
            CompanionBrain.PriorityTargetAlt = null;
            Game.World.Load(room.mapId);

            // Party at the entry: the local player on 'P', companions around it (downed ones rejoin at half HP).
            var local = Game.Player;
            Vector2 entry = Game.World.PlayerSpawn;
            var party = Game.Party;
            if (first && local.IsDead)
            {
                // 다시 도전 after a failed run: back on the feet at the entry.
                if (party != null) party.ReviveMember(local, 1f);
                else local.Revive(1f);
                local.Data.Mana = local.MaxMana;
            }
            local.Place(entry, Facing.Up);
            if (party != null)
                for (int i = 1; i < party.Members.Count; i++)
                {
                    var m = party.Members[i];
                    if (m == null) continue;
                    if (m.IsDead) party.ReviveMember(m, first ? 1f : CompanionRejoinHp);
                    m.Place(party.SpotFor(i - 1), Facing.Up);
                }
            party?.Regroup(RoomRegroupSeconds);

            if (!Follower) SpawnMonsters(room); // [PARTY NET] member PCs get the host's monsters as puppets
            if (!room.isBoss) door = DungeonDoor.Create(Game.World.DungeonDoorCells, Game.World.ObjectsRoot);
            if (door != null) door.Entered += OnDoorEntered;

            Game.Camera.SetTarget(local.transform, true);
            Game.UI.Hud.RefreshAll();
            Game.Audio.PlayMusic(Game.World.Map.music);
            if (room.isBoss) GameEvents.RaiseToast($"보스 방 · {run.Dungeon.bossName}");
            roomReady = true;
            if (NetHost) PartyNet.Current.HostRoomLoaded(index);
            RoomChanged?.Invoke();
        }

        void SpawnMonsters(RoomDef room)
        {
            var cells = Game.World.DungeonSpawns;
            foreach (var g in room.groups)
            {
                var spots = new List<Vector2>();
                foreach (var (digit, pos) in cells) if (digit == g.digit) spots.Add(pos);
                if (spots.Count == 0) spots.Add(Game.World.PlayerSpawn + Vector2.up * 5f);
                int level = DungeonMonsters.LevelFor(run.Numbers, g);
                for (int i = 0; i < g.count; i++)
                {
                    Vector2 p = spots[i % spots.Count];
                    // More monsters than cells: spread them around the cell.
                    if (i >= spots.Count)
                    {
                        float a = i * 2.39996f;
                        Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.8f;
                        if (Game.World.IsFree(q)) p = q;
                    }
                    var e = DungeonMonsters.Spawn(g.monsterId, p, Game.World.ObjectsRoot, run.HpMul, run.DamageMul, level);
                    if (e == null) continue;
                    run.Monsters++;
                    if (g.isBoss) bosses.Add(e);
                    var h = e.GetComponent<Health>();
                    if (h != null) h.Damaged += OnMonsterDamaged;
                }
            }
        }

        void OnMonsterDamaged(DamageInfo info)
        {
            if (run == null || run.State != DungeonRunState.Playing) return;
            var m = info.AttackerMember;
            if (m != null && m.IsLocal) run.RegisterHit(Time.time);
            if (m != null && NetHost) PartyNet.Current.HostRegisterHit(m, info.amount);
        }

        void OnEnemyKilled(EnemyController e, int xp)
        {
            if (run != null && run.State != DungeonRunState.Failed && roomReady) run.Kills++;
        }

        static bool Alive(EnemyController e) => e != null && !e.IsDead && e.isActiveAndEnabled;

        void Update()
        {
            if (run == null) return;
            if (run.State == DungeonRunState.Playing && Game.IsWorldRunning && !busy) run.Elapsed += Time.deltaTime;
            if (ReviveOpen) UpdateRevive();
            if (!roomReady || busy || run.State != DungeonRunState.Playing) return;
            if (Follower) return; // [PARTY NET] the host says when a room or the run is cleared

            if (run.InBossRoom)
            {
                bool bossAlive = false;
                foreach (var b in bosses) if (Alive(b)) bossAlive = true;
                if (!bossAlive && bosses.Count > 0) StartCoroutine(ClearRoutine());
                else if (bosses.Count == 0 && !AnyAlive()) StartCoroutine(ClearRoutine());
                return;
            }
            if (!roomCleared && !AnyAlive()) OnRoomCleared();
        }

        static bool AnyAlive()
        {
            foreach (var e in EnemyController.Active) if (Alive(e)) return true;
            return false;
        }

        void OnRoomCleared()
        {
            if (NetHost) PartyNet.Current.HostRoomCleared();
            roomCleared = true;
            run.ClearedRooms.Add(run.RoomIndex);
            if (door != null) door.Open();
            Game.Audio.PlaySfx("build_complete");
            GameEvents.RaiseToast("방을 정리했습니다! 문이 열렸습니다.");
            RoomChanged?.Invoke();
        }

        void OnDoorEntered()
        {
            if (run == null || busy || run.State != DungeonRunState.Playing || Follower) return;
            int next = run.Room.next != null && run.Room.next.Length > 0 ? run.Room.next[0] : run.RoomIndex + 1;
            if (next >= run.RoomCount) return;
            Game.Audio.PlaySfx("confirm");
            StartCoroutine(Transition(() => LoadRoom(next, false)));
        }

        /// <summary>Automated checks: go through the open gate as if the player walked in.</summary>
        public bool DevEnterDoor()
        {
            if (door == null || !door.IsOpen) return false;
            OnDoorEntered();
            return true;
        }
    }
}
