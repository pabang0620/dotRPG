using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class DungeonDirector
    {
        // =============================== [PARTY NET] Member PC follows the host ===============================

        int followLoading = -1;

        /// <summary>The host started (or is already in) a run: same dungeon here, at the host's room.</summary>
        public void EnterFollower(DungeonDef dungeon, DungeonDifficulty difficulty, int room)
        {
            if (run != null && !run.IsOver && run.Dungeon == dungeon) return;
            if (run != null) EndRunState();
            if (Game.State.Current == GameState.Inventory) Game.Flow.CloseInventory();
            followLoading = room;
            StartRun(dungeon, difficulty, ResetClock.Now, room);
        }

        /// <summary>The host moved to room <paramref name="index"/>.</summary>
        public void FollowRoom(int index)
        {
            if (run == null || run.IsOver) return;
            if (index == followLoading || (index == run.RoomIndex && (roomReady || busy))) return;
            followLoading = index;
            StartCoroutine(Transition(() => LoadRoom(index, false)));
        }

        public void FollowRoomCleared()
        {
            if (run == null || run.State != DungeonRunState.Playing || roomCleared) return;
            roomCleared = true;
            run.ClearedRooms.Add(run.RoomIndex);
            if (door != null) door.Open();
            Game.Audio.PlaySfx("build_complete");
            GameEvents.RaiseToast("방을 정리했습니다! 방장이 문으로 이동하면 함께 넘어갑니다.");
            RoomChanged?.Invoke();
        }

        /// <summary>The host ended the run; this PC reports its own result with the host's counts.</summary>
        public void FollowEnd(bool cleared, string reason, float elapsedSeconds, MemberRunStats mine)
        {
            if (run == null || run.IsOver || run.State == DungeonRunState.Clearing) return;
            if (cleared) Pickup.CollectAll(); // [DUNGEON] leftover drops go into the bag
            run.Elapsed = elapsedSeconds;
            run.HitsTaken = mine.hitsTaken;
            run.MaxCombo = mine.maxCombo;
            run.RevivesUsed = mine.revives;
            if (cleared) StartCoroutine(ClearRoutine());
            else Fail(string.IsNullOrEmpty(reason) ? "파티가 던전 공략에 실패했습니다." : reason);
        }
    }
}
