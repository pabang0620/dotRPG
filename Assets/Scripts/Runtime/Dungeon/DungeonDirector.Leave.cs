using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class DungeonDirector
    {
        bool leaving;

        public bool RunOver => run != null && run.IsOver;

        /// <summary>마을로 (dungeon HUD): an ended run goes straight home; a running one fails first, without the result window.</summary>
        public void LeaveToVillage()
        {
            if (run == null || busy) return;
            if (!run.IsOver)
            {
                leaving = true;
                ReviveOpen = false;
                Fail("던전에서 나왔습니다.");
                if (!run.IsOver) { leaving = false; return; } // could not end it right now (clearing)
            }
            leaving = false;
            ExitToVillage(false);
        }

        /// <summary>포기: the run fails.</summary>
        public void GiveUp()
        {
            if (!ReviveOpen) return;
            ReviveOpen = false;
            Fail("던전 공략을 포기했습니다.");
        }

        IEnumerator FailLater(string reason)
        {
            yield return new WaitForSecondsRealtime(FailDelay);
            Fail(reason);
        }

        void Fail(string reason)
        {
            if (run == null || run.IsOver || run.State == DungeonRunState.Clearing) return;
            if (NetHost)
            {
                PartyNet.Current.HostRunEnded(false, reason, run.Elapsed);
                PartyRunSession.Instance?.SendHostReport(false, run.Elapsed);
            }
            run.State = DungeonRunState.Failed;
            run.FailReason = reason;
            SnapshotDamage();
            run.Rank = DungeonRank.F;
            if (OnlineEconomy.On) OnlineEconomy.FinishDungeon(false, run.Elapsed, run.HitsTaken, run.MaxCombo, run.RevivesUsed, null); // [SERVER]
            EndRunState();
            Game.Audio.PlaySfx("player_down");
            RunEnded?.Invoke(run);
            ShowResult();
        }

        void SnapshotDamage()
        {
            run.MemberDamage.Clear();
            var party = Game.Party;
            if (party == null) return;
            foreach (var m in party.Members)
                if (m != null) run.MemberDamage.Add((m.DisplayName, party.DamageOf(m), m.IsLocal));
        }

        void ShowResult()
        {
            if (leaving) return; // 마을로: going home instead of the result window
            StartCoroutine(ShowResultRoutine());
        }

        IEnumerator ShowResultRoutine()
        {
            while (!Game.IsPlaying || busy) yield return null;
            // [BGM] Result jingle; 다시 도전 (LoadRoom) / leaving (ExitToVillage) switch back to the map's music.
            Game.Audio.PlayMusic(run.State == DungeonRunState.Cleared ? MapRegistry.MusicClear : MapRegistry.MusicFail);
            Game.UI.DungeonResult.Setup(run);
            Game.Flow.OpenWindow(Game.UI.DungeonResult);
        }

        // =============================== Result actions ===============================

        /// <summary>Flips card <paramref name="index"/> for the local player: its reward goes into the bag. Null if not allowed (a raid card already taken).</summary>
        public RewardCard? TakeCard(int index)
        {
            if (run == null || run.Cards == null || index < 0 || index >= run.Cards.Count) return null;
            if (run.CardsTakeAll)
            {
                // [RAID] Every card is taken once; a number already in the bag gives nothing.
                if ((run.TakenMask & (1 << index)) != 0) return null;
                run.TakenMask |= 1 << index;
            }
            var card = run.Cards[index];
            if (card.count > 0 && !string.IsNullOrEmpty(card.itemId)) Game.Session.Inventory.Add(card.itemId, card.count);
            return card;
        }

        /// <summary>Back to the village (and optionally straight into the select window).</summary>
        public void ExitToVillage(bool openSelect)
        {
            if (busy) return;
            if (Game.State.Current == GameState.Inventory) Game.Flow.CloseInventory();
            StartCoroutine(Transition(() =>
            {
                EndRunState();
                run = null;
                Game.Party?.SetDungeonCompanions(false); // back on the map: the mercenaries stay behind
                OnlineEconomy.LeaveDungeon(); // [SERVER]
                PartyRunSession.OnBackInVillage(); // [PARTY] the fight connection closes
                Game.Session.MapId = MapRegistry.Village;
                Game.World.Load(MapRegistry.Village);
                var local = Game.Player;
                local.Spawn(Game.World.PlayerSpawn, Facing.Down, int.MaxValue, Game.Session.PlayerMaxHealth);
                local.Data.Mana = local.MaxMana;
                Game.Camera.SetTarget(local.transform, true);
                GameEvents.RaiseMapEntered(MapRegistry.Village); // [STORY] back in town (story companion leaves, scenes queued there start)
                Game.Quest.NotifyChanged();
                Game.UI.Hud.RefreshAll();
                Game.Audio.PlayMusic(Game.World.Map.music);
                GameEvents.RaiseToast($"{Game.World.Map.displayName}");
                RoomChanged?.Invoke();
            }, openSelect ? (Action)(() => Game.UI.Dungeon.Open(false)) : null));
        }

        /// <summary>Title screen / new game while in a run: drop it without any transition.</summary>
        public void AbortRun()
        {
            StopAllCoroutines();
            busy = false;
            holdBlack = 0f;
            Game.UI?.Fader?.HideCard();
            ReviveOpen = false;
            EndRunState();
            run = null;
            Game.Party?.SetDungeonCompanions(false);
            OnlineEconomy.LeaveDungeon(); // [SERVER] an abandoned run is closed by the server later
            if (PartyRunSession.Active) PartyRunSession.Instance.Leave(); // [PARTY]
            PartyNet.End();
            if (Game.State.Current == GameState.Playing) Time.timeScale = 1f;
            RoomChanged?.Invoke();
        }

        /// <summary>Stops the run's live hooks (field rules back for the companions); the run data stays for the result.</summary>
        void EndRunState()
        {
            ReviveOpen = false;
            roomReady = false;
            if (door != null) door.Entered -= OnDoorEntered;
            Game.Party?.SetCompanionAutoRevive(true);
            HookLocal(null);
        }

        void HookLocal(PlayerController local)
        {
            if (hookedLocal != null) hookedLocal.Damaged -= OnLocalDamaged;
            hookedLocal = local != null ? local.Health : null;
            if (hookedLocal != null) hookedLocal.Damaged += OnLocalDamaged;
        }

        void OnLocalDamaged(DamageInfo info)
        {
            if (run != null && run.State == DungeonRunState.Playing) run.HitsTaken++;
        }

        /// <summary>[E4] How long the loading card stays on the black screen when a run starts.</summary>
        const float LoadingCardSeconds = 1.0f;
        float holdBlack;

        IEnumerator Transition(Action action, Action after = null)
        {
            if (busy) yield break;
            busy = true;
            var fader = Game.UI.Fader;
            yield return fader.Fade(1f, FadeSeconds);
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
            yield return null;
            if (holdBlack > 0f) { yield return new WaitForSecondsRealtime(holdBlack); holdBlack = 0f; }
            yield return fader.Fade(0f, FadeSeconds);
            fader.HideCard();
            busy = false;
            try { after?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
