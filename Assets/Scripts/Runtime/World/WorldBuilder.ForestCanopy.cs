using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        // Only the four persistent forest fields opt in. Towns, dungeons, resource
        // nodes, map text and collision painting retain their original behavior.
        bool UsesForestCanopyComposition => ForestMap && map.worldLayer == WorldLayer.Surface
            && !map.instanced && HuntingGrounds.Get(MapId) != null;

        struct CanopySite
        {
            public Vector2 foot;
            public int hash, depth;
            public float grove, priority;
        }

        struct CanopyTree
        {
            public Vector2 foot;
            public float radius;
            public string species;
        }

        static readonly string[] CanopySpecies = { "broadleaf", "oak_round", "pine_layered", "pine", "willow" };

        static uint CanopyHash(int x, int y, uint seed)
        {
            unchecked
            {
                uint v = seed ^ (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu;
                v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu;
                return v ^ (v >> 16);
            }
        }

        static uint CanopySeed(string id)
        {
            uint result = 2166136261u;
            unchecked { foreach (char c in id) result = (result ^ c) * 16777619u; }
            return result;
        }

        static float CanopyNoise(float x, float y, uint seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float H(int xx, int yy) => (CanopyHash(xx, yy, seed) & 65535u) / 65535f;
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx),
                Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        static bool CanopyFloor(char c) => c != '\0' && c != '%' && c != 'W' && c != '~' && c != 'V';

        int CanopyDepth(int x, int y)
        {
            for (int distance = 1; distance <= 4; distance++)
                for (int dy = -distance; dy <= distance; dy++) for (int dx = -distance; dx <= distance; dx++)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == distance && CanopyFloor(At(x + dx, y + dy))) return distance;
            return 5;
        }

        bool CanopyNearWater(Vector2 foot)
        {
            int x = Mathf.FloorToInt(foot.x), y = Mathf.FloorToInt(foot.y);
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                if (dx * dx + dy * dy <= 20 && At(x + dx, y + dy) == '~') return true;
            return false;
        }

        string CanopySpeciesAt(Vector2 foot, uint seed, int hash)
        {
            int grove = (int)(CanopyHash(Mathf.FloorToInt(foot.x / 9f), Mathf.FloorToInt(foot.y / 8f), seed + 91u) % 4u);
            int roll = hash % 100;
            if (CanopyNearWater(foot) && roll < (MapId == "forest_depths" ? 48 : 23)) return "willow";
            if (MapId == "forest_crossing" && roll < 50) return grove % 2 == 0 ? "pine" : "pine_layered";
            // A grove has one dominant silhouette with occasional companion species,
            // rather than changing species on every other tile.
            int kind = roll < 62 ? grove : (grove + 1 + (hash / 101) % 3) % 4;
            return CanopySpecies[kind];
        }

        // Keep related species in a grove, but avoid the very noticeable same-sprite
        // pair stacked along one screen column. Only the picture changes for solid trees.
        string CanopyCompanion(string preferred, Vector2 foot, List<CanopyTree> trees, int hash)
        {
            foreach (var tree in trees)
            {
                if (tree.species != preferred || Mathf.Abs(tree.foot.x - foot.x) > 2.2f
                    || Mathf.Abs(tree.foot.y - foot.y) > 5.1f) continue;
                if (preferred == "pine" || preferred == "pine_layered") return "oak_round";
                return hash % 2 == 0 ? "pine" : "pine_layered";
            }
            return preferred;
        }

        bool CanopyAtEntrance(Vector2 foot)
        {
            int x = Mathf.FloorToInt(foot.x), y = Mathf.FloorToInt(foot.y);
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                if (dx * dx + dy * dy <= 17 && WorldRoutes.Portal(At(x + dx, y + dy))) return true;
            return false;
        }

        void BuildForestCanopyComposition()
        {
            uint seed = CanopySeed(MapId);
            var state = objectsRoot.gameObject.AddComponent<ForestCanopyScene>();
            state.MapId = MapId;
            var occupied = new List<CanopyTree>();

            // Existing solid trees keep the exact root transform and BoxCollider2D.
            // A scaled Sprite view adjusts only its picture, never physics or harvest state.
            foreach (Transform child in objectsRoot)
            {
                if (child.name != "Tree" || child.GetComponent<ResourceNode>() != null) continue;
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;
                if (sr.sprite.name.StartsWith("town_dead_", System.StringComparison.Ordinal))
                {
                    var replacement = state.DeadOak(sr.sprite);
                    if (replacement != null) { sr.sprite = replacement; state.DeadTreeViews++; }
                    continue;
                }
                if (System.Array.IndexOf(CanopySpecies, sr.sprite.name) < 0) continue;
                Vector2 foot = child.position;
                int hash = (int)(CanopyHash(Mathf.RoundToInt(foot.x * 32), Mathf.RoundToInt(foot.y * 32), seed) & 0x7fffffffu);
                string species = CanopyCompanion(CanopySpeciesAt(foot, seed, hash), foot, occupied, hash);
                float scale = .84f + (hash / 37 % 5) * .04f;
                var source = Game.Art.Get(VillageNatureArt.Prefix + species);
                sr.sprite = state.Scaled(source, scale);
                sr.color = new Color(.86f + (hash % 4) * .025f, .94f, .88f + (hash % 3) * .025f, 1);
                state.StaticTreeViews++;
                state.RegisterSpecies(species);
                occupied.Add(new CanopyTree { foot = foot, radius = sr.sprite.bounds.size.x * .34f, species = species });
            }

            var sites = new List<CanopySite>();
            for (int y = 1; y < height - 1; y++) for (int x = 1; x < width - 1; x++)
            {
                if (At(x, y) != '%') continue;
                int depth = CanopyDepth(x, y); if (depth > 4) continue;
                float grove = CanopyNoise(x / 7.5f, y / 6.5f, seed + 17u);
                // Broad, irregular breathing spaces expose the continuous background.
                if (grove < .37f) continue;
                int hash = (int)(CanopyHash(x, y, seed + 37u) & 0x7fffffffu);
                if (depth >= 3 && hash % 100 > 27) continue;
                float jx = ((hash >> 5) % 17 - 8) / 32f;
                float jy = ((hash >> 12) % 13 - 6) / 32f;
                var foot = new Vector2(x + .5f + jx, y + .5f + jy);
                if (CanopyAtEntrance(foot)) { state.EntranceSitesSkipped++; continue; }
                // The trunk and a small surrounding contact footprint are fully blocked.
                if (At(Mathf.FloorToInt(foot.x - .22f), Mathf.FloorToInt(foot.y - .22f)) != '%'
                    || At(Mathf.FloorToInt(foot.x + .22f), Mathf.FloorToInt(foot.y + .22f)) != '%') continue;
                sites.Add(new CanopySite { foot = foot, hash = hash, depth = depth, grove = grove,
                    priority = (depth >= 3 ? 2 : 0) + (hash % 65521) / 65521f });
            }
            sites.Sort((a, b) => a.priority.CompareTo(b.priority));
            foreach (var site in sites)
            {
                string species = CanopyCompanion(CanopySpeciesAt(site.foot, seed, site.hash), site.foot, occupied, site.hash);
                var source = Game.Art.Get(VillageNatureArt.Prefix + species);
                bool rear = site.depth >= 3;
                float scale = (rear ? .66f : .79f) + (site.hash / 37 % 5) * .04f;
                float radius = source.bounds.size.x * scale * .39f + (site.grove < .52f ? .45f : 0);
                bool crowded = false;
                foreach (var tree in occupied)
                    if ((tree.foot - site.foot).sqrMagnitude < (tree.radius + radius) * (tree.radius + radius)) { crowded = true; break; }
                if (crowded) continue;
                var go = StaticProp("EdgeTree", VillageNatureArt.Prefix + species, site.foot, Vector2.zero, Vector2.zero);
                var sr = go.GetComponent<SpriteRenderer>();
                sr.sprite = state.Scaled(source, scale);
                float light = (rear ? .61f : .84f) + (site.hash % 4) * .025f;
                sr.color = new Color(light * .94f, light, Mathf.Min(1, light + (rear ? .045f : .005f)), 1);
                occupied.Add(new CanopyTree { foot = site.foot, radius = radius, species = species });
                state.EdgeTrees++; if (rear) state.RearTrees++;
                state.RegisterSpecies(species);
            }

            // A handful of low shrubs at the same blocked edge soften isolated trunks.
            // They do not form another continuous row or encroach on the battle floor.
            int shrubs = 0;
            foreach (var site in sites)
            {
                if (shrubs >= 22 || site.depth != 1 || site.hash % 17 != 0) continue;
                bool nearTrunk = false;
                foreach (var tree in occupied) if (Vector2.Distance(tree.foot, site.foot) < .8f) { nearTrunk = true; break; }
                if (nearTrunk) continue;
                var go = StaticProp("Forest edge understory", VillageNatureArt.Prefix + "bush", site.foot, Vector2.zero, Vector2.zero);
                go.transform.localScale = Vector3.one * (.64f + (site.hash % 3) * .12f);
                go.GetComponent<SpriteRenderer>().color = new Color(.68f, .79f, .73f, 1);
                shrubs++;
            }
            state.Understory = shrubs;
        }
    }

    /// <summary>Owns sprite views and optional dead-oak texture; shared art and gameplay stay untouched.</summary>
    public sealed class ForestCanopyScene : MonoBehaviour
    {
        public string MapId { get; internal set; }
        public int EdgeTrees { get; internal set; }
        public int RearTrees { get; internal set; }
        public int StaticTreeViews { get; internal set; }
        public int Understory { get; internal set; }
        public int EntranceSitesSkipped { get; internal set; }
        public int DeadTreeViews { get; internal set; }
        readonly Dictionary<string, Sprite> views = new Dictionary<string, Sprite>();
        readonly HashSet<string> species = new HashSet<string>();
        Texture2D deadOak;
        Rect deadOakRect;
        bool deadOakChecked;
        public int SpeciesCount => species.Count;
        internal void RegisterSpecies(string value) => species.Add(value);
        public UnityEngine.Object[] OwnedResources
        {
            get
            {
                var resources = new List<UnityEngine.Object>();
                foreach (var view in views.Values) resources.Add(view);
                if (deadOak != null) resources.Add(deadOak);
                return resources.ToArray();
            }
        }

        internal Sprite DeadOak(Sprite original)
        {
            string key = "dead-oak:" + original.name;
            if (views.TryGetValue(key, out var found)) return found;
            if (!deadOakChecked)
            {
                deadOakChecked = true;
                string path = Path.Combine(Application.streamingAssetsPath, "SurfaceWorld", "Props", "dead_oak.png");
                if (!StreamingFiles.Exists(path)) return null;
                try
                {
                    if (StreamingFiles.Length(path) > 16 * 1024 * 1024) throw new InvalidDataException("Dead oak source is oversized");
                    deadOak = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    { name = "Forest dead oak", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    if (!ImageConversion.LoadImage(deadOak, StreamingFiles.ReadAllBytes(path), false)
                        || deadOak.width > 2048 || deadOak.height > 2048) throw new InvalidDataException("Invalid dead oak PNG");
                    var pixels = deadOak.GetPixels32();
                    int left = deadOak.width, right = -1, bottom = deadOak.height, top = -1, clear = 0;
                    for (int y = 0; y < deadOak.height; y++) for (int x = 0; x < deadOak.width; x++)
                    {
                        if (pixels[y * deadOak.width + x].a < 8) { clear++; continue; }
                        left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                        bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                    }
                    if (right <= left || top <= bottom || clear < pixels.Length / 10)
                        throw new InvalidDataException("Dead oak requires a transparent silhouette");
                    deadOakRect = new Rect(left, bottom, right - left + 1, top - bottom + 1);
                    // Keep this one map-local texture readable for TreeFade's pixel coverage test.
                }
                catch (System.Exception error)
                {
                    if (deadOak != null) Destroy(deadOak); deadOak = null;
                    Debug.LogWarning("Forest dead oak unavailable; keep original tree. " + error.Message);
                }
            }
            if (deadOak == null) return null;
            // Same world height and normalized feet pivot as the replaced sprite. No
            // root transform or BoxCollider2D changes, even if the new art has wider roots.
            Vector2 pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
            float ppu = deadOakRect.height / original.bounds.size.y;
            var sprite = Sprite.Create(deadOak, deadOakRect, pivot, ppu, 0, SpriteMeshType.FullRect);
            sprite.name = "surface_dead_oak"; views.Add(key, sprite); return sprite;
        }

        internal Sprite Scaled(Sprite source, float scale)
        {
            int step = Mathf.RoundToInt(scale * 32); string key = source.name + ":" + step;
            if (views.TryGetValue(key, out var found)) return found;
            Vector2 pivot = new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
            var view = Sprite.Create(source.texture, source.rect, pivot, source.pixelsPerUnit / (step / 32f), 0, SpriteMeshType.FullRect);
            view.name = source.name; // Existing generated-art material/fade rules still recognize it.
            views.Add(key, view); return view;
        }

        void OnDestroy()
        {
            foreach (var sprite in views.Values) if (sprite != null) Destroy(sprite);
            views.Clear();
            if (deadOak != null) Destroy(deadOak);
        }
    }
}
