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
            CareerFx.Sigil(owner.Position, 1.1f, CareerFx.Holy, .5f);
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
            var fx = CareerFx.Feather(start, shield ? CareerFx.Holy : CareerFx.Life);
            Vector2 at = start;
            for (float t = 0; t < flight; t += Time.deltaTime)
            {
                if (!Live(c) || target == null || target.IsDead) { fx.Kill(); yield break; }
                float u = Mathf.Clamp01((t + Time.deltaTime) / flight);
                Vector2 next = Vector2.Lerp(start, target.Center, u) + Vector2.up * Mathf.Sin(u * Mathf.PI) * .8f;
                CareerFx.OrbTrail(next, (next - at).normalized, CareerFx.Holy);
                fx.MoveTo(next);
                at = next;
                yield return null;
            }
            fx.Kill();
            if (shield) GiveShield(c, target, c.s.power, c.s.duration);
            else Heal(c, target, c.n.damage);
        }

        /// <summary>생명의 파문: a circle that sends out a healing ripple every second.</summary>
        IEnumerator Bloom(Run c)
        {
            Vector2 at = owner.Position;
            CareerFx.Sigil(at, c.n.radius, CareerFx.Life, c.s.duration + .3f, -50f);
            Sound("heal", .6f);
            float interval = c.s.duration / c.s.hits;
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                CareerFx.Shock(at, c.n.radius, CareerFx.Life, .5f);
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
            SkillFx.Spawn("fx_holy", at + Vector2.up * 1.1f, CareerFx.Holy, .6f, SkillFx.TopOrder + 4).Scale(.6f, 1.6f).Pop().Fade(FxFade.Late);
            CareerFx.Shock(at, c.n.radius, CareerFx.Holy, .4f);
            CareerFx.Shock(at, c.n.radius * .6f, Color.white, .3f, .08f);
            Sound("c_holy");
            foreach (var p in Allies(at, c.n.radius))
            {
                if (c.authority && For(p).Cleanse()) CareerTrials.Record(owner, "cleanse", 1);
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
            var fx = CareerFx.Spear(at, c.dir, CareerFx.Holy, false);
            Sound("c_holy", .6f);
            var hit = new System.Collections.Generic.HashSet<EnemyController>();
            float travelled = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { fx.Kill(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + c.dir * step;
                fx.MoveTo(next);
                GlowLineFx.Spawn(at, next, new Color(1f, .9f, .55f, .7f), .3f, .2f, SkillFx.TopOrder);
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (hit.Add(e) && Strike(c, e, c.n.damage, at, 5f, 1, "c_holy"))
                        SkillFx.Spawn("fx_holy", e.Center, CareerFx.Holy, .3f, SkillFx.TopOrder + 4).Scale(.5f, .9f).Pop().Fade(FxFade.Late);
                at = next;
                travelled += step;
                yield return null;
            }
            fx.Kill();
            SkillVisuals.Flash(at, CareerFx.Holy, 1.2f, .15f);
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
                CareerFx.Bless(p.Center, CareerFx.Holy, false);
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
            CareerFx.Pillar(owner.Position, 2.4f, CareerFx.Holy, .8f);
            CareerFx.Sigil(owner.Position, c.n.radius, CareerFx.Holy, 1.2f, -80f);
            CareerFx.Shock(at, c.n.radius, CareerFx.Holy, .5f);
            Sound("c_holy");
            Sound("c_heavy", .5f);
            int damage = Mathf.RoundToInt(c.n.damage * 2.5f / 4.5f);
            foreach (var e in Enemies(at, c.n.radius))
            {
                CareerFx.Pillar(e.Position, 1f, CareerFx.Holy, .5f);
                Strike(c, e, damage, at, 10f, 2, "c_holy");
            }
            foreach (var p in Allies(at, c.n.radius))
            {
                if (c.authority && For(p).Cleanse()) CareerTrials.Record(owner, "cleanse", 1);
                Heal(c, p, c.n.damage);
                GiveShield(c, p, .25f, c.s.duration);
            }
            SkillVisuals.UltFinish(at, CareerFx.Holy, c.n.radius);
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
                CareerFx.Sigil(owner.Position, 3f, CareerFx.Holy, interval, -60f);
                foreach (var p in Allies(owner.Center, 4f))
                {
                    Heal(c, p, amount);
                    if (c.authority) { var st = For(p); st.hotSource = owner; st.hotEnd = Time.time + interval + .1f; }
                }
            }
        }
    }
}
