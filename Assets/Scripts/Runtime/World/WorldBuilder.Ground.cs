using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
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
                case '[':
                case ']':
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
    }
}
