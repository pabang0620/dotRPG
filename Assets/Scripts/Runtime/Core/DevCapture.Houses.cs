using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator HouseScaleShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1f);
            // Check that the expanded neighbourhood is connected to the original plaza at player clearance.
            var reachable = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            var start = Vector2Int.FloorToInt(Game.World.PlayerSpawn);
            reachable.Add(start); queue.Enqueue(start);
            var visited = new HashSet<Vector2Int>(reachable);
            var steps = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var step in steps)
                {
                    var next = at + step;
                    if (!visited.Add(next) || !Game.World.IsFree((Vector2)next + Vector2.one * .5f)) continue;
                    reachable.Add(next); queue.Enqueue(next);
                }
            }
            if (!reachable.Contains(new Vector2Int(56, 21))) throw new Exception("HOUSE FAIL: forest exit disconnected");
            int count = 0;
            foreach (var house in Game.World.ObjectsRoot.GetComponentsInChildren<BoxCollider2D>())
            {
                var renderer = house.GetComponent<SpriteRenderer>();
                var sprite = renderer != null ? renderer.sprite : null;
                var art = VillageBuildingArt.FromSprite(sprite);
                if (art == null) continue;
                count++;
                var door = house.GetComponentInChildren<Interactable>();
                if (Mathf.Abs(sprite.pixelsPerUnit - art.Ppu) > .01f || door == null)
                    throw new Exception("HOUSE FAIL: incorrect sprite scale or missing entrance");
                // Exclude decorative roof overhangs: solid walls must not intersect yard props.
                foreach (var other in Game.World.ObjectsRoot.GetComponentsInChildren<BoxCollider2D>())
                {
                    if (other == house || other.isTrigger || other.GetComponentInParent<NpcController>() != null) continue;
                    if (house.bounds.Intersects(other.bounds))
                        throw new Exception("HOUSE FAIL: footprint overlaps " + other.name);
                }
                var entrance = door.InteractionPoint;
                if (!reachable.Contains(Vector2Int.FloorToInt(entrance + Vector2.down * .6f)))
                    throw new Exception("HOUSE FAIL: plaza route blocked to " + art.Asset);
                yield return Teleport(entrance + Vector2.down * .6f);
                if (Interactable.FindBest(Game.Player.Position, Vector2.up, 1f) != door)
                    throw new Exception("HOUSE FAIL: door is not reachable from its front steps");
                Game.Input.MoveOverride = Vector2.up;
                yield return Wait(.7f);
                Game.Input.MoveOverride = Vector2.zero;
                yield return Wait(.1f);
                if (Game.Player.Position.y >= house.bounds.min.y || Game.Player.Position.y < entrance.y - .5f)
                    throw new Exception("HOUSE FAIL: steps are blocked or wall can be crossed");
                if (Game.Player.GetComponent<CharacterAnimator>().Renderer.sortingOrder <= house.GetComponent<SpriteRenderer>().sortingOrder)
                    throw new Exception("HOUSE FAIL: a visitor on the steps is hidden by the house");
                if (count == 2)
                    RenderRegion(Path.Combine(folder, "house_on_steps.png"),
                        new Rect(house.transform.position.x - 6.5f, house.transform.position.y - 1.5f, 13f, 10f), 96);
                Log($"PASS {art.Asset}: plaza route, door reachable, steps walkable, wall solid, yard clear; position={house.transform.position}");
                if (door is ServiceDoor serviceDoor)
                {
                    // End-to-end room/NPC/return travel is covered by AtlasChecks; keep this art pass on the same world.
                    var room = MapRegistry.Get(MapRegistry.InteriorFor(Game.World.MapId, serviceDoor.Service));
                    if (room == null || !room.IsInterior || room.exteriorMap != Game.World.MapId)
                        throw new Exception("HOUSE FAIL: missing service interior " + art.Asset);
                    Log("PASS service interior registered " + art.Asset);
                }
                // Stand beside the opening so the player's height can be compared directly with the lintel.
                yield return Teleport(entrance + new Vector2(-1.05f, -.15f));
                var center = (Vector2)house.transform.position + Vector2.up * 3.5f;
                RenderRegion(Path.Combine(folder, art.Asset + ".png"),
                    new Rect(center.x - 6.5f, center.y - 5f, 13f, 10f), 96);
            }
            if (count != 7) throw new Exception("HOUSE FAIL: expected seven village buildings");
            yield return RenderMap("town_redesigned", new[] { ("houses", 13f, 27.5f), ("north", 35f, 42f), ("market", 31f, 31f) });
        }
    }
}
