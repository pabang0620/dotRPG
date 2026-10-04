using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Kill objectives whose monsters only come from a story cutscene (1-3: the five skeleton warriors of the
    /// attack night) would stall after a reload or leaving the map, because the cutscene does not play again.
    /// On the cutscene's map, with none of those monsters left standing, the missing number is spawned again
    /// where the cutscene put them. The server caps scripted kills at the scripted total, so this never
    /// grants more than the story intended.
    /// </summary>
    public static class StoryRespawn
    {
        static float nextCheck;

        public static void Tick(QuestManager quests)
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 2f;
            if (!Game.IsPlaying || Game.World == null || (Game.Dungeon != null && Game.Dungeon.InRun) || Game.Cutscenes == null || Game.Cutscenes.IsPlaying) return;
            foreach (var q in quests.Database.All)
            {
                if (quests.StatusOf(q.id) != QuestStatus.Active) continue;
                var step = quests.CurrentStep(q);
                if (step == null) continue;
                var counts = quests.Journal.State(q.id).counts;
                for (int i = 0; i < step.objectives.Count; i++)
                {
                    var o = step.objectives[i];
                    if (o.type != ObjectiveTypes.Kill) continue;
                    int missing = o.count - (i < counts.Count ? counts[i] : 0);
                    if (missing <= 0 || Alive(o.target)) continue;
                    var spawn = FindScriptedSpawn(q, o.target, out string map);
                    if (spawn == null || map != Game.World.MapId) continue;
                    Vector2 at = CutscenePlayer.CellToWorld(spawn.col, spawn.row);
                    for (int k = 0; k < missing; k++)
                        MonsterDatabase.Spawn(o.target, at + Random.insideUnitCircle * 1.2f, Game.World.ObjectsRoot);
                }
            }
        }

        static bool Alive(string id)
        {
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.Stats != null && e.Stats.enemyId == id) return true;
            return false;
        }

        /// <summary>The map where this quest's cutscene put <paramref name="monsterId"/> (and respawns it), or null.</summary>
        public static string ScriptedMap(QuestDef q, string monsterId) =>
            Game.Cutscenes != null && FindScriptedSpawn(q, monsterId, out string map) != null ? map : null;

        /// <summary>The "enemies" command for <paramref name="monsterId"/> in a cutscene of this quest, and the map it plays on.</summary>
        static CutsceneCmd FindScriptedSpawn(QuestDef q, string monsterId, out string map)
        {
            map = "";
            foreach (var step in q.steps)
            {
                foreach (var o in step.objectives)
                {
                    if (o.type != ObjectiveTypes.Cutscene) continue;
                    var def = Game.Cutscenes.Get(o.target);
                    if (def == null) continue;
                    foreach (var c in def.cmds)
                        if (c.op == "enemies" && c.id == monsterId && c.col >= 0f && c.row >= 0f) { map = o.map; return c; }
                }
            }
            return null;
        }
    }
}
