using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        int depthPass, depthFail;

        /// <summary>
        /// -dotrpgDepth: on every 32px map, walks the player (real movement input) into a free-standing
        /// tree and a building from the south and from the north. Checks that only the trunk / wall base
        /// blocks, that the space behind the crown / roof is walkable, and that the character is drawn
        /// behind the object when it stands behind it and in front of it otherwise. Also checks each map's
        /// sun shadows (<see cref="ShadowCheck"/>).
        /// </summary>
        IEnumerator DepthShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.5f);
            foreach (var mapId in new[] { MapRegistry.Village, MapRegistry.Forest, MapRegistry.Canyon, MapRegistry.Winter })
            {
                if (Game.World.MapId != mapId)
                {
                    Game.Flow.TravelTo(mapId);
                    yield return Wait(2.5f);
                }
                Log($"--- map {Game.World.MapId}");
                ShadowCheck();
                var tree = PickObstacle(new[] { "Tree" });
                if (tree != null) yield return WalkAround(tree, mapId + "_tree");
                else Log("no free-standing tree with open space on both sides");
                var building = PickObstacle(new[] { "House", "Inn", "Barn", "ChiefHall", "GeneralStore", "Smithy", "Warehouse" });
                if (building != null) yield return WalkAround(building, mapId + "_building");
                else Log("no building with open space on both sides");
            }
            Game.Input.MoveOverride = null;
            Log($"depth summary: pass={depthPass} fail={depthFail}");
        }

        /// <summary>
        /// Sun shadows on the current map: every tree and rock casts one, no character does (they keep
        /// their own round shadow), each falls to the lower right of its object, and all of them lie under
        /// every depth-sorted object and character.
        /// </summary>
        void ShadowCheck()
        {
            var root = Game.World.ObjectsRoot;
            int casters = 0, wrongWay = 0, onCharacters = 0;
            foreach (var cast in root.GetComponentsInChildren<CastShadow>(true))
            {
                casters++;
                if (cast.GetComponentInParent<NpcController>() != null || cast.GetComponentInParent<EnemyController>() != null) onCharacters++;
                var shadow = cast.GetComponent<SpriteRenderer>();
                var owner = cast.transform.parent.GetComponent<SpriteRenderer>();
                if (shadow.sprite == null || owner == null || !owner.enabled) continue;
                Bounds s = shadow.bounds, o = owner.bounds;
                if (!(s.center.x > o.center.x && s.center.y < o.center.y)) wrongWay++;
            }
            int nodes = 0, nodesShadowed = 0;
            foreach (var node in root.GetComponentsInChildren<ResourceNode>())
            {
                nodes++;
                if (node.GetComponentInChildren<CastShadow>(true) != null) nodesShadowed++;
            }
            int lowestObject = int.MaxValue;
            foreach (var sort in root.GetComponentsInChildren<YSort>())
                foreach (var r in sort.GetComponentsInChildren<SpriteRenderer>())
                    if (r.GetComponent<CastShadow>() == null) lowestObject = Mathf.Min(lowestObject, r.sortingOrder);
            bool under = CastShadow.SortingOrder < lowestObject;
            Report($"sun shadows: {casters} objects cast one, trees/rocks {nodesShadowed}/{nodes}, on characters {onCharacters}, wrong direction {wrongWay}, under every object={under} (shadow {CastShadow.SortingOrder}, lowest object {lowestObject})",
                casters > 0 && nodesShadowed == nodes && onCharacters == 0 && wrongWay == 0 && under);
        }

        /// <summary>The first solid object with one of the names that the player can walk up to from both the south and the north.</summary>
        static BoxCollider2D PickObstacle(string[] names)
        {
            foreach (var box in Game.World.ObjectsRoot.GetComponentsInChildren<BoxCollider2D>())
            {
                if (box.isTrigger || System.Array.IndexOf(names, box.gameObject.name) < 0) continue;
                var b = box.bounds;
                float x = b.center.x;
                if (PathFree(x, b.min.y - 1.6f, b.min.y - 0.55f) && PathFree(x, b.max.y + 0.1f, b.max.y + 1.6f)) return box;
            }
            return null;
        }

        static bool PathFree(float x, float y0, float y1)
        {
            for (float y = y0; y <= y1 + 0.001f; y += 0.2f)
                if (!Game.World.IsFree(new Vector2(x, y))) return false;
            return true;
        }

        IEnumerator WalkAround(BoxCollider2D box, string name)
        {
            var b = box.bounds;
            // A monster wandering onto the test path would stop the player early; clear the area first.
            foreach (var e in EnemyController.Active.ToArray())
                if (e != null && Vector2.Distance(e.Position, b.center) < 8f) e.gameObject.SetActive(false);
            var objectRenderer = box.GetComponent<SpriteRenderer>();
            var body = Game.Player.GetComponentInChildren<CharacterAnimator>().Renderer;
            float x = b.center.x;
            Log($"{name}: '{objectRenderer.sprite.name}' base y={box.transform.position.y:0.00} solid y {b.min.y:0.00}..{b.max.y:0.00} width {b.size.x:0.00}");

            // From the south: walk north into it. The base should stop the player, drawn in front.
            yield return Teleport(new Vector2(x, b.min.y - 1.5f));
            yield return Walk(Vector2.up, 1.2f);
            float southY = Game.Player.Position.y;
            bool blockedSouth = southY < b.min.y - 0.2f;
            bool frontDrawn = body.sortingOrder > objectRenderer.sortingOrder;
            Report($"{name} from south: stopped at y={southY:0.00} (base starts {b.min.y:0.00}) blocked={blockedSouth}; drawn in front={frontDrawn} (player {body.sortingOrder} vs object {objectRenderer.sortingOrder})", blockedSouth && frontDrawn);
            yield return Shot("depth_" + name + "_front");

            // From the north: walk south until the base stops the player, standing behind the crown / roof.
            yield return Teleport(new Vector2(x, b.max.y + 1.5f));
            yield return Walk(Vector2.down, 1.2f);
            float northY = Game.Player.Position.y;
            bool reachedBehind = northY < b.max.y + 0.35f && northY > b.max.y - 0.05f;
            bool behindDrawn = body.sortingOrder < objectRenderer.sortingOrder;
            float overlap = objectRenderer.bounds.max.y - northY;
            Report($"{name} from north: stopped at y={northY:0.00} (base ends {b.max.y:0.00}, art reaches {objectRenderer.bounds.max.y:0.00}, so {overlap:0.00} tiles of it cover the player) behind={reachedBehind}; drawn behind={behindDrawn} (player {body.sortingOrder} vs object {objectRenderer.sortingOrder})", reachedBehind && behindDrawn);
            yield return Shot("depth_" + name + "_behind");
        }

        IEnumerator Walk(Vector2 direction, float seconds)
        {
            Game.Input.MoveOverride = direction;
            yield return Wait(seconds);
            Game.Input.MoveOverride = Vector2.zero;
            yield return Wait(0.25f);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.35f);
        }

        void Report(string message, bool ok)
        {
            if (ok) depthPass++; else depthFail++;
            Log((ok ? "PASS " : "FAIL ") + message);
        }
    }
}
