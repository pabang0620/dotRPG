using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Read-only map previews. Never loads a live world or spawns gameplay objects.</summary>
    public static class WorldAtlas
    {
        public sealed class Marker
        {
            public string id, name, interior;
            public NpcService service;
            public Vector2 position;
        }
        public sealed class Preview
        {
            public Texture2D texture;
            public Rect bounds;
            public bool rendered;
            public readonly List<Vector2> exits = new List<Vector2>();
        }
        static readonly Dictionary<string, Preview> cache = new Dictionary<string, Preview>();
        static readonly Dictionary<string, List<Marker>> entrances = new Dictionary<string, List<Marker>>();
        static readonly Dictionary<string, Vector2> guides = new Dictionary<string, Vector2>();

        public static void Record(WorldBuilder world)
        {
            if (cache.TryGetValue(world.MapId, out var old) && old.texture != null) Object.Destroy(old.texture);
            var p = new Preview { texture = Object.Instantiate(world.Minimap), bounds = world.Bounds, rendered = true };
            p.exits.AddRange(world.PortalCenters); cache[world.MapId] = p;
            entrances[world.MapId] = Doors(world).ToList();
            if (world.DungeonGuidePosition.HasValue) guides[world.MapId] = world.DungeonGuidePosition.Value;
        }

        static IEnumerable<Marker> Doors(WorldBuilder world)
        {
            foreach (var door in world.ObjectsRoot.GetComponentsInChildren<ServiceDoor>())
            {
                var def = WorldBuilder.ServiceNpc(door.Service);
                yield return new Marker { id = def.npcId, name = def.displayName + " · " + MapRegistry.ServiceName(door.Service) + " (실내)",
                    service = door.Service, interior = MapRegistry.InteriorFor(world.MapId, door.Service), position = door.transform.position };
            }
        }

        static string[] Rows(MapInfo info)
        {
            if (info.IsInterior) return new[] { ".....", ".....", ".....", "..P..", "..<.." };
            string source = info.id == MapRegistry.Village && Game.Config.worldMap != null ? Game.Config.worldMap.text : HuntingGrounds.Layout(info.id);
            if (source == null) source = Resources.Load<TextAsset>(info.resource)?.text ?? "P";
            source = HuntingGrounds.AdaptTownLayout(info.id, source);
            return source.Split('\n').Select(r => r.TrimEnd('\r')).Where(r => !r.StartsWith("//") && !string.IsNullOrWhiteSpace(r)).ToArray();
        }

        public static Preview Get(string id)
        {
            if (cache.TryGetValue(id, out var ready)) return ready;
            var info = MapRegistry.Get(id); if (info == null) return null;
            var rows = Rows(info); int w = rows.Max(r => r.Length), h = rows.Length;
            var p = new Preview { bounds = new Rect(0, 0, w, h) };
            // Unvisited regions use a clearly labelled atlas schematic from the exact playable layout.
            const int tile = 6; var c = new PixelCanvas(w * tile, h * tile);
            bool snow = info.theme == MapTheme.Winter;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                char ch = x < rows[y].Length ? rows[y][x] : '.';
                string color = snow ? "c3d4df" : info.theme == MapTheme.Canyon ? "968470" : "59764c";
                if (ch == '~' || ch == 'V') color = "3c7892";
                else if (ch == 'W') color = "626475";
                else if (ch == ',' || ch == '=' || ch == 'd' || ch == 'L' || ch == 'P' || ch == '<' || ch == '>') color = snow ? "969eaa" : "bea57c";
                c.Rect(x * tile, y * tile, tile, tile, PixelCanvas.Hex(color));
                bool tree = ch == 'T' || ch == '%' || ch == 'Y' || ch == 'q' || (snow && (ch == 'Q' || ch == 'y'));
                if (tree) { c.Circle(x * tile + 3, y * tile + 3, 3, PixelCanvas.Hex(snow ? "557c76" : "2b5141")); c.Rect(x * tile + 2, y * tile + 1, 2, 2, PixelCanvas.Hex("7c9c67")); }
                if (ch == '<' || ch == '>')
                {
                    var pos = new Vector2(x + .5f, h - y - .5f);
                    p.exits.Add(pos);
                }
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x]; int bw = 0, bh = 0;
                if (info.id == MapRegistry.Village && "HIMAZL".Contains(ch)) { bw = 10; bh = 8; }
                else if (MapRegistry.IsTown(id) && (ch == 'H' || ch == 'G')) { bw = 3; bh = 3; }
                else if (snow && ch == 'N') { bw = 5; bh = 4; }
                if (bw == 0) continue;
                c.Rect(x * tile, (y - bh + 1) * tile, bw * tile, bh * tile, PixelCanvas.Hex("293740"));
                c.Rect(x * tile + 1, (y - bh + 1) * tile + 1, bw * tile - 2, bh * tile - 3, PixelCanvas.Hex(snow ? "8c9dac" : "846953"));
                c.HLine(x * tile + 2, (x + bw) * tile - 3, (y - bh / 2) * tile, PixelCanvas.Hex("c5b58a"));
            }
            var groups = new List<List<Vector2>>();
            foreach (var exit in p.exits)
            {
                var group = groups.FirstOrDefault(g => g.Any(e => Vector2.Distance(e, exit) < 1.6f));
                if (group == null) { group = new List<Vector2>(); groups.Add(group); }
                group.Add(exit);
            }
            p.exits.Clear();
            foreach (var group in groups) { Vector2 sum = Vector2.zero; foreach (var exit in group) sum += exit; p.exits.Add(sum / group.Count); }
            var tex = new Texture2D(w * tile, h * tile, TextureFormat.RGBA32, false) { name = "Atlas_" + id, filterMode = FilterMode.Point };
            tex.SetPixels32(c.ToTexturePixels()); tex.Apply(); p.texture = tex; cache[id] = p; return p;
        }

        public static List<Marker> Npcs(string id)
        {
            var list = new List<Marker>(); var info = MapRegistry.Get(id);
            if (info == null) return list;
            if (Game.World.MapId == id)
            {
                foreach (var npc in NpcController.All)
                    if (npc != null && npc.isActiveAndEnabled)
                        list.Add(new Marker { id = npc.Definition.npcId, name = npc.Definition.displayName, service = npc.Definition.service, position = npc.transform.position });
                list.AddRange(Doors(Game.World)); return list;
            }
            if (info.IsInterior)
            {
                var def = WorldBuilder.ServiceNpc(info.interiorService);
                list.Add(new Marker { id = def.npcId, name = def.displayName, service = def.service, position = new Vector2(2.5f, 3.35f) }); return list;
            }
            var rows = Rows(info); int h = rows.Length; Vector2 spawn = Vector2.zero;
            var buildings = new List<Vector2>(); Vector2? barn = null;
            var fallback = GameConfig.DefaultNpcs();
            for (int y = 0; y < h; y++) for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x]; var pos = new Vector2(x + .5f, h - y - .5f);
                if (ch == 'P') spawn = pos;
                if (ch == 'H' || (id == MapRegistry.Canyon && ch == 'G')) buildings.Add(new Vector2(x + 1.5f, h - y - .9f));
                if (id == MapRegistry.Winter && ch == 'N') barn = new Vector2(x + 2.5f, h - y - .9f);
                if (id == MapRegistry.Village && (ch == 'M' || ch == 'A' || ch == 'Z'))
                {
                    var service = ch == 'M' ? NpcService.Shop : ch == 'A' ? NpcService.Blacksmith : NpcService.Storage;
                    string key = ch == 'M' ? "town_store" : ch == 'A' ? "town_smithy" : "town_warehouse";
                    AddDoor(service, new Vector2(x + 5, h - y - .9f) + VillageBuildingArt.Find(key).Entrance);
                }
                // Non-NPC map symbols differ by theme; the explicit NPC letters/digits are unambiguous.
                if (!(ch >= '1' && ch <= '9') && ch != 'J' && ch != 'D' && ch != 'K') continue;
                var def = Game.Config.GetNpcBySymbol(ch) ?? fallback.FirstOrDefault(n => n.mapSymbol == ch.ToString());
                if (def == null || (MapRegistry.IsTown(id) && MapRegistry.IsIndoorService(def.service)) || (id == MapRegistry.Village && StoryCast.VillagersHidden)) continue;
                list.Add(new Marker { id = def.npcId, name = def.displayName, service = def.service, position = pos });
            }
            if (MapRegistry.IsTown(id))
            {
                if (entrances.TryGetValue(id, out var doors)) { list.RemoveAll(m => m.interior != null); list.AddRange(doors); }
                else if (id != MapRegistry.Village)
                {
                    buildings = buildings.OrderBy(p => Vector2.Distance(p, spawn)).ToList();
                    if (id == MapRegistry.Winter) { if (barn.HasValue) buildings.Insert(Mathf.Min(1, buildings.Count), barn.Value); buildings.Add(new Vector2(29.5f, 10.1f)); }
                    for (int i = 0; i < 3 && i < buildings.Count; i++) AddDoor(MapRegistry.IndoorServices[i], buildings[i]);
                }
                var guidePos = new Vector2(33.5f, h - 34.5f);
                if (id != MapRegistry.Village) guidePos = spawn + Vector2.up * 3;
                if (guides.TryGetValue(id, out var actual)) guidePos = actual;
                else
                {
                    bool found = false;
                    for (int r = 0; r <= 6 && !found; r++) for (int dy = -r; dy <= r && !found; dy++) for (int dx = -r; dx <= r && !found; dx++)
                    {
                        int cx = (int)guidePos.x + dx, cy = (int)guidePos.y + dy; bool open = true;
                        for (int yy = -1; yy <= 1; yy++) for (int xx = -1; xx <= 1; xx++)
                        { int row = h - 1 - (cy + yy), col = cx + xx; if (row < 0 || row >= h || col < 0 || col >= rows[row].Length || rows[row][col] != ',') open = false; }
                        if (open) { guidePos = new Vector2(cx + .5f, cy + .5f); found = true; }
                    }
                }
                list.Add(new Marker { id = "dungeon_guide", name = WorldBuilder.DungeonGuide.displayName + " · 요일던전", service = NpcService.Dungeon, position = guidePos });
            }
            foreach (var p in StoryCast.Placements)
                if (p.map == id && StoryCast.Visible(p))
                {
                    var def = StoryCast.Find(p.npcId); if (def == null) continue;
                    list.Add(new Marker { id = def.npcId, name = def.displayName, position = CutscenePlayer.CellToWorld(Get(id).bounds, p.col, p.row) });
                }
            return list;
            void AddDoor(NpcService service, Vector2 position)
            {
                var def = WorldBuilder.ServiceNpc(service);
                list.Add(new Marker { id = def.npcId, name = def.displayName + " · " + MapRegistry.ServiceName(service) + " (실내)", service = service,
                    interior = MapRegistry.InteriorFor(id, service), position = position });
            }
        }
    }
}
