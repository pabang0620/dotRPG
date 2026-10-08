using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace DotRPG
{
    /// <summary>
    /// Long idle check: <c>-dotrpgSoak [minutes, default 60] [-soakOut &lt;abs folder&gt;]</c>. A fresh local warrior hunts the
    /// forest with scripted input (walk to the nearest monster, basic attack, cycle skills, potions at low HP; no cheats).
    /// Every <see cref="SoakMenuEveryMinutes"/> minutes bag, skills, map, quest, shop, storage, enhance and settings are opened and
    /// closed; every <see cref="SoakDungeonEveryMinutes"/> minutes one weekday dungeon run (normal) is played. Once a minute one line
    /// goes to soak.csv; at the end soak_report.txt gets PASS / FAIL (first vs last ten minutes: a memory figure grew by more
    /// than 20%, or no kill for five minutes). Saves go to &lt;folder&gt;/saves, never the player's own slots.
    /// </summary>
    public partial class DevCapture
    {
        public const string SoakFlag = "-dotrpgSoak";
        const int SoakDefaultMinutes = 60, SoakMenuEveryMinutes = 5, SoakDungeonEveryMinutes = 15, SoakWindowMinutes = 10;
        const float SoakMaxGrowth = 0.20f, SoakStallSeconds = 300f;
        const string SoakField = "forest";
        const string SoakCsvHeader = "minute,kills_total,kills_per_min,gc_mb,unity_alloc_mb,mono_mb,avg_frame_ms,worst_frame_ms,gameobjects,save_bytes,map,deaths";

        sealed class SoakSample { public int Minute; public double Gc, Alloc, Mono; }

        int soakKills, soakDeaths, soakMenuRounds, soakDungeons, soakDungeonCleared;
        float soakLongestStall;
        double soakFrameSum; int soakFrames; float soakFrameWorst;

        /// <summary>Parses the soak switches; false when the game was not started with -dotrpgSoak.</summary>
        static bool SoakRequested(out int minutes, out string outFolder)
        {
            minutes = SoakDefaultMinutes; outFolder = null;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, SoakFlag);
            if (at < 0) return false;
            if (at + 1 < args.Length && int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m >= 1) minutes = m;
            int o = Array.IndexOf(args, "-soakOut");
            outFolder = o >= 0 && o + 1 < args.Length ? args[o + 1] : Path.Combine(Application.persistentDataPath, "soak");
            return true;
        }

        static void SoakAttach(GameObject go, string folder)
        {
            var capture = go.AddComponent<DevCapture>();
            capture.folder = folder;
            capture.mode = SoakFlag;
        }

        void OnSoakKill(string id) => soakKills++;

        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture);

        IEnumerator SoakRun()
        {
            SoakRequested(out int minutes, out _);
            Log($"soak {minutes} min, field {SoakField}, saves {SaveSystem.DirectoryOverride}");
            Game.Config.autosave = true; // the periodic save is part of what is measured, and it writes to the test folder only
            yield return Wait(1.5f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            while (Game.Flow == null || Game.Flow.IsTransitioning || !Game.IsPlaying) yield return null;
            yield return Wait(1f);
            var script = new ScriptedInput();
            Game.Player.Input = script;
            GameEvents.EnemyKilled += OnSoakKill;
            var csv = Path.Combine(folder, "soak.csv");
            File.WriteAllText(csv, SoakCsvHeader + "\n", new UTF8Encoding(false));
            var samples = new List<SoakSample>();
            var reasons = new List<string>();
            float start = Time.realtimeSinceStartup, end = start + minutes * 60f;
            int nextMinute = 1, lastKills = 0, lastKillTotal = 0;
            float lastKillAt = start;
            bool stallNoted = false;
            float nextAttack = 0f, nextSkill = 0f, nextPotion = 0f, nextManaPotion = 0f;
            int slot = 0;
            while (Time.realtimeSinceStartup < end)
            {
                float now = Time.realtimeSinceStartup;
                float ms = Time.unscaledDeltaTime * 1000f;
                soakFrameSum += ms; soakFrames++; if (ms > soakFrameWorst) soakFrameWorst = ms;
                if (soakKills != lastKillTotal) { lastKillTotal = soakKills; lastKillAt = now; stallNoted = false; }
                float stall = now - lastKillAt;
                if (stall > soakLongestStall) soakLongestStall = stall;
                if (stall >= SoakStallSeconds && !stallNoted)
                {
                    stallNoted = true;
                    reasons.Add($"no kill for {stall / 60f:0.0} min (minute {(now - start) / 60f:0.0}, map {Game.World.MapId}, state {Game.State.Current}, hp {Game.Player.Health.Current}/{Game.Player.Health.Max})");
                }
                if ((now - start) / 60f >= nextMinute)
                {
                    SoakWriteLine(csv, nextMinute, samples, ref lastKills);
                    if (nextMinute % SoakMenuEveryMinutes == 0 && nextMinute < minutes) { script.Move = Vector2.zero; yield return SoakMenus(); lastKillAt = Time.realtimeSinceStartup; }
                    if (nextMinute % SoakDungeonEveryMinutes == 0 && nextMinute < minutes) { script.Move = Vector2.zero; yield return SoakDungeon(); lastKillAt = Time.realtimeSinceStartup; }
                    nextMinute++;
                    continue;
                }
                var local = Game.Player;
                if (local == null || Game.Flow.IsTransitioning) { yield return null; continue; }
                if (local.IsDead)
                {
                    script.Move = Vector2.zero;
                    if (Game.State.Current == GameState.GameOver)
                    {
                        soakDeaths++;
                        Game.Flow.RespawnInVillage();
                        yield return Wait(1f);
                        while (Game.Flow.IsTransitioning) yield return null;
                    }
                    yield return null;
                    continue;
                }
                if (!Game.IsPlaying) { script.Move = Vector2.zero; yield return null; continue; }
                if (Game.World.MapId != SoakField) { script.Move = Vector2.zero; Game.Flow.TravelTo(SoakField); yield return Wait(1f); continue; }
                float hpFrac = local.Health.Max > 0 ? (float)local.Health.Current / local.Health.Max : 1f;
                if (hpFrac < 0.45f && now >= nextPotion) { nextPotion = now + 0.5f; local.UseHealing(); }
                if (local.MaxMana > 0 && local.Mana < local.MaxMana * 0.25f && now >= nextManaPotion) { nextManaPotion = now + 0.5f; local.UseConsumable(ConsumableDatabase.MpPotion); }
                var danger = Telegraph.Active.FirstOrDefault(t => t != null && !t.Resolved && !t.Cancelled && t.Contains(local.Position));
                if (danger != null) { script.Move = EscapeDirection(danger, local.Position); yield return null; continue; }
                EnemyController target = null; float best = float.MaxValue;
                foreach (var e in EnemyController.Active)
                {
                    if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                    float d = (e.Position - local.Position).sqrMagnitude;
                    if (d < best) { best = d; target = e; }
                }
                if (target == null) { script.Move = Vector2.zero; yield return null; continue; }
                Vector2 to = target.Position - local.Position;
                float reach = 0.9f + 0.35f * target.Size;
                script.Move = to.magnitude > reach ? GridPath.Steer(local.Position, target.Position) : Vector2.zero;
                script.Aim = (target.Center - local.Center).normalized;
                if (to.magnitude <= reach + 0.6f)
                {
                    if (now >= nextSkill) { nextSkill = now + 1.2f; script.PressSkill(slot); slot = (slot + 1) % 4; }
                    else if (now >= nextAttack) { nextAttack = now + 0.25f; script.PressAttack(); }
                }
                yield return null;
            }
            GameEvents.EnemyKilled -= OnSoakKill;
            script.Move = Vector2.zero;
            SoakWriteReport(minutes, samples, reasons);
        }

        void SoakWriteLine(string csv, int minute, List<SoakSample> samples, ref int lastKills)
        {
            long gc = GC.GetTotalMemory(false), alloc = Profiler.GetTotalAllocatedMemoryLong(), mono = Profiler.GetMonoUsedSizeLong();
            int objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            long save = 0;
            try { var p = SaveSystem.SlotPath(SaveSystem.ActiveSlot); if (File.Exists(p)) save = new FileInfo(p).Length; } catch (Exception) { }
            double avg = soakFrames > 0 ? soakFrameSum / soakFrames : 0;
            var inv = CultureInfo.InvariantCulture;
            File.AppendAllText(csv, string.Join(",", minute, soakKills, soakKills - lastKills, Mb(gc), Mb(alloc), Mb(mono),
                avg.ToString("0.0", inv), soakFrameWorst.ToString("0.0", inv), objects, save, Game.World.MapId, soakDeaths) + "\n");
            samples.Add(new SoakSample { Minute = minute, Gc = gc / 1048576.0, Alloc = alloc / 1048576.0, Mono = mono / 1048576.0 });
            lastKills = soakKills; soakFrameSum = 0; soakFrames = 0; soakFrameWorst = 0f;
        }

        /// <summary>Opens and closes the windows a player uses; a leak in their build / teardown shows up as growth.</summary>
        IEnumerator SoakMenus()
        {
            if (Game.Player == null || Game.Player.IsDead || !Game.IsPlaying) yield break;
            soakMenuRounds++;
            var ui = Game.UI;
            Game.Flow.OpenWindow(null); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            Game.Flow.OpenWindow(ui.Skills); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            Game.Flow.OpenWindow(ui.WorldMap); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            Game.Flow.OpenWindow(ui.QuestLog); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            ui.Shop.SetKeeper("잡화 상인", "무엇을 사시겠어요?");
            Game.Flow.OpenWindow(ui.Shop); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            ui.Storage.SetKeeper("창고지기", "물건을 맡기시겠어요?");
            Game.Flow.OpenWindow(ui.Storage); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            ui.Enhance.SetKeeper("대장장이", "강화를 도와드리죠.");
            Game.Flow.OpenWindow(ui.Enhance); yield return Wait(.5f); Game.Flow.CloseInventory(); yield return Wait(.3f);
            Game.Flow.Pause(); yield return Wait(.3f);
            ui.Push(ui.Settings); yield return Wait(.5f);
            ui.Pop(); yield return Wait(.2f);
            Game.Flow.Resume(); yield return Wait(.3f);
        }

        /// <summary>One weekday dungeon (normal) on a fixed test weekday; entries are reset, nothing else is granted.</summary>
        IEnumerator SoakDungeon()
        {
            if (Game.Player == null || Game.Player.IsDead || !Game.IsPlaying || Game.Dungeon == null) yield break;
            var clock = WeekdayClock(0);
            ResetClock.NowOverride = () => clock;
            Game.Session.Dungeons.DevClearEntries();
            bool entered = Game.Dungeon.Enter(DungeonDatabase.Weekday[0], DungeonDifficulty.Normal);
            soakDungeons++;
            if (entered)
            {
                yield return Wait(1.6f);
                var res = new SimResult();
                yield return ScriptedRun(res, 1f, 300f);
                if (res.cleared) soakDungeonCleared++;
                Log($"soak dungeon: cleared={res.cleared} failed={res.failed} seconds={res.seconds:0}");
                yield return LeaveResult();
            }
            else Log("soak dungeon: entry refused");
            ResetClock.NowOverride = null;
            while (Game.Flow.IsTransitioning) yield return null;
        }

        void SoakWriteReport(int minutes, List<SoakSample> samples, List<string> reasons)
        {
            int win = Mathf.Max(1, Mathf.Min(SoakWindowMinutes, samples.Count / 2));
            if (samples.Count >= 2)
            {
                var first = samples.Take(win).ToList(); var last = samples.Skip(samples.Count - win).ToList();
                void Compare(string name, Func<SoakSample, double> pick)
                {
                    double a = first.Average(pick), b = last.Average(pick);
                    bool bad = a > 0 && b > a * (1 + SoakMaxGrowth);
                    if (bad) reasons.Add($"{name} grew {(b / a - 1) * 100:0.0}% (first {win} min avg {a:0.00} MB, last {win} min avg {b:0.00} MB)");
                }
                Compare("GC memory", s => s.Gc); Compare("Unity allocated memory", s => s.Alloc); Compare("mono used memory", s => s.Mono);
            }
            else reasons.Add("run too short to compare memory (fewer than 2 samples)");
            var sb = new StringBuilder();
            sb.AppendLine(reasons.Count == 0 ? "PASS" : "FAIL");
            foreach (var r in reasons) sb.AppendLine("reason: " + r);
            sb.AppendLine($"minutes: {minutes}, kills: {soakKills}, longest gap without a kill: {soakLongestStall / 60f:0.0} min");
            sb.AppendLine($"deaths: {soakDeaths}, menu rounds: {soakMenuRounds}, dungeon runs: {soakDungeons} (cleared {soakDungeonCleared})");
            File.WriteAllText(Path.Combine(folder, "soak_report.txt"), sb.ToString(), new UTF8Encoding(false));
            Log("soak finished: " + (reasons.Count == 0 ? "PASS" : "FAIL"));
        }
    }
}
