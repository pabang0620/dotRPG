using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[CH3 SPEC] 파이터 specializations that are whole new moves (Docs/ch3/SKILL_SPECIALIZATION.md 3).</summary>
    public sealed partial class CareerCombat
    {
        IEnumerator CrossLine(Run c)
        {
            Vector2 from = owner.Center, to = from + c.dir * 3.5f;
            Pose(.18f, 2);
            CareerFx.Clip("f_line", from, c.dir, 1f, 26f, VfxLayer.Top, true, Tint(c, CareerFx.Steel)).Squash(3.5f / 6f, 1f);
            Sound("c_heavy", .8f);
            int damage = c.n.damage * 2;
            foreach (var e in Corridor(from, to, .5f))
                if (Strike(c, e, damage, from, 8f, 2, "c_slash") && c.authority && !e.IsDead) e.Stagger(.5f);
            yield break;
        }

        IEnumerator CrossFour(Run c)
        {
            var cut = new HashSet<EnemyController>();
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * 1.1f / 1.7f));
            for (int i = 0; i < 4; i++)
            {
                if (!Live(c)) yield break;
                Vector2 from = owner.Center, dir = CareerFx.Tilt(c.dir, i * 90f);
                Pose(.1f, i % 2);
                CareerFx.Clip("f_arc", from, dir, c.n.range / 1.35f, 40f, VfxLayer.Top, true, Tint(c, CareerFx.Steel)).FlipY(i % 2 == 1);
                Sound("c_slash", .6f);
                foreach (var e in Fan(from, dir, c.n.range, 90f))
                    if (cut.Add(e)) Strike(c, e, damage, from, 5f, 0, "c_slash");
                yield return new WaitForSeconds(CutGap * .5f);
            }
        }

        /// <summary>[CH3 SPEC] 섬광 관통 A: 4.5 on the first monster, 1.1 on the rest (of 2.8). 섬광 잔보 B: 2.7.</summary>
        static int RushDamage(Run c, int order)
        {
            if (c.n.spec == 'a') return Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (order == 1 ? 4.5f : 1.1f) / 2.8f));
            if (c.n.spec == 'b') return Mathf.Max(1, Mathf.RoundToInt(c.n.damage * 2.7f / 2.8f));
            return c.n.damage;
        }

        /// <summary>[CH3 SPEC] 삼섬: three lines (±20°) at half damage, then a burst where they meet the first monster (half).</summary>
        IEnumerator IaidoThree(Run c)
        {
            Vector2 from = owner.Center + c.dir * .3f;
            int half = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * .5f));
            for (int i = -1; i <= 1; i++)
                CareerFx.Clip("f_line", from, CareerFx.Tilt(c.dir, i * 20f), 1f, 5.5f, VfxLayer.Top, true, new Color(1f, 1f, 1f, .7f), false, .18f).Squash(c.n.range / 6f, .6f);
            Sound("c_slash", .5f);
            yield return new WaitForSeconds(.18f);
            if (!Live(c)) yield break;
            Pose(.2f, 2);
            Sound("c_heavy", .9f);
            EnemyController meet = null;
            for (int i = -1; i <= 1; i++)
            {
                Vector2 d = CareerFx.Tilt(c.dir, i * 20f);
                CareerFx.Clip("f_line", from, d, 1f, 22f, VfxLayer.Top, true, Tint(c, Color.white)).Squash(c.n.range / 6f, 1f);
                foreach (var e in Corridor(from, from + d * c.n.range, c.n.radius))
                {
                    if (Strike(c, e, half, from, 6f, 1, "c_slash") && i == 0 && meet == null) meet = e;
                }
            }
            Vector2 burst = meet != null ? meet.Center : from + c.dir * c.n.range * .6f;
            yield return new WaitForSeconds(CutGap);
            if (!Live(c)) yield break;
            CareerFx.Clip("f_x", burst, c.dir, 1.6f, 30f, VfxLayer.Top, true, Tint(c, Color.white));
            foreach (var e in Enemies(burst, 1.5f)) Strike(c, e, half, burst, 8f, 2, "c_heavy");
        }

        /// <summary>[CH3 SPEC] 천검 만상: the cross keeps raining blades on its area, 150% a second.</summary>
        IEnumerator SwordStorm(Run c, Vector2 at, float seconds)
        {
            int tick = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * 1.5f));
            for (float t = 0f; t < seconds; t += 1f)
            {
                yield return new WaitForSeconds(1f);
                if (!Live(c)) yield break;
                for (int k = 0; k < 3; k++) StartCoroutine(RainSword(at + Random.insideUnitCircle * c.n.radius * .8f, .16f));
                foreach (var e in Enemies(at, c.n.radius)) Strike(c, e, tick, at, 3f, 0, "c_slash");
            }
        }
    }
}
