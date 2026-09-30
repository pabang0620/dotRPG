using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Invisible trigger on a map edge. Walking into it moves the player to another map
    /// (see <see cref="GameFlow.TravelTo"/>). Placed by <see cref="WorldBuilder"/> for '&gt;' / '&lt;' cells.
    /// </summary>
    public class MapPortal : MonoBehaviour
    {
        public string TargetMap { get; private set; }

        public static MapPortal Create(Vector2 center, string targetMap, Transform parent)
        {
            var go = new GameObject("Portal_" + targetMap);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.9f, 0.9f);
            var portal = go.AddComponent<MapPortal>();
            portal.TargetMap = targetMap;
            return portal;
        }

        void OnTriggerEnter2D(Collider2D other) => TryTravel(other);

        void OnTriggerStay2D(Collider2D other) => TryTravel(other);

        void TryTravel(Collider2D other)
        {
            // Only the party leader (the local player) travels; companions follow it.
            var leader = Game.Party != null && Game.Party.Leader != null ? Game.Party.Leader : Game.Player;
            if (!Game.IsPlaying || Game.Flow == null || Game.Flow.IsTransitioning || leader == null) return;
            if (other.attachedRigidbody == null || other.attachedRigidbody.gameObject != leader.gameObject) return;
            if (leader.IsDead) return;
            Game.Flow.TravelTo(TargetMap);
        }
    }
}
