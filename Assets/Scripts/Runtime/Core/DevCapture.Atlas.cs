using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator AtlasChecks()
        {
            Game.World.Load(MapRegistry.Village); Game.Player.Place(Game.World.PlayerSpawn, Facing.Down);
            Game.Camera.SetTarget(Game.Player.transform, true); yield return Wait(.15f);
            var fountain = Game.World.ObjectsRoot.GetComponentInChildren<FountainWater>();
            DCheck("plaza fountain has real water animation", fountain != null && fountain.enabled);
            if (fountain != null && fountain.enabled)
            {
                var original = Game.Art.Get("town_fountain_0");
                var source = original.texture.GetPixels32();
                int unique = 0; bool masonry = true;
                var hashes = new System.Collections.Generic.HashSet<int>();
                for (int f = 0; f < FountainWater.FrameCount; f++)
                {
                    var frame = fountain.FrameAt(f); var pixels = frame.texture.GetPixels32(); int hash = 17;
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        unchecked { hash = hash * 31 + pixels[p].GetHashCode(); }
                        if (!FountainWater.IsWater(source[p]) && !source[p].Equals(pixels[p])) masonry = false;
                    }
                    hashes.Add(hash);
                    DCheck("fountain fixed pivot " + f, frame.pivot == original.pivot && frame.pixelsPerUnit == original.pixelsPerUnit && frame.texture.filterMode == FilterMode.Point);
                }
                unique = hashes.Count;
                DCheck("sixteen distinct fountain frames", unique == 16);
                DCheck("fountain masonry and plants stay fixed", masonry);
                int before = fountain.CurrentFrame; yield return Wait(.2f);
                DCheck("fountain animation advances in play", fountain.CurrentFrame != before);
                var pos = (Vector2)fountain.transform.position;
                RenderRegion(Path.Combine(folder, "06_fountain.png"), new Rect(pos.x - 2, pos.y - .1f, 4, 3.5f), 160);
                Game.Player.Place(pos + Vector2.down * 2, Facing.Up); Game.Camera.SetTarget(Game.Player.transform, true);
            }
            yield return PresentationShot("07_minimap_frame");
            Game.Flow.OpenWindow(Game.UI.WorldMap); yield return null;
            string worldId = Game.World.MapId; var playerPos = Game.Player.Position;
            var atlas = Game.UI.WorldMap;
            DCheck("atlas shows live NPCs and indoor entrances", atlas.NpcCount == NpcController.All.Count + Game.World.ObjectsRoot.GetComponentsInChildren<ServiceDoor>().Length);
            atlas.SelectNpc(0); yield return PresentationShot("08_atlas_current");
            atlas.SelectMap("forest_depths"); yield return PresentationShot("08b_atlas_unvisited");
            DCheck("unvisited preview remains explicitly schematic", !WorldAtlas.Get("forest_depths").rendered);
            foreach (var info in MapRegistry.All.Concat(MapRegistry.Interiors).ToArray())
            {
                atlas.SelectMap(info.id);
                DCheck("atlas browse without teleport " + info.id, atlas.SelectedMap == info.id && Game.World.MapId == worldId && Game.Player.Position == playerPos && WorldAtlas.Get(info.id).texture != null);
            }
            atlas.SelectMap(MapRegistry.Winter); atlas.SelectNpc(0); yield return PresentationShot("09_atlas_winter_preview");
            Game.Flow.CloseInventory(); yield return null;

            foreach (string town in new[] { MapRegistry.Village, MapRegistry.Canyon, MapRegistry.Winter })
            {
                Game.World.Load(town); Game.Session.MapId = town; Game.Player.Place(Game.World.PlayerSpawn, Facing.Down);
                Game.Camera.SetTarget(Game.Player.transform, true); yield return Wait(.15f);
                DCheck(town + " has three service doors", Game.World.ObjectsRoot.GetComponentsInChildren<ServiceDoor>().Length == 3);
                DCheck(town + " no duplicate outdoor shop NPCs", !NpcController.Services.Any(n => MapRegistry.IsIndoorService(n.Definition.service)));
                DCheck(town + " weekday guide available", Game.World.DungeonGuidePosition.HasValue && NpcController.Services.Any(n => n.Definition.service == NpcService.Dungeon));
                if (town == MapRegistry.Winter)
                {
                    var guide = NpcController.Services.FirstOrDefault(n => n.Definition.service == NpcService.Dungeon);
                    if (guide != null) { guide.Interact(Game.Player); yield return null; DCheck("winter guide opens weekday dungeon", Game.UI.Top == Game.UI.Dungeon && !Game.UI.Dungeon.IsRaidTab); Game.Flow.CloseInventory(); }
                    RenderRegion(Path.Combine(folder, "10_winter_services.png"), Game.World.Bounds, 24);
                }
                foreach (var service in MapRegistry.IndoorServices)
                {
                    var door = Game.World.ObjectsRoot.GetComponentsInChildren<ServiceDoor>().FirstOrDefault(d => d.Service == service);
                    if (door == null) continue;
                    Vector2 entrance = door.transform.position;
                    Game.Player.Place(entrance + Vector2.down * .8f, Facing.Up);
                    DCheck(town + service + " entrance approachable", Game.World.IsFree(Game.Player.Position));
                    door.Interact(Game.Player);
                    float deadline = Time.realtimeSinceStartup + 12;
                    while (Game.Flow.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return Wait(.08f);
                    string room = MapRegistry.InteriorFor(town, service);
                    DCheck(room + " door enters room", Game.World.MapId == room && Game.Session.MapId == room && Game.World.Bounds.size == new Vector2(5, 5));
                    DCheck(room + " spawn walkable and outside exit", Game.World.IsFree(Game.Player.Position) && Game.Player.Position.y > 1);
                    var save = Game.Session.Capture(Game.Player.Position, Facing.Up);
                    var restored = new GameSession(); restored.Restore(save, Game.Config);
                    DCheck(room + " local save restores inside room", restored.MapId == room && restored.StartPosition == Game.Player.Position);
                    DCheck(room + " return scroll knows parent village", HuntingGrounds.HomeOf(room) == town);
                    DCheck(room + " one matching keeper", NpcController.Services.Count == 1 && NpcController.Services[0].Definition.service == service);
                    DCheck(room + " aisle to counter walkable", Game.World.IsFree(new Vector2(2.5f, 1.8f)));
                    if (NpcController.Services.Count > 0)
                    {
                        var npc = NpcController.Services[0];
                        Game.Player.Place(new Vector2(2.5f, 1.95f), Facing.Up);
                        DCheck(room + " keeper in interaction range", Interactable.FindBest(Game.Player.Position, Vector2.up, 1.4f) == npc);
                        bool questTalk = Game.Quest.HasQuestTalk(npc.Definition.npcId);
                        npc.Interact(Game.Player); yield return null;
                        MenuScreen expected = service == NpcService.Shop ? (MenuScreen)Game.UI.Shop : service == NpcService.Blacksmith ? Game.UI.Enhance : Game.UI.Storage;
                        DCheck(room + " keeper serves player", questTalk || Game.UI.Top == expected);
                        if (questTalk) Game.Dialogue.Abort(); else Game.Flow.CloseInventory();
                        if (Game.State.Current != GameState.Playing) Game.Flow.Resume();
                    }
                    if (town == MapRegistry.Winter)
                    {
                        yield return Wait(2.5f);
                        yield return PresentationShot("11_room_" + service);
                    }
                    // Exercise the real exit trigger, not a direct map load.
                    Game.Player.Place(new Vector2(2.5f, .45f), Facing.Down); yield return Wait(.12f);
                    deadline = Time.realtimeSinceStartup + 12;
                    while (Game.Flow.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return Wait(.08f);
                    DCheck(room + " exit returns to its own door", Game.World.MapId == town && Vector2.Distance(Game.Player.Position, entrance) < 2.6f && Game.World.IsFree(Game.Player.Position));
                    if (Game.World.MapId != town) { Game.World.Load(town); Game.Player.Place(Game.World.PlayerSpawn, Facing.Down); }
                }
            }
            Game.Flow.OpenWindow(Game.UI.WorldMap); atlas.SelectMap(MapRegistry.Winter); atlas.SelectNpc(0); yield return PresentationShot("12_atlas_winter_actual"); Game.Flow.CloseInventory();
        }
    }
}
