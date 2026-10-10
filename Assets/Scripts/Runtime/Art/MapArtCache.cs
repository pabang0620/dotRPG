using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DotRPG
{
    /// <summary>
    /// [MAP ART] Large map artwork (Docs/MAP_ART_LOADING.md) lives in Resources/WorldArt as GPU-compressed textures
    /// instead of PNG files decoded at run time. Map travel waits for <see cref="Prepare"/> behind the fade, so building
    /// the map then takes the texture at once; the maps a portal leads to are requested in the background on arrival.
    /// Only the current, previous, home-town and requested-ahead maps stay loaded (PC 4 neighbours, phones 1); the rest are unloaded.
    /// The textures belong to this cache: map scenes use them but never destroy them.
    /// </summary>
    public static class MapArtCache
    {
        public const string SurfaceFolder = "WorldArt/Surface/", UnderworldFolder = "WorldArt/Underworld/";
        static readonly string[] CompositionMaps = { "hollow_descent", "hollow_roots", "hollow_fungal", "hollow_depths" };
        static readonly string[] CompositionKinds = { "descent", "roots", "fungal", "depths" };
        const float PrepareTimeoutSeconds = 15f;
        static int NeighbourBudget => Application.isMobilePlatform ? 1 : 4;

        static readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, ResourceRequest> pending = new Dictionary<string, ResourceRequest>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static readonly List<string> ahead = new List<string>();
        static string current, previous;

        public static string SurfacePath(string mapId) => SurfaceFolder + mapId;
        public static string CompositionPath(string kind, bool hd) => UnderworldFolder + "composition-" + kind + (hd ? "_hd" : "");
        public static int LoadedCount => loaded.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() { GameEvents.MapEntered -= Entered; GameEvents.MapEntered += Entered; }

        /// <summary>Resource paths a map draws from, preferred first (empty for maps without large artwork).</summary>
        public static IEnumerable<string> PathsFor(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) yield break;
            int c = System.Array.IndexOf(CompositionMaps, mapId);
            if (c >= 0) { yield return CompositionPath(CompositionKinds[c], true); yield break; }
            var info = MapRegistry.Get(mapId);
            if (info != null && info.worldLayer == WorldLayer.Surface && !info.IsInterior && !info.instanced) yield return SurfacePath(mapId);
        }

        /// <summary>Requests a map's artwork and waits until it is loaded (or the timeout). Run behind a fade.</summary>
        public static IEnumerator Prepare(string mapId)
        {
            var paths = new List<string>(PathsFor(mapId));
            if (paths.Count == 0) yield break;
            var watch = Stopwatch.StartNew();
            foreach (var path in paths) Request(path);
            while (watch.Elapsed.TotalSeconds < PrepareTimeoutSeconds)
            {
                bool waiting = false;
                foreach (var path in paths) if (pending.TryGetValue(path, out var r) && !r.isDone) waiting = true;
                if (!waiting) break;
                yield return null;
            }
            foreach (var path in paths) Collect(path);
            Debug.Log($"[MapArt] {mapId} ready in {watch.ElapsedMilliseconds}ms ({loaded.Count} textures loaded)");
        }

        /// <summary>The texture at a resource path: at once when prepared, otherwise loaded synchronously (logged). Null when absent.</summary>
        public static Texture2D Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Collect(path);
            if (loaded.TryGetValue(path, out var texture) && texture != null) return texture;
            if (missing.Contains(path)) return null;
            var watch = Stopwatch.StartNew();
            if (pending.TryGetValue(path, out var request)) { texture = request.asset as Texture2D; pending.Remove(path); }
            else texture = Resources.Load<Texture2D>(path);
            if (texture == null) { missing.Add(path); return null; }
            loaded[path] = texture;
            Debug.Log($"[MapArt] {path} loaded synchronously in {watch.ElapsedMilliseconds}ms");
            return texture;
        }

        static void Request(string path)
        {
            if (loaded.ContainsKey(path) || pending.ContainsKey(path) || missing.Contains(path)) return;
            pending[path] = Resources.LoadAsync<Texture2D>(path);
        }

        static void Collect(string path)
        {
            if (!pending.TryGetValue(path, out var request) || !request.isDone) return;
            pending.Remove(path);
            if (request.asset is Texture2D texture) loaded[path] = texture; else missing.Add(path);
        }

        /// <summary>Arrival on a map: remember it, request the maps its portals lead to and unload everything else.</summary>
        static void Entered(string mapId)
        {
            if (string.IsNullOrEmpty(mapId) || mapId == current) return;
            previous = current;
            current = mapId;
            ahead.Clear();
            var info = MapRegistry.Get(mapId);
            var candidates = new List<string>(WorldRoutes.Neighbors(mapId));
            if (info != null && !string.IsNullOrEmpty(info.exteriorMap)) candidates.Insert(0, info.exteriorMap);
            foreach (var id in candidates)
            {
                if (ahead.Count >= NeighbourBudget) break;
                if (id == previous || ahead.Contains(id)) continue;
                bool hasArt = false;
                foreach (var _ in PathsFor(id)) hasArt = true;
                if (hasArt) ahead.Add(id);
            }
            Trim();
            foreach (var id in ahead) foreach (var path in PathsFor(id)) Request(path);
        }

        static void Trim()
        {
            var keep = new HashSet<string>();
            void Keep(string id)
            {
                foreach (var path in PathsFor(id)) keep.Add(path);
                int c = System.Array.IndexOf(CompositionMaps, id);
                if (c >= 0) keep.Add(CompositionPath(CompositionKinds[c], false)); // fallback picture when the HD one is missing
            }
            Keep(current); Keep(previous);
            // The town a scroll, a defeat or a dungeon exit returns to stays ready (not counted against the neighbour budget).
            if (!string.IsNullOrEmpty(current)) Keep(HuntingGrounds.HomeOf(current));
            foreach (var id in ahead) Keep(id);
            foreach (var path in new List<string>(pending.Keys)) Collect(path);
            foreach (var path in new List<string>(loaded.Keys))
            {
                if (keep.Contains(path)) continue;
                var texture = loaded[path];
                loaded.Remove(path);
                if (texture != null) Resources.UnloadAsset(texture);
            }
        }
    }

    /// <summary>[MAP ART] Rectangles covering one value of a cell mask: equal runs on consecutive rows are merged, so a map layer stays a small mesh.</summary>
    public static class MapArtMesh
    {
        public static List<RectInt> Rects(byte[] mask, int columns, int rows, int value)
        {
            var done = new List<RectInt>();
            var open = new Dictionary<long, RectInt>();
            for (int y = 0; y < rows; y++)
            {
                var next = new Dictionary<long, RectInt>();
                for (int x = 0; x < columns;)
                {
                    if (mask[y * columns + x] != value) { x++; continue; }
                    int start = x;
                    while (x < columns && mask[y * columns + x] == value) x++;
                    long key = ((long)start << 32) | (uint)x;
                    next[key] = open.TryGetValue(key, out var r) ? new RectInt(r.x, r.y, r.width, r.height + 1) : new RectInt(start, y, x - start, 1);
                    open.Remove(key);
                }
                done.AddRange(open.Values);
                open = next;
            }
            done.AddRange(open.Values);
            return done;
        }

        /// <summary>
        /// Sprite over the whole texture whose mesh covers the given cell rectangles. Vertices are texture pixels:
        /// cell * (texture size / cell count) per axis, so a texture scaled down per platform still lines up.
        /// Returns null when no rectangle is given. Throws when the mesh would exceed the sprite index range.
        /// </summary>
        public static Sprite LayerSprite(Texture2D texture, List<RectInt> rects, int columns, int rows, float ppu)
        {
            if (rects.Count == 0) return null;
            if (rects.Count * 4 > ushort.MaxValue) throw new System.IO.InvalidDataException("Map art layer mesh exceeds sprite index capacity");
            int tw = texture.width, th = texture.height;
            float sx = (float)tw / columns, sy = (float)th / rows;
            var vertices = new Vector2[rects.Count * 4];
            var triangles = new ushort[rects.Count * 6];
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                float x0 = Mathf.Min(r.xMin * sx, tw), x1 = Mathf.Min(r.xMax * sx, tw), y0 = Mathf.Min(r.yMin * sy, th), y1 = Mathf.Min(r.yMax * sy, th);
                int v = i * 4;
                vertices[v] = new Vector2(x0, y0); vertices[v + 1] = new Vector2(x1, y0);
                vertices[v + 2] = new Vector2(x1, y1); vertices[v + 3] = new Vector2(x0, y1);
                int t = i * 6;
                triangles[t] = (ushort)v; triangles[t + 1] = (ushort)(v + 1); triangles[t + 2] = (ushort)(v + 2);
                triangles[t + 3] = (ushort)(v + 2); triangles[t + 4] = (ushort)(v + 3); triangles[t + 5] = (ushort)v;
            }
            var sprite = Sprite.Create(texture, new Rect(0, 0, tw, th), Vector2.zero, ppu, 0, SpriteMeshType.FullRect);
            sprite.OverrideGeometry(vertices, triangles);
            return sprite;
        }

        /// <summary>
        /// Height scale that makes a sprite drawn at the horizontal density (ppuX) span exactly the world height:
        /// a texture whose aspect differs slightly from the world (or that a platform resized) keeps cell registration.
        /// </summary>
        public static Vector3 Registration(float ppuX, float ppuY) => new Vector3(1f, ppuX / ppuY, 1f);

        /// <summary>Triangles of a simple polygon (ear clipping), either winding.</summary>
        public static ushort[] Triangulate(IList<Vector2> points)
        {
            int n = points.Count;
            var result = new List<ushort>();
            if (n < 3) return result.ToArray();
            float area = 0;
            for (int i = 0, j = n - 1; i < n; j = i++) area += points[j].x * points[i].y - points[i].x * points[j].y;
            var index = new List<int>();
            for (int i = 0; i < n; i++) index.Add(area > 0 ? i : n - 1 - i);
            int guard = n * n;
            while (index.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < index.Count; i++)
                {
                    int ia = index[(i + index.Count - 1) % index.Count], ib = index[i], ic = index[(i + 1) % index.Count];
                    Vector2 a = points[ia], b = points[ib], c = points[ic];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 0) continue; // reflex corner
                    bool inside = false;
                    for (int k = 0; k < index.Count && !inside; k++)
                    {
                        int ik = index[k];
                        if (ik == ia || ik == ib || ik == ic) continue;
                        inside = InTriangle(points[ik], a, b, c);
                    }
                    if (inside) continue;
                    result.Add((ushort)ia); result.Add((ushort)ib); result.Add((ushort)ic);
                    index.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (index.Count == 3) { result.Add((ushort)index[0]); result.Add((ushort)index[1]); result.Add((ushort)index[2]); }
            return result.ToArray();
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
