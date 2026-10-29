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
                if (Follower) { GameEvents.RaiseToast(run.InBossRoom ? L.T("부활할 수 없습니다. 파티원을 응원하세요.") : L.T("부활할 수 없습니다. 다음 방에서 다시 일어납니다.")); return true; }
                // [REVIVE 24] A host never fails the party on its own: the wipe clock (10 s for anyone's coin) decides.
                if (NetHost) { GameEvents.RaiseToast(run.InBossRoom ? L.T("부활할 수 없습니다. 파티원을 응원하세요.") : L.T("부활할 수 없습니다. 다음 방에서 다시 일어납니다.")); return true; }
                StartCoroutine(FailLater("부활 횟수를 모두 사용했습니다.")); // i18n-ignore raw reason goes to party members; shown through L.T(FailReason)
                return true;
            }
            ReviveOpen = true;
            reviveClock = 0f;
            reviveUntil = ReviveSeconds;
            ReviveCoins.Refresh();
            Game.Audio.PlaySfx("cancel");
            return true;
        }

        /// <summary>
        /// [REVIVE] The host is down and another person is still up: the run goes on (any room; the living member walks
        /// the party through the door and the host rises in the next room like a member does).
        /// </summary>
        bool HostWaits => NetHost && run != null && PartyNet.Current.AnyHumanUp;

        // ---------------- [REVIVE 24] wipe ----------------

        /// <summary>[REVIVE 24] Every person down (AI do not count): this long for someone to use a coin, then the run fails.</summary>
        public const float WipeSeconds = 10f;
        /// <summary>[REVIVE 24] A key press opens the coin only after this long, so attack keys mashed while falling spend nothing.</summary>
        const float ReviveInputGrace = 1f;
        bool wipeActive;
        /// <summary>[REVIVE 24] Seconds left before the wiped party is sent out (0 = no wipe). The host's clock in a party run.</summary>
        public float WipeRemaining { get; private set; }

        /// <summary>[REVIVE 24] Every frame: solo or host judges the wipe; a member counts down the host's clock for the display.</summary>
        void UpdateWipe()
        {
            if (run == null || run.State != DungeonRunState.Playing) { wipeActive = false; WipeRemaining = 0f; return; }
            if (Follower)
            {
                if (wipeActive && Game.IsWorldRunning) WipeRemaining = Mathf.Max(0f, WipeRemaining - Time.deltaTime);
                return; // the host fails the run (RunEnd)
            }
            bool allDown = NetHost ? !PartyNet.Current.AnyHumanUp : Game.Player != null && Game.Player.IsDead;
            if (!allDown)
            {
                if (wipeActive) SetWipe(false, 0f);
                return;
            }
            if (!Game.IsWorldRunning) return;
            if (!wipeActive) SetWipe(true, WipeSeconds);
            if (ReviveCoins.Busy) return; // a coin being paid must not time out
            WipeRemaining -= Time.deltaTime;
            if (WipeRemaining > 0f) return;
            SetWipe(false, 0f);
            ReviveOpen = false;
            Fail("파티원이 모두 쓰러졌습니다."); // i18n-ignore raw reason goes to party members; shown through L.T(FailReason)
        }

        void SetWipe(bool on, float seconds)
        {
            wipeActive = on;
            WipeRemaining = on ? seconds : 0f;
            if (NetHost) PartyNet.Current.HostSendWipe(WipeRemaining);
            if (on) OpenReviveForWipe();
        }

        /// <summary>[REVIVE 24] Host's wipe clock on a member PC (0 = over: someone got up).</summary>
        public void FollowWipe(float seconds)
        {
            if (run == null || run.State != DungeonRunState.Playing) return;
            wipeActive = seconds > 0f;
            WipeRemaining = Mathf.Max(0f, seconds);
            if (wipeActive) OpenReviveForWipe();
        }

        /// <summary>[REVIVE 24] The wipe gives everyone who still has a revive the coin window for the whole countdown.</summary>
        void OpenReviveForWipe()
        {
            var local = Game.Player;
            if (local == null || !local.IsDead || run.RevivesLeft <= 0) return;
            if (!ReviveOpen)
            {
                ReviveOpen = true;
                reviveClock = 0f;
                reviveUntil = WipeSeconds;
                ReviveCoins.Refresh();
                return;
            }
            reviveUntil = Mathf.Max(reviveUntil, reviveClock + WipeSeconds);
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
                // [REVIVE 24] Only the revive key (Enter) after a short grace or the 부활 button spends a coin: attack and
                // interact presses (mashed while falling, or held by auto-hunting) never do.
                if (input.SubmitPressed && reviveClock >= ReviveInputGrace) { AcceptRevive(); return; }
                if (input.CancelPressed) { if (Follower || HostWaits) { ReviveOpen = false; return; } GiveUp(); return; }
            }
            if (reviveClock >= reviveUntil)
            {
                ReviveOpen = false;
                if (Follower || HostWaits || wipeActive) { GameEvents.RaiseToast(L.T("부활하지 않았습니다.")); return; } // [REVIVE 24] the wipe clock decides
                Fail("부활하지 않았습니다. (시간 초과)"); // i18n-ignore raw reason goes to party members; shown through L.T(FailReason)
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
            if (!ReviveCoins.CanUse) { GameEvents.RaiseToast(L.T("부활 코인이 없습니다. (매일 06:00에 1개 지급)")); Game.Audio.PlaySfx("cancel"); return false; }
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
            GameEvents.RaiseToast(ReviveCoins.Free ? L.F("부활했습니다! (남은 부활 {0})", run.RevivesLeft) : L.F("부활했습니다! (남은 부활 {0} · 부활 코인 {1})", run.RevivesLeft, ReviveCoins.Coins));
            RoomChanged?.Invoke();
        }
    }
}
