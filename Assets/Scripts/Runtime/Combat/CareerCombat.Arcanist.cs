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
                case "nebula": yield return Nebula(c); break;
                case "blink": yield return PhaseBlink(c); break;
                case "barrier": MagicBarrier(c); break;
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
            var fx = CareerFx.Gen("m2_fireball", "m_fireball", at, (point - at).sqrMagnitude > .01f ? (point - at).normalized : c.dir, 1.4f, 1.05f, 18f, VfxLayer.Top, true, null, 10f, true);
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
            CareerFx.Gen("m2_fire_explosion", "m_explode", at + Vector2.up * .2f, Vector2.zero, c.n.radius * 2.2f, c.n.radius / 1.25f, 22f, VfxLayer.Top, false);
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
            // [VFX] The generated crystal spear when it exists (Docs/PLAN_SKILL_VFX.md), else the old glow spear.
            var spear = VfxLibrary.Has("m2_ice_spear") ? CareerFx.Gen("m2_ice_spear", null, at, dir, 1.6f, 0f, 24f, VfxLayer.Top, true, null, 10f, true) : null;
            var fx = spear == null ? CareerFx.Spear(at, dir, IceColor, true) : null;
            float travelled = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { fx?.Kill(); spear?.Stop(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + dir * step;
                fx?.MoveTo(next);
                spear?.Place(next);
                CareerFx.OrbTrail(next, dir, IceColor, "fx_snow");
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (shared.Add(e) && Strike(c, e, Mathf.RoundToInt(c.n.damage * power), at, 4f, 1, "c_ice"))
                    {
                        landed[0] = true;
                        Freeze(c, e, c.s.duration);
                        SkillVisuals.Sparks(e.Center, IceColor, 6, 5f, .2f);
                        CareerFx.Gen("m2_ice_shatter", "m_icebloom", e.Position, Vector2.zero, 1.4f, 1f, 22f, VfxLayer.AtFeet, false);
                    }
                at = next;
                travelled += step;
                yield return null;
            }
            fx?.Kill();
            spear?.Stop();
            SkillVisuals.Flash(at, IceColor, 1f, .15f);
            Fx.Burst("fx_chip", at, 3, 2.5f, .5f);
        }

        /// <summary>연쇄전격: a bolt to the monster in front, jumping on to up to five.</summary>
        /// <summary>[VFX] One lightning link: the generated bolt stretched from a to b (flickering), else the old arc.</summary>
        static void Bolt(Vector2 a, Vector2 b, bool strong)
        {
            if (!VfxLibrary.Has("m2_lightning_bolt")) { SkillVisuals.ArcBolt(a, b, strong); return; }
            Vector2 d = b - a;
            float length = Mathf.Max(.3f, d.magnitude);
            // The strip frame is 4:1; keep the bolt about half a unit thick whatever its length.
            CareerFx.Gen("m2_lightning_bolt", null, (a + b) * .5f, d, length, 0f, 24f, VfxLayer.Top, true, null, strong ? .2f : .14f, true)
                ?.Squash(1f, (strong ? .55f : .4f) / (length * .25f));
        }

        IEnumerator ChainLightning(Run c)
        {
            Vector2 from = owner.Center + c.dir * .35f;
            var e = Target(c.dir, c.n.range, out Vector2 point);
            Sound("c_thunder", .9f);
            CareerFx.Gen("m2_lightning_hit", "m_spark", from, Vector2.zero, .9f, .8f, 30f, VfxLayer.Top, false);
            if (e == null) { Bolt(from, from + c.dir * 2.5f, false); yield break; }
            float power = ElementPower("storm");
            var seen = new HashSet<EnemyController>();
            bool any = false;
            for (int i = 0; i < c.s.hits && e != null; i++)
            {
                if (!Live(c)) yield break;
                seen.Add(e);
                Bolt(from, e.Center, true);
                CareerFx.Gen("m2_lightning_hit", "m_spark", e.Center, Vector2.zero, 1.3f, 1.2f, 30f, VfxLayer.Top, false);
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
            CareerFx.Gen("m2_blink", "m_portal", origin, Vector2.zero, 1.6f, 1.1f, 24f, VfxLayer.Top, false);
            CareerFx.Gen("m2_blink", "m_portal", owner.Center, Vector2.zero, 1.6f, 1.1f, 24f, VfxLayer.Top, false);
            CareerFx.Ghost(owner, new Color(.75f, .55f, 1f, .9f), .3f);
            Sound("c_arcane", .7f);
            yield return new WaitForSeconds(.25f);
            if (!Live(c)) yield break;
            CareerFx.Clip("m_starburst", origin, Vector2.zero, c.n.radius / .75f, 26f, VfxLayer.Top, false);
            Sound("c_heavy", .6f);
            foreach (var e in Enemies(origin, c.n.radius)) Strike(c, e, c.n.damage, origin, 8f, 1, "c_arcane");
        }

        /// <summary>중력 균열: a rift that drags monsters into its middle, ticking, then collapses.</summary>
        /// <summary>매직 배리어: a violet veil closes around the mage and turns into a big shield (temporary HP).</summary>
        void MagicBarrier(Run c)
        {
            Pose(.2f, 0);
            var violet = new Color(.75f, .55f, 1f, .9f);
            CareerFx.Clip("g_dome", owner.Center, Vector2.zero, 1.3f, 24f, VfxLayer.Top, false, violet).Follow(owner.transform, owner.Center - owner.Position + Vector2.up * .1f);
            SkillVisuals.Flash(owner.Center, violet, 1.8f, .25f);
            SkillVisuals.Sparks(owner.Center, violet, 10, 3.5f, .4f);
            Sound("c_arcane", .7f);
            if (c.authority) AddShield(Mathf.RoundToInt(owner.Health.Max * c.s.power * c.Scale), c.s.duration, Career.Arcanist);
        }

        /// <summary>중력 균열 pull: catch radius x the hit radius, pull speed (world units/s, faster at the edge).</summary>
        const float RiftPullReach = 1.6f, RiftPullSpeed = 4.5f;

        IEnumerator GravityRift(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 point);
            bool hole = SkillFx.HasImage("fxi_blackhole");
            float hk = c.n.radius * .9f;
            bool v3 = VfxLibrary.Has("m3_rift_core") && VfxLibrary.Has("m3_rift_inflow");
            if (v3)
            {
                // [VFX v3] A floating gravity sphere over the spot, an inward spiral of energy and rocks covering the whole
                // pull area under it, and stars streaming in; it implodes at the end (m3_rift_implode).
                hole = false;
                float reach = c.n.radius * RiftPullReach;
                CareerFx.Gen("m3_rift_inflow", null, point, Vector2.zero, reach * 2.1f, 0f, 14f, VfxLayer.Ground, false, new Color(1f, 1f, 1f, .9f), c.s.duration + .2f, true)?.FadeOut(.3f);
                CareerFx.Gen("m3_rift_core", null, point + Vector2.up * .9f, Vector2.zero, c.n.radius * 1.15f, 0f, 12f, VfxLayer.Top, false, null, c.s.duration + .2f, true)?.FadeOut(.2f);
                SkillFx.Spawn("fx_glow", point, new Color(.45f, .15f, .85f, .45f), c.s.duration + .2f, SkillFx.GroundOrder + 8).Additive().Scale(new Vector2(reach * .6f, reach * .42f), new Vector2(reach * 1.7f, reach * 1.2f)).Fade(FxFade.InOut);
                StartCoroutine(StarsInto(point, reach, c.s.duration));
            }
            else if (VfxLibrary.Has("m2_rift"))
            {
                // [VFX] A torn rift in the floor (generated, looping, no spinning disc) with stars falling into it.
                hole = false;
                CareerFx.Gen("m2_rift", null, point, Vector2.zero, c.n.radius * 2.2f, 0f, 10f, VfxLayer.Ground, false, null, c.s.duration + .3f, true)?.FadeOut(.3f);
                StartCoroutine(StarsInto(point, c.n.radius, c.s.duration));
            }
            else if (hole)
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
                // [BALANCE] A steady pull the whole time (wider than the hit radius), not only a nudge on each tick.
                for (float t = 0f; t < interval; t += Time.deltaTime)
                {
                    if (!Live(c)) yield break;
                    if (c.authority)
                        foreach (var e in Enemies(point, c.n.radius * RiftPullReach))
                        {
                            float d = Vector2.Distance(e.Position, point);
                            e.PullTowards(point, RiftPullSpeed * Mathf.Lerp(.6f, 1.2f, d / (c.n.radius * RiftPullReach)), .12f);
                        }
                    yield return null;
                }
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
            if (v3 && VfxLibrary.Has("m3_rift_implode"))
            {
                CareerFx.Gen("m3_rift_implode", null, point + Vector2.up * .6f, Vector2.zero, c.n.radius * 2.6f, 0f, 18f, VfxLayer.Top, false);
                SkillVisuals.Flash(point, new Color(.8f, .5f, 1f, .9f), c.n.radius * 2f, .2f);
                if (owner.IsLocal) Game.Camera?.Shake(.25f, .3f);
            }
            else CareerFx.Clip("m_collapse", point, Vector2.zero, c.n.radius / 1.4f, 26f, VfxLayer.Top, false);
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
        /// <summary>[CHARGE] Where 성운 폭발 lands: the monster the caster faces in reach, else ahead (also the charge marker).</summary>
        public Vector2 ChargePoint(float range)
        {
            Target(owner.AutoAim(range + 1.5f), range, out Vector2 point);
            return point;
        }

        /// <summary>성운 폭발: the charged burst (damage and radius already scaled by the charge in SkillCaster).</summary>
        IEnumerator Nebula(Run c)
        {
            Vector2 center = ChargePoint(c.n.range);
            bool burst = VfxLibrary.Has("m2_nebula_burst");
            // [VFX] Its own nebula burst (no longer the same collapse as 천체 붕괴) when the generated art exists.
            if (burst) CareerFx.Gen("m2_nebula_burst", null, center, Vector2.zero, c.n.radius * 2.4f, 0f, 18f, VfxLayer.Top, false);
            else CareerFx.Clip("m_collapse", center, Vector2.zero, c.n.radius / 1.4f, 22f, VfxLayer.Top, false);
            yield return new WaitForSeconds(.2f);
            if (!Live(c)) yield break;
            if (!burst) CareerFx.Clip("m_starburst", center, Vector2.zero, c.n.radius / 1.5f, 24f, VfxLayer.Top, false);
            CareerFx.Clip("impact", center, Vector2.zero, c.n.radius / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Violet);
            Sound("c_heavy");
            Sound("c_arcane", .8f);
            Feel(c.n.radius >= 4.5f ? 2 : 1, Vector2.down);
            foreach (var e in Enemies(center, c.n.radius)) Strike(c, e, c.n.damage, center, 10f, 2, "c_arcane");
        }

        /// <summary>
        /// 천체 붕괴 (v3, 2026-10-12, Docs/PLAN_SKILL_VFX.md): around the mage (centred on the caster) a fire, an ice and a
        /// lightning meteor fall in turn; each leaves its terrain where it lands, a magma pool, a glacier, a cluster of
        /// charged crystals. Then the three terrains are dragged into the centre, pulling the monsters with them (held,
        /// bosses too), crush together and the collapse bursts (150%), throwing elemental shards and rings outward.
        /// </summary>
        IEnumerator Cataclysm(Run c)
        {
            const float PerElement = .55f, Fall = .45f, Gather = 1.3f, Press = .2f;
            Vector2 center = owner.Position;
            float R = c.n.radius;
            int order = SkillFx.GroundOrder + 8;
            string[] elements = { "fire", "ice", "storm" };
            Color[] colors = { FireColor, IceColor, new Color(1f, .92f, .4f, .95f) };
            var spots = new Vector2[3];
            var terrain = new ElementTerrainFx[3];
            void KillAll() { foreach (var t in terrain) if (t != null) t.Kill(); }
            Sound("c_arcane", .6f);
            // 1) Three meteors fall in turn around the mage; each leaves its terrain where it lands.
            for (int i = 0; i < 3; i++)
            {
                spots[i] = center + Rotate(c.dir, 90f + i * 120f) * R * .45f;
                StartCoroutine(ElementStar(c, spots[i], elements[i]));
                for (float t = 0f; t < PerElement; t += Time.deltaTime)
                {
                    if (!Live(c)) { KillAll(); yield break; }
                    if (terrain[i] == null && t >= Fall) terrain[i] = ElementTerrainFx.Spawn(elements[i], spots[i], elements[i] == "fire" ? 3.4f : 2.6f, colors[i]);
                    yield return null;
                }
                if (terrain[i] == null) terrain[i] = ElementTerrainFx.Spawn(elements[i], spots[i], elements[i] == "fire" ? 3.4f : 2.6f, colors[i]);
            }
            // 2) The terrains are dragged into the centre, the monsters with them.
            var foes = Enemies(center, R * 1.15f);
            if (c.authority) foreach (var e in foes) { e.Stagger(Gather + Press + .2f); StunStars.Attach(e); }
            Sound("c_arcane", .8f);
            float nextPull = 0f, nextSpark = 0f;
            float[] sizes = { terrain[0].Size, terrain[1].Size, terrain[2].Size };
            for (float t = 0f; t < Gather; t += Time.deltaTime)
            {
                if (!Live(c)) { KillAll(); yield break; }
                float k = t / Gather, ease = k * k;
                for (int i = 0; i < 3; i++)
                {
                    // Spiral in: closer and a little round the centre.
                    Vector2 from = spots[i] - center;
                    terrain[i].Position = center + (Vector2)(Quaternion.Euler(0f, 0f, 90f * ease) * from) * (1f - ease * .85f);
                    terrain[i].Size = sizes[i] * (1f - ease * .55f);
                    terrain[i].Brightness = 1f + ease * 1.5f;
                }
                if (t >= nextSpark)
                {
                    nextSpark = t + .06f;
                    int i = Random.Range(0, 3);
                    SkillVisuals.Sparks(terrain[i].Position, colors[i], 2, 3f, .25f);
                }
                if (c.authority && t >= nextPull)
                {
                    nextPull = t + .2f;
                    foreach (var e in foes) if (e != null && !e.IsDead) e.PullTowards(center, R * .55f, .25f);
                }
                yield return null;
            }
            // 3) They crush together at the centre.
            for (float t = 0f; t < Press; t += Time.deltaTime)
            {
                if (!Live(c)) { KillAll(); yield break; }
                float k = t / Press;
                for (int i = 0; i < 3; i++)
                {
                    terrain[i].Position = Vector2.Lerp(terrain[i].Position, center, k);
                    terrain[i].Size = Mathf.Lerp(terrain[i].Size, .2f, k);
                    terrain[i].Brightness = 3f;
                }
                yield return null;
            }
            KillAll();
            // 4) The collapse bursts: elemental shards and rings thrown outward.
            CareerFx.Gen("m2_cataclysm_collapse", "m_collapse", center, Vector2.zero, R * 2.2f, R / 1.4f, 22f, VfxLayer.Top, false);
            CareerFx.Clip("impact", center, Vector2.zero, R / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Violet);
            for (int i = 0; i < 3; i++)
            {
                StartCoroutine(FlungRing(center, R, colors[i], order, .08f * i));
                Vector2 d = Rotate(c.dir, 90f + i * 120f);
                GlassBurstFx.Burst(center - d * 1.2f, center + d * 1.2f, 12, SkillFx.TopOrder + 3, colors[i]);
            }
            SkillVisuals.Flash(center, new Color(.9f, .8f, 1f, .85f), R * .8f, .2f);
            if (owner.IsLocal) Game.Camera?.Shake(.35f, .35f);
            Sound("c_heavy");
            Sound("c_arcane", .7f);
            foreach (var e in Enemies(center, R)) Strike(c, e, Mathf.RoundToInt(c.n.damage * 1.5f), center, 12f, 2, "c_arcane");
        }

        /// <summary>A ring thrown outward from the collapse: it grows past the radius and fades.</summary>
        IEnumerator FlungRing(Vector2 center, float R, Color color, int order, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var ring = RingFx.Spawn(center, R * .1f, color, order);
            ring.Width = .12f;
            const float Life = .45f;
            for (float t = 0f; t < Life; t += Time.deltaTime)
            {
                float k = t / Life;
                ring.Radius = Mathf.Lerp(R * .1f, R * 1.15f, 1f - (1f - k) * (1f - k));
                ring.Alpha = 1f - k;
                ring.Brightness = 2f - k;
                yield return null;
            }
            ring.Kill();
        }

        IEnumerator ElementStar(Run c, Vector2 ground, string element)
        {
            const float fall = .45f, radius = 3.6f; // [BALANCE 2026-10-11] the awakening covers twice the area
            string meteor = "m2_meteor_" + element;
            if (VfxLibrary.Has(meteor))
            {
                // [VFX] The generated meteor falls along the diagonal it is drawn on, with a ring marking the landing.
                SkillFx.Spawn("fx_ring", ground, element == "fire" ? FireColor : element == "ice" ? IceColor : StormColor, fall, SkillFx.GroundOrder + 8).Scale(radius * 1.2f, radius * .45f).Fade(FxFade.None);
                Vector2 start = ground + new Vector2(-2.6f, 5.5f);
                var rock = CareerFx.Gen(meteor, null, start, Vector2.zero, 1.8f, 0f, 24f, VfxLayer.Top, false, null, fall + .1f, true);
                for (float t = 0; t < fall; t += Time.deltaTime)
                {
                    if (!Live(c)) { rock?.Stop(); yield break; }
                    rock?.Place(Vector2.Lerp(start, ground + new Vector2(0f, .4f), Mathf.Clamp01((t + Time.deltaTime) / fall)));
                    yield return null;
                }
                rock?.Stop();
            }
            else if (element == "fire") SkillVisuals.MeteorFall(ground, fall, radius);
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
            if (fall > 0f && element == "fire" && !VfxLibrary.Has(meteor)) yield return new WaitForSeconds(fall);
            if (!Live(c)) yield break;
            if (element == "fire")
            {
                CareerFx.Gen("m2_fire_explosion", "m_explode", ground + Vector2.up * .3f, Vector2.zero, radius * 2.2f, radius / 1.2f, 22f, VfxLayer.Top, false);
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
