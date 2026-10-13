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
        // =====================================================================================================
        // [TRAILER] -dotrpgTrailer <folder> [-scene hunt,awaken,dungeon,grah,king]: plays real game scenes by itself and
        // saves every frame (30 fps, fixed game time) as JPG under <folder>/clips/<scene>/f0000.jpg for the 1-minute
        // promotion video (Docs/TRAILER_2026-10-13.md, cut with Tools/video/make_trailer.sh). Offline, own saves.
        //   hunt    : 자동 사냥 with three AI companions in a Lv30 hunting ground
        //   awaken  : fighter 천검귀일 and arcanist 유성우 on a real pack
        //   dungeon : a weekday dungeon room with the AI party
        //   grah    : 수호자 그라흐 - 결계 의식 line, circles filled by the party, groggy; then 파동
        //   king    : 해골왕 - 망자의 심판 broken by a burst ("심판 저지! 그로기"), the kill and the raid reward cards
        // =====================================================================================================

        const int TrailerFps = 30;
        /// <summary>-clean 1: the HUD stays hidden while recording (ad shots; the captions come from the edit).</summary>
        static bool trailerClean;
        static readonly string[] TrailerMercs = { "merc_bron", "merc_kai", "merc_elin" };

        IEnumerator TrailerRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave = false;
            AudioListener.volume = 0f;
            QuestAutoPilot.MeasureAnywhere = true; // 자동 사냥 inside dungeon / raid runs too (as the measure mode does)
            trailerClean = MeasureArg("-clean", "0") == "1";
            string value = MeasureArg("-scene", "hunt,awaken,dungeon,grah,king");
            foreach (var scene in value.Split(',').Select(s => s.Trim()))
            {
                Log("scene " + scene);
                switch (scene)
                {
                    case "hunt": yield return TrailerHunt(); break;
                    case "awaken": yield return TrailerAwaken(); break;
                    case "dungeon": yield return TrailerDungeon(); break;
                    case "grah": yield return TrailerGrah(); break;
                    case "king": yield return TrailerKing(); break;
                    default: Log("unknown scene " + scene); break;
                }
                QuestAutoPilot.Stop("");
                QuestAutoPilot.MeasureSkilled = false;
                QuestAutoPilot.MeasureAware = false;
                Time.timeScale = 1f;
            }
            QuestAutoPilot.MeasureAnywhere = false;
        }

        /// <summary>A strong Lv40 hero of <paramref name="career"/> (legendary +5 weapon) with the three AI companions.</summary>
        IEnumerator TrailerHero(Career career, int level = 40)
        {
            var cls = CareerCatalog.Base(career);
            yield return MeasureHero(cls, career, level, level >= 35 ? "un" : "e"); // unique gear from Lv35 (legendary armour does not exist)
            var p = Game.Session.Progression;
            for (int stage = 0; stage < 5; stage++) p.AdvanceAwakening(stage);
            var party = Game.Party;
            party.ClearCompanions();
            foreach (var m in TrailerMercs) party.AddCompanion(m);
            yield return Wait(.5f);
            Game.Player.HealFull();
        }

        /// <summary>Plays on (auto hunt keeps going) for <paramref name="seconds"/> of game time while saving every frame.</summary>
        IEnumerator TrailerRecord(string clip, float seconds, Action<int> onFrame = null)
        {
            string dir = Path.Combine(folder, "clips", clip);
            Directory.CreateDirectory(dir);
            var frame = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            int frames = Mathf.CeilToInt(seconds * TrailerFps);
            Time.timeScale = 1f;
            Time.captureFramerate = TrailerFps;
            for (int f = 0; f < frames; f++)
            {
                if (trailerClean && Game.UI.Hud != null && Game.UI.Hud.gameObject.activeSelf) Game.UI.Hud.gameObject.SetActive(false);
                onFrame?.Invoke(f);
                TrailerKeepAlive();
                yield return new WaitForEndOfFrame();
                frame.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"f{f:0000}.jpg"), frame.EncodeToJPG(92));
                yield return null;
            }
            Time.captureFramerate = 0;
            Destroy(frame);
            if (trailerClean && Game.UI.Hud != null) Game.UI.Hud.gameObject.SetActive(true);
            Log($"clip {clip}: {frames} frames");
        }

        /// <summary>Keeps the hero hunting and on its feet (a run's revive, or a plain one in the field).</summary>
        static void TrailerKeepAlive()
        {
            var p = Game.Player;
            if (p == null) return;
            if (p.IsDead)
            {
                var dir = Game.Dungeon;
                if (dir != null && dir.InRun) { if (dir.ReviveOpen) dir.DevRevive(); }
                else p.Revive(1f, 2f);
                return;
            }
            MeasureEnsureHunting();
        }

        /// <summary>Plays (not recorded) for <paramref name="seconds"/> of game time, a little faster.</summary>
        IEnumerator TrailerPlay(float seconds, float scale = 2f, Func<bool> until = null)
        {
            float t0 = Time.time;
            Time.timeScale = scale;
            while (Time.time - t0 < seconds && (until == null || !until()))
            {
                MeasureScale(scale);
                TrailerKeepAlive();
                yield return null;
            }
            Time.timeScale = 1f;
        }

        static string TrailerZone(int level) => HuntingGrounds.All.OrderBy(z => Mathf.Abs(z.monsterLevel - level)).First().id;

        // ---------------------------------------------------------------- scenes

        IEnumerator TrailerHunt()
        {
            yield return TrailerHero(Career.Fighter, 32);
            Game.Flow.TravelTo(TrailerZone(30), true);
            yield return Wait(.3f);
            while (Game.Flow.IsTransitioning) yield return null;
            yield return TrailerPlay(8f);
            yield return TrailerRecord("hunt", 10f);
        }

        IEnumerator TrailerAwaken()
        {
            foreach (var career in new[] { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop })
            {
                yield return TrailerHero(career);
                Game.Flow.TravelTo(TrailerZone(36), true);
                yield return Wait(.3f);
                while (Game.Flow.IsTransitioning) yield return null;
                // Walk in until a pack is around the hero, then the awakening.
                yield return TrailerPlay(20f, 2f, () => EnemyController.Active.Count(e => e != null && !e.IsDead && Vector2.Distance(e.Position, Game.Player.Position) < 4.5f) >= 4);
                var player = Game.Player;
                var s = CareerCatalog.For(career).First(k => k.kind == CareerSkillKind.Awakening);
                var numbers = player.Data.Stats.Skill(player.Class, 4, s.Gem, new SkillGem[0]);
                QuestAutoPilot.Stop("");
                yield return TrailerRecord("awaken_" + career.ToString().ToLowerInvariant(), career == Career.Arcanist ? 8.5f : 5.5f,
                    f => { if (f == 8) StartCoroutine(CareerCombat.For(player).Cast(s, numbers)); });
            }
        }

        /// <summary>Enters <paramref name="def"/> through the select screen (as a player does); false when it would not open.</summary>
        IEnumerator TrailerEnter(DungeonDef def, DungeonDifficulty diff, bool[] ok)
        {
            ResetClock.NowOverride = () => new DateTime(2026, 10, 18, 12, 0, 0); // a Sunday: every raid and dungeon is open
            if (!string.IsNullOrEmpty(def.unlockQuest)) Game.Session.Journal.State(def.unlockQuest).status = (int)QuestStatus.Active;
            if (def.keyCost > 0) Game.Session.Inventory.Add(DungeonDatabase.SealKey, def.keyCost);
            for (int below = 0; below < (int)diff; below++) Game.Session.Dungeons.RecordClear(def.id, (DungeonDifficulty)below, DungeonRank.A);
            var select = Game.UI.Dungeon;
            select.Open(false);
            yield return Wait(.3f);
            bool entered = select.DevSelect(def.id, diff) && select.DevEnter();
            yield return Wait(1f);
            while (Game.Flow.IsTransitioning) yield return null;
            ok[0] = entered && Game.Dungeon.Run != null;
            if (!ok[0]) { Log($"trailer: {def.id} did not open '{select.DevStatus}'"); select.Close(); }
            QuestAutoPilot.MeasureSkilled = true;
        }

        /// <summary>Clears rooms (not recorded) until the boss room.</summary>
        IEnumerator TrailerToBoss()
        {
            var dir = Game.Dungeon;
            float t0 = Time.time;
            while (dir.InRun && !dir.Run.InBossRoom && Time.time - t0 < 400f)
            {
                MeasureScale(3f);
                TrailerKeepAlive();
                if (!Game.Flow.IsTransitioning && !EnemyController.Active.Any(e => e != null && !e.IsDead && e.isActiveAndEnabled)) dir.DevEnterDoor();
                yield return null;
            }
            Time.timeScale = 1f;
            while (Game.Flow.IsTransitioning) yield return null;
        }

        static EnemyController TrailerBoss() => EnemyController.Active.FirstOrDefault(e => e != null && !e.IsDead && e.IsBoss);

        static void TrailerSetHp(EnemyController boss, float ratio)
        {
            int target = Mathf.RoundToInt(boss.Health.Max * ratio);
            if (boss.Health.Current > target) boss.Health.Drain(boss.Health.Current - target);
        }

        IEnumerator TrailerLeaveRun()
        {
            var dir = Game.Dungeon;
            if (dir.InRun && dir.Run != null && !dir.Run.IsOver) { dir.LeaveToVillage(); yield return Wait(1f); }
            else yield return Wait(DungeonDirector.SlowMotionSeconds + DungeonDirector.ClearHoldSeconds + 1f);
            for (int tries = 0; tries < 5 && dir.InRun; tries++)
            {
                dir.ExitToVillage(false);
                yield return Wait(1f);
                while (Game.Flow.IsTransitioning) yield return null;
            }
            if (Game.UI.Top != null) Game.Flow.CloseInventory();
            ResetClock.NowOverride = null;
        }

        IEnumerator TrailerDungeon()
        {
            yield return TrailerHero(Career.Guardian, 27);
            var ok = new bool[1];
            yield return TrailerEnter(DungeonDatabase.Weekday.First(), DungeonDifficulty.Hero, ok);
            if (!ok[0]) yield break;
            yield return TrailerPlay(3f, 1f);
            yield return TrailerRecord("dungeon", 9f);
            yield return TrailerLeaveRun();
        }

        IEnumerator TrailerGrah()
        {
            yield return TrailerHero(Career.Fighter);
            var ok = new bool[1];
            yield return TrailerEnter(DungeonDatabase.Raids.First(r => r.id == DungeonDatabase.RaidGrah), DungeonDifficulty.Normal, ok);
            if (!ok[0]) yield break;
            yield return TrailerToBoss();
            yield return TrailerPlay(4f, 1f);
            var boss = TrailerBoss();
            if (boss == null) { Log("trailer: no 그라흐"); yield return TrailerLeaveRun(); yield break; }
            var brain = boss.Behaviour as BossBrain;
            // Just above the 65% mark: the next hits start 결계 의식 (line, circles, the party steps on, groggy).
            TrailerSetHp(boss, .66f);
            yield return TrailerRecord("grah_seal", 17f);
            yield return TrailerPlay(2f, 1f, () => brain == null || !brain.Busy);
            if (brain != null && !boss.IsDead) brain.DevPlay(PatternKind.Repel);
            yield return TrailerRecord("grah_repel", 4.5f);
            yield return TrailerLeaveRun();
        }

        IEnumerator TrailerKing()
        {
            yield return TrailerHero(Career.Fighter);
            var ok = new bool[1];
            yield return TrailerEnter(DungeonDatabase.Raids.First(r => r.id == DungeonDatabase.Raid), DungeonDifficulty.Normal, ok);
            if (!ok[0]) yield break;
            yield return TrailerToBoss();
            yield return TrailerPlay(3f, 1f);
            var boss = TrailerBoss();
            if (boss == null) { Log("trailer: no 해골왕"); yield return TrailerLeaveRun(); yield break; }
            var brain = boss.Behaviour as BossBrain;
            // Phase 3 (enrage), then 망자의 심판 broken by the burst of a player who knows it.
            TrailerSetHp(boss, .34f);
            QuestAutoPilot.MeasureAware = true;
            yield return TrailerPlay(12f, 1f, () => brain != null && brain.CastingJudgment);
            yield return TrailerRecord("king_judgment", 11f);
            // The kill, the slow motion and the raid reward cards.
            if (!boss.IsDead) TrailerSetHp(boss, .02f);
            var dir = Game.Dungeon;
            yield return TrailerPlay(20f, 1f, () => boss == null || boss.IsDead);
            yield return TrailerRecord("king_kill", 4f);
            yield return TrailerPlay(30f, 1f, () => Game.UI.Top != null || dir.Run == null || dir.Run.IsOver);
            // The result window, then every raid card flips (as the "모두 뒤집기" button does).
            yield return TrailerRecord("king_reward", 8f, f => { if (f == 60) Game.UI.DungeonResult?.DevFlipAll(); });
            yield return TrailerLeaveRun();
        }
    }
}
