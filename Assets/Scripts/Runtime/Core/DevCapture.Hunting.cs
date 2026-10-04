using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        static Dictionary<string, object> HRow(params (string key, object value)[] fields)
        {
            var row = new Dictionary<string, object>(); foreach (var f in fields) row[f.key] = f.value; return row;
        }
        static int TotalXp()
        {
            var p = Game.Session.Progression; int xp = p.Xp;
            for (int level = 1; level < p.Level; level++) xp += Progression.XpToNext(level);
            return xp;
        }
        IEnumerator HuntingRun()
        {
            dgnPassed = dgnFailed = 0;
            yield return Wait(1f); Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior); yield return Wait(1.5f);
            Game.Player.Health.SetInvulnerable(600f); Game.Player.Input = new ScriptedInput();
            var export = new List<object>(); var dungeonExport = new List<object>();
            DCheck("three towns and nine fields", MapRegistry.All.Count(m => m.safe) == 3 && MapRegistry.All.Count(m => !m.safe) == 9);
            foreach (var village in new[] { MapRegistry.Village, MapRegistry.Canyon, MapRegistry.Winter })
                DCheck(village + " has three hunting grounds", HuntingGrounds.All.Count(z => z.village == village) == 3);
            foreach (var map in MapRegistry.All)
            {
                Game.World.Load(map.id); Game.Player.Place(Game.World.PlayerSpawn, Facing.Right);
                yield return Wait(.2f);
                var zone = HuntingGrounds.Get(map.id);
                var rows = new List<string>();
                var source = HuntingGrounds.Layout(map.id) ?? HuntingGrounds.AdaptTownLayout(map.id, Resources.Load<TextAsset>(map.resource).text);
                foreach (var line in source.Split('\n')) if (line.Length > 0 && !line.StartsWith("//")) rows.Add(line.TrimEnd('\r'));
                File.WriteAllText(Path.Combine(folder, map.id + ".txt"), string.Join("\n", rows));
                var reached = new HashSet<Vector2Int>(); var visited = new HashSet<Vector2Int>(); var queue = new Queue<Vector2Int>();
                var start = Vector2Int.FloorToInt(Game.World.PlayerSpawn); reached.Add(start); visited.Add(start); queue.Enqueue(start);
                var steps = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                while (queue.Count > 0)
                {
                    var at = queue.Dequeue();
                    foreach (var step in steps)
                    {
                        var next = at + step;
                        if (!visited.Add(next) || !Game.World.IsFree((Vector2)next + Vector2.one * .5f)) continue;
                        reached.Add(next); queue.Enqueue(next);
                    }
                }
                DCheck(map.id + " player spawn is free", Game.World.IsFree(Game.World.PlayerSpawn));
                foreach (var target in new[] { map.previousMap, map.nextMap })
                {
                    if (target == null) continue;
                    var entry = Game.World.ArrivalFrom(target, out _);
                    DCheck(map.id + " connected exit / arrival: " + target, reached.Contains(Vector2Int.FloorToInt(entry)) && Game.World.ObjectsRoot.GetComponentsInChildren<MapPortal>().Any(p => p.TargetMap == target));
                }
                var enemies = EnemyController.Active.Where(e => e != null && e.isActiveAndEnabled && !e.IsDead).ToArray();
                if (zone != null)
                {
                    DCheck(map.id + " spawn count", enemies.Length == (map.id == MapRegistry.Forest ? 15 : 24));
                    DCheck(map.id + " levels and XP", enemies.All(e => e.Level == zone.monsterLevel && e.Stats.xpReward == zone.KillXp));
                    DCheck(map.id + " all spawn camps reachable", enemies.All(e => reached.Contains(Vector2Int.FloorToInt(e.Position))));
                    int xp = TotalXp();
                    enemies[0].TakeDamage(new DamageInfo(999999, enemies[0].Position, 0, Team.Player, Game.Player.gameObject));
                    DCheck(map.id + " grants XP without dungeon clear", TotalXp() - xp == zone.KillXp && !Game.Dungeon.InRun);
                    var groups = enemies.GroupBy(e => e.Def.id).Select(g => (object)HRow(("monsterId", g.Key), ("points", g.Count()), ("level", zone.monsterLevel), ("xp", zone.KillXp), ("respawnSeconds", HuntingGrounds.RespawnSeconds))).ToList();
                    export.Add(HRow(("id", map.id), ("village", zone.village), ("minLevel", zone.minLevel), ("maxLevel", zone.maxLevel), ("fieldSpawns", groups), ("theme", map.theme.ToString())));
                    Log($"BALANCE {map.id}: Lv.{zone.minLevel}-{zone.maxLevel}, monster={zone.monsterLevel}, xp={zone.KillXp}, xp/min={zone.KillXp * HuntingGrounds.KillsPerMinute}");
                }
                else DCheck(map.id + " remains safe", enemies.Length == 0);
                bool graphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
                if (graphics && (map.id == MapRegistry.Winter || map.id == "forest_ruins" || map.id == "canyon_pass" || map.id == "winter_edge"))
                    RenderRegion(Path.Combine(folder, map.id + "_overview.png"), Game.World.Bounds, 32);
                if (map.id == MapRegistry.Winter)
                {
                    var art = Game.World.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>().Where(sr => sr.sprite != null && sr.sprite.name.StartsWith(WinterVillageArt.Prefix)).ToArray();
                    foreach (var key in new[] { "house", "barn", "gate", "broadleaf", "fir", "pine", "rock" })
                        DCheck("snow village generated " + key, art.Any(sr => sr.sprite.name == WinterVillageArt.Prefix + key));
                    DCheck("winter fences match carrot plot", Game.World.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>().Any(sr => sr.sprite != null && sr.sprite.name.StartsWith("town_fence_")));
                    DCheck("snow atlas point sampling", art.All(sr => sr.sprite.texture.filterMode == FilterMode.Point));
                }
            }
            foreach (var dungeon in DungeonDatabase.Weekday.Concat(DungeonDatabase.Raids))
            {
                var floors = new List<object>(); var xp = new List<object>();
                for (int tier = 0; tier < 4; tier++)
                {
                    var diff = DungeonDatabase.DifficultyFor(dungeon, (DungeonDifficulty)tier);
                    int floor = HuntingGrounds.DungeonClearFloor(dungeon, diff);
                    floors.Add(floor); xp.Add(DungeonRewards.ClearXp(dungeon, diff, DungeonRank.F));
                    int kills = 0;
                    foreach (var room in dungeon.rooms) foreach (var group in room.groups)
                    {
                        var def = MonsterDatabase.Get(group.monsterId);
                        if (def == null || def.noLoot) continue;
                        kills += group.count * Mathf.RoundToInt(def.xp * (1f + MonsterDatabase.XpPerLevel * (DungeonMonsters.LevelFor(diff, group) - 1)));
                    }
                    int benchmark = dungeon.isRaid ? diff.recommendedLevel : new[] { 10, 18, 26, 36 }[tier];
                    float ratio = (kills + (int)xp[tier]) / (dungeon.referenceSeconds[dungeon.isRaid ? 0 : tier] / 60f) / (HuntingGrounds.XpAt(benchmark) * HuntingGrounds.KillsPerMinute);
                    DCheck($"{dungeon.id} difficulty {tier}: dungeon/field XP ratio {ratio:F2}", ratio >= 1.799f);
                }
                dungeonExport.Add(HRow(("id", dungeon.id), ("clearXpFloor", floors), ("clearXpF", xp)));
            }
            File.WriteAllText(Path.Combine(folder, "balance.json"), MiniJson.Write(HRow(("fields", export), ("dungeons", dungeonExport))));
            // A full respawn cycle, with the hero away from the defeated spawn.
            Game.World.Load("canyon_pass"); Game.Player.Place(Game.World.PlayerSpawn, Facing.Right); yield return Wait(.1f);
            var victim = EnemyController.Active.First(e => e != null && !e.IsDead && e.isActiveAndEnabled);
            var point = victim.Position; victim.TakeDamage(new DamageInfo(999999, point, 0, Team.Player, Game.Player.gameObject));
            yield return Wait(HuntingGrounds.RespawnSeconds + .8f);
            DCheck("field respawn supports continuous hunting", EnemyController.Active.Count(e => e != null && !e.IsDead && e.isActiveAndEnabled) == 24);
            Log($"HUNTING summary: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
