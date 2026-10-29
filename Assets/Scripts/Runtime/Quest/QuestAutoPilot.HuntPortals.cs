using UnityEngine;

namespace DotRPG
{
    public sealed partial class QuestAutoPilot
    {
        float portalAvoidSince = -1f, portalAvoidProgressAt, portalAvoidBestDistance;
        Vector2 portalAvoidGoal;
        string portalAvoidMap;

        /// <summary>Hunting stays on its selected map; navigation to another map still uses portals normally.</summary>
        Vector2 AvoidHuntPortals(PlayerController actor, Vector2 direction, Vector2 goal, bool trackProgress = true)
        {
            var world = Game.World;
            var body = actor.BodyCollider;
            if (!huntMode || world == null || world.MapId != huntMap || HuntingGrounds.Get(world.MapId) == null
                || Game.Dungeon?.InRun == true || body == null || !body.enabled
                || body.isTrigger || direction.sqrMagnitude < .001f)
            {
                if (trackProgress) portalAvoidSince = -1f;
                return direction;
            }

            Vector2 origin = body.bounds.center;
            Vector2 offset = origin - actor.Position;
            float radius = body is CircleCollider2D circle
                ? circle.radius * Mathf.Max(Mathf.Abs(body.transform.lossyScale.x), Mathf.Abs(body.transform.lossyScale.y))
                : ((Vector2)body.bounds.extents).magnitude;
            float look = Mathf.Max(1.2f, radius * 2f + .2f);

            bool PortalClear(Vector2 move)
            {
                Vector2 end = origin + move * look;
                foreach (var portal in MapPortal.Active)
                {
                    if (portal == null || !portal.isActiveAndEnabled) continue;
                    var trigger = portal.GetComponent<Collider2D>();
                    if (trigger == null || !trigger.enabled || !trigger.isTrigger) continue;
                    var bounds = trigger.bounds;
                    var rectangle = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
                    if (!HuntPortalPathCrosses(rectangle, radius + .04f, origin, end)) continue;
                    // A spawn or knockback can already overlap an exit. Allow walking inward or away from it.
                    var expanded = new Rect(rectangle.xMin - radius - .04f, rectangle.yMin - radius - .04f,
                        rectangle.width + 2f * (radius + .04f), rectangle.height + 2f * (radius + .04f));
                    if (expanded.Contains(origin) && !MapPortal.AllowsTravel(world.Bounds, portal.transform.position, actor.Position, move)) continue;
                    return false;
                }
                return true;
            }

            Vector2 straight = direction.normalized;
            if (PortalClear(straight)) { if (trackProgress) portalAvoidSince = -1f; return direction; }
            float distance = Vector2.Distance(actor.Position, goal);
            if (trackProgress && (portalAvoidSince < 0f || portalAvoidMap != world.MapId || Vector2.Distance(portalAvoidGoal, goal) > .5f))
            {
                portalAvoidSince = portalAvoidProgressAt = Time.time;
                portalAvoidMap = world.MapId;
                portalAvoidGoal = goal;
                portalAvoidBestDistance = distance;
            }
            else if (trackProgress && distance < portalAvoidBestDistance - .1f)
            {
                portalAvoidBestDistance = distance;
                portalAvoidProgressAt = Time.time;
            }
            if (trackProgress && Time.time - portalAvoidProgressAt > UnreachableSeconds)
            {
                // Try another monster rather than orbiting an exit indefinitely. Roaming without one stops safely.
                if (chaseTarget != null)
                {
                    unreachable[chaseTarget] = Time.time + UnreachableSkip;
                    chaseTarget = null;
                    lastProgressAt = Time.time;
                }
                else Stop(L.T("길이 막혀 자동 진행을 멈췄습니다."));
                portalAvoidSince = -1f;
                MeasureDecision("hunt_portal_retarget");
                return Vector2.zero;
            }

            // Check the final direction, after NPC avoidance and stuck sidesteps, with the actual body footprint.
            for (int turn = 1; turn <= 8; turn++)
                for (int side = 0; side < 2; side++)
                {
                    float angle = turn * Mathf.PI / 8f * (side == 0 ? 1f : -1f);
                    Vector2 candidate = new Vector2(straight.x * Mathf.Cos(angle) - straight.y * Mathf.Sin(angle),
                        straight.x * Mathf.Sin(angle) + straight.y * Mathf.Cos(angle));
                    if (!PortalClear(candidate)) continue;
                    bool clear = true;
                    for (int step = 1; step <= 6 && clear; step++)
                    {
                        Vector2 feet = origin + candidate * (look * step / 6f) - offset;
                        clear = GridPath.Walkable(feet);
                    }
                    if (!clear) continue;
                    MeasureDecision("hunt_portal_detour");
                    return candidate * direction.magnitude;
                }
            MeasureDecision("hunt_portal_blocked");
            return Vector2.zero;
        }

        /// <summary>Pure swept-circle versus portal-box test, conservatively expanded at the corners.</summary>
        internal static bool HuntPortalPathCrosses(Rect portal, float radius, Vector2 from, Vector2 to)
        {
            float enter = 0f, leave = 1f;
            bool Axis(float start, float delta, float min, float max)
            {
                if (Mathf.Abs(delta) < .00001f) return start >= min && start <= max;
                float a = (min - start) / delta, b = (max - start) / delta;
                if (a > b) { float swap = a; a = b; b = swap; }
                enter = Mathf.Max(enter, a);
                leave = Mathf.Min(leave, b);
                return enter <= leave;
            }
            return Axis(from.x, to.x - from.x, portal.xMin - radius, portal.xMax + radius)
                && Axis(from.y, to.y - from.y, portal.yMin - radius, portal.yMax + radius);
        }
    }
}
