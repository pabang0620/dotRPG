using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace DotRPG
{
    /// <summary>
    /// [P5] <c>-dotrpgPerf &lt;folder&gt;</c>: frame time and garbage while playing - the village with the full
    /// party walking, then a weekday dungeon fought by the scripted player at normal speed.
    /// Report lines "PERF &lt;what&gt; PASS|FAIL" and "PERF summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        int perfPassed, perfFailed;
        /// <summary>Average fps must stay at the 60 fps target (vsync); the slowest 1% of frames above 45 fps.</summary>
        const float PerfMinAvgFps = 57f, PerfMinLowFps = 45f;
        /// <summary>Managed allocation budget while playing, KB per second. Incremental GC is on, so the
        /// collection count says little; the sum of per-frame heap growth is the real allocation rate.</summary>
        const float PerfMaxAllocKBps = 200f;
        /// <summary>Fights spawn skill effects, bolts and warning areas per cast; with incremental GC this rate
        /// keeps the slowest 1% of frames at 60 fps (measured), so combat gets its own, larger budget.</summary>
        const float PerfMaxCombatAllocKBps = 1500f;
        float perfBudget = PerfMaxAllocKBps;
        long perfAllocated, perfLastHeap;

        float perfCounted;

        /// <summary>Adds this frame's heap growth. Room changes (map rebuild = loading) are skipped: they are
        /// one-off loading work behind the fade, not per-frame garbage.</summary>
        void SampleHeap()
        {
            long heap = Profiler.GetMonoUsedSizeLong();
            bool loading = Game.Dungeon != null && Game.Dungeon.IsBusy;
            if (!loading && heap > perfLastHeap) perfAllocated += heap - perfLastHeap;
            if (!loading) perfCounted += Time.unscaledDeltaTime;
            perfLastHeap = heap;
        }

        void BeginHeap() { perfAllocated = 0; perfCounted = 0f; perfLastHeap = Profiler.GetMonoUsedSizeLong(); }

        void PerfCheck(string what, bool ok)
        {
            if (ok) perfPassed++;
            else perfFailed++;
            log?.WriteLine($"PERF {what} {(ok ? "PASS" : "FAIL")}");
        }

        IEnumerator PerfRun()
        {
            Game.Config.autosave = false;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            yield return Wait(1.5f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.6f);
            SetupHero(20, true);
            yield return Wait(1f);

            // Village: walk a square for 15 s with the hired party following, after a warm-up lap
            // (the first frames of every sprite and sound are created once on first use).
            var dirs = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            for (float w = 0f; w < 6f; w += Time.unscaledDeltaTime) { Game.Input.MoveOverride = dirs[(int)(w / 1.5f) % 4]; yield return null; }
            yield return Measure("village walk 15s", 15f, t => Game.Input.MoveOverride = dirs[(int)(t / 1.5f) % 4]);
            Game.Input.MoveOverride = null;
            yield return PerfBreakdown();

            // Dungeon: scripted fight at 1x until cleared or 40 s.
            var clock = WeekdayClock(0);
            ResetClock.NowOverride = () => clock;
            Game.Session.Dungeons.DevClearEntries();
            bool entered = Game.Dungeon.Enter(DungeonDatabase.Weekday[0], DungeonDifficulty.Normal);
            yield return Wait(1.6f);
            var frames = new List<float>(4096);
            long mem0 = 0;
            int gc0 = GC.CollectionCount(0);
            BeginHeap();
            float start = Time.realtimeSinceStartup;
            bool done = false;
            StartCoroutine(Collect(frames, () => done));
            if (entered) yield return ScriptedRun(new SimResult(), 1f, 40f);
            done = true;
            perfBudget = PerfMaxCombatAllocKBps;
            PerfReport("dungeon fight (scripted, 1x)", frames, Time.realtimeSinceStartup - start, mem0, gc0);
            if (entered) yield return LeaveResult();
            ResetClock.NowOverride = null;
            log?.WriteLine($"PERF summary: {perfPassed} passed, {perfFailed} failed");
        }

        /// <summary>Walking a square in the village while switching groups off one by one, to see who allocates.</summary>
        IEnumerator PerfBreakdown()
        {
            var dirs = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            IEnumerator Sample(string what, bool walk)
            {
                yield return Wait(0.5f);
                BeginHeap();
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 4f)
                {
                    Game.Input.MoveOverride = walk ? dirs[(int)((Time.realtimeSinceStartup - t0) / 1f) % 4] : (Vector2?)null;
                    SampleHeap();
                    yield return null;
                }
                Game.Input.MoveOverride = null;
                log?.WriteLine($"PERF breakdown {what}: alloc={perfAllocated / 1024f / (Time.realtimeSinceStartup - t0):0}KB/s");
            }
            yield return Sample("idle", false);
            yield return Sample("walk, everything on", true);
            var hud = Game.UI.Hud.gameObject;
            hud.SetActive(false);
            yield return Sample("walk, HUD off", true);
            var comps = Game.Party.Members.Where(m => m != null && m != Game.Player).ToList();
            foreach (var m in comps) m.gameObject.SetActive(false);
            yield return Sample($"walk, HUD + {comps.Count} companions off", true);
            var npcs = FindObjectsByType<NpcController>(FindObjectsSortMode.None);
            foreach (var n in npcs) n.gameObject.SetActive(false);
            yield return Sample($"walk, + {npcs.Length} NPCs off", true);
            foreach (var n in npcs) if (n != null) n.gameObject.SetActive(true);
            foreach (var m in comps) if (m != null) m.gameObject.SetActive(true);
            hud.SetActive(true);
            yield return Wait(0.5f);
        }

        IEnumerator Collect(List<float> frames, Func<bool> stop)
        {
            yield return null;
            while (!stop()) { frames.Add(Time.unscaledDeltaTime); SampleHeap(); yield return null; }
        }

        IEnumerator Measure(string what, float seconds, Action<float> drive)
        {
            var frames = new List<float>(4096);
            long mem0 = 0;
            int gc0 = GC.CollectionCount(0);
            BeginHeap();
            float start = Time.realtimeSinceStartup;
            yield return null;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                drive?.Invoke(Time.realtimeSinceStartup - start);
                frames.Add(Time.unscaledDeltaTime);
                SampleHeap();
                yield return null;
            }
            PerfReport(what, frames, Time.realtimeSinceStartup - start, mem0, gc0);
        }

        void PerfReport(string what, List<float> frames, float seconds, long mem0, int gc0)
        {
            if (frames.Count < 10) { PerfCheck($"{what}: too few frames ({frames.Count})", false); return; }
            var sorted = frames.OrderByDescending(f => f).ToList();
            float avgFps = frames.Count / frames.Sum();
            float low1 = 1f / sorted[Mathf.Max(0, sorted.Count / 100 - 1)];
            float worstMs = sorted[0] * 1000f;
            int gcs = GC.CollectionCount(0) - gc0;
            float allocKBps = (perfAllocated + mem0) / 1024f / Mathf.Max(0.1f, perfCounted > 0.5f ? perfCounted : seconds);
            PerfCheck($"{what}: frames={frames.Count} avg={avgFps:0.0}fps 1%low={low1:0.0}fps worst={worstMs:0}ms alloc={allocKBps:0}KB/s (budget {perfBudget:0}) gcCycles={gcs}",
                avgFps >= PerfMinAvgFps && low1 >= PerfMinLowFps && allocKBps <= perfBudget);
            perfBudget = PerfMaxAllocKBps;
        }
    }
}
