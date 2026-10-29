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
        // Options: -measureOnly hunting|dungeons|raids|bossrush (-measureSets raid,clear), -measureZones a,b (hunting grounds), -measureMinutes N (hunting minutes per row, game time),
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
            MeasurePortalDirections();
            MapPortal.MeasureInwardBlocks = 0;
            string only = MeasureArg("-measureOnly", "all");
            float minutes = MeasureFloat("-measureMinutes", 2f);
            float scale = MeasureFloat("-measureScale", 5f);
            // Fixed step (default 30 fps, configurable with -measureFps): frames are not capped, so a run goes as fast as
            // the PC renders (finer steps than -measureScale 5, which jumped 0.08 s a frame). -measureFast 0 = the old scale.
            bool fast = MeasureArg("-measureFast", "1") == "1";
            int measureFps = Mathf.Clamp(Mathf.RoundToInt(MeasureFloat("-measureFps", 30f)), 15, 240);
            if (fast)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.captureFramerate = measureFps;
                scale = 1f;
            }
            string outPath = MeasureArg("-measureOut", MeasureDefaultOut);
            var hunting = new List<object>();
            var dungeons = new List<object>();
            var raids = new List<object>();
            var bossrush = new List<object>();
            float started = Time.realtimeSinceStartup;
            MeasureChapter3Open(); // [MEASURE CH3] -measureChapter3 1 (DevCapture.Measure.Ch3.cs)
            measureCheckpoint = () =>
            {
                var partialMeta = HRow(("driverRevision", "decision-trace-14-ninja-leap-fixture"), ("partial", true),
                    ("seedBase", MeasureArg("-measureSeed", "20261026")), ("huntSeedIdentity", "base+zone+level; shared across classes and drivers"), ("ninjaLeapFixture", MeasureArg("-measureNinjaLeapFixture", "0")), ("timeScale", fast ? $"fixed 1/{measureFps}" : (object)scale));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath + ".partial.json", MiniJson.Write(HRow(("meta", partialMeta), ("hunting", hunting),
                    ("dungeons", dungeons), ("raids", raids), ("bossrush", bossrush))));
            };
            Log($"measure start: only={only} minutes={minutes} scale={scale} out={outPath}");
            if (only == "all" || only == "hunting") yield return MeasureHunting(hunting, minutes, scale);
            if (only == "all" || only == "dungeons") yield return MeasureDungeons(dungeons, scale);
            if (only == "all" || only == "raids") yield return MeasureRaids(raids, scale);
            if (only == "bossrush") yield return MeasureBossRush(bossrush, scale); // [BOSS] DevCapture.Measure.BossRush.cs, not part of "all"
            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            QuestAutoPilot.MeasureAnywhere = false;
            int levelCap = Progression.LevelCap;
            MeasureChapter3Restore();
            var meta = HRow(("build", Application.version), ("timeScale", fast ? $"fixed 1/{measureFps}" : (object)scale), ("huntMinutes", minutes), ("only", only), ("levelCap", levelCap),
                ("driverRevision", "decision-trace-14-ninja-leap-fixture"), ("seedBase", MeasureArg("-measureSeed", "20261026")), ("huntSeedIdentity", "base+zone+level; shared across classes and drivers"), ("ninjaLeapFixture", MeasureArg("-measureNinjaLeapFixture", "0")), ("portal_inward_frames_blocked", MapPortal.MeasureInwardBlocks), ("measureDodge", MeasureArg("-measureDodge", "1")),
                ("measuredAt", DateTime.Now.ToString("s")), ("wallSeconds", Mathf.RoundToInt(Time.realtimeSinceStartup - started)),
                ("driver", "hunting: auto = QuestAutoPilot 자동 사냥 (skill every 1.6-2.2 s, basic 0.5-0.7 s), skilled = spam main skill + skills on cooldown; dungeons/raids: skilled; -measureDodge 1 (default): 0.25 s late steps out of ground warnings, stands in its 낙인 circle, hides from 섬멸, hits gimmick totems first. Potions under 40% HP / 20% MP"));
            var root = HRow(("meta", meta), ("hunting", hunting), ("dungeons", dungeons), ("raids", raids), ("bossrush", bossrush));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, MiniJson.Write(root));
            Log($"measure done: hunting={hunting.Count} dungeons={dungeons.Count} raids={raids.Count} bossrush={bossrush.Count} -> {outPath}");
        }

        // ---------------------------------------------------------------- hero setup

        static string MeasureCareerName(CharacterClass cls, Career career) =>
            career != Career.None ? career.ToString().ToLowerInvariant() : cls == CharacterClass.Mage ? "mage" : cls == CharacterClass.Rogue ? "rogue" : "warrior";

        void MeasurePortalDirections()
        {
            var bounds = new Rect(0f, 0f, 56f, 48f);
            int checks = 0;
            void Check(bool value, string label)
            {
                if (!value) throw new InvalidOperationException("Portal direction regression: " + label);
                checks++;
            }
            foreach (var edge in new[] {
                (new Vector2(.5f,24.5f), Vector2.left), (new Vector2(55.5f,24.5f), Vector2.right),
                (new Vector2(28.5f,.5f), Vector2.down), (new Vector2(28.5f,47.5f), Vector2.up) })
            {
                Check(MapPortal.AllowsTravel(bounds, edge.Item1, edge.Item1 - edge.Item2 * .4f, edge.Item2), "outward " + edge.Item2);
                Check(!MapPortal.AllowsTravel(bounds, edge.Item1, edge.Item1 + edge.Item2 * .2f, -edge.Item2), "inward " + edge.Item2);
            }
            Check(!MapPortal.AllowsTravel(bounds, new Vector2(.5f,25.5f), new Vector2(.3f,25.12f), new Vector2(1.82f,-.94f)), "observed diagonal inward crossing");
            Check(MapPortal.AllowsTravel(bounds, new Vector2(26.5f,25.5f), new Vector2(26.5f,24.5f), Vector2.up), "interior stair approach");
            Check(!MapPortal.AllowsTravel(bounds, new Vector2(26.5f,25.5f), new Vector2(26.5f,24.5f), Vector2.down), "interior stair departure");
            Log($"portal direction regression: {checks} passed");
        }

        Action measureCheckpoint;
        sealed class MeasureIdleInput : IActorInput
        {
            public ActorCommand Read(PlayerController self) => ActorCommand.None;
        }
        static void MeasureOwnInput()
        {
            if (Game.Player != null && !QuestAutoPilot.Drives(Game.Player)) Game.Player.Input = new MeasureIdleInput();
        }

        string MeasureWorldTrace() => $"state={Game.State?.Current} map={Game.World?.MapId} transition={Game.Flow?.IsTransitioning} top={Game.UI?.Top?.name} dialogue={Game.Dialogue?.IsOpen} dead={Game.Player?.IsDead} scale={Time.timeScale} pos={Game.Player?.Position} input={Game.Player?.Input?.GetType().Name} hunt={QuestAutoPilot.Hunting}";

        void MeasureResumeField()
        {
            MeasureOwnInput();
            if (Game.Flow.IsTransitioning) return;
            if (!Game.IsPlaying) Log("measure field state before resume: " + MeasureWorldTrace());
            if (Game.Dialogue.IsOpen) Game.Dialogue.Close();
            if (Game.State.Current == GameState.Inventory) Game.Flow.CloseInventory();
            if (Game.State.Current == GameState.Paused) Game.Flow.Resume();
            if (Game.State.Current == GameState.GameOver && Game.Player != null && !Game.Player.IsDead)
                Game.State.Set(GameState.Playing);
        }

        bool measureTravelSucceeded;
        IEnumerator MeasureTravel(string zone)
        {
            QuestAutoPilot.Stop("");
            measureTravelSucceeded = false;
            float deadline = Time.realtimeSinceStartup + 60f, nextRequest = 0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                MeasureResumeField();
                if (!Game.Flow.IsTransitioning && Game.World?.MapId == zone)
                {
                    measureTravelSucceeded = true;
                    yield break;
                }
                if (!Game.Flow.IsTransitioning && Time.realtimeSinceStartup >= nextRequest)
                {
                    Log($"measure travel {zone}: {MeasureWorldTrace()}");
                    Game.Flow.TravelTo(zone, true);
                    nextRequest = Time.realtimeSinceStartup + 2f;
                }
                yield return null;
            }
            Log($"measure travel timeout {zone}: {MeasureWorldTrace()}");
        }

        int measureRowSeed;
        void MeasureSeedRow(string identity)
        {
            int.TryParse(MeasureArg("-measureSeed", "20261026"), out int seedBase);
            unchecked
            {
                uint hash = (uint)seedBase;
                foreach (char c in identity) hash = (hash ^ c) * 16777619u;
                measureRowSeed = (int)(hash & 0x7fffffffu);
            }
            UnityEngine.Random.InitState(measureRowSeed);
        }

        void MeasureReseed() => UnityEngine.Random.InitState(measureRowSeed);

        IEnumerator MeasureHero(CharacterClass cls, Career career, int level, string grade)
        {
            QuestAutoPilot.Stop("");
            while (Game.Flow.IsTransitioning) { MeasureOwnInput(); yield return null; }
            Game.Party?.ClearCompanions();
            Game.Flow.NewGame(cls);
            yield return null;
            yield return Wait(.3f);
            while (Game.Flow.IsTransitioning) yield return null;
            MeasureLevel(cls, career, level, grade);
            MeasureOwnInput();
        }

        /// <summary>Sets the hero to <paramref name="level"/> (only upward), promotes, learns and keys career skills, wears that tier's gear.</summary>
        void MeasureLevel(CharacterClass cls, Career career, int level, string grade)
        {
            var p = Game.Session.Progression;
            level = Mathf.Clamp(level, 1, Progression.LevelCap); // [MEASURE CH3] 50 with chapter 3 open
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
            int tier = MeasureGearTier(level); // [MEASURE CH3] Lv45 / Lv50 tiers too
            var (weapon, body, legs) = MeasureGearStems(cls); // [ROGUE] dagger + plate + greaves
            var bag = Game.Session.Inventory;
            foreach (var gear in new[]
            {
                weapon + tier + "_" + grade + "+5",
                body + tier + "_" + grade,
                legs + tier + "_" + grade,
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

        static void MeasureEnsureHunting()
        {
            MeasureOwnInput();
            if (!QuestAutoPilot.Hunting && Game.Player != null && !Game.Player.IsDead && (Game.Flow == null || !Game.Flow.IsTransitioning)) QuestAutoPilot.ToggleHunt();
        }

        /// <summary>GameState sets the time scale back to 1 on state changes; keep the fast-forward on (slow-motion moments stay).</summary>
        static void MeasureScale(float scale)
        {
            if (Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = scale;
        }

        static float R1(float v) => Mathf.Round(v * 10f) / 10f;
        static float R2(float v) => Mathf.Round(v * 100f) / 100f;

        static void MeasureTraceRow(Dictionary<string, object> row)
        {
            row["decision_seconds"] = QuestAutoPilot.MeasureDecisionSeconds.ToDictionary(x => x.Key, x => (object)R2(x.Value));
            row["skill_requests"] = QuestAutoPilot.MeasureSkillRequests.ToDictionary(x => x.Key, x => (object)x.Value);
            if (Game.Session != null)
            {
                var skills = new List<object>();
                for (int slot = 0; slot < 8; slot++)
                {
                    var gem = Game.Session.Progression.Active(slot);
                    if (gem != null) skills.Add(HRow(("slot", slot), ("id", gem.id), ("rank", Game.Session.Progression.Rank(gem.id))));
                }
                row["equipped_skills"] = skills;
            }
        }

        // ---------------------------------------------------------------- M1 hunting

        IEnumerator MeasureHunting(List<object> rows, float minutes, float scale)
        {
            // A fresh hero per driver prevents XP and skill state carrying into the next row.
            // -measureZones a,b,c: only these hunting grounds (re-measuring a few with more minutes).
            var zoneFilter = MeasureArg("-measureZones", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var plans = MeasureHuntPlans(); // [MEASURE CH3] -measureCareers also picks warrior / mage / rogue
            // Chapter 3 grounds only with the chapter open (closed, the map does not load).
            var zones = HuntingGrounds.All.Where(z => Chapter3.Enabled || !MapRegistry.Chapter3Maps.Contains(z.id)).OrderBy(z => z.monsterLevel).ToList();
            foreach (var (cls, career) in plans)
            {
                foreach (var zone in zones)
                {
                    int lv = Mathf.Clamp(zone.monsterLevel, 1, Progression.LevelCap);
                    if ((career == Career.None) != (lv < 15)) continue;
                    if (zoneFilter.Length > 0 && System.Array.IndexOf(zoneFilter, zone.id) < 0) continue;
                    foreach (bool skilled in new[] { false, true })
                    {
                        MeasureSeedRow($"hunt:{zone.id}:{lv}");
                        yield return MeasureHero(cls, career, lv, "r");
                        // Optional QA fixture only: preserve the standard loadout unless explicitly requested.
                        bool ninjaLeapFixture = MeasureArg("-measureNinjaLeapFixture", "0") == "1";
                        if (ninjaLeapFixture && (career != Career.Ninja || !Game.Session.Progression.EquipSkill(0, "leap")))
                            throw new System.InvalidOperationException("Ninja leap fixture requires a promoted ninja and an unlocked leap skill.");
                        MeasureReseed();
                        yield return MeasureTravel(zone.id);
                        int startLevel = Game.Session.Progression.Level;
                        bool setupValid = measureTravelSucceeded && startLevel == lv && Game.Session.PlayerClass == cls;
                        if (!setupValid) Log($"hunt setup mismatch: intended {cls} Lv{lv}, actual {Game.Session.PlayerClass} Lv{startLevel}");
                        MeasureReseed();
                        // auto = 자동 사냥 as players get it; skilled = a practised player (spam skill, skills on cooldown).
                        QuestAutoPilot.MeasureSkilled = skilled;
                        QuestAutoPilot.ResetMeasureTrace();
                        Game.Player.HealFull();
                        var tally = new MeasureTally();
                        Time.timeScale = scale;
                        float t0 = Time.time, nextHuntTrace = 30f;
                        float rowDeadline = Time.realtimeSinceStartup + Mathf.Max(240f, minutes * 180f);
                        bool rowTimedOut = false;
                        int strays = 0;
                        int ninjaLeapRequestsStart = QuestAutoPilot.NinjaLeapRequests;
                        int ninjaLeapSkipsStart = QuestAutoPilot.NinjaLeapNoTargetSkips;
                        float recoverySeconds = 0f;
                        bool recoveryFailed = false;
                        while (setupValid && Time.time - t0 < minutes * 60f)
                        {
                            if (Time.realtimeSinceStartup >= rowDeadline) { rowTimedOut = true; Log("hunt watchdog: " + MeasureWorldTrace()); break; }
                            if (!Game.IsPlaying && !Game.Flow.IsTransitioning) Log("hunt resume: " + MeasureWorldTrace());
                            MeasureResumeField();
                            tally.Track();
                            if (Time.time - t0 >= nextHuntTrace)
                            {
                                // [MEASURE CH3] Every 30 game seconds: what the driver does (a row with no kills shows why).
                                nextHuntTrace += 30f;
                                int near = EnemyController.Active.Count(e => e != null && !e.IsDead && e.isActiveAndEnabled);
                                Log($"  t={Time.time - t0:0}s kills={tally.kills} enemies={near} map={Game.World?.MapId} hunting={QuestAutoPilot.Hunting} active={QuestAutoPilot.Active} auto='{QuestAutoPilot.Status}'");
                            }
                            MeasureScale(scale);
                            var p = Game.Player;
                            if (p != null && p.IsDead)
                            {
                                tally.deaths++;
                                p.Revive(1f, 2f);
                            }
                            // [MEASURE CH3] The hunt walked off the ground (the 메마른 협곡 grounds open onto 사얍): back in, as a player would.
                            if (Game.World != null && Game.World.MapId != zone.id && !Game.Flow.IsTransitioning && p != null && !p.IsDead)
                            {
                                strays++;
                                QuestAutoPilot.Stop("");
                                float recoveryStart = Time.time;
                                yield return MeasureTravel(zone.id);
                                recoverySeconds += Time.time - recoveryStart;
                                if (!measureTravelSucceeded)
                                {
                                    recoveryFailed = true;
                                    Log("measure: hunt recovery failed for " + zone.id);
                                    break;
                                }
                                // Resume only after the destination body exists; keep recovery time in the measured rate.
                                tally.Track();
                                MeasureEnsureHunting();
                                continue;
                            }
                            MeasureEnsureHunting();
                            yield return null;
                        }
                        float mins = Mathf.Max(.001f, (Time.time - t0) / 60f);
                        Time.timeScale = 1f;
                        QuestAutoPilot.Stop("");
                        tally.Close();
                        string name = MeasureCareerName(cls, career);
                        string driver = skilled ? "skilled" : "auto";
                        var huntRow = HRow(("zone", zone.id), ("name", zone.name), ("mlv", zone.monsterLevel), ("career", name), ("level", startLevel), ("intended_level", lv), ("start_level", startLevel), ("end_level", Game.Session.Progression.Level), ("seed", measureRowSeed),
                            ("driver", driver), ("minutes", R2(mins)), ("qa_fixture", ninjaLeapFixture ? "ninja-leap-slot0" : "standard"),
                            ("kills_per_min", R2(tally.kills / mins)), ("xp_per_min", R1(tally.Xp / mins)), ("gold_per_min", R1(tally.gold / mins)),
                            ("drops_per_min", R2(tally.drops / mins)), ("damage_taken_per_min", R1(tally.damage / mins)),
                            ("potions_per_min", R2(tally.potions / mins)), ("deaths", tally.deaths), ("hero_hp", Game.Player != null ? Game.Player.Health.Max : 0), ("strays", strays),
                            ("ninja_leap_requests", QuestAutoPilot.NinjaLeapRequests - ninjaLeapRequestsStart),
                            ("ninja_leap_no_target_skips", QuestAutoPilot.NinjaLeapNoTargetSkips - ninjaLeapSkipsStart),
                            ("recovery_s", R1(recoverySeconds)), ("valid", setupValid && !recoveryFailed && !rowTimedOut), ("row_timeout", rowTimedOut));
                        MeasureTraceRow(huntRow);
                        rows.Add(huntRow);
                        measureCheckpoint?.Invoke();
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
            MeasureReseed();
            row["seed"] = measureRowSeed;
            bool entered = picked && select.DevEnter();
            Log($"run {def.id} {diff}: picked {picked} entered {entered} '{select.DevStatus}'");
            yield return Wait(1f);
            while (Game.Flow.IsTransitioning) yield return null;
            // The select window can come back over the room (seen on the 해골왕 raid, 2026-10-20): nobody closes it here,
            // and an open window keeps the world (and the run clock) paused.
            if (Game.UI.Top == select) { Log($"run {def.id}: select window still open after entering, closing it"); select.Close(); }
            if (Game.UI.Top != null) Game.Flow.CloseInventory();
            var dir = Game.Dungeon;
            var run = dir.Run;
            if (!entered || run == null)
            {
                row["result"] = "entry_failed";
                row["note"] = select.DevStatus;
                Log($"run {def.id} {diff}: entry failed '{select.DevStatus}' transit={Game.Flow.IsTransitioning} {dir.DevRoomTrace} inRun={dir.InRun}");
                select.Close();
                yield break;
            }
            QuestAutoPilot.MeasureSkilled = true; // runs are played by hand: time a practised player
            QuestAutoPilot.MeasureAware = MeasureArg("-measureAware", "") == "1"; // knows the raid (bursts 망자의 심판)
            QuestAutoPilot.MeasureDodge = MeasureArg("-measureDodge", "1") != "0"; // answers 낙인 / 섬멸 / ground warnings (QuestAutoPilot.Dodge.cs)
            QuestAutoPilot.ResetMeasureTrace();
            RaidRift.ResetCounters(); // [CH3] 공허 균열 / 잿빛 낙인 counters for this row
            GreyBrand.ResetCounters();
            row["play"] = QuestAutoPilot.MeasureAware ? "aware" : "skilled";
            row["dodge"] = QuestAutoPilot.MeasureDodge;
            // Ground hits on the hero (warnings that resolved with the hero inside) and 섬멸 blasts sat out in cover or not.
            int groundHits = 0, coverOk = 0, coverMiss = 0;
            int ruinStarted = BossBrain.RuinStarted, ruinBroken = BossBrain.RuinBroken, ruinFailed = BossBrain.RuinFailed;
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
            float nextTrace = Time.realtimeSinceStartup + 60f;
            while (!run.IsOver && run.Elapsed < limit)
            {
                tally.Track();
                if (Time.realtimeSinceStartup >= nextTrace)
                {
                    // A trace a real minute: a run that never ends (or a paused world) shows where it stands.
                    nextTrace += 60f;
                    Log($"  state={Game.State?.Current} world={Game.IsWorldRunning} top={(Game.UI?.Top != null ? Game.UI.Top.name : "-")} scale={Time.timeScale} run={run.State}");
                    int alive = EnemyController.Active.Count(e => e != null && !e.IsDead && e.isActiveAndEnabled);
                    Log($"  {dir.DevRoomTrace}");
                    Log($"  t={run.Elapsed:0}s room boss={run.InBossRoom} enemies={alive} dead={Game.Player?.IsDead} transit={Game.Flow.IsTransitioning} pos={Game.Player?.Position} auto='{QuestAutoPilot.Status}'");
                }
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
            if (def.chapter >= 3)
            {
                // [CH3] Gimmicks played without bot code (RaidDoom spots): rifts opened / closed / heals that landed, brands wiped / halved / landed.
                row["rift_opened"] = RaidRift.Opened;
                row["rift_closed"] = RaidRift.Closed;
                row["rift_heals"] = RaidRift.Heals;
                row["brand_started"] = GreyBrand.Started;
                row["brand_cleared"] = GreyBrand.Cleared;
                row["brand_halved"] = GreyBrand.Halved;
                row["brand_missed"] = GreyBrand.Missed;
            }
            row["judgment_broken"] = judgBroken;
            row["judgment_landed"] = judgLanded;
            row["judgment_left"] = judgLanded > 0 ? R1(100f * judgLeft / judgLanded) : 0f;
            RaidSeal.Ended -= onSeal;
            row["seal_broken"] = sealOk;
            row["seal_failed"] = sealFail;
            row["ground_hits"] = groundHits;
            row["ruin_started"] = BossBrain.RuinStarted - ruinStarted;
            row["ruin_broken"] = BossBrain.RuinBroken - ruinBroken;
            row["ruin_failed"] = BossBrain.RuinFailed - ruinFailed;
            row["dodges"] = QuestAutoPilot.MeasureDodges;
            MeasureTraceRow(row);
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
            var rotation = MeasureCareerList(MeasureCareers); // [MEASURE CH3] -measureCareers
            foreach (var def in DungeonDatabase.Weekday)
            {
                for (int d = 0; d < DungeonDatabase.ShownDifficultyCount(def); d++) // [CH3] 군주 only with chapter 3 open
                {
                    if (!MeasureDiffAllowed(d)) continue; // -measureDiffs 4 = 군주 only
                    var diff = (DungeonDifficulty)d;
                    int lv = DungeonDatabase.Difficulty(diff).recommendedLevel;
                    // Career rotates with the dungeon, not the difficulty: each difficulty sees every career across the five
                    // dungeons (before, 왕 was always the arcanist and 영웅 always the bishop).
                    int pick = dungeonIndex + d;
                    var career = lv >= 15 ? rotation[pick % rotation.Length] : Career.None;
                    var cls = career != Career.None ? CareerCatalog.Base(career) : (pick % 2 == 0 ? CharacterClass.Warrior : CharacterClass.Mage);
                    // Same hero alone and with three AI, so the two rows compare (2026-10-13).
                    foreach (int companions in new[] { 0, 3 })
                    {
                        MeasureSeedRow($"dungeon:{def.id}:{d}:{career}:{lv}:{companions}");
                        yield return MeasureHero(cls, career, lv, "r");
                        // Optional QA fixture only: preserve the standard loadout unless explicitly requested.
                        bool ninjaLeapFixture = MeasureArg("-measureNinjaLeapFixture", "0") == "1";
                        if (ninjaLeapFixture && (career != Career.Ninja || !Game.Session.Progression.EquipSkill(0, "leap")))
                            throw new System.InvalidOperationException("Ninja leap fixture requires a promoted ninja and an unlocked leap skill.");
                        for (int below = 0; below < d; below++) Game.Session.Dungeons.RecordClear(def.id, (DungeonDifficulty)below, DungeonRank.A);
                        var row = HRow(("id", def.id), ("name", def.name), ("difficulty", d), ("level", lv), ("career", MeasureCareerName(cls, career)),
                            ("party", companions == 0 ? "solo" : "1+3ai"));
                        yield return MeasureOneRun(def, diff, companions, scale, row);
                        rows.Add(row);
                        measureCheckpoint?.Invoke();
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
            var careers = MeasureCareerList(MeasureCareers); // [MEASURE CH3] all six careers by name
            // [CH3] -measureRaid raid_xibak|raid_kamatz opens chapter 3 for the run (offline flag), the others measure the open raids.
            bool wasOpen = Game.Config != null && Game.Config.chapter3Open;
            if (Game.Config != null && DungeonDatabase.AllRaids.Any(r => r.id == onlyRaid && r.chapter >= 3)) Game.Config.chapter3Open = true;
            foreach (var def in DungeonDatabase.Raids.Where(r => onlyRaid == "" || r.id == onlyRaid)) // the open raids (해골왕, 그라흐, and 지바크·카마츠 with chapter 3); 바르가스·바위 심장 are not open yet
                foreach (var diff in new[] { DungeonDifficulty.Normal, DungeonDatabase.RaidHeroic })
                    if (MeasureDiffAllowed(diff == DungeonDifficulty.Normal ? 0 : 1)) // -measureDiffs 0 = normal only
                    foreach (var career in careers)
                    foreach (int companions in new[] { 0, 3 })
                    {
                        var numbers = DungeonDatabase.DifficultyFor(def, diff);
                        int lv = numbers.recommendedLevel;
                        var cls = CareerCatalog.Base(career);
                        Log($"raid {def.id} {diff} {career} x{companions}: hero Lv{lv}");
                        MeasureSeedRow($"raid:{def.id}:{diff}:{career}:{lv}:{companions}");
                        yield return MeasureHero(cls, career, lv, "e");
                        Log("  hero ready");
                        if (!string.IsNullOrEmpty(def.unlockQuest)) Game.Session.Journal.State(def.unlockQuest).status = (int)QuestStatus.Active;
                        if (diff != DungeonDifficulty.Normal) Game.Session.Dungeons.RecordClear(def.id, DungeonDifficulty.Normal, DungeonRank.A);
                        if (def.keyCost > 0) Game.Session.Inventory.Add(DungeonDatabase.SealKey, def.keyCost); // a final raid without keys asks to confirm a practice run
                        var row = HRow(("id", def.id), ("name", def.name), ("difficulty", diff == DungeonDifficulty.Normal ? 0 : 1), ("level", lv),
                            ("career", MeasureCareerName(cls, career)), ("party", companions == 0 ? "solo" : "1+3ai"));
                        // Each raid on one of its own open days (the final raids open on Sundays only).
                        while (def.openDays != null && def.openDays.Length > 0 && Array.IndexOf(def.openDays, clock.DayOfWeek) < 0) clock = clock.AddDays(1);
                        yield return MeasureOneRun(def, diff, companions, scale, row);
                        rows.Add(row);
                        measureCheckpoint?.Invoke();
                        clock = clock.AddDays(7);
                    }
            if (Game.Config != null) Game.Config.chapter3Open = wasOpen;
            ResetClock.NowOverride = null;
        }
    }
}
