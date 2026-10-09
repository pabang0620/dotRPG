using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Map-owned views of authored nature atlases. No gameplay sprite cache is replaced.</summary>
    public static class BiomePropArt
    {
        [Serializable] public sealed class Metadata { public Atlas[] atlases; }
        [Serializable] public sealed class Atlas
        {
            public string id, file;
            public int columns, rows;
            public Cell[] cells;
        }
        [Serializable] public sealed class Cell
        {
            public string id;
            // Rect uses PNG top-left coordinates; pivot uses crop-local bottom-up coordinates.
            public int[] rect;
            public float[] pivot;
        }

        public static BiomePropScene Attach(GameObject owner, string mapId, string biome, string atlasId = null)
        {
            var existing = owner.GetComponent<BiomePropScene>();
            if (existing != null) return existing;
            if (string.IsNullOrEmpty(atlasId)) atlasId = biome;
            string folder = Path.Combine(Application.streamingAssetsPath, "WorldProps");
            string metadataPath = Path.Combine(folder, "metadata.json");
            if (!File.Exists(metadataPath)) return null;
            Texture2D texture = null;
            try
            {
                if (new FileInfo(metadataPath).Length > 256 * 1024) throw new InvalidDataException("Oversized prop metadata");
                var metadata = JsonUtility.FromJson<Metadata>(File.ReadAllText(metadataPath));
                Atlas atlas = null;
                if (metadata?.atlases != null)
                    foreach (var entry in metadata.atlases) if (entry.id == atlasId) { atlas = entry; break; }
                if (atlas == null) return null;
                long cellCount = (long)atlas.columns * atlas.rows;
                if (atlas.columns <= 0 || atlas.rows <= 0 || cellCount > 64 || atlas.cells == null || atlas.cells.Length != cellCount
                    || atlas.file != atlasId + ".png") throw new InvalidDataException("Invalid prop atlas declaration: " + atlasId);
                string file = Path.Combine(folder, atlas.file);
                if (!File.Exists(file)) return null;
                if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException("Oversized prop atlas: " + atlasId);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = "Biome props " + atlasId, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(file), false)
                    || texture.width > 4096 || texture.height > 4096)
                    throw new InvalidDataException("Invalid prop PNG: " + atlasId);
                texture.filterMode = FilterMode.Point;
                var names = new HashSet<string>();
                foreach (var cell in atlas.cells)
                {
                    if (cell == null || string.IsNullOrEmpty(cell.id) || !names.Add(cell.id)
                        || cell.rect == null || cell.rect.Length != 4 || cell.pivot == null || cell.pivot.Length != 2)
                        throw new InvalidDataException("Invalid prop cell: " + atlasId);
                    int[] r = cell.rect;
                    if (r[0] < 0 || r[1] < 0 || r[2] <= 0 || r[3] <= 0
                        || (long)r[0] + r[2] > texture.width || (long)r[1] + r[3] > texture.height
                        || float.IsNaN(cell.pivot[0]) || float.IsNaN(cell.pivot[1])
                        || cell.pivot[0] < 0 || cell.pivot[0] > 1 || cell.pivot[1] < 0 || cell.pivot[1] > 1)
                        throw new InvalidDataException("Prop crop or foot outside atlas: " + atlasId + "/" + cell.id);
                }
                var scene = owner.AddComponent<BiomePropScene>();
                scene.Initialize(mapId, biome, texture, atlas, file);
                texture = null; // ownership passes to the map scene
                return scene;
            }
            catch (Exception error)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                Debug.LogWarning("Biome props unavailable; keep original art. " + error.Message);
                return null;
            }
        }
    }

    /// <summary>Audit data captures physical objects before their picture is replaced.</summary>
    public sealed class BiomePropRecord
    {
        public SpriteRenderer Renderer { get; }
        public Transform Root { get; }
        public Sprite OriginalSprite { get; }
        public Sprite OriginalRenderedSprite { get; }
        public Sprite ReplacementSprite { get; }
        public string Slot { get; }
        public bool IsResource { get; }
        public bool IsFloorDecoration { get; }
        public Vector3 OriginalRootPosition { get; }
        public Quaternion OriginalRootRotation { get; }
        public Vector3 OriginalRootScale { get; }
        public Vector3 OriginalVisualPosition { get; }
        public Quaternion OriginalVisualRotation { get; }
        public Vector3 OriginalVisualScale { get; }
        public int OriginalSortingOrder { get; }
        public int ExpectedSortingOrder { get; }
        public int OriginalSortingLayer { get; }
        public Color OriginalColor { get; }
        public float WidthRatio { get; }
        public float HeightRatio { get; }
        readonly Collider2D[] colliders;
        readonly string colliderSignature;
        public bool CollidersUnchanged => ColliderSignature(Root.GetComponentsInChildren<Collider2D>(true)) == colliderSignature;

        internal BiomePropRecord(SpriteRenderer renderer, Transform root, Sprite original, Sprite replacement, string slot, bool resource, bool floorDecoration = false)
        {
            Renderer = renderer; Root = root; OriginalSprite = original; OriginalRenderedSprite = renderer.sprite;
            ReplacementSprite = replacement; Slot = slot; IsResource = resource;
            OriginalRootPosition = root.position; OriginalRootRotation = root.rotation; OriginalRootScale = root.localScale;
            OriginalVisualPosition = renderer.transform.localPosition;
            OriginalVisualRotation = renderer.transform.localRotation; OriginalVisualScale = renderer.transform.localScale;
            OriginalSortingOrder = renderer.sortingOrder; OriginalSortingLayer = renderer.sortingLayerID; OriginalColor = renderer.color;
            IsFloorDecoration = floorDecoration;
            ExpectedSortingOrder = floorDecoration ? -29500 : OriginalSortingOrder;
            WidthRatio = replacement.bounds.size.x / original.bounds.size.x;
            HeightRatio = replacement.bounds.size.y / original.bounds.size.y;
            colliders = root.GetComponentsInChildren<Collider2D>(true); colliderSignature = ColliderSignature(colliders);
        }

        static string ColliderSignature(Collider2D[] items)
        {
            var s = new StringBuilder();
            void Number(float n) => s.Append(n.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            void Vector(Vector2 v) { Number(v.x); Number(v.y); }
            foreach (var c in items)
            {
                if (c == null) { s.Append("destroyed;"); continue; }
                s.Append(c.GetEntityId().ToString()).Append(':').Append(c.GetType().Name).Append(':')
                    .Append(c.enabled).Append(':').Append(c.isTrigger).Append(':').Append(c.gameObject.activeSelf).Append(':');
                Vector(c.offset); Vector(c.transform.position); Vector(c.transform.lossyScale); Number(c.transform.eulerAngles.z);
                if (c is BoxCollider2D box) { Vector(box.size); Number(box.edgeRadius); }
                else if (c is CircleCollider2D circle) Number(circle.radius);
                else if (c is CapsuleCollider2D capsule) { Vector(capsule.size); s.Append(capsule.direction); }
                else if (c is PolygonCollider2D polygon)
                    for (int p = 0; p < polygon.pathCount; p++) { s.Append('/'); foreach (var v in polygon.GetPath(p)) Vector(v); }
                else if (c is EdgeCollider2D edge) { Number(edge.edgeRadius); foreach (var v in edge.points) Vector(v); }
                s.Append(';');
            }
            return s.ToString();
        }
    }

    /// <summary>One readable atlas plus shared sprite views, destroyed when this map leaves.</summary>
    public sealed class BiomePropScene : MonoBehaviour
    {
        public string MapId { get; private set; }
        public string Biome { get; private set; }
        public string AtlasId { get; private set; }
        public string AtlasSourcePath { get; private set; }
        public int EligibleCount { get; internal set; }
        public int SkippedCount { get; internal set; }
        public int AppliedCount => records.Count;
        public int ResourceCount { get; private set; }
        public int StaticCount => AppliedCount - ResourceCount;
        public int SpeciesCount => species.Count;
        public Texture2D AtlasTexture { get; private set; }
        public IReadOnlyList<BiomePropRecord> Records => records;
        public UnityEngine.Object[] OwnedResources => owned.ToArray();
        public int OwnedResourceCount => owned.Count;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<BiomePropRecord> records = new List<BiomePropRecord>();
        readonly HashSet<string> species = new HashSet<string>();
        readonly Dictionary<string, BiomePropArt.Cell> cells = new Dictionary<string, BiomePropArt.Cell>();
        readonly Dictionary<string, Sprite> views = new Dictionary<string, Sprite>();

        internal void Initialize(string mapId, string biome, Texture2D texture, BiomePropArt.Atlas atlas, string sourcePath)
        {
            MapId = mapId; Biome = biome; AtlasId = atlas.id; AtlasSourcePath = sourcePath; AtlasTexture = texture; owned.Add(texture);
            foreach (var cell in atlas.cells) cells.Add(cell.id, cell);
        }

        internal Sprite View(string slot, Sprite original)
        {
            if (original == null || !cells.TryGetValue(slot, out var cell)) return null;
            string key = slot + ":" + original.GetEntityId();
            if (views.TryGetValue(key, out var cached)) return cached;
            float height = original.bounds.size.y, width = original.bounds.size.x;
            if (height <= .001f || width <= .001f) return null;
            int[] r = cell.rect;
            // Preserve the original height unless the new silhouette would widen a route's
            // visual obstruction. Uniform fit keeps TreeFade's sprite-space sampling exact.
            float ppu = Mathf.Max(r[3] / height, r[2] / (width * 1.15f));
            var rect = new Rect(r[0], AtlasTexture.height - r[1] - r[3], r[2], r[3]);
            var sprite = Sprite.Create(AtlasTexture, rect, new Vector2(cell.pivot[0], cell.pivot[1]), ppu, 0, SpriteMeshType.FullRect);
            sprite.name = "biome_prop_" + AtlasId + "_" + slot;
            views.Add(key, sprite); owned.Add(sprite); return sprite;
        }

        internal void Record(BiomePropRecord record)
        {
            records.Add(record); species.Add(record.Slot);
            if (record.IsResource) ResourceCount++;
        }

        void OnDestroy()
        {
            // SpriteLibrary and ForestCanopyScene own all original art. Only this atlas
            // and its new views belong here; never destroy a replaced shared sprite.
            foreach (var value in owned) if (value != null) Destroy(value);
            owned.Clear(); views.Clear(); records.Clear(); AtlasTexture = null;
        }
    }
}
