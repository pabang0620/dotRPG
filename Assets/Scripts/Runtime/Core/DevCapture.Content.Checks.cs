using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        // =============================== -dotrpgDungeon additions ===============================

        IEnumerator ContentChecks(Action<DateTime> setClock)
        {
            var dir = Game.Dungeon;
            var hud = Game.UI.Hud;
            SetupHero(15, true);
            yield return Wait(0.6f);

            // ---------- Data: every RoomDef group has map cells, every room map has its 'P' entry ----------
            var dataErrors = DungeonValidation.Validate();
            DCheck($"room data: errors={dataErrors.Count} [{string.Join(" | ", dataErrors)}]", dataErrors.Count == 0);

            // ---------- Field boss: bar centred, clear of party frames and the quest panel ----------
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(1.6f);
            var local = Game.Player;
            var fieldBoss = MonsterDatabase.Spawn("boss_gold_foreman", local.Position + Vector2.up * 4f, Game.World.ObjectsRoot, 1f, 1f, 5);
            yield return Wait(1.0f);
            string over1080 = BossBarOverlaps();
            bool bound = BossHpBarView.Instance != null && BossHpBarView.Instance.Current == fieldBoss;
            yield return Shot("dgn_20_field_boss_1080");
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return Wait(0.8f);
            string over720 = BossBarOverlaps();
            yield return Shot("dgn_21_field_boss_720");
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return Wait(0.8f);
            DCheck($"field boss bar: bound={bound} quest={hud.DevQuestVisible} overlaps1080='{over1080}' overlaps720='{over720}'",
                bound && hud.DevQuestVisible && over1080 == "" && over720 == "");
            yield return KillAllEnemies(local, 0.05f);
            yield return Wait(1.0f);
            Game.Flow.TravelTo(MapRegistry.Village);
            yield return Wait(1.6f);

            // ---------- Each weekday dungeon: themed monsters + boss, bar bound in the boss room ----------
            var bossIds = new[] { "boss_gold_foreman", "boss_mine_captain", "boss_lich", "boss_archer_chief", "boss_armory_warden" };
            var themed = new[] { "skel_gold", "skel_miner", "skel_necro", "skel_archer", "skel_shield" };
            for (int d = 0; d < 5; d++)
            {
                setClock(WeekdayClock(d));
                // Earlier checks spent today's entries: start each themed run with a fresh daily count.
                Game.Session.Dungeons.DevClearEntries();
                var def = DungeonDatabase.Weekday[d];
                if (!roomKillHooked) { roomKillHooked = true; GameEvents.EnemyKilled += id => roomKills.Add(id); }
                bool entered = dir.Enter(def, DungeonDifficulty.Normal);
                yield return Wait(1.4f);
                var run = dir.Run;
                local = Game.Player;
                var missing = new List<string>();
                var seen = new HashSet<string>();
                roomKills.Clear();
                bool questHidden = true;
                for (int room = 0; entered && run != null && room < def.RoomCount; room++)
                {
                    var alive = DungeonEnemies().Where(e => e.Summoner == null && e.Def != null).Select(e => e.Def.id).ToList();
                    alive.AddRange(roomKills); // companions may already have killed some before the check
                    roomKills.Clear();
                    foreach (var id in alive) seen.Add(id);
                    foreach (var g in def.rooms[room].groups)
                        if (alive.Count(id => id == g.monsterId) < g.count) missing.Add($"r{room}:{g.monsterId}");
                    questHidden &= !hud.DevQuestVisible;
                    if (room == def.bossRoom)
                    {
                        var boss = DungeonEnemies().FirstOrDefault(e => e.IsBoss);
                        yield return Wait(0.8f);
                        string over = BossBarOverlaps();
                        bool barBound = boss != null && BossHpBarView.Instance != null && BossHpBarView.Instance.Current == boss;
                        if (d == 0 || d == 4) yield return Shot($"dgn_2{2 + d / 4}_boss_{def.id}");
                        string roomOver720 = "";
                        if (d == 0)
                        {
                            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                            yield return Wait(0.8f);
                            roomOver720 = BossBarOverlaps();
                            yield return Shot("dgn_24_boss_room_720");
                            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                            yield return Wait(0.8f);
                        }
                        DCheck($"{def.id} boss room: boss={boss?.Def?.id} bar={barBound} quest hidden={!hud.DevQuestVisible} overlaps='{over}' 720='{roomOver720}'",
                            boss != null && boss.Def.id == bossIds[d] && barBound && !hud.DevQuestVisible && over == "" && roomOver720 == "");
                        yield return KillAllEnemies(local, 0.05f);
                        break;
                    }
                    yield return ClearDungeonRoom(local, false);
                    yield return Wait(0.3f);
                    dir.DevEnterDoor();
                    yield return Wait(1.4f);
                }
                DCheck($"{def.id} themed monsters: entered={entered} seen=[{string.Join(",", seen.OrderBy(s => s))}] missing=[{string.Join(",", missing)}] questHidden={questHidden}",
                    entered && missing.Count == 0 && seen.Contains(themed[d]) && seen.Contains(bossIds[d]) && questHidden);
                yield return LeaveResult();
                DCheck($"{def.id} back: quest tracker shown again={hud.DevQuestVisible} map={Game.World.MapId}", hud.DevQuestVisible && Game.World.MapId == MapRegistry.Village);
            }

            // ---------- Raid: full clear by scripted play (Lv27, 4 members), phases, weekly lock ----------
            // [RAID] The mid raid opens with quest 1-9 on 수·토·일: Wednesday, the day its reward was already taken.
            var now = WeekdayClock(2);
            setClock(now);
            Game.Session.Journal.State("c1_fortress").status = (int)QuestStatus.Active;
            SetupHero(27, true);
            foreach (var m in Game.Party.Members) if (m != null && m.IsDead) Game.Party.ReviveMember(m, 1f);
            foreach (var m in Game.Party.Members) m?.HealFull();
            yield return Wait(0.6f);
            bool claimedBefore = !Game.Session.Dungeons.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now);
            bool rEntered = dir.Enter(DungeonDatabase.SkeletonKing, DungeonDifficulty.Normal);
            yield return Wait(1.4f);
            var raid = dir.Run;
            bool locked = raid != null && raid.RewardsLocked;
            var ids = DungeonEnemies().Where(e => e.Def != null).Select(e => e.Def.id).Distinct().OrderBy(s => s).ToList();
            var res = new SimResult();
            if (rEntered) yield return ScriptedRun(res, RaidCheckTimeScale, SimCapSeconds);
            DCheck($"raid scripted clear: entered={rEntered} room1=[{string.Join(",", ids)}] cleared={res.cleared} time={res.seconds:0}s revives={res.revives} kingPhase={res.maxPhase}/3 totemRevives={KingTotemRevives()} timeScale={RaidCheckTimeScale}",
                rEntered && res.cleared && res.maxPhase >= 3 && ids.Contains("skel_knight"));
            yield return Shot("dgn_25_raid_scripted_end");
            DCheck($"raid weekly lock honoured: claimedBefore={claimedBefore} practiceLocked={locked} cards={raid?.Cards?.Count ?? 0} stillClaimed={!Game.Session.Dungeons.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now)}",
                rEntered && raid != null && raid.Dungeon.isRaid && claimedBefore && locked && (raid.Cards == null || raid.Cards.Count == 0) && raid.XpGained == 0
                && !Game.Session.Dungeons.RaidRewardAvailable(DungeonDatabase.SkeletonKing, now));
            yield return LeaveResult();
        }

        // =============================== -dotrpgBalance ===============================

        IEnumerator BalanceRun()
        {
            balPassed = balFailed = 0;
            string set = "all";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dotrpgBalanceSet") set = args[i + 1];
            Game.Config.autosave = false;
            DateTime clock = WeekdayClock(0);
            ResetClock.NowOverride = () => clock;
            DungeonAuthority.Current = new LocalDungeonAuthority(20250303);
            yield return Wait(1.5f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.6f);
            log?.WriteLine($"BAL time scale {SimTimeScale}x, times are game seconds; set={set}");

            if (set == "all" || set == "normal")
            {
                SetupHero(5, false);
                yield return Wait(0.6f);
                // Same level for every run: clear XP from the previous dungeon must not carry over.
                for (int d = 0; d < 5; d++) { HoldLevel(5); yield return BalanceOne(DungeonDatabase.Weekday[d], DungeonDifficulty.Normal, d, v => clock = v); }
            }
            if (set == "curve")
            {
                // Difficulty curve: two dungeons per difficulty at its recommended level.
                foreach (var diff in new[] { DungeonDifficulty.Normal, DungeonDifficulty.Adventure, DungeonDifficulty.King, DungeonDifficulty.Hero })
                {
                    int lv = DungeonDatabase.Difficulty(diff).recommendedLevel;
                    foreach (int d in new[] { 0, 4 }) { HoldLevel(lv); yield return BalanceOne(DungeonDatabase.Weekday[d], diff, d, v => clock = v); }
                }
            }
            if (set == "all" || set == "hero" || set == "raid") SetupHero(27, true);
            if (set == "all" || set == "hero")
            {
                yield return Wait(0.6f);
                for (int d = 0; d < 5; d++) { HoldLevel(27); yield return BalanceOne(DungeonDatabase.Weekday[d], DungeonDifficulty.Hero, d, v => clock = v); }
            }
            if (set == "all" || set == "raid")
            {
                Game.Session.Journal.State("c1_fortress").status = (int)QuestStatus.Active; // [RAID] story opens the raid
                yield return Wait(0.6f);
                HoldLevel(27);
                yield return BalanceOne(DungeonDatabase.SkeletonKing, DungeonDifficulty.Normal, 2, v => clock = v); // Wednesday: mid raids are 수·토·일
            }
            ResetClock.NowOverride = null;
            log?.WriteLine($"BAL summary: {balPassed} passed, {balFailed} failed");
        }

        /// <summary>Back to exactly <paramref name="level"/> (passives are not used by the balance runs).</summary>
        void HoldLevel(int level)
        {
            var prog = Game.Session.Progression;
            if (prog.Level == level && prog.Xp == 0) return;
            prog.Reset(Game.Player.Class);
            SetupHero(level, level >= 20);
        }

        IEnumerator BalanceOne(DungeonDef def, DungeonDifficulty diff, int day, Action<DateTime> setClock)
        {
            setClock(WeekdayClock(day));
            var progress = Game.Session.Dungeons;
            progress.Reset();
            for (var d = DungeonDifficulty.Normal; d < diff; d++) progress.RecordClear(def.id, d, DungeonRank.A);
            var party = Game.Party;
            foreach (var m in party.Members) if (m != null && m.IsDead) party.ReviveMember(m, 1f);
            foreach (var m in party.Members) m?.HealFull();
            bool entered = Game.Dungeon.Enter(def, diff);
            yield return Wait(1.4f);
            var res = new SimResult();
            if (entered) yield return ScriptedRun(res, SimTimeScale, SimCapSeconds);
            var numbers = DungeonDatabase.DifficultyFor(def, diff);
            float lo = def.isRaid ? RaidMinSeconds : DungeonMinSeconds, hi = def.isRaid ? RaidMaxSeconds : DungeonMaxSeconds;
            int lv = Game.Session.Progression.Level;
            BCheck($"{def.id} {numbers.name} Lv{lv} x{party.Count}: entered={entered} cleared={res.cleared} time={res.seconds:0}s ({DungeonRun.Clock(res.seconds)}) revives={res.revives}/{numbers.revives} rank={res.rank} kingPhase={res.maxPhase} target={lo:0}-{hi:0}s",
                entered && res.cleared && res.seconds >= lo && res.seconds <= hi && res.revives < numbers.revives);
            if (entered) yield return LeaveResult();
        }
    }
}
