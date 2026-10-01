using System;
using System.Collections;
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
            int count = 0;
            foreach (var house in Game.World.ObjectsRoot.GetComponentsInChildren<BoxCollider2D>())
            {
                if (house.name != "House") continue;
                count++;
                var sprite = house.GetComponent<SpriteRenderer>().sprite;
                var door = house.GetComponentInChildren<DialogueInteractable>();
                if (Mathf.Abs(sprite.pixelsPerUnit - 120f) > .01f || door == null)
                    throw new Exception("HOUSE FAIL: incorrect sprite scale or missing entrance");
                // Exclude decorative roof overhangs: solid walls must not intersect yard props.
                foreach (var other in Game.World.ObjectsRoot.GetComponentsInChildren<BoxCollider2D>())
                {
                    if (other == house || other.isTrigger || other.GetComponentInParent<NpcController>() != null) continue;
                    if (house.bounds.Intersects(other.bounds))
                        throw new Exception("HOUSE FAIL: footprint overlaps " + other.name);
                }
                var entrance = door.InteractionPoint;
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
                Log($"PASS house {count}: door reachable, steps walkable, wall solid, yard clear; position={house.transform.position}");
                // Stand beside the opening so the player's height can be compared directly with the lintel.
                yield return Teleport(entrance + new Vector2(-1.05f, -.15f));
                var center = (Vector2)house.transform.position + Vector2.up * 3.5f;
                RenderRegion(Path.Combine(folder, $"house_scale_{count}.png"),
                    new Rect(center.x - 6.5f, center.y - 5f, 13f, 10f), 96);
            }
            if (count != 3) throw new Exception("HOUSE FAIL: expected three village houses");
            yield return RenderMap("town_scaled", new[] { ("houses", 13f, 27.5f) });
        }
    }
}
