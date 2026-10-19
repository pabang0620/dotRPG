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
        // [MEASURE] -dotrpgMeasure <folder>: plays the game by itself and times it (Docs/PLAN_PLAYTEST_MEASURE_2026-10-12.md).
        // M1 hunting (20 grounds), M2 weekday dungeons and M3 raids (normal + heroic), each alone and with three AI
        // companions (same hero). The hero is driven by 자동 사냥 (QuestAutoPilot, allowed in runs by MeasureAnywhere) at the
        // recommended level with rare +5 gear (epic for raids); damage, potions and deaths are real. Offline.
        // Options: -measureOnly hunting|dungeons|raids, -measureZones a,b (hunting grounds), -measureMinutes N (hunting minutes per row, game time),
        // -measureRaid id, -measureCareers fighter,bishop (raids), -measureAware 1 (bursts 망자의 심판), -measureDodge 0 (old bot: instant step out only), -measureScale N (time scale), -measureOut <file.json>.
        // =====================================================================================================

        const string MeasureDefaultOut = @"C:\Users\admin\Desktop\games\analysis\player-sim\data\dotrpg-measured.json";
        static readonly Career[] MeasureCareers = { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop };

        static string MeasureArg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static float MeasureFloat(string name, float fallback) =>
            float.TryParse(MeasureArg(name, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        IEnumerator MeasureRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1f);
            Game.Config.autosave = false;
            AudioListener.volume = 0f;
            DungeonAuthority.Current = new LocalDungeonAuthority(20261012);
            QuestAutoPilot.MeasureAnywhere = true;
            string only = MeasureArg("-measureOnly", "all");
            float minutes = MeasureFloat("-measureMinutes", 2f);
            float scale = MeasureFloat("-measureScale", 5f);
            // Fixed step (default): every frame is 1/30 s of game time and frames are not capped, so a run goes as fast as
            // the PC renders (finer steps than -measureScale 5, which jumped 0.08 s a frame). -measureFast 0 = the old scale.
            bool fast = MeasureArg("-measureFast", "1") == "1";
            if (fast)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.captureFramerate = 30;
                scale = 1f;
            }
            string outPath = MeasureArg("-measureOut", MeasureDefaultOut);
            var hunting = new List<object>();
            var dungeons = new List<object>();
            var raids = new List<object>();
            float started = Time.realtimeSinceStartup;
            Log($"measure start: only={only} minutes={minutes} scale={scale} out={outPath}");
            if (only == "all" || only == "hunting") yield return MeasureHunting(hunting, minutes, scale);
            if (only == "all" || only == "dungeons") yield return MeasureDungeons(dungeons, scale);
            if (only == "all" || only == "raids") yield return MeasureRaids(raids, scale);
            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            QuestAutoPilot.MeasureAnywhere = false;
            var meta = HRow(("build", Application.version), ("timeScale", fast ? "fixed 1/30" : (object)scale), ("huntMinutes", minutes), ("only", only),
                ("measuredAt", DateTime.Now.ToString("s")), ("wallSeconds", Mathf.RoundToInt(Time.realtimeSinceStartup - started)),
                ("driver", "hunting: auto = QuestAutoPilot 자동 사냥 (skill every 2.4-3.2 s, basic 0.5-0.7 s), skilled = spam main skill + skills on cooldown; dungeons/raids: skilled; -measureDodge 1 (default): 0.25 s late steps out of ground warnings, stands in its 낙인 circle, hides from 섬멸, hits gimmick totems first. Potions under 40% HP / 20% MP"));
            var root = HRow(("meta", meta), ("hunting", hunting), ("dungeons", dungeons), ("raids", raids));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, MiniJson.Write(root));
            Log($"measure done: hunting={hunting.Count} dungeons={dungeons.Count} raids={raids.Count} -> {outPath}");
        }

        // ---------------------------------------------------------------- hero setup

        static string MeasureCareerName(CharacterClass cls, Career career) =>
            career != Career.None ? career.ToString().ToLowerInvariant() : (cls == CharacterClass.Mage ? "mage" : "warrior");

        IEnumerator MeasureHero(CharacterClass cls, Career career, int level, string grade)
        {
            Game.Party?.ClearCompanions();
            Game.Flow.NewGame(cls);
            yield return Wait(.3f);
            while (Game.Flow.IsTransitioning) yield return null;
            MeasureLevel(cls, career, level, grade);
        }

        /// <summary>Sets the hero to <paramref name="level"/> (only upward), promotes, learns and keys career skills, wears that tier's gear.</summary>
        void MeasureLevel(CharacterClass cls, Career career, int level, string grade)
        {
            var p = Game.Session.Progression;
            level = Mathf.Clamp(level, 1, 40);
            if (level > p.Level) p.SetFromServer(level, 0);
            if (career != Career.None && level >= 15)
            {
                if (!p.IsPromoted) p.Promote(career);
                var skills = CareerCatalog.For(career).Where(s => s.kind != CareerSkillKind.Awakening && s.level <= level).ToList();
                foreach (var s in skills) if (p.Rank(s.id) == 0) p.Learn(s.id);
                // Points left: raise the active skills one rank at a time while they last (a player's usual spend).
                for (int guard = 0; guard < 40; guard++)
                {
                    bool any = false;
                    foreach (var s in skills.Where(s => s.kind == CareerSkillKind.Active)) any |= p.Learn(s.id);
                    if (!any) break;
                }
                int[] keys = { 0, 1, 2, 3, 5, 6, 7 };
                int k = 0;
                foreach (var s in skills)
                    if (k < keys.Length && s.kind == CareerSkillKind.Active && p.Rank(s.id) > 0 && p.EquipSkill(keys[k], s.id)) k++;
            }
            int tier = 1;
            foreach (int t in new[] { 1, 10, 15, 20, 25, 30, 35, 40 }) if (t <= level) tier = t;
            bool warrior = cls == CharacterClass.Warrior;
            var bag = Game.Session.Inventory;
            foreach (var gear in new[]
            {
                (warrior ? "eq_sword_" : "eq_staff_") + tier + "_" + grade + "+5",
                (warrior ? "eq_plate_" : "eq_robe_") + tier + "_" + grade,
                (warrior ? "eq_greaves_" : "eq_skirt_") + tier + "_" + grade,
                "eq_neck_" + tier + "_" + grade, "eq_ring_" + tier + "_" + grade,
            })
            {
                if (bag.Count(gear) == 0) bag.Add(gear, 1);
                if (!Game.Session.Equipment.Equip(gear, cls)) Log("measure gear not worn: " + gear);
            }
            bool hi = level >= 20;
            string hp = hi ? ConsumableDatabase.HpPotionHi : ConsumableDatabase.HpPotion, mp = hi ? ConsumableDatabase.MpPotionHi : ConsumableDatabase.MpPotion;
            if (bag.Count(hp) < 200) bag.Add(hp, 300);
            if (bag.Count(mp) < 200) bag.Add(mp, 300);
            if (bag.Count(ConsumableDatabase.Gold) < 50000) bag.Add(ConsumableDatabase.Gold, 100000);
            Game.Player.HealFull();
        }

        /// <summary>Counters of one measured stretch (kills, XP, gold, drops, damage taken, potions, deaths).</summary>
        sealed class MeasureTally
        {
            public int kills, gold, drops, potions, deaths, xp0;
            public float damage;
            PlayerController tracked;
            readonly Inventory bag = Game.Session.Inventory;

            public MeasureTally()
            {
                xp0 = TotalXp();
                GameEvents.EnemyKilled += OnKill;
                bag.Changed += OnBag;
                Track();
            }

            void OnKill(string id) => kills++;

            void OnBag(string id, int total, int delta)
            {
                bool potion = id == ConsumableDatabase.HpPotion || id == ConsumableDatabase.HpPotionHi || id == ConsumableDatabase.MpPotion || id == ConsumableDatabase.MpPotionHi;
                if (potion) { if (delta < 0) potions -= delta; return; }
                if (delta <= 0) return;
                if (id == ConsumableDatabase.Gold) gold += delta;
                else drops += delta;
            }

            void OnDamaged(DamageInfo info) => damage += info.amount;

            /// <summary>Follows the hero's body (rebuilt on map and room changes).</summary>
            public void Track()
            {
                var p = Game.Player;
                if (p == tracked) return;
                if (tracked != null && tracked.Health != null) tracked.Health.Damaged -= OnDamaged;
                tracked = p;
                if (p != null && p.Health != null) p.Health.Damaged += OnDamaged;
            }

            public int Xp => TotalXp() - xp0;

            public void Close()
            {
                GameEvents.EnemyKilled -= OnKill;
                bag.Changed -= OnBag;
                if (tracked != null && tracked.Health != null) tracked.Health.Damaged -= OnDamaged;
            }
        }

        static void MeasureEnsureHunting()
        {
            if (!QuestAutoPilot.Hunting && Game.Player != null && !Game.Player.IsDead && (Game.Flow == null || !Game.Flow.IsTransitioning)) QuestAutoPilot.ToggleHunt();
        }

        /// <summary>GameState sets the time scale back to 1 on state changes; keep the fast-forward on (slow-motion moments stay).</summary>
        static void MeasureScale(float scale)
        {
            if (Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = scale;
        }

        static float R1(float v) => Mathf.Round(v * 10f) / 10f;
        static float R2(float v) => Mathf.Round(v * 100f) / 100f;

        // ---------------------------------------------------------------- M1 hunting

        IEnumerator MeasureHunting(List<object> rows, float minutes, float scale)
        {
            // One hero per class (before the career) or career, levelled up zone by zone (a new game per row is slow).
            // -measureZones a,b,c: only these hunting grounds (re-measuring a few with more minutes).
            var zoneFilter = MeasureArg("-measureZones", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var plans = new List<(CharacterClass cls, Career career)> { (CharacterClass.Warrior, Career.None), (CharacterClass.Mage, Career.None) };
            plans.AddRange(MeasureCareers.Select(c => (CareerCatalog.Base(c), c)));
            foreach (var (cls, career) in plans)
            {
                bool made = false;
                foreach (var zone in HuntingGrounds.All.OrderBy(z => z.monsterLevel))
                {
                    int lv = Mathf.Max(1, zone.monsterLevel);
                    if ((career == Career.None) != (lv < 15)) continue;
                    if (zoneFilter.Length > 0 && System.Array.IndexOf(zoneFilter, zone.id) < 0) continue;
                    if (!made) { yield return MeasureHero(cls, career, lv, "r"); made = true; }
                    else MeasureLevel(cls, career, lv, "r");
                    Game.Flow.TravelTo(zone.id, true);
                    yield return Wait(.3f);
                    while (Game.Flow.IsTransitioning) yield return null;
                    foreach (bool skilled in new[] { false, true })
                    {
                        // auto = 자동 사냥 as players get it; skilled = a practised player (spam skill, skills on cooldown).
                        QuestAutoPilot.MeasureSkilled = skilled;
                        Game.Player.HealFull();
                        var tally = new MeasureTally();
                        Time.timeScale = scale;
                        float t0 = Time.time;
                        while (Time.time - t0 < minutes * 60f)
                        {
                            tally.Track();
                            MeasureScale(scale);
                            var p = Game.Player;
                            if (p != null && p.IsDead)
                            {
                                tally.deaths++;
                                p.Revive(1f, 2f);
                            }
                            MeasureEnsureHunting();
                            yield return null;
                        }
                        float mins = (Time.time - t0) / 60f;
                        Time.timeScale = 1f;
                        QuestAutoPilot.Stop("");
                        tally.Close();
                        string name = MeasureCareerName(cls, career);
                        string driver = skilled ? "skilled" : "auto";
                        rows.Add(HRow(("zone", zone.id), ("name", zone.name), ("mlv", zone.monsterLevel), ("career", name), ("level", Game.Session.Progression.Level),
                            ("driver", driver), ("minutes", R2(mins)),
                            ("kills_per_min", R2(tally.kills / mins)), ("xp_per_min", R1(tally.Xp / mins)), ("gold_per_min", R1(tally.gold / mins)),
                            ("drops_per_min", R2(tally.drops / mins)), ("damage_taken_per_min", R1(tally.damage / mins)),
                            ("potions_per_min", R2(tally.potions / mins)), ("deaths", tally.deaths), ("hero_hp", Game.Player != null ? Game.Player.Health.Max : 0)));
                        Log($"hunt {zone.id} Lv{lv} {name} {driver}: kills/min {tally.kills / mins:0.0} xp/min {tally.Xp / mins:0} dmg/min {tally.damage / mins:0} potions {tally.potions} deaths {tally.deaths}");
                    }
                    QuestAutoPilot.MeasureSkilled = false;
                }
            }
        }

        // ---------------------------------------------------------------- M2 / M3 runs

        /// <summary>
        /// One dungeon or raid run, played to the end by 자동 사냥: clears each room, walks through the gate, revives with
        /// the run's revives. Returns the row (result clear / fail / timeout / entry_failed).
        /// </summary>
        IEnumerator MeasureOneRun(DungeonDef def, DungeonDifficulty diff, int companions, float scale, Dictionary<string, object> row)
        {
            var party = Game.Party;
            party.ClearCompanions();
            string[] mercs = { "merc_bron", "merc_kai", "merc_elin" };
            for (int i = 0; i < companions && i < mercs.Length; i++) party.AddCompanion(mercs[i]);
            yield return Wait(.5f);
            Game.Player.HealFull();
            var select = Game.UI.Dungeon;
            select.Open(false);
            yield return Wait(.3f);
            bool picked = select.DevSelect(def.id, diff);
            bool entered = picked && select.DevEnter();
            yield return Wait(1f);
            while (Game.Flow.IsTransitioning) yield return null;
            var dir = Game.Dungeon;
            var run = dir.Run;
            if (!entered || run == null)
            {
                row["result"] = "entry_failed";
                row["note"] = select.DevStatus;
                Log($"run {def.id} {diff}: entry failed '{select.DevStatus}'");
                select.Close();
                yield break;
            }
            QuestAutoPilot.MeasureSkilled = true; // runs are played by hand: time a practised player
            QuestAutoPilot.MeasureAware = MeasureArg("-measureAware", "") == "1"; // knows the raid (bursts 망자의 심판)
            QuestAutoPilot.MeasureDodge = MeasureArg("-measureDodge", "1") != "0"; // answers 낙인 / 섬멸 / ground warnings (QuestAutoPilot.Dodge.cs)
            QuestAutoPilot.MeasureDodges = QuestAutoPilot.MeasureCoverMoves = 0;
            row["play"] = QuestAutoPilot.MeasureAware ? "aware" : "skilled";
            row["dodge"] = QuestAutoPilot.MeasureDodge;
            // Ground hits on the hero (warnings that resolved with the hero inside) and 섬멸 blasts sat out in cover or not.
            int groundHits = 0, coverOk = 0, coverMiss = 0;
            bool annihilating = false, coveredLast = false;
            Action<Telegraph, List<PlayerController>> onResolve = (t, inside) => { if (t.damage > 0 && Game.Player != null && inside.Contains(Game.Player)) groundHits++; };
            Telegraph.AnyResolved += onResolve;
            var tally = new MeasureTally();
            Time.timeScale = scale;
            float bossAt = -1f, limit = def.isRaid ? 1800f : 900f;
            // Boss mechanics (Docs/PLAN_BOSS_DIFFICULTY_2026-10-13.md): groggies and 망자의 심판 broken / landed.
            var hooked = new HashSet<EnemyController>();
            int groggies = 0, judgBroken = 0, judgLanded = 0;
            float judgLeft = 0f;
            var brains = new List<BossBrain>();
            int sealOk = 0, sealFail = 0; // counted from the event: a dead boss's counters are gone with it
            Action<EnemyController, bool> onSeal = (_, broken) => { if (broken) sealOk++; else sealFail++; };
            RaidSeal.Ended += onSeal;
            bool wasDead = false;
            while (!run.IsOver && run.Elapsed < limit)
            {
                tally.Track();
                foreach (var e in EnemyController.Active)
                    if (e != null && e.IsBoss && hooked.Add(e))
                    {
                        e.GroggyStarted += _ => groggies++;
                        if (e.Behaviour is BossBrain bb)
                        {
                            bb.JudgmentEnded += (b2, broken) => { if (broken) judgBroken++; else { judgLanded++; judgLeft += b2.LastJudgmentLeft; } };
                            brains.Add(bb);
                        }
                    }
                MeasureScale(scale);
                var p = Game.Player;
                if (SafeCover.Annihilating) { annihilating = true; coveredLast = p != null && SafeCover.Shelters(SafeCover.AnnihilateSource, p); }
                else if (annihilating) { annihilating = false; if (coveredLast) coverOk++; else coverMiss++; }
                if (run.InBossRoom && bossAt < 0f) bossAt = run.Elapsed;
                if (p != null && p.IsDead)
                {
                    // Count each fall once, also the last one with no revive left (the run then fails).
                    if (!wasDead) tally.deaths++;
                    wasDead = true;
                    if (dir.ReviveOpen && run.RevivesLeft > 0) dir.DevRevive();
                }
                else if (!Game.Flow.IsTransitioning)
                {
                    wasDead = false;
                    if (!EnemyController.Active.Any(e => e != null && !e.IsDead && e.isActiveAndEnabled)) dir.DevEnterDoor();
                    MeasureEnsureHunting();
                }
                yield return null;
            }
            Time.timeScale = 1f;
            QuestAutoPilot.Stop("");
            QuestAutoPilot.MeasureSkilled = false;
            QuestAutoPilot.MeasureAware = false;
            QuestAutoPilot.MeasureDodge = false;
            Telegraph.AnyResolved -= onResolve;
            tally.Close();
            string result = run.State == DungeonRunState.Cleared ? "clear" : run.State == DungeonRunState.Failed ? "fail" : "timeout";
            row["result"] = result;
            row["clear_s"] = R1(run.Elapsed);
            row["boss_s"] = bossAt >= 0f ? R1(run.Elapsed - bossAt) : 0f;
            row["deaths"] = tally.deaths;
            row["revives"] = run.RevivesUsed;
            row["potions"] = tally.potions;
            row["damage_taken"] = Mathf.RoundToInt(tally.damage);
            row["hero_hp"] = Game.Player != null ? Game.Player.Health.Max : 0;
            row["groggies"] = groggies;
            row["judgment_broken"] = judgBroken;
            row["judgment_landed"] = judgLanded;
            row["judgment_left"] = judgLanded > 0 ? R1(100f * judgLeft / judgLanded) : 0f;
            RaidSeal.Ended -= onSeal;
            row["seal_broken"] = sealOk;
            row["seal_failed"] = sealFail;
            row["ground_hits"] = groundHits;
            row["dodges"] = QuestAutoPilot.MeasureDodges;
            row["cover_moves"] = QuestAutoPilot.MeasureCoverMoves;
            row["annihilate_covered"] = coverOk;
            row["annihilate_hit"] = coverMiss;
            if (result == "timeout") dir.GiveUp();
            yield return Wait(DungeonDirector.SlowMotionSeconds + DungeonDirector.ClearHoldSeconds + 1f);
            // The reward cards do not matter here: straight back to the village (the result screen waits on its flips).
            for (int tries = 0; tries < 5 && dir.InRun; tries++)
            {
                dir.ExitToVillage(false);
                yield return Wait(1f);
                while (Game.Flow.IsTransitioning) yield return null;
            }
            if (Game.UI.Top != null) Game.Flow.CloseInventory();
            if (dir.InRun) Log($"run {def.id} {diff}: still in the run after leaving");
            party.ClearCompanions();
            Log($"run {def.id} {diff} x{companions}: {result} {run.Elapsed:0}s boss {(bossAt >= 0 ? run.Elapsed - bossAt : 0):0}s deaths {tally.deaths} potions {tally.potions} groggy {groggies} judgment {judgBroken}/{judgBroken + judgLanded} left {(judgLanded > 0 ? 100f * judgLeft / judgLanded : 0f):0}% seal {sealOk}/{sealOk + sealFail} ground hits {groundHits} dodges {QuestAutoPilot.MeasureDodges} annihilate {coverOk}/{coverOk + coverMiss}");
        }

        IEnumerator MeasureDungeons(List<object> rows, float scale)
        {
            DateTime clock = new DateTime(2026, 10, 17, 12, 0, 0); // a Saturday: every weekday dungeon is open
            ResetClock.NowOverride = () => clock;
            int dungeonIndex = 0;
            foreach (var def in DungeonDatabase.Weekday)
            {
                for (int d = 0; d < DungeonDatabase.DifficultyCount; d++)
                {
                    var diff = (DungeonDifficulty)d;
                    int lv = DungeonDatabase.Difficulty(diff).recommendedLevel;
                    // Career rotates with the dungeon, not the difficulty: each difficulty sees every career across the five
                    // dungeons (before, 왕 was always the arcanist and 영웅 always the bishop).
                    int pick = dungeonIndex + d;
                    var career = lv >= 15 ? MeasureCareers[pick % 4] : Career.None;
                    var cls = career != Career.None ? CareerCatalog.Base(career) : (pick % 2 == 0 ? CharacterClass.Warrior : CharacterClass.Mage);
                    // Same hero alone and with three AI, so the two rows compare (2026-10-13).
                    foreach (int companions in new[] { 0, 3 })
                    {
                        yield return MeasureHero(cls, career, lv, "r");
                        for (int below = 0; below < d; below++) Game.Session.Dungeons.RecordClear(def.id, (DungeonDifficulty)below, DungeonRank.A);
                        var row = HRow(("id", def.id), ("name", def.name), ("difficulty", d), ("level", lv), ("career", MeasureCareerName(cls, career)),
                            ("party", companions == 0 ? "solo" : "1+3ai"));
                        yield return MeasureOneRun(def, diff, companions, scale, row);
                        rows.Add(row);
                        clock = clock.AddDays(7);
                    }
                }
                dungeonIndex++;
            }
            ResetClock.NowOverride = null;
        }

        IEnumerator MeasureRaids(List<object> rows, float scale)
        {
            DateTime clock = new DateTime(2026, 10, 17, 12, 0, 0); // Saturday: raids open (수·토·일)
            ResetClock.NowOverride = () => clock;
            string onlyRaid = MeasureArg("-measureRaid", "");
            // -measureCareers fighter,bishop: only these careers (each career runs alone and with three AI, so the rows compare).
            var careerFilter = MeasureArg("-measureCareers", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var careers = MeasureCareers.Where(c => careerFilter.Length == 0 || careerFilter.Contains(c.ToString().ToLowerInvariant())).ToArray();
            foreach (var def in DungeonDatabase.Raids.Where(r => onlyRaid == "" || r.id == onlyRaid)) // the open raids (해골왕, 그라흐); 바르가스·바위 심장 are not open yet
                foreach (var diff in new[] { DungeonDifficulty.Normal, DungeonDatabase.RaidHeroic })
                    foreach (var career in careers)
                    foreach (int companions in new[] { 0, 3 })
                    {
                        var numbers = DungeonDatabase.DifficultyFor(def, diff);
                        int lv = numbers.recommendedLevel;
                        var cls = CareerCatalog.Base(career);
                        yield return MeasureHero(cls, career, lv, "e");
                        if (!string.IsNullOrEmpty(def.unlockQuest)) Game.Session.Journal.State(def.unlockQuest).status = (int)QuestStatus.Active;
                        if (diff != DungeonDifficulty.Normal) Game.Session.Dungeons.RecordClear(def.id, DungeonDifficulty.Normal, DungeonRank.A);
                        if (def.keyCost > 0) Game.Session.Inventory.Add(DungeonDatabase.SealKey, def.keyCost); // a final raid without keys asks to confirm a practice run
                        var row = HRow(("id", def.id), ("name", def.name), ("difficulty", diff == DungeonDifficulty.Normal ? 0 : 1), ("level", lv),
                            ("career", MeasureCareerName(cls, career)), ("party", companions == 0 ? "solo" : "1+3ai"));
                        // Each raid on one of its own open days (the final raids open on Sundays only).
                        while (def.openDays != null && def.openDays.Length > 0 && Array.IndexOf(def.openDays, clock.DayOfWeek) < 0) clock = clock.AddDays(1);
                        yield return MeasureOneRun(def, diff, companions, scale, row);
                        rows.Add(row);
                        clock = clock.AddDays(7);
                    }
            ResetClock.NowOverride = null;
        }
    }
}
