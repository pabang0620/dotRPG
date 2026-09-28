using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    /// <summary>
    /// Builds the playable map from a plain-text layout (Resources/Maps/Village.txt):
    /// ground and water go into Unity Tilemaps, everything else is spawned as objects.
    /// Text maps are quick to iterate on without an editor; later maps can be painted with the
    /// Tile Palette instead and only the object spawning here needs to be kept.
    ///
    /// Legend (see also the header of Village.txt):
    ///   .  grass            =  dirt path         #  farm soil        ~  water        d  dock
    ///   T  tree             O  fruit tree        S  stump            R  rock (mineable)
    ///   B  berry bush       F  fence             f  flower           r  pebble       t  grass tuft
    ///   C  carrot on soil   c  carrot crate      n  sign             H  house (anchor)
    ///   X  construction site (anchor)            P  player start     k  skeleton spawn
    ///   1-9 NPCs (defined in GameConfig.npcs)
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        GameConfig config;
        char[,] cells;
        int width, height;

        Transform objectsRoot;
        Tilemap groundMap, waterMap, decoMap;
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();

        public Vector2 PlayerSpawn { get; private set; }
        public Rect Bounds { get; private set; }
        public Transform ObjectsRoot => objectsRoot;
        /// <summary>Interesting spots for the title-screen camera to drift between.</summary>
        public readonly List<Vector2> PointsOfInterest = new List<Vector2>();

        public static WorldBuilder Create(GameConfig config)
        {
            var go = new GameObject("World");
            var builder = go.AddComponent<WorldBuilder>();
            builder.config = config;
            builder.Parse(config.worldMap != null ? config.worldMap.text : FallbackMap);
            builder.Build();
            return builder;
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
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row; // first text row is the top of the map
                for (int x = 0; x < width; x++)
                    cells[x, y] = x < rows[row].Length ? rows[row][x] : '.';
            }
            Bounds = new Rect(0, 0, width, height);
        }

        char At(int x, int y) => x >= 0 && y >= 0 && x < width && y < height ? cells[x, y] : '\0';

        static bool IsWater(char c) => c == '~';
        static bool IsDirt(char c) => c == '=';
        static bool IsSoil(char c) => c == '#' || c == 'C';

        void Build()
        {
            var grid = new GameObject("Grid").AddComponent<Grid>();
            grid.transform.SetParent(transform, false);
            groundMap = CreateTilemap(grid.transform, "Ground", -30000, false);
            decoMap = CreateTilemap(grid.transform, "GroundDetail", -29000, false);
            waterMap = CreateTilemap(grid.transform, "Water", -29500, true);

            objectsRoot = new GameObject("Objects").transform;
            objectsRoot.SetParent(transform, false);

            var fxRoot = new GameObject("Fx").transform;
            fxRoot.SetParent(transform, false);
            Fx.Root = fxRoot;

            var skeletonSpawns = new List<Vector2>();
            PlayerSpawn = new Vector2(width * 0.5f, height * 0.5f);
            var rng = new System.Random(1234);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    char c = cells[x, y];
                    var cell = new Vector3Int(x, y, 0);
                    PaintGround(cell, c, rng);
                    SpawnObject(c, x, y, skeletonSpawns, rng);
                }

            CreateBoundaryWalls();

            if (skeletonSpawns.Count > 0)
            {
                var spawner = new GameObject("SkeletonSpawner").AddComponent<EnemySpawner>();
                spawner.transform.SetParent(objectsRoot, false);
                spawner.Setup(config.skeletonStats, CharacterLook.Skeleton, skeletonSpawns);
            }
            if (PointsOfInterest.Count == 0) PointsOfInterest.Add(PlayerSpawn);
        }

        Tilemap CreateTilemap(Transform parent, string name, int order, bool solid)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var map = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order;
            renderer.mode = TilemapRenderer.Mode.Chunk;
            if (solid)
            {
                go.AddComponent<TilemapCollider2D>();
            }
            return map;
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
            foreach (char candidate in new[] { '=', '#', 'd' })
            {
                int count = 0;
                foreach (char a in around) if (a == candidate) count++;
                if (count >= 2) return candidate;
            }
            return '.';
        }

        static char RawGround(char c)
        {
            switch (c)
            {
                case '~':
                case '=':
                case '#':
                case 'd':
                    return c;
                case 'C':
                    return '#';
                default:
                    return '.';
            }
        }

        void PaintGround(Vector3Int cell, char c, System.Random rng)
        {
            int x = cell.x, y = cell.y;
            char ground = GroundAt(x, y);
            if (IsWater(ground))
            {
                char above = GroundAt(x, y + 1);
                bool edge = !IsWater(above) && above != 'd' && above != '\0';
                waterMap.SetTile(cell, GetTile(edge ? $"tile_water_edge_{(x * 7 + y) % 4}" : $"tile_water_{(x * 3 + y * 5) % 4}", true));
                return;
            }
            if (ground == 'd')
            {
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

            groundMap.SetTile(cell, GetTile($"tile_grass_{(x + y) % 2}"));
            // Scatter small grass details on empty grass.
            if (c == '.' && rng.NextDouble() < 0.07) decoMap.SetTile(cell, GetTile("deco_tuft"));
        }

        static bool IsGroundLike(char neighbour, System.Func<char, bool> same) => neighbour == '\0' || same(neighbour);

        void SpawnObject(char c, int x, int y, List<Vector2> skeletonSpawns, System.Random rng)
        {
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
                case 'n':
                {
                    var sign = StaticProp("Sign", "sign", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.4f));
                    break;
                }
                case 'H':
                {
                    // Anchor is the bottom-left cell of a 3x3 footprint.
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    var house = StaticProp("House", "house", pos, new Vector2(2.8f, 1.9f), new Vector2(0f, 1.2f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(house.transform, false);
                    door.Setup("문 두드리기", "house_door");
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
                case 'P':
                    PlayerSpawn = center;
                    break;
                case 'k':
                    skeletonSpawns.Add(center);
                    break;
                default:
                    if (c >= '1' && c <= '9')
                    {
                        var def = config.GetNpcBySymbol(c);
                        if (def != null)
                        {
                            NpcController.Create(def, center, objectsRoot);
                            PointsOfInterest.Add(center);
                        }
                        else Debug.LogWarning($"[dotRPG] Map uses NPC symbol '{c}' but no NpcDefinition has it.");
                    }
                    break;
            }
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
            int index = 0;
            for (int yy = height - 1; yy >= 0; yy--)
                for (int xx = 0; xx < width; xx++)
                {
                    if (cells[xx, yy] != 'n') continue;
                    if (xx == x && yy == y) return $"sign_{index}";
                    index++;
                }
            return "sign_0";
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

        void Decoration(string spriteKey, Vector2 position)
        {
            var go = new GameObject(spriteKey);
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get(spriteKey);
            sr.sortingOrder = -20000; // always under characters
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
