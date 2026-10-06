using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>비숍: healing, shields and blessings, with light attacks of holy light.</summary>
    public sealed partial class CareerCombat
    {
        IEnumerator Bishop(Run c)
        {
            switch (c.s.effect)
            {
                case "heal": yield return Feathers(c, false); break;
                case "bloom": StartCoroutine(Bloom(c)); break;
                case "cleanse": Bell(c); break;
                case "light": yield return LightSpear(c); break;
                case "wings": yield return Feathers(c, true); break;
                case "bless": yield return BlessChain(c); break;
                case "dawn": yield return Dawn(c); break;
            }
        }

        /// <summary>치유의 깃 / 천사의 품: a feather flies to every ally around; it heals (or shields) on arrival.</summary>
        IEnumerator Feathers(Run c, bool shield)
        {
            Sound(shield ? "c_holy" : "heal", .7f);
            float longest = 0f;
            foreach (var p in Allies(owner.Center, c.n.radius))
            {
                float flight = Mathf.Clamp(Vector2.Distance(owner.Center, p.Center) / 9f, .15f, .4f);
                longest = Mathf.Max(longest, flight);
                StartCoroutine(Feather(c, p, flight, shield));
            }
            yield return new WaitForSeconds(longest);
        }

        IEnumerator Feather(Run c, PlayerController target, float flight, bool shield)
        {
            Vector2 start = owner.Center + Vector2.up * .4f;
            var fx = CareerFx.Clip("b_feather", start, Vector2.zero, 1f, 14f, VfxLayer.Top, false, shield ? Color.white : new Color(.85f, 1f, .85f, 1f), false, 10f, true);
            Vector2 at = start;
            for (float t = 0; t < flight; t += Time.deltaTime)
            {
                if (!Live(c) || target == null || target.IsDead) { fx?.Stop(); yield break; }
                float u = Mathf.Clamp01((t + Time.deltaTime) / flight);
                Vector2 next = Vector2.Lerp(start, target.Center, u) + Vector2.up * Mathf.Sin(u * Mathf.PI) * .8f;
                CareerFx.OrbTrail(next, (next - at).normalized, CareerFx.Holy);
                fx?.Place(next);
                at = next;
                yield return null;
            }
            fx?.Stop();
            if (shield)
            {
                // Translucent wings open behind the ally, then the shield settles on them.
                VfxPlayer.Play("b_wings", target.Center + Vector2.up * .15f, Vector2.zero, new Color(1f, 1f, 1f, .75f), .75f, 18f, false, .7f, false, VfxLayer.AtFeet, false, -60)?.FadeOut(.25f);
                GiveShield(c, target, c.s.power, c.s.duration);
            }
            else Heal(c, target, c.n.damage);
        }

        /// <summary>생명의 파문: a circle that sends out a healing ripple every second.</summary>
        IEnumerator Bloom(Run c)
        {
            Vector2 at = owner.Position;
            CareerFx.Clip("b_lotus", at, Vector2.zero, c.n.radius / 1.6f, 14f, VfxLayer.Ground, false, new Color(1f, 1f, 1f, .9f), false, c.s.duration + .3f).Squash(1f, .7f).FadeOut(.4f);
            Sound("heal", .6f);
            float interval = c.s.duration / c.s.hits;
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                SkillFx.Spawn("fx_ring", at, new Color(.7f, 1f, .7f, .8f), .55f, SkillFx.GroundOrder + 14).Scale(new Vector2(.3f, .21f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
                foreach (var p in Allies(at, c.n.radius))
                {
                    Heal(c, p, c.n.damage);
                    if (c.authority) { var st = For(p); st.hotSource = owner; st.hotEnd = Time.time + interval + .1f; }
                }
                yield return new WaitForSeconds(interval);
            }
        }

        /// <summary>정화의 종: a ring of bell light that cleanses and heals allies and pushes monsters away.</summary>
        void Bell(Run c)
        {
            Vector2 at = owner.Center;
            // A bell of light swings and rings above the bishop; the ring of sound spreads over the ground.
            CareerFx.Clip("b_bell", at + Vector2.up * 1.4f, Vector2.zero, 1f, 20f, VfxLayer.Top, false);
            for (int k = 0; k < 2; k++)
                SkillFx.Spawn("fx_ring", owner.Position, new Color(1f, .92f, .6f, .85f), .5f, SkillFx.GroundOrder + 14).Scale(new Vector2(.3f, .21f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late).Delay(k * .12f);
            Sound("c_holy");
            foreach (var p in Allies(at, c.n.radius))
            {
                if (c.authority && For(p).Cleanse())
                {
                    CareerTrials.Record(owner, "cleanse", 1);
                    // The curse leaves the body as dark smoke.
                    for (int k = 0; k < 6; k++)
                        SkillFx.Spawn("fx_dust", p.Center + Random.insideUnitCircle * .3f, new Color(.18f, .12f, .25f, .85f), .6f, SkillFx.TopOrder + 1)
                            .Move(new Vector2(Random.Range(-.6f, .6f), Random.Range(1.2f, 2.2f)), 1f).Scale(1f, 1.8f).Fade(FxFade.Late);
                }
                Heal(c, p, c.n.damage);
            }
            int damage = Mathf.RoundToInt(c.n.damage * .9f / 1.4f);
            foreach (var e in Enemies(at, c.n.radius)) Strike(c, e, damage, at, 9f, 1, "c_holy");
        }

        /// <summary>심판의 광창: a spear of light that pierces along the aim.</summary>
        IEnumerator LightSpear(Run c)
        {
            const float speed = 20f;
            Vector2 at = owner.Center + c.dir * .4f;
            var fx = CareerFx.Clip("b_spear", at, c.dir, 1f, 20f, VfxLayer.Top, true, null, false, 10f, true);
            Sound("c_holy", .6f);
            var hit = new System.Collections.Generic.HashSet<EnemyController>();
            float travelled = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { fx?.Stop(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + c.dir * step;
                fx?.Place(next);
                GlowLineFx.Spawn(at, next, new Color(1f, .9f, .55f, .7f), .3f, .2f, SkillFx.TopOrder);
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (hit.Add(e)) Strike(c, e, c.n.damage, at, 5f, 1, "c_holy");
                at = next;
                travelled += step;
                yield return null;
            }
            fx?.Stop();
            CareerFx.Clip("b_cross", at, Vector2.zero, 1f, 26f, VfxLayer.Top, false);
        }

        /// <summary>축복의 연결: a thread of light to each ally in turn, raising their damage.</summary>
        IEnumerator BlessChain(Run c)
        {
            Sound("c_holy", .8f);
            Vector2 from = owner.Center;
            foreach (var p in Allies(owner.Center, c.n.radius))
            {
                if (!Live(c)) yield break;
                GlowLineFx.Spawn(from, p.Center, new Color(1f, .85f, .45f, .85f), .35f, .3f, SkillFx.TopOrder + 1);
                CareerFx.Clip("b_cross", p.Center + Vector2.up * .3f, Vector2.zero, .9f, 22f, VfxLayer.Top, false);
                if (c.authority)
                {
                    var st = For(p);
                    st.bless = Mathf.RoundToInt(c.s.power * c.Scale);
                    st.blessEnd = Time.time + c.s.duration;
                }
                from = p.Center;
                yield return new WaitForSeconds(.08f);
            }
        }

        /// <summary>천상의 행진: pillars of light strike the monsters around, the party is cleansed, healed and shielded,
        /// and a sanctuary follows the bishop healing every second.</summary>
        IEnumerator Dawn(Run c)
        {
            Vector2 at = owner.Center;
            // The sky opens: a great column of light on the bishop, wings and light spread behind, pillars on every monster.
            CareerFx.Clip("b_pillar", owner.Position, Vector2.zero, 1.7f, 18f, VfxLayer.Top, false);
            VfxPlayer.Play("b_wings", owner.Center + Vector2.up * .2f, Vector2.zero, Color.white, 1.3f, 14f, false, 1.4f, false, VfxLayer.AtFeet, false, -60)?.FadeOut(.4f);
            CareerFx.Clip("b_lotus", owner.Position, Vector2.zero, c.n.radius / 1.6f, 16f, VfxLayer.Ground, false, new Color(1f, .95f, .7f, .8f), false, 1.2f).Squash(1f, .7f).FadeOut(.4f);
            Sound("c_holy");
            Sound("c_heavy", .5f);
            int damage = Mathf.RoundToInt(c.n.damage * 2.5f / 4.5f);
            foreach (var e in Enemies(at, c.n.radius))
            {
                CareerFx.Clip("b_pillar", e.Position, Vector2.zero, .8f, 22f, VfxLayer.AtFeet, false);
                Strike(c, e, damage, at, 10f, 2, "c_holy");
            }
            foreach (var p in Allies(at, c.n.radius))
            {
                if (c.authority && For(p).Cleanse()) CareerTrials.Record(owner, "cleanse", 1);
                Heal(c, p, c.n.damage);
                GiveShield(c, p, .25f, c.s.duration);
            }
            StartCoroutine(Sanctuary(c));
            yield break;
        }

        IEnumerator Sanctuary(Run c)
        {
            float interval = c.s.duration / c.s.hits;
            int amount = Mathf.Max(1, c.n.damage / 6);
            for (int i = 0; i < c.s.hits; i++)
            {
                yield return new WaitForSeconds(interval);
                if (!Live(c)) yield break;
                SkillFx.Spawn("fx_ring", owner.Position, new Color(1f, .9f, .55f, .7f), interval, SkillFx.GroundOrder + 14).Scale(new Vector2(2.6f, 1.8f), new Vector2(3f, 2.1f)).Fade(FxFade.InOut);
                foreach (var p in Allies(owner.Center, 4f))
                {
                    Heal(c, p, amount);
                    if (c.authority) { var st = For(p); st.hotSource = owner; st.hotEnd = Time.time + interval + .1f; }
                }
            }
        }
    }
}
