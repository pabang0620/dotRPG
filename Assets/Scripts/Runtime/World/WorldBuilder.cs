using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    /// <summary>
    /// Builds the playable map from a plain-text layout (Resources/Maps/*.txt, see <see cref="MapRegistry"/>):
    /// ground, water and cliffs go into Unity Tilemaps, everything else is spawned as objects.
    /// Text maps are quick to iterate on without an editor; later maps can be painted with the
    /// Tile Palette instead and only the object spawning here needs to be kept.
    ///
    /// Legend (see also the header of each map file):
    ///   .  grass            =  dirt path         #  farm soil        ~  water        d  dock
    ///   ,  flagstone        W  cliff (solid)     L  stairs
    ///   T  tree             O  fruit tree        S  stump            R  rock (mineable)
    ///   B  berry bush       F  fence             f  flower           r  pebble       t  grass tuft
    ///   C  carrot on soil   c  carrot crate      x  wooden box       b  barrel       n  sign
    ///   H  house (anchor, blue roof in the village / red roof in the canyon)
    ///   G  green-roof house (anchor)             X  construction site (anchor)
    ///   P  player start     k  skeleton spawn    &gt;  portal to next map   &lt;  portal to previous map
    ///   1-9 NPCs (defined in GameConfig.npcs)
    /// The winter theme (Winter.txt) adds its own symbols: snowy trees Q q Y y, waterfall V, ice i, cabin H,
    /// farm building N, hay h, campfire j, log seat l, workbench w, lamp p, mailbox m, pot u, woodpile z,
    /// garden bed v (see that file's header).
    /// </summary>
    public partial class WorldBuilder : MonoBehaviour
    {
        /// <summary>Returned by a theme's RawGround hook to fall back to the shared ground rules.</summary>
        const char NoGroundOverride = '\u0001';

        /// <summary>Paints a whole map's ground at 32 pixels per tile (rows bottom-up); ground[x, y] = GroundAt(x, y).</summary>
        delegate Color32[] GroundPainter(char[,] ground, int w, int h, out int pw, out int ph);

        GameConfig config;
        MapInfo map;
        char[,] cells;
        int width, height;

        Transform objectsRoot;
        Tilemap groundMap, waterMap, decoMap, cliffMap, edgeMap;
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        readonly Dictionary<string, List<Vector2>> portalCells = new Dictionary<string, List<Vector2>>();

        public Vector2 PlayerSpawn { get; private set; }
        public Rect Bounds { get; private set; }
        public Transform ObjectsRoot => objectsRoot;
        public MapInfo Map => map;
        public string MapId => map != null ? map.id : MapRegistry.Village;
        bool Canyon => map != null && map.theme == MapTheme.Canyon;
        bool Winter => map != null && map.theme == MapTheme.Winter;
        /// <summary>Village town / forest hunting ground: painted 32px ground and the town_* art set.</summary>
        bool Hd => map != null && map.HighRes;
        bool ForestMap => map != null && map.theme == MapTheme.Forest;
        /// <summary>Canyon town drawn with the 32px art (see WorldBuilder.Canyon.cs).</summary>
        bool CanyonHd => Canyon && CanyonHdReady;
        /// <summary>Winter village drawn with the 32px art (see WorldBuilder.Winter.cs).</summary>
        bool WinterHd => Winter && WinterHdReady;
        /// <summary>Interesting spots for the title-screen camera to drift between.</summary>
        public readonly List<Vector2> PointsOfInterest = new List<Vector2>();

        public static WorldBuilder Create(GameConfig config)
        {
            var go = new GameObject("World");
            var builder = go.AddComponent<WorldBuilder>();
            builder.config = config;
            builder.map = MapRegistry.Default;
            builder.Parse(builder.LoadText(builder.map));
            builder.Build();
            return builder;
        }

        /// <summary>Switches to another map (or rebuilds the current one) and recreates every object.</summary>
        public void Load(string mapId)
        {
            var next = MapRegistry.Get(mapId) ?? MapRegistry.Default;
            if (next != map)
            {
                map = next;
                Parse(LoadText(map));
            }
            Rebuild();
        }

        string LoadText(MapInfo info)
        {
            if (info.id == MapRegistry.Sanctum) return SunkenSanctumArt.Layout();
            if (info.IsInterior) return ".....\n.....\n.....\n..P..\n..<..";
            if (info.id == MapRegistry.Village && config.worldMap != null) return config.worldMap.text;
            var generated = HuntingGrounds.Layout(info.id);
            if (generated != null) return generated;
            var asset = Resources.Load<TextAsset>(info.resource);
            if (asset != null) return HuntingGrounds.AdaptTownLayout(info.id, asset.text);
            Debug.LogWarning($"[dotRPG] Map '{info.resource}' not found; using the fallback layout.");
            return FallbackMap;
        }

        /// <summary>Destroys and recreates every world object (used for New Game / Load / Title).</summary>
        public void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            PointsOfInterest.Clear();
            Build();
        }

        /// <summary>
        /// True when a character could stand at <paramref name="feet"/>: inside the map and not inside a
        /// wall, water, building or prop. Characters themselves do not count as blocking.
        /// </summary>
        public bool IsFree(Vector2 feet)
        {
            if (!Bounds.Contains(feet)) return false;
            // Saved positions are checked during the load frame, before TilemapCollider2D.LateUpdate.
            // Apply pending terrain changes so rewritten hunting maps cannot restore a player inside water.
            var waterCollider = waterMap != null ? waterMap.GetComponent<TilemapCollider2D>() : null;
            var cliffCollider = cliffMap != null ? cliffMap.GetComponent<TilemapCollider2D>() : null;
            if (waterCollider != null && waterCollider.hasTilemapChanges) waterCollider.ProcessTilemapChanges();
            if (cliffCollider != null && cliffCollider.hasTilemapChanges) cliffCollider.ProcessTilemapChanges();
            Physics2D.SyncTransforms();
            foreach (var hit in Physics2D.OverlapCircleAll(feet + new Vector2(0f, 0.22f), 0.26f))
            {
                if (hit == null || hit.isTrigger) continue;
                if (Game.Player != null && hit.transform.IsChildOf(Game.Player.transform)) continue;
                if (hit.GetComponentInParent<EnemyController>() != null || hit.GetComponentInParent<NpcController>() != null) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Where a player coming from <paramref name="fromMap"/> should appear: a step inside the portal
        /// that leads back there. Falls back to the map's start point.
        /// </summary>
        /// <summary>Centre of the portal that leads to <paramref name="targetMap"/> (quest auto-walk).</summary>
        public bool PortalTowards(string targetMap, out Vector2 center)
        {
            center = Vector2.zero;
            if (string.IsNullOrEmpty(targetMap) || !portalCells.TryGetValue(targetMap, out var list) || list.Count == 0) return false;
            foreach (var p in list) center += p;
            center /= list.Count;
            return true;
        }

        public Vector2 ArrivalFrom(string fromMap, out Facing facing)
        {
            facing = Facing.Down;
            if (MapId == MapRegistry.Sanctum) { facing = Facing.Up; return PlayerSpawn; }
            if (map.IsInterior) { facing = Facing.Up; return PlayerSpawn; }
            var from = MapRegistry.Get(fromMap);
            if (from != null && from.IsInterior && from.exteriorMap == MapId)
                foreach (var door in objectsRoot.GetComponentsInChildren<ServiceDoor>())
                    if (door.Service == from.interiorService)
                    {
                        var point = (Vector2)door.transform.position + Vector2.down * .8f;
                        for (int step = 0; step < 6; step++)
                            if (IsFree(point + Vector2.down * step * .3f)) return point + Vector2.down * step * .3f;
                    }
            if (string.IsNullOrEmpty(fromMap) || !portalCells.TryGetValue(fromMap, out var list) || list.Count == 0)
                return PlayerSpawn;
            Vector2 sum = Vector2.zero;
            foreach (var p in list) sum += p;
            Vector2 portal = sum / list.Count;
            // Step away from the nearest map edge (portals sit on the border).
            float left = portal.x, right = width - portal.x, bottom = portal.y, top = height - portal.y;
            float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            Vector2 inward = min == left ? Vector2.right : min == right ? Vector2.left : min == bottom ? Vector2.up : Vector2.down;
            facing = FacingExtensions.FromVector(inward, Facing.Down);
            return portal + inward * 2.2f;
        }

        void Parse(string text)
        {
            var rows = new List<string>();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("//") || line.Trim().Length == 0) continue;
                rows.Add(line);
            }
            height = rows.Count;
            width = 0;
            foreach (var r in rows) width = Mathf.Max(width, r.Length);
            cells = new char[width, height];
            char filler = Canyon ? ',' : '.';
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row; // first text row is the top of the map
                for (int x = 0; x < width; x++)
                    cells[x, y] = x < rows[row].Length ? rows[row][x] : filler;
            }
            if (map != null && map.HighRes)
            {
                // A lone dense-forest cell reads as a dark blob; draw it as an ordinary tree instead.
                var lone = new List<Vector2Int>();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        if (cells[x, y] != '%') continue;
                        int n = 0;
                        if (x > 0 && cells[x - 1, y] == '%') n++;
                        if (x < width - 1 && cells[x + 1, y] == '%') n++;
                        if (y > 0 && cells[x, y - 1] == '%') n++;
                        if (y < height - 1 && cells[x, y + 1] == '%') n++;
                        bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                        if (n <= 1 && !edge) lone.Add(new Vector2Int(x, y));
                    }
                foreach (var c in lone) cells[c.x, c.y] = 'T';
            }
            Bounds = new Rect(0, 0, width, height);
        }

        char At(int x, int y) => x >= 0 && y >= 0 && x < width && y < height ? cells[x, y] : '\0';

        static bool IsWater(char c) => c == '~' || c == 'V';
        static bool IsLand(char c) => c != '\0' && c != '~' && c != 'V' && c != 'd';
        static bool IsDirt(char c) => c == '=';
        static bool IsSoil(char c) => c == '#' || c == 'C';

        void Build()
        {
            var grid = new GameObject("Grid").AddComponent<Grid>();
            grid.transform.SetParent(transform, false);
            groundMap = CreateTilemap(grid.transform, "Ground", -30000, false);
            decoMap = CreateTilemap(grid.transform, "GroundDetail", -29000, false);
            edgeMap = CreateTilemap(grid.transform, "GrassEdge", -29100, false);
            waterMap = CreateTilemap(grid.transform, "Water", -29500, true);
            cliffMap = CreateTilemap(grid.transform, "Cliffs", -28500, true);

            objectsRoot = new GameObject("Objects").transform;
            objectsRoot.SetParent(transform, false);

            var fxRoot = new GameObject("Fx").transform;
            fxRoot.SetParent(transform, false);
            Fx.Root = fxRoot;

            var skeletonSpawns = new List<Vector2>();
            portalCells.Clear();
            ClearDungeonMarks(); // [DUNGEON]
            PlayerSpawn = new Vector2(width * 0.5f, height * 0.5f);
            var rng = new System.Random(1234);
            if (map.IsInterior) { BuildInterior(); return; }
            if (MapId == MapRegistry.Sanctum) { BuildSunkenSanctum(); return; }
            if (map.theme == MapTheme.SanctumField) { BuildSanctumField(); return; }

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    char c = cells[x, y];
                    var cell = new Vector3Int(x, y, 0);
                    if (Hd) PaintHdCollision(cell);
                    else if (CanyonHd) PaintCanyonHdCell(cell, c);
                    else if (WinterHd) PaintWinterHdCell(cell, c);
                    else PaintGround(cell, c, rng);
                    SpawnObject(c, x, y, skeletonSpawns, rng);
                }

            if (Hd)
            {
                BuildPaintedGround(PaintTownGround);
                DecorateForestEdges();
                DecorateVillageNature();
            }
            else if (CanyonHd) BuildCanyonHd();
            else if (WinterHd) BuildWinterHd();
            HuntingScenery.Dress(objectsRoot,cells,width,height,HuntingGrounds.Get(MapId));
            CreateBoundaryWalls();
            ConfigureTownServices();
            SpawnDungeonGuide(); // [DUNGEON] 던전 안내원 by the village plaza
            Game.Cutscenes?.OnWorldRebuilt();
            StoryCast.SpawnFor(MapId, objectsRoot, Bounds); // [STORY] story characters present at this point of the story

            if (skeletonSpawns.Count > 0)
            {
                var spawner = new GameObject("SkeletonSpawner").AddComponent<EnemySpawner>();
                spawner.transform.SetParent(objectsRoot, false);
                var zone = HuntingGrounds.Get(MapId);
                if (zone != null) spawner.SetupField(zone, HuntingGrounds.PackPoints(skeletonSpawns)); // packs of three
                else spawner.Setup(config.skeletonStats, CharacterLook.Skeleton, skeletonSpawns);
            }
            if (Hd || CanyonHd || WinterHd) ApplySharpMaterial();
            ApplyDungeonTint(); // [DGNTERRAIN] darker mine / ice cave rooms (WorldBuilder.DungeonLook.cs)
            AddTreeFades();
            if (PointsOfInterest.Count == 0) PointsOfInterest.Add(PlayerSpawn);
            BuildMinimap();
            AddDungeonVignette(); // [DGNTERRAIN]
        }
    }
}
