using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class DungeonDirector
    {
        /// <summary>
        /// Called by <see cref="GameFlow.OnPlayerDied"/>. True when the dungeon takes over (coin countdown or
        /// failure) instead of the normal game over.
        /// </summary>
        public bool HandleLocalDeath()
        {
            if (run == null) return false;
            if (run.State != DungeonRunState.Playing) return true;
            if (Follower)
            {
                // [PARTY NET] Downed members get up at the next room (the host's rule for every member).
                GameEvents.RaiseToast("쓰러졌습니다. 다음 방에서 다시 일어납니다.");
                return true;
            }
            if (run.RevivesLeft <= 0)
            {
                StartCoroutine(FailLater("부활 횟수를 모두 사용했습니다."));
                return true;
            }
            ReviveOpen = true;
            reviveClock = 0f;
            reviveUntil = ReviveSeconds;
            ReviveCoins.Refresh();
            Game.Audio.PlaySfx("cancel");
            return true;
        }

        void UpdateRevive()
        {
            if (run == null || run.State != DungeonRunState.Playing) { ReviveOpen = false; return; }
            if (Game.Player != null && !Game.Player.IsDead) { ReviveOpen = false; return; }
            if (!Game.IsWorldRunning) return;
            if (ReviveCoins.Busy) return; // [REVIVE] the countdown waits for the coin answer (a paid coin must not time out)
            reviveClock += Time.deltaTime;
            var input = Game.Input;
            if (Game.IsPlaying && !Game.State.ChangedThisFrame && input != null)
            {
                if (input.SubmitPressed || input.InteractPressed || input.AttackPressed) { AcceptRevive(); return; }
                if (input.CancelPressed) { GiveUp(); return; }
            }
            if (reviveClock >= reviveUntil) { ReviveOpen = false; Fail("부활하지 않았습니다. (시간 초과)"); }
        }

        /// <summary>
        /// 부활: spends one of the run's revives and (from Lv.11) one revive coin; full HP / MP where the player fell
        /// and 3 s of invulnerability. The coin is confirmed first, so the countdown keeps running while it is asked.
        /// </summary>
        public bool AcceptRevive()
        {
            var local = Game.Player;
            if (!ReviveOpen || run == null || local == null || !local.IsDead || run.RevivesLeft <= 0 || ReviveCoins.Busy) return false;
            if (!ReviveCoins.CanUse) { GameEvents.RaiseToast("부활 코인이 없습니다. (매일 06:00에 1개 지급)"); Game.Audio.PlaySfx("cancel"); return false; }
            ReviveCoins.Use("dungeon", (ok, why) =>
            {
                if (!ok) { GameEvents.RaiseToast(why); Game.Audio.PlaySfx("cancel"); return; }
                DoRevive();
            });
            return true;
        }

        void DoRevive()
        {
            var local = Game.Player;
            // The coin is already paid here: revive whenever the run is still going and the player is still down.
            if (run == null || run.State != DungeonRunState.Playing || local == null || !local.IsDead || run.RevivesLeft <= 0) return;
            ReviveOpen = false;
            run.RevivesUsed++;
            if (NetHost) PartyNet.Current.HostRevived(local);
            if (Game.Party != null) Game.Party.ReviveMember(local, 1f, ReviveInvulnerable);
            else local.Revive(1f, ReviveInvulnerable);
            local.Data.Mana = local.MaxMana;
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast(ReviveCoins.Free ? $"부활했습니다! (남은 부활 {run.RevivesLeft})" : $"부활했습니다! (남은 부활 {run.RevivesLeft} · 부활 코인 {ReviveCoins.Coins})");
            RoomChanged?.Invoke();
        }
    }
}
