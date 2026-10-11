using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The look of the career skills, built from the same pixel pieces as the base skills (SkillFx) and tinted per career.
    /// Gameplay code says what happened and where; colours, counts and timings live here.
    /// </summary>
    public static class CareerFx
    {
        public static readonly Color Steel = new Color(0.55f, 0.82f, 1f, 0.95f);
        public static readonly Color Teal = new Color(0.38f, 0.95f, 0.88f, 0.95f);
        public static readonly Color Violet = new Color(0.74f, 0.52f, 1f, 0.95f);
        public static readonly Color Holy = new Color(1f, 0.9f, 0.55f, 0.95f);
        public static readonly Color HolyWarm = new Color(1f, 0.72f, 0.3f, 0.85f);
        public static readonly Color Life = new Color(0.6f, 1f, 0.6f, 0.9f);

        public static Color Main(Career c) => c == Career.Fighter ? Steel : c == Career.Guardian ? Teal : c == Career.Arcanist ? Violet : Holy;
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        public static float Angle(Vector2 dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // ---------- Frame clips (Docs/PLAN_SKILL_VFX.md) ----------

        /// <summary>Plays a frame clip; facing left is a mirror image of the right-facing art (VfxPlayer).</summary>
        public static VfxPlayer Clip(string clip, Vector2 at, Vector2 dir, float scale = 1f, float fps = 24f, VfxLayer layer = VfxLayer.Top,
            bool turn = true, Color? tint = null, bool additive = false, float life = 0f, bool loop = false) =>
            VfxPlayer.Play(clip, at, dir, tint ?? Color.white, scale, fps, additive, life, loop, layer, turn);

        /// <summary>
        /// [VFX] Generated art when it exists (Art/VfxImg, Docs/PLAN_SKILL_VFX.md), else the old code-drawn clip.
        /// <paramref name="size"/> is the generated frame's width in world units; <paramref name="oldScale"/> the old clip's scale.
        /// The generated strip uses its own fps unless one is given.
        /// </summary>
        public static VfxPlayer Gen(string generated, string old, Vector2 at, Vector2 dir, float size, float oldScale, float oldFps = 24f,
            VfxLayer layer = VfxLayer.Top, bool turn = true, Color? tint = null, float life = 0f, bool loop = false, float fps = 0f)
        {
            var strip = VfxLibrary.Strip(generated);
            if (strip != null) return VfxPlayer.Play(generated, at, dir, tint ?? Color.white, size, fps > 0f ? fps : strip.fps, strip.additive, life, loop, layer, turn);
            return old == null ? null : Clip(old, at, dir, oldScale, oldFps, layer, turn, tint, false, life, loop);
        }

        /// <summary>Turns a right-facing offset angle into the aim's mirror-correct direction (tilts mirror on the left).</summary>
        public static Vector2 Tilt(Vector2 dir, float degrees) => Rotate(dir, dir.x < -0.01f ? -degrees : degrees);

        /// <summary>The career's own hit mark on a monster, bigger for heavier blows.</summary>
        public static void Hit(Vector2 at, Vector2 dir, Career career, int weight)
        {
            float k = weight == 2 ? 1.5f : weight == 1 ? 1.15f : 0.85f;
            string clip = career == Career.Fighter ? "f_x" : career == Career.Guardian ? "g_clang" : career == Career.Arcanist ? "m_hit" : "b_cross";
            Clip(clip, at, career == Career.Fighter ? dir : Vector2.zero, k, 30f, VfxLayer.Top, career == Career.Fighter);
            if (weight == 2) SkillVisuals.Flash(at, new Color(1f, 1f, 1f, 0.55f), 1.4f, 0.1f);
        }

        /// <summary>A tinted copy of the body sprite left behind (dash and leap afterimages).</summary>
        public static void Ghost(PlayerController owner, Color tint, float life = 0.28f)
        {
            var body = owner != null ? owner.GetComponent<CharacterAnimator>()?.Body : null;
            if (body == null || body.sprite == null) return;
            var go = new GameObject("Afterimage");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = body.transform.position;
            go.transform.localScale = body.transform.lossyScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = body.sprite;
            sr.flipX = body.flipX;
            sr.sharedMaterial = FxMaterials.Additive;
            sr.sortingOrder = body.sortingOrder - 1;
            go.AddComponent<GhostFade>().Begin(sr, tint, life);
        }

        // ---------- Blades ----------

        /// <summary>A sword arc in front of <paramref name="center"/>: white edge over a coloured afterglow and wind streaks.</summary>
        public static void Slash(Vector2 center, Vector2 dir, float reach, Color color, float tilt = 0f, bool flip = false, float life = 0.17f)
        {
            float s = reach / 1.2f; // fx_arc spans about 1.2 units of reach at scale 1
            float ang = Angle(dir) + tilt;
            Vector2 at = center + Rotate(dir, tilt) * reach * 0.45f;
            int order = SkillFx.TopOrder + 2;
            SkillFx.Spawn("fx_arc", at, Color.white, life, order + 1).Rotate(ang).Flip(false, flip)
                .Scale(new Vector2(s * 0.45f, s * 0.8f), new Vector2(s * 0.9f, s * 1.15f)).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_arc", at - Rotate(dir, tilt) * 0.12f, A(color, 0.85f), life * 1.4f, order).Additive().Rotate(ang).Flip(false, flip)
                .Scale(new Vector2(s * 0.6f, s), new Vector2(s * 1.1f, s * 1.35f)).Fade(FxFade.Late);
            Vector2 side = new Vector2(-dir.y, dir.x) * (flip ? -1f : 1f);
            for (int i = 0; i < 6; i++)
            {
                float u = (i + 0.5f) / 6f - 0.5f;
                Vector2 p = at + Rotate(side, tilt) * u * reach * 1.1f;
                SkillFx.Spawn("fx_streak", p, A(Color.Lerp(color, Color.white, 0.5f), 0.95f), Random.Range(0.12f, 0.2f), order + 2)
                    .Move(Rotate(dir, tilt) * Random.Range(4f, 7f) + Rotate(side, tilt) * u * 4f, 6f).FaceMotion().Scale(1.2f, 0.4f);
            }
        }

        /// <summary>A long thin cut line (일섬, light spear) that flares then fades.</summary>
        public static void Line(Vector2 from, Vector2 to, Color color, float width, float life)
        {
            GlowLineFx.Spawn(from, to, A(color, 0.8f), width * 2.2f, life, SkillFx.TopOrder + 1);
            GlowLineFx.Spawn(from, to, new Color(1f, 1f, 1f, 0.95f), width * 0.7f, life * 0.7f, SkillFx.TopOrder + 2);
        }

        /// <summary>Speed lines and dust behind a dashing body.</summary>
        public static void DashTrail(Vector2 at, Vector2 feet, Vector2 dir, Color color)
        {
            Vector2 side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < 2; i++)
                SkillFx.Spawn("fx_streak", at + side * Random.Range(-0.35f, 0.35f), A(color, 0.9f), 0.2f, SkillFx.TopOrder)
                    .Move(-dir * Random.Range(5f, 8f), 5f).FaceMotion().Scale(1.6f, 0.45f);
            SkillFx.Spawn("fx_glow", at, A(color, 0.35f), 0.16f, SkillFx.TopOrder - 2).Additive().Scale(0.9f, 0.5f);
            SkillFx.Spawn("fx_dust", feet, new Color(1f, 0.98f, 0.92f, 0.6f), 0.35f, SkillFx.GroundOrder + 10)
                .Move(-dir * 1.5f + side * Random.Range(-1f, 1f), 4f).Scale(0.6f, 1.2f);
        }

        /// <summary>A big sword dropping onto <paramref name="ground"/> (steel blue beam, warning ring).</summary>
        public static void SwordDrop(Vector2 ground, float fallTime, Color color, float size = 1f, bool dark = false)
        {
            float height = 4.5f * size;
            SkillFx.Spawn(SkillFx.Pick(dark ? "fxi_bigsword_dark" : "fxi_bigsword", "fx_bigsword"), ground + Vector2.up * height, Color.white, fallTime, SkillFx.TopOrder + 4)
                .Move(Vector2.down * (height / fallTime)).Scale(size, size).Fade(FxFade.None);
            GlowLineFx.Spawn(ground + Vector2.up * height, ground, A(color, 0.6f), 0.45f * size, fallTime + 0.1f, SkillFx.TopOrder + 3);
            SkillFx.Spawn("fx_ring", ground, A(color, 0.9f), fallTime, SkillFx.GroundOrder + 8).Scale(1.4f * size, 0.45f * size).Fade(FxFade.None);
        }

        public static void SwordImpact(Vector2 ground, float radius, Color color, bool big)
        {
            float k = big ? 1.4f : 1f;
            SkillFx.Spawn(SkillFx.Pick("fxi_bigsword", "fx_bigsword"), ground, Color.white, big ? 0.9f : 0.5f, SkillFx.At(ground.y, 6)).Scale(k, k).Fade(FxFade.Late);
            SkillFx.Crack(ground, radius, big ? 1.6f : 1f);
            Shock(ground + Vector2.up * 0.1f, radius, color, 0.3f);
            SkillVisuals.Flash(ground + Vector2.up * 0.3f, A(color, 0.7f), radius * 1.8f, 0.2f);
            SkillVisuals.Sparks(ground + Vector2.up * 0.2f, Color.Lerp(color, Color.white, 0.4f), big ? 14 : 7, 7.5f, 0.24f);
            Fx.Burst("fx_chip", ground, big ? 8 : 4, 3.5f, 0.7f);
        }

        // ---------- Areas ----------

        public static void Shock(Vector2 at, float radius, Color color, float life = 0.3f, float delay = 0f)
        {
            SkillFx.Spawn("fx_shock", at, A(color, 0.95f), life, SkillFx.TopOrder - 4).Scale(radius * 0.25f, radius * 1.05f).Fade(FxFade.Quick).Delay(delay);
            SkillFx.Spawn("fx_shock", at, A(color, 0.45f), life * 1.3f, SkillFx.TopOrder - 5).Additive().Scale(radius * 0.3f, radius * 1.15f).Delay(delay);
        }

        /// <summary>Ground slam: dust ring, rock chips, cracks and a shock ring.</summary>
        public static void Slam(Vector2 feet, float radius, Color color, bool big)
        {
            SkillFx.Crack(feet, radius * (big ? 1.1f : 0.9f), big ? 1.8f : 1.2f);
            Shock(feet + Vector2.up * 0.1f, radius, color, 0.35f);
            if (big) Shock(feet + Vector2.up * 0.1f, radius * 0.6f, Color.white, 0.25f, 0.06f);
            SkillVisuals.Flash(feet + Vector2.up * 0.25f, A(color, 0.6f), radius * 1.6f, 0.22f);
            int n = big ? 14 : 9;
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n + Random.Range(-0.2f, 0.2f);
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                SkillFx.Spawn("fx_dust", feet + d * 0.3f, new Color(0.95f, 0.92f, 0.85f, 0.75f), 0.5f, SkillFx.GroundOrder + 10).Move(d * radius * 1.4f, 4f).Scale(0.9f, 1.8f);
            }
            if (big)
                for (int k = 0; k < 6; k++)
                {
                    Vector2 q = feet + Random.insideUnitCircle * radius * 0.7f;
                    SkillFx.Spawn("fx_spike", q, Color.white, Random.Range(0.5f, 0.7f), SkillFx.At(q.y, 2)).Scale(1.1f, 1.4f).Pop().Flip(Random.value < 0.5f).Delay(k * 0.03f);
                }
            Fx.Burst("fx_chip", feet, big ? 9 : 5, 3.5f, 0.7f);
        }

        /// <summary>A column of light from the sky.</summary>
        public static void Pillar(Vector2 ground, float width, Color color, float life)
        {
            GlowLineFx.Spawn(ground + Vector2.up * 6f, ground, A(color, 0.75f), width, life, SkillFx.TopOrder + 3);
            GlowLineFx.Spawn(ground + Vector2.up * 6f, ground, new Color(1f, 1f, 1f, 0.9f), width * 0.35f, life * 0.8f, SkillFx.TopOrder + 4);
            SkillFx.Spawn("fx_glow", ground + Vector2.up * 0.3f, A(color, 0.6f), life, SkillFx.TopOrder + 2).Additive().Scale(width * 1.6f, width * 2.6f).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_ring", ground, A(color, 0.9f), life, SkillFx.GroundOrder + 8).Scale(width * 0.4f, width * 1.4f).Fade(FxFade.Quick);
        }

        /// <summary>[VFX] The drawn small shield (same family as the falling aegis) when it exists: shown in its own colours.</summary>
        public static bool ShieldArt => SkillFx.HasImage("fxi_shield_small");
        public static string ShieldSprite => SkillFx.Pick("fxi_shield_small", "fx_aegis");

        /// <summary>A bright mark that a healing / shield / blessing reached an ally.</summary>
        public static void Bless(Vector2 at, Color color, bool shield)
        {
            if (shield && ShieldArt) SkillFx.Spawn(ShieldSprite, at + Vector2.up * 0.25f, Color.white, 0.5f, SkillFx.TopOrder + 3).Scale(0.7f, 1.25f).Pop().Fade(FxFade.Late);
            else SkillFx.Spawn(shield ? "fx_aegis" : "fx_holy", at + Vector2.up * 0.2f, A(color, 0.95f), 0.45f, SkillFx.TopOrder + 3).Scale(0.4f, shield ? 0.95f : 0.85f).Pop().Fade(FxFade.Late);
            SkillFx.Spawn("fx_glow", at, A(color, 0.5f), 0.35f, SkillFx.TopOrder + 1).Additive().Scale(0.8f, 1.6f).Fade(FxFade.Quick);
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = at + new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(-0.4f, 0.1f));
                SkillFx.Spawn("fx_spark", p, A(Color.Lerp(color, Color.white, 0.4f), 1f), Random.Range(0.35f, 0.55f), SkillFx.TopOrder + 2)
                    .Move(new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(1.4f, 2.4f)), 1f).Scale(1f, 0.4f).Delay(i * 0.03f);
            }
        }

        // ---------- Projectiles ----------

        public static MovingFx Orb(Vector2 pos, Color color, float size, string core = "fx_spark")
        {
            var fx = new MovingFx();
            fx.Add(SkillFx.Spawn("fx_glow", pos, A(color, 0.7f), 10f, SkillFx.TopOrder + 1).Additive().Scale(size * 1.6f, size * 1.6f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn(core, pos, Color.white, 10f, SkillFx.TopOrder + 2).Scale(size, size).Spin(540f).Fade(FxFade.None));
            return fx;
        }

        public static void OrbTrail(Vector2 pos, Vector2 dir, Color color, string bit = "fx_spark")
        {
            SkillFx.Spawn("fx_glow", pos - dir * 0.15f, A(color, 0.35f), 0.18f, SkillFx.TopOrder).Additive().Scale(0.8f, 0.4f);
            if (Random.value < 0.6f)
                SkillFx.Spawn(bit, pos + Random.insideUnitCircle * 0.12f, A(Color.Lerp(color, Color.white, 0.3f), 1f), Random.Range(0.2f, 0.35f), SkillFx.TopOrder)
                    .Move(-dir * 1.6f + Random.insideUnitCircle * 1.2f, 3f).Scale(0.9f, 0.4f);
        }

        public static MovingFx Feather(Vector2 pos, Color color)
        {
            var fx = new MovingFx();
            fx.Add(SkillFx.Spawn("fx_glow", pos, A(color, 0.6f), 10f, SkillFx.TopOrder + 1).Additive().Scale(0.9f, 0.9f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn("fx_feather", pos, A(Color.Lerp(color, Color.white, 0.5f), 1f), 10f, SkillFx.TopOrder + 2).Spin(360f).Fade(FxFade.None));
            return fx;
        }

        public static MovingFx Shield(Vector2 pos, Color color)
        {
            var fx = new MovingFx();
            fx.Add(SkillFx.Spawn("fx_glow", pos, A(color, 0.7f), 10f, SkillFx.TopOrder + 1).Additive().Scale(1.4f, 1.4f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn(SkillFx.Pick("fxi_aegis", "fx_aegis"), pos, Color.white, 10f, SkillFx.TopOrder + 2).Scale(0.8f, 0.8f).Spin(900f).Fade(FxFade.None));
            return fx;
        }

        public static MovingFx Spear(Vector2 pos, Vector2 dir, Color color, bool ice)
        {
            var fx = new MovingFx();
            fx.Add(SkillFx.Spawn("fx_glow", pos, A(color, 0.6f), 10f, SkillFx.TopOrder + 1).Additive().Scale(1.3f, 1.3f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn(ice ? "fx_shard" : "fx_streak", pos, Color.white, 10f, SkillFx.TopOrder + 2).Rotate(Angle(dir)).Scale(ice ? 2.2f : 2.6f, ice ? 1.4f : 1f).Fade(FxFade.None));
            return fx;
        }

        // ---------- Impacts ----------

        /// <summary>Spark burst on a monster, sized by the weight of the blow (0 light, 1 medium, 2 heavy).</summary>
        public static void Hit(Vector2 at, Vector2 dir, Color color, int weight)
        {
            float k = weight == 2 ? 1.5f : weight == 1 ? 1.15f : 0.85f;
            float rot = Angle(dir) + Random.Range(-35f, 35f);
            SkillFx.Spawn("fx_cut", at, Color.white, 0.14f, SkillFx.TopOrder + 4).Rotate(rot).Scale(new Vector2(0.4f * k, k), new Vector2(1.3f * k, 0.55f * k)).Fade(FxFade.Quick);
            SkillFx.Spawn("fx_cut", at, A(color, 0.9f), 0.18f, SkillFx.TopOrder + 3).Additive().Rotate(rot).Scale(new Vector2(0.6f * k, 1.6f * k), new Vector2(1.6f * k, k)).Fade(FxFade.Quick);
            SkillVisuals.Flash(at, A(Color.Lerp(color, Color.white, 0.5f), 0.7f), 1.1f * k, 0.12f);
            SkillVisuals.Sparks(at, Color.Lerp(color, Color.white, 0.35f), weight == 2 ? 12 : weight == 1 ? 8 : 5, 7f * k, 0.2f);
            if (weight == 2) SkillFx.Spawn("fx_ring", at, A(color, 0.9f), 0.22f, SkillFx.TopOrder + 2).Scale(0.2f, 1.1f).Fade(FxFade.Quick);
        }

        public static void Burst(Vector2 at, float radius, Color color, int bits, string bit = "fx_spark")
        {
            SkillVisuals.Flash(at, A(color, 0.75f), radius * 1.8f, 0.22f);
            Shock(at, radius, color, 0.3f);
            for (int i = 0; i < bits; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                if (d == Vector2.zero) d = Vector2.up;
                SkillFx.Spawn(bit, at + d * 0.15f, A(Color.Lerp(color, Color.white, 0.3f), 1f), Random.Range(0.25f, 0.4f), SkillFx.TopOrder + 1)
                    .Move(d * radius * Random.Range(3f, 4.5f), 5f).FaceMotion().Scale(1.2f, 0.45f);
            }
        }

        static Vector2 Rotate(Vector2 d, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
        }
    }

    /// <summary>Fades an afterimage out.</summary>
    public sealed class GhostFade : MonoBehaviour
    {
        SpriteRenderer sr; Color tint; float life, age;
        public void Begin(SpriteRenderer r, Color c, float seconds) { sr = r; tint = c; life = seconds; sr.color = c; }
        void Update()
        {
            age += Time.deltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            float a = 1f - age / life;
            sr.color = new Color(tint.r, tint.g, tint.b, tint.a * a * a);
        }
    }

    /// <summary>Career skill icons (Resources/Art/Careers/Icons) and the after-cast recovery of each skill.</summary>
    public static class CareerMoves
    {
        public static Sprite Icon(string key) =>
            key != null && key.StartsWith("career_") ? Resources.Load<Sprite>("Art/Careers/Icons/" + key) : null;

        /// <summary>Seconds the body stays in the skill after the cast moment (short so skills chain into attacks).</summary>
        public static float Recovery(CareerSkill s)
        {
            switch (s.effect)
            {
                case "cross": case "light": case "heal": case "guard": case "blink": case "frenzy": case "nebula": case "sanctuary": return 0.1f;
                case "break": case "fire": case "ice": case "storm": case "orbit": case "taunt": return 0.15f;
                case "flurry": return 0.7f;
                case "iaido": case "bash": return 0.22f;
                case "shieldthrow": return 0.14f;
                case "rush": return 0.25f;
                case "execute": return 0.32f;
                case "swordrain": case "aegis": case "cataclysm": case "dawn": return 0.5f;
                default: return 0.15f;
            }
        }
    }
}
