using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>수호자: taunts, stuns and shields. Low damage, heavy impacts.</summary>
    public sealed partial class CareerCombat
    {
        const float WallShield = .18f;

        IEnumerator Guardian(Run c)
        {
            switch (c.s.effect)
            {
                case "guard": GuardStance(c); break;
                case "shieldthrow": yield return ShieldThrow(c); break;
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
            CareerFx.Slam(owner.Position, c.n.radius, CareerFx.Teal, false);
            CareerFx.Aegis(owner.Center + c.dir * .35f, 1.3f, CareerFx.Teal, .5f);
            Sound("c_shield");
            Feel(1, Vector2.down);
            foreach (var e in Enemies(owner.Center, c.n.radius)) Strike(c, e, c.n.damage, owner.Center, 10f, 1, "c_shield");
            if (c.authority) { AddGuard(Mathf.RoundToInt(30 * c.Scale), c.s.duration); guardSlowEnd = Time.time + c.s.duration; }
        }

        /// <summary>회귀의 방패: the thrown shield hits on the way out and back, and shields every ally it touches.</summary>
        IEnumerator ShieldThrow(Run c)
        {
            Pose(.2f, 1);
            Sound("swing");
            Vector2 start = owner.Center, far = start + c.dir * c.n.range;
            var fx = CareerFx.Shield(start, CareerFx.Teal);
            var shielded = new HashSet<PlayerController> { owner };
            GiveShield(c, owner, WallShield, c.s.duration);
            const float leg = .28f;
            // A monster hit on the way out can be hit again on the way back once its hit invulnerability is over.
            var outbound = new Dictionary<EnemyController, float>();
            for (int pass = 0; pass < 2; pass++)
            {
                var hit = new HashSet<EnemyController>();
                Vector2 previous = pass == 0 ? start : far;
                for (float t = 0; t < leg; t += Time.deltaTime)
                {
                    if (!Live(c)) { fx.Kill(); yield break; }
                    float u = Mathf.Clamp01((t + Time.deltaTime) / leg);
                    Vector2 at = pass == 0 ? Vector2.Lerp(start, far, 1 - (1 - u) * (1 - u)) : Vector2.Lerp(far, owner.Center, u * u);
                    fx.MoveTo(at);
                    CareerFx.OrbTrail(at, (at - previous).normalized, CareerFx.Teal);
                    foreach (var e in Corridor(previous, at, .75f))
                    {
                        if (hit.Contains(e) || pass == 1 && outbound.TryGetValue(e, out float when) && Time.time - when < CutGap) continue;
                        hit.Add(e);
                        if (pass == 0) outbound[e] = Time.time;
                        if (Strike(c, e, c.n.damage, previous, 6f, 1, "c_shield")) Stun(c, e, .5f);
                    }
                    foreach (var p in Allies(at, .9f)) if (shielded.Add(p)) GiveShield(c, p, WallShield, c.s.duration);
                    previous = at;
                    yield return null;
                }
            }
            fx.Kill();
            if (Live(c)) { SkillVisuals.Flash(owner.Center, CareerFx.Teal, 1.2f, .15f); Sound("c_shield", .5f); }
        }

        /// <summary>수호의 맹세: a ward that follows the guardian; allies inside take less damage and get a shield once.</summary>
        IEnumerator Oath(Run c)
        {
            OathRadius = c.n.radius;
            oathEnd = Time.time + c.s.duration;
            CareerFx.Sigil(owner.Position, c.n.radius, CareerFx.Teal, .7f);
            CareerFx.Aegis(owner.Center + Vector2.up * .4f, 1.6f, CareerFx.Teal, .6f);
            Sound("c_holy", .7f);
            var shielded = new HashSet<PlayerController>();
            float end = Time.time + c.s.duration;
            while (Time.time < end && Live(c))
            {
                foreach (var p in Allies(owner.Center, c.n.radius))
                {
                    if (c.authority) For(p).AddGuard(20, .35f);
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
            CareerFx.Slam(owner.Position, 1.4f, CareerFx.Teal, false);
            for (int i = 0; i < 3; i++) CareerFx.Shock(at, c.n.radius * (.6f + i * .2f), i == 1 ? new Color(1f, .8f, .4f, .9f) : CareerFx.Teal, .45f, i * .08f);
            SkillVisuals.Flash(at, new Color(1f, .6f, .3f, .4f), c.n.radius * 1.2f, .3f);
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
            CareerFx.Aegis(at + c.dir * .7f, 1.4f, CareerFx.Teal, .35f);
            CareerFx.Shock(at + c.dir * 1.1f, 1.3f, CareerFx.Teal, .25f);
            Sound("c_shield");
            foreach (var e in Fan(at, c.dir, c.n.range, 110f))
                if (Strike(c, e, c.n.damage, at, 12f, 2, "c_shield")) Stun(c, e, c.s.duration);
        }

        /// <summary>응보의 방진: hold the shield; the first blow taken (or the end of the stance) releases a counter blast.</summary>
        void CounterStance(Run c)
        {
            Pose(.2f, 0);
            CareerFx.Aegis(owner.Center + c.dir * .3f, 1.5f, new Color(1f, .85f, .45f, .95f), .45f);
            CareerFx.Sigil(owner.Position, 1f, new Color(1f, .85f, .45f, .9f), .5f, -260f);
            Sound("c_shield", .6f);
            counterDamage = c.n.damage;
            counterEnd = Time.time + c.s.duration;
            if (c.authority) AddGuard(40, c.s.duration);
        }

        IEnumerator CounterBlast(float power)
        {
            var s = CareerCatalog.Get("g_counter");
            var c = new Run { s = s, dir = Aim(), map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };
            Vector2 at = owner.Center;
            Pose(.25f, 2);
            CareerFx.Slam(owner.Position, s.radius, new Color(1f, .82f, .4f, .95f), true);
            CareerFx.Aegis(at, 1.8f, new Color(1f, .85f, .45f, .95f), .4f);
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
            CareerFx.Aegis(at + Vector2.up * .8f, 3f, CareerFx.Teal, .7f);
            CareerFx.Slam(feet, c.n.radius, CareerFx.Teal, true);
            for (int i = 0; i < 3; i++) CareerFx.Shock(at, c.n.radius * (.5f + i * .25f), CareerFx.Teal, .5f, i * .07f);
            Sound("c_heavy");
            Sound("c_shield");
            foreach (var e in Enemies(at, c.n.radius))
                if (Strike(c, e, c.n.damage, at, 12f, 2, "c_shield")) { Stun(c, e, 1.5f); Taunt(c, e, 4f); }
            foreach (var p in Allies(at, c.n.radius + 1f)) GiveShield(c, p, .25f, c.s.duration);
            SkillVisuals.UltFinish(at, CareerFx.Teal, c.n.radius);
            yield break;
        }
    }
}
