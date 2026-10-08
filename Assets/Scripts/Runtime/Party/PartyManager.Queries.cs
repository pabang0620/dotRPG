using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class PartyManager
    {
        /// <summary>Alive members whose body centre is within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public List<PlayerController> MembersInCircle(Vector2 center, float radius)
        {
            var list = new List<PlayerController>();
            foreach (var m in AliveMembers)
                if (Vector2.Distance(m.Center, center) < radius) list.Add(m);
            return list;
        }

        /// <summary>
        /// Alive members inside a cone: within <paramref name="radius"/> of <paramref name="origin"/> and at most
        /// <paramref name="halfAngleDeg"/> degrees off <paramref name="direction"/>.
        /// </summary>
        public List<PlayerController> MembersInArc(Vector2 origin, Vector2 direction, float radius, float halfAngleDeg)
        {
            var list = new List<PlayerController>();
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            foreach (var m in AliveMembers)
            {
                Vector2 to = m.Center - origin;
                if (to.magnitude > radius) continue;
                if (to.sqrMagnitude < 0.0001f || Vector2.Angle(dir, to) <= halfAngleDeg) list.Add(m);
            }
            return list;
        }

        // [MONSTER] Line / rectangle query (charges, arrow lines, line telegraphs).
        /// <summary>
        /// Alive members whose body centre lies in the rectangle starting at <paramref name="origin"/>, running
        /// <paramref name="length"/> along <paramref name="direction"/>, <paramref name="halfWidth"/> to each side.
        /// </summary>
        public List<PlayerController> MembersInRect(Vector2 origin, Vector2 direction, float length, float halfWidth)
        {
            var list = new List<PlayerController>();
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            foreach (var m in AliveMembers)
            {
                Vector2 to = m.Center - origin;
                float along = Vector2.Dot(to, dir);
                float across = Mathf.Abs(to.x * dir.y - to.y * dir.x);
                if (along >= 0f && along <= length && across <= halfWidth) list.Add(m);
            }
            return list;
        }

        /// <summary>Nearest alive member to <paramref name="point"/> (null when everyone is down).</summary>
        public PlayerController NearestAlive(Vector2 point)
        {
            PlayerController best = null;
            float bestDist = float.MaxValue;
            foreach (var m in AliveMembers)
            {
                float d = Vector2.Distance(m.Position, point);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        /// <summary>The member a monster should go after (threat table; solo = the local player).</summary>
        public PlayerController SelectTarget(EnemyController enemy)
        {
            if (enemy == null) return null;
            return ThreatTable.For(enemy).Select(this);
        }

        /// <summary>True for a hit dealt by a companion (drawn with white damage numbers).</summary>
        public static bool IsCompanionHit(DamageInfo info)
        {
            var m = info.AttackerMember;
            return m != null && !m.IsLocal;
        }
    }
}
