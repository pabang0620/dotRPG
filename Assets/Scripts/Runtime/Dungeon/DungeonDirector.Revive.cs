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
            if (run.RevivesLeft <= 0)
            {
                // [REVIVE] No revive left: a member waits for the next room; the host stays down in the boss room while
                // another person fights on (elsewhere only the host can lead through the door, so the run ends).
                if (Follower) { GameEvents.RaiseToast(run.InBossRoom ? "부활할 수 없습니다. 파티원을 응원하세요." : "부활할 수 없습니다. 다음 방에서 다시 일어납니다."); return true; }
                if (HostWaits) { GameEvents.RaiseToast("부활할 수 없습니다. 파티원을 응원하세요."); return true; }
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

        /// <summary>[REVIVE] The host is down in the boss room and another person is still up: the run goes on.</summary>
        bool HostWaits => NetHost && run != null && run.InBossRoom && PartyNet.Current.AnyHumanUp;
        float allDownFor;

        void UpdateRevive()
        {
            if (run == null || run.State != DungeonRunState.Playing) { ReviveOpen = false; return; }
            // [REVIVE] Party run: it fails once every person is down and nobody revived for a while (AI cannot finish it).
            if (NetHost && Game.IsWorldRunning)
            {
                allDownFor = PartyNet.Current.AnyHumanUp || ReviveOpen ? 0f : allDownFor + Time.deltaTime;
                if (allDownFor > ReviveSeconds + 2f) { allDownFor = 0f; Fail("파티원이 모두 쓰러졌습니다."); return; }
            }
            if (Game.Player != null && !Game.Player.IsDead) { ReviveOpen = false; return; }
            if (!Game.IsWorldRunning) return;
            if (ReviveCoins.Busy) return; // [REVIVE] the countdown waits for the coin answer (a paid coin must not time out)
            reviveClock += Time.deltaTime;
            var input = Game.Input;
            if (Game.IsPlaying && !Game.State.ChangedThisFrame && input != null)
            {
                if (input.SubmitPressed || input.InteractPressed || input.AttackPressed) { AcceptRevive(); return; }
                if (input.CancelPressed) { if (Follower || HostWaits) { ReviveOpen = false; return; } GiveUp(); return; }
            }
            if (reviveClock >= reviveUntil)
            {
                ReviveOpen = false;
                if (Follower || HostWaits) { GameEvents.RaiseToast("부활하지 않았습니다."); return; }
                Fail("부활하지 않았습니다. (시간 초과)");
            }
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

        /// <summary>[MEASURE] Dev-only: the revive a player would buy with a coin, without the coin.</summary>
        internal void DevRevive() => DoRevive();

        void DoRevive()
        {
            var local = Game.Player;
            // The coin is already paid here: revive whenever the run is still going and the player is still down.
            if (run == null || run.State != DungeonRunState.Playing || local == null || !local.IsDead || run.RevivesLeft <= 0) return;
            ReviveOpen = false;
            if (Follower)
            {
                // [REVIVE] The host checks the seat's and the party's revives and stands the body up (HP snapshot).
                PartyNet.Current.MemberRequestRevive();
                return;
            }
            run.RevivesUsed++;
            run.PartyRevivesUsed++;
            if (NetHost)
            {
                PartyNet.Current.HostRevived(local);
                PartyNet.Current.HostSendReviveState(PartyNet.Current.MySlot);
            }
            if (Game.Party != null) Game.Party.ReviveMember(local, 1f, ReviveInvulnerable);
            else local.Revive(1f, ReviveInvulnerable);
            local.Data.Mana = local.MaxMana;
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast(ReviveCoins.Free ? $"부활했습니다! (남은 부활 {run.RevivesLeft})" : $"부활했습니다! (남은 부활 {run.RevivesLeft} · 부활 코인 {ReviveCoins.Coins})");
            RoomChanged?.Invoke();
        }
    }
}
