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
            // [CH3 SPEC] 연속 순환 (m_flow B): every tenth cast of a no-cooldown spell gives back 3% MP.
            if (c.authority && c.s.cooldown <= 0f && Prog.Rank("m_flow") > 0 && Prog.SpecOf("m_flow") == 'b' && ++flowCasts % 10 == 0) GainManaShare(.03f);
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
            if (rank <= 0) return 1f;
            bool fresh = Time.time < elementEnd;
            // [CH3 SPEC] 원소 집중 (A): the third landing of the same element in a row +18%. 원소 공명 (B): a different element +8%.
            char spec = Prog.SpecOf("m_elements");
            if (spec == 'a') return fresh && element == lastElement && sameElementRun >= 2 ? 1.18f : 1f;
            bool other = lastElement != "" && lastElement != element && fresh;
            if (spec == 'b') return other ? 1.08f : 1f;
            return other ? 1.12f + .04f * (rank - 1) : 1f;
        }

        /// <param name="at">Where it landed (원소 공명 bursts there when the element changed).</param>
        void ElementLanded(Run c, string element, Vector2? at = null)
        {
            if (!c.authority) return;
            CareerTrials.Record(owner, element, 1);
            bool fresh = Time.time < elementEnd;
            bool changed = lastElement != "" && lastElement != element && fresh;
            sameElementRun = fresh && element == lastElement ? sameElementRun + 1 : 1;
            if (changed && at.HasValue && Prog.Rank("m_elements") > 0 && Prog.SpecOf("m_elements") == 'b') ElementBurst(at.Value, element);
            lastElement = element;
            elementEnd = Time.time + 5;
        }

        IEnumerator Burn(EnemyController e, int value, string map)
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(1f);
                if (e == null || e.IsDead || !Valid(map)) yield break;
                SecondaryHit(e, value, true); // [ARMOR] a burn goes through armour
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
            CareerFx.Gen("m2_fire_explosion", "m_explode", at + Vector2.up * .2f, Vector2.zero, c.n.radius * 2.2f, c.n.radius / 1.25f, 22f, VfxLayer.Top, false, SpecTint(c));
            SkillFx.Spawn("fx_scorch", at, Color.white, 1.6f, SkillFx.GroundOrder + 4).Scale(c.n.radius * .7f, c.n.radius * 1.1f).Fade(FxFade.Late);
            Sound("c_fire");
            float power = ElementPower("fire");
            bool any = false;
            int burn = Mathf.RoundToInt(c.n.damage * .07f);
            // [CH3 SPEC] 홍련 집속탄 (A): the whole fireball on the first monster, no blast or burn. 홍련 연화 (B): burns stack
            // up to three on one monster, each 0.3 x attack a second.
            bool focus = c.n.HasExtra("fire_focus"), stack = c.n.HasExtra("fire_stack");
            if (focus)
            {
                var first = Enemies(at, c.n.radius);
                if (first.Count > 0 && Strike(c, first[0], Mathf.RoundToInt(c.n.damage * power), at, 7f, 2, "c_fire")) any = true;
            }
            else
                foreach (var e in Enemies(at, c.n.radius))
                    if (Strike(c, e, Mathf.RoundToInt(c.n.damage * power), at, 7f, 1, "c_fire"))
                    {
                        any = true;
                        if (!c.authority) continue;
                        if (stack) StartCoroutine(StackBurn(e, Mathf.Max(1, Mathf.RoundToInt(c.n.damage * .3f / .9f)), c.map));
                        else StartCoroutine(Burn(e, Mathf.Max(1, burn), c.map));
                    }
            if (any) ElementLanded(c, "fire", at);
        }

        /// <summary>빙결삼창: three piercing ice spears in a fan; each monster is hit once and frozen.</summary>
        IEnumerator IceSpears(Run c)
        {
            Sound("c_ice", .8f);
            var shared = new HashSet<EnemyController>();
            var landed = new bool[1];
            float power = ElementPower("ice");
            // [CH3 SPEC] 빙결일창 (A): one spear. 빙결육창 (B): six spears over ±35 degrees, each its own (a monster can take
            // several) and each bouncing once off the first monster it hits.
            int count = c.n.projectiles > 0 ? c.n.projectiles : 3;
            float spread = float.IsNaN(c.n.spreadDeg) ? 20f : c.n.spreadDeg;
            bool own = c.n.HasExtra("ice_bounce");
            for (int i = 0; i < count; i++)
            {
                if (!Live(c)) yield break;
                float angle = count == 1 ? 0f : Mathf.Lerp(-spread, spread, i / (float)(count - 1));
                StartCoroutine(IceSpear(c, Rotate(c.dir, angle), own ? new HashSet<EnemyController>() : shared, power, landed));
                yield return new WaitForSeconds(.04f);
            }
            yield return new WaitForSeconds(c.n.range / 15f);
            if (landed[0]) ElementLanded(c, "ice", iceLandedAt);
        }

        IEnumerator IceSpear(Run c, Vector2 dir, HashSet<EnemyController> shared, float power, bool[] landed)
        {
            const float speed = 15f;
            Vector2 at = owner.Center + dir * .4f;
            // [VFX] The generated crystal spear when it exists (Docs/PLAN_SKILL_VFX.md), else the old glow spear.
            var spear = VfxLibrary.Has("m2_ice_spear") ? CareerFx.Gen("m2_ice_spear", null, at, dir, 1.6f, 0f, 24f, VfxLayer.Top, true, null, 10f, true) : null;
            var fx = spear == null ? CareerFx.Spear(at, dir, IceColor, true) : null;
            float travelled = 0f;
            // [CH3 SPEC] 빙결일창 ramps +25% per monster it has gone through (up to three); 빙결육창 bounces once.
            bool ramp = c.n.HasExtra("ice_ramp"), bounce = c.n.HasExtra("ice_bounce");
            bool bounced = false;
            int pierced = 0;
            Color trail = c.n.spec != '\0' ? SkillSpecs.Tint(c.n, IceColor) : IceColor;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { fx?.Kill(); spear?.Stop(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + dir * step;
                fx?.MoveTo(next);
                spear?.Place(next);
                CareerFx.OrbTrail(next, dir, trail, "fx_snow");
                EnemyController turnFrom = null;
                foreach (var e in Corridor(at, next, c.n.radius))
                {
                    if (!shared.Add(e)) continue;
                    float k = ramp ? 1f + .25f * Mathf.Min(3, pierced) : 1f;
                    if (!Strike(c, e, Mathf.RoundToInt(c.n.damage * power * k), at, 4f, ramp && pierced >= 2 ? 2 : 1, "c_ice")) continue;
                    landed[0] = true;
                    iceLandedAt = e.Center;
                    pierced++;
                    Freeze(c, e, Dur(c));
                    SkillVisuals.Sparks(e.Center, trail, 6, 5f, .2f);
                    CareerFx.Gen("m2_ice_shatter", "m_icebloom", e.Position, Vector2.zero, 1.4f, 1f, 22f, VfxLayer.AtFeet, false, SpecTint(c));
                    if (bounce && !bounced) { turnFrom = e; break; }
                }
                at = next;
                travelled += step;
                if (turnFrom != null)
                {
                    bounced = true;
                    EnemyController to = null;
                    foreach (var other in Enemies(turnFrom.Center, 4f)) if (!shared.Contains(other)) { to = other; break; }
                    if (to != null)
                    {
                        dir = (to.Center - turnFrom.Center).normalized;
                        at = turnFrom.Center;
                        travelled = Mathf.Max(travelled, c.n.range - 4.5f);
                    }
                }
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
            // [CH3 SPEC] 직격 뇌전 (A): one target, a shock on the third hit in a row. 광역 연쇄 (B): eight links over 4.5 m,
            // and the last one jumps back to the first monster for half damage.
            bool direct = c.n.HasExtra("storm_direct"), loop = c.n.HasExtra("storm_loop");
            EnemyController first = e, last = e;
            int links = Hits(c);
            for (int i = 0; i < links && e != null; i++)
            {
                if (!Live(c)) yield break;
                seen.Add(e);
                Bolt(from, e.Center, true);
                CareerFx.Gen("m2_lightning_hit", "m_spark", e.Center, Vector2.zero, 1.3f, 1.2f, 30f, VfxLayer.Top, false, SpecTint(c));
                if (Strike(c, e, Mathf.RoundToInt(c.n.damage * power), from, 3f, direct ? 2 : 1, "c_thunder"))
                {
                    any = true;
                    Stun(c, e, .15f);
                    if (direct) StormDirect(c, e);
                }
                last = e;
                from = e.Center;
                yield return new WaitForSeconds(.08f);
                e = null;
                foreach (var next in Enemies(from, c.n.radius)) if (!seen.Contains(next)) { e = next; break; }
            }
            if (loop && first != null && last != first && !first.IsDead && Live(c))
            {
                Bolt(last.Center, first.Center, true);
                Strike(c, first, Mathf.RoundToInt(c.n.damage * power * .5f), last.Center, 2f, 1, "c_thunder");
            }
            if (any) ElementLanded(c, "storm", last != null ? last.Center : (Vector2?)null);
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
            // [CH3 SPEC] 이중 도약 (B): a second press within 4 s leaps back to where the first leap started.
            bool twice = c.n.HasExtra("veil_double");
            if (twice && VeilReturn(c)) yield break;
            Vector2 origin = owner.Center;
            if (!owner.NetPuppet) owner.SkillBlink(c.dir, c.n.range);
            GiveShield(c, owner, twice ? .2f : .12f, c.s.duration);
            if (twice) VeilOpen(c, origin);
            if (c.n.HasExtra("veil_stab"))
            {
                // 차원 찌르기 (A): no echo; the mage lands and runs through the monster in front.
                CareerFx.Gen("m2_blink", "m_portal", owner.Center, Vector2.zero, 1.6f, 1.1f, 24f, VfxLayer.Top, false, SkillSpecs.ColorA);
                Sound("c_arcane", .7f);
                var stab = Target(c.dir, 2.4f, out Vector2 _);
                if (stab != null)
                {
                    CareerFx.Clip("m_starburst", stab.Center, Vector2.zero, .9f, 26f, VfxLayer.Top, false, SkillSpecs.ColorA);
                    Strike(c, stab, c.n.damage, owner.Center, 6f, 2, "c_arcane");
                }
                yield break;
            }
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
            // [CH3 SPEC] 응집 배리어 (A): 55% for 30 s. 공유 배리어 (B): 18% for 60 s (x3 people = the 55%), shared with allies within 4 m; when the
            // mage's own breaks early it slows the monsters around.
            bool dense = c.n.HasExtra("barrier_dense"), shared = c.n.HasExtra("barrier_shared");
            float fraction = dense ? .55f : shared ? .18f : c.s.power, duration = Dur(c);
            if (c.authority) AddShield(Mathf.RoundToInt(owner.Health.Max * fraction * c.Scale), duration, Career.Arcanist);
            if (!shared) return;
            foreach (var p in Allies(owner.Center, 4f))
            {
                if (p == owner) continue;
                if (c.authority) For(p).AddShield(Mathf.RoundToInt(p.Health.Max * fraction * c.Scale), duration, Career.Arcanist);
                CareerFx.Bless(p.Center, SkillSpecs.ColorB, false);
            }
            StartCoroutine(BarrierBreak(Time.time + duration));
        }

        /// <summary>중력 균열 pull: catch radius x the hit radius, pull speed (world units/s, faster at the edge).</summary>
        const float RiftPullReach = 1.6f, RiftPullSpeed = 4.5f;

        IEnumerator GravityRift(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 point);
            // [CH3 SPEC] 특이점 (A): a small core that only pulls for 3 s and collapses once (the whole spell). 광역 균열 (B): a
            // wide rift for 8 s, eight hits, pull twice as strong, a weaker collapse.
            bool singular = c.n.HasExtra("rift_singularity"), wide = c.n.HasExtra("rift_wide");
            float riftTime = Dur(c), pullReach = singular ? 4.2f : c.n.radius * RiftPullReach, pullSpeed = RiftPullSpeed * (wide ? 2f : 1f);
            float collapse = singular ? 1f : wide ? 1.5f : 2f;
            bool hole = SkillFx.HasImage("fxi_blackhole");
            float hk = c.n.radius * .9f;
            bool v3 = VfxLibrary.Has("m3_rift_core") && VfxLibrary.Has("m3_rift_inflow");
            if (v3)
            {
                // [VFX v3] A floating gravity sphere over the spot, an inward spiral of energy and rocks covering the whole
                // pull area under it, and stars streaming in; it implodes at the end (m3_rift_implode).
                hole = false;
                float reach = pullReach;
                CareerFx.Gen("m3_rift_inflow", null, point, Vector2.zero, reach * 2.1f, 0f, 14f, VfxLayer.Ground, false, SpecTint(c) ?? new Color(1f, 1f, 1f, .9f), riftTime + .2f, true)?.FadeOut(.3f);
                CareerFx.Gen("m3_rift_core", null, point + Vector2.up * .9f, Vector2.zero, c.n.radius * 1.15f, 0f, 12f, VfxLayer.Top, false, SpecTint(c), riftTime + .2f, true)?.FadeOut(.2f);
                SkillFx.Spawn("fx_glow", point, new Color(.45f, .15f, .85f, .45f), riftTime + .2f, SkillFx.GroundOrder + 8).Additive().Scale(new Vector2(reach * .6f, reach * .42f), new Vector2(reach * 1.7f, reach * 1.2f)).Fade(FxFade.InOut);
                StartCoroutine(StarsInto(point, reach, riftTime));
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
            int ticks = Hits(c);
            float interval = riftTime / ticks;
            for (int i = 0; i < ticks; i++)
            {
                // [BALANCE] A steady pull the whole time (wider than the hit radius), not only a nudge on each tick.
                for (float t = 0f; t < interval; t += Time.deltaTime)
                {
                    if (!Live(c)) yield break;
                    if (c.authority)
                        foreach (var e in Enemies(point, pullReach))
                        {
                            float d = Vector2.Distance(e.Position, point);
                            e.PullTowards(point, pullSpeed * Mathf.Lerp(.6f, 1.2f, d / pullReach), .12f);
                        }
                    yield return null;
                }
                if (!Live(c)) yield break;
                if (singular) continue; // the core only pulls until it collapses
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
            foreach (var e in Enemies(point, c.n.radius)) Strike(c, e, Mathf.RoundToInt(c.n.damage * collapse), point, 10f, 2, "c_arcane");
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

        /// <summary>[CHARGE] Where 성운 폭발 lands: the monster the caster faces in reach, else ahead (also the charge marker).</summary>
        public Vector2 ChargePoint(float range)
        {
            Target(owner.AutoAim(range + 1.5f), range, out Vector2 point);
            return point;
        }

        /// <summary>성운 폭발: the charged burst (damage and radius already scaled by the charge in SkillCaster).</summary>
        IEnumerator Nebula(Run c)
        {
            // [CH3 SPEC] 성운 수축 (A): the charged burst shrinks to a third of the area for twice the damage (full charge: 0.6x
            // area, 6x damage). 성운 도약 (B): x1.2 damage (3.6x at full charge), then the mage leaps 3 m away with a 15% shield.
            if (c.n.HasExtra("orbit_shrink")) { c.n.radius /= 3f; c.n.damage *= 2; }
            bool leap = c.n.HasExtra("orbit_leap");
            if (leap) c.n.damage = Mathf.RoundToInt(c.n.damage * 1.2f);
            Vector2 center = ChargePoint(c.n.range);
            bool burst = VfxLibrary.Has("m2_nebula_burst");
            // [VFX] Its own nebula burst (not the awakening's) when the generated art exists.
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
            if (!leap) yield break;
            Vector2 away = owner.Center - center;
            if (!owner.NetPuppet) owner.SkillBlink(away.sqrMagnitude > .01f ? away.normalized : -c.dir, 3f);
            CareerFx.Gen("m2_blink", "m_portal", owner.Center, Vector2.zero, 1.4f, 1f, 24f, VfxLayer.Top, false, SkillSpecs.ColorB);
            GiveShield(c, owner, .15f, 3f);
        }
    }
}
