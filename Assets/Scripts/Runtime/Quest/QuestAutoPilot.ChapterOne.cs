using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [AUTO] Chapter 1 (1-1 up to 2-1) plays itself: while a chapter 1 main quest is left, auto-progress starts on its
    /// own whenever the hero is idle in the field. Any key still hands control to the player; once the keys are let go
    /// for a moment it picks the story up again. If auto-progress stops by itself (a step the player must do by hand,
    /// a path it cannot find) it waits until the quest moves on instead of restarting into the same stop.
    /// </summary>
    public sealed partial class QuestAutoPilot
    {
        const float ChapterOneIdleSeconds = 2.5f;

        /// <summary>Time the player last took over by pressing a key (AutoInput).</summary>
        static float playerTookOverAt = -99f;
        static bool chapterOneAnnounced;
        /// <summary>The current run was started by the chapter 1 auto-start (quieter toasts).</summary>
        bool autoStarted;

        // [AUTO] 2026-10-29: auto-progress no longer starts by itself when the hero stands idle (request item 3). It runs only
        // when the player turns it on (button / key), so the idle starter below is not installed any more.
        static void InstallChapterOneStarter()
        {
            var go = new GameObject("ChapterOneAutoStart");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<ChapterOneStarter>();
        }

        /// <summary>Called by AutoInput when a key takes control back.</summary>
        static void NotePlayerTookOver() => playerTookOverAt = Time.unscaledTime;

        /// <summary>A chapter 1 main quest is still left (the chapter ends where 2-1 starts).</summary>
        static bool InChapterOne()
        {
            var q = Game.Quest;
            if (q == null || q.Database == null) return false;
            var main = q.CurrentMain();
            if (main != null) return main.chapter == 1;
            return ChapterLeft(1); // between quests (a level gate inside chapter 1)
        }

        /// <summary>Changes whenever the story moves: the main quest, its step, its objective counts, the hero's level.</summary>
        static string ChapterOneProgressKey()
        {
            var q = Game.Quest;
            var sb = new StringBuilder();
            var main = q.CurrentMain();
            if (main != null)
            {
                var s = Game.Session.Journal.State(main.id);
                sb.Append(main.id).Append('|').Append(s.status).Append('|').Append(s.step);
                foreach (var c in s.counts) sb.Append(',').Append(c);
            }
            sb.Append("|lv").Append(Game.Session.Progression.Level).Append('|').Append(Game.World != null ? Game.World.MapId : "");
            return sb.ToString();
        }

        sealed class ChapterOneStarter : MonoBehaviour
        {
            float idleSince, nextCheck;

            void Update()
            {
                if (UnityEngine.Input.anyKey || UnityEngine.Input.touchCount > 0) idleSince = Time.unscaledTime;
                if (Time.unscaledTime < nextCheck) return;
                nextCheck = Time.unscaledTime + 0.5f;

                if (MeasureAnywhere) return; // [MEASURE] -dotrpgMeasure drives the hero itself (the chapter 1 start took over measured hunts)
                if (Active || Game.MiniDungeon?.Active == true || Game.MiniDungeon?.Busy == true || BossRushDirector.InArena) return;
                var p = Game.Player;
                if (p == null || p.IsDead || Game.Session == null || Game.World == null || !Game.IsPlaying) return;
                if (Game.Flow != null && Game.Flow.IsTransitioning) return;
                if (Game.Dungeon != null && Game.Dungeon.InRun) return;
                if (!InChapterOne()) return;

                float idleFor = Time.unscaledTime - Mathf.Max(idleSince, playerTookOverAt);
                if (idleFor < ChapterOneIdleSeconds) return;

                // The last run ended by itself (or by the button), not by a key: wait until the story moves on.
                bool selfStopped = Instance != null && Instance.lastStopAt > playerTookOverAt;
                if (selfStopped && Instance.stopKey == ChapterOneProgressKey()) return;

                if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
                Instance.huntMode = false;
                Instance.autoStarted = true;
                Instance.quietStart = true; // the chapter 1 toast below says it instead
                if (!chapterOneAnnounced)
                {
                    chapterOneAnnounced = true;
                    GameEvents.RaiseToast(L.T("1장은 자동으로 진행됩니다. 아무 키나 누르면 직접 조작하고, 손을 떼면 다시 이어집니다."));
                }
                Instance.Begin();
                if (!Active) Instance.autoStarted = false;
            }
        }

        /// <summary>Set before Begin: skip the start toast (chapter 1 restarts after the player lets go).</summary>
        bool quietStart;
        /// <summary>When and at which progress the last run stopped (for the chapter 1 restart rule).</summary>
        float lastStopAt = -99f;
        string stopKey;
    }
}
