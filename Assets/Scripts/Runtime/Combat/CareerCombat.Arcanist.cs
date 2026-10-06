using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>메이지: elemental area damage. Switching between fire, ice and lightning powers 원소의 기억.</summary>
    public sealed partial class CareerCombat
    {
        static readonly Color FireColor = new Color(1f, .55f, .2f, .95f);
        static readonly Color IceColor = new Color(.6f, .9f, 1f, .95f);
        static readonly Color StormColor = new Color(.55f, .85f, 1f, .95f);

        IEnumerator Arcanist(Run c)
        {
            switch (c.s.effect)
            {
                case "fire": yield return Fireball(c); break;
                case "ice": yield return IceSpears(c); break;
                case "storm": yield return ChainLightning(c); break;
                case "orbit": yield return StarShots(c); break;
                case "blink": yield return PhaseBlink(c); break;
                case "rift": StartCoroutine(GravityRift(c)); break;
                case "cataclysm": yield return Cataclysm(c); break;
            }
        }

        /// <summary>원소의 기억: a different element than the last one that landed hits harder.</summary>
        float ElementPower(string element)
        {
            int rank = Prog.Rank("m_elements");
            return rank > 0 && lastElement != "" && lastElement != element && Time.time < elementEnd ? 1.12f + .04f * (rank - 1) : 1f;
        }

        void ElementLanded(Run c, string element)
        {
            if (!c.authority) return;
            CareerTrials.Record(owner, element, 1);
            lastElement = element;
            elementEnd = Time.time + 5;
        }

        IEnumerator Burn(EnemyController e, int value, string map)
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(1f);
                if (e == null || e.IsDead || !Valid(map)) yield break;
                SecondaryHit(e, value);
                SkillFx.Spawn("fx_flame", e.Center + Random.insideUnitCircle * .2f, Color.white, .35f, SkillFx.TopOrder + 2).Move(Vector2.up * 1.5f).Scale(1.6f, .4f);
            }
        }

        /// <summary>홍련구: a fireball flies to the target and bursts; the monsters caught burn for 3 s.</summary>
        IEnumerator Fireball(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 point);
            Vector2 at = owner.Center + c.dir * .4f;
            var fx = CareerFx.Clip("m_fireball", at, (point - at).sqrMagnitude > .01f ? (point - at).normalized : c.dir, 1.05f, 18f, VfxLayer.Top, true, null, false, 10f, true);
            const float speed = 13f;
            while (true)
            {
                if (!Live(c)) { fx?.Stop(); yield break; }
                Vector2 to = point - at;
                float step = speed * Time.deltaTime;
                bool arrived = to.magnitude <= step;
                Vector2 next = arrived ? point : at + to.normalized * step;
                fx?.Place(next);
                CareerFx.OrbTrail(next - to.normalized * .35f, to.normalized, FireColor, "fx_flame");
                if (arrived || Corridor(at, next, .3f).Count > 0) { at = next; break; }
                at = next;
                yield return null;
            }
            fx?.Stop();
            CareerFx.Clip("m_explode", at + Vector2.up * .2f, Vector2.zero, c.n.radius / 1.25f, 22f, VfxLayer.Top, false);
            SkillFx.Spawn("fx_scorch", at, Color.white, 1.6f, SkillFx.GroundOrder + 4).Scale(c.n.radius * .7f, c.n.radius * 1.1f).Fade(FxFade.Late);
            Sound("c_fire");
            float power = ElementPower("fire");
            bool any = false;
            int burn = Mathf.RoundToInt(c.n.damage * .07f);
            foreach (var e in Enemies(at, c.n.radius))
                if (Strike(c, e, Mathf.RoundToInt(c.n.damage * power), at, 7f, 1, "c_fire"))
                {
                    any = true;
                    if (c.authority) StartCoroutine(Burn(e, Mathf.Max(1, burn), c.map));
                }
            if (any) ElementLanded(c, "fire");
        }

        /// <summary>빙결삼창: three piercing ice spears in a fan; each monster is hit once and frozen.</summary>
        IEnumerator IceSpears(Run c)
        {
            Sound("c_ice", .8f);
            var shared = new HashSet<EnemyController>();
            var landed = new bool[1];
            float power = ElementPower("ice");
            for (int i = -1; i <= 1; i++)
            {
                if (!Live(c)) yield break;
                StartCoroutine(IceSpear(c, Rotate(c.dir, i * 20f), shared, power, landed));
                yield return new WaitForSeconds(.04f);
            }
            yield return new WaitForSeconds(c.n.range / 15f);
            if (landed[0]) ElementLanded(c, "ice");
        }

        IEnumerator IceSpear(Run c, Vector2 dir, HashSet<EnemyController> shared, float power, bool[] landed)
        {
            const float speed = 15f;
            Vector2 at = owner.Center + dir * .4f;
            var fx = CareerFx.Spear(at, dir, IceColor, true);
            float travelled = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { fx.Kill(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + dir * step;
                fx.MoveTo(next);
                CareerFx.OrbTrail(next, dir, IceColor, "fx_snow");
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (shared.Add(e) && Strike(c, e, Mathf.RoundToInt(c.n.damage * power), at, 4f, 1, "c_ice"))
                    {
                        landed[0] = true;
                        Freeze(c, e, c.s.duration);
                        SkillVisuals.Sparks(e.Center, IceColor, 6, 5f, .2f);
                        CareerFx.Clip("m_icebloom", e.Position, Vector2.zero, 1f, 22f, VfxLayer.AtFeet, false);
                    }
                at = next;
                travelled += step;
                yield return null;
            }
            fx.Kill();
            SkillVisuals.Flash(at, IceColor, 1f, .15f);
            Fx.Burst("fx_chip", at, 3, 2.5f, .5f);
        }

        /// <summary>연쇄전격: a bolt to the monster in front, jumping on to up to five.</summary>
        IEnumerator ChainLightning(Run c)
        {
            Vector2 from = owner.Center + c.dir * .35f;
            var e = Target(c.dir, c.n.range, out Vector2 point);
            Sound("c_thunder", .9f);
            CareerFx.Clip("m_spark", from, Vector2.zero, .8f, 30f, VfxLayer.Top, false);
            if (e == null) { SkillVisuals.ArcBolt(from, from + c.dir * 2.5f, false); yield break; }
            float power = ElementPower("storm");
            var seen = new HashSet<EnemyController>();
            bool any = false;
            for (int i = 0; i < c.s.hits && e != null; i++)
            {
                if (!Live(c)) yield break;
                seen.Add(e);
                SkillVisuals.ArcBolt(from, e.Center, true);
                CareerFx.Clip("m_spark", e.Center, Vector2.zero, 1.2f, 30f, VfxLayer.Top, false);
                if (Strike(c, e, Mathf.RoundToInt(c.n.damage * power), from, 3f, 1, "c_thunder")) { any = true; Stun(c, e, .15f); }
                from = e.Center;
                yield return new WaitForSeconds(.08f);
                e = null;
                foreach (var next in Enemies(from, c.n.radius)) if (!seen.Contains(next)) { e = next; break; }
            }
            if (any) ElementLanded(c, "storm");
        }

        /// <summary>성운탄: three stars curve after the monsters in front and burst on them.</summary>
        IEnumerator StarShots(Run c)
        {
            var targets = Fan(owner.Center, c.dir, c.n.range, 150f);
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                var target = targets.Count > 0 ? targets[i % targets.Count] : null;
                StartCoroutine(Star(c, target, i));
                Sound("c_arcane", .5f);
                yield return new WaitForSeconds(.18f);
            }
        }

        IEnumerator Star(Run c, EnemyController target, int index)
        {
            Vector2 start = owner.Center + c.dir * .3f;
            Vector2 fallback = start + Rotate(c.dir, (index - 1) * 18f) * c.n.range * .8f;
            Vector2 side = new Vector2(-c.dir.y, c.dir.x) * (index - 1) * 1.1f;
            var fx = CareerFx.Clip("m_star", start, Vector2.zero, 1.1f, 20f, VfxLayer.Top, false, null, false, 10f, true);
            const float flight = .42f;
            Vector2 at = start;
            for (float t = 0; t < flight; t += Time.deltaTime)
            {
                if (!Live(c)) { fx?.Stop(); yield break; }
                float u = Mathf.Clamp01((t + Time.deltaTime) / flight);
                Vector2 end = target != null && !target.IsDead ? target.Center : fallback;
                Vector2 next = Vector2.Lerp(start, end, u * u) + side * Mathf.Sin(u * Mathf.PI) * .8f;
                CareerFx.OrbTrail(next, (next - at).normalized, CareerFx.Violet);
                fx?.Place(next);
                at = next;
                yield return null;
            }
            fx?.Stop();
            CareerFx.Clip("m_starburst", at, Vector2.zero, c.n.radius / .75f, 26f, VfxLayer.Top, false);
            foreach (var e in Enemies(at, c.n.radius)) Strike(c, e, c.n.damage, at, 5f, 1, "c_arcane");
        }

        /// <summary>차원도약: teleport, take a short shield, and leave an echo behind that explodes.</summary>
        IEnumerator PhaseBlink(Run c)
        {
            Vector2 origin = owner.Center;
            if (!owner.NetPuppet) owner.SkillBlink(c.dir, c.n.range);
            GiveShield(c, owner, .12f, c.s.duration);
            // Gates open where the mage leaves and arrives; a violet echo of the body stays behind and bursts.
            CareerFx.Clip("m_portal", origin, Vector2.zero, 1.1f, 24f, VfxLayer.Top, false);
            CareerFx.Clip("m_portal", owner.Center, Vector2.zero, 1.1f, 24f, VfxLayer.Top, false);
            CareerFx.Ghost(owner, new Color(.75f, .55f, 1f, .9f), .3f);
            Sound("c_arcane", .7f);
            yield return new WaitForSeconds(.25f);
            if (!Live(c)) yield break;
            CareerFx.Clip("m_starburst", origin, Vector2.zero, c.n.radius / .75f, 26f, VfxLayer.Top, false);
            Sound("c_heavy", .6f);
            foreach (var e in Enemies(origin, c.n.radius)) Strike(c, e, c.n.damage, origin, 8f, 1, "c_arcane");
        }

        /// <summary>중력 균열: a rift that drags monsters into its middle, ticking, then collapses.</summary>
        IEnumerator GravityRift(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 point);
            bool hole = SkillFx.HasImage("fxi_blackhole");
            float hk = c.n.radius * .9f;
            if (hole)
            {
                // [VFX] A black hole holding a galaxy: the drawn disc turns slowly, a dark halo around it, and stars keep
                // falling into the middle for the whole duration.
                SkillFx.Spawn("fx_glow", point, new Color(.35f, .15f, .7f, .55f), c.s.duration + .3f, SkillFx.GroundOrder + 9).Additive().Scale(new Vector2(hk * 1.2f, hk * .85f), new Vector2(hk * 2.6f, hk * 1.9f)).Fade(FxFade.InOut);
                SkillFx.Spawn(SkillFx.Pick("fxi_blackhole", "fx_glow"), point, Color.white, c.s.duration + .3f, SkillFx.GroundOrder + 10).Spin(-70f).Scale(new Vector2(hk * .3f, hk * .22f), new Vector2(hk, hk * .72f)).Fade(FxFade.Late);
                StartCoroutine(StarsInto(point, c.n.radius, c.s.duration));
            }
            else CareerFx.Clip("m_vortex", point, Vector2.zero, c.n.radius / 1.56f, 14f, VfxLayer.Ground, false, null, false, c.s.duration + .3f, true).Squash(1f, .72f).FadeOut(.3f);
            Sound("c_arcane");
            float interval = c.s.duration / c.s.hits;
            for (int i = 0; i < c.s.hits; i++)
            {
                yield return new WaitForSeconds(interval);
                if (!Live(c)) yield break;
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 4f + Random.Range(-.2f, .2f);
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    SkillFx.Spawn("fx_streak", point + d * c.n.radius, new Color(.75f, .6f, 1f, .9f), .3f, SkillFx.TopOrder)
                        .Move(-d * c.n.radius / .3f, 1f).FaceMotion().Scale(1.2f, .4f);
                }
                // Knockback pushes away from the source (from the feet): a source beyond each monster pulls it into the rift.
                foreach (var e in Enemies(point, c.n.radius))
                {
                    Vector2 inward = point - e.Center;
                    bool pull = !e.IsBoss && inward.sqrMagnitude > .16f;
                    Strike(c, e, c.n.damage, pull ? e.Position - inward : e.Position, pull ? 3.5f : 0f, 0, "c_arcane");
                }
            }
            yield return new WaitForSeconds(CutGap);
            if (!Live(c)) yield break;
            CareerFx.Clip("m_collapse", point, Vector2.zero, c.n.radius / 1.4f, 26f, VfxLayer.Top, false);
            if (hole) SkillFx.Spawn(SkillFx.Pick("fxi_blackhole", "fx_glow"), point, Color.white, .3f, SkillFx.TopOrder + 2).Spin(-720f).Scale(new Vector2(hk, hk * .72f), new Vector2(.05f, .04f)).Fade(FxFade.None);
            Sound("c_heavy");
            foreach (var e in Enemies(point, c.n.radius)) Strike(c, e, c.n.damage * 2, point, 10f, 2, "c_arcane");
        }

        /// <summary>Stars of every colour spiral into the black hole while it is open (about 14 a second).</summary>
        IEnumerator StarsInto(Vector2 point, float radius, float seconds)
        {
            var tones = new[] { new Color(1f, 1f, 1f, 1f), new Color(.7f, .85f, 1f, 1f), new Color(.85f, .6f, 1f, 1f), new Color(.5f, 1f, .95f, 1f) };
            for (float t = 0f; t < seconds; t += .07f)
            {
                if (owner == null) yield break;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * .72f);
                // Starts on the rim and falls in along a curve (a sideways drift turns it into a spiral).
                Vector2 side = new Vector2(-d.y, d.x) * radius * 1.4f;
                SkillFx.Spawn("fx_sparkle", point + d * radius * Random.Range(.9f, 1.25f), tones[Random.Range(0, tones.Length)], .55f, SkillFx.GroundOrder + 11)
                    .Move(-d * radius / .55f + side, 2.5f).Scale(Random.Range(.5f, .9f), .15f).Fade(FxFade.Late);
                yield return new WaitForSeconds(.07f);
            }
        }

        /// <summary>천체 붕괴: a fire, an ice and a lightning star fall in turn, then the middle collapses.</summary>
        IEnumerator Cataclysm(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 center);
            CareerFx.Clip("m_vortex", center, Vector2.zero, c.n.radius / 1.56f, 10f, VfxLayer.Ground, false, new Color(1f, 1f, 1f, .6f), false, 2f, true).Squash(1f, .72f).FadeOut(.4f);
            string[] elements = { "fire", "ice", "storm" };
            // Fire, ice and lightning stars fall in turn around the centre (more of them after the 2026-10-06 balance).
            int stars = Mathf.Max(3, c.s.hits - 1);
            for (int i = 0; i < stars; i++)
            {
                if (!Live(c)) yield break;
                Vector2 ground = center + Rotate(c.dir, 90f + i * 360f / stars) * c.n.radius * .45f;
                StartCoroutine(ElementStar(c, ground, elements[i % 3]));
                yield return new WaitForSeconds(.3f);
            }
            yield return new WaitForSeconds(.45f);
            if (!Live(c)) yield break;
            // The finale: a great arcane sphere sucks inward, flashes and bursts.
            CareerFx.Clip("m_collapse", center, Vector2.zero, c.n.radius / 1.4f, 22f, VfxLayer.Top, false);
            yield return new WaitForSeconds(.22f);
            if (!Live(c)) yield break;
            CareerFx.Clip("impact", center, Vector2.zero, c.n.radius / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Violet);
            Sound("c_heavy");
            Sound("c_arcane", .7f);
            foreach (var e in Enemies(center, c.n.radius)) Strike(c, e, Mathf.RoundToInt(c.n.damage * 1.5f), center, 12f, 2, "c_arcane");
        }

        IEnumerator ElementStar(Run c, Vector2 ground, string element)
        {
            const float fall = .45f, radius = 1.8f;
            if (element == "fire") SkillVisuals.MeteorFall(ground, fall, radius);
            else
            {
                var color = element == "ice" ? IceColor : StormColor;
                SkillFx.Spawn("fx_ring", ground, color, fall, SkillFx.GroundOrder + 8).Scale(radius * 1.2f, radius * .45f).Fade(FxFade.None);
                var head = element == "ice" ? SkillVisuals.FrostOrbHead(ground + new Vector2(-2.4f, 5.5f), new Vector2(.4f, -1f)) : CareerFx.Orb(ground + new Vector2(2.4f, 5.5f), color, 1.2f, "fx_zap");
                Vector2 start = ground + new Vector2(element == "ice" ? -2.4f : 2.4f, 5.5f);
                for (float t = 0; t < fall; t += Time.deltaTime)
                {
                    if (!Live(c)) { head.Kill(); yield break; }
                    head.MoveTo(Vector2.Lerp(start, ground, Mathf.Clamp01((t + Time.deltaTime) / fall)));
                    yield return null;
                }
                head.Kill();
            }
            if (fall > 0f && element == "fire") yield return new WaitForSeconds(fall);
            if (!Live(c)) yield break;
            if (element == "fire")
            {
                CareerFx.Clip("m_explode", ground + Vector2.up * .3f, Vector2.zero, radius / 1.2f, 22f, VfxLayer.Top, false);
                SkillFx.Spawn("fx_scorch", ground, Color.white, 2f, SkillFx.GroundOrder + 4).Scale(radius * .7f, radius * 1.1f).Fade(FxFade.Late);
                Sound("c_fire");
            }
            else if (element == "ice")
            {
                SkillVisuals.FrostOrbBurst(ground + Vector2.up * .3f, ground, radius);
                for (int k = -1; k <= 1; k++) CareerFx.Clip("m_icebloom", ground + new Vector2(k * .6f, -.1f * Mathf.Abs(k)), Vector2.zero, 1.3f, 20f, VfxLayer.AtFeet, false);
                Sound("c_ice");
            }
            else
            {
                SkillVisuals.Thunder(ground);
                SkillVisuals.Thunder(ground + Random.insideUnitCircle * .6f);
                CareerFx.Clip("m_spark", ground + Vector2.up * .3f, Vector2.zero, 2.2f, 26f, VfxLayer.Top, false);
                Sound("c_thunder");
            }
            float power = ElementPower(element);
            bool any = false;
            foreach (var e in Enemies(ground, radius))
            {
                if (!Strike(c, e, Mathf.RoundToInt(c.n.damage * power), ground, 8f, 2, element == "fire" ? "c_fire" : element == "ice" ? "c_ice" : "c_thunder")) continue;
                any = true;
                if (element == "ice") Freeze(c, e, 1.5f);
                else if (element == "storm") Stun(c, e, .3f);
                else if (c.authority) StartCoroutine(Burn(e, Mathf.Max(1, Mathf.RoundToInt(c.n.damage * .06f)), c.map));
            }
            if (any) ElementLanded(c, element);
        }
    }
}
