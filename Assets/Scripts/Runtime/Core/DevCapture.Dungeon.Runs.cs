using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator FailedRunChecks(DateTime clock)
        {
            var dir = Game.Dungeon;
            var select = Game.UI.Dungeon;
            select.Open(false);
            yield return Wait(0.5f);
            select.DevSelect("gold_vein", DungeonDifficulty.Normal);
            select.DevEnter();
            yield return Wait(1.4f);
            var local = Game.Player;
            var run = dir.Run;
            yield return KillMember(local);
            yield return Wait(0.4f);
            bool prompt = dir.ReviveOpen;
            dir.GiveUp();
            yield return Wait(0.8f);
            var result = Game.UI.DungeonResult;
            DCheck($"failed via death + 포기: prompt={prompt} state={run.State} reason='{run.FailReason}' top={Game.UI.Top?.name} stamp={result.DevStamp} cards={(run.Cards == null ? 0 : run.Cards.Count)} done={result.DevDone} autoRevive={Game.Party.CompanionAutoRevive}",
                prompt && run.State == DungeonRunState.Failed && Game.UI.Top == result && result.DevStamp == "실패" && run.Cards == null && result.DevDone && Game.Party.CompanionAutoRevive);
            yield return Wait(0.4f);
            yield return Shot("dgn_12_failed");
            // 다시 도전 (one entry left).
            int left = Game.Session.Dungeons.EntriesLeft(clock);
            result.DevRetry();
            yield return Wait(1.4f);
            run = dir.Run;
            DCheck($"retry: entries {left}->{Game.Session.Dungeons.EntriesLeft(clock)} new run={run != null && run.State == DungeonRunState.Playing} room={run?.RoomIndex} alive={!local.IsDead} hp={local.Health.Current}/{local.Health.Max} map={Game.World.MapId}",
                left == 1 && Game.Session.Dungeons.EntriesLeft(clock) == 0 && run != null && run.State == DungeonRunState.Playing && run.RoomIndex == 0 && !local.IsDead && Game.World.MapId == MapRegistry.DgnCanyon1);
            // No revives left → failure without a prompt.
            run.RevivesUsed = run.Numbers.revives;
            yield return KillMember(local);
            yield return Wait(0.3f);
            bool noPrompt = !dir.ReviveOpen;
            yield return Wait(DungeonDirector.FailDelay + 0.6f);
            DCheck($"no revives left -> failed: noPrompt={noPrompt} state={run.State} reason='{run.FailReason}' top={Game.UI.Top?.name}",
                noPrompt && run.State == DungeonRunState.Failed && Game.UI.Top == result);
            yield return Shot("dgn_13_failed_no_revive");
            result.DevLeave(false);
            yield return Wait(1.4f);
        }

        IEnumerator RaidChecks(Func<DateTime> now)
        {
            // [RAID] The mid raid opens with quest 1-9 and only on 수·토·일: play it on the Wednesday of the test week.
            Game.Session.Journal.State("c1_fortress").status = (int)QuestStatus.Active;
            var clockBefore = ResetClock.NowOverride;
            ResetClock.NowOverride = () => now().AddDays(2);
            var dir = Game.Dungeon;
            var select = Game.UI.Dungeon;
            var progress = Game.Session.Dungeons;
            // Enter from the forest with autosave on: the save points to the village.
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(1.6f);
            Game.Config.autosave = true;
            select.Open(true);
            yield return Wait(0.5f);
            select.DevSelect(DungeonDatabase.Raid, DungeonDifficulty.Normal);
            bool entered = select.DevEnter();
            Game.Config.autosave = false;
            yield return Wait(1.4f);
            var saved = Game.Saves.Read();
            var run = dir.Run;
            DCheck($"raid entry: ok={entered} map={Game.World.MapId} size={run?.PartySize} locked={run?.RewardsLocked} save map={saved?.mapId} pos=({saved?.playerX:0},{saved?.playerY:0}) entriesUsed={progress.EntriesUsed(now())}",
                entered && Game.World.MapId == MapRegistry.DgnRaid1 && run != null && run.PartySize == 4 && !run.RewardsLocked && saved != null && saved.mapId == MapRegistry.Village && saved.playerX < 0f && progress.EntriesUsed(now()) == 3);
            var local = Game.Player;
            for (int room = 0; room < 2; room++)
            {
                yield return ClearDungeonRoom(local, false);
                yield return Wait(0.3f);
                dir.DevEnterDoor();
                yield return Wait(1.4f);
            }
            yield return Shot("dgn_15_raid_boss");
            yield return KillAllEnemies(local, 0.05f);
            yield return Wait(DungeonDirector.SlowMotionSeconds + DungeonDirector.ClearHoldSeconds + 1f);
            var result = Game.UI.DungeonResult;
            // [RAID] The four cards are all taken, one by one (a legendary card's staging takes up to ~2 s).
            bool rp = true;
            for (int i = 0; i < 4; i++)
            {
                rp &= result.PlayerPick(i);
                yield return Wait(2.4f);
            }
            DCheck($"raid clear: pick={rp} state={run.State} cards={run.Cards?.Count} takeAll={run.CardsTakeAll} gold={run.RaidGold} raidAvail={progress.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now())} done={result.DevDone}",
                run.State == DungeonRunState.Cleared && run.Cards != null && run.Cards.Count == 4 && run.CardsTakeAll && run.RaidGold >= 3000 && run.RaidGold <= 4500
                && !progress.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now()) && result.DevDone);
            yield return Shot("dgn_16_raid_result");
            result.DevLeave(false);
            yield return Wait(1.4f);
            // Practice run: no rewards this week; quit to the title mid-run and continue in the village.
            select.Open(true);
            yield return Wait(0.4f);
            select.DevSelect(DungeonDatabase.Raid, DungeonDifficulty.Normal);
            Game.Config.autosave = true; // the save before entering now holds this week's claim
            select.DevEnter();
            Game.Config.autosave = false;
            yield return Wait(1.4f);
            run = dir.Run;
            bool locked = run != null && run.RewardsLocked;
            Game.Flow.ReturnToTitle();
            yield return Wait(1.4f);
            bool aborted = !dir.InRun && Game.Party.CompanionAutoRevive;
            Game.Flow.ContinueGame();
            yield return Wait(1.6f);
            DCheck($"raid practice + quit mid-run: locked={locked} aborted={aborted} continue map={Game.World.MapId} inRun={dir.InRun} raidAvail={Game.Session.Dungeons.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now())}",
                locked && aborted && Game.World.MapId == MapRegistry.Village && !dir.InRun && !Game.Session.Dungeons.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now()));
            ResetClock.NowOverride = clockBefore;
        }

        IEnumerator SoloRunChecks(DateTime clock)
        {
            var party = Game.Party;
            party.ClearCompanions();
            yield return Wait(0.4f);
            var select = Game.UI.Dungeon;
            select.Open(false);
            yield return Wait(0.5f);
            select.DevSelect("smelter", DungeonDifficulty.Normal);
            yield return Wait(0.2f);
            yield return Shot("dgn_17_select_tuesday");
            bool entered = select.DevEnter();
            yield return Wait(1.4f);
            var dir = Game.Dungeon;
            var run = dir.Run;
            var local = Game.Player;
            DCheck($"solo entry: ok={entered} size={run?.PartySize} hpMul={run?.HpMul:0.0} members={party.Count} map={Game.World.MapId}",
                entered && run != null && run.PartySize == 1 && Mathf.Approximately(run.HpMul, 1f) && party.Count == 1 && Game.World.MapId == MapRegistry.DgnCanyon2);
            for (int room = 0; room < 3; room++)
            {
                yield return ClearDungeonRoom(local, room == 0);
                yield return Wait(0.3f);
                dir.DevEnterDoor();
                yield return Wait(1.4f);
            }
            yield return KillAllEnemies(local, 0.05f);
            yield return Wait(DungeonDirector.SlowMotionSeconds + DungeonDirector.ClearHoldSeconds + 1f);
            var result = Game.UI.DungeonResult;
            result.PlayerPick(3);
            yield return Wait(1.6f);
            var who = Enumerable.Range(0, 4).Select(i => Strip(result.DevWho(i))).ToList();
            DCheck($"solo clear: state={run.State} rank={run.Rank} meters={run.MemberDamage.Count} who=[{string.Join(",", who)}] done={result.DevDone}",
                run.State == DungeonRunState.Cleared && run.MemberDamage.Count == 1 && who[3] == "나" && who.Count(w => w == "미획득") == 3 && result.DevDone);
            yield return Shot("dgn_18_solo_result");
            result.DevLeave(false);
            yield return Wait(1.4f);
            DCheck($"solo back in village: map={Game.World.MapId} inRun={dir.InRun} entries={Game.Session.Dungeons.EntriesLeft(clock)}/3",
                Game.World.MapId == MapRegistry.Village && !dir.InRun && Game.Session.Dungeons.EntriesLeft(clock) == 2);
        }
    }
}
