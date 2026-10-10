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
            DCheck("surface chart is HD and visible", Game.UI.WorldMap.gameObject.activeInHierarchy && WorldAtlasArt.Chart(WorldLayer.Surface).texture.width == 2560);
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

        IEnumerator SurfaceAtlasFieldAudit()
        {
            var screen = Game.UI.WorldMap;
            var world = Game.World; string activeMap = world.MapId, selectedMap = screen.SelectedMap;
            Vector2 playerPosition = Game.Player.Position;
            Canvas.ForceUpdateCanvases();
            var chart = screen.GetComponentsInChildren<RectTransform>().FirstOrDefault(t => t.name == "Chart_Surface");
            DCheck("surface field chart exists", chart != null);
            if (chart == null) yield break;
            var fields = WorldRoutes.Regions.SelectMany(region => region.Skip(1).Take(4)).ToArray();
            var labels = chart.GetComponentsInChildren<Text>().Where(t => t.name.StartsWith("Stage_")).ToArray();
            var nodes = chart.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Node_")).ToArray();
            var plates = chart.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("NamePlate_") && i.enabled && i.raycastTarget).ToArray();
            DCheck("surface chart has all sixteen distinct field labels", fields.Length == 16 && fields.Distinct().Count() == 16
                && labels.Length == 16 && fields.All(id => labels.Count(t => t.name == "Stage_" + id) == 1));
            DCheck("surface chart has all sixteen distinct field nodes", fields.All(id => nodes.Count(n => n.name == "Node_" + id) == 1));
            var bounds = new Dictionary<string, Rect>();
            foreach (var region in WorldRoutes.Regions)
            {
                for (int stage = 1; stage <= 4; stage++)
                {
                    string id = region[stage];
                    var node = nodes.FirstOrDefault(n => n.name == "Node_" + id);
                    var button = node != null ? node.GetComponent<Button>() : null;
                    var label = labels.FirstOrDefault(t => t.name == "Stage_" + id);
                    DCheck(id + " atlas node has an active clickable button", node != null && node.enabled && node.raycastTarget
                        && button != null && button.enabled && button.IsInteractable() && button.gameObject.activeInHierarchy);
                    DCheck(id + " atlas label is stage " + stage, label != null && label.enabled && label.text == stage.ToString());
                    if (node == null) continue;
                    Rect nodeBounds = PortalAtlasScreenBounds(node.rectTransform); bounds[id] = nodeBounds;
                    DCheck(id + " atlas stage label is centered", label != null && label.alignment == TextAnchor.MiddleCenter
                        && Vector2.Distance(nodeBounds.center, PortalAtlasScreenBounds(label.rectTransform).center) <= .75f);
                    var overlaps = plates.Where(p => p.name != "NamePlate_" + id
                        && nodeBounds.Overlaps(PortalAtlasScreenBounds(p.rectTransform))).Select(p => p.name).ToArray();
                    DCheck(id + " atlas node clear of unrelated nameplate hitboxes", overlaps.Length == 0);
                    if (overlaps.Length > 0) Log(id + " atlas overlapping nameplates: " + string.Join(", ", overlaps));
                }
                bool complete = region.Skip(1).Take(4).All(id => bounds.ContainsKey(id));
                DCheck(region[0] + " atlas stage 2 is above stage 1", complete && bounds[region[2]].center.y > bounds[region[1]].center.y);
                DCheck(region[0] + " atlas stage 3 is below stage 1", complete && bounds[region[3]].center.y < bounds[region[1]].center.y);
                DCheck(region[0] + " atlas stage 4 is right of stages 1 2 3", complete
                    && region.Skip(1).Take(3).All(id => bounds[region[4]].center.x > bounds[id].center.x));
            }
            var eventSystem = EventSystem.current;
            DCheck("surface atlas click audit has an event system", eventSystem != null);
            foreach (string id in fields)
            {
                var button = screen.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Node_" + id);
                if (button != null)
                {
                    // Raycast the real screen position before invoking the UI button, so
                    // a visually correct number hidden behind another hitbox still fails.
                    var hits = new List<RaycastResult>();
                    if (eventSystem != null)
                    {
                        var pointer = new PointerEventData(eventSystem)
                        { position = PortalAtlasScreenBounds((RectTransform)button.transform).center };
                        eventSystem.RaycastAll(pointer, hits);
                    }
                    var firstHit = hits.Count > 0 ? hits[0].gameObject : null;
                    var hitButton = firstHit != null ? firstHit.GetComponentInParent<Button>() : null;
                    DCheck(id + " atlas number receives the pointer click", hitButton == button);
                    if (hitButton != button) Log(id + " atlas first pointer hit: " + (firstHit != null ? firstHit.name : "none"));
                    button.onClick.Invoke(); yield return null;
                }
                DCheck(id + " atlas click selects the exact field", button != null && screen.SelectedMap == id && !screen.AtlasVisible);
                DCheck(id + " atlas click preserves the active world and player", Game.World == world && Game.World.MapId == activeMap
                    && Vector2.Distance(Game.Player.Position, playerPosition) <= .01f);
                screen.SelectWorld(WorldLayer.Surface); yield return null; Canvas.ForceUpdateCanvases();
                DCheck(id + " surface atlas reopens after selection", screen.AtlasVisible && screen.SelectedWorld == WorldLayer.Surface);
            }
            screen.SelectMap(selectedMap); screen.SelectWorld(WorldLayer.Surface); yield return null;
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
