using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public static partial class GameDataExport
    {
        static string Maps()
        {
            var j = Doc();
            j.Arr("maps", MapRegistry.All.Concat(MapRegistry.Interiors).Concat(MapRegistry.Rooms), (o, m) =>
            {
                o.Obj().Str("id", m.id).Bool("instanced", m.instanced).Bool("safe", m.safe);
                var census = Census(m);
                // [SERVER 8] A field party can share this map's monsters (open map with a field spawner).
                o.Bool("sharedField", !m.instanced && !m.safe && census.spawnPoints > 0);
                if (census.width > 0) o.Key("bounds").Obj().Num("minX", 0).Num("minY", 0).Num("maxX", census.width).Num("maxY", census.height).End();
                var zone = HuntingGrounds.Get(m.id);
                if (zone != null)
                {
                    var counts = Enumerable.Range(0, census.spawnPoints * HuntingGrounds.PackSize).GroupBy(i => zone.monsters[i % zone.monsters.Length]); // packs of three (WorldBuilder)
                    o.Arr("fieldSpawns", counts, (x, g) => x.Obj().Str("monsterId", g.Key).Num("points", g.Count())
                        .Num("level", zone.monsterLevel).Num("xp", zone.KillXp).Num("respawnSeconds", HuntingGrounds.RespawnSeconds).End());
                }
                else o.Arr("fieldSpawns", census.spawnPoints > 0 ? new[] { census.spawnPoints } : new int[0],
                    (x, n) => x.Obj().Str("monsterId", Resources.Load<GameConfig>("Data/GameConfig").skeletonStats.enemyId).Num("points", n).Num("level", 1).End()); // field skeletons are level 1 (EnemyController.Create)
                // [FIELD BOSS] One boss per quarter hour on this map (FieldBosses): the server credits one kill per window.
                var fb = FieldBosses.For(m.id);
                if (fb != null)
                    o.Key("fieldBoss").Obj().Str("monsterId", fb.monsterId).Num("level", FieldBosses.LevelOf(fb)).Num("xp", FieldBosses.XpOf(fb))
                        .Num("intervalSeconds", FieldBosses.IntervalSeconds).End();
                o.Arr("scriptedSpawns", ScriptedSpawns(m.id), (x, sp) => x.Obj().Str("monsterId", sp.Item1).Num("total", sp.Item2).Str("quest", sp.Item3).End());
                o.Arr("nodes", census.nodes, (x, nd) => x.Obj().Str("id", nd.Item1).Str("kind", nd.Item2).End());
                o.Arr("chests", census.chests, (x, c) => x.Val(c));
                return o.End();
            });
            return j.End().ToString();
        }

        sealed class MapCensus
        {
            public int width, height, spawnPoints;
            public readonly List<(string, string)> nodes = new List<(string, string)>();
            public readonly List<string> chests = new List<string>();
        }

        /// <summary>
        /// Gathering nodes, chests and skeleton spawn points of a map, with the same rules as WorldBuilder.Parse +
        /// SpawnObject: text rows flipped (first row = top), lone '%' on high-res maps drawn as 'T', then the
        /// map-type handlers in WorldBuilder's order (canyon HD, winter HD, high-res town/forest, then the base switch).
        /// Node and chest ids are "{mapId}:{x}:{y}" like TreasureChest. Keep in step with WorldBuilder.
        /// </summary>
        static MapCensus Census(MapInfo map)
        {
            var c = new MapCensus();
            if (map.IsInterior) { c.width = c.height = 5; return c; }
            var source = HuntingGrounds.Layout(map.id);
            if (source == null)
            {
                var text = string.IsNullOrEmpty(map.resource) ? null : Resources.Load<TextAsset>(map.resource);
                if (text == null) return c;
                source = HuntingGrounds.AdaptTownLayout(map.id, text.text);
            }
            var rows = source.Split('\n').Select(r => r.TrimEnd('\r')).Where(l => !l.StartsWith("//") && l.Trim().Length > 0).ToList();
            c.height = rows.Count;
            c.width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            bool canyon = map.theme == MapTheme.Canyon, winter = map.theme == MapTheme.Winter, hd = map.HighRes;
            char filler = canyon ? ',' : '.';
            var cells = new char[c.width, c.height];
            for (int row = 0; row < c.height; row++)
                for (int x = 0; x < c.width; x++)
                    cells[x, c.height - 1 - row] = x < rows[row].Length ? rows[row][x] : filler;
            if (hd)
            {
                var lone = new List<Vector2Int>();
                for (int y = 0; y < c.height; y++)
                    for (int x = 0; x < c.width; x++)
                    {
                        if (cells[x, y] != '%') continue;
                        int n = 0;
                        if (x > 0 && cells[x - 1, y] == '%') n++;
                        if (x < c.width - 1 && cells[x + 1, y] == '%') n++;
                        if (y > 0 && cells[x, y - 1] == '%') n++;
                        if (y < c.height - 1 && cells[x, y + 1] == '%') n++;
                        bool edge = x == 0 || y == 0 || x == c.width - 1 || y == c.height - 1;
                        if (n <= 1 && !edge) lone.Add(new Vector2Int(x, y));
                    }
                foreach (var p in lone) cells[p.x, p.y] = 'T';
            }
            if (map.instanced) return c; // dungeon rooms: monsters come from the room definition, nothing to gather
            for (int y = 0; y < c.height; y++)
                for (int x = 0; x < c.width; x++)
                {
                    char ch = cells[x, y];
                    string id = $"{map.id}:{x}:{y}";
                    string kind = null;
                    bool chest = false, handled = false;
                    if (canyon) // SpawnCanyonHdObject
                    {
                        if (ch == 'R') { kind = "rock"; handled = true; }
                        else if (ch == '$') { chest = true; handled = true; }
                        else if (ch == 'T' || ch == 'O' || ch == 't') handled = true;
                    }
                    if (!handled && winter && ch == 'R') handled = true; // SpawnWinterHdObject: a stone prop, not a node
                    if (!handled && hd) // SpawnTownObject
                    {
                        if (ch == 't') { kind = "tree"; handled = true; }
                        else if (ch == 'R') { kind = "rock"; handled = true; }
                        else if (ch == 'C') { kind = "crop"; handled = true; }
                        else if (ch == '$') { chest = true; handled = true; }
                        else if (ch == 'T' || ch == 'O') handled = true;
                    }
                    if (!handled) // base switch
                    {
                        if (ch == 'T' || ch == 'O') kind = "tree";
                        else if (ch == 'R') kind = "rock";
                        else if (ch == 'C') kind = "crop";
                        else if (ch == '$') chest = true;
                        else if (ch == 'k') c.spawnPoints++;
                    }
                    if (kind != null) c.nodes.Add((id, kind));
                    if (chest) c.chests.Add(id);
                }
            return c;
        }

        /// <summary>Monsters a cutscene spawns on a map (Cutscenes.json "enemies" steps), counted per quest.</summary>
        static List<(string, int, string)> ScriptedSpawns(string mapId)
        {
            var list = new List<(string, int, string)>();
            if (mapId != MapRegistry.Village) return list;
            var cut = Resources.Load<TextAsset>("Data/Cutscenes");
            if (cut == null) return list;
            // {"op": "enemies", "id": "skel_warrior", "count": 3, ...} inside the attack-night scene (quest c1_ashes kills).
            int total = 0;
            string monster = null;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(cut.text,
                         "\"op\"\\s*:\\s*\"enemies\"[^}]*?\"id\"\\s*:\\s*\"([^\"]+)\"[^}]*?\"count\"\\s*:\\s*(\\d+)"))
            {
                monster = m.Groups[1].Value;
                total += int.Parse(m.Groups[2].Value);
            }
            if (monster != null) list.Add((monster, total, "c1_ashes"));
            return list;
        }
    }
}
