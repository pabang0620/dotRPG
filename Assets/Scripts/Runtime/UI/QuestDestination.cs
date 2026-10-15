using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [UI] Where the quest currently being followed wants the player to go, for the minimap and the world map.
    /// It asks auto-progress for its next goal (the same choice it would act on: checked quests, then the main quest,
    /// then the pinned side quest), so the marks show the way whether auto-progress is running or not.
    /// Cached for half a second; null when there is nowhere to go (a scene playing, a step done by hand).
    /// </summary>
    public static class QuestDestination
    {
        public sealed class Info
        {
            public string kind;    // "Npc", "Prop", "Hunt", "Map", "Site", "DungeonGuide"
            public string id;      // npc id, interact id or monster id ("*" any)
            public string map;     // map the goal is on
            public string label;   // what auto-progress would say ("한나에게 가는 중")
        }

        static Info cached;
        static float cachedAt = -99f;
        static QuestAutoPilot probe;

        /// <summary>The current destination (null: none).</summary>
        public static Info Current
        {
            get
            {
                if (Time.unscaledTime - cachedAt < 0.5f) return cached;
                cachedAt = Time.unscaledTime;
                cached = Resolve();
                return cached;
            }
        }

        static Info Resolve()
        {
            if (Game.Quest == null || Game.Session == null || Game.World == null) return null;
            try
            {
                var pilot = QuestAutoPilot.Instance;
                if (pilot == null)
                {
                    // An idle auto-progress object only to ask it; it never runs (not started), so it does nothing on its own.
                    if (probe == null)
                    {
                        var go = new GameObject("QuestDestinationProbe") { hideFlags = HideFlags.HideAndDontSave };
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        probe = go.AddComponent<QuestAutoPilot>();
                    }
                    pilot = probe;
                }
                if (!pilot.PeekGoal(out string kind, out string id, out string map, out string label)) return null;
                return new Info
                {
                    kind = kind,
                    id = id,
                    map = string.IsNullOrEmpty(map) ? Game.World.MapId : map,
                    label = label,
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QuestDestination] " + e.Message);
                return null;
            }
        }

        /// <summary>The map on the world chart to mark (interiors show as their town).</summary>
        public static string RegionOf(Info d)
        {
            if (d == null) return null;
            var info = MapRegistry.Get(d.map);
            return info?.exteriorMap ?? d.map;
        }

        /// <summary>
        /// A point on the current map to head for: the goal itself when it is here, else the exit toward its map.
        /// <paramref name="exit"/> is true for the second case. False when there is no single spot (any monster here).
        /// </summary>
        public static bool PointHere(Info d, out Vector2 point, out bool exit)
        {
            point = default;
            exit = false;
            var world = Game.World;
            if (d == null || world == null) return false;
            string here = world.MapId;
            if (d.map == here) return GoalPoint(d, out point);
            string next = NextMapTowards(here, d.map);
            if (next == null) return false;
            // An exit is a row of portal tiles: aim at their middle.
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (var portal in MapPortal.Active)
                if (portal != null && portal.TargetMap == next) { sum += (Vector2)portal.transform.position; count++; }
            if (count == 0) return false;
            point = sum / count;
            exit = true;
            return true;
        }

        static bool GoalPoint(Info d, out Vector2 point)
        {
            point = default;
            switch (d.kind)
            {
                case "Npc":
                    foreach (var n in NpcController.All)
                        if (n != null && n.isActiveAndEnabled && n.Definition != null && n.Definition.npcId == d.id) { point = n.transform.position; return true; }
                    foreach (var it in Interactable.All)
                        if (it is ServiceDoor door && WorldBuilder.ServiceNpc(door.Service)?.npcId == d.id) { point = door.transform.position; return true; }
                    return false;
                case "DungeonGuide":
                    foreach (var n in NpcController.Services)
                        if (n != null && n.isActiveAndEnabled && n.Definition.service == NpcService.Dungeon) { point = n.transform.position; return true; }
                    return false;
                case "Prop":
                    foreach (var it in Interactable.All)
                        if (it is StoryProp prop && prop.InteractId == d.id) { point = prop.transform.position; return true; }
                    return false;
                case "Site":
                    foreach (var it in Interactable.All)
                        if (it is ConstructionSite site) { point = site.transform.position; return true; }
                    return false;
                case "Hunt":
                    if (string.IsNullOrEmpty(d.id) || d.id == "*" || Game.Player == null) return false; // the whole ground is the goal
                    float best = float.MaxValue;
                    foreach (var e in EnemyController.Active)
                    {
                        if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                        bool match = (e.Stats != null && e.Stats.enemyId == d.id) || (e.Def != null && e.Def.id == d.id);
                        if (!match) continue;
                        float dist = (e.Position - Game.Player.Position).sqrMagnitude;
                        if (dist < best) { best = dist; point = e.Position; }
                    }
                    return best < float.MaxValue;
                default:
                    return false;
            }
        }

        /// <summary>The neighbouring map on the way from <paramref name="from"/> to <paramref name="to"/> (breadth-first over map links).</summary>
        public static string NextMapTowards(string from, string to)
        {
            var cur = MapRegistry.Get(from);
            if (cur == null || string.IsNullOrEmpty(to) || from == to) return null;
            if (cur.IsInterior) return cur.exteriorMap;
            var target = MapRegistry.Get(to);
            if (target != null && target.IsInterior) to = target.exteriorMap; // walk to the town, then the door
            if (from == to) return null;
            var queue = new Queue<(string map, string first)>();
            var seen = new HashSet<string> { from };
            foreach (var n in WorldRoutes.Neighbors(from)) queue.Enqueue((n, n));
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!seen.Add(node.map)) continue;
                if (node.map == to) return node.first;
                foreach (var n in WorldRoutes.Neighbors(node.map)) queue.Enqueue((n, node.first));
            }
            return null;
        }

        /// <summary>A pulsing yellow "!" label used by both maps.</summary>
        public static UnityEngine.UI.Text CreateMark(Transform parent, string name, int size)
        {
            var t = UIFactory.Text(parent, name, "!", size, new Color32(255, 214, 64, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size + 6, size + 8));
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            t.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = new Color(0.25f, 0.08f, 0.02f, 1f);
            return t;
        }

        /// <summary>Gentle pulse for a mark (scale only).</summary>
        public static void Pulse(Transform mark) => mark.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.15f);
    }
}
