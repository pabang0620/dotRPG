using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CONTENT] Dungeon content checks (appended to <c>-dotrpgDungeon</c>): themed monsters and bosses per
    /// weekday dungeon, boss bar binding and HUD layering (no overlaps at 1280x720 and 1920x1080, quest tracker
    /// hidden in dungeons), a field boss, and one full raid clear by scripted play (king phases, weekly lock).
    /// <c>-dotrpgBalance &lt;folder&gt; [-dotrpgBalanceSet normal|hero|raid]</c>: time-scaled scripted clears of
    /// every weekday dungeon (일반 Lv5 / 영웅 Lv27) and the raid (Lv27), lines "BAL ... PASS|FAIL".
    /// </summary>
    public partial class DevCapture
    {
        // ---------- Simulation tuning ----------
        /// <summary>Time scale of the scripted balance runs (game-time clear times are reported).</summary>
        const float SimTimeScale = 4f;
        /// <summary>Target clear window in game seconds (weekday dungeons, raid).</summary>
        const float DungeonMinSeconds = 120f, DungeonMaxSeconds = 300f, RaidMinSeconds = 300f, RaidMaxSeconds = 600f;
        const float SimCapSeconds = 700f;
        /// <summary>Time scale of the -dotrpgDungeon raid clear (2x keeps telegraph / wind-up timing faithful).</summary>
        const float RaidCheckTimeScale = 2f;
        /// <summary>Scripted play: drink an HP potion below this HP fraction, an MP potion below this MP fraction.</summary>
        const float SimPotionHp = 0.5f, SimPotionMp = 0.25f;
        /// <summary>Scripted play: back off below this HP fraction or when 3+ monsters crowd in.</summary>
        const float SimRetreatHp = 0.3f, SimRetreatSeconds = 1.2f, SimRetreatCooldown = 3f;
        /// <summary>Potions handed to the scripted hero (a player going into a raid carries a stack).</summary>
        const int SimHpPotions = 30, SimMpPotions = 20;
        static readonly string[] Mercs = { "merc_bron", "merc_kai", "merc_elin" };

        bool balanceOnly;
        int balPassed, balFailed;

        void BCheck(string what, bool ok)
        {
            if (ok) balPassed++;
            else balFailed++;
            log?.WriteLine($"BAL {what} {(ok ? "PASS" : "FAIL")}");
        }

        static DateTime WeekdayClock(int index) => new DateTime(2025, 3, 3 + index, 10, 0, 0);

        // =============================== Shared helpers ===============================

        static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        /// <summary>Union of the active child graphics under <paramref name="root"/> (screen pixels).</summary>
        static Rect? GraphicsRect(Transform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return null;
            Rect? r = null;
            foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
            {
                if (!g.enabled || g.color.a < 0.05f) continue;
                var s = ScreenRect(g.rectTransform);
                if (s.width < 1f || s.height < 1f) continue;
                r = r == null ? s : Rect.MinMaxRect(Mathf.Min(r.Value.xMin, s.xMin), Mathf.Min(r.Value.yMin, s.yMin), Mathf.Max(r.Value.xMax, s.xMax), Mathf.Max(r.Value.yMax, s.yMax));
            }
            return r;
        }

        static Transform HudChild(string name)
        {
            if (Game.UI == null || Game.UI.Hud == null) return null;
            foreach (var t in Game.UI.Hud.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>"" when the boss bar overlaps none of the other HUD blocks, else the names it touches.</summary>
        static string BossBarOverlaps()
        {
            var bar = BossHpBarView.Instance != null ? BossHpBarView.Instance.transform.Find("Bar") : null;
            var barRect = GraphicsRect(bar);
            if (barRect == null) return "noBar";
            var hits = new List<string>();
            foreach (var n in new[] { "Quest", "PartyFrames", "RoomMap", "Clock", "Minimap", "Status" })
            {
                var r = GraphicsRect(HudChild(n));
                if (r != null && r.Value.Overlaps(barRect.Value)) hits.Add(n);
            }
            return string.Join("+", hits);
        }

        void SetupHero(int level, bool goodGear)
        {
            var prog = Game.Session.Progression;
            while (prog.Level < level) prog.AddXp(prog.XpNeeded);
            var bag = Game.Session.Inventory;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.starter || !item.UsableBy(CharacterClass.Warrior)) continue;
                if (goodGear ? item.rarity >= ItemRarity.Rare : item.rarity <= ItemRarity.Uncommon) bag.Add(item.id, 1);
            }
            Game.Session.Equipment.AutoEquip(CharacterClass.Warrior);
            int hpHave = bag.Count(ConsumableDatabase.HpPotion), mpHave = bag.Count(ConsumableDatabase.MpPotion);
            if (hpHave < SimHpPotions) bag.Add(ConsumableDatabase.HpPotion, SimHpPotions - hpHave);
            if (mpHave < SimMpPotions) bag.Add(ConsumableDatabase.MpPotion, SimMpPotions - mpHave);
            var party = Game.Party;
            foreach (var id in Mercs) if (party.Find(id) == null) party.AddCompanion(id);
            Game.Player.HealFull();
        }

        public sealed class SimResult
        {
            public bool cleared, failed;
            public float seconds;
            public int revives, maxPhase = 1;
            public DungeonRank rank;
        }

        /// <summary>
        /// Basic scripted play for the local hero until the run ends: walk to the nearest monster, basic attack,
        /// cycle the four skills, take revives, step through open gates. Companions use their own AI.
        /// </summary>
        IEnumerator ScriptedRun(SimResult res, float scale, float cap)
        {
            var dir = Game.Dungeon;
            var run = dir.Run;
            var local = Game.Player;
            var script = new ScriptedInput();
            var brain = local.Input;
            local.Input = script;
            float nextAttack = 0f, nextSkill = 0f, doorAt = -1f, nextPotion = 0f, nextManaPotion = 0f, retreatUntil = 0f, retreatReadyAt = 0f;
            int slot = 0;
            while (run != null && !run.IsOver && run.Elapsed < cap)
            {
                if (!dir.IsBusy && Time.timeScale > 0.5f && !Mathf.Approximately(Time.timeScale, scale)) Time.timeScale = scale;
                if (dir.ReviveOpen)
                {
                    script.Move = Vector2.zero;
                    if (run.RevivesLeft > 0) dir.AcceptRevive();
                    yield return null;
                    continue;
                }
                if (dir.IsBusy || local.IsDead) { script.Move = Vector2.zero; yield return null; continue; }
                foreach (var e in EnemyController.Active)
                    if (e != null && !e.IsDead && e.IsBoss && e.Behaviour is BossBrain bb) res.maxPhase = Mathf.Max(res.maxPhase, bb.Phase);
                var target = DungeonEnemies().OrderBy(e => Vector2.Distance(e.Position, local.Position)).FirstOrDefault();
                if (target == null)
                {
                    script.Move = Vector2.zero;
                    if (dir.Door != null && dir.Door.IsOpen)
                    {
                        if (doorAt < 0f) doorAt = Time.time + 1f;
                        else if (Time.time >= doorAt) { dir.DevEnterDoor(); doorAt = -1f; }
                    }
                    yield return null;
                    continue;
                }
                doorAt = -1f;
                // Potions like a player would: HP below the threshold, MP too low for skills.
                float hpFrac = local.Health != null && local.Health.Max > 0 ? (float)local.Health.Current / local.Health.Max : 1f;
                if (hpFrac < SimPotionHp && Time.time >= nextPotion)
                {
                    nextPotion = Time.time + 0.5f;
                    local.UseHealing();
                }
                if (local.MaxMana > 0 && local.Mana < local.MaxMana * SimPotionMp && Time.time >= nextManaPotion)
                {
                    nextManaPotion = Time.time + 0.5f;
                    local.UseConsumable(ConsumableDatabase.MpPotion);
                }
                Vector2 to = target.Position - local.Position;
                float reach = 0.9f + 0.35f * target.Size;
                // Reposition: at low HP (potion on cooldown) or when three or more monsters crowd in, step
                // away from their centre for a moment instead of trading hits; companions keep the aggro.
                var near = DungeonEnemies().Where(e => Vector2.Distance(e.Position, local.Position) < 2.2f).ToList();
                bool wantRetreat = (hpFrac < SimRetreatHp && near.Count > 0) || near.Count >= 3;
                if (Time.time >= retreatUntil && wantRetreat && Time.time >= retreatReadyAt)
                {
                    retreatUntil = Time.time + SimRetreatSeconds;
                    retreatReadyAt = retreatUntil + SimRetreatCooldown;
                }
                if (Time.time < retreatUntil)
                {
                    Vector2 centre = near.Count > 0 ? (Vector2)near.Aggregate(Vector3.zero, (s, e) => s + (Vector3)e.Position) / near.Count : target.Position;
                    Vector2 away = local.Position - centre;
                    if (away.sqrMagnitude < 0.01f) away = Vector2.down;
                    script.Move = away.normalized;
                    script.Aim = (target.Center - local.Center).normalized;
                    // Ranged skills still fire while backing off.
                    if (Time.time >= nextSkill)
                    {
                        nextSkill = Time.time + 1.2f;
                        script.PressSkill(slot);
                        slot = (slot + 1) % 4;
                    }
                    yield return null;
                    continue;
                }
                script.Move = to.magnitude > reach ? to.normalized : Vector2.zero;
                script.Aim = (target.Center - local.Center).normalized;
                if (to.magnitude <= reach + 0.6f)
                {
                    if (Time.time >= nextSkill)
                    {
                        nextSkill = Time.time + 1.2f;
                        script.PressSkill(slot);
                        slot = (slot + 1) % 4;
                    }
                    else if (Time.time >= nextAttack)
                    {
                        nextAttack = Time.time + 0.25f;
                        script.PressAttack();
                    }
                }
                yield return null;
            }
            local.Input = brain;
            script.Move = Vector2.zero;
            Time.timeScale = Game.IsPlaying ? 1f : Time.timeScale;
            res.seconds = run != null ? run.Elapsed : 0f;
            res.revives = run != null ? run.RevivesUsed : 0;
            res.rank = run != null ? run.Rank : DungeonRank.F;
            res.cleared = run != null && run.State == DungeonRunState.Cleared;
            res.failed = run != null && run.State == DungeonRunState.Failed;
            if (run != null && run.State == DungeonRunState.Playing)
            {
                // Time cap: leave the run and go home.
                dir.AbortRun();
                Game.Flow.TravelTo(MapRegistry.Village);
            }
        }

        /// <summary>Waits for the result screen after a run and leaves to the village.</summary>
        IEnumerator LeaveResult()
        {
            Time.timeScale = 1f;
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && Game.UI.Top != Game.UI.DungeonResult && Game.Dungeon.InRun) yield return null;
            if (Game.UI.Top != Game.UI.DungeonResult) { yield return Wait(1.6f); yield break; }
            var result = Game.UI.DungeonResult;
            // Pick a card (refused until the stamp delay has passed), then wait for every companion flip:
            // 마을로 only works once the cards are done.
            until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && !result.DevDone)
            {
                result.PlayerPick(0);
                yield return null;
            }
            yield return Wait(0.3f);
            result.DevLeave(false);
            // Wait for the fade and the village to be loaded and playable again.
            until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && (Game.Dungeon.InRun || Game.Dungeon.IsBusy || !Game.IsPlaying || Game.World.MapId != MapRegistry.Village)) yield return null;
            yield return Wait(0.5f);
        }

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
                bool entered = dir.Enter(def, DungeonDifficulty.Normal);
                yield return Wait(1.4f);
                var run = dir.Run;
                local = Game.Player;
                var missing = new List<string>();
                var seen = new HashSet<string>();
                bool questHidden = true;
                for (int room = 0; entered && run != null && room < def.RoomCount; room++)
                {
                    var alive = DungeonEnemies().Where(e => e.Summoner == null && e.Def != null).Select(e => e.Def.id).ToList();
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
            var now = WeekdayClock(1);
            setClock(now);
            SetupHero(27, true);
            foreach (var m in Game.Party.Members) if (m != null && m.IsDead) Game.Party.ReviveMember(m, 1f);
            foreach (var m in Game.Party.Members) m?.HealFull();
            yield return Wait(0.6f);
            bool claimedBefore = !Game.Session.Dungeons.RaidRewardAvailable(now);
            bool rEntered = dir.Enter(DungeonDatabase.SkeletonKing, DungeonDifficulty.Normal);
            yield return Wait(1.4f);
            var raid = dir.Run;
            bool locked = raid != null && raid.RewardsLocked;
            var ids = DungeonEnemies().Where(e => e.Def != null).Select(e => e.Def.id).Distinct().OrderBy(s => s).ToList();
            var res = new SimResult();
            if (rEntered) yield return ScriptedRun(res, RaidCheckTimeScale, SimCapSeconds);
            DCheck($"raid scripted clear: entered={rEntered} room1=[{string.Join(",", ids)}] cleared={res.cleared} time={res.seconds:0}s revives={res.revives} kingPhase={res.maxPhase}/3 timeScale={RaidCheckTimeScale}",
                rEntered && res.cleared && res.maxPhase >= 3 && ids.Contains("skel_knight"));
            yield return Shot("dgn_25_raid_scripted_end");
            DCheck($"raid weekly lock honoured: claimedBefore={claimedBefore} practiceLocked={locked} cards={raid?.Cards?.Count ?? 0} stillClaimed={!Game.Session.Dungeons.RaidRewardAvailable(now)}",
                rEntered && raid != null && raid.Dungeon.isRaid && claimedBefore && locked && (raid.Cards == null || raid.Cards.Count == 0) && raid.XpGained == 0
                && !Game.Session.Dungeons.RaidRewardAvailable(now));
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
                for (int d = 0; d < 5; d++) yield return BalanceOne(DungeonDatabase.Weekday[d], DungeonDifficulty.Normal, d, v => clock = v);
            }
            if (set == "all" || set == "hero" || set == "raid") SetupHero(27, true);
            if (set == "all" || set == "hero")
            {
                yield return Wait(0.6f);
                for (int d = 0; d < 5; d++) yield return BalanceOne(DungeonDatabase.Weekday[d], DungeonDifficulty.Hero, d, v => clock = v);
            }
            if (set == "all" || set == "raid")
            {
                yield return Wait(0.6f);
                yield return BalanceOne(DungeonDatabase.SkeletonKing, DungeonDifficulty.Normal, 1, v => clock = v);
            }
            ResetClock.NowOverride = null;
            log?.WriteLine($"BAL summary: {balPassed} passed, {balFailed} failed");
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
