using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>수호자: taunts, stuns and shields. Low damage, heavy impacts.</summary>
    public sealed partial class CareerCombat
    {
        const float WallShield = .18f;
        /// <summary>회귀의 방패 has no cooldown: three shields fly at once, one more per node rank above 1 (up to 5).</summary>
        /// [CH3 SPEC] A 거대 방패: one shield at a time. B 방패 산개: two more (5~7).
        public int MaxShieldsOut => Prog.SpecOf("g_wall") == 'a' ? 1 : Mathf.Clamp(2 + Mathf.Max(1, Prog.Rank("g_wall")), 3, 5) + (Prog.SpecOf("g_wall") == 'b' ? 2 : 0);
        /// <summary>When each thrown shield is back (a time list, so a stopped coroutine can never leave one counted forever).</summary>
        readonly List<float> shieldBack = new List<float>();
        int ShieldsOut { get { shieldBack.RemoveAll(t => t <= Time.time); return shieldBack.Count; } }

        /// <summary>False while every shield of 회귀의 방패 is still out (checked before mana is spent).</summary>
        public bool CanCast(CareerSkill s) => s.effect != "shieldthrow" || ShieldsOut < MaxShieldsOut;

        // [VFX 2026-10-11] One guardian look: steel teal body, warm gold edge. Rings are sized to the real radius
        // (fx_ring at Scale(r, r * .7) covers the radius r on the ground) and nothing lasts past ~2 s.
        static readonly Color GTeal = new Color(.42f, .95f, .88f, .9f), GGold = new Color(1f, .82f, .42f, .95f);

        /// <summary>A ground ring that grows from the feet to exactly <paramref name="r"/> and fades.</summary>
        static void GuardRing(Vector2 feet, float r, Color color, float life, float delay = 0f)
        {
            SkillFx.Spawn("fx_ring", feet, color, life, SkillFx.GroundOrder + 14)
                .Scale(new Vector2(r * .25f, r * .175f), new Vector2(r, r * .7f)).Fade(FxFade.Late).Delay(delay);
        }

        /// <summary>A ground ring held at exactly <paramref name="r"/> (shows the reach), fading at the end.</summary>
        static void GuardArea(Vector2 feet, float r, Color color, float life)
        {
            SkillFx.Spawn("fx_ring", feet, color, life, SkillFx.GroundOrder + 14).Scale(new Vector2(r, r * .7f), new Vector2(r, r * .7f)).Fade(FxFade.Late);
            SkillFx.Spawn("fx_glow", feet, new Color(color.r, color.g, color.b, .16f), life * .8f, SkillFx.GroundOrder + 13).Additive()
                .Scale(new Vector2(r * 1.7f, r * 1.15f), new Vector2(r * 1.9f, r * 1.3f)).Fade(FxFade.Late);
        }

        IEnumerator Guardian(Run c)
        {
            switch (c.s.effect)
            {
                case "guard": GuardStance(c); break;
                case "shieldthrow": StartCoroutine(ShieldThrow(c)); break; // the next throw does not wait for this one
                case "oath": StartCoroutine(Oath(c)); break;
                case "taunt": yield return TauntRoar(c); break;
                case "bash": yield return ShieldBash(c); break;
                case "counter": CounterStance(c); break;
                case "aegis": yield return AegisSlam(c); break;
            }
        }

        /// <summary>강철의 보루: plant the shield, shove the monsters around, then hold.</summary>
        void GuardStance(Run c)
        {
            Pose(.2f, 2);
            // [VFX] The shield slams down: a teal barrier dome closes over the body (about 2 s), the shove is a dust slam
            // and a ring to the exact push radius with a gold edge.
            Vector2 body = owner.Center - owner.Position, feet = owner.Position;
            if (VfxLibrary.Has("g2_barrier_dome"))
                // [VFX] The generated hexagon dome assembles over the body and holds (about 1.8 s).
                CareerFx.Gen("g2_barrier_dome", null, owner.Center, Vector2.zero, 2.6f, 0f, 16f, VfxLayer.Top, false, null, Mathf.Min(1.8f, c.s.duration))
                    ?.Follow(owner.transform, body + Vector2.up * .1f).FadeOut(.4f);
            else
            {
                CareerFx.Clip("g_dome", owner.Center, Vector2.zero, 1.35f, 30f, VfxLayer.Top, false, new Color(.85f, 1f, .97f, .95f)).Follow(owner.transform, body + Vector2.up * .1f);
                CareerFx.Clip("g_domeloop", owner.Center, Vector2.zero, 1.35f, 12f, VfxLayer.Top, false, new Color(.8f, 1f, .95f, .5f), false, Mathf.Min(1.8f, c.s.duration), true)
                    .Follow(owner.transform, body + Vector2.up * .1f).FadeOut(.4f);
            }
            CareerFx.Slam(feet, c.n.radius * .8f, GTeal, false);
            GuardRing(feet, c.n.radius, GTeal, .45f);
            GuardRing(feet, c.n.radius, GGold, .35f, .05f);
            Sound("c_shield");
            Sound("c_heavy", .6f);
            Feel(1, Vector2.down);
            foreach (var e in Enemies(owner.Center, c.n.radius)) Strike(c, e, c.n.damage, owner.Center, 10f, 1, "c_shield");
            // [GUARDIAN] A clear one-second invulnerability, then the damage reduction (the old slow is gone).
            // [CH3 SPEC] A 불굴의 보루: 2 s invulnerable and a 15 s shield wall. B 수호의 벽: a wall twice as wide, the 20%
            // guard shared with allies within 4 m, and half of every blow it softens answered (SpecOnHurt).
            if (c.authority) { AddInvulnerable(c.n.spec == 'a' ? 2f : 1f); AddGuard(Mathf.RoundToInt(30 * c.Scale), c.s.duration); }
            if (c.n.spec == 'b' && c.authority)
            {
                wallReflectEnd = Time.time + c.s.duration;
                foreach (var p in Allies(owner.Center, 4f)) if (p != owner) For(p).AddGuard(Mathf.RoundToInt(20 * c.Scale), c.s.duration);
            }
            // [RAID] The 거대 방패 stays planted in front toward the aim for 7 s; the ground behind it is cover from 섬멸.
            // Every PC plants it (the host's copy decides the damage, a member's copy is the picture).
            Vector2 face = c.dir.sqrMagnitude > .0001f ? c.dir.normalized : Vector2.down;
            if (c.n.spec == 'a') SafeCover.AddWall(feet + face * .9f, face, SafeCover.WallHalfWidth, 15f);
            else if (c.n.spec == 'b') SafeCover.AddWall(feet + face * .9f, face, SafeCover.WallHalfWidth * 2f);
            else SafeCover.AddWall(feet + face * .9f, face);
            if (c.n.spec != '\0') GuardRing(feet, c.n.radius, Tint(c, GTeal), .5f, .1f);
            if (owner.IsLocal) SkillVisuals.Flash(owner.Center, new Color(.6f, 1f, .95f, .8f), 1.8f, .25f);
        }

        /// <summary>회귀의 방패: the thrown shield hits on the way out and back, and shields every ally it touches.</summary>
        /// <summary>[VFX 2026-10-29] World width of the thrown shield picture (as big as 풍마 수리검's).</summary>
        const float ShieldThrowWidth = 1.9f;

        IEnumerator ShieldThrow(Run c)
        {
            Pose(.2f, 1);
            Sound("swing");
            const float leg = .55f;
            Vector2 start = owner.Center, far = start + c.dir * c.n.range;
            // [VFX] A clearly drawn shield spinning in flight (the clip when the art is missing), a teal trail, a gold clang per hit.
            // [CH3 SPEC] A 거대 방패: one big shield at 3.5x that plants 2 s of cover where it turns. B 방패 산개: 0.75x and it
            // bounces once onto a monster near its far point before coming back.
            // [VFX 2026-10-29] Drawn like 풍마 수리검 (n_fuma, radius x 2.6 wide): one big spinning disc of the shield art, about
            // twice the hit corridor (.75 each side) wide; 거대 방패 A x1.4. Was a 1.15x (A 1.7x) sprite. Hits use the corridor only.
            float size = ShieldThrowWidth * (c.n.spec == 'a' ? 1.4f : 1f);
            var shieldPic = Game.Art.Get(CareerFx.ShieldSprite);
            float big = size / Mathf.Max(.05f, shieldPic != null ? shieldPic.bounds.size.x : 1f);
            int wallDamage = c.n.spec == 'a' ? c.n.damage * 7 / 2 : c.n.spec == 'b' ? Mathf.Max(1, c.n.damage * 3 / 4) : c.n.damage;
            SkillFx shieldArt = CareerFx.ShieldArt ? SkillFx.Spawn(CareerFx.ShieldSprite, start, c.n.spec != '\0' ? Tint(c, Color.white) : Color.white, leg * 2f + .2f, SkillFx.TopOrder + 3).Scale(big, big).Spin(-1080f).Fade(FxFade.None) : null;
            // [VFX 2026-10-29] Like the ninja's stars: a bright additive copy spinning behind the shield and a glowing trail.
            SkillFx shieldHalo = shieldArt != null ? SkillFx.Spawn(CareerFx.ShieldSprite, start, new Color(1f, 1f, 1f, .5f), leg * 2f + .2f, SkillFx.TopOrder + 2).Additive().Scale(big * 1.3f, big * 1.3f).Spin(-1080f).Fade(FxFade.None) : null;
            float trailIn = 0f;
            var fx = shieldArt == null ? CareerFx.Clip("g_spin", start, Vector2.zero, size, 28f, VfxLayer.Top, false, null, false, 10f, true) : null;
            var shielded = new HashSet<PlayerController> { owner };
            GiveShield(c, owner, WallShield, c.s.duration);
            // A slow flight (1.1 s there and back) keeps several shields out together; ranks throw faster (SkillCaster).
            shieldBack.Add(Time.time + leg * 2f + .05f);
            {
            // A monster hit on the way out can be hit again on the way back once its hit invulnerability is over.
            var outbound = new Dictionary<EnemyController, float>();
            for (int pass = 0; pass < 2; pass++)
            {
                var hit = new HashSet<EnemyController>();
                Vector2 previous = pass == 0 ? start : far;
                for (float t = 0; t < leg; t += Time.deltaTime)
                {
                    if (!Live(c)) { fx?.Stop(); if (shieldArt != null) shieldArt.Kill(); if (shieldHalo != null) shieldHalo.Kill(); yield break; }
                    float u = Mathf.Clamp01((t + Time.deltaTime) / leg);
                    Vector2 at = pass == 0 ? Vector2.Lerp(start, far, 1 - (1 - u) * (1 - u)) : Vector2.Lerp(far, owner.Center, u * u);
                    fx?.Place(at);
                    if (shieldArt != null) shieldArt.transform.position = at;
                    if (shieldHalo != null) shieldHalo.transform.position = at;
                    trailIn -= Time.deltaTime;
                    if (trailIn <= 0f)
                    {
                        trailIn = .025f;
                        SkillFx.Spawn("fx_glow", at, new Color(.7f, 1f, .95f, .6f), .2f, SkillFx.TopOrder + 1).Additive().Scale(size * .9f, size * .25f);
                    }
                    CareerFx.DashTrail(at, at + Vector2.down * .4f, (at - previous).sqrMagnitude > .0001f ? (at - previous).normalized : c.dir, CareerFx.Teal);
                    foreach (var e in Corridor(previous, at, .75f))
                    {
                        if (hit.Contains(e) || pass == 1 && outbound.TryGetValue(e, out float when) && Time.time - when < CutGap) continue;
                        hit.Add(e);
                        if (pass == 0) outbound[e] = Time.time;
                        if (Strike(c, e, wallDamage, previous, 6f, 1, "c_shield"))
                        {
                            Stun(c, e, .3f);
                            CareerFx.Clip("g_clang", e.Center, Vector2.zero, 1.25f, 30f, VfxLayer.Top, false, GGold); // [VFX 2026-10-29] was .8
                            SkillVisuals.Flash(e.Center, new Color(1f, .92f, .6f, .65f), 1.3f, .12f);
                        }
                    }
                    foreach (var p in Allies(at, .9f)) if (shielded.Add(p)) GiveShield(c, p, WallShield, c.s.duration);
                    previous = at;
                    yield return null;
                }
                if (pass == 0 && c.n.spec == 'a') SafeCover.AddWall(far, c.dir, SafeCover.WallHalfWidth, 2f);
                if (pass == 0 && c.n.spec == 'b')
                {
                    EnemyController bounce = null;
                    foreach (var e in Enemies(far, 3f)) if (!hit.Contains(e)) { bounce = e; break; }
                    if (bounce != null)
                    {
                        Vector2 from = far;
                        for (float t = 0; t < .22f; t += Time.deltaTime)
                        {
                            if (!Live(c)) { fx?.Stop(); if (shieldArt != null) shieldArt.Kill(); if (shieldHalo != null) shieldHalo.Kill(); yield break; }
                            Vector2 at = Vector2.Lerp(from, bounce.Center, Mathf.Clamp01((t + Time.deltaTime) / .22f));
                            fx?.Place(at);
                            if (shieldArt != null) shieldArt.transform.position = at;
                            if (shieldHalo != null) shieldHalo.transform.position = at;
                            yield return null;
                        }
                        if (Strike(c, bounce, wallDamage, from, 6f, 1, "c_shield")) CareerFx.Clip("g_clang", bounce.Center, Vector2.zero, 1.25f, 30f, VfxLayer.Top, false, SkillSpecs.ColorB);
                        far = bounce.Center;
                    }
                }
            }
            fx?.Stop();
            if (shieldArt != null) shieldArt.Kill();
            if (shieldHalo != null) shieldHalo.Kill();
            if (Live(c)) { CareerFx.Clip("g_clang", owner.Center + c.dir * .3f, Vector2.zero, 1.5f, 30f); Sound("c_shield", .5f); } // [VFX 2026-10-29] was 1.2
            }
        }

        /// <summary>수호의 맹세: a ward that follows the guardian; allies inside take less damage and get a shield once.</summary>
        IEnumerator Oath(Run c)
        {
            OathRadius = c.n.radius;
            oathEnd = Time.time + c.s.duration;
            // [VFX] The ward rises once to its exact radius (gold edge, teal hexes) and follows for 2 s; the buff row keeps
            // the minute (a ring on the ground for the whole minute was clutter).
            CareerFx.Clip("g_ward", owner.Position, Vector2.zero, c.n.radius / 1.69f, 14f, VfxLayer.Ground, false, c.n.spec != '\0' ? Tint(c, Color.white) : new Color(1f, 1f, 1f, .9f), false, Mathf.Min(2f, c.s.duration), true)
                .Follow(owner.transform, Vector2.zero).Squash(1f, .7f).FadeOut(.5f);
            GuardRing(owner.Position, c.n.radius, GGold, .6f);
            CareerFx.Pillar(owner.Position, .9f, GTeal, .45f);
            Sound("c_holy", .7f);
            var shielded = new HashSet<PlayerController>();
            float end = Time.time + c.s.duration;
            while (Time.time < end && Live(c))
            {
                // [CH3 SPEC] A 맹세의 심장: 2 m, -45%, a 25% shield. B 맹세의 들판: 6.5 m, -15%, a 10% shield and 2% MP a second.
                int ward = c.n.spec == 'a' ? 45 : c.n.spec == 'b' ? 15 : 30;
                float oathShield = c.n.spec == 'a' ? .25f : c.n.spec == 'b' ? .10f : c.s.power;
                foreach (var p in Allies(owner.Center, c.n.radius))
                {
                    if (c.authority) { For(p).AddGuard(ward, .35f); For(p).Cleanse(); }
                    if (c.authority && c.n.spec == 'b') p.Data.Mana = Mathf.Min(p.Data.Stats.MaxMp, p.Data.Mana + p.Data.Stats.MaxMp * .005f);
                    if (shielded.Add(p)) GiveShield(c, p, oathShield, end - Time.time);
                }
                yield return new WaitForSeconds(.25f);
            }
        }

        /// <summary>대지의 호령: a stomp whose shock wave taunts every monster it reaches.</summary>
        IEnumerator TauntRoar(Run c)
        {
            Pose(.25f, 2);
            Vector2 at = owner.Center;
            // The roar rolls out as sound-wave arcs to the front and back; dust kicks up at the stomp.
            // [VFX] A stomp, then a gold shock ring that runs out to the exact taunt radius in step with the taunt itself;
            // every monster it reaches gets a "!" (Taunt). The roar arcs stay close to the body.
            CareerFx.Clip("g_roar", at, c.dir, 1.6f, 22f, VfxLayer.Top, true, GGold);
            CareerFx.Clip("g_roar", at, -c.dir, 1.4f, 22f, VfxLayer.Top, true, GGold);
            CareerFx.Slam(owner.Position, 1.6f, GGold, false);
            GuardRing(owner.Position, c.n.radius, GGold, .5f);
            GuardRing(owner.Position, c.n.radius, GTeal, .6f, .08f);
            Sound("c_heavy", .8f);
            Sound("c_shield", .5f);
            Feel(1, Vector2.down);
            // [CH3 SPEC] A 표적 호령: only the strongest monster, taunted 8 s (bosses 2.4 s) and taking 8% more damage.
            if (c.n.spec == 'a')
            {
                EnemyController strongest = null;
                foreach (var e in Enemies(at, c.n.radius)) if (strongest == null || e.Health.Max > strongest.Health.Max) strongest = e;
                if (strongest != null)
                {
                    Strike(c, strongest, c.n.damage, at, 3f, 1, "c_shield");
                    if (c.authority && !strongest.IsDead)
                    {
                        ThreatTable.For(strongest).Force(owner, strongest.IsBoss ? 2.4f : (float.IsNaN(c.n.duration) ? 8f : c.n.duration));
                        MarkTaken(strongest, 8, 8f);
                    }
                }
                yield break;
            }
            var reached = new HashSet<EnemyController>();
            const float spread = .3f;
            for (float t = 0; t < spread; t += Time.deltaTime)
            {
                if (!Live(c)) yield break;
                float r = c.n.radius * Mathf.Clamp01((t + Time.deltaTime) / spread);
                foreach (var e in Enemies(at, r))
                    if (reached.Add(e)) { Strike(c, e, c.n.damage, at, 3f, 0, "c_shield"); Taunt(c, e, c.n.spec == 'b' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration); }
                yield return null;
            }
            if (c.n.spec == 'b') StartCoroutine(TauntCrash(c, reached));
        }

        /// <summary>[CH3 SPEC] 광역 호령: taunted monsters crowding onto the guardian bump into each other (30%).</summary>
        IEnumerator TauntCrash(Run c, HashSet<EnemyController> taunted)
        {
            int bump = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * .3f / .4f)); // 0.3 where the taunt blow itself is 0.4
            for (float t = 0f; t < 3f; t += .5f)
            {
                yield return new WaitForSeconds(.5f);
                if (!Live(c) || !c.authority) yield break;
                var list = new List<EnemyController>();
                foreach (var e in taunted) if (e != null && !e.IsDead) list.Add(e);
                var bumped = new HashSet<EnemyController>();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        if (Vector2.Distance(list[i].Center, list[j].Center) < .9f)
                        {
                            if (bumped.Add(list[i])) SecondaryHit(list[i], bump);
                            if (bumped.Add(list[j])) SecondaryHit(list[j], bump);
                        }
            }
        }

        /// <summary>방패 강타: a step in and a shield blow that stuns and throws monsters back.</summary>
        IEnumerator ShieldBash(Run c)
        {
            // [CH3 SPEC] B 방패 돌진: a 2 m charge behind the raised shield (80% less damage), 1.1x.
            float distance = Dash(c.dir, c.n.spec == 'b' ? 2f : .8f);
            if (c.n.spec == 'b' && c.authority) AddGuard(80, DashSeconds(distance) + .3f);
            CareerFx.Ghost(owner, new Color(GTeal.r, GTeal.g, GTeal.b, .7f), .25f);
            yield return new WaitForSeconds(DashSeconds(distance));
            if (!Live(c)) yield break;
            Pose(.22f, 2);
            Vector2 at = owner.Center;
            if (SkillFx.HasImage("fxi_aegis"))
            {
                // [VFX] The drawn shield punches forward and stops at the hit line; a clang and a shock ring mark the blow.
                Vector2 hit = at + c.dir * Mathf.Min(1.3f, c.n.range * .6f);
                SkillFx.Spawn(SkillFx.Pick("fxi_aegis", "fx_aegis"), at + c.dir * .3f, Color.white, .3f, SkillFx.TopOrder + 4)
                    .Move(c.dir * 5f, 18f).Scale(.42f, .58f).Fade(FxFade.Late);
                CareerFx.Clip("g_clang", hit, Vector2.zero, 1.25f, 30f, VfxLayer.Top, false);
                CareerFx.Clip("impact", hit + Vector2.down * .35f, Vector2.zero, .85f, 28f, VfxLayer.Ground, false, CareerFx.Teal); // [VFX 2026-10-29] was .6
                SkillFx.Spawn("fx_ring", hit, new Color(.5f, 1f, .95f, .8f), .25f, SkillFx.TopOrder + 2).Scale(.2f, 1.1f).Fade(FxFade.Quick);
            }
            else CareerFx.Clip("g_bash", at, c.dir, 1.4f, 26f); // [VFX 2026-10-29] was 1.15
            // [VFX] The blow's 110 degree cone: a wide gold arc to the full reach and dust thrown along its edges.
            CareerFx.Slash(at, c.dir, c.n.range, GGold, 0f, false, .2f);
            for (int k = 0; k < 7; k++)
            {
                Vector2 d = CareerFx.Tilt(c.dir, -55f + k * (110f / 6f));
                SkillFx.Spawn("fx_dust", owner.Position + d * .3f, new Color(.95f, .92f, .85f, .75f), .45f, SkillFx.GroundOrder + 10).Move(d * c.n.range * 2.4f, 5f).Scale(.8f, 1.6f);
            }
            Sound("c_shield");
            Sound("c_heavy", .5f);
            // [CH3 SPEC] A 파쇄 강타: the first monster alone, 5.5 (of 3.0), stunned 2 s (bosses cut by their own rule).
            int bashDamage = c.n.spec == 'a' ? Mathf.RoundToInt(c.n.damage * 5.5f / 3f) : c.n.spec == 'b' ? Mathf.RoundToInt(c.n.damage * 1.1f) : c.n.damage;
            if (c.n.spec != '\0') CareerFx.Slash(at, c.dir, c.n.range, Tint(c, GGold), 0f, false, .2f);
            foreach (var e in Fan(at, c.dir, c.n.range, 110f))
            {
                if (Strike(c, e, bashDamage, at, 12f, 2, "c_shield")) { Stun(c, e, c.n.spec == 'a' ? 2f : c.s.duration); Taunt(c, e, 3f); }
                if (c.n.spec == 'a') break;
            }
        }

        /// <summary>응보의 방진: hold the shield; the first blow taken (or the end of the stance) releases a counter blast.</summary>
        void CounterStance(Run c)
        {
            Pose(.2f, 0);
            // [VFX] A gold stance ring of the blast's exact radius and a gold barrier pulse (short; the buff row tracks the minute).
            GuardArea(owner.Position, c.n.radius, GGold, 1.2f);
            CareerFx.Clip("g_dome", owner.Center, Vector2.zero, 1.25f, 30f, VfxLayer.Top, false, new Color(1f, .88f, .55f, .9f)).Follow(owner.transform, owner.Center - owner.Position + Vector2.up * .1f);
            Sound("c_shield", .6f);
            counterDamage = c.n.damage;
            counterEnd = Time.time + c.s.duration;
            // [CH3 SPEC] A 일격 응보: one block, one 400% answer to the front. B 연쇄 응보: five blocks, 25% on 1.5x the radius.
            counterSpec = c.n.spec;
            counterCharges = c.n.spec == 'a' ? 1 : c.n.spec == 'b' ? 5 : CounterBlocks;
            if (c.authority) AddGuard(40, c.s.duration);
        }

        IEnumerator CounterBlast(float power)
        {
            var s = CareerCatalog.Get("g_counter");
            var c = new Run { s = s, dir = Aim(), map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };
            Vector2 at = owner.Center;
            Pose(.25f, 2);
            float blastRadius = counterSpec == 'b' ? s.radius * 1.5f : s.radius;
            if (counterSpec == 'a')
            {
                // One heavy blow on the monster in front.
                var front = Target(c.dir, s.radius, out _);
                CareerFx.Clip("g_clang", at + c.dir * .6f, Vector2.zero, 2f, 30f, VfxLayer.Top, false, SkillSpecs.ColorA);
                Sound("c_heavy");
                Feel(2, c.dir);
                if (front != null) Strike(c, front, Mathf.RoundToInt(counterDamage * power), at, 14f, 2, "c_shield");
                CareerTrials.Record(owner, "counter", 1);
                yield break;
            }
            // [VFX] The blocked blow bursts back in gold: a burst and a slam out to the blast's exact radius, a big clang.
            CareerFx.Burst(at, s.radius * .5f, GGold, 14);
            CareerFx.Slam(owner.Position, s.radius * .8f, GGold, false);
            GuardRing(owner.Position, s.radius, GGold, .45f);
            CareerFx.Clip("g_hexburst", at, Vector2.zero, s.radius / 1.3f, 26f, VfxLayer.Top, false, GGold);
            CareerFx.Clip("g_clang", at, Vector2.zero, 2f, 30f, VfxLayer.Top, false, GGold);
            Sound("c_heavy");
            Sound("c_shield", .6f);
            Feel(2, Vector2.down);
            foreach (var e in Enemies(at, blastRadius)) Strike(c, e, Mathf.RoundToInt(counterDamage * power), at, 12f, 2, "c_shield");
            CareerTrials.Record(owner, "counter", 1);
            yield break;
        }

        /// <summary>
        /// 천쇄방패 (v2, 2026-10-12, Docs/PLAN_SKILL_VFX.md): while the guardian leaps, a hex barrier sigil is drawn on the
        /// ground; on the slam giant shields of light fall from the sky one by one around its edge (monsters inside, bosses too, are
        /// rooted and taunted, allies shielded), the walls close in pulling the monsters together, snap shut and burst
        /// into golden glass for the hit, breaking the monsters' armour for 8 s (no armour, +20% damage taken).
        /// </summary>
        const float BreakTime = 8f;

        IEnumerator AegisSlam(Run c)
        {
            const float Draw = .6f, Arrive = .75f, Hold = 1.6f, Close = .15f; // the shields land one by one during Arrive
            Pose(Draw + .1f, 2);
            Vector2 at = owner.Center, feet = owner.Position;
            float r = c.n.radius;
            int order = SkillFx.GroundOrder + 8;
            // 1) The barrier is drawn round the guardian's feet while it leaps: three nested steel-blue hexagons
            //    (a fortress shape, not a round plate like the mage's circle or the bishop's sanctum).
            var steel = new Color(.5f, .85f, 1f);
            var hexes = new[]
            {
                RingFx.Spawn(feet, r, steel, order, "g6_hex"), // the engraved hexagonal barrier circle (generated, additive)
                RingFx.Spawn(feet, r * .66f, steel, order + 3),
                RingFx.Spawn(feet, r * .3f, GGold, order + 6),
            };
            hexes[0].Width = .12f;
            hexes[0].PictureScale = 1.06f; // the picture's hexagon spans 94% of its frame: its corners meet the drawn ones
            for (int i = 0; i < hexes.Length; i++) { hexes[i].Sides = 6; hexes[i].Corner = 90f + i * 30f; hexes[i].Reveal = 0f; }
            void KillHexes() { foreach (var h in hexes) h.Kill(); }
            Sound("c_shield");
            for (float t = 0f; t < Draw; t += Time.deltaTime)
            {
                if (!Live(c)) { KillHexes(); yield break; }
                float k = t / Draw;
                for (int i = 0; i < hexes.Length; i++) hexes[i].Reveal = Mathf.Clamp01((k - i * .2f) / .6f);
                yield return null;
            }
            foreach (var h in hexes) h.Reveal = 1f;
            // 2) The slam: the shields start falling round the edge, everything inside is held and taunted, allies are shielded.
            CareerFx.Slam(feet, r * .7f, GTeal, true);
            SkillFx.Crack(feet, r, 1.8f);
            Feel(2, Vector2.down);
            if (owner.IsLocal) Game.Camera?.Shake(.25f, .3f);
            Sound("c_heavy");
            var foes = Enemies(at, r);
            foreach (var e in foes)
            {
                // [ARMOR] The guardian roots (속박) instead of the fighter's stagger: held in place, bosses too, but
                // still swinging at whoever stands next to them; taunted onto the guardian.
                if (c.authority) e.Root(Arrive + Hold + Close + .15f);
                Taunt(c, e, 4f);
            }
            foreach (var p in Allies(at, r + 1f)) GiveShield(c, p, .25f, c.s.duration);
            // [CH3 SPEC] B 천쇄 광역: allies inside take 30% less damage for 8 s.
            if (c.n.spec == 'b' && c.authority) foreach (var p in Allies(at, r)) For(p).AddGuard(30, 8f);
            var wall = ShieldWallFx.Spawn(feet, r * .95f, 12);
            wall.Tint = GGold;
            // 3) Once the shields have landed they close in slowly, brightening; the monsters are drawn toward the middle.
            float nextPull = 0f;
            for (float t = 0f; t < Arrive + Hold; t += Time.deltaTime)
            {
                if (!Live(c)) { KillHexes(); wall.Kill(); yield break; }
                float k = Mathf.Clamp01((t - Arrive) / Hold), ease = k * k * (3f - 2f * k);
                wall.Radius = Mathf.Lerp(r * .95f, r * .35f, ease);
                wall.Brightness = 1f + ease * .9f;
                foreach (var h in hexes) h.Brightness = 1f + ease * .8f;
                // The inner hexagons turn against each other as the barrier tightens.
                hexes[1].Corner = 120f + 40f * t;
                hexes[2].Corner = 150f - 70f * t;
                if (c.authority && t >= nextPull)
                {
                    nextPull = t + .2f;
                    foreach (var e in foes) if (e != null && !e.IsDead) e.PullTowards(feet, r * .38f, .25f);
                }
                yield return null;
            }
            // 4) Snap shut.
            Sound("c_shield");
            for (float t = 0f; t < Close; t += Time.deltaTime)
            {
                if (!Live(c)) { KillHexes(); wall.Kill(); yield break; }
                wall.Radius = Mathf.Lerp(r * .35f, r * .08f, t / Close);
                wall.Brightness = 2.2f;
                yield return null;
            }
            // 5) The barrier bursts: golden glass, a shock ring to the radius, the hit.
            wall.Shatter(order + 20);
            KillHexes();
            CareerFx.Clip("impact", feet, Vector2.zero, r / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Teal);
            GuardRing(feet, r, GGold, .5f);
            SkillVisuals.Flash(at, new Color(1f, .9f, .55f, .8f), r * .9f, .18f);
            Feel(2, Vector2.down);
            if (owner.IsLocal) Game.Camera?.Shake(.35f, .35f);
            Sound("c_heavy");
            foreach (var e in Enemies(feet, r))
            {
                Strike(c, e, c.n.damage, feet, 12f, 2, "c_shield");
                // [ARMOR] 방어 붕괴: 8 s without armour and 20% more damage taken, for the whole party to cash in.
                if (c.authority) e.BreakArmor(c.n.spec != '\0' && !float.IsNaN(c.n.duration) ? c.n.duration : BreakTime); // shows the split shield over its head (ArmorBreakMark)
                // [CH3 SPEC] A 천쇄 일점: the break goes to about -40% (another 17% from me for 12 s). B: 3 s more held in place.
                if (c.n.spec == 'a') MarkTaken(e, 17, 12f);
                if (c.n.spec == 'b' && c.authority) e.Root(3f);
            }
        }
    }
}
