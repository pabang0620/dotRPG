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
                CareerFx.Slash(from, c.dir, c.n.range, CareerFx.Steel, i == 0 ? 28f : -28f, i == 1);
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
            CareerFx.Slash(start, c.dir, 1.2f, CareerFx.Steel, 0f, false, .12f);
            Vector2 previous = start;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                if (!Live(c)) yield break;
                Vector2 at = Vector2.Lerp(start, end, Mathf.Clamp01((t + Time.deltaTime) / seconds));
                CareerFx.DashTrail(at, at + Vector2.down * .45f, c.dir, CareerFx.Steel);
                foreach (var e in Corridor(previous, at, c.n.radius))
                    if (hit.Add(e)) Strike(c, e, c.n.damage, e.Center - c.dir, 7f, 1, "c_slash");
                previous = at;
                yield return null;
            }
            if (!Live(c)) yield break;
            CareerFx.Slash(end, c.dir, 1.6f, CareerFx.Steel, 0f, true, .2f);
            CareerFx.Line(start, end, CareerFx.SteelDeep, .25f, .25f);
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
                CareerFx.Slash(from, c.dir, c.n.range, CareerFx.Steel, Random.Range(-40f, 40f), i % 2 == 1, .12f);
                Sound("c_slash", .6f);
                foreach (var e in Fan(from, c.dir, c.n.range, 140f)) Strike(c, e, c.n.damage, from, 2.5f, 0, "c_slash");
                yield return new WaitForSeconds(CutGap);
            }
            if (!Live(c)) yield break;
            Vector2 at = owner.Center;
            Pose(.25f, 2);
            CareerFx.Slash(at, c.dir, c.n.range * 1.2f, CareerFx.Steel, 0f, false, .25f);
            CareerFx.Shock(at + c.dir * c.n.range * .5f, c.n.range * .7f, CareerFx.Steel);
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
            var blade = new MovingFx();
            float size = c.n.radius / .7f;
            blade.Add(SkillFx.Spawn("fx_glow", at, new Color(.4f, .7f, 1f, .6f), 10f, SkillFx.TopOrder + 1).Additive().Scale(size * 1.6f, size * 1.6f).Fade(FxFade.None));
            blade.Add(SkillFx.Spawn("fx_arc", at, Color.white, 10f, SkillFx.TopOrder + 2).Rotate(CareerFx.Angle(c.dir)).Scale(size * .6f, size).Fade(FxFade.None));
            var hit = new HashSet<EnemyController>();
            float travelled = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { blade.Kill(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + c.dir * step;
                blade.MoveTo(next);
                SkillFx.Spawn("fx_arc", next - c.dir * .15f, new Color(.45f, .75f, 1f, .5f), .16f, SkillFx.TopOrder).Additive().Rotate(CareerFx.Angle(c.dir)).Scale(size * .9f, size * .8f);
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (hit.Add(e) && Strike(c, e, c.n.damage, e.Center - c.dir, 6f, 1, "c_slash") && c.authority)
                    {
                        broken[e] = Time.time + c.s.duration;
                        SkillFx.Spawn("fx_crack", e.Center, CareerFx.Steel, .6f, SkillFx.TopOrder + 3).Scale(.4f, .7f).Fade(FxFade.Late);
                    }
                at = next;
                travelled += step;
                yield return null;
            }
            blade.Kill();
            SkillVisuals.Flash(at, CareerFx.Steel, 1.4f, .18f);
            SkillVisuals.Sparks(at, CareerFx.Steel, 7, 6f, .2f);
        }

        /// <summary>일섬: a thin line is drawn first; a beat later everything on it is cut at once.</summary>
        IEnumerator Iaido(Run c)
        {
            Vector2 from = owner.Center + c.dir * .3f, to = from + c.dir * c.n.range;
            GlowLineFx.Spawn(from, to, new Color(.6f, .85f, 1f, .45f), .12f, .2f, SkillFx.TopOrder + 1);
            Sound("c_slash", .5f);
            yield return new WaitForSeconds(.18f);
            if (!Live(c)) yield break;
            Pose(.2f, 2);
            CareerFx.Line(from, to, CareerFx.Steel, c.n.radius * .6f, .3f);
            SkillVisuals.Flash(owner.Center + c.dir * c.n.range * .5f, new Color(.7f, .9f, 1f, .5f), c.n.range * .8f, .15f);
            Sound("c_heavy", .9f);
            foreach (var e in Corridor(from, to, c.n.radius))
                if (Strike(c, e, c.n.damage, from, 8f, 2, "c_slash"))
                    CareerFx.Slash(e.Center - c.dir * .5f, c.dir, 1f, CareerFx.Steel, Random.Range(-60f, 60f), Random.value < .5f, .14f);
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
                for (float t = 0; t < seconds; t += Time.deltaTime)
                {
                    if (!Live(c)) yield break;
                    CareerFx.DashTrail(owner.Center, owner.Position, dir, CareerFx.Steel);
                    yield return null;
                }
            }
            if (!Live(c)) yield break;
            Vector2 at = owner.Center + dir * .6f;
            Pose(.25f, 2);
            CareerFx.Slash(owner.Center, Vector2.down, 1.6f, CareerFx.Steel, 0f, false, .2f);
            CareerFx.Slam(at + Vector2.down * .3f, c.n.radius, CareerFx.Steel, true);
            Sound("c_heavy");
            foreach (var e in Enemies(at, c.n.radius))
            {
                bool low = e.Health.Current < e.Health.Max * .35f;
                Strike(c, e, Mathf.RoundToInt(c.n.damage * (low ? 1.6f : 1f)), at, 11f, 2, "c_heavy");
                if (low) SkillFx.Spawn("fx_cut", e.Center, Color.white, .25f, SkillFx.TopOrder + 6).Rotate(90f).Scale(new Vector2(.5f, 1.4f), new Vector2(1.8f, .7f)).Fade(FxFade.Quick);
            }
        }

        /// <summary>천검귀일: six swords rain onto the monsters in front, then a giant sword pins the centre.</summary>
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
                yield return new WaitForSeconds(CutGap);
            }
            Target(c.dir, c.n.range, out Vector2 center);
            CareerFx.SwordDrop(center, .35f, CareerFx.Steel, 1.6f);
            yield return new WaitForSeconds(.35f);
            if (!Live(c)) yield break;
            Pose(.3f, 2);
            yield return FallingSword(c, center, Mathf.RoundToInt(c.n.damage * 3.75f), c.n.radius, 0f, true);
            SkillVisuals.UltFinish(center, CareerFx.Steel, c.n.radius);
        }

        IEnumerator FallingSword(Run c, Vector2 ground, int damage, float radius, float fall, bool giant)
        {
            if (fall > 0f)
            {
                CareerFx.SwordDrop(ground, fall, CareerFx.Steel, .8f);
                yield return new WaitForSeconds(fall);
            }
            if (!Live(c)) yield break;
            CareerFx.SwordImpact(ground, radius, CareerFx.Steel, giant);
            if (giant) CareerFx.Slam(ground, radius, CareerFx.Steel, true);
            Sound("c_heavy", giant ? 1f : .6f);
            foreach (var e in Enemies(ground, radius))
                if (Strike(c, e, damage, ground, giant ? 12f : 5f, giant ? 2 : 1, "c_heavy") && giant) Stun(c, e, .8f);
        }
    }
}
