using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>파이터: a fast melee duelist. Short cuts chain into each other; heavy finishers end them.</summary>
    public sealed partial class CareerCombat
    {
        // Monsters ignore hits for 0.15 s after one lands, so the cuts of one skill are at least this far apart.
        const float CutGap = .17f;

        IEnumerator Fighter(Run c)
        {
            switch (c.s.effect)
            {
                case "cross": yield return CrossCut(c); break;
                case "rush": yield return FlashRush(c); break;
                case "flurry": yield return Flurry(c); break;
                case "break": yield return BreakWave(c); break;
                case "iaido": yield return Iaido(c); break;
                case "execute": yield return Execute(c); break;
                case "swordrain": yield return SwordRain(c); break;
            }
        }

        /// <summary>십자참: two crossing cuts in a 120° fan; the second one shoves.</summary>
        IEnumerator CrossCut(Run c)
        {
            for (int i = 0; i < 2; i++)
            {
                if (!Live(c)) yield break;
                Vector2 from = owner.Center;
                Pose(.14f, i);
                // First a downward cut, then a rising one: the second arc is the first one flipped, so they cross.
                CareerFx.Clip("f_arc", from, CareerFx.Tilt(c.dir, i == 0 ? 12f : -12f), c.n.range / 1.35f, 34f).FlipY(i == 1);
                if (i == 1) CareerFx.Clip("f_x", from + c.dir * c.n.range * .55f, c.dir, 1.6f, 30f);
                Sound("c_slash", .8f);
                foreach (var e in Fan(from, c.dir, c.n.range, 120f))
                    Strike(c, e, c.n.damage, from, i == 0 ? 4f : 9f, i, "c_slash");
                if (i == 0) yield return new WaitForSeconds(CutGap);
            }
        }

        /// <summary>섬광보: dash through the line, cutting every monster passed, and finish with a flash cut.</summary>
        IEnumerator FlashRush(Run c)
        {
            Vector2 start = owner.Center;
            float distance = Dash(c.dir, c.n.range);
            float seconds = Mathf.Max(.08f, DashSeconds(distance));
            Vector2 end = start + c.dir * distance;
            var hit = new HashSet<EnemyController>();
            Sound("c_dash");
            Vector2 previous = start;
            float nextGhost = 0f;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                if (!Live(c)) yield break;
                Vector2 at = Vector2.Lerp(start, end, Mathf.Clamp01((t + Time.deltaTime) / seconds));
                if (t >= nextGhost) { nextGhost = t + seconds / 4f; CareerFx.Ghost(owner, new Color(.45f, .7f, 1f, .75f), .3f); }
                foreach (var e in Corridor(previous, at, c.n.radius))
                    if (hit.Add(e)) Strike(c, e, c.n.damage, e.Center - c.dir, 7f, 1, "c_slash");
                previous = at;
                yield return null;
            }
            if (!Live(c)) yield break;
            // A straight blade of light along the path, then every monster passed shows its cut at once.
            CareerFx.Clip("f_line", start, c.dir, 1f, 26f).Squash(Mathf.Max(.2f, distance / 6f), 1f);
            yield return new WaitForSeconds(.12f);
            foreach (var e in hit) if (e != null && !e.IsDead) CareerFx.Clip("f_cut", e.Center, c.dir, 1.3f, 26f);
        }

        /// <summary>난무: four quick cuts in front, then a big rising finisher.</summary>
        IEnumerator Flurry(Run c)
        {
            Dash(c.dir, .5f);
            for (int i = 0; i < 4; i++)
            {
                if (!Live(c)) yield break;
                Vector2 from = owner.Center;
                Pose(.12f, i % 2);
                float[] tilt = { 22f, -18f, 38f, -30f };
                CareerFx.Clip("f_arc", from, CareerFx.Tilt(c.dir, tilt[i]), c.n.range / 1.45f, 46f).FlipY(i % 2 == 1);
                Sound("c_slash", .6f);
                foreach (var e in Fan(from, c.dir, c.n.range, 140f)) Strike(c, e, c.n.damage, from, 2.5f, 0, "c_slash");
                yield return new WaitForSeconds(CutGap);
            }
            if (!Live(c)) yield break;
            Vector2 at = owner.Center;
            Pose(.25f, 2);
            // The finisher: a big rising cut with wind streaks thrown forward.
            CareerFx.Clip("f_arc", at, CareerFx.Tilt(c.dir, -8f), c.n.range / 1.1f, 30f).FlipY(true);
            for (int k = 0; k < 5; k++) CareerFx.DashTrail(at + c.dir * (.4f + k * .3f) + new Vector2(0f, (k - 2) * .18f), at, -c.dir, CareerFx.Steel);
            Sound("c_heavy", .8f);
            int finisher = Mathf.RoundToInt(c.n.damage * 1.6f / .7f);
            foreach (var e in Fan(at, c.dir, c.n.range + .3f, 150f)) Strike(c, e, finisher, at, 11f, 2, "c_heavy");
        }

        /// <summary>파쇄 검기: a piercing crescent; monsters it cuts take 15% more of my damage for a while.</summary>
        IEnumerator BreakWave(Run c)
        {
            const float speed = 14f;
            Pose(.2f, 1);
            Sound("c_slash");
            Vector2 start = owner.Center + c.dir * .4f, at = start;
            float size = c.n.radius / .95f;                       // the crescent clip is 2.5 units tall
            var blade = CareerFx.Clip("f_wave", at, c.dir, size, 16f, VfxLayer.Top, true, null, false, 10f, true);
            var hit = new HashSet<EnemyController>();
            float travelled = 0f, nextTrail = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { blade?.Stop(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + c.dir * step;
                blade?.Place(next);
                if (travelled >= nextTrail)
                {
                    nextTrail = travelled + .45f;
                    CareerFx.Clip("f_wave", next - c.dir * .2f, c.dir, size * .92f, 16f, VfxLayer.Top, true, new Color(.6f, .8f, 1f, .45f), true, .2f).FadeOut(.2f);
                }
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (hit.Add(e) && Strike(c, e, c.n.damage, e.Center - c.dir, 6f, 1, "c_slash") && c.authority)
                    {
                        broken[e] = Time.time + c.s.duration;
                        CareerFx.Clip("f_shatter", e.Center, Vector2.zero, 1.2f, 20f);
                    }
                at = next;
                travelled += step;
                yield return null;
            }
            blade?.Stop();
            CareerFx.Clip("f_x", at, c.dir, 1.2f, 30f);
        }

        /// <summary>일섬: a thin line is drawn first; a beat later everything on it is cut at once.</summary>
        IEnumerator Iaido(Run c)
        {
            Vector2 from = owner.Center + c.dir * .3f, to = from + c.dir * c.n.range;
            float stretch = c.n.range / 6f;
            // A hairline flickers first (frame 0 of the clip held), then the blade of light splits the line.
            CareerFx.Clip("f_line", from, c.dir, 1f, 5.5f, VfxLayer.Top, true, new Color(1f, 1f, 1f, .7f), false, .18f).Squash(stretch, .6f);
            Sound("c_slash", .5f);
            yield return new WaitForSeconds(.18f);
            if (!Live(c)) yield break;
            Pose(.2f, 2);
            CareerFx.Clip("f_line", from, c.dir, 1f, 22f).Squash(stretch, 1.2f);
            if (owner.IsLocal) SkillVisuals.Flash(owner.Center + c.dir * c.n.range * .5f, new Color(.85f, .95f, 1f, .45f), c.n.range * 1.4f, .1f);
            Sound("c_heavy", .9f);
            int k = 0;
            foreach (var e in Corridor(from, to, c.n.radius))
                if (Strike(c, e, c.n.damage, from, 8f, 2, "c_slash"))
                    CareerFx.Clip("f_cut", e.Center, CareerFx.Tilt(c.dir, (k++ % 3 - 1) * 25f), 1.5f, 26f);
        }

        /// <summary>단죄: leap onto the monster in front and bring the sword down; low-HP targets take 1.6×.</summary>
        IEnumerator Execute(Run c)
        {
            var target = Target(c.dir, c.n.range, out Vector2 point);
            Vector2 toTarget = point - owner.Center;
            Vector2 dir = toTarget.sqrMagnitude > .01f ? toTarget.normalized : c.dir;
            float distance = Mathf.Max(0f, toTarget.magnitude - .7f);
            if (distance > .1f)
            {
                distance = Dash(dir, distance);
                Sound("c_dash", .7f);
                float seconds = DashSeconds(distance);
                float nextGhost = 0f;
                for (float t = 0; t < seconds; t += Time.deltaTime)
                {
                    if (!Live(c)) yield break;
                    if (t >= nextGhost) { nextGhost = t + .05f; CareerFx.Ghost(owner, new Color(.45f, .7f, 1f, .7f), .25f); }
                    yield return null;
                }
            }
            if (!Live(c)) yield break;
            // Up/down aims land a little farther (the body is taller than it is wide) and an upward blow is drawn by
            // depth, behind the fighter, instead of over the head; a downward blow stays in front.
            bool vertical = Mathf.Abs(dir.y) > Mathf.Abs(dir.x);
            Vector2 at = owner.Center + dir * (vertical ? (dir.y > 0f ? .95f : .8f) : .6f);
            Pose(.25f, 2);
            // The overhead cut lands on the target, the ground cracks (the Fighter's only cracking blow).
            Vector2 ground = at + Vector2.down * .35f;
            CareerFx.Clip("f_vslash", ground, dir, .85f, 30f, vertical && dir.y > 0f ? VfxLayer.AtFeet : VfxLayer.Top, false);
            CareerFx.Clip("impact", ground, Vector2.zero, c.n.radius / 1.7f, 26f, VfxLayer.Ground, false, CareerFx.Steel);
            SkillFx.Crack(ground, c.n.radius, 1.4f);
            Sound("c_heavy");
            foreach (var e in Enemies(at, c.n.radius))
            {
                bool low = e.Health.Current < e.Health.Max * .35f;
                Strike(c, e, Mathf.RoundToInt(c.n.damage * (low ? 1.6f : 1f)), at, 11f, 2, "c_heavy");
                if (low) CareerFx.Clip("f_x", e.Center, dir, 2.2f, 26f);
            }
        }

        /// <summary>
        /// 천검귀일: six striking swords rain onto the monsters in front inside a storm of extra swords (two more per
        /// strike, for the look only, no damage), then the dark greatsword pins the centre.
        /// </summary>
        IEnumerator SwordRain(Run c)
        {
            // Swords go to the monsters in front first; with fewer than six they fall on the same ones again.
            var foes = Fan(owner.Center, c.dir, c.n.range + 1f, 160f);
            Vector2 zone = owner.Center + c.dir * c.n.range * .6f;
            for (int i = 0; i < 6; i++)
            {
                if (!Live(c)) yield break;
                var foe = foes.Count > 0 ? foes[i % foes.Count] : null;
                Vector2 point = foe != null && !foe.IsDead ? foe.Center + Random.insideUnitCircle * .35f : zone + Random.insideUnitCircle * 2f;
                StartCoroutine(FallingSword(c, point, c.n.damage, 1.2f, .22f, false));
                // [VFX] The storm: more swords land around the zone between the strikes (no damage, damage stays on the six).
                for (int k = 0; k < 2; k++)
                    StartCoroutine(RainSword(zone + Random.insideUnitCircle * (c.n.range * .45f), .18f + k * .06f));
                yield return new WaitForSeconds(CutGap);
            }
            Target(c.dir, c.n.range, out Vector2 center);
            CareerFx.SwordDrop(center, .35f, CareerFx.Steel, 1.6f, true); // the finisher: the dark greatsword
            yield return new WaitForSeconds(.35f);
            if (!Live(c)) yield break;
            Pose(.3f, 2);
            yield return FallingSword(c, center, Mathf.RoundToInt(c.n.damage * 3.75f), c.n.radius, 0f, true);
        }

        /// <summary>A sword of the rain that only shows: smaller, lands, sparks and fades.</summary>
        IEnumerator RainSword(Vector2 ground, float fall)
        {
            CareerFx.SwordDrop(ground, fall, CareerFx.Steel, .6f);
            yield return new WaitForSeconds(fall);
            SkillFx.Spawn(SkillFx.Pick("fxi_bigsword", "fx_bigsword"), ground, Color.white, .4f, SkillFx.At(ground.y, 6)).Scale(.75f, .75f).Fade(FxFade.Late);
            CareerFx.Clip("f_spark", ground, Vector2.zero, .7f, 28f, VfxLayer.Top, false);
            Sound("c_heavy", .25f);
        }

        IEnumerator FallingSword(Run c, Vector2 ground, int damage, float radius, float fall, bool giant)
        {
            if (fall > 0f)
            {
                CareerFx.SwordDrop(ground, fall, CareerFx.Steel, .8f);
                yield return new WaitForSeconds(fall);
            }
            if (!Live(c)) yield break;
            SkillFx.Spawn(giant ? SkillFx.Pick("fxi_bigsword_dark", "fx_bigsword") : SkillFx.Pick("fxi_bigsword", "fx_bigsword"), ground, Color.white, giant ? .9f : .5f, SkillFx.At(ground.y, 6)).Scale(giant ? 1.4f : 1f, giant ? 1.4f : 1f).Fade(FxFade.Late);
            CareerFx.Clip("f_spark", ground, Vector2.zero, giant ? 2f : 1f, 26f, VfxLayer.Top, false);
            if (giant)
            {
                CareerFx.Clip("impact", ground, Vector2.zero, radius / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Steel);
                SkillFx.Crack(ground, radius, 1.8f);
            }
            Sound("c_heavy", giant ? 1f : .6f);
            foreach (var e in Enemies(ground, radius))
                if (Strike(c, e, damage, ground, giant ? 12f : 5f, giant ? 2 : 1, "c_heavy") && giant) Stun(c, e, .8f);
        }
    }
}
