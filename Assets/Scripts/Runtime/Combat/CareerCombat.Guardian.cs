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
        public int MaxShieldsOut => Mathf.Clamp(2 + Mathf.Max(1, Prog.Rank("g_wall")), 3, 5);
        /// <summary>When each thrown shield is back (a time list, so a stopped coroutine can never leave one counted forever).</summary>
        readonly List<float> shieldBack = new List<float>();
        int ShieldsOut { get { shieldBack.RemoveAll(t => t <= Time.time); return shieldBack.Count; } }

        /// <summary>False while every shield of 회귀의 방패 is still out (checked before mana is spent).</summary>
        public bool CanCast(CareerSkill s) => s.effect != "shieldthrow" || ShieldsOut < MaxShieldsOut;

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
            // Hex tiles close a barrier around the body, the shove throws hex shards out, then the barrier holds.
            Vector2 body = owner.Center - owner.Position;
            CareerFx.Clip("g_dome", owner.Center, Vector2.zero, 1.25f, 30f, VfxLayer.Top, false, new Color(1f, 1f, 1f, .9f)).Follow(owner.transform, body + Vector2.up * .1f);
            CareerFx.Clip("g_domeloop", owner.Center, Vector2.zero, 1.25f, 12f, VfxLayer.Top, false, new Color(1f, 1f, 1f, .55f), false, c.s.duration, true)
                .Follow(owner.transform, body + Vector2.up * .1f).FadeOut(.4f);
            CareerFx.Clip("g_hexburst", owner.Center, Vector2.zero, c.n.radius / 1.3f, 26f, VfxLayer.Top, false);
            Sound("c_shield");
            Feel(1, Vector2.down);
            foreach (var e in Enemies(owner.Center, c.n.radius)) Strike(c, e, c.n.damage, owner.Center, 10f, 1, "c_shield");
            // [GUARDIAN] A clear one-second invulnerability, then the damage reduction (the old slow is gone).
            if (c.authority) { AddInvulnerable(1f); AddGuard(Mathf.RoundToInt(30 * c.Scale), c.s.duration); }
            if (owner.IsLocal) SkillVisuals.Flash(owner.Center, new Color(.6f, 1f, .95f, .8f), 1.8f, .25f);
        }

        /// <summary>회귀의 방패: the thrown shield hits on the way out and back, and shields every ally it touches.</summary>
        IEnumerator ShieldThrow(Run c)
        {
            Pose(.2f, 1);
            Sound("swing");
            Vector2 start = owner.Center, far = start + c.dir * c.n.range;
            var fx = CareerFx.Clip("g_spin", start, Vector2.zero, 1.1f, 28f, VfxLayer.Top, false, null, false, 10f, true);
            var shielded = new HashSet<PlayerController> { owner };
            GiveShield(c, owner, WallShield, c.s.duration);
            // A slow flight (1.1 s there and back) keeps several shields out together; ranks throw faster (SkillCaster).
            const float leg = .55f;
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
                    if (!Live(c)) { fx?.Stop(); yield break; }
                    float u = Mathf.Clamp01((t + Time.deltaTime) / leg);
                    Vector2 at = pass == 0 ? Vector2.Lerp(start, far, 1 - (1 - u) * (1 - u)) : Vector2.Lerp(far, owner.Center, u * u);
                    fx?.Place(at);
                    CareerFx.DashTrail(at, at + Vector2.down * .4f, (at - previous).sqrMagnitude > .0001f ? (at - previous).normalized : c.dir, CareerFx.Teal);
                    foreach (var e in Corridor(previous, at, .75f))
                    {
                        if (hit.Contains(e) || pass == 1 && outbound.TryGetValue(e, out float when) && Time.time - when < CutGap) continue;
                        hit.Add(e);
                        if (pass == 0) outbound[e] = Time.time;
                        if (Strike(c, e, c.n.damage, previous, 6f, 1, "c_shield")) Stun(c, e, .3f);
                    }
                    foreach (var p in Allies(at, .9f)) if (shielded.Add(p)) GiveShield(c, p, WallShield, c.s.duration);
                    previous = at;
                    yield return null;
                }
            }
            fx?.Stop();
            if (Live(c)) { CareerFx.Clip("g_clang", owner.Center + c.dir * .3f, Vector2.zero, 1.2f, 30f); Sound("c_shield", .5f); }
            }
        }

        /// <summary>수호의 맹세: a ward that follows the guardian; allies inside take less damage and get a shield once.</summary>
        IEnumerator Oath(Run c)
        {
            OathRadius = c.n.radius;
            oathEnd = Time.time + c.s.duration;
            // The ward ring follows the guardian on the ground; its hexes light in a chase.
            CareerFx.Clip("g_ward", owner.Position, Vector2.zero, c.n.radius / 1.69f, 14f, VfxLayer.Ground, false, new Color(1f, 1f, 1f, .9f), false, c.s.duration, true)
                .Follow(owner.transform, Vector2.zero).Squash(1f, .7f).FadeOut(.5f);
            Sound("c_holy", .7f);
            var shielded = new HashSet<PlayerController>();
            float end = Time.time + c.s.duration;
            while (Time.time < end && Live(c))
            {
                foreach (var p in Allies(owner.Center, c.n.radius))
                {
                    if (c.authority) { For(p).AddGuard(30, .35f); For(p).Cleanse(); }
                    if (shielded.Add(p)) GiveShield(c, p, c.s.power, end - Time.time);
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
            var wave = new Color(1f, .78f, .45f, .95f);
            CareerFx.Clip("g_roar", at, c.dir, c.n.radius / 3f, 22f, VfxLayer.Top, true, wave);
            CareerFx.Clip("g_roar", at, -c.dir, c.n.radius / 3.4f, 22f, VfxLayer.Top, true, wave);
            Fx.Dust(owner.Position);
            Sound("c_heavy", .8f);
            Sound("c_shield", .5f);
            Feel(1, Vector2.down);
            var reached = new HashSet<EnemyController>();
            const float spread = .3f;
            for (float t = 0; t < spread; t += Time.deltaTime)
            {
                if (!Live(c)) yield break;
                float r = c.n.radius * Mathf.Clamp01((t + Time.deltaTime) / spread);
                foreach (var e in Enemies(at, r))
                    if (reached.Add(e)) { Strike(c, e, c.n.damage, at, 3f, 0, "c_shield"); Taunt(c, e, c.s.duration); }
                yield return null;
            }
        }

        /// <summary>방패 강타: a step in and a shield blow that stuns and throws monsters back.</summary>
        IEnumerator ShieldBash(Run c)
        {
            float distance = Dash(c.dir, .8f);
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
                CareerFx.Clip("impact", hit + Vector2.down * .35f, Vector2.zero, .6f, 28f, VfxLayer.Ground, false, CareerFx.Teal);
                SkillFx.Spawn("fx_ring", hit, new Color(.5f, 1f, .95f, .8f), .25f, SkillFx.TopOrder + 2).Scale(.2f, 1.1f).Fade(FxFade.Quick);
            }
            else CareerFx.Clip("g_bash", at, c.dir, 1.15f, 26f);
            Sound("c_shield");
            foreach (var e in Fan(at, c.dir, c.n.range, 110f))
                if (Strike(c, e, c.n.damage, at, 12f, 2, "c_shield")) { Stun(c, e, c.s.duration); Taunt(c, e, 3f); }
        }

        /// <summary>응보의 방진: hold the shield; the first blow taken (or the end of the stance) releases a counter blast.</summary>
        void CounterStance(Run c)
        {
            Pose(.2f, 0);
            StartCoroutine(OrbitShields(c.s.duration));
            Sound("c_shield", .6f);
            counterDamage = c.n.damage;
            counterEnd = Time.time + c.s.duration;
            if (c.authority) AddGuard(40, c.s.duration);
        }

        /// <summary>Three gold shields circle the guardian while the counter stance is up.</summary>
        IEnumerator OrbitShields(float seconds)
        {
            var gold = new Color(1f, .86f, .45f, .95f);
            var shields = new SkillFx[3];
            // [VFX] Small drawn shields (subtle: about a quarter of a tile), else the old crest kept small.
            for (int k = 0; k < 3; k++)
                shields[k] = CareerFx.ShieldArt
                    ? SkillFx.Spawn(CareerFx.ShieldSprite, owner.Center, new Color(1f, 1f, 1f, .85f), seconds + .1f, SkillFx.TopOrder + 2).Scale(.5f, .5f).Fade(FxFade.None)
                    : SkillFx.Spawn("fx_aegis", owner.Center, gold, seconds + .1f, SkillFx.TopOrder + 2).Scale(.35f, .35f).Fade(FxFade.None);
            float end = Time.time + seconds;
            while (Time.time < end && counterEnd > Time.time && owner != null)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (shields[k] == null) continue;
                    float a = Time.time * 4f + k * Mathf.PI * 2f / 3f;
                    shields[k].transform.position = owner.Center + new Vector2(Mathf.Cos(a) * .7f, Mathf.Sin(a) * .4f);
                }
                yield return null;
            }
            foreach (var sh in shields) if (sh != null) sh.Kill();
        }

        IEnumerator CounterBlast(float power)
        {
            var s = CareerCatalog.Get("g_counter");
            var c = new Run { s = s, dir = Aim(), map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };
            Vector2 at = owner.Center;
            Pose(.25f, 2);
            // The circling shields burst outward in eight directions.
            var gold = new Color(1f, .86f, .45f, 1f);
            for (int k = 0; k < 8; k++)
            {
                var d = new Vector2(Mathf.Cos(k * Mathf.PI / 4f), Mathf.Sin(k * Mathf.PI / 4f));
                SkillFx.Spawn("fx_aegis", at + d * .4f, gold, .35f, SkillFx.TopOrder + 3).Move(d * s.radius / .35f, 2f).Scale(.6f, .45f).Spin(720f).Fade(FxFade.Late);
            }
            CareerFx.Clip("g_hexburst", at, Vector2.zero, s.radius / 1.3f, 26f, VfxLayer.Top, false, gold);
            CareerFx.Clip("g_clang", at, Vector2.zero, 2f, 30f, VfxLayer.Top, false, gold);
            Sound("c_heavy");
            Sound("c_shield", .6f);
            Feel(2, Vector2.down);
            foreach (var e in Enemies(at, s.radius)) Strike(c, e, Mathf.RoundToInt(counterDamage * power), at, 12f, 2, "c_shield");
            CareerTrials.Record(owner, "counter", 1);
            yield break;
        }

        /// <summary>천쇄방패: a leaping shield slam that stuns and taunts everything near, and shields the party.</summary>
        IEnumerator AegisSlam(Run c)
        {
            Pose(.35f, 2);
            Vector2 at = owner.Center, feet = owner.Position;
            // A giant shield falls from the sky onto the guardian's spot.
            const float fall = .22f;
            SkillFx.Spawn(SkillFx.Pick("fxi_aegis", "fx_aegis"), feet + Vector2.up * 4.5f, Color.white, fall, SkillFx.TopOrder + 4).Move(Vector2.down * (4f / fall)).Scale(2.6f, 2.6f).Fade(FxFade.None);
            GlowLineFx.Spawn(feet + Vector2.up * 5f, feet, new Color(.4f, .95f, .9f, .55f), .8f, fall + .1f, SkillFx.TopOrder + 3);
            yield return new WaitForSeconds(fall);
            if (!Live(c)) yield break;
            SkillFx.Spawn(SkillFx.Pick("fxi_aegis", "fx_aegis"), feet + Vector2.up * .9f, Color.white, .8f, SkillFx.At(feet.y, 6)).Scale(2.6f, 2.4f).Fade(FxFade.Late);
            CareerFx.Clip("impact", feet, Vector2.zero, c.n.radius / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Teal);
            if (CareerFx.ShieldArt)
            {
                // [VFX] Eight small shields burst out from the slam in a ring (same art family as the falling aegis).
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .6f);
                    SkillFx.Spawn(CareerFx.ShieldSprite, at, Color.white, .45f, SkillFx.TopOrder + 3).Move(d * c.n.radius * 2.2f, 5f).Scale(.9f, .6f).Fade(FxFade.Late);
                }
                SkillFx.Spawn("fx_ring", feet, new Color(.5f, 1f, .95f, .85f), .4f, SkillFx.GroundOrder + 8).Scale(new Vector2(.4f, .28f), new Vector2(c.n.radius * 1.2f, c.n.radius * .84f)).Fade(FxFade.Quick);
            }
            else CareerFx.Clip("g_hexburst", at, Vector2.zero, c.n.radius / 1.3f, 24f, VfxLayer.Top, false);
            SkillFx.Crack(feet, c.n.radius, 1.8f);
            Sound("c_heavy");
            Sound("c_shield");
            foreach (var e in Enemies(at, c.n.radius))
                if (Strike(c, e, c.n.damage, at, 12f, 2, "c_shield"))
                {
                    Stun(c, e, 1.5f);
                    Taunt(c, e, 4f);
                    // Chains of light burst from the ground and bind the monster.
                    for (int k = -1; k <= 1; k += 2) CareerFx.Clip("g_chain", e.Position + new Vector2(k * .35f, 0f), Vector2.zero, 1f, 22f, VfxLayer.AtFeet, false).FlipY(false);
                }
            foreach (var p in Allies(at, c.n.radius + 1f)) GiveShield(c, p, .25f, c.s.duration);
        }
    }
}
