using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [DUNGEON] <c>-dotrpgDungeon &lt;folder&gt;</c>: dungeon framework checks. Pure rules first (reset clock,
    /// rank table, weekday rotation, save fields), then a Lv15 warrior with good gear and three mercenaries
    /// enters 황금 광맥 (일반) from the village guide: rooms, gates, a companion downed and rejoining, a coin
    /// revive, the boss, CLEAR, the result and the card flip. Then a failed run (death + 포기), 다시 도전,
    /// the no-revive failure, the entry limit, the raid (weekly lock, save-before-entering, abort + continue)
    /// and a solo run. Report lines "DGN &lt;what&gt; PASS|FAIL", last line "DGN summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        bool dungeonOnly;
        int dgnPassed, dgnFailed;

        void DCheck(string what, bool ok)
        {
            if (ok) dgnPassed++;
            else dgnFailed++;
            log?.WriteLine($"DGN {what} {(ok ? "PASS" : "FAIL")}");
        }

        /// <summary>Monday 2025-03-03 10:00 (local clock) — 황금 광맥 is open.</summary>
        static readonly DateTime Monday = new DateTime(2025, 3, 3, 10, 0, 0);

        static List<EnemyController> DungeonEnemies() =>
            EnemyController.Active.Where(e => e != null && !e.IsDead && e.isActiveAndEnabled).ToList();

        IEnumerator DungeonRunCapture()
        {
            dgnPassed = dgnFailed = 0;
            bool autosave = Game.Config.autosave;
            Game.Config.autosave = false;
            DateTime clock = Monday;
            ResetClock.NowOverride = () => clock;
            DungeonAuthority.Current = new LocalDungeonAuthority(20250303);
            yield return Wait(1.5f);

            PureDungeonChecks();

            // ---------- Setup: Lv15 warrior, good gear, three mercenaries ----------
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.6f);
            var prog = Game.Session.Progression;
            while (prog.Level < 15) prog.AddXp(prog.XpNeeded);
            var bag = Game.Session.Inventory;
            foreach (var item in EquipmentDatabase.All)
                if (!item.starter && item.UsableBy(CharacterClass.Warrior) && item.rarity >= ItemRarity.Rare) bag.Add(item.id, 1);
            Game.Session.Equipment.AutoEquip(CharacterClass.Warrior);
            var party = Game.Party;
            foreach (var id in new[] { "merc_bron", "merc_kai", "merc_elin" }) party.AddCompanion(id);
            yield return Wait(0.8f);
            var local = Game.Player;
            local.HealFull();
            int power = local.Data.Stats.Power(local.Class);
            DCheck($"setup: Lv{prog.Level} power={power} members={party.Count} weapon={Game.Session.Equipment[EquipSlot.Weapon]}", prog.Level == 15 && party.Count == 4);

            // ---------- Village guide → select window ----------
            var guide = NpcController.Services.FirstOrDefault(n => n != null && n.Definition.service == NpcService.Dungeon);
            DCheck($"village guide: exists={guide != null} at={Game.World.DungeonGuidePosition} prompt='{guide?.Prompt}' free={(guide != null && Vector2.Distance(guide.transform.position, Game.World.PlayerSpawn) < 12f)}",
                guide != null && guide.Prompt == "던전 입장" && Vector2.Distance(guide.transform.position, Game.World.PlayerSpawn) < 12f);
            if (guide != null)
            {
                local.Place((Vector2)guide.transform.position + Vector2.down * 1.2f, Facing.Up);
                yield return Wait(0.6f);
                yield return Shot("dgn_00_guide");
                guide.Interact(local);
            }
            else Game.UI.Dungeon.Open(false);
            yield return Wait(0.6f);
            var select = Game.UI.Dungeon;
            DCheck($"select window open: state={Game.State.Current} top={Game.UI.Top?.name} raidTab={select.DevRaidTab}",
                Game.State.Current == GameState.Inventory && Game.UI.Top == select && !select.DevRaidTab);
            DCheck($"weekday rows on Monday: gold={Strip(select.DevRowTag("gold_vein"))} smelter={Strip(select.DevRowTag("smelter"))} day='{Strip(select.DevDayText)}'",
                Strip(select.DevRowTag("gold_vein")) == "개방" && Strip(select.DevRowTag("smelter")) == "닫힘" && Strip(select.DevDayText).Contains("남은 입장 3/3"));
            select.DevSelect("gold_vein", DungeonDifficulty.Normal);
            yield return Wait(0.3f);
            yield return Shot("dgn_01_select");
            select.DevSelect("gold_vein", DungeonDifficulty.Adventure);
            bool advLocked = Strip(select.DevStatus).Contains("먼저 클리어");
            select.DevSelect("smelter", DungeonDifficulty.Normal);
            bool smelterClosed = Strip(select.DevStatus).Contains("열리지 않는");
            DCheck($"locked difficulty + closed dungeon refused: adventure='{Strip(select.DevStatus)}' {advLocked} smelterClosed={smelterClosed}", advLocked && smelterClosed);
            select.DevSelect(DungeonDatabase.Raid, DungeonDifficulty.Normal);
            yield return Wait(0.3f);
            yield return Shot("dgn_02_raid_tab");
            DCheck($"raid tab: raidTab={select.DevRaidTab} day='{Strip(select.DevDayText)}'", select.DevRaidTab && Strip(select.DevDayText).Contains("받을 수 있음"));
            select.DevSelect("gold_vein", DungeonDifficulty.Normal);
            yield return Wait(0.2f);

            // ---------- Enter 황금 광맥 (일반) ----------
            bool entered = select.DevEnter();
            yield return Wait(1.3f);
            var dir = Game.Dungeon;
            var run = dir.Run;
            local = Game.Player;
            var hud = FindAnyObjectByType<DungeonHudView>();
            int expected = DungeonDatabase.Get("gold_vein").rooms[0].groups.Sum(g => g.count);
            DCheck($"entered: ok={entered} inRun={dir.InRun} map={Game.World.MapId} state={Game.State.Current} entriesLeft={Game.Session.Dungeons.EntriesLeft(clock)} monsters={DungeonEnemies().Count}/{expected} partySize={run?.PartySize} hpMul={run?.HpMul:0.0}",
                entered && dir.InRun && Game.World.MapId == MapRegistry.DgnCanyon1 && Game.State.Current == GameState.Playing && Game.Session.Dungeons.EntriesLeft(clock) == 2
                && DungeonEnemies().Count == expected && run.PartySize == 4 && Mathf.Approximately(run.HpMul, 3f));
            DCheck($"party at the entry: local={local.Position} spawn={Game.World.PlayerSpawn} dist=[{string.Join(",", party.Members.Select(m => Vector2.Distance(m.Position, local.Position).ToString("0.0")))}] autoRevive={party.CompanionAutoRevive}",
                Vector2.Distance(local.Position, Game.World.PlayerSpawn) < 0.6f && party.Members.All(m => Vector2.Distance(m.Position, local.Position) < 3f) && !party.CompanionAutoRevive);
            DCheck($"dungeon HUD: clock={hud?.DevClockVisible} roomMap={hud?.DevRoomMapVisible} minimapHidden={hud?.DevMinimapHidden} text='{Strip(hud?.DevRoomText)}' door={(dir.Door != null && !dir.Door.IsOpen)}",
                hud != null && hud.DevClockVisible && hud.DevRoomMapVisible && hud.DevMinimapHidden && Strip(hud.DevRoomText).StartsWith("방 1/4") && dir.Door != null && !dir.Door.IsOpen);
            yield return Wait(0.8f);
            yield return Shot("dgn_03_room1");

            // Save guard (manual + auto) and the return scroll.
            string slot = SaveSystem.SlotPath(SaveSystem.DefaultSlot);
            DateTime before = File.Exists(slot) ? File.GetLastWriteTimeUtc(slot) : DateTime.MinValue;
            Game.Flow.SaveGame();
            Game.Config.autosave = true;
            Game.Flow.Autosave();
            Game.Config.autosave = false;
            bool scroll = Game.Flow.UseTownScroll();
            DateTime after = File.Exists(slot) ? File.GetLastWriteTimeUtc(slot) : DateTime.MinValue;
            DCheck($"no saving in a dungeon: fileChanged={before != after} scrollUsed={scroll} inRun={dir.InRun}", before == after && !scroll && dir.InRun);

            // ---------- Room 1: fight a little (combo), then finish ----------
            yield return ClearDungeonRoom(local, true);
            DCheck($"room 1 cleared: door open={dir.Door?.IsOpen} cleared=[{string.Join(",", run.ClearedRooms)}] kills={run.Kills} combo={run.MaxCombo} hits={run.HitsTaken}",
                dir.Door != null && dir.Door.IsOpen && run.ClearedRooms.Contains(0) && run.MaxCombo >= 1 && run.Kills == 5);
            yield return Wait(0.4f);
            yield return Shot("dgn_04_door_open");

            // A companion goes down: no field revive inside the room…
            var kai = party.Find("merc_kai");
            yield return KillMember(kai);
            yield return Wait(PartyManager.FieldReviveSeconds + 1f);
            bool stillDown = kai.IsDead;
            // …then rejoins at the next room's entry with half HP.
            float kaiHp = -1f, kaiNear = -1f;
            Action snap = () => { kaiHp = kai.Health.Current / (float)kai.Health.Max; kaiNear = Vector2.Distance(kai.Position, Game.Player.Position); };
            dir.RoomChanged += snap;
            local.Place(dir.Door.Threshold, Facing.Up);
            yield return Wait(1.6f);
            dir.RoomChanged -= snap;
            DCheck($"companion down stays down, rejoins next room: downAfter{PartyManager.FieldReviveSeconds + 1f:0}s={stillDown} room={run.RoomIndex} map={Game.World.MapId} kaiAlive={!kai.IsDead} hp={kaiHp:0.00} nearAtEntry={kaiNear:0.0}",
                stillDown && run.RoomIndex == 1 && Game.World.MapId == MapRegistry.DgnCanyon2 && !kai.IsDead && Mathf.Abs(kaiHp - DungeonDirector.CompanionRejoinHp) < 0.05f && kaiNear >= 0f && kaiNear < 3f);
            DCheck($"walked through the gate: local at entry={Vector2.Distance(local.Position, Game.World.PlayerSpawn):0.0} hud='{Strip(hud.DevRoomText)}'",
                Vector2.Distance(local.Position, Game.World.PlayerSpawn) < 0.6f && Strip(hud.DevRoomText).StartsWith("방 2/4"));

            // ---------- Room 2 ----------
            yield return ClearDungeonRoom(local, false);
            yield return Wait(0.3f);
            dir.DevEnterDoor();
            yield return Wait(1.4f);

            // ---------- Room 3: coin revive ----------
            yield return KillMember(local);
            yield return Wait(0.5f);
            bool prompt = dir.ReviveOpen && hud.DevReviveVisible;
            float remaining = dir.ReviveRemaining;
            yield return Shot("dgn_05_revive");
            bool accepted = dir.AcceptRevive();
            yield return Wait(0.2f);
            DCheck($"coin revive: prompt={prompt} remaining={remaining:0.0} accepted={accepted} alive={!local.IsDead} hp={local.Health.Current}/{local.Health.Max} mp={local.Mana}/{local.MaxMana} invuln={local.Health.IsInvulnerable} used={run.RevivesUsed} left={run.RevivesLeft} state={Game.State.Current}",
                prompt && remaining > 8f && accepted && !local.IsDead && local.Health.Current == local.Health.Max && local.Mana == local.MaxMana && local.Health.IsInvulnerable
                && run.RevivesUsed == 1 && run.RevivesLeft == 4 && Game.State.Current == GameState.Playing);
            yield return ClearDungeonRoom(local, false);
            yield return Wait(0.3f);
            dir.DevEnterDoor();
            yield return Wait(1.4f);

            // ---------- Boss room ----------
            DCheck($"boss room: map={Game.World.MapId} index={run.RoomIndex} door={(dir.Door != null)} enemies={DungeonEnemies().Count} boss={DungeonEnemies().Any(e => e.transform.localScale.x > 1.2f)}",
                Game.World.MapId == MapRegistry.DgnCanyonBoss && run.InBossRoom && dir.Door == null && DungeonEnemies().Any(e => e.transform.localScale.x > 1.2f));
            yield return Wait(0.6f);
            yield return Shot("dgn_06_boss");
            var boss = DungeonEnemies().OrderByDescending(e => e.transform.localScale.x).First();
            int addsBefore = DungeonEnemies().Count - 1;
            var bh = boss.GetComponent<Health>();
            for (int tries = 0; tries < 30 && !boss.IsDead; tries++) { boss.TakeDamage(new DamageInfo(bh.Current + 999, local.Center, 0f, Team.Player, local.gameObject)); if (!boss.IsDead) yield return null; }
            yield return Wait(0.25f);
            float slow = Time.timeScale;
            yield return Shot("dgn_07_clear");
            yield return Wait(1.1f);
            int addsAfter = DungeonEnemies().Count;
            DCheck($"boss down: slowmo={slow:0.00} state={run.State} addsBefore={addsBefore} addsAfter={addsAfter} timeScale={Time.timeScale:0.00}",
                Mathf.Abs(slow - DungeonDirector.SlowMotionScale) < 0.01f && addsBefore > 0 && addsAfter == 0);
            yield return Wait(2.0f);
            var result = Game.UI.DungeonResult;
            DCheck($"result: state={Game.State.Current} top={Game.UI.Top?.name} runState={run.State} rank={run.Rank} score={run.Score.Total} (t{run.Score.time} h{run.Score.hits} k{run.Score.kills} c{run.Score.combo} -{run.Score.revivePenalty}) kills={run.Kills}/{run.Monsters} time={run.Elapsed:0.0}s xp={run.XpGained} stamp={result.DevStamp}",
                Game.State.Current == GameState.Inventory && Game.UI.Top == result && run.State == DungeonRunState.Cleared && run.Score.revivePenalty == 10
                && run.Kills >= run.Monsters && run.XpGained > 0 && result.DevStamp == run.Rank.ToString());
            DCheck($"damage meters: {string.Join(", ", run.MemberDamage.Select(m => $"{m.name}={m.damage}"))}", run.MemberDamage.Count == 4 && run.MemberDamage[0].local && run.MemberDamage[0].damage > 0);
            yield return Wait(0.5f);
            yield return Shot("dgn_08_result");

            // ---------- Card flip ----------
            var cards = run.Cards;
            int pick = 1;
            string pickId = cards[pick].itemId;
            int have = bag.Count(pickId);
            bool flipped = result.PlayerPick(pick);
            yield return Wait(0.2f);
            yield return Shot("dgn_09_card_pick");
            yield return Wait(3.4f);
            var who = Enumerable.Range(0, 4).Select(i => Strip(result.DevWho(i))).ToList();
            DCheck($"cards: flipped={flipped} done={result.DevDone} all={Enumerable.Range(0, 4).All(result.DevFlipped)} who=[{string.Join(",", who)}] card={cards[pick].Label} bag {have}->{bag.Count(pickId)}",
                flipped && result.DevDone && Enumerable.Range(0, 4).All(result.DevFlipped) && who[pick] == "나"
                && who.Count(w => w == "브론" || w == "카이" || w == "엘린") == 3 && bag.Count(pickId) == have + cards[pick].count);
            yield return Shot("dgn_10_cards_done");
            var progress = Game.Session.Dungeons;
            DCheck($"progress: cleared={progress.IsCleared("gold_vein", DungeonDifficulty.Normal)} best={progress.BestRank("gold_vein", DungeonDifficulty.Normal)} adventureUnlocked={progress.IsUnlocked(DungeonDatabase.Get("gold_vein"), DungeonDifficulty.Adventure)} kingLocked={!progress.IsUnlocked(DungeonDatabase.Get("gold_vein"), DungeonDifficulty.King)} entries={progress.EntriesLeft(clock)}/3",
                progress.IsCleared("gold_vein", DungeonDifficulty.Normal) && progress.BestRank("gold_vein", DungeonDifficulty.Normal) == run.Rank
                && progress.IsUnlocked(DungeonDatabase.Get("gold_vein"), DungeonDifficulty.Adventure) && !progress.IsUnlocked(DungeonDatabase.Get("gold_vein"), DungeonDifficulty.King)
                && progress.EntriesLeft(clock) == 2);

            // ---------- Back to the village ----------
            result.DevLeave(false);
            yield return Wait(1.4f);
            local = Game.Player;
            DCheck($"back in the village: map={Game.World.MapId} inRun={dir.InRun} state={Game.State.Current} autoRevive={party.CompanionAutoRevive} minimap={!hud.DevMinimapHidden} near={party.Members.All(m => Vector2.Distance(m.Position, local.Position) < 3f)}",
                Game.World.MapId == MapRegistry.Village && !dir.InRun && Game.State.Current == GameState.Playing && party.CompanionAutoRevive && !hud.DevMinimapHidden
                && party.Members.All(m => Vector2.Distance(m.Position, local.Position) < 3f));
            yield return Shot("dgn_11_village");

            // ---------- Failed run: death + 포기, then 다시 도전, then no revives left ----------
            yield return FailedRunChecks(clock);

            // ---------- Entry limit ----------
            select.Open(false);
            yield return Wait(0.5f);
            select.DevSelect("gold_vein", DungeonDifficulty.Normal);
            bool refused = !select.DevEnter();
            DCheck($"no entries left: refused={refused} status='{Strip(select.DevStatus)}' left={progress.EntriesLeft(clock)}", refused && progress.EntriesLeft(clock) == 0 && Strip(select.DevStatus).Contains("모두 사용"));
            yield return Shot("dgn_14_no_entries");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);

            // ---------- Save / continue keeps the counters ----------
            var rank0 = progress.BestRank("gold_vein", DungeonDifficulty.Normal);
            Game.Flow.SaveGame();
            yield return Wait(0.4f);
            Game.Flow.ReturnToTitle();
            yield return Wait(1.4f);
            Game.Flow.ContinueGame();
            yield return Wait(1.6f);
            local = Game.Player;
            progress = Game.Session.Dungeons;
            DCheck($"save/continue keeps dungeon progress: entriesUsed={progress.EntriesUsed(clock)} cleared={progress.IsCleared("gold_vein", DungeonDifficulty.Normal)} best={progress.BestRank("gold_vein", DungeonDifficulty.Normal)}=={rank0}",
                progress.EntriesUsed(clock) == 3 && progress.IsCleared("gold_vein", DungeonDifficulty.Normal) && progress.BestRank("gold_vein", DungeonDifficulty.Normal) == rank0);
            DCheck($"continue: companions spawn next to the player: members={party.Count} dist=[{string.Join(",", party.Members.Select(m => Vector2.Distance(m.Position, local.Position).ToString("0.0")))}]",
                party.Count == 4 && party.Members.All(m => Vector2.Distance(m.Position, local.Position) < 3f));

            // ---------- Raid: weekly lock, save before entering, abort + continue ----------
            yield return RaidChecks(() => clock);

            // ---------- Next day (daily reset) + solo run ----------
            clock = new DateTime(2025, 3, 4, 7, 0, 0); // Tuesday 07:00
            DCheck($"daily reset at 06:00: entries {progress.EntriesLeft(clock)}/3", progress.EntriesLeft(clock) == 3);
            yield return SoloRunChecks(clock);

            ResetClock.NowOverride = null;
            DungeonAuthority.Current = new LocalDungeonAuthority();
            Game.Config.autosave = autosave;
            log?.WriteLine($"DGN summary: {dgnPassed} passed, {dgnFailed} failed");
        }

        // =============================== Pure rules ===============================

        void PureDungeonChecks()
        {
            // Reset clock boundaries.
            var d0559 = new DateTime(2025, 3, 5, 5, 59, 0);
            var d0600 = new DateTime(2025, 3, 5, 6, 0, 0);
            DCheck($"daily reset: 05:59 -> {ResetClock.DailyResetStart(d0559):MM-dd HH:mm} day={ResetClock.GameDay(d0559)}, 06:00 -> {ResetClock.DailyResetStart(d0600):MM-dd HH:mm} day={ResetClock.GameDay(d0600)}",
                ResetClock.DailyResetStart(d0559) == new DateTime(2025, 3, 4, 6, 0, 0) && ResetClock.DailyResetStart(d0600) == d0600
                && ResetClock.GameDay(d0559) == DayOfWeek.Tuesday && ResetClock.GameDay(d0600) == DayOfWeek.Wednesday);
            long stamp = ResetClock.DailyResetStart(d0559).Ticks;
            DCheck($"daily expiry: stamp(05:59) expired at 05:59={ResetClock.DailyExpired(stamp, d0559)} at 06:00={ResetClock.DailyExpired(stamp, d0600)}",
                !ResetClock.DailyExpired(stamp, d0559) && ResetClock.DailyExpired(stamp, d0600));
            var thu0559 = new DateTime(2025, 3, 6, 5, 59, 0);
            var thu0600 = new DateTime(2025, 3, 6, 6, 0, 0);
            var wed = new DateTime(2025, 3, 12, 23, 0, 0);
            var sun = new DateTime(2025, 3, 9, 12, 0, 0);
            DCheck($"weekly reset (Thu 06:00): thu05:59->{ResetClock.WeeklyResetStart(thu0559):MM-dd} thu06:00->{ResetClock.WeeklyResetStart(thu0600):MM-dd} sun->{ResetClock.WeeklyResetStart(sun):MM-dd} nextWed->{ResetClock.WeeklyResetStart(wed):MM-dd} next={ResetClock.NextWeeklyReset(thu0600):MM-dd}",
                ResetClock.WeeklyResetStart(thu0559) == new DateTime(2025, 2, 27, 6, 0, 0) && ResetClock.WeeklyResetStart(thu0600) == thu0600
                && ResetClock.WeeklyResetStart(sun) == thu0600 && ResetClock.WeeklyResetStart(wed) == thu0600 && ResetClock.NextWeeklyReset(thu0600) == new DateTime(2025, 3, 13, 6, 0, 0));
            // Week wrap across a year / month boundary.
            var newYear = new DateTime(2026, 1, 1, 7, 0, 0); // Thursday
            var nye = new DateTime(2025, 12, 31, 23, 0, 0);
            DCheck($"week wrap: 2025-12-31 -> {ResetClock.WeeklyResetStart(nye):yyyy-MM-dd} 2026-01-01 07:00 -> {ResetClock.WeeklyResetStart(newYear):yyyy-MM-dd}",
                ResetClock.WeeklyResetStart(nye) == new DateTime(2025, 12, 25, 6, 0, 0) && ResetClock.WeeklyResetStart(newYear) == new DateTime(2026, 1, 1, 6, 0, 0));
            var claimed = new DungeonProgress();
            claimed.ClaimRaid(new DateTime(2025, 3, 5, 12, 0, 0));
            DCheck($"raid lock: claimed Wed -> available Thu05:59={claimed.RaidRewardAvailable(thu0559)} Thu06:00={claimed.RaidRewardAvailable(thu0600)}",
                !claimed.RaidRewardAvailable(thu0559) && claimed.RaidRewardAvailable(thu0600));

            // Rank table.
            var table = new (int score, DungeonRank rank)[] { (100, DungeonRank.SSS), (95, DungeonRank.SSS), (94, DungeonRank.SS), (88, DungeonRank.SS), (87, DungeonRank.S), (80, DungeonRank.S),
                (79, DungeonRank.A), (70, DungeonRank.A), (69, DungeonRank.B), (60, DungeonRank.B), (59, DungeonRank.C), (50, DungeonRank.C), (49, DungeonRank.D), (40, DungeonRank.D), (39, DungeonRank.E), (25, DungeonRank.E), (24, DungeonRank.F), (0, DungeonRank.F) };
            var bad = table.Where(t => DungeonRanking.RankOf(t.score) != t.rank).Select(t => $"{t.score}->{DungeonRanking.RankOf(t.score)}").ToList();
            DCheck($"rank table: {(bad.Count == 0 ? "all boundaries" : string.Join(",", bad))} xp SSS+{DungeonRanking.XpBonusPercent(DungeonRank.SSS)} B+{DungeonRanking.XpBonusPercent(DungeonRank.B)} C+{DungeonRanking.XpBonusPercent(DungeonRank.C)}",
                bad.Count == 0 && DungeonRanking.XpBonusPercent(DungeonRank.SSS) == 50 && DungeonRanking.XpBonusPercent(DungeonRank.B) == 10 && DungeonRanking.XpBonusPercent(DungeonRank.C) == 0);
            var perfect = DungeonRanking.Score(100f, 150f, 0, 20, 20, 25, 0);
            var revived = DungeonRanking.Score(100f, 150f, 0, 20, 20, 25, 2);
            var slowRun = DungeonRanking.Score(450f, 150f, 12, 20, 20, 4, 0);
            DCheck($"rank score: perfect={perfect.Total}{perfect.Rank} 2 revives={revived.Total}{revived.Rank} slow/hit={slowRun.Total}{slowRun.Rank} (t{slowRun.time} h{slowRun.hits} c{slowRun.combo})",
                perfect.Total == 100 && perfect.Rank == DungeonRank.SSS && revived.Total == 80 && revived.Rank == DungeonRank.S && slowRun.time == 0 && slowRun.hits == 0 && slowRun.Rank == DungeonRank.F);

            // Combo: hits at most 1.5 s apart chain.
            var cr = new DungeonRun(DungeonDatabase.Get("gold_vein"), DungeonDifficulty.Normal, 1);
            foreach (float t in new[] { 10f, 10.5f, 11.9f, 13.3f, 15.0f, 15.2f }) cr.RegisterHit(t);
            DCheck($"combo rule (1.5 s window): max={cr.MaxCombo} current={cr.Combo}", cr.MaxCombo == 4 && cr.Combo == 2);

            // Weekday rotation for 7 days (Mon..Sun).
            var week = new List<string>();
            bool rotationOk = true;
            for (int i = 0; i < 7; i++)
            {
                var day = Monday.AddDays(i);
                var open = DungeonDatabase.Weekday.Where(d => ResetClock.IsOpen(d, day)).Select(d => d.id).ToList();
                week.Add($"{day.DayOfWeek.ToString().Substring(0, 3)}:{open.Count}");
                bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                if (weekend) rotationOk &= open.Count == 5;
                else rotationOk &= open.Count == 1 && DungeonDatabase.Weekday[i].id == open[0];
                rotationOk &= ResetClock.IsOpen(DungeonDatabase.SkeletonKing, day);
            }
            // Before 06:00 on Tuesday it is still Monday.
            rotationOk &= ResetClock.IsOpen(DungeonDatabase.Get("gold_vein"), new DateTime(2025, 3, 4, 5, 0, 0)) && !ResetClock.IsOpen(DungeonDatabase.Get("smelter"), new DateTime(2025, 3, 4, 5, 0, 0));
            DCheck($"weekday rotation: {string.Join(" ", week)} (raid always, Tue 05:00 = Monday)", rotationOk);

            // Data skeleton.
            bool data = DungeonDatabase.Weekday.Count == 5 && DungeonDatabase.Weekday.All(d => d.RoomCount == 4 && d.bossRoom == 3 && d.rooms[3].isBoss && d.rooms.All(r => MapRegistry.Get(r.mapId) != null && Resources.Load<TextAsset>(MapRegistry.Get(r.mapId).resource) != null))
                && DungeonDatabase.SkeletonKing.RoomCount == 3 && DungeonDatabase.SkeletonKing.maxParty == 4
                && Enumerable.Range(0, 4).Select(i => DungeonDatabase.Difficulty((DungeonDifficulty)i).revives).SequenceEqual(new[] { 5, 4, 3, 2 })
                && Enumerable.Range(0, 4).Select(i => DungeonDatabase.Difficulty((DungeonDifficulty)i).recommendedLevel).SequenceEqual(new[] { 5, 12, 20, 27 })
                && DungeonDatabase.RaidDifficulty.revives == 3 && Enumerable.Range(1, 4).Select(DungeonDatabase.PartyScale).SequenceEqual(new[] { 1f, 1.7f, 2.4f, 3f });
            DCheck($"dungeon table: 5 weekday x (3 rooms + boss), raid 2 + boss, revives 5/4/3/2 raid 3, rec Lv 5/12/20/27, party HP 1/1.7/2.4/3, rooms={MapRegistry.Rooms.Count()}", data);

            // Save fields round trip (+ old saves without them).
            var p = new DungeonProgress();
            var t0 = new DateTime(2025, 3, 3, 9, 0, 0);
            p.UseEntry(t0); p.UseEntry(t0);
            p.RecordClear("gold_vein", DungeonDifficulty.Normal, DungeonRank.A);
            p.RecordClear("gold_vein", DungeonDifficulty.Normal, DungeonRank.C);
            p.RecordClear("gold_vein", DungeonDifficulty.Normal, DungeonRank.SS);
            p.ClaimRaid(t0);
            var sd = new SaveData();
            p.Capture(sd);
            var json = JsonUtility.ToJson(sd);
            var back = new DungeonProgress();
            back.Restore(JsonUtility.FromJson<SaveData>(json));
            var old = new DungeonProgress();
            old.Restore(JsonUtility.FromJson<SaveData>("{\"version\":3,\"mapId\":\"village\"}"));
            DCheck($"save fields: entries={back.EntriesUsed(t0)} best={back.BestRank("gold_vein", DungeonDifficulty.Normal)} raidAvail={back.RaidRewardAvailable(t0)} | old save: entries={old.EntriesUsed(t0)} best={old.BestRank("gold_vein", DungeonDifficulty.Normal)?.ToString() ?? "none"} raid={old.RaidRewardAvailable(t0)}",
                back.EntriesUsed(t0) == 2 && back.BestRank("gold_vein", DungeonDifficulty.Normal) == DungeonRank.SS && !back.RaidRewardAvailable(t0) && back.IsCleared("gold_vein", DungeonDifficulty.Normal)
                && old.EntriesUsed(t0) == 0 && old.BestRank("gold_vein", DungeonDifficulty.Normal) == null && old.RaidRewardAvailable(t0));

            // Authority seam: a seeded authority deals the same cards twice.
            var def = DungeonDatabase.Get("armory");
            var r1 = new DungeonRun(def, DungeonDifficulty.Hero, 1);
            var a = new LocalDungeonAuthority(7).DealCards(r1, CharacterClass.Warrior);
            var b = new LocalDungeonAuthority(7).DealCards(r1, CharacterClass.Warrior);
            bool same = a.Count == 4 && a.Select(c => c.itemId + c.count).SequenceEqual(b.Select(c => c.itemId + c.count));
            DCheck($"authority: seeded deals match={same} cards=[{string.Join(", ", a.Select(c => c.Label))}]", same);
        }

        // =============================== Room helpers ===============================

        /// <summary>Kills the room: optionally a few seconds of real (scripted) attacks first, then direct hits from the local player.</summary>
        IEnumerator ClearDungeonRoom(PlayerController local, bool fight)
        {
            if (fight)
            {
                var script = new ScriptedInput();
                var brain = local.Input;
                local.Input = script;
                float end = Time.realtimeSinceStartup + 4f, nextAttack = 0f;
                while (Time.realtimeSinceStartup < end)
                {
                    if (local.Health.Current < local.Health.Max / 2) local.HealFull();
                    var target = DungeonEnemies().OrderBy(e => Vector2.Distance(e.Position, local.Position)).FirstOrDefault();
                    if (target == null) break;
                    if (Vector2.Distance(target.Position, local.Position) > 1.1f) MoveEnemy(target, local.Position + Vector2.right * 0.9f);
                    if (Time.realtimeSinceStartup >= nextAttack)
                    {
                        nextAttack = Time.realtimeSinceStartup + 0.3f;
                        script.Aim = (target.Center - local.Center).normalized;
                        script.PressAttack();
                    }
                    yield return null;
                }
                local.Input = brain;
            }
            yield return KillAllEnemies(local, 0.12f);
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && Game.Dungeon.Door != null && !Game.Dungeon.Door.IsOpen) yield return null;
        }

        /// <summary>Hits every living monster from the local player until the room is empty (a hit can land in a short invulnerability window).</summary>
        IEnumerator KillAllEnemies(PlayerController local, float gap)
        {
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until)
            {
                var alive = DungeonEnemies();
                if (alive.Count == 0) yield break;
                foreach (var e in alive)
                {
                    var h = e.GetComponent<Health>();
                    if (h == null || e.IsDead) continue;
                    e.TakeDamage(new DamageInfo(h.Current + 999, local.Center, 0f, Team.Player, local.gameObject));
                    if (gap > 0f) yield return Wait(gap);
                }
                yield return null;
            }
        }

        /// <summary>Downs a party member as if a monster had hit it (retries through block rolls and invulnerability).</summary>
        IEnumerator KillMember(PlayerController m)
        {
            float until = Time.realtimeSinceStartup + 4f;
            while (!m.IsDead && Time.realtimeSinceStartup < until)
            {
                m.TakeDamage(new DamageInfo(999999, m.Position + Vector2.right, 0f, Team.Enemy));
                if (!m.IsDead) yield return null;
            }
        }

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
            bool rp = result.PlayerPick(0);
            yield return Wait(3.8f);
            DCheck($"raid clear: pick={rp} state={run.State} cards={run.Cards?.Count} raidAvail={progress.RaidRewardAvailable(now())} done={result.DevDone}",
                run.State == DungeonRunState.Cleared && run.Cards != null && run.Cards.Count == 4 && !progress.RaidRewardAvailable(now()) && result.DevDone);
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
            DCheck($"raid practice + quit mid-run: locked={locked} aborted={aborted} continue map={Game.World.MapId} inRun={dir.InRun} raidAvail={Game.Session.Dungeons.RaidRewardAvailable(now())}",
                locked && aborted && Game.World.MapId == MapRegistry.Village && !dir.InRun && !Game.Session.Dungeons.RaidRewardAvailable(now()));
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
