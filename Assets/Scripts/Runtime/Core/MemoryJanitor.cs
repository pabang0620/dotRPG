using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [MEMORY] Keeps a long play session from growing without bound. Runs behind the black screen of a map change
    /// (portal, dungeon entry/exit, return to town, mini dungeon, boss rush):
    /// 1. per-map ground caches keep only the current, home-town and most recent maps (the rest are destroyed);
    /// 2. the character frame and hit-flash caches are trimmed once they pass their cap;
    /// 3. Resources.UnloadUnusedAssets + GC.Collect free what nothing references any more.
    /// Never runs while the screen is visible, and skips the collection during a party dungeon run (network timing).
    /// </summary>
    public static class MemoryJanitor
    {
        /// <summary>Maps (current included) whose painted ground stays cached; the home town is kept on top of these.</summary>
        static int RecentMapBudget => Application.isMobilePlatform ? 2 : 6;
        /// <summary>A full unload at most this often unless a cache was trimmed (it costs a few hundred ms behind the fade).</summary>
        const float MinSecondsBetweenCollects = 90f;
        const int CharacterFrameCap = 1500, FlashCap = 1500;

        static readonly List<string> recentMaps = new List<string>();
        static float lastCollect = -1000f;

        public static int Sweeps { get; private set; }

        /// <summary>A map was built (WorldBuilder.Load): it becomes the most recent one.</summary>
        public static void NoteMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return;
            recentMaps.Remove(mapId);
            recentMaps.Insert(0, mapId);
            if (recentMaps.Count > 16) recentMaps.RemoveAt(recentMaps.Count - 1);
        }

        /// <summary>True when a map's cached ground must stay: the current map, its home town or a recent map.</summary>
        public static bool KeepsMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return false;
            string current = Game.World != null ? Game.World.MapId : null;
            if (mapId == current) return true;
            if (current != null && mapId == HuntingGrounds.HomeOf(current)) return true;
            int index = recentMaps.IndexOf(mapId);
            return index >= 0 && index < RecentMapBudget;
        }

        /// <summary>
        /// Trims the caches and, when worthwhile, unloads unused assets. Yield it while the screen is black.
        /// <paramref name="collect"/> false = trim only (a dungeon room change in the middle of a run).
        /// </summary>
        public static IEnumerator Sweep(bool collect = true)
        {
            int trimmed = 0;
            try
            {
                trimmed += WorldBuilder.TrimPaintedGround(KeepsMap);
                trimmed += UnderworldArt.TrimFloors(KeepsMap);
                trimmed += HuntingScenery.TrimFloors(KeepsMap);
                if (Game.Art != null) trimmed += Game.Art.TrimCaches(CharacterFrameCap, FlashCap, LiveLookIds);
                StreamingFiles.ClearCache();
            }
            catch (Exception e) { Debug.LogException(e); }
            Sweeps++;
            // A party run keeps its network timing: the collection waits for the way back to town.
            if (!collect || PartyRunSession.Active) yield break;
            if (trimmed == 0 && Time.realtimeSinceStartup - lastCollect < MinSecondsBetweenCollects) yield break;
            lastCollect = Time.realtimeSinceStartup;
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            MemoryLog.Line("sweep trimmed=" + trimmed);
        }

        /// <summary>Look ids drawn by a character on screen right now (their frames stay cached).</summary>
        static HashSet<string> LiveLookIds()
        {
            var live = new HashSet<string>();
            foreach (var animator in UnityEngine.Object.FindObjectsByType<CharacterAnimator>(FindObjectsInactive.Include))
                if (animator != null && animator.Look != null && animator.Look.id != null) live.Add(animator.Look.id);
            return live;
        }
    }
}
