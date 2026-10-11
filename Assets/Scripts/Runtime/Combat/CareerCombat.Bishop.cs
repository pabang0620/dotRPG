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
                case "sanctuary": DivineGuard(c); break;
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
                Wings(target.Center + Vector2.up * .15f, target.Position.y, .75f, .7f);
                GiveShield(c, target, c.s.power, c.s.duration);
            }
            else Heal(c, target, c.n.damage);
        }

        /// <summary>생명의 파문: a circle that sends out a healing ripple every second.</summary>
        IEnumerator Bloom(Run c)
        {
            Vector2 at = owner.Position;
            // [VFX] W is life on the ground: a green lotus opens and turns slowly where it was cast (T is gold light from above).
            float k = c.n.radius / 1.25f;
            if (SkillFx.HasImage("fxi_life_lotus"))
                SkillFx.Spawn("FxImg/fxi_life_lotus", at, new Color(.8f, 1f, .82f, .9f), c.s.duration + .3f, SkillFx.GroundOrder + 9).Scale(new Vector2(k * .3f, k * .3f), new Vector2(k, k)).Fade(FxFade.Late);
            else CareerFx.Clip("b_lotus", at, Vector2.zero, c.n.radius / 1.6f, 14f, VfxLayer.Ground, false, new Color(1f, 1f, 1f, .9f), false, c.s.duration + .3f).Squash(1f, .7f).FadeOut(.4f);
            Sound("heal", .6f);
            float interval = c.s.duration / c.s.hits;
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                SkillFx.Spawn("fx_ring", at, new Color(.7f, 1f, .7f, .8f), .55f, SkillFx.GroundOrder + 14).Scale(new Vector2(.3f, .21f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
                foreach (var p in Allies(at, c.n.radius))
                {
                    Heal(c, p, c.n.damage);
                    FootHeal(p, LifeGreen);
                    if (c.authority) { var st = For(p); st.hotSource = owner; st.hotEnd = Time.time + interval + .1f; }
                }
                yield return new WaitForSeconds(interval);
            }
        }

        static readonly Color LifeGreen = new Color(.45f, 1f, .55f, 1f), HolyGold = new Color(1f, .88f, .45f, 1f);

        /// <summary>
        /// [VFX] Healing filling someone up from the feet: a glow pools under them, light rises up the body and small
        /// motes float away. Green for life (W), gold for the sanctuary (T).
        /// </summary>
        static void FootHeal(PlayerController p, Color tone)
        {
            if (p == null) return;
            Vector2 feet = p.Position;
            SkillFx.Spawn("fx_glow", feet, new Color(tone.r, tone.g, tone.b, .55f), .7f, SkillFx.At(feet.y, -2)).Additive().Scale(new Vector2(.5f, .25f), new Vector2(1.3f, .55f)).Fade(FxFade.Late);
            // The column of light rising from the ground up to the chest.
            SkillFx.Spawn("fx_glow", feet + Vector2.up * .1f, new Color(tone.r, tone.g, tone.b, .35f), .6f, SkillFx.At(feet.y, 8)).Additive().Move(Vector2.up * 1.2f, 2f).Scale(new Vector2(.5f, .3f), new Vector2(.55f, 1.4f)).Fade(FxFade.Late);
            for (int i = 0; i < 5; i++)
                SkillFx.Spawn("fx_spark", feet + new Vector2(Random.Range(-.3f, .3f), Random.Range(0f, .15f)), new Color(Mathf.Lerp(tone.r, 1f, .4f), Mathf.Lerp(tone.g, 1f, .4f), Mathf.Lerp(tone.b, 1f, .4f), 1f), Random.Range(.45f, .7f), SkillFx.At(feet.y, 9))
                    .Move(new Vector2(Random.Range(-.15f, .15f), Random.Range(1.6f, 2.4f)), 1.2f).Scale(.9f, .35f).Delay(i * .05f);
        }

        /// <summary>
        /// [VFX] Angel wings behind someone: the drawn pair opens from narrow to full and fades, under the body (wings
        /// image); otherwise the procedural flipbook.
        /// </summary>
        static void Wings(Vector2 at, float feetY, float scale, float life)
        {
            if (!SkillFx.HasImage("fxi_wings"))
            {
                VfxPlayer.Play("b_wings", at, Vector2.zero, new Color(1f, 1f, 1f, scale < 1f ? .75f : 1f), scale, scale < 1f ? 18f : 14f, false, life, false, VfxLayer.AtFeet, false, -60)?.FadeOut(life * .3f);
                return;
            }
            float k = scale * .62f;
            SkillFx.Spawn("fx_glow", at, new Color(1f, .95f, .75f, .45f), life, SkillFx.At(feetY, -61)).Additive().Scale(new Vector2(k * 1.2f, k * .8f), new Vector2(k * 3.2f, k * 1.8f)).Fade(FxFade.Late);
            SkillFx.Spawn(SkillFx.Pick("fxi_wings", "fx_holy"), at, Color.white, life, SkillFx.At(feetY, -60)).Scale(new Vector2(k * .25f, k * .8f), new Vector2(k, k)).Fade(FxFade.Late);
        }

        /// <summary>정화의 종: a ring of bell light that cleanses and heals allies and pushes monsters away.</summary>
        void Bell(Run c)
        {
            Vector2 at = owner.Center;
            // A bell of light swings and rings above the bishop; the ring of sound spreads over the ground.
            if (SkillFx.HasImage("fxi_bell"))
            {
                // [VFX] The drawn bell drops in, rings (swings and dies out) and leaves a flash of light.
                Vector2 bell = at + Vector2.up * 1.5f;
                SkillFx.Spawn("fx_glow", bell, new Color(1f, .9f, .55f, .55f), .7f, SkillFx.TopOrder + 3).Additive().Scale(1.2f, 2.6f).Fade(FxFade.Late);
                SkillFx.Spawn(SkillFx.Pick("fxi_bell", "fx_holy"), bell, Color.white, .95f, SkillFx.TopOrder + 4).Scale(.55f, .9f).Swing(16f, 2.4f).Fade(FxFade.Late);
            }
            else CareerFx.Clip("b_bell", at + Vector2.up * 1.4f, Vector2.zero, 1f, 20f, VfxLayer.Top, false);
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
            Wings(owner.Center + Vector2.up * .2f, owner.Position.y, 1.3f, 1.4f);
            // [VFX] T is light from above: a gold sanctuary sigil (no green lotus, that is W).
            if (SkillFx.HasImage("fxi_holy_sigil"))
                SkillFx.Spawn("FxImg/fxi_holy_sigil", owner.Position, new Color(1f, .92f, .62f, 1f), 1.4f, SkillFx.GroundOrder + 9).Scale(new Vector2(c.n.radius * .25f, c.n.radius * .25f), new Vector2(c.n.radius * .8f, c.n.radius * .8f)).Fade(FxFade.Late);
            else CareerFx.Clip("b_lotus", owner.Position, Vector2.zero, c.n.radius / 1.6f, 16f, VfxLayer.Ground, false, new Color(1f, .95f, .7f, .8f), false, 1.2f).Squash(1f, .7f).FadeOut(.4f);
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
                FootHeal(p, HolyGold);
                GiveShield(c, p, .25f, c.s.duration);
            }
            StartCoroutine(Sanctuary(c));
            yield break;
        }

        /// <summary>
        /// The sanctuary under the bishop: one steady gold sigil that follows the bishop's feet (no blinking, no turning).
        /// Allies inside are healed every interval and hit 50% harder while they stand in it.
        /// </summary>
        IEnumerator Sanctuary(Run c)
        {
            float interval = c.s.duration / c.s.hits;
            int amount = Mathf.Max(1, c.n.damage / 6);
            SkillFx sigil = SkillFx.HasImage("fxi_holy_sigil")
                ? SkillFx.Spawn("FxImg/fxi_holy_sigil", owner.Position, new Color(1f, .92f, .62f, .7f), c.s.duration + .2f, SkillFx.GroundOrder + 9).Scale(3.2f, 3.2f).Fade(FxFade.Late)
                : SkillFx.Spawn("fx_ring", owner.Position, new Color(1f, .9f, .55f, .7f), c.s.duration + .2f, SkillFx.GroundOrder + 14).Scale(new Vector2(2.8f, 1.95f), new Vector2(2.8f, 1.95f)).Fade(FxFade.Late);
            float end = Time.time + c.s.duration, nextTick = Time.time + interval;
            while (Time.time < end)
            {
                if (!Live(c)) { if (sigil != null) sigil.Kill(); yield break; }
                if (sigil != null) sigil.transform.position = owner.Position;
                foreach (var p in Allies(owner.Center, SanctumRadius))
                    if (c.authority) For(p).sanctumEnd = Time.time + .25f; // [BALANCE] +50% damage while inside
                if (Time.time >= nextTick)
                {
                    nextTick += interval;
                    foreach (var p in Allies(owner.Center, SanctumRadius))
                    {
                        Heal(c, p, amount);
                        FootHeal(p, HolyGold);
                        if (c.authority) { var st = For(p); st.hotSource = owner; st.hotEnd = Time.time + interval + .1f; }
                    }
                }
                yield return null;
            }
        }

        const float SanctumRadius = 4f;

        /// <summary>신의 가호: every ally in reach (and the bishop) takes no damage for a moment, with a golden glow.</summary>
        void DivineGuard(Run c)
        {
            Pose(.2f, 2);
            Sound("c_holy");
            CareerFx.Clip("b_pillar", owner.Position, Vector2.zero, c.n.radius / 2.2f, 18f, VfxLayer.Ground, false);
            CareerFx.Clip("b_wings", owner.Center, Vector2.zero, 1.2f, 20f);
            // [UX] The exact reach on the ground: a gold ring of the skill's radius that holds, then fades.
            SkillFx.Spawn("fx_ring", owner.Position, new Color(1f, .88f, .4f, .9f), 1.1f, SkillFx.GroundOrder + 14)
                .Scale(new Vector2(c.n.radius, c.n.radius * .7f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
            SkillFx.Spawn("fx_glow", owner.Position, new Color(1f, .9f, .5f, .22f), .9f, SkillFx.GroundOrder + 13).Additive()
                .Scale(new Vector2(c.n.radius * 1.6f, c.n.radius * 1.1f), new Vector2(c.n.radius * 1.9f, c.n.radius * 1.3f)).Fade(FxFade.Late);
            var allies = Allies(owner.Center, c.n.radius);
            if (!allies.Contains(owner)) allies.Add(owner);
            foreach (var p in allies)
            {
                if (p == null || p.IsDead) continue;
                if (c.authority) p.Health.SetInvulnerable(c.s.duration);
                PowerAura.Play(p, c.s.duration, new Color(1f, .9f, .45f), new Color(1f, 1f, .85f));
                CareerFx.Clip("b_cross", p.Center, Vector2.zero, .9f, 22f);
                WorldPopupText.Show(p.transform, $"{c.s.duration:0.#}초 무적", new Color(1f, .9f, .45f), c.s.duration + .5f); // [UX] who is protected, and for how long
            }
        }
    }
}
