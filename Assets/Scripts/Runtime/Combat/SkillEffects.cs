using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{










    /// <summary>
    /// The look of every skill, built from the pieces above. Gameplay code only says what happened
    /// and where; all colours, counts and timings live here.
    /// </summary>
    public static class SkillVisuals
    {
        public static readonly Color ArcGlow = new Color(0.45f, 0.9f, 1f, 1f);
        public static readonly Color ArcAura = new Color(0.36f, 0.26f, 0.95f, 0.75f);
        public static readonly Color MageViolet = new Color(0.66f, 0.52f, 1f, 0.9f);
        public static readonly Color FrostBlue = new Color(0.6f, 0.88f, 1f, 0.9f);
        public static readonly Color WhirlGold = new Color(1f, 0.82f, 0.35f, 0.9f);
        public static readonly Color EarthOrange = new Color(1f, 0.66f, 0.28f, 0.8f);

        /// <summary>Burst of light.</summary>
        public static void Flash(Vector2 pos, Color color, float size, float life = 0.18f, int order = SkillFx.TopOrder)
        {
            SkillFx.Spawn("fx_glow", pos, color, life, order).Additive().Scale(size * 0.35f, size).Fade(FxFade.Quick);
        }

        /// <summary>Sparks flying out of a point, drawn as short streaks.</summary>
        public static void Sparks(Vector2 pos, Color color, int count, float speed, float life = 0.22f, int order = SkillFx.TopOrder)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                float size = Random.Range(0.8f, 1.2f);
                SkillFx.Spawn("fx_streak", pos + dir * 0.08f, color, life * Random.Range(0.7f, 1.2f), order)
                    .Move(dir * speed * Random.Range(0.6f, 1.2f), 6f).FaceMotion().Scale(size, size * 0.5f).Fade(FxFade.Late);
            }
        }

        /// <summary>Two magic circles turning in opposite directions under a caster's feet.</summary>
        public static void CastCircle(Vector2 feet, Color color)
        {
            SkillFx.Spawn("fx_rune", feet, color, 0.6f, SkillFx.GroundOrder + 6).Scale(0.45f, 1f).Spin(-150f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_rune", feet, new Color(1f, 1f, 1f, color.a * 0.45f), 0.5f, SkillFx.GroundOrder + 7)
                .Rotate(30f).Scale(0.45f, 1f).Spin(200f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_glow", feet, new Color(color.r, color.g, color.b, 0.5f), 0.55f, SkillFx.GroundOrder + 5)
                .Additive().Scale(0.8f, 1.6f).Fade(FxFade.InOut);
        }

        /// <summary>Flash at the tip of the staff when a spell leaves it.</summary>
        public static void StaffFlash(Vector2 pos, Color color)
        {
            Flash(pos, color, 0.9f, 0.16f);
            Sparks(pos, color, 4, 3f, 0.18f);
        }

        // ---------- 회전 베기 (Whirl) ----------

        public static void Whirl(Vector2 center, Vector2 feet, float radius)
        {
            float s = radius / 1.7f; // fx_swoosh's outer edge is 1.7 units at scale 1
            int order = SkillFx.At(feet.y, 60);
            // A gold blade, its glow, and a faster pale inner blade.
            SkillFx.Spawn("fx_blade", center, Color.white, 0.36f, order + 1).Scale(s * 0.85f, s).Spin(-1300f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_swoosh", center, new Color(WhirlGold.r, WhirlGold.g, WhirlGold.b, 0.45f), 0.36f, order)
                .Additive().Rotate(180f).Scale(s * 0.9f, s * 1.08f).Spin(-1300f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_blade", center, new Color(1f, 1f, 1f, 0.85f), 0.28f, order + 2).Rotate(120f).Scale(s * 0.55f, s * 0.72f).Spin(-1800f).Fade(FxFade.Late);
            // Shock ring at the edge and a golden flash in the middle.
            SkillFx.Spawn("fx_shock", center, new Color(1f, 0.7f, 0.25f, 0.85f), 0.3f, order - 1).Scale(radius * 0.5f, radius * 1.05f).Fade(FxFade.Quick);
            Flash(center, new Color(1f, 0.8f, 0.35f, 0.45f), radius * 1.1f, 0.2f, order - 2);
            // Wind streaks swirling clockwise, and dust pushed out along the ground.
            for (int i = 0; i < 16; i++)
            {
                float ang = i * Mathf.PI * 2f / 16f + Random.Range(-0.15f, 0.15f);
                var radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var tangent = new Vector2(radial.y, -radial.x);
                float r = radius * Random.Range(0.45f, 0.95f);
                SkillFx.Spawn("fx_streak", center + radial * r, new Color(1f, 0.88f, 0.5f, 0.95f), Random.Range(0.18f, 0.28f), order + 3)
                    .Move(tangent * Random.Range(6f, 9f) + radial * 1.5f, 4f).FaceMotion().Scale(1.3f, 0.6f);
            }
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f + Random.Range(-0.2f, 0.2f);
                var radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                SkillFx.Spawn("fx_dust", feet + radial * radius * 0.6f, new Color(1f, 0.98f, 0.92f, 0.8f), 0.4f, SkillFx.GroundOrder + 10)
                    .Move(radial * 2f, 5f).Scale(0.8f, 1.5f);
            }
        }

        /// <summary>Sword-cut flash on a monster hit by a melee skill.</summary>
        public static void SlashHit(Vector2 pos, Color color)
        {
            float rot = Random.Range(0f, 180f);
            SkillFx.Spawn("fx_cut", pos, Color.white, 0.16f, SkillFx.TopOrder + 1).Rotate(rot)
                .Scale(new Vector2(0.4f, 1f), new Vector2(1.3f, 0.6f)).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_cut", pos, color, 0.2f, SkillFx.TopOrder).Additive().Rotate(rot)
                .Scale(new Vector2(0.6f, 1.6f), new Vector2(1.6f, 1f)).Fade(FxFade.Quick);
            Flash(pos, new Color(color.r, color.g, color.b, 0.6f), 1.1f, 0.14f);
            Sparks(pos, color, 6, 7f, 0.18f);
        }

        // ---------- 대지 강타 (Slam) ----------




        // ---------- 번개 사슬 (Arc) ----------

        /// <summary>One jump of the chain: the bolt plus the impact where it lands.</summary>
        public static void ArcBolt(Vector2 from, Vector2 to, bool landed)
        {
            LightningFx.Strike(from, to, ArcGlow, ArcAura, 0.34f);
            Flash(to, new Color(0.6f, 0.85f, 1f, 0.5f), landed ? 1.3f : 0.9f, 0.2f);
            SkillFx.Spawn("fx_zap", to, Color.white, 0.2f, SkillFx.TopOrder + 3).Rotate(Random.Range(0f, 45f))
                .Scale(landed ? 0.7f : 0.5f, landed ? 1.25f : 0.9f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_ring", to, new Color(0.55f, 0.6f, 1f, 0.85f), 0.22f, SkillFx.TopOrder - 2).Scale(0.2f, 0.85f).Fade(FxFade.Quick);
            Sparks(to, new Color(0.6f, 0.92f, 1f, 1f), landed ? 9 : 5, 7f, 0.22f);
            if (!landed) return;
            // Small arcs crackling over the monster.
            for (int k = 0; k < 2; k++)
            {
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(0.4f, 0.6f);
                LightningFx.Strike(to + off * 0.25f, to + off, ArcGlow, ArcAura, 0.2f, 0.45f);
            }
        }

        // ---------- 서리 폭발 (Frost Nova) ----------

        public static void Nova(Vector2 center, Vector2 feet, float radius)
        {
            // Frosted ground and a frost sigil under the caster.
            SkillFx.Spawn("fx_frost", feet, new Color(1f, 1f, 1f, 0.9f), 1.5f, SkillFx.GroundOrder + 3)
                .Scale(radius / 1.5f * 0.35f, radius / 1.5f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_rune", feet, new Color(0.6f, 0.9f, 1f, 0.9f), 0.9f, SkillFx.GroundOrder + 6)
                .Scale(radius * 0.3f, radius * 0.95f).Spin(-90f).Fade(FxFade.Late);
            // A bright burst that lights up the area.
            Flash(center, new Color(0.8f, 0.95f, 1f, 0.6f), radius * 1.0f, 0.22f);
            SkillFx.Spawn("fx_glow", center, new Color(0.45f, 0.75f, 1f, 0.25f), 0.45f, SkillFx.TopOrder - 10)
                .Additive().Scale(radius * 1.5f, radius * 3f).Fade(FxFade.Quick);
            // Shock rings.
            SkillFx.Spawn("fx_shock", center, new Color(0.6f, 0.88f, 1f, 0.95f), 0.35f, SkillFx.TopOrder - 5).Scale(radius * 0.3f, radius).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_shock", center, new Color(0.85f, 0.97f, 1f, 0.7f), 0.28f, SkillFx.TopOrder - 4).Scale(radius * 0.15f, radius * 0.7f).Fade(FxFade.Quick).Delay(0.05f);
            SkillFx.Spawn("fx_shock", center, new Color(0.45f, 0.75f, 1f, 0.4f), 0.45f, SkillFx.TopOrder - 6).Additive().Scale(radius * 0.4f, radius * 1.15f);
            // Ice shards flying out to the edge.
            const int shards = 18;
            for (int i = 0; i < shards; i++)
            {
                float ang = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / shards;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                SkillFx.Spawn("fx_shard", center + dir * 0.2f, Color.white, Random.Range(0.3f, 0.38f), SkillFx.TopOrder - 3)
                    .Move(dir * (radius / 0.22f) * Random.Range(0.8f, 1.1f), 3.5f).FaceMotion().Scale(Random.Range(1f, 1.4f), 0.8f).Fade(FxFade.Late);
            }
            // Crystals bursting out of the ground around the edge, then breaking apart.
            int crystals = Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 8, 14);
            for (int i = 0; i < crystals; i++)
            {
                float ang = (i + Random.Range(-0.25f, 0.25f)) * Mathf.PI * 2f / crystals;
                Vector2 q = feet + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius * Random.Range(0.8f, 0.95f);
                float size = Random.Range(0.75f, 1.1f);
                float delay = 0.06f + Random.Range(0f, 0.06f);
                SkillFx.Spawn("fx_ice", q, Color.white, 0.75f, SkillFx.At(q.y, 3)).Scale(size, size).Pop().Flip(Random.value < 0.5f).Delay(delay);
                for (int k = 0; k < 2; k++)
                {
                    Vector2 d = Random.insideUnitCircle.normalized;
                    SkillFx.Spawn("fx_shard", q + Vector2.up * 0.3f, new Color(0.9f, 0.97f, 1f, 1f), 0.3f, SkillFx.At(q.y, 4))
                        .Move(d * 2.2f + Vector2.up, 4f).FaceMotion().Scale(0.7f, 0.5f).Delay(delay + 0.55f);
                }
            }
            // Snow drifting down.
            for (int i = 0; i < 16; i++)
            {
                Vector2 p = center + Random.insideUnitCircle * radius;
                SkillFx.Spawn("fx_snow", p, new Color(1f, 1f, 1f, 0.95f), Random.Range(0.7f, 1.2f), SkillFx.TopOrder - 2)
                    .Move(new Vector2(Random.Range(-0.3f, 0.3f), -Random.Range(0.3f, 0.7f))).Spin(Random.Range(-200f, 200f))
                    .Fade(FxFade.InOut).Delay(Random.Range(0f, 0.2f));
            }
        }

        // ---------- Mage basic attack ----------

        public static void BoltImpact(Vector2 pos, bool landed, int order)
        {
            Flash(pos, new Color(0.75f, 0.65f, 1f, 0.85f), landed ? 1.4f : 0.9f, 0.16f, order);
            SkillFx.Spawn("fx_ring", pos, new Color(0.8f, 0.7f, 1f, 0.8f), 0.18f, order + 1).Scale(0.1f, landed ? 0.6f : 0.4f).Fade(FxFade.Quick);
            Sparks(pos, new Color(0.7f, 0.95f, 1f, 1f), landed ? 6 : 3, 5f, 0.18f, order + 2);
        }

        public static readonly Color FireOrange = new Color(1f, 0.55f, 0.15f, 0.95f);
        public static readonly Color UltGold = new Color(1f, 0.84f, 0.35f, 1f);

        // ---------- 검기 (Sword Wave) ----------

        static float Angle(Vector2 dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;




        // ---------- 전쟁 함성 (War Cry) ----------

        public static void WarCry(Vector2 center, Vector2 feet, float radius)
        {
            int order = SkillFx.At(feet.y, 50);
            // Sound waves rolling out, a red-gold flash and dust blown along the ground.
            for (int i = 0; i < 3; i++)
                SkillFx.Spawn("fx_shock", center, new Color(1f, 0.5f - i * 0.08f, 0.2f, 0.9f), 0.45f, order + i)
                    .Scale(radius * 0.2f, radius * (0.75f + i * 0.2f)).Fade(FxFade.Quick).Delay(i * 0.09f);
            SkillFx.Spawn("fx_ring", center, new Color(1f, 0.9f, 0.6f, 0.9f), 0.3f, order + 4).Scale(0.3f, radius * 0.8f).Fade(FxFade.Quick);
            Flash(center, new Color(1f, 0.45f, 0.2f, 0.5f), radius * 1.3f, 0.3f, order - 1);
            SkillFx.Spawn("fx_rune", feet, new Color(1f, 0.45f, 0.2f, 0.9f), 0.6f, SkillFx.GroundOrder + 6).Scale(0.5f, radius * 0.6f).Spin(-200f).Fade(FxFade.Late);
            for (int i = 0; i < 12; i++)
            {
                float ang = i * Mathf.PI * 2f / 12f + Random.Range(-0.15f, 0.15f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                SkillFx.Spawn("fx_dust", feet + dir * 0.4f, new Color(1f, 0.95f, 0.85f, 0.75f), 0.45f, SkillFx.GroundOrder + 10).Move(dir * 3f, 4f).Scale(0.8f, 1.6f);
                SkillFx.Spawn("fx_streak", center + dir * 0.5f, new Color(1f, 0.7f, 0.35f, 0.9f), 0.25f, order + 5).Move(dir * 7f, 3f).FaceMotion().Scale(1.3f, 0.6f);
            }
        }

        // ---------- 천검 강림 (Sword Rain, warrior awakening) ----------

        /// <summary>A giant sword falling from the sky onto <paramref name="ground"/>, with a light beam and a target mark.</summary>
        public static void SwordDrop(Vector2 ground, float fallTime)
        {
            const float height = 5f;
            SkillFx.Spawn(SkillFx.Pick("fxi_bigsword", "fx_bigsword"), ground + Vector2.up * height, Color.white, fallTime, SkillFx.TopOrder + 4)
                .Move(Vector2.down * (height / fallTime)).Fade(FxFade.None);
            GlowLineFx.Spawn(ground + Vector2.up * height, ground, new Color(1f, 0.85f, 0.4f, 0.7f), 0.5f, fallTime + 0.12f, SkillFx.TopOrder + 3);
            SkillFx.Spawn("fx_ring", ground, new Color(1f, 0.8f, 0.3f, 0.9f), fallTime, SkillFx.GroundOrder + 8).Scale(1.4f, 0.4f).Fade(FxFade.None);
        }

        public static void SwordImpact(Vector2 ground, float radius)
        {
            // The sword stays stuck in the ground for a moment.
            SkillFx.Spawn(SkillFx.Pick("fxi_bigsword", "fx_bigsword"), ground, Color.white, 0.75f, SkillFx.At(ground.y, 6)).Fade(FxFade.Late);
            SkillFx.Crack(ground, radius, 1.2f);
            SkillFx.Spawn("fx_shock", ground + Vector2.up * 0.1f, new Color(1f, 0.78f, 0.3f, 0.95f), 0.3f, SkillFx.TopOrder).Scale(radius * 0.3f, radius * 1.2f).Fade(FxFade.Quick);
            Flash(ground + Vector2.up * 0.3f, new Color(1f, 0.85f, 0.45f, 0.6f), radius * 1.8f, 0.2f);
            Sparks(ground + Vector2.up * 0.2f, new Color(1f, 0.9f, 0.5f, 1f), 8, 7f, 0.22f);
            Fx.Burst("fx_chip", ground, 4, 3f, 0.6f);
        }

        // ---------- 빙뢰구 (Frost Orb: ice + lightning) ----------

        /// <summary>The flying orb: a faceted ice core in a cold glow, with a crackling electric halo.</summary>
        public static MovingFx FrostOrbHead(Vector2 pos, Vector2 dir)
        {
            var fx = new MovingFx();
            fx.Add(SkillFx.Spawn("fx_glow", pos, new Color(0.4f, 0.75f, 1f, 0.7f), 10f, SkillFx.TopOrder + 1).Additive().Scale(1.7f, 1.7f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn("fx_zap", pos, new Color(0.7f, 0.6f, 1f, 0.85f), 10f, SkillFx.TopOrder + 2).Additive().Scale(0.95f, 0.95f).Spin(-520f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn("fx_frostorb", pos, Color.white, 10f, SkillFx.TopOrder + 3).Rotate(Angle(dir)).Scale(1.15f, 1.15f).Spin(260f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn("fx_zap", pos, new Color(0.85f, 0.97f, 1f, 0.9f), 10f, SkillFx.TopOrder + 4).Additive().Rotate(22f).Scale(0.6f, 0.6f).Spin(700f).Fade(FxFade.None));
            Flash(pos, new Color(0.6f, 0.9f, 1f, 0.7f), 1.3f, 0.16f);
            return fx;
        }

        /// <summary>Frost and sparks left behind the orb.</summary>
        public static void FrostOrbTrail(Vector2 pos, Vector2 dir)
        {
            Vector2 back = -dir;
            SkillFx.Spawn("fx_snow", pos + back * 0.18f + Random.insideUnitCircle * 0.12f, new Color(1f, 1f, 1f, 0.95f), Random.Range(0.3f, 0.5f), SkillFx.TopOrder)
                .Move(back * 1.2f + Random.insideUnitCircle * 0.6f, 2.5f).Spin(Random.Range(-300f, 300f)).Scale(1.1f, 0.5f);
            SkillFx.Spawn("fx_glow", pos + back * 0.25f, new Color(0.45f, 0.8f, 1f, 0.35f), 0.2f, SkillFx.TopOrder).Additive().Scale(0.9f, 0.4f);
            if (Random.value < 0.35f)
                SkillFx.Spawn("fx_shard", pos, new Color(0.9f, 0.98f, 1f, 1f), 0.3f, SkillFx.TopOrder)
                    .Move(back * 2.2f + Random.insideUnitCircle * 1.4f, 3f).FaceMotion().Scale(0.8f, 0.5f);
            if (Random.value < 0.3f)
                SkillFx.Spawn("fx_spark", pos + Random.insideUnitCircle * 0.2f, new Color(0.7f, 0.6f, 1f, 1f), 0.25f, SkillFx.TopOrder)
                    .Move(back * 1.5f + Random.insideUnitCircle * 1.8f, 2f).Scale(0.9f, 0.4f);
        }

        /// <summary>A small bolt jumping from the orb to a monster it passes.</summary>
        public static void FrostOrbZap(Vector2 from, Vector2 to)
        {
            LightningFx.Strike(from, to, ArcGlow, ArcAura, 0.18f, 0.6f);
            Flash(to, new Color(0.65f, 0.85f, 1f, 0.55f), 0.9f, 0.14f);
            SkillFx.Spawn("fx_zap", to, Color.white, 0.14f, SkillFx.TopOrder + 3).Rotate(Random.Range(0f, 45f)).Scale(0.4f, 0.75f).Fade(FxFade.Late);
            Sparks(to, new Color(0.7f, 0.92f, 1f, 1f), 4, 5f, 0.16f);
        }

        /// <summary>Idle crackle around the orb when nothing is close enough to zap.</summary>
        public static void FrostOrbSpark(Vector2 pos)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            if (d == Vector2.zero) d = Vector2.up;
            LightningFx.Strike(pos + d * 0.12f, pos + d * Random.Range(0.45f, 0.7f), ArcGlow, ArcAura, 0.1f, 0.35f);
        }

        /// <summary>The orb shattering: an icy blast with lightning arcs, flying shards and crystals.</summary>
        public static void FrostOrbBurst(Vector2 pos, Vector2 ground, float radius)
        {
            SkillFx.Spawn("fx_frost", ground, new Color(1f, 1f, 1f, 0.9f), 1.3f, SkillFx.GroundOrder + 3)
                .Scale(radius / 1.5f * 0.3f, radius / 1.5f * 0.9f).Fade(FxFade.Late);
            Flash(pos, new Color(0.8f, 0.95f, 1f, 0.75f), radius * 1.6f, 0.24f);
            SkillFx.Spawn("fx_glow", pos, new Color(0.45f, 0.7f, 1f, 0.3f), 0.4f, SkillFx.TopOrder - 10).Additive().Scale(radius * 1.2f, radius * 2.4f).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_shock", pos, new Color(0.6f, 0.9f, 1f, 0.95f), 0.32f, SkillFx.TopOrder + 1).Scale(radius * 0.3f, radius * 1.05f).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_shock", pos, new Color(0.7f, 0.6f, 1f, 0.7f), 0.28f, SkillFx.TopOrder + 2).Additive().Scale(radius * 0.2f, radius * 0.8f).Fade(FxFade.Quick).Delay(0.04f);
            SkillFx.Spawn("fx_zap", pos, Color.white, 0.22f, SkillFx.TopOrder + 4).Rotate(Random.Range(0f, 45f)).Scale(0.8f, 1.6f).Fade(FxFade.Late);
            // Lightning arcs thrown out of the blast.
            for (int i = 0; i < 5; i++)
            {
                float ang = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / 5f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                LightningFx.Strike(pos + d * 0.15f, pos + d * radius * Random.Range(0.75f, 1.05f), ArcGlow, ArcAura, 0.24f, 0.7f);
            }
            // Ice shards flying out.
            for (int i = 0; i < 14; i++)
            {
                float ang = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / 14f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                SkillFx.Spawn("fx_shard", pos + d * 0.15f, Color.white, Random.Range(0.3f, 0.4f), SkillFx.TopOrder - 3)
                    .Move(d * (radius / 0.24f) * Random.Range(0.8f, 1.1f), 3.5f).FaceMotion().Scale(Random.Range(1f, 1.3f), 0.8f).Fade(FxFade.Late);
            }
            // Crystals bursting out of the ground around the blast.
            int crystals = Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 5, 10);
            for (int i = 0; i < crystals; i++)
            {
                float ang = (i + Random.Range(-0.25f, 0.25f)) * Mathf.PI * 2f / crystals;
                Vector2 q = ground + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f) * radius * Random.Range(0.55f, 0.85f);
                float size = Random.Range(0.6f, 0.95f);
                SkillFx.Spawn("fx_ice", q, Color.white, 0.6f, SkillFx.At(q.y, 3)).Scale(size, size).Pop().Flip(Random.value < 0.5f).Delay(0.04f + Random.Range(0f, 0.05f));
            }
            Sparks(pos, new Color(0.7f, 0.92f, 1f, 1f), 10, 7f, 0.25f);
            for (int i = 0; i < 10; i++)
            {
                Vector2 p = pos + Random.insideUnitCircle * radius;
                SkillFx.Spawn("fx_snow", p, new Color(1f, 1f, 1f, 0.95f), Random.Range(0.6f, 1f), SkillFx.TopOrder - 2)
                    .Move(new Vector2(Random.Range(-0.3f, 0.3f), -Random.Range(0.3f, 0.7f))).Spin(Random.Range(-200f, 200f))
                    .Fade(FxFade.InOut).Delay(Random.Range(0f, 0.15f));
            }
        }

        // ---------- 마을 귀환 주문서 (town return scroll) ----------

        /// <summary>A column of light and a turning sigil around the player reading the return scroll.</summary>
        public static void TownPortal(Vector2 feet)
        {
            var gold = new Color(1f, 0.88f, 0.5f, 0.95f);
            SkillFx.Spawn("fx_rune", feet, new Color(0.65f, 0.85f, 1f, 0.95f), 1.2f, SkillFx.GroundOrder + 6).Scale(0.3f, 1.2f).Spin(-160f).Fade(FxFade.InOut);
            SkillFx.Spawn("fx_rune", feet, new Color(gold.r, gold.g, gold.b, 0.6f), 1.1f, SkillFx.GroundOrder + 7).Rotate(30f).Scale(0.2f, 0.85f).Spin(220f).Fade(FxFade.InOut);
            SkillFx.Spawn("fx_glow", feet + Vector2.up * 0.6f, new Color(0.6f, 0.85f, 1f, 0.45f), 1.15f, SkillFx.TopOrder - 5).Additive().Scale(1f, 2.6f).Fade(FxFade.InOut);
            GlowLineFx.Spawn(feet, feet + Vector2.up * 3.2f, new Color(0.65f, 0.88f, 1f, 0.75f), 0.95f, 1.2f, SkillFx.TopOrder - 4);
            for (int i = 0; i < 22; i++)
            {
                Vector2 p = feet + new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.05f, 0.4f));
                SkillFx.Spawn("fx_streak", p, i % 3 == 0 ? gold : new Color(0.8f, 0.95f, 1f, 0.9f), Random.Range(0.45f, 0.7f), SkillFx.TopOrder)
                    .Move(Vector2.up * Random.Range(2.5f, 4.5f), 1f).FaceMotion().Scale(1.3f, 0.5f).Delay(Random.Range(0f, 0.85f));
            }
            Flash(feet + Vector2.up * 0.6f, new Color(1f, 1f, 1f, 0.8f), 2.8f, 0.3f, SkillFx.TopOrder + 2);
            SkillFx.Spawn("fx_glow", feet + Vector2.up * 0.6f, new Color(1f, 1f, 1f, 0.85f), 0.3f, SkillFx.TopOrder + 3).Additive().Scale(0.6f, 3f).Fade(FxFade.Quick).Delay(0.9f);
        }

        /// <summary>Fire explosion (fireball, meteor). <paramref name="pos"/> is the blast centre; the scorch mark goes on the ground under it.</summary>
        public static void Explosion(Vector2 pos, Vector2 ground, float radius, bool big)
        {
            float k = big ? 1.3f : 1f;
            Flash(pos, new Color(1f, 0.75f, 0.3f, 0.75f), radius * 2f * k, 0.25f);
            SkillFx.Spawn("fx_shock", pos, new Color(1f, 0.55f, 0.15f, 0.95f), 0.32f, SkillFx.TopOrder + 1).Scale(radius * 0.3f, radius * 1.1f * k).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_scorch", ground, Color.white, big ? 2f : 1.4f, SkillFx.GroundOrder + 4).Scale(radius * 0.7f, radius * 1.1f * k).Fade(FxFade.Late);
            int flames = big ? 18 : 12;
            for (int i = 0; i < flames; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                if (d == Vector2.zero) d = Vector2.up;
                SkillFx.Spawn("fx_flame", pos + d * 0.15f, Color.white, Random.Range(0.3f, 0.45f), SkillFx.TopOrder + 2)
                    .Move(d * radius * Random.Range(3f, 4.5f), 5f).Scale(Random.Range(1.4f, 2f) * k, 0.4f).Spin(Random.Range(-360f, 360f));
            }
            Sparks(pos, new Color(1f, 0.7f, 0.25f, 1f), big ? 12 : 8, 8f, 0.3f);
            for (int i = 0; i < 5; i++)
                SkillFx.Spawn("fx_dust", pos + Random.insideUnitCircle * radius * 0.5f, new Color(0.35f, 0.3f, 0.3f, 0.6f), Random.Range(0.6f, 0.9f), SkillFx.TopOrder)
                    .Move(new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.8f, 1.4f)), 1f).Scale(1.2f, 2.4f).Delay(0.08f);
        }

        // ---------- 낙뢰 (Thunder) ----------

        public static void Thunder(Vector2 ground)
        {
            Vector2 top = ground + new Vector2(Random.Range(-0.5f, 0.5f), 7f);
            LightningFx.Strike(top, ground + Vector2.up * 0.15f, ArcGlow, ArcAura, 0.32f, 1.5f);
            Flash(ground + Vector2.up * 0.4f, new Color(0.6f, 0.85f, 1f, 0.6f), 2.2f, 0.22f);
            SkillFx.Spawn("fx_zap", ground + Vector2.up * 0.35f, Color.white, 0.22f, SkillFx.TopOrder + 3).Rotate(Random.Range(0f, 45f)).Scale(0.8f, 1.5f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_shock", ground, new Color(0.55f, 0.75f, 1f, 0.9f), 0.3f, SkillFx.GroundOrder + 9).Scale(0.2f, 1.1f).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_scorch", ground, new Color(0.55f, 0.65f, 1f, 0.8f), 1f, SkillFx.GroundOrder + 4).Scale(0.5f, 0.8f).Fade(FxFade.Late);
            Sparks(ground + Vector2.up * 0.3f, new Color(0.65f, 0.92f, 1f, 1f), 10, 7f, 0.25f);
        }

        // ---------- 메테오 (Meteor, mage awakening) ----------

        public static void MeteorFall(Vector2 ground, float fallTime, float radius)
        {
            Vector2 start = ground + new Vector2(-2.6f, 6f);
            Vector2 vel = (ground - start) / fallTime;
            SkillFx.Spawn("fx_glow", start, new Color(1f, 0.5f, 0.15f, 0.8f), fallTime, SkillFx.TopOrder + 3).Additive().Move(vel).Scale(2f, 2.4f).Fade(FxFade.None);
            // [VFX] The drawn meteor already trails its fire down-right along this path, so it does not spin.
            var rock = SkillFx.Spawn(SkillFx.Pick("fxi_meteor", "fx_meteor"), start, Color.white, fallTime, SkillFx.TopOrder + 4).Move(vel).Scale(1.6f, 1.9f).Fade(FxFade.None);
            if (!SkillFx.HasImage("fxi_meteor")) rock.Spin(-420f);
            // Flame trail left along the path as the meteor passes.
            for (int k = 1; k <= 10; k++)
            {
                float t = k / 10f;
                SkillFx.Spawn("fx_flame", Vector2.Lerp(start, ground, t) + Random.insideUnitCircle * 0.1f, Color.white, 0.3f, SkillFx.TopOrder + 2)
                    .Scale(2.2f, 0.5f).Move(Random.insideUnitCircle * 0.6f).Delay(fallTime * t * 0.95f);
            }
            // Warning mark on the ground, shrinking towards the moment of impact.
            SkillFx.Spawn("fx_ring", ground, new Color(1f, 0.35f, 0.15f, 0.9f), fallTime, SkillFx.GroundOrder + 8).Scale(radius * 1.2f, radius * 0.45f).Fade(FxFade.None);
            SkillFx.Spawn("fx_glow", ground, new Color(1f, 0.35f, 0.1f, 0.35f), fallTime, SkillFx.GroundOrder + 7).Additive().Scale(radius * 0.8f, radius * 1.6f).Fade(FxFade.InOut);
        }

        public static void MeteorImpact(Vector2 ground, float radius)
        {
            Explosion(ground + Vector2.up * 0.35f, ground, radius, true);
            SkillFx.Crack(ground, radius, 1.6f);
            SkillFx.Spawn("fx_shock", ground + Vector2.up * 0.1f, new Color(1f, 0.85f, 0.5f, 0.8f), 0.4f, SkillFx.TopOrder).Scale(radius * 0.5f, radius * 1.6f).Fade(FxFade.Quick).Delay(0.05f);
            Fx.Burst("fx_chip", ground, 6, 4f, 0.7f);
        }

        // ---------- Awakening moment ----------

        /// <summary>Cut-in for an awakening skill: banner, darkened world, a burst of light around the caster.</summary>
        public static void Awakening(PlayerController owner, string skillName, Color color)
        {
            GameEvents.RaiseAwakening(skillName, color);
            DimFx.Show(1.6f, 0.5f);
            Vector2 c = owner.Center;
            Flash(c, new Color(color.r, color.g, color.b, 0.85f), 3.6f, 0.45f);
            SkillFx.Spawn("fx_shock", c, color, 0.5f, SkillFx.TopOrder + 5).Scale(0.3f, 3.2f).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_rune", owner.Position, new Color(color.r, color.g, color.b, 0.95f), 1.4f, SkillFx.TopOrder - 20).Scale(0.6f, 1.8f).Spin(-120f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_rune", owner.Position, new Color(1f, 1f, 1f, 0.5f), 1.2f, SkillFx.TopOrder - 19).Rotate(30f).Scale(0.4f, 1.3f).Spin(160f).Fade(FxFade.Late);
            for (int i = 0; i < 14; i++)
            {
                Vector2 p = owner.Position + new Vector2(Random.Range(-0.9f, 0.9f), Random.Range(-0.2f, 0.4f));
                SkillFx.Spawn("fx_streak", p, new Color(1f, 1f, 0.9f, 0.9f), Random.Range(0.4f, 0.7f), SkillFx.TopOrder)
                    .Move(Vector2.up * Random.Range(4f, 7f), 1.5f).FaceMotion().Scale(1.6f, 0.6f).Delay(Random.Range(0f, 0.25f));
            }
            Game.Camera?.Shake(0.1f, 0.3f);
        }

        /// <summary>Closing flourish after an awakening skill's last strike.</summary>
        public static void UltFinish(Vector2 center, Color color, float range)
        {
            Flash(center, new Color(color.r, color.g, color.b, 0.5f), range * 1.2f, 0.35f);
            SkillFx.Spawn("fx_shock", center, new Color(color.r, color.g, color.b, 0.85f), 0.5f, SkillFx.TopOrder).Scale(range * 0.3f, range * 1.05f).Fade(FxFade.Quick);
        }
    }
}
