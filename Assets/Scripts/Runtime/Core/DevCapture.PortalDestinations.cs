using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator PortalDestinationRun()
        {
            yield return Wait(1); Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "포탈 검증"); yield return Wait(1.5f);
            var input = new ScriptedInput(); Game.Player.Input = input;
            Game.Player.Health.SetInvulnerable(600); AudioListener.volume = 0; Game.Audio?.SetVolumes(0, 0);
            dgnPassed = dgnFailed = 0;
            var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            foreach (var info in MapRegistry.All.Where(m => !m.instanced && !m.IsInterior))
            {
                var preview = WorldAtlas.Get(info.id);
                DCheck(info.id + " preview exits have destinations", preview.portals.All(p => MapRegistry.Exists(p.target)));
                if (!string.IsNullOrEmpty(info.hubMap)) DCheck(info.id + " unvisited hub marker", preview.portals.Any(p => p.target == info.hubMap));
            }
            foreach (var id in new[] { MapRegistry.Village, MapRegistry.Canyon, MapRegistry.Winter, MapRegistry.Sanctum })
            {
                Game.World.Load(id); Game.Player.Place(Game.World.PlayerSpawn, Facing.Down); yield return Wait(.25f);
                bool exists = Game.World.PortalTowards(MapRegistry.Undergate, out var portal);
                DCheck(id + " hub exit exists", exists); if (!exists) continue;
                var inward = WorldRoutes.HubInward(id);
                var arrival = Game.World.ArrivalFrom(MapRegistry.Undergate, out _);
                Log(id + " hub=" + portal + " arrival=" + arrival);
                DCheck(id + " peripheral approach matches atlas", Vector2.Distance(portal, WorldRoutes.HubApproach(id)) < .1f);
                DCheck(id + " three-wide passage", MapPortal.Active.Count(p => p.TargetMap == MapRegistry.Undergate) == 3);
                DCheck(id + " arrival is free and outside trigger", Game.World.IsFree(arrival) && Vector2.Distance(arrival, portal) > 2);
                DCheck(id + " hub plaza artwork removed", !Game.World.ObjectsRoot.GetComponentsInChildren<Transform>().Any(t => t.name.Contains("뿌리샘") && t.name.Contains("Passage")));
                foreach (var target in WorldRoutes.Neighbors(id)) DCheck(id + " keeps route " + target, Game.World.PortalTowards(target, out _));
                Game.Player.Place(arrival + inward, Facing.Down); Game.Camera.SetTarget(Game.Player.transform, true); yield return Wait(.3f);
                InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)Game.Camera.Camera.WorldToScreenPoint(portal)); yield return null; yield return null;
                DCheck(id + " world hover destination", PortalTooltip.Instance.ShownTarget == MapRegistry.Undergate);
                WorldLayerShot(id + "-portal-hover");
                Game.Flow.OpenWindow(Game.UI.WorldMap); Game.UI.WorldMap.SelectMap(id); yield return Wait(.15f);
                var pin = Game.UI.WorldMap.GetComponentsInChildren<PortalDestinationPin>().First(p => p.Target == MapRegistry.Undergate);
                InputSystem.QueueDeltaStateEvent(mouse.position, RectTransformUtility.WorldToScreenPoint(null, pin.transform.position)); yield return null; yield return null;
                DCheck(id + " atlas hover destination", PortalTooltip.Instance.ShownTarget == MapRegistry.Undergate);
                WorldLayerShot(id + "-map-hover");
                Game.UI.WorldMap.Close(); InputSystem.QueueDeltaStateEvent(mouse.position, Vector2.zero); yield return Wait(.1f);
                DCheck(id + " tooltip hides on close", PortalTooltip.Instance.ShownTarget == null);
                var mini = Game.UI.GetComponentsInChildren<MinimapView>().First();
                var miniPin = mini.GetComponentsInChildren<PortalDestinationPin>().First(p => p.Target == MapRegistry.Undergate);
                InputSystem.QueueDeltaStateEvent(mouse.position, RectTransformUtility.WorldToScreenPoint(null, miniPin.transform.position)); yield return null; yield return null;
                DCheck(id + " minimap hover destination", PortalTooltip.Instance.ShownTarget == MapRegistry.Undergate);
                if (id == MapRegistry.Village) WorldLayerShot("minimap-hover");
                InputSystem.QueueDeltaStateEvent(mouse.position, Vector2.zero);
                // Walk across the 3-tile portal rather than calling TravelTo.
                input.Move = -inward; float deadline = Time.unscaledTime + 4;
                while (Game.World.MapId == id && Time.unscaledTime < deadline) yield return null;
                input.Move = Vector2.zero; while (Game.Flow.IsTransitioning) yield return null; yield return Wait(.3f);
                DCheck(id + " walking triggers hub travel", Game.World.MapId == MapRegistry.Undergate);
                Log("walk ended " + Game.World.MapId + " " + Game.Player.Position + " state=" + Game.State.Current);
                if (Game.World.MapId == MapRegistry.Undergate)
                {
                    Game.World.PortalTowards(id, out var back); Game.Player.Place(back, Facing.Down);
                    yield return Wait(.8f); while (Game.Flow.IsTransitioning) yield return null;
                    DCheck(id + " return route", Game.World.MapId == id); yield return Wait(.5f);
                    DCheck(id + " return does not bounce", Game.World.MapId == id && Game.World.IsFree(Game.Player.Position));
                }
            }
            Game.Flow.OpenWindow(Game.UI.WorldMap); Game.UI.WorldMap.SelectWorld(WorldLayer.Surface); yield return Wait(.2f);
            DCheck("surface chart is HD and visible", Game.UI.WorldMap.gameObject.activeInHierarchy && SurfaceAtlasArtIsHd());
            yield return SurfaceAtlasFieldAudit();
            WorldLayerShot("world-atlas-surface-hd"); yield return Wait(.2f);
            var gateway = Game.UI.WorldMap.GetComponentsInChildren<Button>().First(b => b.name == "NamePlate_undergate");
            gateway.onClick.Invoke(); yield return Wait(.2f);
            DCheck("central root spring opens underground chart", Game.UI.WorldMap.AtlasVisible && Game.UI.WorldMap.SelectedWorld == WorldLayer.Underground);
            WorldLayerShot("world-atlas-underground-hd"); yield return Wait(.2f); Game.UI.WorldMap.Close();
            var chain = new[] { MapRegistry.Undergate, "hollow_descent", "hollow_roots", "hollow_fungal", "hollow_depths" };
            for (int i = 1; i < chain.Length; i++)
            {
                var expected = i == chain.Length - 1 ? new[] { chain[i - 1] } : new[] { chain[i - 1], chain[i + 1] };
                DCheck(chain[i] + " exact linear connections", WorldRoutes.Neighbors(chain[i]).OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)));
            }

            DCheck("portal verification muted", AudioListener.volume == 0);
            Log($"PORTAL RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }

        // Acceptance data is deliberately independent of the atlas label/position helpers.
        static readonly (string town, string next, string prefix, string name, string[] fields)[] AtlasFieldExpectations =
        {
            ("village", "canyon", "숲", "해골 숲", new[] { "forest", "forest_ruins", "forest_depths", "forest_crossing" }),
            ("canyon", "winter", "협곡", "바위 협곡", new[] { "canyon_pass", "canyon_mine", "canyon_ridge", "canyon_gate" }),
            ("winter", "sunken_sanctum", "설원", "눈꽃 숲", new[] { "winter_edge", "winter_lake", "winter_peak", "winter_reach" }),
            ("sunken_sanctum", "village", "성소", "고대 성소", new[] { "sanctum_hall", "sanctum_archive", "sanctum_roots", "sanctum_court" })
        };

        // Product geography and authored physical exits, independent of routing/atlas helpers.
        static readonly (string target, string direction, Vector2 outward, Vector2 portal)[] HubCompassExpectations =
        {
            ("village", "west", Vector2.left, new Vector2(.5f, 22.5f)),
            ("canyon", "north", Vector2.up, new Vector2(26.5f, 43.5f)),
            ("winter", "east", Vector2.right, new Vector2(51.5f, 22.5f)),
            ("sunken_sanctum", "south", Vector2.down, new Vector2(26.5f, .5f))
        };

        IEnumerator HubCompassRun()
        {
            yield return Wait(1); Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "뿌리샘 방위 검증"); yield return Wait(1.5f);
            var input = new ScriptedInput(); Game.Player.Input = input;
            Game.Player.Health.SetInvulnerable(600);
            AudioListener.volume = 0; Game.Audio?.SetVolumes(0, 0); dgnPassed = dgnFailed = 0;
            ApplyRequestedResolution(); yield return Wait(.3f);
            DCheck("hub compass test controls the party leader", Game.Party == null || Game.Party.Leader == Game.Player);
            var screen = Game.UI.WorldMap;
            Game.Flow.OpenWindow(screen); screen.SelectWorld(WorldLayer.Surface); yield return Wait(.2f);
            var chart = screen.GetComponentsInChildren<RectTransform>().FirstOrDefault(t => t.name == "Chart_Surface");
            DCheck("hub compass surface chart exists", chart != null);
            if (chart != null) SurfaceAtlasCompassAudit(chart);
            WorldLayerShot("hub-compass-atlas"); screen.Close(); yield return Wait(.2f);

            // Direct travel only establishes the initial test map, never the checked crossings.
            Game.Flow.TravelTo("undergate", true);
            float setupDeadline = Time.unscaledTime + 30;
            while (Game.Flow.IsTransitioning && Time.unscaledTime < setupDeadline) yield return null;
            yield return Wait(.3f);
            DCheck("hub compass setup reaches undergate", Game.World.MapId == "undergate" && !Game.Flow.IsTransitioning);
            Log("HUB COMPASS input: ScriptedInput movement through live physics triggers; direct travel only for test setup.");
            foreach (var expected in HubCompassExpectations)
            {
                if (Game.World.MapId != "undergate" || Game.Flow.IsTransitioning)
                {
                    DCheck(expected.direction + " hub crossing can begin in undergate", false);
                    continue;
                }
                Game.Player.Health.SetInvulnerable(600);
                string label = "undergate " + expected.direction + " to " + expected.target;
                bool exists = Game.World.PortalTowards(expected.target, out var portal);
                DCheck(label + " has the exact authored portal center", exists && Vector2.Distance(portal, expected.portal) < .01f);
                var triggers = MapPortal.Active.Where(p => p != null && p.isActiveAndEnabled && p.TargetMap == expected.target).ToArray();
                Vector2 tangent = expected.outward.x != 0 ? Vector2.up : Vector2.right;
                DCheck(label + " has three live triggers across its opening", triggers.Length == 3 && new[] { -1, 0, 1 }.All(offset =>
                    triggers.Count(p => Vector2.Distance(p.transform.position, expected.portal + tangent * offset) < .01f
                        && p.GetComponent<Collider2D>() != null && p.GetComponent<Collider2D>().enabled && p.GetComponent<Collider2D>().isTrigger) == 1));
                Vector2 expectedArrival = expected.portal - expected.outward * 2.2f;
                DCheck(label + " has a safe inward arrival", Vector2.Distance(Game.World.ArrivalFrom(expected.target, out _), expectedArrival) < .01f
                    && Game.World.IsFree(expectedArrival));
                if (!exists) continue;
                Vector2 approach = expected.portal - expected.outward * 3.2f;
                DCheck(label + " approach is walkable", Game.World.IsFree(approach));
                Game.Player.Place(approach, FacingExtensions.FromVector(expected.outward, Facing.Down));
                Game.Camera.SetTarget(Game.Player.transform, true); yield return Wait(.2f);
                WorldLayerShot("hub-compass-" + expected.direction + "-approach");
                yield return HubCompassWalk(input, "undergate", expected.target, expected.outward, label);
                if (Game.World.MapId != expected.target || Game.Flow.IsTransitioning) continue;

                Game.Player.Health.SetInvulnerable(600);
                bool returnExists = Game.World.PortalTowards("undergate", out var returnPortal);
                DCheck(expected.target + " arrival matches its reciprocal safe point", Game.World.IsFree(Game.Player.Position)
                    && Vector2.Distance(Game.Player.Position, Game.World.ArrivalFrom("undergate", out _)) < .1f);
                DCheck(expected.target + " arrival stays outside the return trigger", returnExists
                    && Vector2.Distance(Game.Player.Position, returnPortal) > 1.3f);
                yield return HubCompassStable(expected.target, expected.target + " arrival does not bounce");
                WorldLayerShot("hub-compass-" + expected.direction + "-destination");
                if (!returnExists || Game.World.MapId != expected.target || Game.Flow.IsTransitioning) continue;

                Vector2 returnDirection = (returnPortal - Game.Player.Position).normalized;
                yield return HubCompassWalk(input, expected.target, "undergate", returnDirection, expected.target + " reciprocal return");
                if (Game.World.MapId != "undergate" || Game.Flow.IsTransitioning) continue;
                Game.Player.Health.SetInvulnerable(600);
                DCheck(expected.target + " return arrives at the correct hub side", Vector2.Distance(Game.Player.Position, expectedArrival) < .1f
                    && Game.World.IsFree(Game.Player.Position) && Vector2.Distance(Game.Player.Position, expected.portal) > 1.3f);
                yield return HubCompassStable("undergate", expected.target + " return does not bounce");
            }
            input.Move = Vector2.zero;
            DCheck("hub compass verification stays muted", AudioListener.volume == 0);
            Log($"HUB COMPASS RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }

        IEnumerator HubCompassWalk(ScriptedInput input, string from, string to, Vector2 direction, string label)
        {
            Vector2 start = Game.Player.Position; float distance = 0, deadline = Time.unscaledTime + 6;
            input.Move = direction;
            while (Game.World.MapId == from && !Game.Flow.IsTransitioning && Time.unscaledTime < deadline)
            {
                distance = Mathf.Max(distance, Vector2.Distance(start, Game.Player.Position));
                yield return null;
            }
            // Held movement would otherwise continue during both fades and after Spawn.
            input.Move = Vector2.zero;
            if (Game.World.MapId == from) distance = Mathf.Max(distance, Vector2.Distance(start, Game.Player.Position));
            DCheck(label + " walking reaches a live portal trigger", distance > .5f && (Game.Flow.IsTransitioning || Game.World.MapId != from));
            deadline = Time.unscaledTime + 30;
            while (Game.Flow.IsTransitioning && Time.unscaledTime < deadline) yield return null;
            yield return Wait(.2f);
            DCheck(label + " walking selects the exact destination", Game.World.MapId == to && !Game.Flow.IsTransitioning);
            Log(label + " walking distance=" + distance + " arrival=" + Game.World.MapId + " " + Game.Player.Position);
        }

        IEnumerator HubCompassStable(string expectedMap, string label)
        {
            bool stable = true; float deadline = Time.unscaledTime + .75f;
            while (Time.unscaledTime < deadline)
            {
                stable &= Game.World.MapId == expectedMap && !Game.Flow.IsTransitioning;
                yield return null;
            }
            DCheck(label, stable && Game.World.MapId == expectedMap && !Game.Flow.IsTransitioning && Game.World.IsFree(Game.Player.Position));
        }

        void SurfaceAtlasCompassAudit(RectTransform chart)
        {
            Canvas.ForceUpdateCanvases();
            var nodes = chart.GetComponentsInChildren<Image>().Where(i => i.enabled && i.name.StartsWith("Node_")).ToArray();
            var hub = nodes.FirstOrDefault(n => n.name == "Node_undergate");
            DCheck("atlas compass has the central undergate node", hub != null);
            if (hub == null) return;
            Vector2 size = chart.rect.size, hubCenter = PortalAtlasLocalBounds(hub.rectTransform, chart).center;
            Vector2 Normalize(Vector2 delta) => new Vector2(delta.x / size.x, delta.y / size.y);
            Vector2 offset = Normalize(hubCenter - chart.rect.center);
            DCheck("undergate remains near the center of the atlas", Mathf.Abs(offset.x) <= .1f && Mathf.Abs(offset.y) <= .1f);
            foreach (var expected in HubCompassExpectations)
            {
                var town = nodes.FirstOrDefault(n => n.name == "Node_" + expected.target);
                Vector2 delta = town != null ? Normalize(PortalAtlasLocalBounds(town.rectTransform, chart).center - hubCenter) : Vector2.zero;
                Vector2 tangent = new Vector2(-expected.outward.y, expected.outward.x);
                DCheck(expected.target + " town is " + expected.direction + " of undergate", town != null
                    && Vector2.Dot(delta, expected.outward) > .12f && Vector2.Dot(delta, expected.outward) > Mathf.Abs(Vector2.Dot(delta, tangent)));
                var fields = AtlasFieldExpectations.First(r => r.town == expected.target).fields;
                Vector2 cluster = Vector2.zero; bool complete = true;
                foreach (string id in fields)
                {
                    var node = nodes.FirstOrDefault(n => n.name == "Node_" + id);
                    Vector2 fieldDelta = node != null ? Normalize(PortalAtlasLocalBounds(node.rectTransform, chart).center - hubCenter) : Vector2.zero;
                    DCheck(id + " stays in the " + expected.direction + " biome", node != null && Vector2.Dot(fieldDelta, expected.outward) > .015f);
                    cluster += fieldDelta; complete &= node != null;
                }
                cluster /= fields.Length;
                DCheck(expected.target + " field cluster is " + expected.direction + " of undergate", complete
                    && Vector2.Dot(cluster, expected.outward) > .12f && Vector2.Dot(cluster, expected.outward) > Mathf.Abs(Vector2.Dot(cluster, tangent)));
            }
        }

        IEnumerator AtlasFieldRun()
        {
            yield return Wait(1); Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "지도 표기 검증"); yield return Wait(1.5f);
            Game.Player.Input = new ScriptedInput(); Game.Player.Health.SetInvulnerable(600);
            AudioListener.volume = 0; Game.Audio?.SetVolumes(0, 0); dgnPassed = dgnFailed = 0;
            ApplyRequestedResolution(); yield return Wait(.3f);
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-screen-width" && int.TryParse(args[i + 1], out int width))
                    DCheck("atlas requested screen width " + width, Screen.width == width);
                if (args[i] == "-screen-height" && int.TryParse(args[i + 1], out int height))
                    DCheck("atlas requested screen height " + height, Screen.height == height);
            }
            Log("ATLAS FIELD resolution: " + Screen.width + "x" + Screen.height);
            var screen = Game.UI.WorldMap;
            Game.Flow.OpenWindow(screen); screen.SelectWorld(WorldLayer.Surface); yield return Wait(.2f);
            DCheck("surface chart is HD and visible", screen.gameObject.activeInHierarchy && screen.AtlasVisible && SurfaceAtlasArtIsHd());
            WorldLayerShot("atlas-fields-before"); yield return null;
            yield return SurfaceAtlasFieldAudit();
            WorldLayerShot("atlas-fields-after"); yield return null;

            var activeWorld = Game.World; string activeMap = activeWorld.MapId;
            Vector2 playerPosition = Game.Player.Position;
            foreach (string name in new[] { "NamePlate_undergate", "Node_undergate" })
            {
                var gateway = screen.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == name);
                var pointer = PortalAtlasPointer(gateway, out var hit);
                DCheck(name + " receives the pointer click", gateway != null && hit == gateway);
                if (gateway != null && hit == gateway)
                    ExecuteEvents.Execute(gateway.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                yield return null;
                DCheck(name + " opens the underground chart", screen.AtlasVisible && screen.SelectedWorld == WorldLayer.Underground);
                if (screen.SelectedWorld == WorldLayer.Underground)
                {
                    DCheck("underground chart retains four floor nodes", new[] { "hollow_descent", "hollow_roots", "hollow_fungal", "hollow_depths" }
                        .All(id => screen.GetComponentsInChildren<Button>().Any(b => b.name == "Node_" + id)));
                    if (name == "NamePlate_undergate") WorldLayerShot("atlas-fields-underground");
                }
                screen.SelectWorld(WorldLayer.Surface); yield return null; Canvas.ForceUpdateCanvases();
            }
            AtlasFieldRouteAudit();
            DCheck("atlas audit preserves active world and player", Game.World == activeWorld && Game.World.MapId == activeMap
                && Vector2.Distance(Game.Player.Position, playerPosition) <= .01f);
            DCheck("atlas field verification muted", AudioListener.volume == 0);
            screen.Close();
            Log($"ATLAS FIELD RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }

        void SurfaceAtlasTransferAudit(RectTransform chart, Image[] nodes)
        {
            var groups = chart.GetComponentsInChildren<RectTransform>().Where(t => t.name.StartsWith("RouteTransfer_")).ToArray();
            DCheck("surface atlas has exactly four directed region transfers", groups.Length == 4);
            var plates = chart.GetComponentsInChildren<Image>().Where(i => i.enabled && i.gameObject.activeInHierarchy && i.name.StartsWith("NamePlate_")).ToArray();
            foreach (var region in AtlasFieldExpectations)
            {
                string from = region.fields[3], to = region.next, name = "RouteTransfer_" + from + "_" + to;
                var group = groups.FirstOrDefault(t => t.name == name);
                var source = nodes.FirstOrDefault(n => n.name == "Node_" + from);
                var destination = nodes.FirstOrDefault(n => n.name == "Node_" + to);
                bool endpointsExist = source != null && destination != null;
                Vector2 a = endpointsExist ? PortalAtlasLocalBounds(source.rectTransform, chart).center : Vector2.zero;
                Vector2 b = endpointsExist ? PortalAtlasLocalBounds(destination.rectTransform, chart).center : Vector2.zero;
                DCheck(from + " has one directed visual transfer to " + to, group != null && groups.Count(t => t.name == name) == 1 && endpointsExist);
                var lines = group != null ? group.GetComponentsInChildren<Image>().Where(i => i.name == "Route").ToArray() : new Image[0];
                bool exactLines = endpointsExist && lines.Length > 0;
                foreach (var line in lines)
                {
                    var rect = line.rectTransform;
                    Vector2 start = chart.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.center.y, 0)));
                    Vector2 end = chart.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.center.y, 0)));
                    exactLines &= line.enabled && line.gameObject.activeInHierarchy && line.color.a > .5f && !line.raycastTarget
                        && Vector2.Distance(start, a) <= .75f && Vector2.Distance(end, b) <= .75f;
                }
                DCheck(from + " rendered transfer connects directly to " + to + " without detours", exactLines);
                var arrows = group != null ? group.GetComponentsInChildren<Image>().Where(i => i.name == "TransferArrow").ToArray() : new Image[0];
                var arrow = arrows.FirstOrDefault();
                DCheck(from + " transfer has one visible nonblocking arrow", arrows.Length == 1 && arrow != null && arrow.enabled
                    && arrow.gameObject.activeInHierarchy && arrow.sprite != null && arrow.color.a > .9f && !arrow.raycastTarget);
                if (arrow == null) continue;
                Rect arrowBounds = PortalAtlasLocalBounds(arrow.rectTransform, chart);
                Vector2 direction = b - a;
                float fraction = direction.sqrMagnitude > 0 ? Vector2.Dot(arrowBounds.center - a, direction) / direction.sqrMagnitude : 0;
                Vector2 arrowForward = chart.InverseTransformVector(arrow.rectTransform.right);
                DCheck(from + " transfer arrow points from stage 4 toward " + to, endpointsExist && fraction > .1f && fraction < .9f
                    && Vector2.Distance(arrowBounds.center, a + direction * fraction) <= .75f
                    && Vector2.Dot(arrowForward.normalized, direction.normalized) > .99f);
                var obscurers = plates.Where(p => arrowBounds.Overlaps(PortalAtlasLocalBounds(p.rectTransform, chart))).Select(p => p.name)
                    .Concat(nodes.Where(n => n.enabled && n.gameObject.activeInHierarchy && n != source && n != destination
                        && arrowBounds.Overlaps(PortalAtlasLocalBounds(n.rectTransform, chart))).Select(n => n.name)).ToArray();
                DCheck(from + " transfer arrow is inside the chart and clear of nameplates and other nodes", PortalAtlasContains(chart.rect, arrowBounds) && obscurers.Length == 0);
                if (obscurers.Length > 0) Log(from + " transfer arrow obscured by: " + string.Join(", ", obscurers));
            }
        }

        static bool SurfaceAtlasArtIsHd()
        {
            var texture = WorldAtlasArt.Chart(WorldLayer.Surface).texture;
            return texture != null && texture.width >= 1600 && texture.height > 0
                && Mathf.Abs(texture.width / (float)texture.height - 16f / 9f) < .01f;
        }

        void AtlasFieldRouteAudit()
        {
            foreach (var region in AtlasFieldExpectations)
            {
                var f = region.fields;
                DCheck(region.town + " retains field stage order", WorldRoutes.Regions.Any(actual =>
                    actual.SequenceEqual(new[] { region.town }.Concat(f).Concat(new[] { region.next }))));
                DCheck(f[0] + " retains exact split connections", WorldRoutes.Neighbors(f[0]).OrderBy(x => x)
                    .SequenceEqual(new[] { region.town, f[1], f[2] }.OrderBy(x => x)));
                foreach (int stage in new[] { 1, 2 })
                    DCheck(f[stage] + " retains exact merge connections", WorldRoutes.Neighbors(f[stage]).OrderBy(x => x)
                        .SequenceEqual(new[] { f[0], f[3] }.OrderBy(x => x)));
                DCheck(f[3] + " retains next-region connections", WorldRoutes.Neighbors(f[3]).OrderBy(x => x)
                    .SequenceEqual(new[] { f[1], f[2], region.next }.OrderBy(x => x)));
                string previousField = AtlasFieldExpectations.First(r => r.next == region.town).fields[3];
                DCheck(region.town + " retains town, field and hub routes", WorldRoutes.Neighbors(region.town).OrderBy(x => x)
                    .SequenceEqual(new[] { previousField, f[0], "undergate" }.OrderBy(x => x)));
            }
            DCheck("undergate retains all towns and underground entrance", WorldRoutes.Neighbors("undergate").OrderBy(x => x)
                .SequenceEqual(new[] { "village", "canyon", "winter", "sunken_sanctum", "hollow_descent" }.OrderBy(x => x)));
            var chain = new[] { "undergate", "hollow_descent", "hollow_roots", "hollow_fungal", "hollow_depths" };
            for (int i = 1; i < chain.Length; i++)
            {
                var expected = i == chain.Length - 1 ? new[] { chain[i - 1] } : new[] { chain[i - 1], chain[i + 1] };
                DCheck(chain[i] + " retains exact linear connections", WorldRoutes.Neighbors(chain[i]).OrderBy(x => x)
                    .SequenceEqual(expected.OrderBy(x => x)));
            }
        }

        IEnumerator SurfaceAtlasFieldAudit()
        {
            var screen = Game.UI.WorldMap;
            var world = Game.World; string activeMap = world.MapId, selectedMap = screen.SelectedMap;
            Vector2 playerPosition = Game.Player.Position;
            Canvas.ForceUpdateCanvases();
            var chart = screen.GetComponentsInChildren<RectTransform>().FirstOrDefault(t => t.name == "Chart_Surface");
            DCheck("surface field chart exists", chart != null);
            if (chart == null) yield break;
            SurfaceAtlasCompassAudit(chart);
            var fields = AtlasFieldExpectations.SelectMany(region => region.fields).ToArray();
            var labels = chart.GetComponentsInChildren<Text>().Where(t => t.name.StartsWith("Stage_")).ToArray();
            var nodes = chart.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Node_")).ToArray();
            var obstacles = chart.GetComponentsInChildren<Graphic>().Where(g => g.enabled && g.gameObject.activeInHierarchy
                && (g.name.StartsWith("NamePlate_") || g.name.StartsWith("Name_") || g.name == "ChartTitle" || g.name == "Compass"
                    || (g.name.StartsWith("Node_") && !fields.Contains(g.name.Substring(5))))).ToArray();
            DCheck("surface chart has all sixteen distinct field labels", fields.Length == 16 && fields.Distinct().Count() == 16
                && labels.Length == 16 && fields.All(id => labels.Count(t => t.name == "Stage_" + id) == 1));
            DCheck("surface chart has all sixteen distinct field nodes", fields.All(id => nodes.Count(n => n.name == "Node_" + id) == 1));
            var chartBounds = PortalAtlasScreenBounds(chart);
            DCheck("surface chart is inside the screen", chartBounds.xMin >= 0 && chartBounds.yMin >= 0
                && chartBounds.xMax <= Screen.width + .5f && chartBounds.yMax <= Screen.height + .5f);
            var inset = Rect.MinMaxRect(chart.rect.xMin + 8, chart.rect.yMin + 8, chart.rect.xMax - 8, chart.rect.yMax - 8);
            var bounds = new Dictionary<string, Rect>();
            foreach (var region in AtlasFieldExpectations)
            {
                for (int stage = 1; stage <= 4; stage++)
                {
                    string id = region.fields[stage - 1], expectedLabel = region.prefix + " " + stage;
                    var node = nodes.FirstOrDefault(n => n.name == "Node_" + id);
                    var button = node != null ? node.GetComponent<Button>() : null;
                    var label = labels.FirstOrDefault(t => t.name == "Stage_" + id);
                    DCheck(id + " atlas node has an active clickable button", node != null && node.enabled && node.raycastTarget
                        && button != null && button.enabled && button.IsInteractable() && button.gameObject.activeInHierarchy);
                    DCheck(id + " atlas label is exactly " + expectedLabel, label != null && label.enabled
                        && label.gameObject.activeInHierarchy && label.text == expectedLabel);
                    DCheck(id + " full atlas label fits its text box", label != null && label.color.a > .9f && label.fontSize >= 14
                        && label.preferredWidth <= label.rectTransform.rect.width + .5f && label.preferredHeight <= label.rectTransform.rect.height + .5f);
                    var sidebar = screen.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Region_" + id);
                    var sidebarLabel = sidebar != null ? sidebar.GetComponentInChildren<Text>() : null;
                    string sidebarText = sidebarLabel != null ? System.Text.RegularExpressions.Regex.Replace(sidebarLabel.text, "<[^>]*>", "") : "";
                    DCheck(id + " sidebar number and name match the atlas", sidebarText.StartsWith("[" + stage + "] " + MapRegistry.Get(id).displayName + "   "));
                    if (node == null) continue;
                    Rect nodeBounds = PortalAtlasLocalBounds(node.rectTransform, chart); bounds[id] = nodeBounds;
                    DCheck(id + " atlas stage label is centered", label != null && label.alignment == TextAnchor.MiddleCenter
                        && Vector2.Distance(nodeBounds.center, PortalAtlasLocalBounds(label.rectTransform, chart).center) <= .75f);
                    DCheck(id + " atlas node and label keep eight-pixel chart margin", PortalAtlasContains(inset, nodeBounds)
                        && label != null && PortalAtlasContains(inset, PortalAtlasLocalBounds(label.rectTransform, chart)));
                    var overlaps = obstacles.Where(p => nodeBounds.Overlaps(PortalAtlasLocalBounds(p.rectTransform, chart))).Select(p => p.name).ToArray();
                    DCheck(id + " atlas node clears town icons, nameplates and title", overlaps.Length == 0);
                    if (overlaps.Length > 0) Log(id + " atlas overlapping objects: " + string.Join(", ", overlaps));
                }
                var f = region.fields; bool complete = f.All(id => bounds.ContainsKey(id));
                Vector2 flow = region.town == "village" ? new Vector2(1, 1).normalized : region.town == "canyon" ? Vector2.right
                    : region.town == "winter" ? Vector2.down : Vector2.left;
                Vector2 Normalize(Vector2 delta) => new Vector2(delta.x / chart.rect.width, delta.y / chart.rect.height);
                Vector2 first = complete ? bounds[f[0]].center : Vector2.zero, fourth = complete ? bounds[f[3]].center : Vector2.zero;
                Vector2 branchCenter = complete ? (bounds[f[1]].center + bounds[f[2]].center) * .5f : Vector2.zero;
                DCheck(region.town + " atlas stage 2 is above stage 3", complete && bounds[f[1]].center.y > bounds[f[2]].center.y);
                DCheck(region.town + " atlas stage 4 follows stage 1 clockwise", complete && Vector2.Dot(Normalize(fourth - first), flow) > .015f);
                DCheck(region.town + " atlas branches follow stage 1 clockwise", complete && Vector2.Dot(Normalize(branchCenter - first), flow) > .015f);
                DCheck(region.town + " atlas stage 4 follows its branches clockwise", complete && Vector2.Dot(Normalize(fourth - branchCenter), flow) > .015f);
                var townNode = nodes.FirstOrDefault(n => n.name == "Node_" + region.town);
                var nextTownNode = nodes.FirstOrDefault(n => n.name == "Node_" + region.next);
                DCheck(region.town + " town is close to its own stage 1", complete && townNode != null
                    && Normalize(first - PortalAtlasLocalBounds(townNode.rectTransform, chart).center).magnitude <= .24f);
                DCheck(f[3] + " is immediately followed by nearby " + region.next, complete && nextTownNode != null
                    && Normalize(PortalAtlasLocalBounds(nextTownNode.rectTransform, chart).center - fourth).magnitude <= .24f);
            }
            SurfaceAtlasTransferAudit(chart, nodes);
            for (int i = 0; i < fields.Length; i++)
            {
                string id = fields[i];
                var overlaps = fields.Skip(i + 1).Where(other => bounds.ContainsKey(id) && bounds.ContainsKey(other) && bounds[id].Overlaps(bounds[other])).ToArray();
                DCheck(id + " atlas node does not overlap another field", overlaps.Length == 0);
                if (overlaps.Length > 0) Log(id + " atlas overlapping fields: " + string.Join(", ", overlaps));
            }
            var eventSystem = EventSystem.current;
            DCheck("surface atlas click audit has an event system", eventSystem != null);
            var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            bool syntheticPointer = Application.isBatchMode
                || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-atlasFieldCheck") >= 0;
            string pointerMode = syntheticPointer ? "synthetic raycast-resolved pointer handlers" : "queued mouse input";
            Log("ATLAS FIELD pointer mode: " + pointerMode + "; physical OS hover is not asserted by the synthetic mode.");
            Log("ATLAS FIELD input diagnostics: applicationFocused=" + Application.isFocused + "; eventSystemFocused="
                + (eventSystem != null && eventSystem.isFocused) + "; module="
                + (eventSystem != null && eventSystem.currentInputModule != null ? eventSystem.currentInputModule.GetType().Name : "none")
                + "; mouseEnabled=" + mouse.enabled + "; mouseCanRunInBackground=" + mouse.canRunInBackground
                + "; backgroundBehavior=" + InputSystem.settings.backgroundBehavior);
            foreach (var region in AtlasFieldExpectations)
            for (int index = 0; index < region.fields.Length; index++)
            {
                string id = region.fields[index]; int stage = index + 1;
                var button = screen.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Node_" + id);
                var pointer = PortalAtlasPointer(button, out var hitButton);
                DCheck(id + " atlas label receives the pointer click", button != null && hitButton == button);
                if (button != null)
                {
                    if (!syntheticPointer)
                    {
                        InputSystem.QueueDeltaStateEvent(mouse.position, Vector2.zero); yield return null; yield return null;
                    }
                    var legend = screen.GetComponentsInChildren<Text>().FirstOrDefault(t => t.name == "Legend");
                    string restingLegend = legend != null ? legend.text : null;
                    var pointerTarget = pointer != null ? pointer.pointerCurrentRaycast.gameObject : null;
                    if (syntheticPointer)
                    {
                        // Hidden batch windows may disable mouse devices on focus loss. Exercise
                        // the real UI event handlers at the top raycast hit without claiming OS input.
                        if (hitButton == button)
                        {
                            pointer.pointerEnter = pointerTarget;
                            ExecuteEvents.ExecuteHierarchy(pointerTarget, pointer, ExecuteEvents.pointerEnterHandler);
                        }
                    }
                    else
                    {
                        InputSystem.QueueDeltaStateEvent(mouse.position, PortalAtlasScreenBounds((RectTransform)button.transform).center);
                        yield return null; yield return null;
                    }
                    var zone = HuntingGrounds.Get(id);
                    string expectedHover = region.name + " · 사냥터 " + stage + "  " + MapRegistry.Get(id).displayName
                        + (zone != null ? $"   Lv.{zone.minLevel}~{zone.maxLevel}" : "   안전 지역") + "   ·  클릭하여 지역 지도 보기";
                    DCheck(id + " pointer hover shows exact region, number and field name", legend != null && legend.text == expectedHover);
                    if (legend != null && legend.text != expectedHover) Log(id + " actual atlas hover: " + legend.text);
                    if (syntheticPointer)
                    {
                        if (hitButton == button)
                        {
                            ExecuteEvents.ExecuteHierarchy(pointerTarget, pointer, ExecuteEvents.pointerExitHandler);
                            pointer.pointerEnter = null;
                        }
                    }
                    else
                    {
                        InputSystem.QueueDeltaStateEvent(mouse.position, Vector2.zero); yield return null; yield return null;
                    }
                    DCheck(id + " pointer exit restores the atlas legend", legend != null && !string.IsNullOrEmpty(restingLegend)
                        && restingLegend != expectedHover && legend.text == restingLegend);
                    // Dispatch through the raycast target's actual click handler, not its stored action.
                    if (hitButton == button) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    yield return null;
                }
                DCheck(id + " atlas click selects the exact field", button != null && screen.SelectedMap == id && !screen.AtlasVisible);
                DCheck(id + " atlas click preserves the active world and player", Game.World == world && Game.World.MapId == activeMap
                    && Vector2.Distance(Game.Player.Position, playerPosition) <= .01f);
                screen.SelectWorld(WorldLayer.Surface); yield return null; Canvas.ForceUpdateCanvases();
                DCheck(id + " surface atlas reopens after selection", screen.AtlasVisible && screen.SelectedWorld == WorldLayer.Surface);
            }
            if (!syntheticPointer)
            {
                InputSystem.QueueDeltaStateEvent(mouse.position, Vector2.zero); yield return null; yield return null;
            }
            screen.SelectMap(selectedMap); screen.SelectWorld(WorldLayer.Surface); yield return null;
        }

        static PointerEventData PortalAtlasPointer(Button button, out Button hitButton)
        {
            hitButton = null;
            var eventSystem = EventSystem.current;
            if (button == null || eventSystem == null) return null;
            var pointer = new PointerEventData(eventSystem)
            { position = PortalAtlasScreenBounds((RectTransform)button.transform).center, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); eventSystem.RaycastAll(pointer, hits);
            if (hits.Count > 0)
            {
                pointer.pointerCurrentRaycast = hits[0];
                hitButton = hits[0].gameObject.GetComponentInParent<Button>();
            }
            return pointer;
        }

        static bool PortalAtlasContains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .01f && inner.yMin >= outer.yMin - .01f
            && inner.xMax <= outer.xMax + .01f && inner.yMax <= outer.yMax + .01f;

        static Rect PortalAtlasLocalBounds(RectTransform rect, RectTransform chart)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = chart.InverseTransformPoint(corners[0]), max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = chart.InverseTransformPoint(corners[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static Rect PortalAtlasScreenBounds(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
