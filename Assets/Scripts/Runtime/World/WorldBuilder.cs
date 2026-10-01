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
            if (info.id == MapRegistry.Village && config.worldMap != null) return config.worldMap.text;
            var asset = Resources.Load<TextAsset>(info.resource);
            if (asset != null) return asset.text;
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
        public Vector2 ArrivalFrom(string fromMap, out Facing facing)
        {
            facing = Facing.Down;
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
            }
            else if (CanyonHd) BuildCanyonHd();
            else if (WinterHd) BuildWinterHd();
            CreateBoundaryWalls();
            SpawnDungeonGuide(); // [DUNGEON] 던전 안내원 by the village plaza

            if (skeletonSpawns.Count > 0)
            {
                var spawner = new GameObject("SkeletonSpawner").AddComponent<EnemySpawner>();
                spawner.transform.SetParent(objectsRoot, false);
                spawner.Setup(config.skeletonStats, CharacterLook.Skeleton, skeletonSpawns);
            }
            if (Hd || CanyonHd || WinterHd) ApplySharpMaterial();
            ApplyDungeonTint(); // [DGNTERRAIN] darker mine / ice cave rooms (WorldBuilder.DungeonLook.cs)
            AddTreeFades();
            if (PointsOfInterest.Count == 0) PointsOfInterest.Add(PlayerSpawn);
            BuildMinimap();
            AddDungeonVignette(); // [DGNTERRAIN]
        }

        /// <summary>
        /// Overview of the current map for the HUD minimap and the map window: the map itself rendered
        /// from above at <see cref="MinimapTexelsPerTile"/> texels per tile (characters, monsters and
        /// effects hidden), so it shows the real ground, roofs and trees. Falls back to one flat colour
        /// per tile when nothing can be rendered (no graphics device).
        /// </summary>
        public Texture2D Minimap { get; private set; }

        public const int MinimapTexelsPerTile = 16;

        /// <summary>Centres of every portal cell (drawn as exits on the minimap).</summary>
        public readonly List<Vector2> PortalPoints = new List<Vector2>();

        void BuildMinimap()
        {
            if (Minimap != null) Destroy(Minimap);
            PortalPoints.Clear();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (cells[x, y] == '>' || cells[x, y] == '<') PortalPoints.Add(new Vector2(x + 0.5f, y + 0.5f));
            Minimap = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null ? RenderMinimap() : null;
            if (Minimap == null) Minimap = BuildFlatMinimap();
        }

        Texture2D RenderMinimap()
        {
            int tw = width * MinimapTexelsPerTile, th = height * MinimapTexelsPerTile;
            // Render at twice the size and halve it: every texel is then the average of four, so fine
            // detail turns into clean colour instead of flickering noise.
            int rw = tw * 2, rh = th * 2;
            if (rw > SystemInfo.maxTextureSize || rh > SystemInfo.maxTextureSize) { rw = tw; rh = th; }
            var camGo = new GameObject("MinimapCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = height * 0.5f;
            cam.aspect = width / (float)height;
            cam.transform.position = new Vector3(width * 0.5f, height * 0.5f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = config.backgroundColor;
            cam.nearClipPlane = -50f;
            cam.farClipPlane = 50f;
            var big = RenderTexture.GetTemporary(rw, rh, 16, RenderTextureFormat.ARGB32);
            big.filterMode = FilterMode.Bilinear;
            var small = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            var hidden = HideForMinimap();
            Texture2D tex = null;
            try
            {
                cam.targetTexture = big;
                cam.Render();
                Graphics.Blit(big, small);
                var prev = RenderTexture.active;
                RenderTexture.active = small;
                tex = new Texture2D(tw, th, TextureFormat.RGBA32, true)
                {
                    name = "Minimap_" + MapId,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                tex.Apply(true, false);
                RenderTexture.active = prev;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[dotRPG] Minimap render failed: {e.Message}");
                if (tex != null) Destroy(tex);
                tex = null;
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(big);
                RenderTexture.ReleaseTemporary(small);
                Destroy(camGo);
            }
            return tex;
        }

        /// <summary>Characters, drops, glows and exit arrows are left out of the minimap picture.</summary>
        List<Renderer> HideForMinimap()
        {
            var list = new List<Renderer>();
            void Hide(Component root)
            {
                if (root == null) return;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; list.Add(r); }
            }
            foreach (var npc in objectsRoot.GetComponentsInChildren<NpcController>(true)) Hide(npc);
            foreach (var e in objectsRoot.GetComponentsInChildren<EnemyController>(true)) Hide(e);
            foreach (var p in objectsRoot.GetComponentsInChildren<Pickup>(true)) Hide(p);
            if (Game.Player != null) Hide(Game.Player);
            if (Fx.Root != null) Hide(Fx.Root);
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (!sr.forceRenderingOff && (sr.gameObject.name.StartsWith("arrow_") || sr.sharedMaterial == FxMaterials.Additive))
                {
                    sr.forceRenderingOff = true;
                    list.Add(sr);
                }
            return list;
        }

        /// <summary>One flat colour per tile (the minimap before it was rendered from the map art).</summary>
        Texture2D BuildFlatMinimap()
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Minimap_" + MapId,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    px[y * width + x] = MinimapColor(x, y);
            // Houses and the construction site cover a 3x3 footprint from their anchor.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    char c = cells[x, y];
                    if (c == 'H' || c == 'G' || c == 'X')
                    {
                        var roof = c == 'X' ? new Color32(134, 224, 160, 255)
                            : c == 'G' ? new Color32(63, 143, 79, 255)
                            : Winter ? new Color32(168, 112, 74, 255)
                            : Canyon ? new Color32(192, 57, 43, 255) : new Color32(59, 143, 227, 255);
                        for (int yy = y; yy < Mathf.Min(height, y + 3); yy++)
                            for (int xx = x; xx < Mathf.Min(width, x + 3); xx++)
                                px[yy * width + xx] = roof;
                    }
                    else if (Winter && c == 'N')
                    {
                        for (int yy = y; yy < Mathf.Min(height, y + 4); yy++)
                            for (int xx = x; xx < Mathf.Min(width, x + 5); xx++)
                                px[yy * width + xx] = new Color32(201, 162, 122, 255);
                    }
                    else if (c == 'I' || c == 'M')
                    {
                        int w = c == 'I' ? 5 : 3, h = c == 'I' ? 4 : 2;
                        var roof = c == 'I' ? new Color32(184, 67, 47, 255) : new Color32(221, 106, 76, 255);
                        for (int yy = y; yy < Mathf.Min(height, y + h); yy++)
                            for (int xx = x; xx < Mathf.Min(width, x + w); xx++)
                                px[yy * width + xx] = roof;
                    }
                    else if (c == '>' || c == '<')
                    {
                        px[y * width + x] = new Color32(255, 211, 74, 255);
                    }
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        Color32 MinimapColor(int x, int y)
        {
            if (Winter) return WinterMinimapColor(x, y);
            char c = cells[x, y];
            switch (c)
            {
                case 'T': case 'O': return new Color32(47, 125, 50, 255);
                case 'R': return new Color32(154, 163, 174, 255);
                case 'F': return new Color32(142, 78, 42, 255);
                case 'B': return new Color32(62, 155, 67, 255);
            }
            switch (GroundAt(x, y))
            {
                case '~': return Canyon ? new Color32(47, 159, 166, 255) : new Color32(47, 127, 224, 255);
                case '=': return new Color32(217, 160, 102, 255);
                case '#': return new Color32(173, 112, 64, 255);
                case 'd': return new Color32(224, 160, 106, 255);
                case ',': return new Color32(201, 167, 124, 255);
                case 'W': return new Color32(70, 54, 46, 255);
                case 'L': return new Color32(156, 125, 88, 255);
                default: return Canyon ? new Color32(79, 154, 69, 255) : new Color32(99, 199, 77, 255);
            }
        }

        Tilemap CreateTilemap(Transform parent, string name, int order, bool solid)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tilemap = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order;
            renderer.mode = TilemapRenderer.Mode.Chunk;
            if (solid)
            {
                go.AddComponent<TilemapCollider2D>();
            }
            return tilemap;
        }

        Tile GetTile(string key, bool solid = false)
        {
            string cacheKey = solid ? key + "|solid" : key;
            if (tiles.TryGetValue(cacheKey, out var tile)) return tile;
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = key;
            tile.sprite = Game.Art.Get(key);
            tile.colliderType = solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            tiles[cacheKey] = tile;
            return tile;
        }

        /// <summary>
        /// Ground under a cell. Object symbols (NPCs, signs...) have no ground of their own, so they
        /// take the ground shared by at least two neighbours (e.g. the fisher standing on the dock).
        /// </summary>
        char GroundAt(int x, int y)
        {
            char c = At(x, y);
            if (c == '\0') return '\0';
            char raw = RawGround(c);
            if (raw != '.') return raw;
            if (c == '.') return '.';
            char[] around = { RawGround(At(x - 1, y)), RawGround(At(x + 1, y)), RawGround(At(x, y + 1)), RawGround(At(x, y - 1)) };
            foreach (char candidate in new[] { '=', '#', 'd', ',' })
            {
                int count = 0;
                foreach (char a in around) if (a == candidate) count++;
                if (count >= 2) return candidate;
            }
            // Canyon objects stand on flagstone unless they are clearly on grass.
            if (Canyon)
            {
                int grass = 0;
                foreach (char a in around) if (a == '.') grass++;
                return grass >= 3 ? '.' : ',';
            }
            return '.';
        }

        char RawGround(char c)
        {
            if (CanyonHd) { char g = CanyonHdRawGround(c); if (g != NoGroundOverride) return g; }
            if (WinterHd) { char g = WinterHdRawGround(c); if (g != NoGroundOverride) return g; }
            switch (c)
            {
                case '~':
                case '=':
                case '#':
                case 'd':
                case ',':
                case 'W':
                case 'L':
                    return c;
                case 'C':
                    return '#';
                case 'V':
                    return Winter ? 'V' : '.';
                case 'i':
                    return Winter ? '~' : '.';
                case ':':
                case ';':
                case '%':
                    return Hd ? c : '.';
                case '>':
                case '<':
                    if (Hd) return ForestMap ? '=' : ',';
                    return Canyon || Winter ? ',' : '=';
                case '\0':
                    return '\0';
                default:
                    return '.';
            }
        }

        void PaintGround(Vector3Int cell, char c, System.Random rng)
        {
            if (Winter)
            {
                PaintWinterGround(cell, c, rng);
                return;
            }
            int x = cell.x, y = cell.y;
            char ground = GroundAt(x, y);
            if (IsWater(ground))
            {
                char above = GroundAt(x, y + 1);
                if (Canyon)
                {
                    int wmask = 0;
                    if (above == 'W') wmask |= 8;
                    else if (above == 'd') wmask |= 32;
                    else if (above != '\0' && !IsWater(above)) wmask |= 1;
                    if (IsLand(GroundAt(x + 1, y))) wmask |= 2;
                    if (IsLand(GroundAt(x - 1, y))) wmask |= 4;
                    if (IsLand(GroundAt(x, y - 1))) wmask |= 16;
                    waterMap.SetTile(cell, GetTile($"tile_cw_{wmask}_{(x * 7 + y * 3) % 4}", true));
                    return;
                }
                bool edge = !IsWater(above) && above != 'd' && above != '\0';
                waterMap.SetTile(cell, GetTile(edge ? $"tile_water_edge_{(x * 7 + y) % 4}" : $"tile_water_{(x * 3 + y * 5) % 4}", true));
                return;
            }
            if (ground == 'W')
            {
                // Rows of wall down to lower ground decide face vs. rock mass (3/4 view).
                int d = 1;
                while (d < 4 && GroundAt(x, y - d) == 'W') d++;
                char belowWall = GroundAt(x, y - d);
                if (belowWall == '\0' || belowWall == 'W') d = 4;
                int flags = 0;
                char up = GroundAt(x, y + 1);
                if (up != 'W' && up != '\0' && !IsWater(up)) flags |= 1;
                char l = GroundAt(x - 1, y), r = GroundAt(x + 1, y);
                if (l != 'W' && l != '\0') flags |= 2;
                if (r != 'W' && r != '\0') flags |= 4;
                cliffMap.SetTile(cell, GetTile($"tile_cliff_{d}_{flags}_{(x * 5 + y * 3) % 3}", true));
                return;
            }
            if (ground == 'L')
            {
                groundMap.SetTile(cell, GetTile("tile_stairs"));
                return;
            }
            if (ground == ',')
            {
                // Paving from one large seamless pattern; darker in the shade right below a cliff.
                bool shade = GroundAt(x, y + 1) == 'W';
                int px = ((x % 8) + 8) % 8, py = 7 - ((y % 8) + 8) % 8; // pattern rows run top-down
                groundMap.SetTile(cell, GetTile($"tile_pave_{px}_{py}" + (shade ? "_s" : "")));
                int gmask = 0;
                if (GroundAt(x, y + 1) == '.') gmask |= 1;
                if (GroundAt(x + 1, y) == '.') gmask |= 2;
                if (GroundAt(x, y - 1) == '.') gmask |= 4;
                if (GroundAt(x - 1, y) == '.') gmask |= 8;
                if (gmask != 0) edgeMap.SetTile(cell, GetTile($"deco_grassedge_{gmask}"));
                else if (c == ',' && !shade && rng.NextDouble() < 0.025) decoMap.SetTile(cell, GetTile("deco_moss"));
                return;
            }
            if (ground == 'd')
            {
                if (Canyon)
                {
                    int part = (GroundAt(x, y + 1) != 'd' ? 1 : 0) | (GroundAt(x, y - 1) != 'd' ? 2 : 0);
                    groundMap.SetTile(cell, GetTile($"tile_bridge_{part}"));
                    return;
                }
                groundMap.SetTile(cell, GetTile("tile_dock"));
                return;
            }
            if (IsDirt(ground) || IsSoil(ground))
            {
                bool soil = IsSoil(ground);
                System.Func<char, bool> same = soil ? (System.Func<char, bool>)IsSoil : IsDirt;
                int mask = 0;
                if (!IsGroundLike(GroundAt(x, y + 1), same)) mask |= 1;
                if (!IsGroundLike(GroundAt(x + 1, y), same)) mask |= 2;
                if (!IsGroundLike(GroundAt(x, y - 1), same)) mask |= 4;
                if (!IsGroundLike(GroundAt(x - 1, y), same)) mask |= 8;
                groundMap.SetTile(cell, GetTile($"tile_{(soil ? "soil" : "dirt")}_{mask}_{(x * 5 + y * 3) % 3}"));
                return;
            }

            groundMap.SetTile(cell, GetTile(Canyon ? $"tile_mossgrass_{(x + y) % 2}" : $"tile_grass_{(x + y) % 2}"));
            // Scatter small grass details on empty grass.
            if (c == '.' && rng.NextDouble() < 0.07) decoMap.SetTile(cell, GetTile("deco_tuft"));
        }

        static bool IsGroundLike(char neighbour, System.Func<char, bool> same) => neighbour == '\0' || same(neighbour);

        void SpawnObject(char c, int x, int y, List<Vector2> skeletonSpawns, System.Random rng)
        {
            if (SpawnDungeonSymbol(c, x, y)) return; // [DUNGEON] '@' doors and 1-9 spawn groups in dungeon rooms
            if (CanyonHd && SpawnCanyonHdObject(c, x, y, rng)) return;
            if (WinterHd && SpawnWinterHdObject(c, x, y, rng)) return;
            if (Winter && !WinterHd && SpawnWinterObject(c, x, y, rng)) return;
            if (Hd && SpawnTownObject(c, x, y, rng)) return;
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            switch (c)
            {
                case 'T': ResourceNode.Create(ResourceKind.Tree, foot, objectsRoot, config); break;
                case 'O': ResourceNode.Create(ResourceKind.Tree, foot, objectsRoot, config, true); break;
                case 'R': ResourceNode.Create(ResourceKind.Rock, foot, objectsRoot, config); break;
                case 'S': StaticProp("Stump", "stump", foot, new Vector2(0.8f, 0.5f), new Vector2(0f, 0.2f)); break;
                case 'B': StaticProp("Bush", "bush", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f)); break;
                case 'F': StaticProp("Fence", $"fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.5f), new Vector2(0f, 0.3f)); break;
                case 'f': Decoration($"deco_flower_{rng.Next(0, 2)}", center + new Vector2(rng.Next(-3, 4) / 16f, -0.3f)); break;
                case 'r': Decoration("deco_pebble", center + new Vector2(rng.Next(-3, 4) / 16f, -0.2f)); break;
                case 't': Decoration("deco_tuft", center); break;
                case 'C': CropPlot.Create(foot, objectsRoot, config); break;
                case 'c':
                    StaticProp("CarrotCrate", "crate", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f));
                    break;
                case 'x':
                    StaticProp("Box", "box", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f));
                    break;
                case 'b':
                    StaticProp("Barrel", "barrel", foot, new Vector2(0.7f, 0.5f), new Vector2(0f, 0.25f));
                    break;
                case 'I':
                {
                    // Inn landmark: anchor = bottom-left of a 5x4 footprint.
                    var pos = new Vector2(x + 2.5f, y + 0.1f);
                    var inn = StaticProp("Inn", "inn", pos, new Vector2(4.6f, 2.3f), new Vector2(0f, 1.3f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(inn.transform, false);
                    door.Setup("여관 둘러보기", "canyon_inn");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.3f, new Vector2(0f, 1.6f));
                    PointsOfInterest.Add(pos + Vector2.up * 1.5f);
                    break;
                }
                case 'M':
                    StaticProp("Stall", "stall", new Vector2(x + 1.5f, y + 0.1f), new Vector2(2.7f, 0.9f), new Vector2(0f, 0.5f));
                    break;
                case 'o':
                    StaticProp("Well", "well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.6f, 1.0f), new Vector2(0f, 0.55f));
                    break;
                case 'e':
                    StaticProp("Bench", "bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.25f));
                    break;
                case '$':
                    TreasureChest.Create($"{MapId}:{x}:{y}", foot, objectsRoot);
                    break;
                case '&':
                    Anvil.Create(foot, objectsRoot);
                    PointsOfInterest.Add(center);
                    break;
                case 'A':
                {
                    var gate = StaticProp("Gate", "gate", new Vector2(x + 2f, y + 0.1f), new Vector2(3.9f, 1.3f), new Vector2(0f, 0.65f));
                    var look = gate.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "canyon_gate");
                    look.ConfigureShape(new Vector2(0f, -0.2f), 0.6f, new Vector2(0f, 2.6f));
                    break;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "sign", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.4f));
                    break;
                }
                case 'H':
                case 'G':
                {
                    // Anchor is the bottom-left cell of a 3x3 footprint.
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    string sprite = c == 'G' ? "house_green" : Canyon ? "house_red" : "house";
                    var house = StaticProp("House", sprite, pos, new Vector2(2.8f, 1.9f), new Vector2(0f, 1.2f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(house.transform, false);
                    door.Setup("문 두드리기", Canyon ? "canyon_house_door" : "house_door");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.2f, new Vector2(0f, 1.4f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    break;
                }
                case 'X':
                {
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    ConstructionSite.Create(pos, objectsRoot);
                    PointsOfInterest.Add(pos + Vector2.up);
                    break;
                }
                case '>':
                case '<':
                {
                    string target = c == '>' ? map?.nextMap : map?.previousMap;
                    if (string.IsNullOrEmpty(target) || !MapRegistry.Exists(target))
                    {
                        Debug.LogWarning($"[dotRPG] Portal '{c}' on map '{MapId}' has no target map.");
                        break;
                    }
                    MapPortal.Create(center, target, objectsRoot);
                    if (!portalCells.TryGetValue(target, out var list)) portalCells[target] = list = new List<Vector2>();
                    list.Add(center);
                    // One arrow marker per portal group (on its first cell in reading order).
                    if (At(x - 1, y) != c && At(x, y + 1) != c) Decoration(PortalArrowKey(x, y), center, -19990);
                    break;
                }
                case 'P':
                    PlayerSpawn = center;
                    break;
                case 'k':
                    skeletonSpawns.Add(center);
                    break;
                default:
                    if ((c >= '1' && c <= '9') || (c >= 'A' && c <= 'Z'))
                    {
                        var def = config.GetNpcBySymbol(c) ?? FindDefaultNpc(c);
                        if (def != null)
                        {
                            NpcController.Create(def, center, objectsRoot);
                            PointsOfInterest.Add(center);
                        }
                        else if (c <= '9') Debug.LogWarning($"[dotRPG] Map uses NPC symbol '{c}' but no NpcDefinition has it.");
                    }
                    break;
            }
        }

        static List<NpcDefinition> defaultNpcs;

        /// <summary>NPCs added after GameConfig.asset was saved are still found via the code defaults.</summary>
        static NpcDefinition FindDefaultNpc(char symbol)
        {
            if (defaultNpcs == null) defaultNpcs = GameConfig.DefaultNpcs();
            foreach (var npc in defaultNpcs)
                if (!string.IsNullOrEmpty(npc.mapSymbol) && npc.mapSymbol[0] == symbol) return npc;
            return null;
        }

        /// <summary>The NPC that runs a town service (for shop doors that open the same window).</summary>
        public static NpcDefinition ServiceNpc(NpcService service)
        {
            if (Game.Config != null)
                foreach (var npc in Game.Config.npcs) if (npc.service == service) return npc;
            if (defaultNpcs == null) defaultNpcs = GameConfig.DefaultNpcs();
            foreach (var npc in defaultNpcs) if (npc.service == service) return npc;
            return null;
        }

        string PortalArrowKey(int x, int y)
        {
            float left = x, right = width - 1 - x, bottom = y, top = height - 1 - y;
            float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            if (min == right) return "arrow_right";
            if (min == left) return "arrow_left";
            if (min == top) return "arrow_up";
            return "arrow_down";
        }

        int FenceMask(int x, int y)
        {
            int mask = 0;
            if (At(x - 1, y) == 'F') mask |= 1;
            if (At(x + 1, y) == 'F') mask |= 2;
            if (At(x, y + 1) == 'F') mask |= 4;
            if (At(x, y - 1) == 'F') mask |= 8;
            return mask;
        }

        string SignDialogueFor(int x, int y)
        {
            // Signs are identified by their order on the map (top-left first) so text stays in JSON.
            // The first map keeps the original ids (sign_0...), other maps are prefixed: canyon_sign_0...
            string prefix = MapId == MapRegistry.Village ? "sign_" : MapId + "_sign_";
            int index = 0;
            for (int yy = height - 1; yy >= 0; yy--)
                for (int xx = 0; xx < width; xx++)
                {
                    if (cells[xx, yy] != 'n') continue;
                    if (xx == x && yy == y) return prefix + index;
                    index++;
                }
            return prefix + "0";
        }

        GameObject StaticProp(string name, string spriteKey, Vector2 position, Vector2 colliderSize, Vector2 colliderOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get(spriteKey);
            if (colliderSize != Vector2.zero)
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.size = colliderSize;
                col.offset = colliderOffset;
            }
            go.AddComponent<YSort>().Configure(true);
            return go;
        }

        void Decoration(string spriteKey, Vector2 position, int order = -20000)
        {
            var go = new GameObject(spriteKey);
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get(spriteKey);
            sr.sortingOrder = order; // always under characters
        }

        // ================= High-resolution town & forest =================

        static Tile colliderTile;

        /// <summary>An invisible, solid grid cell (the painted ground shows what is there).</summary>
        static Tile ColliderTile()
        {
            if (colliderTile != null) return colliderTile;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Blank", filterMode = FilterMode.Point };
            var clear = new Color32[16];
            tex.SetPixels32(clear);
            tex.Apply(false, true);
            colliderTile = ScriptableObject.CreateInstance<Tile>();
            colliderTile.name = "Solid";
            colliderTile.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            colliderTile.colliderType = Tile.ColliderType.Grid;
            return colliderTile;
        }

        /// <summary>Water and dense forest block movement; everything else is drawn by the painted ground.</summary>
        void PaintHdCollision(Vector3Int cell)
        {
            char g = GroundAt(cell.x, cell.y);
            if (g == '~') waterMap.SetTile(cell, ColliderTile());
            else if (g == '%') cliffMap.SetTile(cell, ColliderTile());
        }

        /// <summary>The painted ground of each high-resolution map, made once and reused on every visit.</summary>
        static readonly Dictionary<string, List<(Vector2 pos, Sprite sprite)>> paintedGround = new Dictionary<string, List<(Vector2, Sprite)>>();

        /// <summary>Forgets the painted ground (developer timing tests). The current map keeps its sprites until rebuilt.</summary>
        public static void ClearPaintedGroundCache() => paintedGround.Clear();

        Color32[] PaintTownGround(char[,] ground, int w, int h, out int pw, out int ph) => TownTerrain.Paint(ground, w, h, ForestMap, out pw, out ph);

        /// <summary>
        /// Paints the map's ground once with <paramref name="paint"/> (32 px per tile, cached per map id) and
        /// lays it out as 256px chunk sprites under everything else.
        /// </summary>
        void BuildPaintedGround(GroundPainter paint)
        {
            if (!paintedGround.TryGetValue(MapId, out var chunks))
            {
                var ground = new char[width, height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        ground[x, y] = GroundAt(x, y);
                var px = paint(ground, width, height, out int pw, out int ph);
                chunks = new List<(Vector2, Sprite)>();
                const int Chunk = 256;
                for (int cy = 0; cy < ph; cy += Chunk)
                    for (int cx = 0; cx < pw; cx += Chunk)
                    {
                        int w = Mathf.Min(Chunk, pw - cx), h = Mathf.Min(Chunk, ph - cy);
                        var block = new Color32[w * h];
                        for (int y = 0; y < h; y++) System.Array.Copy(px, (cy + y) * pw + cx, block, y * w, w);
                        var sprite = Game.Art.CreateRaw($"ground_{MapId}_{cx}_{cy}", block, w, h, 2);
                        chunks.Add((new Vector2(cx / (float)TownTerrain.Px, cy / (float)TownTerrain.Px), sprite));
                    }
                paintedGround[MapId] = chunks;
            }
            var root = new GameObject("PaintedGround").transform;
            root.SetParent(transform, false);
            foreach (var (pos, sprite) in chunks)
            {
                var go = new GameObject("Ground");
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = -30000;
                if (FxMaterials.Sharp != null) sr.sharedMaterial = FxMaterials.Sharp;
            }
        }

        /// <summary>
        /// Trees standing along the edge of every dense-forest area, so the painted canopy gets real
        /// trunks and crowns that overlap the player correctly. No colliders: the forest cells are solid.
        /// </summary>
        void DecorateForestEdges()
        {
            var rng = new System.Random(MapId.GetHashCode() & 0x7fff);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (cells[x, y] != '%') continue;
                    int open = 0, near = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            char n = At(x + dx, y + dy);
                            if (n == '\0' || n == '%') continue;
                            int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                            if (d == 1 && (dx == 0 || dy == 0)) open++;
                            else near++;
                        }
                    double chance = open > 0 ? 0.85 : near > 0 ? 0.4 : 0.0;
                    if (rng.NextDouble() >= chance) continue;
                    var pos = new Vector2(x + 0.5f + (float)(rng.NextDouble() - 0.5) * 0.5f, y + 0.3f + (float)(rng.NextDouble() - 0.5) * 0.3f);
                    string key = rng.NextDouble() < 0.22 ? $"town_pine_{rng.Next(0, 2)}"
                        : ForestMap ? $"town_tree_{3 + rng.Next(0, 3)}" : $"town_tree_{rng.Next(0, 3)}";
                    StaticProp("EdgeTree", key, pos, Vector2.zero, Vector2.zero);
                }
        }

        /// <summary>Gives every high-resolution sprite the crisp-scaling material (see SpriteSharp.shader).</summary>
        void ApplySharpMaterial()
        {
            var sharp = FxMaterials.Sharp;
            if (sharp == null) return;
            float basePpu = config.pixelsPerUnit + 0.5f;
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sprite != null && sr.sprite.pixelsPerUnit > basePpu && sr.sharedMaterial != FxMaterials.Additive)
                    sr.sharedMaterial = sharp;
        }

        /// <summary>
        /// Every tree turns see-through while the player stands behind it: free-standing trees, the trees
        /// along the forest edges and the choppable ones (all named "Tree" or "EdgeTree").
        /// </summary>
        void AddTreeFades()
        {
            foreach (Transform child in objectsRoot)
                if (child.name == "Tree" || child.name == "EdgeTree") TreeFade.Attach(child.gameObject);
        }

        /// <summary>Tree with a small trunk collider; its crown overlaps whatever is behind it.</summary>
        void HdTree(string sprite, Vector2 foot, float trunkWidth)
        {
            StaticProp("Tree", sprite, foot, new Vector2(trunkWidth, 0.35f), new Vector2(0f, 0.15f));
        }

        /// <summary>A building on a w x h footprint anchored at its bottom-left cell, with a door interactable.</summary>
        GameObject HdBuilding(string name, string sprite, int x, int y, int w, int h, string prompt, string dialogue, NpcService service = NpcService.None)
        {
            var pos = new Vector2(x + w * 0.5f, y + 0.1f);
            float colH = Mathf.Max(1f, h - 0.85f);
            var go = StaticProp(name, sprite, pos, new Vector2(w - 0.15f, colH), new Vector2(0f, colH * 0.5f + 0.05f));
            if (service != NpcService.None) ServiceDoor.Attach(go, service, prompt);
            else if (!string.IsNullOrEmpty(dialogue))
            {
                var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                door.transform.SetParent(go.transform, false);
                door.Setup(prompt, dialogue);
                door.ConfigureShape(new Vector2(0f, 0f), 0.25f, new Vector2(0f, 1.5f));
            }
            PointsOfInterest.Add(pos + Vector2.up * 1.2f);
            return go;
        }

        /// <summary>Objects of the high-resolution maps. Returns false for symbols handled by the shared code (NPCs, portals, start, spawns).</summary>
        bool SpawnTownObject(char c, int x, int y, System.Random rng)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            Vector2 Jitter(float amount) => new Vector2((float)(rng.NextDouble() - 0.5) * amount, (float)(rng.NextDouble() - 0.5) * amount * 0.5f);
            switch (c)
            {
                case 'T': HdTree(ForestMap ? $"town_tree_{3 + rng.Next(0, 3)}" : $"town_tree_{rng.Next(0, 3)}", foot + Jitter(0.3f), 0.7f); return true;
                case 'O': HdTree($"town_fruit_{rng.Next(0, 2)}", foot + Jitter(0.2f), 0.6f); return true;
                case 'Y': HdTree($"town_pine_{rng.Next(0, 2)}", foot + Jitter(0.3f), 0.5f); return true;
                case 'q': HdTree($"town_dead_{rng.Next(0, 2)}", foot, 0.5f); return true;
                case 't': ResourceNode.Create(ResourceKind.Tree, foot, objectsRoot, config, false, true); return true;
                case 'R': ResourceNode.Create(ResourceKind.Rock, foot, objectsRoot, config, false, true); return true;
                case 'S': StaticProp("Stump", "town_stump", foot, new Vector2(0.8f, 0.45f), new Vector2(0f, 0.2f)); return true;
                case 'B': StaticProp("Bush", $"town_bush_{rng.Next(0, 3)}", foot, new Vector2(0.95f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'r': Decoration($"town_pebbles_{rng.Next(0, 2)}", center + new Vector2(0f, -0.25f)); return true;
                case 'F': StaticProp("Fence", $"town_fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.45f), new Vector2(0f, 0.28f)); return true;
                case 'b': StaticProp("Barrel", "town_barrel", foot, new Vector2(0.7f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'x': StaticProp("Crate", "town_crate", foot, new Vector2(0.85f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'c': StaticProp("CarrotCrate", "town_ccrate", foot, new Vector2(0.85f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 's': StaticProp("Sacks", "town_sacks", foot, new Vector2(0.95f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'h': StaticProp("Hay", "town_hay", foot, new Vector2(1f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'u': StaticProp("FlowerPot", "town_pot", foot, new Vector2(0.55f, 0.35f), new Vector2(0f, 0.17f)); return true;
                case 'v': StaticProp("FlowerBox", "town_planter", foot, new Vector2(0.95f, 0.35f), new Vector2(0f, 0.17f)); return true;
                case 'm': StaticProp("Mailbox", "town_mailbox", foot, new Vector2(0.45f, 0.3f), new Vector2(0f, 0.15f)); return true;
                case 'z': StaticProp("Woodpile", "town_woodpile", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'e': StaticProp("Bench", "town_bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.22f)); return true;
                case 'w': StaticProp("Well", "town_well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.7f, 0.9f), new Vector2(0f, 0.5f)); PointsOfInterest.Add(center); return true;
                case 'p':
                {
                    var lamp = StaticProp("Lamp", "town_lamp", foot, new Vector2(0.35f, 0.3f), new Vector2(0f, 0.15f));
                    WarmGlow.Attach(lamp.transform, new Vector2(0f, 1.95f), 1.4f, 0.28f);
                    return true;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "town_sign_0", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.5f));
                    return true;
                }
                case 'N':
                {
                    var board = StaticProp("NoticeBoard", "town_board", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = board.AddComponent<DialogueInteractable>();
                    interact.Setup("게시판 읽기", "town_board");
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.5f, new Vector2(0f, 2.1f));
                    PointsOfInterest.Add(center);
                    return true;
                }
                case '&': Anvil.Create(foot, objectsRoot, "town_anvil"); return true;
                case 'C': CropPlot.Create(foot, objectsRoot, config, true); return true;
                case '$': TreasureChest.Create($"{MapId}:{x}:{y}", foot, objectsRoot, true); return true;
                case 'g': StaticProp("Grave", $"town_grave_{rng.Next(0, 2)}", foot, new Vector2(0.75f, 0.35f), new Vector2(0f, 0.17f)); return true;
                case 'i': StaticProp("Ruin", "town_ruin", foot, new Vector2(0.7f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'j': Decoration("town_bones", center + new Vector2(0f, -0.2f)); return true;
                case 'o': Decoration("town_shroom", center + new Vector2(0f, -0.2f)); return true;
                case 'l': StaticProp("Log", "town_log", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.45f), new Vector2(0f, 0.25f)); return true;
                case 'H':
                {
                    int style = (x * 7 + y * 3) % 3;
                    HdBuilding("House", $"town_house_{style}", x, y, 3, 3, "문 두드리기", "town_house_door");
                    return true;
                }
                case 'I': HdBuilding("ChiefHall", "town_hall", x, y, 5, 4, "문 두드리기", "town_hall_door"); return true;
                case 'M': HdBuilding("GeneralStore", "town_store", x, y, 4, 3, "잡화점 들어가기", null, NpcService.Shop); return true;
                case 'A': HdBuilding("Smithy", "town_smithy", x, y, 4, 3, "대장간 들어가기", null, NpcService.Blacksmith); return true;
                case 'Z': HdBuilding("Warehouse", "town_warehouse", x, y, 4, 3, "창고 들어가기", null, NpcService.Storage); return true;
                case 'X':
                {
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    ConstructionSite.Create(pos, objectsRoot, true);
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'Q':
                {
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    var fountain = StaticProp("Fountain", "town_fountain_0", pos, new Vector2(2.8f, 1.1f), new Vector2(0f, 0.65f));
                    fountain.AddComponent<SpriteCycler>().Setup(new[] { "town_fountain_0", "town_fountain_1", "town_fountain_2" }, 6f);
                    var look = fountain.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "town_fountain");
                    look.ConfigureShape(new Vector2(0f, -0.1f), 0.9f, new Vector2(0f, 2.9f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
            }
            return false;
        }

        // ================= Winter village =================

        /// <summary>
        /// Winter ground: one seamless snow field, cobble paths whose edges are buried in snow, low
        /// cliffs with snowy lips, the river with snowy banks, a waterfall where it crosses the cliff,
        /// wooden bridge/deck and stone stairs.
        /// </summary>
        void PaintWinterGround(Vector3Int cell, char c, System.Random rng)
        {
            int x = cell.x, y = cell.y;
            char ground = GroundAt(x, y);
            if (ground == 'V')
            {
                int part = GroundAt(x, y + 1) != 'V' ? 0 : GroundAt(x, y - 1) != 'V' ? 2 : 1;
                int sides = (GroundAt(x - 1, y) != 'V' ? 1 : 0) | (GroundAt(x + 1, y) != 'V' ? 2 : 0);
                waterMap.SetTile(cell, GetTile($"snow_fall_{part}_{x % 3}_{sides}", true));
                return;
            }
            if (ground == '~')
            {
                char above = GroundAt(x, y + 1), below = GroundAt(x, y - 1);
                int mask = 0;
                if (above == 'W') mask |= 8;
                else if (above == 'd') mask |= 32;
                else if (above == 'V') mask |= 64;
                else if (above != '\0' && !IsWater(above)) mask |= 1;
                if (IsLand(GroundAt(x + 1, y))) mask |= 2;
                if (IsLand(GroundAt(x - 1, y))) mask |= 4;
                if (IsLand(below) && below != 'W') mask |= 16;
                int bend = WinterBend(x, y);
                if (bend != 0) mask |= bend << 7;
                else
                {
                    // A neighbour's S-curve reaching into this cell.
                    if (WinterBend(x - 1, y) == 1) mask |= 1 << 11;
                    if (WinterBend(x + 1, y) == 2) mask |= 2 << 11;
                    if (WinterBend(x - 1, y) == 4) mask |= 4 << 11;
                    if (WinterBend(x + 1, y) == 8) mask |= 8 << 11;
                }
                waterMap.SetTile(cell, GetTile($"snow_water_{mask}_{(x * 7 + y * 3) % 4}", true));
                return;
            }
            if (ground == 'W')
            {
                int d = 1;
                while (d < 4 && GroundAt(x, y - d) == 'W') d++;
                char belowWall = GroundAt(x, y - d);
                if (belowWall == '\0' || belowWall == 'W') d = 4;
                int flags = 0;
                char up = GroundAt(x, y + 1);
                if (up != 'W' && up != '\0' && !IsWater(up)) flags |= 1;
                char l = GroundAt(x - 1, y), r = GroundAt(x + 1, y);
                if (l != 'W' && l != '\0') flags |= 2;
                if (r != 'W' && r != '\0') flags |= 4;
                cliffMap.SetTile(cell, GetTile($"snow_cliff_{d}_{flags}_{(x * 5 + y * 3) % 3}", true));
                return;
            }
            if (ground == 'L')
            {
                int sides = (GroundAt(x - 1, y) != 'L' ? 1 : 0) | (GroundAt(x + 1, y) != 'L' ? 2 : 0);
                groundMap.SetTile(cell, GetTile($"snow_stairs_{sides}"));
                return;
            }
            if (ground == 'd')
            {
                int part = (GroundAt(x, y + 1) != 'd' ? 1 : 0) | (GroundAt(x, y - 1) != 'd' ? 2 : 0);
                groundMap.SetTile(cell, GetTile($"snow_bridge_{part}"));
                return;
            }
            // Snow and path both sample 128px seamless patterns, so no tile border shows.
            int sx = ((x % 8) + 8) % 8, sy = 7 - ((y % 8) + 8) % 8;
            if (ground == ',')
            {
                groundMap.SetTile(cell, GetTile($"snow_path_{sx}_{sy}"));
                bool Snow(int dx, int dy) => GroundAt(x + dx, y + dy) == '.';
                int mask = 0;
                if (Snow(0, 1)) mask |= 1;
                if (Snow(1, 0)) mask |= 2;
                if (Snow(0, -1)) mask |= 4;
                if (Snow(-1, 0)) mask |= 8;
                if ((mask & 3) == 0 && Snow(1, 1)) mask |= 16;
                if ((mask & 6) == 0 && Snow(1, -1)) mask |= 32;
                if ((mask & 12) == 0 && Snow(-1, -1)) mask |= 64;
                if ((mask & 9) == 0 && Snow(-1, 1)) mask |= 128;
                if (mask != 0) edgeMap.SetTile(cell, GetTile($"snow_pathedge_{mask}"));
                return;
            }
            groundMap.SetTile(cell, GetTile($"snow_ground_{sx}_{sy}"));
            // Rare small marks of varied kinds (drift bumps, ice pellets, tracks, sparkles...).
            if (c == '.' && rng.NextDouble() < 0.045) decoMap.SetTile(cell, GetTile($"snow_speck_{rng.Next(0, 12)}"));
        }

        /// <summary>
        /// Smooth river bends: a water cell at the corner of a one-tile step in the bank (land on two sides,
        /// the bank carrying on one column over above or below) is drawn as one S-shaped bank, so the river
        /// reads as a curve instead of a staircase. Returns 1 (land N+W), 2 (N+E), 4 (S+W), 8 (S+E) or 0.
        /// </summary>
        int WinterBend(int x, int y)
        {
            if (GroundAt(x, y) != '~') return 0;
            bool Solid(int dx, int dy) { char ch = GroundAt(x + dx, y + dy); return ch != '\0' && !IsWater(ch) && ch != 'd'; }
            bool Wet(int dx, int dy) => IsWater(GroundAt(x + dx, y + dy));
            bool n = Solid(0, 1), s = Solid(0, -1), w = Solid(-1, 0), e = Solid(1, 0);
            if (n && w && Wet(1, 0) && Wet(0, -1) && Wet(1, 1) && Solid(-1, -1)) return 1;
            if (n && e && Wet(-1, 0) && Wet(0, -1) && Wet(-1, 1) && Solid(1, -1)) return 2;
            if (s && w && Wet(1, 0) && Wet(0, 1) && Wet(1, -1) && Solid(-1, 1)) return 4;
            if (s && e && Wet(-1, 0) && Wet(0, 1) && Wet(-1, -1) && Solid(1, 1)) return 8;
            return 0;
        }

        /// <summary>Objects of the winter village. Returns false for symbols shared with the other maps.</summary>
        bool SpawnWinterObject(char c, int x, int y, System.Random rng)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            switch (c)
            {
                case 'Q': WinterTree($"snow_tree_0_{rng.Next(0, 3)}", foot, 0.7f); return true;
                case 'q': WinterTree($"snow_tree_{(rng.NextDouble() < 0.5 ? 1 : 2)}_{rng.Next(0, 3)}", foot, 0.55f); return true;
                case 'Y': WinterTree($"snow_pine_0_{rng.Next(0, 3)}", foot, 0.5f); return true;
                case 'y': WinterTree($"snow_pine_1_{rng.Next(0, 3)}", foot, 0.4f); return true;
                case 'B': StaticProp("Bush", $"snow_bush_{rng.Next(0, 3)}", foot, new Vector2(0.9f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'g': Decoration($"snow_grass_{rng.Next(0, 3)}", center + new Vector2(rng.Next(-4, 5) / 16f, -0.25f)); return true;
                case 'R': StaticProp("Stone", $"snow_rock_{rng.Next(0, 2)}", foot, new Vector2(0.8f, 0.45f), new Vector2(0f, 0.2f)); return true;
                case 'i': Decoration($"snow_ice_{rng.Next(0, 3)}", center + new Vector2(rng.Next(-3, 4) / 16f, rng.Next(-3, 4) / 16f), -20500); return true;
                case 'F': StaticProp("Fence", $"snow_fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'v': StaticProp("GardenBed", $"snow_bed_{rng.Next(0, 2)}", new Vector2(x + 0.5f, y + 0.1f), new Vector2(0.9f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'h': StaticProp("Hay", "snow_hay", foot, new Vector2(1f, 0.55f), new Vector2(0f, 0.3f)); return true;
                case 'o': StaticProp("Well", "snow_well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.6f, 1.0f), new Vector2(0f, 0.55f)); return true;
                case 'e': StaticProp("Bench", "snow_bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.25f)); return true;
                case 'l': StaticProp("LogSeat", "snow_log", foot, new Vector2(1.2f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'w': StaticProp("Workbench", "snow_workbench", new Vector2(x + 1f, y + 0.1f), new Vector2(2f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'm': StaticProp("Mailbox", "snow_mailbox", foot, new Vector2(0.5f, 0.3f), new Vector2(0f, 0.15f)); return true;
                case 'u': StaticProp("FlowerPot", "snow_pot", foot, new Vector2(0.6f, 0.35f), new Vector2(0f, 0.18f)); return true;
                case 'z': StaticProp("Woodpile", "snow_woodpile", new Vector2(x + 1f, y + 0.1f), new Vector2(1.7f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'b': StaticProp("Barrel", "snow_barrel", foot, new Vector2(0.7f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'x': StaticProp("Crate", "snow_crate", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f)); return true;
                case 'p':
                {
                    var lamp = StaticProp("Lamp", "snow_lamp", foot, new Vector2(0.35f, 0.3f), new Vector2(0f, 0.15f));
                    WarmGlow.Attach(lamp.transform, new Vector2(0f, 2.35f), 1.5f, 0.3f);
                    return true;
                }
                case 'j':
                {
                    var fire = StaticProp("Campfire", "snow_fire_0", foot, new Vector2(1.1f, 0.5f), new Vector2(0f, 0.25f));
                    fire.AddComponent<CampfireFx>();
                    var warm = fire.AddComponent<DialogueInteractable>();
                    warm.Setup("불 쬐기", "winter_fire");
                    warm.ConfigureShape(new Vector2(0f, 0.2f), 0.3f, new Vector2(0f, 1.3f));
                    PointsOfInterest.Add(center);
                    return true;
                }
                case 'H':
                {
                    // Anchor is the bottom-left cell of a 3x3 footprint.
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    var house = StaticProp("House", "snow_house", pos, new Vector2(2.8f, 1.9f), new Vector2(0f, 1.2f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(house.transform, false);
                    door.Setup("문 두드리기", "winter_house_door");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.2f, new Vector2(0f, 1.4f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'N':
                {
                    // Farm building: anchor = bottom-left of a 5x4 footprint.
                    var pos = new Vector2(x + 2.5f, y + 0.1f);
                    var barn = StaticProp("Barn", "snow_barn", pos, new Vector2(4.7f, 2.4f), new Vector2(0f, 1.3f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(barn.transform, false);
                    door.Setup("살펴보기", "winter_barn");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.4f, new Vector2(0f, 1.9f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "snow_sign", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.4f));
                    return true;
                }
                case 'A':
                {
                    var gate = StaticProp("Gate", "snow_gate", new Vector2(x + 2f, y + 0.1f), new Vector2(3.9f, 1.3f), new Vector2(0f, 0.65f));
                    var look = gate.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "winter_gate");
                    look.ConfigureShape(new Vector2(0f, -0.2f), 0.6f, new Vector2(0f, 3f));
                    return true;
                }
            }
            return false;
        }

        /// <summary>Snowy tree: only the trunk blocks movement, the crown overlaps what is behind it.</summary>
        void WinterTree(string sprite, Vector2 foot, float trunkWidth)
        {
            StaticProp("Tree", sprite, foot, new Vector2(trunkWidth, 0.35f), new Vector2(0f, 0.15f));
        }

        Color32 WinterMinimapColor(int x, int y)
        {
            switch (cells[x, y])
            {
                case 'Q': case 'q': return new Color32(95, 165, 150, 255);
                case 'Y': case 'y': return new Color32(64, 130, 112, 255);
                case 'R': return new Color32(163, 169, 186, 255);
                case 'F': return new Color32(168, 112, 74, 255);
                case 'B': return new Color32(111, 188, 171, 255);
            }
            switch (GroundAt(x, y))
            {
                case '~': case 'V': return new Color32(79, 159, 220, 255);
                case ',': return new Color32(183, 187, 200, 255);
                case 'd': return new Color32(201, 144, 104, 255);
                case 'W': return new Color32(154, 142, 166, 255);
                case 'L': return new Color32(201, 198, 210, 255);
                default: return new Color32(236, 242, 244, 255);
            }
        }

        void CreateBoundaryWalls()
        {
            var walls = new GameObject("Bounds");
            walls.transform.SetParent(transform, false);
            void Wall(Vector2 center, Vector2 size)
            {
                var box = walls.AddComponent<BoxCollider2D>();
                box.offset = center;
                box.size = size;
            }
            Wall(new Vector2(width * 0.5f, -0.5f), new Vector2(width + 2, 1));
            Wall(new Vector2(width * 0.5f, height + 0.5f), new Vector2(width + 2, 1));
            Wall(new Vector2(-0.5f, height * 0.5f), new Vector2(1, height + 2));
            Wall(new Vector2(width + 0.5f, height * 0.5f), new Vector2(1, height + 2));
        }

        const string FallbackMap =
            "TTTTTTTTTTTT\n" +
            "T....1.....T\n" +
            "T..P.......T\n" +
            "T....R..k..T\n" +
            "T~~~~......T\n" +
            "TTTTTTTTTTTT\n";
    }
}
