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
        /// behind the object when it stands behind it and in front of it otherwise. Trees must also turn
        /// see-through while the player is behind them and be solid again once the player has left.
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
                int trees = 0, choppable = 0, edge = 0, missing = 0;
                foreach (Transform child in Game.World.ObjectsRoot)
                {
                    if (child.name != "Tree" && child.name != "EdgeTree") continue;
                    trees++;
                    if (child.name == "EdgeTree") edge++;
                    if (child.GetComponent<ResourceNode>() != null) choppable++;
                    if (child.GetComponent<TreeFade>() == null) missing++;
                }
                Report($"every tree on {mapId} can turn see-through: {trees} trees ({choppable} choppable, {edge} along the forest edge), {missing} without it", trees > 0 && missing == 0);
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
            var fade = box.GetComponent<TreeFade>();
            var body = Game.Player.GetComponentInChildren<CharacterAnimator>().Renderer;
            float x = b.center.x;
            Log($"{name}: '{objectRenderer.sprite.name}' base y={box.transform.position.y:0.00} solid y {b.min.y:0.00}..{b.max.y:0.00} width {b.size.x:0.00} see-through={(fade != null)}");

            // From the south: walk north into it. The base should stop the player, drawn in front.
            yield return Teleport(new Vector2(x, b.min.y - 1.5f));
            yield return Walk(Vector2.up, 1.2f);
            float southY = Game.Player.Position.y;
            bool blockedSouth = southY < b.min.y - 0.2f;
            bool frontDrawn = body.sortingOrder > objectRenderer.sortingOrder;
            Report($"{name} from south: stopped at y={southY:0.00} (base starts {b.min.y:0.00}) blocked={blockedSouth}; drawn in front={frontDrawn} (player {body.sortingOrder} vs object {objectRenderer.sortingOrder})", blockedSouth && frontDrawn);
            if (fade != null) Report($"{name} stays solid while the player is in front of it: alpha={fade.Alpha:0.00}", fade.Alpha > 0.99f && objectRenderer.color.a > 0.99f);
            yield return Shot("depth_" + name + "_front");

            // From the north: walk south until the base stops the player, standing behind the crown / roof.
            yield return Teleport(new Vector2(x, b.max.y + 1.5f));
            yield return Walk(Vector2.down, 1.2f);
            float northY = Game.Player.Position.y;
            bool reachedBehind = northY < b.max.y + 0.35f && northY > b.max.y - 0.05f;
            bool behindDrawn = body.sortingOrder < objectRenderer.sortingOrder;
            float overlap = objectRenderer.bounds.max.y - northY;
            Report($"{name} from north: stopped at y={northY:0.00} (base ends {b.max.y:0.00}, art reaches {objectRenderer.bounds.max.y:0.00}, so {overlap:0.00} tiles of it cover the player) behind={reachedBehind}; drawn behind={behindDrawn} (player {body.sortingOrder} vs object {objectRenderer.sortingOrder})", reachedBehind && behindDrawn);
            if (fade != null) Report($"{name} turns see-through while the player is behind it: alpha={fade.Alpha:0.00} (faded = {TreeFade.FadedAlpha:0.00})", fade.Alpha < 0.6f && objectRenderer.color.a < 0.6f);
            yield return Shot("depth_" + name + "_behind");

            // Stepping out again brings the tree back to solid.
            if (fade != null)
            {
                yield return Teleport(new Vector2(x, b.min.y - 1.5f));
                yield return Wait(0.5f);
                Report($"{name} solid again once the player has left: alpha={fade.Alpha:0.00}", fade.Alpha > 0.99f && objectRenderer.color.a > 0.99f);
            }
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
