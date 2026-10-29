using UnityEngine;

namespace DotRPG
{
    /// <summary>Positions an AI near a ritual totem so the existing skill auto-aim actually selects it.</summary>
    internal static class RitualTargeting
    {
        public static bool IsTotem(EnemyController target)
        {
            if (target == null) return false;
            foreach (var enemy in EnemyController.Active)
            {
                if (enemy == null || enemy.IsDead || !(enemy.Behaviour is BossBrain boss) || !boss.RuinActive) continue;
                foreach (var totem in boss.RuinTotemsLeft) if (totem == target) return true;
            }
            return false;
        }

        public static bool NeedsApproach(PlayerController actor, EnemyController target)
        {
            Vector2 toward = target.Center - actor.Center;
            float distance = toward.magnitude;
            Vector2 aim = distance > .001f ? toward / distance : actor.AimDirection;
            // Match PlayerController.AutoAim: distance minus 0.8 m for a target directly ahead.
            float wanted = distance - .8f;
            foreach (var enemy in EnemyController.Active)
            {
                if (enemy == null || enemy == target || enemy.IsDead || !enemy.isActiveAndEnabled) continue;
                Vector2 other = enemy.Center - actor.Center;
                float length = other.magnitude;
                if (length >= .01f && length - Vector2.Dot(other / length, aim) * .8f <= wanted) return true;
            }
            return false;
        }

        /// <summary>Walk around enemy bodies that terrain-only GridPath does not include.</summary>
        public static Vector2 Steer(PlayerController actor, Vector2 target)
        {
            Vector2 from = actor.Position;
            Vector2 desired = GridPath.Steer(from, target);
            if (desired.sqrMagnitude < .001f) return Vector2.zero;
            // Prefer the smallest detour, with a deterministic side on a head-on approach.
            for (int turn = 0; turn <= 8; turn++)
            {
                for (int side = 0; side < (turn == 0 ? 1 : 2); side++)
                {
                    float angle = turn * Mathf.PI / 8f * (side == 0 ? 1f : -1f);
                    Vector2 direction = new Vector2(desired.x * Mathf.Cos(angle) - desired.y * Mathf.Sin(angle),
                        desired.x * Mathf.Sin(angle) + desired.y * Mathf.Cos(angle));
                    bool clear = true;
                    for (int step = 1; step <= 6 && clear; step++)
                    {
                        Vector2 point = from + direction * (step * .2f);
                        if (!GridPath.Walkable(point)) { clear = false; break; }
                        clear = ClearOfBodies(actor, from, point);
                    }
                    if (clear) return direction;
                }
            }
            return Vector2.zero;
        }

        /// <summary>Checks actual body geometry, including large bosses and the offset between feet and collider centres.</summary>
        public static bool ClearOfBodies(PlayerController actor, Vector2 from, Vector2 point, bool allowMovingOut = true)
        {
            var body = actor != null ? actor.BodyCollider : null;
            if (body == null || !body.enabled || body.isTrigger) return true;
            Vector2 offset = (Vector2)body.bounds.center - actor.Position;
            float radius = BodyRadius(body);
            Vector2 centre = point + offset, origin = from + offset;
            foreach (var enemy in EnemyController.Active)
            {
                if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) continue;
                var obstacle = enemy.GetComponent<Collider2D>();
                if (obstacle == null || !obstacle.enabled || obstacle.isTrigger) continue;
                Vector2 obstacleCentre = obstacle.bounds.center;
                float clearance = radius + BodyRadius(obstacle) + .04f;
                float distance = Vector2.Distance(centre, obstacleCentre);
                if (distance >= clearance) continue;
                // If already overlapping, permit movement out rather than trapping the actor in place.
                if (!allowMovingOut || distance <= Vector2.Distance(origin, obstacleCentre)) return false;
            }
            return true;
        }

        static float BodyRadius(Collider2D collider)
        {
            if (collider is CircleCollider2D circle)
            {
                Vector3 scale = collider.transform.lossyScale;
                return circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }
            // Existing actors use circles; bound other collider types conservatively.
            return ((Vector2)collider.bounds.extents).magnitude;
        }
    }
}
