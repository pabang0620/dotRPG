using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        readonly HashSet<string> propLifecycleSamples = new HashSet<string>();
        static bool VerifyProps => Array.IndexOf(Environment.GetCommandLineArgs(), "-propsVerify") >= 0;
        static bool VerifyForestFields => Array.IndexOf(Environment.GetCommandLineArgs(), "-forestFields") >= 0;
        static bool ForestFieldMap(string id) => id == "forest" || id == "forest_ruins" || id == "forest_depths" || id == "forest_crossing";

        static UnityEngine.Object[] PropOwnedResources(WorldBuilder world)
            => world == null ? Array.Empty<UnityEngine.Object>() : world.GetComponentsInChildren<BiomePropScene>(true)
                .SelectMany(s => s.OwnedResources).Where(r => r != null).Distinct().ToArray();

        object SurfaceHiddenNatureAudit(string id)
        {
            var records = Game.World.SurfaceHiddenUnsupportedNature.ToArray();
            bool forest = id == "forest" || id == "forest_ruins" || id == "forest_depths" || id == "forest_crossing";
            var ground = typeof(WorldBuilder).GetMethod("GroundAt", BindingFlags.Instance | BindingFlags.NonPublic);
            bool valid = records.Length == Game.World.SurfaceHiddenUnsupportedNatureCount
                && records.Select(r => r.Renderer).Distinct().Count() == records.Length
                && (records.Length == 0 || forest && ground != null);
            var rows = new List<object>();
            foreach (var record in records)
            {
                var sr = record.Renderer;
                if (sr == null) { valid = false; continue; }
                var t = sr.transform; var foot = Vector2Int.FloorToInt(t.position);
                char actualGround = ground == null ? '\0' : (char)ground.Invoke(Game.World, new object[] { foot.x, foot.y });
                valid &= !sr.enabled && t.parent == Game.World.ObjectsRoot
                    && (sr.name == "EdgeTree" || sr.name == "Forest edge understory")
                    && foot == record.FootCell && record.Ground == '%' && actualGround == '%'
                    && Vector3.Distance(t.position, record.OriginalPosition) < .0001f
                    && Quaternion.Angle(t.rotation, record.OriginalRotation) < .001f
                    && Vector3.Distance(t.localScale, record.OriginalScale) < .0001f
                    && sr.GetComponentsInChildren<Collider2D>(true).Length == 0
                    && sr.GetComponentsInChildren<ResourceNode>(true).Length == 0
                    && sr.GetComponentsInChildren<NpcController>(true).Length == 0
                    && sr.GetComponentsInChildren<ServiceDoor>(true).Length == 0
                    && sr.GetComponentsInChildren<MapPortal>(true).Length == 0;
                rows.Add(HRow(("name", sr.name), ("position", SurfacePoint(t.position)),
                    ("cell", SurfacePoint(foot)), ("ground", actualGround.ToString())));
            }
            DCheck(id + " hidden forest duplicates are exact nonphysical blocked-cell decorations", valid);
            return HRow(("count", records.Length), ("scopeAndTransformsPreserved", valid), ("records", rows));
        }

        object PropAudit(string id)
        {
            var scenes = Game.World.GetComponentsInChildren<BiomePropScene>(true);
            var composition = Game.World.GetComponentInChildren<SurfaceWorldScene>();
            bool underground = MapRegistry.Get(id)?.worldLayer == WorldLayer.Underground;
            if (VerifyProps || VerifyForestFields)
                DCheck(id + " prop pass scope", underground ? scenes.Length == 0 : composition != null && scenes.Length == 1);
            if (scenes.Length == 0)
                return HRow(("applied", 0), ("undergroundExcluded", underground), ("scenes", 0));

            var excluded = new HashSet<SpriteRenderer>();
            foreach (var surface in Game.World.GetComponentsInChildren<SurfaceWorldScene>())
                foreach (var r in surface.Layers) if (r != null) excluded.Add(r);
            foreach (var cave in Game.World.GetComponentsInChildren<UnderworldCompositionScene>())
                foreach (var r in cave.Layers.Concat(cave.ForegroundRenderers)) if (r != null) excluded.Add(r);
            var rows = new List<object>();
            foreach (var scene in scenes)
            {
                var records = scene.Records.ToArray();
                bool field = ForestFieldMap(id);
                string expectedAtlas = field && VerifyForestFields ? "forest_fields" : scene.Biome;
                bool atlasScope = field ? scene.AtlasId == "forest_fields" || !VerifyForestFields && scene.AtlasId == scene.Biome
                    : scene.AtlasId == expectedAtlas;
                atlasScope &= scene.AtlasId != "forest_fields" || scene.Biome == "forest" && field;
                string expectedPath = Path.Combine(Application.streamingAssetsPath, "WorldProps", scene.AtlasId + ".png");
                bool atlasPath = !string.IsNullOrEmpty(scene.AtlasSourcePath) && File.Exists(scene.AtlasSourcePath)
                    && string.Equals(Path.GetFullPath(expectedPath), Path.GetFullPath(scene.AtlasSourcePath), StringComparison.OrdinalIgnoreCase);
                DCheck(id + " forest field atlas stays inside the four target maps", atlasScope && atlasPath);
                if (field && VerifyForestFields)
                {
                    var metadata = JsonUtility.FromJson<BiomePropArt.Metadata>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "WorldProps", "metadata.json")));
                    var declaration = metadata.atlases.FirstOrDefault(a => a.id == "forest_fields");
                    var slots = new HashSet<string>(new[] { "broadleaf", "oak", "pine", "willow", "broadleaf_open", "oak_lean", "pine_open", "dead",
                        "rock", "bush", "log", "stump", "ruin", "dead_bent", "rock_slab", "fern" });
                    DCheck(id + " field atlas declares sixteen fitted variants", declaration != null && declaration.columns == 4 && declaration.rows == 4
                        && declaration.cells.Length == 16 && slots.SetEquals(declaration.cells.Select(c => c.id)));
                    DCheck(id + " field props keep willow and open layered-pine identities", records.All(r => slots.Contains(r.Slot)
                        && (r.OriginalSprite.name.IndexOf("willow", StringComparison.OrdinalIgnoreCase) < 0 || r.Slot == "willow")
                        && (r.OriginalSprite.name.IndexOf("pine_layered", StringComparison.OrdinalIgnoreCase) < 0 || r.Slot == "pine_open")));
                }
                var owned = new HashSet<UnityEngine.Object>(scene.OwnedResources);
                bool count = scene.MapId == id && records.Length == scene.AppliedCount
                    && scene.EligibleCount == scene.AppliedCount + scene.SkippedCount && scene.SkippedCount == 0
                    && scene.ResourceCount == records.Count(r => r.IsResource)
                    && scene.StaticCount == records.Count(r => !r.IsResource)
                    && scene.SpeciesCount == records.Select(r => r.Slot).Distinct().Count();
                bool transforms = records.All(r => r.Root != null && r.Renderer != null
                    && Vector3.Distance(r.Root.position, r.OriginalRootPosition) < .0001f
                    && Quaternion.Angle(r.Root.rotation, r.OriginalRootRotation) < .001f
                    && Vector3.Distance(r.Root.localScale, r.OriginalRootScale) < .0001f
                    && Vector3.Distance(r.Renderer.transform.localPosition, r.OriginalVisualPosition) < .0001f
                    && Quaternion.Angle(r.Renderer.transform.localRotation, r.OriginalVisualRotation) < .001f
                    && Vector3.Distance(r.Renderer.transform.localScale, r.OriginalVisualScale) < .0001f);
                bool collisions = records.All(r => r.Root != null && r.CollidersUnchanged);
                bool order = records.All(r => r.Renderer != null && r.Renderer.sortingOrder == r.ExpectedSortingOrder
                    && r.Renderer.sortingLayerID == r.OriginalSortingLayer
                    && (r.IsFloorDecoration
                        ? scene.Biome == "sanctum" && r.Root != null && r.Root.name == "rubble"
                            && !r.IsResource && r.Slot == "rubble" && r.ExpectedSortingOrder == -29500
                        : r.ExpectedSortingOrder == r.OriginalSortingOrder));
                // TreeFade may legitimately change alpha while the player walks behind a tree.
                bool color = records.All(r => r.Renderer != null
                    && Mathf.Abs(r.Renderer.color.r - r.OriginalColor.r) < .0001f
                    && Mathf.Abs(r.Renderer.color.g - r.OriginalColor.g) < .0001f
                    && Mathf.Abs(r.Renderer.color.b - r.OriginalColor.b) < .0001f);
                bool pictures = records.All(r => r.Renderer != null && r.ReplacementSprite != null
                    && r.Renderer.sprite == r.ReplacementSprite && r.ReplacementSprite.texture == scene.AtlasTexture
                    && r.ReplacementSprite.name == "biome_prop_" + scene.AtlasId + "_" + r.Slot
                    && r.WidthRatio > 0 && r.WidthRatio <= 1.1501f && r.HeightRatio > 0 && r.HeightRatio <= 1.0001f);
                bool ownership = scene.AtlasTexture != null && scene.AtlasTexture.filterMode == FilterMode.Point
                    && owned.Contains(scene.AtlasTexture) && owned.OfType<Texture2D>().Count() == 1
                    && owned.Count == scene.OwnedResourceCount
                    && records.All(r => owned.Contains(r.ReplacementSprite) && !owned.Contains(r.OriginalSprite)
                        && r.OriginalSprite != null && r.OriginalRenderedSprite != null);
                bool resource = records.Where(r => r.IsResource).All(r =>
                {
                    var node = r.Root.GetComponent<ResourceNode>();
                    return node != null && node.HasVisualOverride && node.Available
                        && node.VisualRenderer == r.Renderer && node.FullVisualSprite == r.ReplacementSprite
                        && (node.Kind != ResourceKind.Tree || node.DepletedVisualSprite != null && owned.Contains(node.DepletedVisualSprite));
                });
                bool preserved = records.All(r => !excluded.Contains(r.Renderer)
                    && r.Root.GetComponent<MapPortal>() == null && r.Root.GetComponent<NpcController>() == null
                    && r.Root.GetComponent<ServiceDoor>() == null
                    && r.Renderer.GetComponentInParent<PlayerController>() == null
                    && r.Renderer.GetComponentInParent<EnemyController>() == null);
                DCheck(id + " complete prop replacement census", count);
                DCheck(id + " prop root and visual transforms preserved", transforms);
                DCheck(id + " prop colliders preserved", collisions);
                DCheck(id + " prop sorting follows exact floor-rubble exception and preserves RGB", order && color);
                DCheck(id + " fitted prop pictures use one owned atlas", pictures && ownership);
                DCheck(id + " resource full and stump overrides connected", resource);
                DCheck(id + " background, columns, actors and services excluded", preserved);
                rows.Add(HRow(("biome", scene.Biome), ("eligible", scene.EligibleCount), ("applied", scene.AppliedCount),
                    ("atlasId", scene.AtlasId), ("atlasSourcePath", scene.AtlasSourcePath), ("atlasSourceSha256", SurfaceFileHash(scene.AtlasSourcePath)),
                    ("skipped", scene.SkippedCount), ("resources", scene.ResourceCount), ("staticProps", scene.StaticCount),
                    ("species", scene.SpeciesCount), ("ownedResources", scene.OwnedResourceCount),
                    ("atlasWidth", scene.AtlasTexture == null ? 0 : scene.AtlasTexture.width),
                    ("atlasHeight", scene.AtlasTexture == null ? 0 : scene.AtlasTexture.height),
                    ("transformsPreserved", transforms), ("collidersPreserved", collisions), ("sortingMatchesPolicy", order),
                    ("floorRubbleCount", records.Count(r => r.IsFloorDecoration)),
                    ("slots", records.GroupBy(r => r.Slot).OrderBy(g => g.Key)
                        .Select(g => (object)HRow(("slot", g.Key), ("count", g.Count()))).ToArray())));
            }
            return HRow(("scenes", scenes.Length), ("applied", scenes.Sum(s => s.AppliedCount)), ("details", rows));
        }

        // Isolated QA only: quiet depletion grants no loot and contacts no server. A short
        // temporary timer exercises the real respawn coroutine; all touched state is restored.
        IEnumerator PropResourceExercise(Action<object> report)
        {
            var scene = Game.World.GetComponentInChildren<BiomePropScene>();
            var grounding = Game.World.GetComponentInChildren<SurfacePropGrounding>();
            var contacts = grounding != null ? grounding.ResourceContacts.ToArray() : Array.Empty<SurfaceResourceContact>();
            var results = new List<object>();
            if (scene == null) { report(HRow(("tested", false))); yield break; }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo Field(string name) => typeof(ResourceNode).GetField(name, flags);
            var timer = Field("respawnSeconds");
            var health = Field("health");
            var depleted = Field("depleted");
            foreach (var kind in new[] { ResourceKind.Tree, ResourceKind.Rock })
            {
                string key = scene.AtlasId + ":" + kind;
                if (propLifecycleSamples.Contains(key)) continue;
                var record = scene.Records.Where(r => r.IsResource && r.Root != null
                    && r.Root.GetComponent<ResourceNode>().Kind == kind && r.Root.GetComponent<ResourceNode>().Available
                    && Vector2.Distance(r.Root.position, Game.Player.Position) > 2)
                    .OrderByDescending(r => contacts.Any(c => c.Node == r.Root.GetComponent<ResourceNode>())).FirstOrDefault();
                if (record == null) continue;
                var node = record.Root.GetComponent<ResourceNode>();
                var contact = contacts.FirstOrDefault(c => c.Node == node);
                var sr = node.VisualRenderer;
                var circle = node.GetComponent<CircleCollider2D>();
                bool access = timer != null && health != null && depleted != null && circle != null;
                DCheck(key + " resource lifecycle access", access);
                if (!access) continue;
                propLifecycleSamples.Add(key);
                float originalTimer = (float)timer.GetValue(node);
                int originalHealth = (int)health.GetValue(node);
                var originalSprite = sr.sprite; bool originalEnabled = sr.enabled;
                var originalPosition = sr.transform.localPosition; var originalScale = sr.transform.localScale;
                bool circleEnabled = circle.enabled; float radius = circle.radius; var offset = circle.offset;
                var configuration = new[] { "maxHealth", "dropItem", "dropAmount", "fullSpriteKey", "stumpKey", "fxHeight" }
                    .ToDictionary(n => n, n => Field(n).GetValue(node));
                bool depletedOk = false, restored = false, unchanged = false;
                bool depletedContact = false, restoredContact = false;
                try
                {
                    timer.SetValue(node, .08f);
                    node.StartRegrowing();
                    depletedOk = !node.Available && (kind == ResourceKind.Tree
                        ? sr.enabled && sr.sprite == node.DepletedVisualSprite && Mathf.Approximately(circle.radius, .35f)
                        : !sr.enabled && !circle.enabled);
                    DCheck(key + " quiet depleted art state", depletedOk);
                    if (contact != null)
                    {
                        contact.SyncVisual();
                        depletedContact = kind == ResourceKind.Tree
                            ? contact.Renderer.enabled && contact.DepletedSprite != null && contact.Renderer.sprite == contact.DepletedSprite
                            : !contact.Renderer.enabled && contact.DepletedSprite == null;
                        DCheck(key + " depleted contact matches stump or absent rock", depletedContact);
                    }
                    float deadline = Time.realtimeSinceStartup + 3f;
                    while ((!node.Available || Vector3.Distance(sr.transform.localScale, Vector3.one) > .0001f)
                        && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return null;
                    restored = node.Available && sr.enabled && sr.sprite == node.FullVisualSprite
                        && sr.sprite == record.ReplacementSprite && record.CollidersUnchanged
                        && Vector3.Distance(sr.transform.localScale, originalScale) < .0001f
                        && (int)health.GetValue(node) == (int)Field("maxHealth").GetValue(node);
                    unchanged = configuration.All(p => Equals(Field(p.Key).GetValue(node), p.Value));
                    DCheck(key + " real respawn restores new full art and collider", restored);
                    DCheck(key + " yield and health configuration preserved", unchanged);
                    if (contact != null)
                    {
                        contact.SyncVisual();
                        restoredContact = contact.Renderer.enabled && contact.StandingSprite != null
                            && contact.Renderer.sprite == contact.StandingSprite
                            && contact.GetComponentsInChildren<Collider2D>(true).Length == 0;
                        DCheck(key + " respawn restores standing contact without collider", restoredContact);
                    }
                }
                finally
                {
                    // No coroutine is stopped on success; on failure discard this QA probe's
                    // coroutine before putting the exact original isolated test state back.
                    if (!restored) node.StopAllCoroutines();
                    timer.SetValue(node, originalTimer); health.SetValue(node, originalHealth); depleted.SetValue(node, false);
                    sr.sprite = originalSprite; sr.enabled = originalEnabled;
                    sr.transform.localPosition = originalPosition; sr.transform.localScale = originalScale;
                    circle.radius = radius; circle.offset = offset; circle.enabled = circleEnabled;
                    node.GetComponent<YSort>()?.Refresh(); Physics2D.SyncTransforms();
                    if (contact != null) contact.SyncVisual();
                }
                DCheck(key + " lifecycle probe restores timer and collision", (float)timer.GetValue(node) == originalTimer && record.CollidersUnchanged);
                results.Add(HRow(("kind", kind.ToString()), ("node", node.NodeId), ("depletedArt", depletedOk),
                    ("respawnArtAndCollider", restored), ("configurationPreserved", unchanged),
                    ("contactTested", contact != null), ("depletedContact", depletedContact), ("respawnContact", restoredContact)));
            }
            report(HRow(("tested", results.Count > 0), ("biome", scene.Biome), ("atlasId", scene.AtlasId), ("samples", results)));
        }

        object PropUndergroundSource(string id, UnderworldCompositionScene scene, object audit)
        {
            var portals = WorldRoutes.Neighbors(id).OrderBy(s => s).Select(target =>
            {
                bool exists = Game.World.PortalTowards(target, out var center);
                var arrival = Game.World.ArrivalFrom(target, out var facing);
                return (object)HRow(("target", target), ("exists", exists), ("center", SurfacePoint(center)),
                    ("arrival", SurfacePoint(arrival)), ("facing", facing.ToString()));
            }).ToArray();
            return HRow(("id", id), ("sourcePath", scene.SourcePath), ("sourceSha256", SurfaceFileHash(scene.SourcePath)),
                ("layoutSha256", SurfaceHash(Encoding.UTF8.GetBytes(HuntingGrounds.Layout(id)))),
                ("bounds", SurfaceRect(Game.World.Bounds)), ("spawn", SurfacePoint(Game.World.PlayerSpawn)),
                ("portals", portals), ("propIntegration", audit),
                ("foregroundColumns", scene.ForegroundRenderers.Select(r => (object)HRow(("name", r.name),
                    ("sprite", r.sprite.name), ("position", SurfacePoint(r.transform.position)), ("order", r.sortingOrder))).ToArray()));
        }
    }
}
