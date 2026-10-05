using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Tile-grid steering for AI movement (companions, the automated test player). Walking straight at a
    /// target stalls at water or walls (the 지하 수로 room has one bridge); when the straight line is
    /// blocked this returns the direction of the next tile on the shortest way, from a breadth-first
    /// distance field around the target tile. The walkable grid is built once per loaded map from the
    /// static colliders; fields are cached per target tile. No allocation after the first build.
    /// </summary>
    public static class GridPath
    {
        const float ProbeRadius = 0.3f;

        static string mapKey;
        static Rect bounds;
        static bool built;
        static int w, h;
        static bool[] free;
        static int[] field, queue;
        static int fieldTarget = -1;
        static readonly Collider2D[] hits = new Collider2D[16];
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Forget the grid (a gate opened, the map was rebuilt).</summary>
        public static void Invalidate() => built = false;

        /// <summary>Unit direction to walk from <paramref name="from"/> to reach <paramref name="to"/> (feet positions).</summary>
        public static Vector2 Steer(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.0001f) return Vector2.zero;
            Vector2 straight = d / len;
            if (!Ensure() || LineClear(from, to)) return straight;
            int target = Cell(to);
            if (target < 0) return straight;
            if (target != fieldTarget) BuildField(target);
            int me = Cell(from);
            if (me < 0) return straight;
            int best = -1, bestDist = field[me] >= 0 ? field[me] : int.MaxValue;
            int mx = me % w, my = me / w;
            for (int k = 0; k < 8; k++)
            {
                int nx = mx + DX[k], ny = my + DY[k];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int n = ny * w + nx;
                if (field[n] < 0 || field[n] >= bestDist) continue;
                // Diagonal steps may not cut a blocked corner.
                if (k >= 4 && (!free[my * w + nx] || !free[ny * w + mx])) continue;
                best = n; bestDist = field[n];
            }
            if (best < 0) return straight; // unreachable: keep pushing, the caller's stuck logic takes over
            Vector2 next = CellCenter(best);
            Vector2 dir = next - from;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : straight;
        }

        static bool Ensure()
        {
            var world = Game.World;
            if (world == null) return false;
            if (built && world.MapId == mapKey && world.Bounds == bounds) return true;
            built = true;
            mapKey = world.MapId;
            bounds = world.Bounds;
            w = Mathf.Max(1, Mathf.CeilToInt(bounds.width));
            h = Mathf.Max(1, Mathf.CeilToInt(bounds.height));
            free = new bool[w * h];
            field = new int[w * h];
            queue = new int[w * h];
            fieldTarget = -1;
            Physics2D.SyncTransforms();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    free[y * w + x] = !Blocked(CellCenter(y * w + x));
            return true;
        }

        static bool Blocked(Vector2 p)
        {
            int n = Physics2D.OverlapCircleNonAlloc(p + new Vector2(0f, .22f), ProbeRadius, hits);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i];
                if (c == null || c.isTrigger) continue;
                if (c.GetComponentInParent<PlayerController>() != null || c.GetComponentInParent<EnemyController>() != null
                    || c.GetComponentInParent<NpcController>() != null) continue;
                return true;
            }
            return false;
        }

        static int Cell(Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x - bounds.xMin), y = Mathf.FloorToInt(p.y - bounds.yMin);
            return x < 0 || y < 0 || x >= w || y >= h ? -1 : y * w + x;
        }

        static Vector2 CellCenter(int c) => new Vector2(bounds.xMin + c % w + 0.5f, bounds.yMin + c / w + 0.5f);

        static bool LineClear(Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(len / 0.2f);
            for (int i = 1; i < steps; i++)
            {
                // A centreline can cross a free tile while the character's shoulder clips adjacent water.
                Vector2 p=Vector2.Lerp(a,b,i/(float)steps)+new Vector2(0,.22f);
                for(int sy=-1;sy<=1;sy+=2)for(int sx=-1;sx<=1;sx+=2){int c=Cell(p+new Vector2(sx*ProbeRadius,sy*ProbeRadius));if(c<0||!free[c])return false;}
            }
            return true;
        }

        static void BuildField(int target)
        {
            fieldTarget = target;
            for (int i = 0; i < field.Length; i++) field[i] = -1;
            int head = 0, tail = 0;
            field[target] = 0;
            queue[tail++] = target;
            while (head < tail)
            {
                int c = queue[head++];
                int cx = c % w, cy = c / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (field[n] >= 0 || !free[n]) continue;
                    if (k >= 4 && (!free[cy * w + nx] || !free[ny * w + cx])) continue;
                    field[n] = field[c] + 1;
                    queue[tail++] = n;
                }
            }
        }
    }
}
