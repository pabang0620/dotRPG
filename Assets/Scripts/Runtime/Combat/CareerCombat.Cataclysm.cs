using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>메이지 awakening 유성우 (split from CareerCombat.Arcanist.cs to keep files under 500 lines).</summary>
    public sealed partial class CareerCombat
    {
        /// <summary>
        /// 유성우 (v5, 2026-10-12, Docs/PLAN_SKILL_VFX.md, replaces 천체 붕괴): the world darkens and a storm cloud as wide as the
        /// area gathers over it (0.8 s); for 4 s 192 fire, ice and lightning comets rain on the area, most aimed at monsters, each
        /// hitting a small area (7.5%) and leaving an elemental mark that burns or chills whoever stands on it (damage over
        /// time, through armour); then three great comets, one of each element, strike together (150% each) and the
        /// cloud fades. The mage's awakening is area damage over time, not a stagger.
        /// </summary>
        IEnumerator Cataclysm(Run c)
        {
            const float Gather = .8f, MarkLife = 3f, Tick = .5f;
            // [CH3 SPEC] 대혜성 (A): 12 comets in 2 s, each worth 16 (120%), landing 2.5 m wide with marks twice as big.
            // 성운 폭우 (B): 288 comets over 12 s at two thirds (5%) each, every hit slowing for 1.5 s.
            bool great = c.n.HasExtra("comet_great"), storm = c.n.HasExtra("comet_storm");
            float Rain = great ? 2f : storm ? 12f : 4f;
            int Comets = great ? 12 : storm ? 288 : 192;
            float cometShare = great ? .075f * 16f : storm ? .05f : .075f, cometRadius = great ? 2.5f : 1.1f, markScale = great ? 2f : 1f;
            Vector2 center = owner.Position;
            float R = c.n.radius;
            int order = SkillFx.TopOrder + 4;
            Color[] colors = { FireColor, IceColor, new Color(1f, .92f, .4f, .95f), new Color(.45f, .7f, 1f, .95f) };
            if (c.n.spec != '\0') colors[3] = SkillSpecs.Tint(c.n, colors[3]);
            int hitDamage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * cometShare));
            int markDamage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * .1f));
            var marks = new System.Collections.Generic.List<(Vector2 at, int element, float until, float next)>();
            // Overlapping marks do not stack: a monster takes the mark damage at most once per tick.
            var markHitUntil = new System.Collections.Generic.Dictionary<EnemyController, float>();

            // 1) The sky darkens and the storm cloud gathers high over the area. The mage faces the viewer (it is a
            //    circle around the mage, not aimed). The cloud's shadow on the ground marks where the comets fall.
            owner.FaceTowards(owner.Position + Vector2.down);
            var shadow = new GameObject("CloudShadow").AddComponent<SpriteRenderer>();
            if (Fx.Root != null) shadow.transform.SetParent(Fx.Root, false);
            shadow.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Alpha != null) shadow.sharedMaterial = FxMaterials.Alpha;
            shadow.sortingOrder = SkillFx.GroundOrder + 2;
            float shadowW = Mathf.Max(.01f, shadow.sprite.bounds.size.x);
            DimFx.Show(Gather + Rain + 1.4f, .35f);
            // A storm cloud as wide as the area hangs over it (m6_cloud, alpha blended; else a dark glow).
            var nebula = new GameObject("StormCloud").AddComponent<SpriteRenderer>();
            if (Fx.Root != null) nebula.transform.SetParent(Fx.Root, false);
            bool art = VfxLibrary.Has("m6_cloud") && VfxLibrary.Get("m6_cloud").Length > 0;
            nebula.sprite = art ? VfxLibrary.Get("m6_cloud")[0] : Game.Art.Get("fx_glow");
            if (FxMaterials.Alpha != null) nebula.sharedMaterial = FxMaterials.Alpha;
            nebula.sortingOrder = SkillFx.TopOrder - 2;
            float nebulaW = Mathf.Max(.01f, nebula.sprite.bounds.size.x);
            var nebulaTint = art ? Color.white : new Color(.16f, .14f, .26f);
            float flashUntil = 0f;
            void Nebula(float alpha, float t)
            {
                if (nebula == null) return;
                // High in the sky above the area (lifted, so it reads as overhead, not as fog around the fight),
                // drifting a little and now and then lit from inside by lightning; its shadow darkens the area below.
                nebula.transform.position = center + new Vector2(.15f * Mathf.Sin(t * .7f), R * .95f);
                float s = R * 2f / nebulaW * (1f + .03f * Mathf.Sin(t * 1.1f));
                nebula.transform.localScale = new Vector3(s, art ? s : s * .55f, 1f);
                if (shadow != null)
                {
                    shadow.transform.position = center + new Vector2(.15f * Mathf.Sin(t * .7f), 0f);
                    float k = R * 2.6f / shadowW;
                    shadow.transform.localScale = new Vector3(k, k, 1f);
                    shadow.color = new Color(.05f, .04f, .1f, alpha * .45f);
                }
                if (Time.time > flashUntil && Random.value < .02f) flashUntil = Time.time + .08f;
                float lit = Time.time < flashUntil ? 1.6f : 1f;
                nebula.color = new Color(Mathf.Min(1f, nebulaTint.r * lit), Mathf.Min(1f, nebulaTint.g * lit), Mathf.Min(1f, nebulaTint.b * lit), alpha * .85f);
            }
            void Cleanup()
            {
                if (nebula != null) Destroy(nebula.gameObject);
                if (shadow != null) Destroy(shadow.gameObject);
            }
            Sound("c_arcane", .7f);
            float clock = 0f;
            for (float t = 0f; t < Gather; t += Time.deltaTime)
            {
                if (!Live(c)) { Cleanup(); yield break; }
                clock += Time.deltaTime;
                Nebula(.85f * t / Gather, clock);
                if (Random.value < .35f) SkillVisuals.Sparks(center + Vector2.up * R * .45f + Random.insideUnitCircle * R * .5f, colors[3], 1, 1f, .4f);
                yield return null;
            }

            // A comet lands: a small hit and an elemental mark that works for MarkLife seconds.
            void Land(Vector2 at, int element, float radius, int damage, bool big)
            {
                SkillVisuals.Flash(at, new Color(Mathf.Lerp(colors[element].r, 1f, .5f), Mathf.Lerp(colors[element].g, 1f, .5f), Mathf.Lerp(colors[element].b, 1f, .5f), .8f), radius * (big ? 1.4f : 1f), .12f);
                SkillVisuals.Sparks(at, colors[element], big ? 10 : 4, big ? 5f : 3f, .35f);
                if (big) { CareerFx.Clip("impact", at, Vector2.zero, radius / 1.7f, 22f, VfxLayer.Ground, false, CareerFx.Violet); if (owner.IsLocal) Game.Camera?.Shake(.2f, .25f); }
                Sound(element == 0 ? "c_fire" : element == 1 ? "c_ice" : element == 2 ? "c_thunder" : "c_arcane", big ? 1f : .35f);
                bool any = false;
                foreach (var e in Enemies(at, radius))
                {
                    if (!Strike(c, e, damage, at, big ? 8f : 2f, big ? 2 : 1, "c_arcane")) continue;
                    any = true;
                    if (element == 1) Freeze(c, e, big ? 1.2f : .4f);
                    if (storm && c.authority) e.Slow(.3f, 1.5f);
                }
                // The element-memory synergy counts fire, ice and lightning hits as before.
                if (any && element < 3) ElementLanded(c, element == 0 ? "fire" : element == 1 ? "ice" : "storm");
                GroundMarkFx.Spawn(at, element, colors[element], big ? 2.4f : 1.3f * markScale, MarkLife);
                marks.Add((at, element, Time.time + MarkLife, Time.time + Tick));
            }

            // 2) The rain: comets over 4 s, most aimed at monsters, the rest anywhere in the area.
            float every = Rain / Comets, nextComet = 0f;
            int launched = 0;
            for (float t = 0f; t < Rain + .4f; t += Time.deltaTime)
            {
                if (!Live(c)) { Cleanup(); yield break; }
                clock += Time.deltaTime;
                Nebula(.85f, clock);
                if (launched < Comets && t >= nextComet)
                {
                    nextComet += every;
                    launched++;
                    var foes = Enemies(center, R);
                    Vector2 target = foes.Count > 0 && Random.value < .65f
                        ? foes[Random.Range(0, foes.Count)].Position + Random.insideUnitCircle * .4f
                        : center + Random.insideUnitCircle * R * .9f;
                    int element = launched % 4 == 0 ? 3 : Random.Range(0, 3);
                    CometFx.Spawn(target, colors[element], great ? .5f : .32f, great ? 1.6f : 1f, order, () => Land(target, element, cometRadius, hitDamage, great));
                }
                TickMarks();
                yield return null;
            }

            // 3) Three great comets, one of each element, strike together.
            for (int i = 0; i < 3; i++)
            {
                Vector2 target = center + Rotate(c.dir, 90f + i * 120f) * R * .35f;
                int element = i;
                CometFx.Spawn(target, colors[element], .45f, 2f, order + 4, () => Land(target, element, R * .4f, Mathf.RoundToInt(c.n.damage * 1.5f), true));
            }
            Sound("c_heavy");
            for (float t = 0f; t < MarkLife + .2f; t += Time.deltaTime)
            {
                if (!Live(c)) { Cleanup(); yield break; }
                clock += Time.deltaTime;
                Nebula(.85f * Mathf.Clamp01(1f - (t - .4f) / 1f), clock); // the nebula clears after the finale
                TickMarks();
                yield return null;
            }
            Cleanup();

            // Each mark burns, chills or shocks whoever stands on it every 0.5 s (through armour).
            void TickMarks()
            {
                for (int i = marks.Count - 1; i >= 0; i--)
                {
                    var m = marks[i];
                    if (Time.time > m.until) { marks.RemoveAt(i); continue; }
                    if (Time.time < m.next) continue;
                    m.next += Tick;
                    marks[i] = m;
                    foreach (var e in Enemies(m.at, .9f * markScale))
                    {
                        if (markHitUntil.TryGetValue(e, out float until) && Time.time < until) continue;
                        markHitUntil[e] = Time.time + Tick - .01f;
                        if (c.authority) SecondaryHit(e, markDamage, true);
                    }
                }
            }
        }
    }
}
