using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class DungeonDirector
    {
        // =============================== Clear ===============================

        IEnumerator ClearRoutine()
        {
            run.State = DungeonRunState.Clearing;
            run.ClearedRooms.Add(run.RoomIndex);
            // Nobody dies to a stray hit during the celebration.
            if (Game.Party != null)
                foreach (var m in Game.Party.AliveMembers) m.Health.SetInvulnerable(SlowMotionSeconds + ClearHoldSeconds + 3f);
            ClearBanner?.Invoke();
            Game.Audio.PlaySfx("dungeon_clear"); // [H3]
            Game.Camera?.Shake(0.15f, 0.3f);
            if (!Game.IsOnlineWorld && Game.IsPlaying) Time.timeScale = SlowMotionScale;
            yield return new WaitForSecondsRealtime(SlowMotionSeconds);
            Game.State.RefreshTimeScale();
            // The rest of the room falls with its master (retried: a monster hit a moment ago is briefly invulnerable).
            float holdEnd = Time.realtimeSinceStartup + ClearHoldSeconds;
            while (AnyAlive() && Time.realtimeSinceStartup < holdEnd)
            {
                foreach (var e in new List<EnemyController>(EnemyController.Active))
                    if (Alive(e)) e.TakeDamage(new DamageInfo(e.GetComponent<Health>().Current + 99999, e.Position, 0f, Team.Player));
                yield return null;
            }
            float rest = holdEnd - Time.realtimeSinceStartup;
            if (rest > 0f) yield return new WaitForSecondsRealtime(rest);
            while (!Game.IsWorldRunning || busy) yield return null;
            FinishCleared();
        }

        void FinishCleared()
        {
            Pickup.CollectAll(); // [DUNGEON] leftover drops go into the bag
            if (NetHost)
            {
                PartyNet.Current.HostRunEnded(true, null, run.Elapsed);
                PartyRunSession.Instance?.SendHostReport(true, run.Elapsed);
            }
            if (OnlineEconomy.On && OnlineEconomy.RunId != null) { StartCoroutine(FinishClearedOnline()); return; }
            var auth = DungeonAuthority.Current;
            run.State = DungeonRunState.Cleared;
            SnapshotDamage();
            run.Score = auth.ScoreRun(run);
            run.Rank = run.Score.Rank;
            var now = ResetClock.Now;
            Progress.RecordClear(run.Dungeon.id, run.Difficulty, run.Rank);
            if (!run.RewardsLocked)
            {
                run.XpGained = auth.ClearXp(run, run.Rank);
                if (run.XpGained > 0) Game.Session.Progression.AddXp(run.XpGained);
                run.Cards = auth.DealCards(run, Game.Player.Class);
                if (run.Dungeon.isRaid)
                {
                    Progress.ClaimRaid(run.Dungeon, now);
                    PayRaidKeys(run.Dungeon);
                }
            }
            EndRunState();
            RunEnded?.Invoke(run);
            ShowResult();
        }

        /// <summary>
        /// [SERVER] The server checks the run, decides rank and clear XP (already in its delta) and keeps the four
        /// cards hidden until one is picked. The local score is shown only if the server has none.
        /// </summary>
        IEnumerator FinishClearedOnline()
        {
            run.State = DungeonRunState.Cleared;
            SnapshotDamage();
            run.Score = DungeonAuthority.Current.ScoreRun(run);
            run.Rank = run.Score.Rank;
            busy = true;
            bool answered = false;
            Dictionary<string, object> data = null;
            OnlineEconomy.FinishDungeon(true, run.Elapsed, run.HitsTaken, run.MaxCombo, run.RevivesUsed, d => { data = d; answered = true; });
            float waited = 0f, limit = PartyNet.Active ? OnlineEconomy.PartyResultWaitSeconds + 15f : 15f;
            if (PartyNet.Active) GameEvents.RaiseToast("다른 파티원의 결과를 확인하는 중...");
            while (!answered && waited < limit) { waited += Time.unscaledDeltaTime; yield return null; }
            busy = false;
            string result = MiniJson.Str(data, "result");
            if (result == "cleared")
            {
                var score = MiniJson.Obj(data, "score");
                if (score != null)
                    run.Score = new RankScore
                    {
                        time = MiniJson.Int(score, "time"), hits = MiniJson.Int(score, "hits"), kills = MiniJson.Int(score, "kills"),
                        combo = MiniJson.Int(score, "combo"), revivePenalty = MiniJson.Int(score, "revive_penalty"),
                    };
                if (Enum.TryParse(MiniJson.Str(data, "rank", ""), out DungeonRank rank)) run.Rank = rank;
                run.XpGained = MiniJson.Int(data, "granted_xp");
                // Face-down placeholders: the server reveals them on the pick.
                int count = MiniJson.Int(data, "card_count");
                run.Cards = count > 0 ? new List<RewardCard>(new RewardCard[count]) : null;
                Progress.RecordClear(run.Dungeon.id, run.Difficulty, run.Rank);
                // [ANTI-ABUSE] A party daily dungeon pays nothing to a member who barely fought.
                if (data.TryGetValue("reward_locked", out var pl) && pl is bool partyLocked && partyLocked)
                    GameEvents.RaiseToast(RaidLockText(MiniJson.Str(data, "reward_lock_reason")));
                var raid = MiniJson.Obj(data, "raid");
                if (raid != null && raid.TryGetValue("reward_locked", out var rl) && rl is bool locked && locked)
                    GameEvents.RaiseToast(RaidLockText(MiniJson.Str(raid, "lock_reason")));
                else if (run.Dungeon.isRaid)
                {
                    Progress.ClaimRaid(run.Dungeon, ResetClock.Now); // the server paid this period's reward
                    int cores = MiniJson.Int(raid, "core_gain");
                    if (cores > 0) GameEvents.RaiseToast($"고대의 핵 +{cores} (대장간에서 장비 승급에 씁니다)");
                }
            }
            else
            {
                // Held for review or not answered: no reward on this screen.
                run.XpGained = 0;
                run.Cards = null;
                run.NoRewardNote = result == "held" ? "held" : "noanswer";
                GameEvents.RaiseToast(result == "held" ? "결과를 확인하는 중입니다. 보상은 확인 후 지급됩니다." : "서버에 결과를 보내지 못했습니다.");
            }
            EndRunState();
            RunEnded?.Invoke(run);
            ShowResult();
        }

        /// <summary>[SERVER] Flips card <paramref name="index"/> on the server; done(own card or null). All four cards are filled in.</summary>
        public void TakeCardOnline(int index, Action<RewardCard?> done)
        {
            var current = run;
            OnlineEconomy.PickCard(index, (own, all) =>
            {
                if (current != null && all != null && current.Cards != null)
                    for (int i = 0; i < all.Count && i < current.Cards.Count; i++) current.Cards[i] = all[i];
                done?.Invoke(own);
            });
        }

        /// <summary>[RAID] Mid raids drop seal key fragments; final raids take the fragments they cost.</summary>
        void PayRaidKeys(DungeonDef raid)
        {
            var bag = Game.Session.Inventory;
            if (raid.keyCost > 0)
            {
                bag.Remove(DungeonDatabase.SealKey, Mathf.Min(raid.keyCost, bag.Count(DungeonDatabase.SealKey)));
                GameEvents.RaiseToast($"봉인 열쇠 조각 {raid.keyCost}개가 빛을 잃었습니다.");
            }
            if (raid.keyMax > 0)
            {
                int keys = UnityEngine.Random.Range(raid.keyMin, raid.keyMax + 1);
                bag.Add(DungeonDatabase.SealKey, keys);
                GameEvents.RaiseToast($"봉인 열쇠 조각 +{keys} (보유 {bag.Count(DungeonDatabase.SealKey)})");
            }
            int cores = PromoteRules.CoreGain(raid, run != null ? run.Difficulty : DungeonDifficulty.Normal);
            if (cores > 0)
            {
                bag.Add(DungeonDatabase.RaidCore, cores);
                GameEvents.RaiseToast($"고대의 핵 +{cores} (대장간에서 장비 승급에 씁니다)");
            }
        }

        // =============================== Death / revive / failure ===============================
    }
}
