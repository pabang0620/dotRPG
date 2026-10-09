using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        // Export only: no layout, collider, portal, service or save data is changed.
        // PNG top is north; JSON cells/ground rows are north-to-south, world origin bottom-left.
        IEnumerator SurfaceWorldRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1f);
            Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "지상 환경 검증");
            yield return Wait(1.5f);
            AudioListener.volume = 0;
            Game.Audio?.SetVolumes(0, 0);
            var surfaceInput = new ScriptedInput();
            Game.Player.Input = surfaceInput;
            Game.Player.Health.SetInvulnerable(3600f);
            dgnPassed = dgnFailed = 0;
            var maps = MapRegistry.All.Where(m => m.worldLayer == WorldLayer.Surface && !m.instanced && !m.IsInterior).ToArray();
            DCheck("surface export has five towns and sixteen fields", maps.Length == 21 && maps.Count(m => MapRegistry.IsTown(m.id)) == 5);
            string only = SurfaceArg("-surfaceOnly");
            if (!string.IsNullOrEmpty(only))
            {
                maps = maps.Where(m => m.id == only).ToArray();
                DCheck("requested surface map exists: " + only, maps.Length == 1);
            }
            bool groundOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-surfaceGroundOnly") >= 0;
            var index = new List<object>();
            string baseline = SurfaceArg("-surfaceBaseline");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cellsField = typeof(WorldBuilder).GetField("cells", flags);
            var groundMethod = typeof(WorldBuilder).GetMethod("GroundAt", flags);
            DCheck("runtime layout export access", cellsField != null && groundMethod != null);
            if (cellsField == null || groundMethod == null) yield break;

            foreach (var map in maps)
            {
                Log("SURFACE EXPORT " + map.id);
                var oldScenes = Game.World.GetComponentsInChildren<SurfaceWorldScene>();
                var oldResources = SurfaceOwnedLayers(oldScenes);
                float loadStart = Time.realtimeSinceStartup;
                Game.World.Load(map.id);
                float loadMilliseconds = (Time.realtimeSinceStartup - loadStart) * 1000f;
                Game.Player.Place(Game.World.PlayerSpawn, Facing.Down);
                Game.Camera.SetTarget(Game.Player.transform, true);
                yield return Wait(.18f);
                yield return null; // OnDestroy may schedule owned texture destruction for the next frame.
                bool oldReleased = oldScenes.All(s => s == null) && oldResources.All(r => r == null);
                DCheck(map.id + " previous composition resources released", oldReleased);
                AudioListener.volume = 0;
                Game.Audio?.SetVolumes(0, 0);
                Physics2D.SyncTransforms();
                var bounds = Game.World.Bounds;
                int w = Mathf.RoundToInt(bounds.width), h = Mathf.RoundToInt(bounds.height);
                var cells = (char[,])cellsField.GetValue(Game.World);
                var ground = new char[w, h];
                var rows = new List<string>();
                var groundRows = new List<string>();
                var camps = new List<object>();
                for (int y = h - 1; y >= 0; y--)
                {
                    var line = new char[w]; var baseLine = new char[w];
                    for (int x = 0; x < w; x++)
                    {
                        line[x] = cells[x, y];
                        baseLine[x] = ground[x, y] = (char)groundMethod.Invoke(Game.World, new object[] { x, y });
                        if (line[x] == 'k') camps.Add(SurfacePoint(new Vector2(x + .5f, y + .5f)));
                    }
                    rows.Add(new string(line)); groundRows.Add(new string(baseLine));
                }
                var portals = new List<object>();
                foreach (string target in WorldRoutes.Neighbors(map.id).OrderBy(s => s, StringComparer.Ordinal))
                {
                    bool exists = Game.World.PortalTowards(target, out var center);
                    var arrival = Game.World.ArrivalFrom(target, out var facing);
                    bool free = Game.World.IsFree(arrival);
                    portals.Add(HRow(("target", target), ("exists", exists), ("center", SurfacePoint(center)),
                        ("arrival", SurfacePoint(arrival)), ("facing", facing.ToString()), ("arrivalFree", free)));
                    DCheck(map.id + " portal " + target, exists);
                    DCheck(map.id + " safe arrival " + target, free);
                }
                var doors = Game.World.ObjectsRoot.GetComponentsInChildren<ServiceDoor>()
                    .OrderBy(d => d.Service.ToString(), StringComparer.Ordinal)
                    .Select(d => (object)HRow(("service", d.Service.ToString()), ("position", SurfacePoint(d.transform.position)),
                        ("interior", MapRegistry.InteriorFor(map.id, d.Service)))).ToList();
                bool spawnFree = Game.World.IsFree(Game.World.PlayerSpawn);
                DCheck(map.id + " spawn free", spawnFree);

                // Keep the exact WorldBuilder feet-circle rule, but synchronize physics once above.
                // Dynamic actors are excluded just as they are in WorldBuilder.IsFree.
                const int px = 4;
                int pw = w * px, ph = h * px;
                var semantic = new Color32[pw * ph];
                var walking = new Color32[pw * ph];
                var walkBytes = new byte[pw * ph];
                var terrainBytes = new byte[pw * ph];
                var hits = new Collider2D[128];
                var filter = new ContactFilter2D { useTriggers = false };
                var waterMap = (UnityEngine.Tilemaps.Tilemap)typeof(WorldBuilder).GetField("waterMap", flags).GetValue(Game.World);
                var cliffMap = (UnityEngine.Tilemaps.Tilemap)typeof(WorldBuilder).GetField("cliffMap", flags).GetValue(Game.World);
                int freeSamples = 0, saturated = 0;
                for (int y = 0; y < ph; y++) for (int x = 0; x < pw; x++)
                {
                    int at = y * pw + x;
                    Vector2 p = new Vector2(bounds.xMin + (x + .5f) / px, bounds.yMin + (y + .5f) / px);
                    int count = Physics2D.OverlapPoint(p, filter, hits);
                    byte kind = 1; // 1 floor, 2 water, 0 wall, 3 existing prop footprint.
                    if (count == hits.Length) saturated++;
                    for (int i = 0; i < count; i++)
                    {
                        var hit = hits[i]; if (!SurfaceStatic(hit)) continue;
                        if (hit.transform == waterMap.transform) { kind = 2; break; }
                        if (hit.transform == cliffMap.transform) kind = 0;
                        else if (kind == 1) kind = 3;
                    }
                    terrainBytes[at] = kind;
                    semantic[at] = kind == 1 ? new Color32(181, 156, 113, 255) : kind == 2 ? new Color32(34, 71, 89, 255)
                        : kind == 3 ? new Color32(121, 88, 95, 255) : new Color32(23, 31, 50, 255);
                    count = Physics2D.OverlapCircle(p + new Vector2(0, .22f), .26f, filter, hits);
                    bool free = bounds.Contains(p); if (count == hits.Length) { saturated++; free = false; }
                    for (int i = 0; i < count && free; i++) if (SurfaceStatic(hits[i])) free = false;
                    walkBytes[at] = free ? (byte)1 : (byte)0;
                    walking[at] = free ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                    if (free) freeSamples++;
                }
                DCheck(map.id + " collision buffer complete", saturated == 0);
                var routeAudit = SurfaceRoutes(map.id, bounds, pw, ph, px, walkBytes, cells, out var exercise);
                object movement = null;
                if (!groundOnly)
                    yield return SurfaceExercise(map.id, surfaceInput, exercise, result => movement = result);
                object stairs = null;
                if (map.id == MapRegistry.Sanctum && Game.World.MapId == map.id)
                    yield return SurfaceStairs(surfaceInput, !groundOnly, result => stairs = result);
                // An unexpected portal transition is already a failed movement check;
                // stop exporting rather than attaching the next map's state to this id.
                if (Game.World.MapId != map.id)
                {
                    Log("SURFACE EXPORT STOPPED: short movement changed map from " + map.id + " to " + Game.World.MapId);
                    break;
                }
                SurfacePng(Path.Combine(folder, map.id + "-guide.png"), pw, ph, semantic);
                SurfacePng(Path.Combine(folder, map.id + "-walk-mask.png"), pw, ph, walking);
                string collisionHash = SurfaceHash(walkBytes), terrainHash = SurfaceHash(terrainBytes);
                var invariant = HRow(("id", map.id), ("bounds", SurfaceRect(bounds)), ("cells", rows), ("ground", groundRows),
                    ("spawn", SurfacePoint(Game.World.PlayerSpawn)), ("portals", portals), ("serviceDoors", doors),
                    ("camps", camps), ("collisionHash", collisionHash), ("terrainHash", terrainHash));
                string invariantText = MiniJson.Write(invariant);
                File.WriteAllText(Path.Combine(folder, map.id + "-baseline.json"), invariantText);
                File.WriteAllText(Path.Combine(folder, map.id + "-layout.txt"), string.Join("\n", rows));
                File.WriteAllText(Path.Combine(folder, map.id + "-ground.txt"), string.Join("\n", groundRows));
                if (!string.IsNullOrEmpty(baseline))
                {
                    string old = Path.Combine(baseline, map.id + "-baseline.json");
                    DCheck(map.id + " baseline exists", File.Exists(old));
                    DCheck(map.id + " navigation and services unchanged", File.Exists(old) && File.ReadAllText(old) == invariantText);
                }
                bool graphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
                object groundCapture = null;
                object vistaCapture = null;
                if (graphics)
                {
                    groundCapture = SurfaceGroundCapture(map.id, bounds);
                    // A clean composition guide must not bake monsters or the hero into its art.
                    if (!groundOnly)
                    {
                    // Enemy telegraphs/projectiles are separate objects under Fx.Root,
                    // not descendants of their caster. Hide them only during this render.
                    var hidden = SurfaceHideCombatRenderers(true);
                    try { RenderRegion(Path.Combine(folder, map.id + "-overview.png"), bounds, 32); }
                    finally { foreach (var r in hidden) if (r != null) r.enabled = true; }
                    Game.Camera.SetTarget(Game.Player.transform, true);
                    yield return Wait(.06f);
                    WorldLayerShot(map.id + "-gameplay");
                    vistaCapture = SurfaceVistaCapture(map.id);
                    }
                }
                object composition = SurfaceSceneAudit(map.id);
                object propAudit = PropAudit(map.id);
                object resourceAudit = null;
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-propsVerify") >= 0)
                    yield return PropResourceExercise(result => resourceAudit = result);
                index.Add(HRow(("id", map.id), ("name", map.displayName), ("theme", map.theme.ToString()),
                    ("town", MapRegistry.IsTown(map.id)), ("width", w), ("height", h), ("spawnFree", spawnFree),
                    ("freeSamples", freeSamples), ("collisionHash", collisionHash), ("terrainHash", terrainHash),
                    ("baselineHash", SurfaceHash(Encoding.UTF8.GetBytes(invariantText))), ("screenshots", graphics),
                    ("groundOnlyCapture", groundCapture), ("surfaceComposition", composition), ("vistaCapture", vistaCapture),
                    ("routeAudit", routeAudit), ("movement", movement), ("loadMilliseconds", loadMilliseconds),
                    ("sanctumStairs", stairs),
                    ("propIntegration", propAudit), ("resourceVisualLifecycle", resourceAudit),
                    ("previousCompositionReleased", oldReleased), ("previousOwnedLayerResources", oldResources.Length)));
                File.WriteAllText(Path.Combine(folder, "surface-index.json"), MiniJson.Write(index));
                yield return null;
            }
            if (maps.Length > 0)
            {
                var lastScenes = Game.World.GetComponentsInChildren<SurfaceWorldScene>();
                var lastResources = SurfaceOwnedLayers(lastScenes);
                Game.World.Load(MapRegistry.Village);
                Game.Player.Place(Game.World.PlayerSpawn, Facing.Down);
                yield return Wait(.1f); yield return null;
                DCheck("last exported composition resources released", lastScenes.All(s => s == null) && lastResources.All(r => r == null));
            }
            DCheck("surface export silent", AudioListener.volume == 0);
            File.WriteAllText(Path.Combine(folder, "surface-export.json"), MiniJson.Write(HRow(("version", 2), ("mapCount", index.Count),
                ("requestedMap", only), ("groundOnly", groundOnly),
                ("guidePixelsPerTile", 4), ("overviewPixelsPerTile", 32), ("origin", "bottom-left; text rows north-to-south"),
                ("palette", HRow(("floor", "#B59C71"), ("water", "#224759"), ("wall", "#171F32"), ("propFootprint", "#79585F"))),
                ("walkMask", "white = feet circle radius .26 at offset (0,.22) clears static colliders; black = blocked"),
                ("passed", dgnPassed), ("failed", dgnFailed), ("maps", index))));
            Log($"SURFACE RESULTS: {dgnPassed} passed, {dgnFailed} failed; {index.Count} maps");
        }

        static List<Renderer> SurfaceHideCombatRenderers(bool hideActors)
        {
            var hidden = new List<Renderer>();
            var ember = Game.Art.Optional("fx_spark");
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.enabled) continue;
                bool actor = hideActors && (r.GetComponentInParent<PlayerController>() != null
                    || r.GetComponentInParent<EnemyController>() != null || r.GetComponentInParent<NpcController>() != null);
                bool transient = r.GetComponentInParent<Telegraph>() != null || r.GetComponentInParent<MonsterProjectile>() != null
                    || r.GetComponentInParent<FxParticle>() != null || r.GetComponentInParent<DamageNumber>() != null
                    || r.GetComponentInParent<VfxPlayer>() != null;
                if (r.GetComponentInParent<SkillFx>() != null)
                    transient |= !(r is SpriteRenderer sr && sr.sprite == ember); // Keep campfire embers.
                if (!actor && !transient) continue;
                r.enabled = false; hidden.Add(r);
            }
            return hidden;
        }

        object SurfaceVistaCapture(string id)
        {
            Vector2 desired;
            switch (id)
            {
                case "canyon_ridge": desired = new Vector2(18.5f, 31.5f); break; // Raised western cliff rim.
                case "winter_edge": desired = new Vector2(14f, 35f); break; // Frozen lake and northern ledges.
                case "forest_depths": desired = new Vector2(13.5f, 10.5f); break; // Wet forest's southern canopy edge.
                case MapRegistry.Sanctum: desired = new Vector2(19.5f, 47.5f); break; // Upper landing, steps and pool.
                default: return null;
            }
            Vector2 chosen = desired; float best = float.MaxValue;
            // Inspect the actual collider footprint including a small standing margin.
            // No colliders, actors, nav data or normal camera settings are changed.
            for (int y = -8; y <= 8; y++) for (int x = -8; x <= 8; x++)
            {
                var p = desired + new Vector2(x, y) * .25f;
                float distance = (p - desired).sqrMagnitude;
                if (distance >= best || Game.World.PortalCenters.Any(v => Vector2.Distance(v, p) < 2f)) continue;
                if (!Game.World.IsFree(p) || !Game.World.IsFree(p + Vector2.up * .35f)
                    || !Game.World.IsFree(p + Vector2.down * .35f) || !Game.World.IsFree(p + Vector2.left * .35f)
                    || !Game.World.IsFree(p + Vector2.right * .35f)) continue;
                chosen = p; best = distance;
            }
            bool safe = best < float.MaxValue;
            DCheck(id + " scenic camera anchor fits actual player collision", safe);
            if (!safe) return HRow(("captured", false), ("requestedPosition", SurfacePoint(desired)));
            var oldPosition = Game.Player.Position; var oldFacing = Game.Player.Facing;
            var hidden = SurfaceHideCombatRenderers(false);
            var hiddenBanners = new List<GameObject>();
            foreach (var bossUi in UnityEngine.Object.FindObjectsByType<BossHpBarView>(FindObjectsInactive.Exclude))
            {
                // BossHpBarView.Build places only the transient centre-screen intro
                // at this child. Keep the boss HP bar, warnings and all other HUD visible.
                var banner = bossUi.transform.Find("BossBanner");
                if (banner == null || !banner.gameObject.activeSelf) continue;
                hiddenBanners.Add(banner.gameObject); banner.gameObject.SetActive(false);
            }
            try
            {
                Game.Player.Place(chosen, Facing.Down);
                Game.Player.GetComponent<YSort>()?.Refresh();
                Game.Camera.SetTarget(Game.Player.transform, true);
                // Synchronous render avoids giving AI time to converge on the camera anchor.
                // Enemies, NPCs, buildings, water and environmental particles remain visible.
                WorldLayerShot(id + "-vista-gameplay");
                // A second shot at the exact same live camera is for scenery review.
                // Gameplay HUD/capture above remains untouched; no AI frame runs here.
                var actors = SurfaceHideCombatRenderers(true);
                var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
                    .Where(c => c.enabled).ToArray();
                try
                {
                    foreach (var canvas in canvases) canvas.enabled = false;
                    var cam = Game.Camera.Camera;
                    var size = new Vector2(cam.orthographicSize * 2 * cam.aspect, cam.orthographicSize * 2);
                    RenderRegion(Path.Combine(folder, id + "-vista-environment.png"), new Rect((Vector2)cam.transform.position - size * .5f, size), 64);
                }
                finally
                {
                    foreach (var r in actors) if (r != null) r.enabled = true;
                    foreach (var canvas in canvases) if (canvas != null) canvas.enabled = true;
                }
            }
            finally
            {
                foreach (var banner in hiddenBanners) if (banner != null) banner.SetActive(true);
                foreach (var r in hidden) if (r != null) r.enabled = true;
                Game.Player.Place(oldPosition, oldFacing);
                Game.Player.GetComponent<YSort>()?.Refresh();
                Game.Camera.SetTarget(Game.Player.transform, true);
            }
            DCheck(id + " scenic capture restores boss intro banner", hiddenBanners.All(b => b != null && b.activeSelf));
            return HRow(("captured", true), ("file", id + "-vista-gameplay.png"), ("position", SurfacePoint(chosen)),
                ("environmentFile", id + "-vista-environment.png"),
                ("requestedPosition", SurfacePoint(desired)), ("camera", "normal follow camera and HUD"),
                ("temporaryCombatEffectsHidden", hidden.Count), ("temporaryBossIntroBannersHidden", hiddenBanners.Count),
                ("playerRestored", Game.Player.Position == oldPosition));
        }

        static UnityEngine.Object[] SurfaceOwnedLayers(SurfaceWorldScene[] scenes)
        {
            var resources = new List<UnityEngine.Object>();
            foreach (var scene in scenes)
            {
                resources.AddRange(scene.OwnedResources);
                var grounding = scene.GetComponent<SurfacePropGrounding>();
                if (grounding != null) resources.AddRange(grounding.OwnedResources);
                var canopy = scene.transform.parent.GetComponent<ForestCanopyScene>();
                if (canopy != null) resources.AddRange(canopy.OwnedResources);
                var world = scene.GetComponentInParent<WorldBuilder>();
                if (world != null) foreach (var water in world.GetComponentsInChildren<LivingWater>())
                    resources.AddRange(water.OwnedMasks);
                if (world != null) resources.AddRange(PropOwnedResources(world));
            }
            return resources.Distinct().ToArray();
        }

        object SurfaceRoutes(string id, Rect bounds, int w, int h, int px, byte[] free, char[,] cells, out List<Vector2> exercise)
        {
            Vector2 Point(int i) => new Vector2(bounds.xMin + (i % w + .5f) / px, bounds.yMin + (i / w + .5f) / px);
            int Nearest(Vector2 p)
            {
                int bx = Mathf.FloorToInt((p.x - bounds.xMin) * px), by = Mathf.FloorToInt((p.y - bounds.yMin) * px), chosen = -1;
                float best = .6001f * .6001f;
                for (int yy = Math.Max(0, by - 3); yy <= Math.Min(h - 1, by + 3); yy++)
                    for (int xx = Math.Max(0, bx - 3); xx <= Math.Min(w - 1, bx + 3); xx++)
                    {
                        int i = yy * w + xx; if (free[i] == 0) continue;
                        float d = (Point(i) - p).sqrMagnitude; if (d >= best) continue;
                        bool clear = true;
                        for (int k = 0; k <= 6 && clear; k++) clear = Game.World.IsFree(Vector2.Lerp(p, Point(i), k / 6f));
                        if (clear) { best = d; chosen = i; }
                    }
                return chosen;
            }
            int start = Nearest(Game.World.PlayerSpawn);
            var parent = new int[free.Length]; for (int i = 0; i < parent.Length; i++) parent[i] = -2;
            var queue = new Queue<int>();
            if (start >= 0) { parent[start] = -1; queue.Enqueue(start); }
            int reached = 0;
            void Enqueue(int from, int next)
            {
                if (next >= 0 && free[next] != 0 && parent[next] == -2) { parent[next] = from; queue.Enqueue(next); }
            }
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(); reached++;
                int x = at % w, y = at / w;
                if (x > 0) Enqueue(at, at - 1);
                if (x + 1 < w) Enqueue(at, at + 1);
                if (y > 0) Enqueue(at, at - w);
                if (y + 1 < h) Enqueue(at, at + w);
            }
            List<Vector2> Trace(int end)
            {
                var path = new List<Vector2>();
                if (end < 0 || parent[end] == -2) return path;
                for (int i = end; i >= 0; i = parent[i]) path.Add(Point(i));
                path.Reverse(); path.Insert(0, Game.World.PlayerSpawn); return path;
            }
            DCheck(id + " spawn anchors to actual quarter-tile navigation", start >= 0);
            var routeRows = new List<object>();
            foreach (var target in WorldRoutes.Neighbors(id).OrderBy(s => s, StringComparer.Ordinal))
            {
                var arrival = Game.World.ArrivalFrom(target, out _); int end = Nearest(arrival);
                bool connected = end >= 0 && parent[end] != -2;
                DCheck(id + " spawn reaches portal arrival " + target, connected);
                var trace = Trace(end); if (trace.Count > 0) trace.Add(arrival);
                routeRows.Add(HRow(("target", target), ("connected", connected), ("quarterTileSteps", trace.Count),
                    ("trace", trace.Where((p, i) => i % px == 0 || i == trace.Count - 1).Select(SurfacePoint).ToList())));
            }
            // A representative camp direction exercises the beginning of a real combat
            // route; towns use a short open direction away from portal triggers.
            int candidate = -1; float nearestCamp = float.MaxValue;
            for (int y = 0; y < cells.GetLength(1); y++) for (int x = 0; x < cells.GetLength(0); x++)
                if (cells[x, y] == 'k')
                {
                    var camp = new Vector2(x + .5f, y + .5f); int end = Nearest(camp);
                    float distance = (camp - Game.World.PlayerSpawn).sqrMagnitude;
                    if (end >= 0 && parent[end] != -2 && distance >= 2.25f && distance < nearestCamp) { candidate = end; nearestCamp = distance; }
                }
            bool towardsCamp = candidate >= 0;
            if (candidate < 0 && start >= 0)
            {
                float best = float.MinValue;
                for (int i = 0; i < parent.Length; i++)
                {
                    if (parent[i] == -2) continue; var p = Point(i); float d = Vector2.Distance(p, Game.World.PlayerSpawn);
                    if (d < 1.25f || d > 1.65f) continue;
                    bool nearPortal = Game.World.PortalCenters.Any(v => Vector2.Distance(v, p) < 1.35f); if (nearPortal) continue;
                    float score = -(p - bounds.center).sqrMagnitude;
                    if (score > best) { best = score; candidate = i; }
                }
            }
            exercise = new List<Vector2>(); float length = 0;
            foreach (var p in Trace(candidate))
            {
                if (exercise.Count > 0) length += Vector2.Distance(exercise[exercise.Count - 1], p);
                if (length > 1.65f) break;
                exercise.Add(p);
            }
            return HRow(("samplePixelsPerTile", px), ("reachableSamples", reached), ("freeSamples", free.Count(v => v != 0)),
                ("portalRoutes", routeRows), ("exerciseTowardsCamp", towardsCamp), ("exercisePath", exercise.Select(SurfacePoint).ToList()));
        }

        IEnumerator SurfaceExercise(string id, ScriptedInput input, List<Vector2> path, Action<object> report)
        {
            bool available = path.Count >= 3;
            DCheck(id + " short movement route available", available);
            if (!available) { report(HRow(("tested", false))); yield break; }
            Vector2 start = Game.Player.Position; float began = Time.realtimeSinceStartup;
            float furthest = 0; bool free = true;
            IEnumerator Follow(List<Vector2> route)
            {
                int step = 1; float deadline = Time.realtimeSinceStartup + 1.1f;
                while (step < route.Count && Time.realtimeSinceStartup < deadline && Game.World.MapId == id)
                {
                    var delta = route[step] - Game.Player.Position;
                    if (delta.magnitude < .13f) { step++; continue; }
                    input.Move = delta.normalized;
                    yield return null;
                    furthest = Mathf.Max(furthest, Vector2.Distance(start, Game.Player.Position));
                    free &= Game.World.IsFree(Game.Player.Position);
                }
                input.Move = Vector2.zero;
            }
            yield return Follow(path);
            bool outward = Vector2.Distance(Game.Player.Position, path[path.Count - 1]) < .4f && furthest >= .65f;
            var reverse = new List<Vector2>(path); reverse.Reverse();
            // Start from the nearest point if the time cap stopped the outward leg early.
            int closest = 0; float best = float.MaxValue;
            for (int i = 0; i < reverse.Count; i++) { float d = (reverse[i] - Game.Player.Position).sqrMagnitude; if (d < best) { best = d; closest = i; } }
            reverse.RemoveRange(0, closest); reverse.Insert(0, Game.Player.Position);
            yield return Follow(reverse);
            input.Move = Vector2.zero;
            bool returned = Game.World.MapId == id && Vector2.Distance(start, Game.Player.Position) < .4f;
            DCheck(id + " actual input moves forward", outward);
            DCheck(id + " actual input returns without portal travel", returned);
            DCheck(id + " actual movement feet remain free", free);
            report(HRow(("tested", true), ("outward", outward), ("returned", returned), ("allFeetFree", free),
                ("furthestDistance", furthest), ("milliseconds", (Time.realtimeSinceStartup - began) * 1000f)));
            if (Game.World.MapId == id)
            {
                Game.Player.Place(start, Facing.Down); // Restore the screenshot anchor after physics verification.
                Game.Camera.SetTarget(Game.Player.transform, true);
            }
        }

        IEnumerator SurfaceStairs(ScriptedInput input, bool walk, Action<object> report)
        {
            var allStairs = Game.World.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(r => r.name == "Central terrace stairs" || r.sprite != null && r.sprite.name == "sanctum_stairs").ToArray();
            var stair = allStairs.FirstOrDefault();
            var asset = SunkenSanctumArt.Asset("stairs");
            bool one = allStairs.Length == 1 && stair != null && stair.enabled && !stair.forceRenderingOff && stair.gameObject.activeInHierarchy;
            bool unchanged = one && asset != null && stair.sprite == asset && stair.sprite.name == "sanctum_stairs"
                && Vector2.Distance(stair.transform.position, new Vector2(24, 42)) < .001f
                && (stair.transform.localScale - new Vector3(4 / asset.bounds.size.x, 6.2f / asset.bounds.size.y, 1)).sqrMagnitude < .000001f
                && Quaternion.Angle(stair.transform.rotation, Quaternion.identity) < .001f && stair.sortingOrder == -27900;
            DCheck("sanctum exactly one active foreground staircase", one);
            DCheck("sanctum original stairs sprite position size and order retained", unchanged);
            var curbs = Game.World.GetComponentsInChildren<BoxCollider2D>(true).Where(c => c.name == "Stair side curb").ToArray();
            bool curbsUnchanged = curbs.Length == 2 && curbs.All(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger
                && (c.size - new Vector2(.3f, 4.4f)).sqrMagnitude < .000001f && c.offset == Vector2.zero
                && c.transform.localScale == Vector3.one && Quaternion.Angle(c.transform.rotation, Quaternion.identity) < .001f)
                && new[] { 22.15f, 25.85f }.All(x => curbs.Any(c => Vector2.Distance(c.transform.position, new Vector2(x, 44.4f)) < .001f));
            DCheck("sanctum two original stair curb collisions retained", curbsUnchanged);
            // (24,49) is the original upper pond, not the landing. Use the real
            // stair crest then turn west to the upper terrace, matching SanctumRun.
            var path = new[] { new Vector2(24, 42), new Vector2(24, 47.2f), new Vector2(18, 47.2f), new Vector2(18, 49) };
            bool routeFree = true;
            for (int segment = 1; segment < path.Length; segment++)
            {
                int samples = Mathf.CeilToInt(Vector2.Distance(path[segment - 1], path[segment]) * 8);
                for (int step = 0; step <= samples; step++)
                    routeFree &= Game.World.IsFree(Vector2.Lerp(path[segment - 1], path[segment], step / (float)samples));
            }
            DCheck("sanctum existing stair and upper landing feet corridor free", routeFree);
            DCheck("sanctum upper central pond remains blocked", !Game.World.IsFree(new Vector2(24, 49)));
            bool outward = false, returned = false, allFeetFree = true;
            var trace = new List<Vector2>(); float started = Time.realtimeSinceStartup;
            Vector2 oldPosition = Game.Player.Position, oldMove = input.Move;
            Facing oldFacing = Game.Player.Facing;
            var camera = Game.Camera.Camera; Vector3 oldCameraPosition = camera.transform.position;
            float oldZoom = camera.orthographicSize;
            if (walk && routeFree)
            {
                IEnumerator To(Vector2 target)
                {
                    float deadline = Time.realtimeSinceStartup + 7f;
                    while (Game.World.MapId == MapRegistry.Sanctum && Vector2.Distance(Game.Player.Position, target) > .06f
                        && Time.realtimeSinceStartup < deadline)
                    {
                        input.Move = Vector2.ClampMagnitude((target - Game.Player.Position) / .25f, 1);
                        yield return null;
                        allFeetFree &= Game.World.IsFree(Game.Player.Position);
                        if (trace.Count == 0 || Vector2.Distance(trace[trace.Count - 1], Game.Player.Position) > .25f) trace.Add(Game.Player.Position);
                    }
                    input.Move = Vector2.zero;
                }
                try
                {
                    Game.Player.Place(path[0], Facing.Up); Game.Camera.SetTarget(Game.Player.transform, true);
                    bool reached = true;
                    for (int i = 1; i < path.Length && reached; i++)
                    {
                        yield return To(path[i]);
                        reached &= Game.World.MapId == MapRegistry.Sanctum && Vector2.Distance(Game.Player.Position, path[i]) < .12f;
                    }
                    outward = reached;
                    for (int i = path.Length - 2; i >= 0 && reached; i--)
                    {
                        yield return To(path[i]);
                        reached &= Game.World.MapId == MapRegistry.Sanctum && Vector2.Distance(Game.Player.Position, path[i]) < .12f;
                    }
                    returned = reached;
                }
                finally
                {
                    input.Move = oldMove;
                    if (Game.World.MapId == MapRegistry.Sanctum)
                    {
                        Game.Player.Place(oldPosition, oldFacing); Game.Player.GetComponent<YSort>()?.Refresh();
                        Game.Camera.SetTarget(Game.Player.transform, true); camera.transform.position = oldCameraPosition;
                    }
                }
                DCheck("sanctum actual input climbs stairs and reaches upper terrace", outward);
                DCheck("sanctum actual input returns down original stairs", returned);
                DCheck("sanctum stair traversal feet stay free", allFeetFree);
                DCheck("sanctum stair traversal restores player and camera", Game.World.MapId == MapRegistry.Sanctum
                    && Game.Player.Position == oldPosition && Game.Player.Facing == oldFacing && camera.orthographicSize == oldZoom
                    && camera.transform.position == oldCameraPosition);
            }
            report(HRow(("oneActiveStaircase", one), ("originalSpriteAndTransform", unchanged), ("curbsUnchanged", curbsUnchanged),
                ("sprite", stair == null || stair.sprite == null ? null : stair.sprite.name), ("position", stair == null ? null : SurfacePoint(stair.transform.position)),
                ("route", path.Select(SurfacePoint).ToList()), ("routeFree", routeFree), ("tested", walk && routeFree),
                ("outward", outward), ("returned", returned), ("allFeetFree", allFeetFree), ("trace", trace.Select(SurfacePoint).ToList()),
                ("milliseconds", (Time.realtimeSinceStartup - started) * 1000f)));
        }

        // Exact old terrain renderers, not a broad sort-order test (which would admit
        // light glows, water overlays, arrows and props). No changes to their objects.
        static bool SurfaceOriginalGround(Renderer renderer)
        {
            if (!renderer.transform.IsChildOf(Game.World.transform)) return false;
            if (renderer.GetComponentInParent<SurfaceWorldScene>() != null) return false;
            var parent = renderer.transform.parent;
            if (renderer is UnityEngine.Tilemaps.TilemapRenderer)
                return parent != null && parent.name == "Grid" && parent.parent == Game.World.transform
                    && (renderer.name == "Ground" || renderer.name == "GroundDetail" || renderer.name == "GrassEdge"
                        || renderer.name == "Water" || renderer.name == "Cliffs");
            if (!(renderer is SpriteRenderer sr) || sr.sprite == null) return false;
            if (parent != null && parent.name == "PaintedGround" && parent.parent == Game.World.transform && sr.name == "Ground") return true;
            return parent == Game.World.ObjectsRoot && (sr.name == "Sanctum terrain"
                || sr.name == "Layered sanctuary ground" || sr.name == "Cave floor and stratified rock");
        }

        object SurfaceGroundCapture(string mapId, Rect bounds)
        {
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            var originals = renderers.Where(SurfaceOriginalGround).ToArray();
            // Invisible collision tilemaps exist even if no old painting was built.
            int paintings = originals.Count(r => r is SpriteRenderer);
            bool dedicatedGroundRun = Array.IndexOf(Environment.GetCommandLineArgs(), "-surfaceGroundOnly") >= 0;
            bool compositionReplacedGround = Game.World.GetComponentInChildren<SurfaceWorldScene>() != null;
            if (paintings > 0 || dedicatedGroundRun || !compositionReplacedGround)
                DCheck(mapId + " original static ground painting available", paintings > 0);
            if (paintings == 0)
            {
                Log("GROUND CAPTURE SKIPPED " + mapId + ": old terrain was not built; run with the composition fallback enabled");
                return HRow(("captured", false), ("reason", "original terrain painting absent"));
            }
            var enabled = renderers.Select(r => r.enabled).ToArray();
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
            var canvasEnabled = canvases.Select(c => c.enabled).ToArray();
            var sprites = originals.OfType<SpriteRenderer>().ToArray();
            var materials = sprites.Select(s => s.sharedMaterial).ToArray();
            Material temporary = null;
            var plain = FxMaterials.Sharp;
            if (plain == null) temporary = plain = new Material(Shader.Find("Sprites/Default"));
            try
            {
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = SurfaceOriginalGround(renderers[i]);
                foreach (var canvas in canvases) canvas.enabled = false;
                // LivingWater modifies ground materials, so freezing its clock would still
                // bake animated highlights. Plain sharp sampling recovers the original PNG.
                foreach (var sprite in sprites) sprite.sharedMaterial = plain;
                RenderRegion(Path.Combine(folder, mapId + "-ground-only.png"), bounds, 32);
            }
            finally
            {
                for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) sprites[i].sharedMaterial = materials[i];
                for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = enabled[i];
                for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = canvasEnabled[i];
                if (temporary != null) Destroy(temporary);
            }
            return HRow(("captured", true), ("file", mapId + "-ground-only.png"), ("pixelsPerTile", 32),
                ("paintingRenderers", paintings), ("groundRendererNames", originals.Select(r => r.name).Distinct().ToList()),
                ("propsAndActors", false), ("waterAnimation", false), ("ui", false));
        }

        object SurfaceSceneAudit(string mapId)
        {
            var scenes = Game.World.GetComponentsInChildren<SurfaceWorldScene>();
            if (int.TryParse(SurfaceArg("-surfaceExpectedPpu"), out int requiredPpu))
                DCheck(mapId + " requested composition exists at " + requiredPpu + "ppu", scenes.Length == 1);
            if (scenes.Length == 0) return null; // Baseline/fallback maps have no composition metadata.
            DCheck(mapId + " one map-owned surface composition", scenes.Length == 1);
            var s = scenes[0];
            DCheck(mapId + " composition belongs to current map", s.MapId == mapId && s.WorldBounds == Game.World.Bounds);
            DCheck(mapId + " exclusive composition layer slots", s.Layers.Length == 3 && s.LayerCount > 0 && s.UncoveredPixels == 0 && s.OverlappingPixels == 0 && s.MismatchedPixels == 0);
            DCheck(mapId + " supporting edges avoid walkable terrain", s.SupportOnWalkablePixels == 0);
            DCheck(mapId + " composition uses sharp pixels", s.Layers.Where(r => r != null).All(r => r.sprite != null && r.sprite.texture.filterMode == FilterMode.Point));
            var textures = s.Layers.Where(r => r != null).Select(r => r.sprite.texture).Distinct().ToArray();
            DCheck(mapId + " composition shares one unreadable source texture", textures.Length == 1 && textures[0] == s.SharedTexture
                && !s.SharedTexture.isReadable && s.OwnedResources.OfType<Texture2D>().Count() == 1);
            DCheck(mapId + " mesh cells and source UVs exactly partition artwork", s.WrongOwnerPixels == 0 && s.PixelBoundsErrors == 0
                && s.MismatchedPixels == 0 && s.LayerVertexCounts.Length == 3 && s.LayerTriangleCounts.Length == 3);
            int expectedPpu = s.SourceWidth >= s.WorldBounds.width * 96 && s.SourceHeight >= s.WorldBounds.height * 96 ? 96
                : s.SourceWidth >= s.WorldBounds.width * 48 && s.SourceHeight >= s.WorldBounds.height * 48 ? 48 : 32;
            DCheck(mapId + " registered source resolution is preserved", s.PixelsPerUnit == expectedPpu
                && s.RasterWidth == s.WorldBounds.width * expectedPpu && s.RasterHeight == s.WorldBounds.height * expectedPpu
                && s.SharedTexture.width == s.RasterWidth && s.SharedTexture.height == s.RasterHeight
                && s.SharedTexture.width <= SystemInfo.maxTextureSize && s.SharedTexture.height <= SystemInfo.maxTextureSize);
            if (int.TryParse(SurfaceArg("-surfaceExpectedPpu"), out int requestedPpu))
                DCheck(mapId + " requested artwork resolution " + requestedPpu, s.PixelsPerUnit == requestedPpu);
            var waterAudit = SurfaceWaterAudit(mapId, s);
            var grounding = s.GetComponent<SurfacePropGrounding>();
            if (grounding != null)
            {
                var ownedContacts = new HashSet<UnityEngine.Object>(grounding.OwnedResources);
                var expectedContacts = new HashSet<UnityEngine.Object>();
                bool ContactPicture(Sprite picture)
                {
                    if (picture == null || picture.texture == null) return false;
                    expectedContacts.Add(picture); expectedContacts.Add(picture.texture);
                    return ownedContacts.Contains(picture) && ownedContacts.Contains(picture.texture)
                        && picture.texture.filterMode == FilterMode.Point;
                }
                // Empty ground masks deliberately allocate no static layer. Resources
                // may still own independent patches even when this shared layer is absent.
                bool staticLayer = grounding.ShadowPixelCount == 0 ? grounding.Renderer == null
                    : grounding.ShadowPixelCount > 0 && grounding.Renderer != null
                        && grounding.Renderer.sortingOrder == -21000 && ContactPicture(grounding.Renderer.sprite);
                bool resourceLayers = grounding.ResourceContacts.All(c => c != null && c.Node != null
                    && c.transform.IsChildOf(s.transform) && c.Node.transform.IsChildOf(Game.World.transform)
                    && c.Renderer != null && c.Renderer.transform.IsChildOf(c.transform)
                    && c.Renderer.sortingOrder == -21000
                    && (c.StandingSprite != null || c.DepletedSprite != null)
                    && (c.StandingSprite == null || ContactPicture(c.StandingSprite))
                    && (c.DepletedSprite == null || ContactPicture(c.DepletedSprite)));
                bool contactCensus = grounding.ResourceContactCount == grounding.ResourceContacts.Count
                    && (grounding.ResourceContactCount == 0 ? grounding.ResourceShadowPixelCount == 0 : grounding.ResourceShadowPixelCount > 0)
                    && ownedContacts.Count == grounding.OwnedResources.Length && ownedContacts.SetEquals(expectedContacts);
                DCheck(mapId + " contact layer is visual only and below actors", staticLayer && resourceLayers
                    && grounding.GetComponentsInChildren<Collider2D>(true).Length == 0);
                DCheck(mapId + " empty and resource contact ownership matches generated pixels", contactCensus);
            }
            var canopy = Game.World.GetComponentInChildren<ForestCanopyScene>();
            var hiddenNatureAudit = SurfaceHiddenNatureAudit(mapId);
            if (Game.World.SurfaceHiddenUnsupportedLandmarkCount > 0)
            {
                var hiddenLandmarks = Game.World.GetComponentsInChildren<SpriteRenderer>()
                    .Where(r => r.name == "Fantasy landmark " + mapId && !r.enabled).ToArray();
                DCheck(mapId + " unsupported canyon decorations are visual only", hiddenLandmarks.Length == Game.World.SurfaceHiddenUnsupportedLandmarkCount
                    && hiddenLandmarks.All(r => r.GetComponentsInChildren<Collider2D>(true).Length == 0));
            }
            return HRow(("mapId", s.MapId), ("sourcePath", s.SourcePath), ("sourceSha256", SurfaceFileHash(s.SourcePath)), ("sourceWidth", s.SourceWidth), ("sourceHeight", s.SourceHeight),
                ("propGrounding", grounding == null ? null : HRow(("props", grounding.PropCount), ("shadowPixels", grounding.ShadowPixelCount),
                    ("staticRenderer", grounding.Renderer != null), ("resourceContacts", grounding.ResourceContactCount),
                    ("resourceShadowPixels", grounding.ResourceShadowPixelCount), ("ownedResources", grounding.OwnedResources.Length))),
                ("waterRegistration", waterAudit), ("sharedTextureCount", textures.Length), ("texturePayloadBytes", s.TexturePayloadBytes),
                ("storage", "one shared texture; exclusive tile-run sprite meshes"), ("maxTextureSize", SystemInfo.maxTextureSize),
                ("layerVertexCounts", s.LayerVertexCounts), ("layerTriangleCounts", s.LayerTriangleCounts),
                ("wrongOwnerPixels", s.WrongOwnerPixels), ("pixelBoundsErrors", s.PixelBoundsErrors),
                ("forestCanopy", canopy == null ? null : HRow(("edgeTrees", canopy.EdgeTrees), ("rearTrees", canopy.RearTrees),
                    ("staticTreeViews", canopy.StaticTreeViews), ("deadTreeViews", canopy.DeadTreeViews), ("species", canopy.SpeciesCount),
                    ("entranceSitesSkipped", canopy.EntranceSitesSkipped), ("ownedResources", canopy.OwnedResources.Length))),
                ("pixelsPerUnit", s.PixelsPerUnit), ("rasterWidth", s.RasterWidth), ("rasterHeight", s.RasterHeight),
                ("worldBounds", SurfaceRect(s.WorldBounds)), ("sourceAspectRatioError", s.SourceAspectRatioError),
                ("sourceOpaquePixels", s.SourceOpaquePixelCount), ("rasterOpaquePixels", s.RasterOpaquePixelCount),
                ("layerPixelCounts", s.LayerPixelCounts), ("layerRasterPixelCounts", s.LayerRasterPixelCounts),
                ("uncoveredPixels", s.UncoveredPixels), ("overlappingPixels", s.OverlappingPixels), ("mismatchedPixels", s.MismatchedPixels),
                ("supportOnWalkablePixels", s.SupportOnWalkablePixels),
                ("activeLayerCount", s.LayerCount), ("hiddenOriginalGroundRenderers", Game.World.SurfaceHiddenGroundRendererCount),
                ("hiddenUnsupportedLandmarks", Game.World.SurfaceHiddenUnsupportedLandmarkCount),
                ("hiddenUnsupportedNature", hiddenNatureAudit),
                ("layers", s.Layers.Select(r => (object)HRow(("name", r == null ? null : r.name), ("order", r == null ? 0 : r.sortingOrder),
                    ("enabled", r != null && r.enabled))).ToList()));
        }

        object SurfaceWaterAudit(string mapId, SurfaceWorldScene scene)
        {
            var results = new List<object>();
            var floor = scene.Layers.Length > 2 ? scene.Layers[2] : null;
            foreach (var water in Game.World.GetComponentsInChildren<LivingWater>())
            {
                bool expectedBinding = floor != null && water.Field != null && water.Field.WetPixels > 0
                    && water.Field.Width == scene.WorldBounds.width * 32 && water.Field.Height == scene.WorldBounds.height * 32;
                if (expectedBinding) DCheck(mapId + " composed floor is bound to existing water simulation", water.CompositionBindingCount > 0);
                if (water.CompositionBindingCount == 0) continue;
                var mask = water.CompositionMask;
                var props = new MaterialPropertyBlock(); if (floor != null) floor.GetPropertyBlock(props);
                int errors = 0;
                if (floor != null)
                {
                    var vertices = floor.sprite.vertices; var uv = floor.sprite.uv;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector2 world = floor.transform.TransformPoint(vertices[i]);
                        if (Mathf.Abs(uv[i].x * water.Field.Width - world.x * 32f) > .003f
                            || Mathf.Abs(uv[i].y * water.Field.Height - world.y * 32f) > .003f) errors++;
                    }
                }
                bool aligned = floor != null && mask != null && props.GetTexture("_WaterMask") == mask
                    && mask.width == water.Field.Width && mask.height == water.Field.Height
                    && mask.filterMode == FilterMode.Point && !mask.isReadable && errors == 0;
                DCheck(mapId + " shared water mask retains exact 32ppu world registration", aligned);
                results.Add(HRow(("bindings", water.CompositionBindingCount), ("width", mask == null ? 0 : mask.width),
                    ("height", mask == null ? 0 : mask.height), ("pixelsPerUnit", 32), ("worldUvErrors", errors),
                    ("texturePayloadBytes", mask == null ? 0L : (long)mask.width * mask.height * 4), ("aligned", aligned)));
            }
            return results;
        }

        static bool SurfaceStatic(Collider2D hit)
        {
            return hit != null && hit.enabled && hit.gameObject.activeInHierarchy && !hit.isTrigger
                && !(Game.Player != null && hit.transform.IsChildOf(Game.Player.transform))
                && hit.GetComponentInParent<EnemyController>() == null && hit.GetComponentInParent<NpcController>() == null;
        }
        static string SurfaceArg(string name)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        static object SurfacePoint(Vector2 p) => HRow(("x", Math.Round(p.x, 4)), ("y", Math.Round(p.y, 4)));
        static object SurfaceRect(Rect r) => HRow(("x", r.x), ("y", r.y), ("width", r.width), ("height", r.height));
        static string SurfaceHash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static string SurfaceFileHash(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using (var input = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        static void SurfacePng(string path, int w, int h, Color32[] colors)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            try { texture.SetPixels32(colors); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(texture); }
        }
    }
}
