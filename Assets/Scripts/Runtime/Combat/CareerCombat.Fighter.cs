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
                case "frenzy": Frenzy(c); break;
                case "break": yield return BreakWave(c); break;
                case "iaido": yield return Iaido(c); break;
                case "execute": yield return Execute(c); break;
                case "swordrain": yield return SwordRain(c); break;
                case "xslash": yield return XSlash(c); break;
                case "stance": Stance(c); break;
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

        /// <summary>검귀 해방: the fighter's burst window. For a while every hit of mine lands much harder.</summary>
        void Frenzy(Run c)
        {
            Pose(.2f, 2);
            Sound("c_heavy", .9f);
            Feel(1, Vector2.up);
            for (int i = 0; i < 4; i++) CareerFx.Clip("f_arc", owner.Center, CareerFx.Tilt(Vector2.right, i * 90f), 1.1f, 40f).FlipY(i % 2 == 1);
            // Burning silhouette, afterimages and embers off the body for the whole window.
            // [UI] Only the first 2 s burn on the body now that it lasts a minute; the buff row shows the rest.
            PowerAura.Play(owner, Mathf.Min(2f, c.s.duration), new Color(1f, .25f, .12f), new Color(1f, .78f, .3f));
            // [VFX] polish: one big red surge rising off the body when the minute-long window opens.
            SkillFx.Spawn("fx_glow", owner.Position + Vector2.up * .3f, new Color(1f, .3f, .15f, .7f), .7f, SkillFx.TopOrder).Additive()
                .Move(Vector2.up * 1.6f, 3f).Scale(new Vector2(.7f, .9f), new Vector2(1.5f, 3.2f)).Fade(FxFade.Late);
            SkillVisuals.Sparks(owner.Center, new Color(1f, .55f, .25f, 1f), 14, 5f, .35f);
            frenzy = Mathf.RoundToInt(c.s.power * c.Scale);
            frenzyEnd = Time.time + c.s.duration;
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
            // [VFX] The generated heavy crescent (f2_break_crescent, square frame) about as tall as the old one; else f_wave.
            bool drawn = VfxLibrary.Has("f2_break_crescent");
            float genSize = c.n.radius * 2.6f;
            var blade = CareerFx.Gen("f2_break_crescent", "f_wave", at, c.dir, genSize, size, 16f, VfxLayer.Top, true, null, 10f, true);
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
                    if (drawn) CareerFx.Gen("f2_break_crescent", null, next - c.dir * .2f, c.dir, genSize * .92f, 0f, 16f, VfxLayer.Top, true, new Color(1f, 1f, 1f, .4f), .2f)?.FadeOut(.2f);
                    else CareerFx.Clip("f_wave", next - c.dir * .2f, c.dir, size * .92f, 16f, VfxLayer.Top, true, new Color(.6f, .8f, 1f, .45f), true, .2f).FadeOut(.2f);
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
            if (vertical)
            {
                // Up/down: the side-view chop arc would read as a sideways swing, so the cut is drawn along the aim
                // instead: a thin straight slash from the fighter to the target, a narrow beam of the blade coming down
                // on the spot, and the spark where it lands (behind the body when striking upward).
                var layer = dir.y > 0f ? VfxLayer.AtFeet : VfxLayer.Top;
                float reach = Mathf.Max(.8f, (at - owner.Center).magnitude + .4f);
                CareerFx.Clip("f_line", owner.Center, dir, 1f, 30f, layer, true).Squash(reach / 6f, .55f);
                GlowLineFx.Spawn(at + Vector2.up * 1.1f, ground, new Color(.75f, .9f, 1f, .85f), .22f, .16f, SkillFx.TopOrder + 2);
                CareerFx.Clip("f_spark", ground, Vector2.zero, 1.1f, 30f, layer, false);
            }
            else CareerFx.Clip("f_vslash", ground, dir, .85f, 30f, VfxLayer.Top, false);
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
        /// 천검귀일: striking swords rain onto the monsters all around me (not just in front) inside a storm of extra
        /// swords (two more per strike, for the look only, no damage), then the dark greatsword lands on me and
        /// pins everything in its radius.
        /// </summary>
        IEnumerator SwordRain(Run c)
        {
            // Swords go to every monster within the range around me; with fewer than the strike count they fall on the same ones again.
            var foes = Enemies(owner.Center, c.n.range + 1f);
            Vector2 zone = owner.Center;
            for (int i = 0; i < Mathf.Max(6, c.s.hits); i++) // awakening hit count (9 after the 2026-10-06 balance)
            {
                if (!Live(c)) yield break;
                var foe = foes.Count > 0 ? foes[i % foes.Count] : null;
                Vector2 point = foe != null && !foe.IsDead ? foe.Center + Random.insideUnitCircle * .35f : zone + Random.insideUnitCircle * c.n.range * .8f;
                StartCoroutine(FallingSword(c, point, c.n.damage, 1.2f, .22f, false));
                // [VFX] The storm: more swords land around the zone between the strikes (no damage, damage stays on the six).
                for (int k = 0; k < 2; k++)
                    StartCoroutine(RainSword(zone + Random.insideUnitCircle * c.n.range, .18f + k * .06f));
                yield return new WaitForSeconds(CutGap);
            }
            Vector2 center = owner.Center;
            CareerFx.SwordDrop(center, .35f, CareerFx.Steel, 1.6f, true); // the finisher: the dark greatsword, on me
            yield return new WaitForSeconds(.35f);
            if (!Live(c)) yield break;
            Pose(.3f, 2);
            yield return FallingSword(c, center, Mathf.RoundToInt(c.n.damage * 3.75f), c.n.radius, 0f, true);
        }

        /// <summary>How much of a glass sheet cell the cracked cross fills (measured after processing).</summary>
        const float CrackFill = .87f;

        /// <summary>
        /// 천검귀일 (v6, 2026-10-12): a cross (+) that breaks space like glass. A horizontal cut, then a vertical one
        /// (1.3 s); glass cracks spread along the cross while it trembles (1.4 s), the cut lines tear (0.3 s) and the
        /// cross shatters into glass shards for one big hit at 3 s. Every monster in the radius (bosses too) stays staggered from the cast until the burst.
        /// </summary>
        IEnumerator XSlash(Run c)
        {
            const float Cut = .55f, Gap = .2f, Draw = 1.3f, Shake = 1.4f, Tear = .3f, Mul = 10f, Width = .22f; // the line stays readable on screen (0.1 vanished)
            const float Total = Draw + Shake + Tear; // 3 s
            Vector2 at = owner.Center;
            var blue = new Color(.45f, .8f, 1f, 1f);
            Pose(Total + .2f, 2);
            if (c.authority)
                foreach (var e in Enemies(at, c.n.radius)) { e.Stagger(Total); StunStars.Attach(e); }
            SkillFx.Spawn("fx_ring", owner.Position, new Color(.55f, .8f, 1f, .6f), Total, SkillFx.GroundOrder + 10)
                .Scale(new Vector2(c.n.radius, c.n.radius * .7f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
            // The cross is drawn smaller than the area it hits (the ring shows the area).
            float arm = c.n.radius * .6f; // 2026-10-12: 20% smaller than .75
            int order = SkillFx.TopOrder + 2;
            // 1) Horizontal cut (left to right), then the vertical cut (top to bottom).
            var first = Swipe(at + Vector2.left * arm, at + Vector2.right * arm, blue, Width, Cut, order);
            yield return new WaitForSeconds(Cut * .7f);
            CutEnd(at + Vector2.right * arm, blue);
            yield return new WaitForSeconds(Cut * .3f + Gap);
            if (!Live(c)) { first.Kill(); yield break; }
            var second = Swipe(at + Vector2.up * arm, at + Vector2.down * arm, blue, Width, Cut, order);
            yield return new WaitForSeconds(Cut * .7f);
            CutEnd(at + Vector2.down * arm, blue);
            yield return new WaitForSeconds(Draw - Cut - Gap - Cut * .7f);
            if (!Live(c)) { first.Kill(); second.Kill(); yield break; }
            // 2) The cut cross cracks like glass (f3_glass_crack2 spreads along the lines) and trembles harder and harder.
            //    Without the art the drawn lines just tremble.
            VfxPlayer crack = VfxLibrary.Has("f3_glass_crack2")
                ? CareerFx.Gen("f3_glass_crack2", null, at, Vector2.zero, arm * 2f / CrackFill, 0f, 24f, VfxLayer.Top, false, null, Shake + Tear + .05f, false, 8f / Shake)
                : null;
            DimFx.Show(Shake + Tear + .3f, .25f);
            float nextSpark = 0f;
            for (float t = 0f; t < Shake + Tear; t += Time.deltaTime)
            {
                if (!Live(c)) { crack?.Stop(); first.Kill(); second.Kill(); yield break; }
                float k = Mathf.Clamp01(t / Shake), e = Mathf.Clamp01((t - Shake) / Tear);
                var jitter = Random.insideUnitCircle * (t < Shake ? Mathf.Lerp(.03f, .12f, k * k) : .14f);
                crack?.Place(at + jitter);
                first.Offset = second.Offset = jitter;
                // 3) The last 0.3 s: the cut lines tear apart and the cracked cross swells before it breaks.
                if (e > 0f) { first.Explode(); second.Explode(); crack?.Resize(arm * 2f / CrackFill * (1f + .08f * e)); }
                if (t >= nextSpark)
                {
                    nextSpark = t + Mathf.Lerp(.12f, .04f, k);
                    float u = Random.Range(-.9f, .9f) * arm;
                    SkillVisuals.Sparks(at + (Random.value < .5f ? new Vector2(0f, u) : new Vector2(u, 0f)), blue, 2, 2.5f, .25f);
                }
                if (t >= Shake && t - Time.deltaTime < Shake) Sound("c_slash");
                yield return null;
            }
            // 4) Shatter: the cross breaks into glass shards (f3_glass_shards) and the hit lands now (3 s after the cast).
            crack?.Stop();
            SpaceRiftFx.Burst(at + Vector2.left * arm, at + Vector2.right * arm, 24, order + 3);
            SpaceRiftFx.Burst(at + Vector2.up * arm, at + Vector2.down * arm, 24, order + 3);
            Sound("c_heavy");
            Feel(2, Vector2.down);
            if (owner.IsLocal) Game.Camera?.Shake(.35f, .35f);
            SkillVisuals.Flash(at, new Color(.75f, .92f, 1f, .7f), arm * 1.2f, .15f);
            foreach (var e in Enemies(at, c.n.radius))
                if (Strike(c, e, Mathf.RoundToInt(c.n.damage * Mul), at, 10f, 2, "c_heavy"))
                    CareerFx.Clip("f_cut", e.Center, Vector2.right, 1.5f, 26f);
        }

        /// <summary>One 천검귀일 cut: the stroke, a flash where the blade bites in, the swing sound and a light shake.</summary>
        StrokeFx Swipe(Vector2 from, Vector2 to, Color color, float width, float cut, int order)
        {
            SkillVisuals.Flash(from, new Color(.85f, .95f, 1f, .8f), 1.2f, .12f);
            Sound("c_slash");
            if (owner.IsLocal) Game.Camera?.Shake(.08f, .12f);
            return StrokeFx.Spawn(from, to, color, width, cut, order);
        }

        /// <summary>Where a 천검귀일 cut runs out: sparks fly on along the line.</summary>
        static void CutEnd(Vector2 at, Color color)
        {
            SkillVisuals.Sparks(at, Color.Lerp(color, Color.white, .5f), 6, 4f, .3f);
            SkillVisuals.Flash(at, new Color(.85f, .95f, 1f, .6f), .8f, .1f);
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
