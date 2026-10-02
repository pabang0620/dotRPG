using System;
using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [STORY] <c>-dotrpgStory</c>: a new game played through chapter 1 by signals (cutscenes skipped, talks
    /// finished, kills and interactions raised) checking quest states, flags, story characters, props and the
    /// raid rules. Report lines "STORY ... PASS|FAIL", last line "STORY summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        int storyPassed, storyFailed, storyErrors;

        void StoryCheck(string what, bool ok)
        {
            if (ok) storyPassed++;
            else storyFailed++;
            log?.WriteLine($"STORY {what} {(ok ? "PASS" : "FAIL")}");
        }

        void CountStoryErrors(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) storyErrors++;
        }

        static QuestStatus St(string id) => Game.Quest.StatusOf(id);

        /// <summary>Lets cutscenes play out (skipped) and conversations close until the world is playable again.</summary>
        IEnumerator SettleStory(float timeout = 20f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                if (Game.Cutscenes.IsPlaying) Game.Cutscenes.DevSkip();
                else if (Game.Dialogue.IsOpen) Game.Dialogue.Close();
                else if (Game.IsPlaying && !Game.Flow.IsTransitioning) { yield return Wait(0.3f); if (Game.IsPlaying && !Game.Cutscenes.IsPlaying) yield break; }
                yield return null;
            }
            Log("settle timeout: state=" + Game.State.Current + " cutscene=" + Game.Cutscenes.CurrentId);
        }

        /// <summary>Talks to <paramref name="npcId"/> the way NpcController does (dialogue choice + finish signal).</summary>
        void Talk(string npcId)
        {
            string id = Game.Quest.DialogueFor(npcId, npcId + "_idle", "");
            Log($"talk {npcId} -> {id}");
            Game.Quest.OnDialogueFinished(id, npcId);
        }

        bool HasStoryNpc(string npcId)
        {
            foreach (var n in Game.World.ObjectsRoot.GetComponentsInChildren<NpcController>())
                if (n.Definition != null && n.Definition.npcId == npcId) return true;
            return false;
        }

        int StoryProps(string sprite)
        {
            int n = 0;
            foreach (var p in Game.World.ObjectsRoot.GetComponentsInChildren<StoryProp>()) if (p.name == "Story_" + sprite) n++;
            return n;
        }

        IEnumerator StoryRun()
        {
            storyPassed = storyFailed = storyErrors = 0;
            Application.logMessageReceived += CountStoryErrors;
            yield return Wait(1f);
            StoryCheck($"quest data loaded: {Game.Quest.Database.All.Count} quests, chapter 1 '{Game.Quest.Database.Chapter(1)?.title}'", Game.Quest.Database.All.Count >= 13);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.5f);
            StoryCheck($"new game opens 1-1 ({St("c1_morning")}) and plays the intro ({Game.Cutscenes.CurrentId})",
                St("c1_morning") == QuestStatus.Active && Game.Cutscenes.IsPlaying);
            StoryCheck("bram, hanna, ria stand in the village before the attack", HasStoryNpc("bram") && HasStoryNpc("hanna") && HasStoryNpc("ria"));
            StoryCheck($"stable built ({Game.World.ObjectsRoot.Find("Stable") != null}), horse and trough props ({StoryProps("story_horse")}/{StoryProps("story_trough")})",
                Game.World.ObjectsRoot.Find("Stable") != null && StoryProps("story_horse") == 1 && StoryProps("story_trough") == 1);
            yield return SettleStory();
            StoryCheck($"intro done -> step 2 (state {Game.State.Current})", Game.Session.Journal.State("c1_morning").step == 1 && Game.IsPlaying);

            GameEvents.RaiseInteracted("feed_horse");
            GameEvents.RaiseInteracted("fill_trough");
            Talk("hanna");
            Talk("ria");
            yield return SettleStory();
            StoryCheck($"1-1 complete ({St("c1_morning")}), 1-2 offered by hanna ({St("c1_festival")}), mark={Game.Quest.MarkFor("hanna")}",
                St("c1_morning") == QuestStatus.Completed && St("c1_festival") == QuestStatus.Available && Game.Quest.MarkFor("hanna") == QuestMark.Available);
            StoryCheck("festival flag puts up the lanterns after a rebuild", Game.Quest.HasFlag("festival_eve"));

            Talk("hanna"); // accept
            StoryCheck($"1-2 accepted ({St("c1_festival")})", St("c1_festival") == QuestStatus.Active);
            Talk("merchant"); Talk("farmer"); Talk("chief");
            Talk("hanna"); Talk("ria");
            yield return SettleStory();
            StoryCheck($"1-2 complete, 1-3 runs ({St("c1_burning")})", St("c1_festival") == QuestStatus.Completed && St("c1_burning") == QuestStatus.Active);
            // The attack scene plays on its own (village); after it the plaza fight is on.
            float until = Time.realtimeSinceStartup + 5f;
            while (Game.Session.Journal.State("c1_burning").step == 0 && Time.realtimeSinceStartup < until) { if (Game.Cutscenes.IsPlaying) Game.Cutscenes.DevSkip(); yield return null; }
            yield return SettleStory();
            int skeletons = 0;
            foreach (var e in Game.World.ObjectsRoot.GetComponentsInChildren<EnemyController>()) if (e != null && !e.IsDead) skeletons++;
            StoryCheck($"attack night: flag={Game.Quest.HasFlag(StoryIds.FlagAttackNight)} skeletons in the plaza={skeletons}", Game.Quest.HasFlag(StoryIds.FlagAttackNight) && skeletons >= 5);
            for (int i = 0; i < 5; i++) GameEvents.RaiseEnemyKilled("skel_warrior");
            yield return SettleStory(40f);
            yield return Wait(1f);
            yield return SettleStory(40f);
            StoryCheck($"after the stable scene: 1-3 {St("c1_burning")}, flags attack_done={Game.Quest.HasFlag("attack_done")} stable_burned={Game.Quest.HasFlag("stable_burned")}",
                St("c1_burning") == QuestStatus.Completed && Game.Quest.HasFlag("attack_done") && Game.Quest.HasFlag("stable_burned") && !Game.Quest.HasFlag(StoryIds.FlagAttackNight));
            StoryCheck($"1-4 runs ({St("c1_ashes")}), graves={StoryProps("story_grave") + StoryProps("story_grave_1")}, kael={HasStoryNpc("kael")}, bram gone={!HasStoryNpc("bram")}",
                St("c1_ashes") == QuestStatus.Active && StoryProps("story_grave") + StoryProps("story_grave_1") == 2 && HasStoryNpc("kael") && !HasStoryNpc("bram"));
            StoryCheck($"villagers back after the night: chief={HasStoryNpc("chief")}", HasStoryNpc("chief"));
            GameEvents.RaiseInteracted("family_graves");
            yield return SettleStory(30f);
            StoryCheck($"1-4 complete ({St("c1_ashes")}), 1-5 offered by kael ({St("c1_rise")})", St("c1_ashes") == QuestStatus.Completed && St("c1_rise") == QuestStatus.Available);

            Talk("kael");
            for (int i = 0; i < 3; i++) GameEvents.RaiseEnemyKilled("skeleton");
            Talk("kael");
            yield return SettleStory();
            StoryCheck($"1-5 complete ({St("c1_rise")}), chief offers 1-6 ({St("c1_rebuild")}) with '{Game.Quest.DialogueFor("chief", "chief_intro", "")}'",
                St("c1_rise") == QuestStatus.Completed && St("c1_rebuild") == QuestStatus.Available && Game.Quest.DialogueFor("chief", "chief_intro", "") == "chief_intro");

            // [J8] 카엘 hunts with the party outside the towns and stays in the village as an NPC.
            Game.Flow.TravelTo(MapRegistry.Forest, true);
            yield return Wait(2.5f);
            yield return SettleStory();
            bool kaelInForest = Game.Party.Has(StoryCompanions.MercIdFor("kael"));
            Game.Flow.TravelTo(MapRegistry.Village, true);
            yield return Wait(2.5f);
            yield return SettleStory();
            StoryCheck($"kael joins outside town ({kaelInForest}) and leaves in the village ({!Game.Party.Has(StoryCompanions.MercIdFor("kael"))}), npc there={HasStoryNpc("kael")}",
                kaelInForest && !Game.Party.Has(StoryCompanions.MercIdFor("kael")) && HasStoryNpc("kael"));
            Talk("chief");
            Game.Quest.MarkWorkshopBuilt();
            for (int i = 0; i < 3; i++) GameEvents.RaiseEnemyKilled("skeleton");
            yield return SettleStory();
            StoryCheck($"1-6 ready to report ({St("c1_rebuild")})", St("c1_rebuild") == QuestStatus.ReadyToTurnIn);
            Talk("chief");
            StoryCheck($"1-6 complete, ruby ring in the bag ({Game.Session.Inventory.Count("eq_ring_ruby")})", St("c1_rebuild") == QuestStatus.Completed && Game.Session.Inventory.Count("eq_ring_ruby") > 0);

            StoryCheck($"raids locked by the story: '{DungeonDirector.RaidLockReason(DungeonDatabase.SkeletonKing)}'", DungeonDirector.RaidLockReason(DungeonDatabase.SkeletonKing) != null);
            // Rest of chapter 1 by signals.
            Talk("kael");
            GameEvents.RaiseInteracted("ria_ribbon");
            Talk("kael");
            StoryCheck($"1-7 complete ({St("c1_trail")})", St("c1_trail") == QuestStatus.Completed);
            Talk("kael");
            Game.Session.Progression.AddXp(200000);
            Game.Quest.DevSignal(ObjectiveTypes.Dungeon, "gold_vein", 12);
            yield return SettleStory();
            Talk("kael");
            StoryCheck($"1-8 complete ({St("c1_stronger")}) at level {Game.Session.Progression.Level}", St("c1_stronger") == QuestStatus.Completed);
            Talk("kael");
            StoryCheck($"mid raid opened by 1-9: lock='{DungeonDirector.RaidLockReason(DungeonDatabase.SkeletonKing)}'", DungeonDirector.RaidLockReason(DungeonDatabase.SkeletonKing) == null);
            Game.Quest.DevSignal(ObjectiveTypes.Raid, DungeonDatabase.Raid, 1);
            yield return SettleStory();
            Talk("kael");
            Talk("kael");
            Game.Quest.DevSignal(ObjectiveTypes.Raid, DungeonDatabase.RaidBargas, 1);
            yield return SettleStory(30f);
            Talk("chief");
            yield return SettleStory(30f);
            StoryCheck($"chapter 1 done: 1-10 {St("c1_bargas")}, 1-end {St("c1_road")}, flag={Game.Quest.HasFlag("chapter1_done")}",
                St("c1_bargas") == QuestStatus.Completed && St("c1_road") == QuestStatus.Completed && Game.Quest.HasFlag("chapter1_done"));

            // Chapter 2.
            StoryCheck($"2-1 runs ({St("c2_canyon")})", St("c2_canyon") == QuestStatus.Active);
            Game.Flow.TravelTo(MapRegistry.Canyon, true);
            yield return Wait(2.5f);
            yield return SettleStory();
            StoryCheck($"canyon: leona={HasStoryNpc("leona")} knights={HasStoryNpc("knight_dorn") && HasStoryNpc("knight_ivy") && HasStoryNpc("knight_mo")} kael={HasStoryNpc("kael")}",
                HasStoryNpc("leona") && HasStoryNpc("knight_mo") && HasStoryNpc("kael"));
            Talk("leona");
            Talk("leona");
            Game.Quest.DevSignal(ObjectiveTypes.Dungeon, "gold_vein", 3);
            yield return SettleStory();
            for (int i = 0; i < 12; i++) GameEvents.RaiseEnemyKilled("skel_shield");
            yield return SettleStory();
            Talk("knight_dorn"); Talk("knight_ivy"); Talk("knight_mo");
            yield return SettleStory();
            Talk("leona");
            yield return SettleStory(30f);
            StoryCheck($"2-2 complete ({St("c2_trial")}), 2-3 played ({St("c2_priest")}), orban offers 2-4 ({St("c2_mine")})",
                St("c2_trial") == QuestStatus.Completed && St("c2_priest") == QuestStatus.Completed && St("c2_mine") == QuestStatus.Available);
            Talk("orban");
            Game.Quest.DevSignal(ObjectiveTypes.Dungeon, "gold_vein", 5);
            yield return SettleStory(30f);
            Talk("kael");
            Game.Quest.DevSignal(ObjectiveTypes.Dungeon, "gold_vein", 15);
            yield return SettleStory(30f);
            StoryCheck($"2-4 {St("c2_mine")}, 2-5 {St("c2_growth")}", St("c2_mine") == QuestStatus.Completed && St("c2_growth") == QuestStatus.Completed);
            Talk("leona");
            Game.Quest.DevSignal(ObjectiveTypes.Raid, DungeonDatabase.RaidGolem, 1);
            yield return SettleStory();
            Talk("leona");
            Talk("orban");
            Game.Quest.DevSignal(ObjectiveTypes.Raid, DungeonDatabase.RaidGrah, 1);
            yield return SettleStory(40f);
            yield return Wait(1f);
            yield return SettleStory(40f);
            StoryCheck($"betrayal: flag={Game.Quest.HasFlag("kael_betrayed")} 2-7 {St("c2_grah")} 2-end {St("c2_end")} chapter2_done={Game.Quest.HasFlag("chapter2_done")}",
                Game.Quest.HasFlag("kael_betrayed") && St("c2_grah") == QuestStatus.Completed && St("c2_end") == QuestStatus.Completed && Game.Quest.HasFlag("chapter2_done"));

            Game.Flow.TravelTo(MapRegistry.Forest, true);
            yield return Wait(2.5f);
            yield return SettleStory();
            StoryCheck($"kael no longer joins after the betrayal ({!Game.Party.Has(StoryCompanions.MercIdFor("kael"))})", !Game.Party.Has(StoryCompanions.MercIdFor("kael")));
            // Raid rules.
            var now = new DateTime(2026, 10, 7, 12, 0, 0); // Wednesday
            var king = DungeonDatabase.SkeletonKing;
            var bargas = DungeonDatabase.Get(DungeonDatabase.RaidBargas);
            StoryCheck($"mid raid opens Wed/Sat/Sun: wed={ResetClock.IsOpen(king, now)} thu={ResetClock.IsOpen(king, now.AddDays(1))} sat={ResetClock.IsOpen(king, now.AddDays(3))}",
                ResetClock.IsOpen(king, now) && !ResetClock.IsOpen(king, now.AddDays(1)) && ResetClock.IsOpen(king, now.AddDays(3)));
            StoryCheck($"final raid only on Sunday: sun={ResetClock.IsOpen(bargas, now.AddDays(4))} sat={ResetClock.IsOpen(bargas, now.AddDays(3))}",
                ResetClock.IsOpen(bargas, now.AddDays(4)) && !ResetClock.IsOpen(bargas, now.AddDays(3)));
            var p = new DungeonProgress();
            p.ClaimRaid(king, now);
            StoryCheck($"mid raid pays once a day: same day={p.RaidRewardAvailable(king, now)} saturday={p.RaidRewardAvailable(king, now.AddDays(3))} this week={p.RaidClearsThisWeek(king, now.AddDays(3))}",
                !p.RaidRewardAvailable(king, now) && p.RaidRewardAvailable(king, now.AddDays(3)));
            p.ClaimRaid(bargas, now.AddDays(4));
            StoryCheck($"final raid pays once a week: next day={p.RaidRewardAvailable(bargas, now.AddDays(5))} next thursday={p.RaidRewardAvailable(bargas, now.AddDays(8))}",
                !p.RaidRewardAvailable(bargas, now.AddDays(5)) && p.RaidRewardAvailable(bargas, now.AddDays(8)));

            // Save and load keep the journal.
            Game.Flow.SaveGame();
            yield return Wait(0.5f);
            var data = Game.Saves.Read();
            StoryCheck($"save v{data?.version} keeps quests ({data?.quests?.Count}) and flags ({data?.storyFlags?.Count})",
                data != null && data.version == SaveData.CurrentVersion && data.quests.Count >= 6 && data.storyFlags.Contains("attack_done"));
            var old = new SaveData { version = 4, quest = new QuestProgress { stage = (int)QuestStage.Active, woodDelivered = 2, skeletonsDefeated = 1 } };
            SaveSystem.Migrate(old);
            StoryCheck($"v4 save migrates: prologue done, workshop active ({old.quests.Count} quests)",
                old.quests.Exists(q => q.id == "c1_rise" && q.status == (int)QuestStatus.Completed) && old.quests.Exists(q => q.id == QuestManager.WorkshopQuest && q.status == (int)QuestStatus.Active));

            StoryCheck($"no errors logged during the run ({storyErrors})", storyErrors == 0);
            Application.logMessageReceived -= CountStoryErrors;
            log?.WriteLine($"STORY summary: {storyPassed} passed, {storyFailed} failed");
        }
    }
}
