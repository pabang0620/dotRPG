using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Materials shared by skill effects: the normal sprite blend and an additive "light" blend.</summary>
    public static class FxMaterials
    {
        static Material alpha, additive, softAlpha, softAdditive;
        static bool loaded;

        /// <summary>The default sprite material (normal alpha blending).</summary>
        public static Material Alpha { get { Load(); return alpha; } }

        /// <summary>Adds light to whatever is behind it. Falls back to <see cref="Alpha"/> if the shader is missing.</summary>
        public static Material Additive { get { Load(); return additive; } }

        /// <summary>For line renderers: a line with soft edges, normal blending.</summary>
        public static Material SoftAlpha { get { Load(); return softAlpha; } }

        /// <summary>For line renderers: a line with soft edges, additive.</summary>
        public static Material SoftAdditive { get { Load(); return softAdditive; } }

        static Material sharp;
        static bool sharpLoaded;

        /// <summary>
        /// Crisp scaling for the 32px-per-tile art (see SpriteSharp.shader). Falls back to the default
        /// sprite material, in which case the art's textures are switched back to point filtering.
        /// </summary>
        public static Material Sharp
        {
            get
            {
                if (sharpLoaded) return sharp;
                sharpLoaded = true;
                var shader = Resources.Load<Shader>("Shaders/SpriteSharp");
                if (shader == null) shader = Shader.Find("DotRPG/SpriteSharp");
                if (shader != null && shader.isSupported) sharp = new Material(shader) { name = "SpriteSharp" };
                else Debug.LogWarning("[dotRPG] Sharp sprite shader missing; high-resolution art uses point filtering.");
                return sharp;
            }
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var probe = new GameObject("FxMaterialProbe");
            alpha = probe.AddComponent<SpriteRenderer>().sharedMaterial;
            Object.Destroy(probe);
            if (alpha == null) alpha = new Material(Shader.Find("Sprites/Default"));

            var shader = Resources.Load<Shader>("Shaders/SpriteAdditive");
            if (shader == null) shader = Shader.Find("DotRPG/SpriteAdditive");
            additive = shader != null && shader.isSupported ? new Material(shader) { name = "FxAdditive" } : alpha;
            if (additive == alpha) Debug.LogWarning("[dotRPG] Additive FX shader missing; glows fall back to normal blending.");

            // 1x16 texture that fades out towards both edges of a line.
            var soft = new Texture2D(1, 16, TextureFormat.RGBA32, false)
            {
                name = "FxSoftLine",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int i = 0; i < 16; i++)
            {
                float edge = 1f - Mathf.Abs((i + 0.5f) / 8f - 1f);
                soft.SetPixel(0, i, new Color(1f, 1f, 1f, edge * edge));
            }
            soft.Apply(false, true);
            softAlpha = new Material(alpha) { name = "FxSoftLineAlpha", mainTexture = soft };
            softAdditive = new Material(additive) { name = "FxSoftLineAdd", mainTexture = soft };
        }

        /// <summary>A world-space line renderer child for effects.</summary>
        public static LineRenderer NewLine(Transform parent, Material material, int order)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = order;
            line.positionCount = 0;
            return line;
        }
    }

    /// <summary>How an effect fades over its life.</summary>
    public enum FxFade
    {
        /// <summary>Steady fade from start to end.</summary>
        Linear,
        /// <summary>Bright at first, gone quickly (flashes).</summary>
        Quick,
        /// <summary>Fully visible for most of its life, fades at the end.</summary>
        Late,
        /// <summary>Fades in, holds, fades out.</summary>
        InOut,
        /// <summary>Fully visible until it ends (falling swords and meteors).</summary>
        None,
    }

    /// <summary>
    /// One short-lived sprite effect. <see cref="Spawn"/> creates it; the chainable setters add
    /// scaling, spin, drift, a start delay, additive glow, and an "erupt from the ground" pop.
    /// </summary>
    public class SkillFx : MonoBehaviour
    {
        /// <summary>On the ground: over tiles and decorations, under every character.</summary>
        public const int GroundOrder = -19000;

        /// <summary>In the air: over everything in the world.</summary>
        public const int TopOrder = 21000;

        /// <summary>Sorted together with characters standing at <paramref name="y"/>.</summary>
        public static int At(float y, int boost = 40) => YSort.OrderFor(y) + boost;

        SpriteRenderer sr;
        float life, age, delay, spin, drag, swingDeg, swingHz;
        Vector3 scaleFrom = Vector3.one, scaleTo = Vector3.one;
        Vector2 velocity;
        bool pop, faceMotion;
        Color color;
        FxFade fade = FxFade.Linear;

        /// <summary>[VFX] The generated picture (Art/FxImg/&lt;img&gt;) when it exists, else the procedural sprite.</summary>
        public static string Pick(string img, string fallback) => Game.Art != null && Game.Art.Optional("FxImg/" + img) != null ? "FxImg/" + img : fallback;

        /// <summary>True when the generated picture is there (callers then skip the procedural flipbook it replaces).</summary>
        public static bool HasImage(string img) => Game.Art != null && Game.Art.Optional("FxImg/" + img) != null;

        public static SkillFx Spawn(string sprite, Vector2 position, Color color, float life, int order)
        {
            var go = new GameObject("SkillFx");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = position;
            var fx = go.AddComponent<SkillFx>();
            fx.sr = go.AddComponent<SpriteRenderer>();
            fx.sr.sprite = Game.Art.Get(sprite);
            fx.sr.sortingOrder = order;
            fx.color = color;
            fx.life = Mathf.Max(0.02f, life);
            fx.Apply(0f);
            return fx;
        }

        public SkillFx Additive() { sr.sharedMaterial = FxMaterials.Additive; return this; }

        public SkillFx Scale(float from, float to) => Scale(new Vector2(from, from), new Vector2(to, to));

        public SkillFx Scale(Vector2 from, Vector2 to)
        {
            scaleFrom = new Vector3(from.x, from.y, 1f);
            scaleTo = new Vector3(to.x, to.y, 1f);
            Apply(0f);
            return this;
        }

        /// <summary>Bursts up out of the ground (with a small overshoot), holds, then sinks back. Uses the target scale.</summary>
        public SkillFx Pop() { pop = true; Apply(0f); return this; }

        /// <summary>Degrees per second; negative = clockwise.</summary>
        public SkillFx Spin(float degreesPerSecond) { spin = degreesPerSecond; return this; }

        /// <summary>Rocks back and forth around the pivot (a ringing bell): <paramref name="degrees"/> either side, dying out.</summary>
        public SkillFx Swing(float degrees, float hz) { swingDeg = degrees; swingHz = hz; return this; }

        public SkillFx Rotate(float degrees) { transform.rotation = Quaternion.Euler(0f, 0f, degrees); return this; }

        public SkillFx Move(Vector2 speed, float dragPerSecond = 0f) { velocity = speed; drag = dragPerSecond; return this; }

        /// <summary>Keeps the sprite's +x axis pointing along its motion (sparks, shards).</summary>
        public SkillFx FaceMotion() { faceMotion = true; Face(); return this; }

        public SkillFx Fade(FxFade mode) { fade = mode; Apply(0f); return this; }

        public SkillFx Flip(bool x, bool y = false) { sr.flipX = x; sr.flipY = y; return this; }

        /// <summary>Stays hidden for <paramref name="seconds"/>, then plays.</summary>
        public SkillFx Delay(float seconds) { delay = Mathf.Max(0f, seconds); sr.enabled = delay <= 0f; return this; }

        void Face()
        {
            if (velocity.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age < delay) return;
            if (!sr.enabled) sr.enabled = true;
            float t = (age - delay) / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            if (velocity.sqrMagnitude > 0f)
            {
                transform.position += (Vector3)(velocity * dt);
                if (drag > 0f) velocity *= Mathf.Max(0f, 1f - drag * dt);
                if (faceMotion) Face();
            }
            if (spin != 0f) transform.Rotate(0f, 0f, spin * dt);
            if (swingDeg != 0f) transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(age * swingHz * Mathf.PI * 2f) * swingDeg * (1f - Mathf.Clamp01(age / life)));
            Apply(t);
        }

        void Apply(float t)
        {
            if (pop)
            {
                float k = t < 0.18f ? EaseOutBack(t / 0.18f) : t > 0.72f ? Mathf.Clamp01(1f - (t - 0.72f) / 0.28f) : 1f;
                transform.localScale = new Vector3(scaleTo.x * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(k)), scaleTo.y * k, 1f);
            }
            else
            {
                transform.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, 1f - (1f - t) * (1f - t));
            }

            float a;
            switch (fade)
            {
                case FxFade.Quick: a = (1f - t) * (1f - t); break;
                case FxFade.Late: a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f; break;
                case FxFade.InOut: a = t < 0.15f ? t / 0.15f : t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f; break;
                case FxFade.None: a = 1f; break;
                default: a = 1f - t; break;
            }
            var c = color;
            c.a *= Mathf.Clamp01(a);
            sr.color = c;
        }

        /// <summary>Removes the effect now (a projectile that hit something).</summary>
        public void Kill()
        {
            if (this != null) Destroy(gameObject);
        }

        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float m = x - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }
    }

    /// <summary>
    /// Jagged, flickering lightning between two points: a coloured aura, a bright glow, a white core
    /// and two small forks. The path is re-randomised every few frames so it crackles.
    /// </summary>
    public class LightningFx : MonoBehaviour
    {
        class Strand
        {
            public LineRenderer line;
            public float width;
            public Color color;
            public int path; // 0 = main bolt, 1/2 = forks
        }

        readonly List<Strand> strands = new List<Strand>();
        Vector3[] main, forkA, forkB;
        Vector2 from, to;
        float life, age, nextShape, amplitude, flicker = 1f;
        int segments;

        public static LightningFx Strike(Vector2 from, Vector2 to, Color glow, Color aura, float life = 0.3f, float thickness = 1f)
        {
            var go = new GameObject("Lightning");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<LightningFx>();
            fx.from = from;
            fx.to = to;
            fx.life = Mathf.Max(0.05f, life);
            float len = Vector2.Distance(from, to);
            fx.segments = Mathf.Clamp(Mathf.RoundToInt(len / 0.28f), 3, 18);
            fx.amplitude = Mathf.Clamp(len * 0.12f, 0.06f, 0.38f) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(thickness));
            fx.main = new Vector3[fx.segments + 1];
            fx.forkA = new Vector3[4];
            fx.forkB = new Vector3[4];
            fx.Add(FxMaterials.SoftAlpha, 0.62f * thickness, aura, 0, 0);
            fx.Add(FxMaterials.SoftAdditive, 0.3f * thickness, glow, 0, 1);
            fx.Add(FxMaterials.Alpha, 0.11f * thickness, Color.white, 0, 2);
            if (len > 0.8f)
                for (int f = 1; f <= 2; f++)
                {
                    fx.Add(FxMaterials.SoftAlpha, 0.3f * thickness, aura, f, 0);
                    fx.Add(FxMaterials.Alpha, 0.06f * thickness, Color.white, f, 2);
                }
            fx.Reshape();
            fx.Refresh(0f);
            return fx;
        }

        void Add(Material material, float width, Color color, int path, int orderOffset)
        {
            var line = FxMaterials.NewLine(transform, material, SkillFx.TopOrder + orderOffset);
            strands.Add(new Strand { line = line, width = width, color = color, path = path });
        }

        void Reshape()
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            Vector2 dir = len > 0.0001f ? d / len : Vector2.right;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float off = i == 0 || i == segments ? 0f : Random.Range(-amplitude, amplitude) * Mathf.Lerp(0.45f, 1f, Mathf.Sin(t * Mathf.PI));
                main[i] = from + d * t + perp * off;
            }
            Fork(forkA, dir, len, 1f);
            Fork(forkB, dir, len, -1f);
            flicker = Random.Range(0.7f, 1f);
        }

        void Fork(Vector3[] points, Vector2 dir, float len, float side)
        {
            Vector2 p = main[Random.Range(1, Mathf.Max(2, segments - 1))];
            float ang = side * Random.Range(25f, 55f) * Mathf.Deg2Rad;
            var fdir = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
            var fperp = new Vector2(-fdir.y, fdir.x);
            float step = Mathf.Min(len, 2.5f) * Random.Range(0.1f, 0.16f);
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = p;
                p += fdir * step + fperp * (Random.Range(-step, step) * 0.6f);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            if (age >= nextShape)
            {
                nextShape = age + 0.045f;
                Reshape();
            }
            Refresh(t);
        }

        void Refresh(float t)
        {
            float k = 1f - t;
            foreach (var s in strands)
            {
                var points = s.path == 0 ? main : s.path == 1 ? forkA : forkB;
                s.line.positionCount = points.Length;
                s.line.SetPositions(points);
                s.line.widthMultiplier = s.width * (0.55f + 0.45f * k);
                var c = s.color;
                c.a *= k * flicker;
                s.line.startColor = c;
                s.line.endColor = s.path == 0 ? c : new Color(c.r, c.g, c.b, 0f);
            }
        }
    }

    /// <summary>A glowing line that fades out: the molten fissure a ground slam leaves behind.</summary>
    public class GlowLineFx : MonoBehaviour
    {
        LineRenderer glow, core;
        Color color;
        float life, age, width;

        public static void Spawn(Vector2 from, Vector2 to, Color color, float width, float life, int order)
        {
            var go = new GameObject("GlowLine");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<GlowLineFx>();
            fx.color = color;
            fx.width = width;
            fx.life = Mathf.Max(0.05f, life);
            fx.glow = FxMaterials.NewLine(go.transform, FxMaterials.SoftAlpha, order);
            fx.core = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 1);
            foreach (var line in new[] { fx.glow, fx.core })
            {
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }
            fx.Refresh(0f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            Refresh(t);
        }

        void Refresh(float t)
        {
            float k = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
            glow.widthMultiplier = width;
            core.widthMultiplier = width * 0.45f;
            var g = color;
            g.a *= k;
            glow.startColor = glow.endColor = g;
            var c = new Color(1f, 0.95f, 0.75f, color.a * k);
            core.startColor = core.endColor = c;
        }
    }

    /// <summary>Ice crystals around a frozen monster's feet. They shatter when the freeze ends or the monster dies.</summary>
    public class IceEncase : MonoBehaviour
    {
        EnemyController enemy;
        readonly List<Transform> crystals = new List<Transform>();
        readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        readonly List<float> sizes = new List<float>();
        SpriteRenderer glow;
        float age;

        public static void Attach(EnemyController enemy)
        {
            if (enemy == null || enemy.IsDead || enemy.GetComponent<IceEncase>() != null) return;
            enemy.gameObject.AddComponent<IceEncase>().Build(enemy);
        }

        void Build(EnemyController target)
        {
            enemy = target;
            AddCrystal(new Vector2(-0.3f, 0.06f), 0.8f, 14f, false);
            AddCrystal(new Vector2(0.32f, 0.08f), 0.9f, -12f, true);
            AddCrystal(new Vector2(0.03f, -0.06f), 0.62f, 0f, false);
            var g = new GameObject("IceGlow");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            g.transform.localScale = Vector3.one * 0.9f;
            glow = g.AddComponent<SpriteRenderer>();
            glow.sprite = Game.Art.Get("fx_glow");
            glow.sharedMaterial = FxMaterials.Additive;
            LateUpdate();
        }

        void AddCrystal(Vector2 offset, float size, float tilt, bool flip)
        {
            var go = new GameObject("Ice");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            go.transform.localScale = new Vector3(size, 0f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get("fx_ice");
            sr.flipX = flip;
            crystals.Add(go.transform);
            renderers.Add(sr);
            sizes.Add(size);
        }

        void LateUpdate()
        {
            if (enemy == null) { Destroy(this); return; }
            if (enemy.IsDead || !enemy.IsFrozen) { Shatter(); return; }
            age += Time.deltaTime;
            float grow = SkillFx.EaseOutBack(Mathf.Clamp01(age / 0.16f));
            int order = YSort.OrderFor(enemy.Position.y) + 8;
            for (int i = 0; i < crystals.Count; i++)
            {
                crystals[i].localScale = new Vector3(sizes[i], sizes[i] * grow, 1f);
                renderers[i].sortingOrder = order + (crystals[i].localPosition.y < 0f ? 1 : 0);
            }
            glow.sortingOrder = order + 2;
            glow.color = new Color(0.55f, 0.85f, 1f, 0.3f + 0.1f * Mathf.Sin(age * 8f));
        }

        void Shatter()
        {
            Vector2 c = (Vector2)transform.position + new Vector2(0f, 0.3f);
            int order = SkillFx.At(transform.position.y, 30);
            for (int i = 0; i < 9; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                SkillFx.Spawn("fx_shard", c + dir * 0.15f, Color.white, Random.Range(0.3f, 0.45f), order)
                    .Move(dir * Random.Range(2.5f, 4.5f) + Vector2.up * 0.8f, 4f).FaceMotion().Scale(0.9f, 0.6f).Fade(FxFade.Late);
            }
            SkillFx.Spawn("fx_glow", c, new Color(0.7f, 0.92f, 1f, 0.6f), 0.2f, order + 1).Additive().Scale(0.4f, 1.2f).Fade(FxFade.Quick);
            foreach (var t in crystals) if (t != null) Destroy(t.gameObject);
            if (glow != null) Destroy(glow.gameObject);
            Destroy(this);
        }
    }

    /// <summary>Three little stars circling over a stunned monster's head.</summary>
    public class StunStars : MonoBehaviour
    {
        EnemyController enemy;
        readonly SpriteRenderer[] stars = new SpriteRenderer[3];
        float age;

        public static void Attach(EnemyController enemy)
        {
            if (enemy == null || enemy.IsDead || !enemy.IsStunned || enemy.GetComponent<StunStars>() != null) return;
            var s = enemy.gameObject.AddComponent<StunStars>();
            s.enemy = enemy;
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Star");
                go.transform.SetParent(enemy.transform, false);
                s.stars[i] = go.AddComponent<SpriteRenderer>();
                s.stars[i].sprite = Game.Art.Get("fx_star");
            }
            s.LateUpdate();
        }

        void LateUpdate()
        {
            if (enemy == null) { Destroy(this); return; }
            if (enemy.IsDead || !enemy.IsStunned)
            {
                foreach (var s in stars) if (s != null) Destroy(s.gameObject);
                Destroy(this);
                return;
            }
            age += Time.deltaTime;
            int order = YSort.OrderFor(enemy.Position.y) + 20;
            for (int i = 0; i < 3; i++)
            {
                float a = age * 5f + i * Mathf.PI * 2f / 3f;
                stars[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 0.32f, 1.08f + Mathf.Sin(a) * 0.1f, 0f);
                stars[i].sortingOrder = order + (Mathf.Sin(a) < 0f ? 1 : -3);
            }
        }
    }

    /// <summary>Glow and rising embers around the player while a buff lasts (전쟁 함성).</summary>
    public class BuffAura : MonoBehaviour
    {
        SpriteRenderer glow;
        Color color;
        float until, next;

        public static void Attach(Transform target, Color color, float seconds)
        {
            var aura = target.GetComponent<BuffAura>();
            if (aura == null)
            {
                aura = target.gameObject.AddComponent<BuffAura>();
                var go = new GameObject("BuffGlow");
                go.transform.SetParent(target, false);
                go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                aura.glow = go.AddComponent<SpriteRenderer>();
                aura.glow.sprite = Game.Art.Get("fx_glow");
                aura.glow.sharedMaterial = FxMaterials.Additive;
            }
            aura.color = color;
            aura.until = Time.time + seconds;
        }

        void LateUpdate()
        {
            if (Time.time >= until)
            {
                if (glow != null) Destroy(glow.gameObject);
                Destroy(this);
                return;
            }
            float left = until - Time.time;
            float pulse = 0.55f + 0.2f * Mathf.Sin(Time.time * 7f);
            glow.color = new Color(color.r, color.g, color.b, pulse * Mathf.Clamp01(left / 0.5f) * 0.6f);
            glow.transform.localScale = Vector3.one * (1.1f + 0.08f * Mathf.Sin(Time.time * 7f));
            glow.sortingOrder = YSort.OrderFor(transform.position.y) - 3;
            if (Time.time >= next)
            {
                next = Time.time + 0.07f;
                Vector2 p = (Vector2)transform.position + new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(0.1f, 0.6f));
                SkillFx.Spawn("fx_spark", p, new Color(1f, Mathf.Lerp(0.5f, 0.85f, Random.value), 0.3f, 1f), Random.Range(0.35f, 0.55f), SkillFx.At(transform.position.y, 30))
                    .Move(new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(1.2f, 2f)), 1f).Scale(1f, 0.4f);
            }
        }
    }

    /// <summary>Darkens the world (not the effects on top of it) for an awakening skill's big moment.</summary>
    public class DimFx : MonoBehaviour
    {
        static DimFx current;
        SpriteRenderer sr;
        float life, age, strength;

        public static void Show(float seconds, float alpha)
        {
            if (current == null)
            {
                var go = new GameObject("AwakeningDim");
                current = go.AddComponent<DimFx>();
                current.sr = go.AddComponent<SpriteRenderer>();
                current.sr.sprite = Game.Art.Get("ui_white");
                current.sr.sortingOrder = SkillFx.TopOrder - 1000; // over every character, under the skill's light
            }
            current.life = seconds;
            current.age = 0f;
            current.strength = alpha;
            current.LateUpdate();
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
            float t = age / life;
            var cam = Game.Camera != null ? Game.Camera.Camera : Camera.main;
            if (t >= 1f || cam == null) { Destroy(gameObject); return; }
            float a = t < 0.15f ? t / 0.15f : t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
            sr.color = new Color(0.02f, 0.02f, 0.08f, strength * a);
            var p = cam.transform.position;
            transform.position = new Vector3(p.x, p.y, 0f);
            float h = cam.orthographicSize * 2f + 2f, w = h * cam.aspect + 2f;
            transform.localScale = new Vector3(w / 0.25f, h / 0.25f, 1f); // ui_white is 0.25 units across
        }
    }

    /// <summary>A projectile's look (core sprite + glow) that gameplay code moves every frame.</summary>
    public sealed class MovingFx
    {
        readonly List<SkillFx> parts = new List<SkillFx>();

        public MovingFx Add(SkillFx fx) { parts.Add(fx); return this; }

        public void MoveTo(Vector2 p)
        {
            foreach (var f in parts) if (f != null) f.transform.position = p;
        }

        public void Kill()
        {
            foreach (var f in parts) if (f != null) f.Kill();
            parts.Clear();
        }
    }

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

        /// <summary>The hammer blow itself, where the shock wave starts.</summary>
        public static void SlamImpact(Vector2 pos, float radius)
        {
            Flash(pos + Vector2.up * 0.2f, new Color(1f, 0.7f, 0.3f, 0.45f), radius * 2.2f, 0.25f, SkillFx.At(pos.y, 44));
            SkillFx.Spawn("fx_shock", pos + Vector2.up * 0.1f, new Color(0.95f, 0.62f, 0.25f, 0.95f), 0.35f, SkillFx.At(pos.y, 45))
                .Scale(radius * 0.3f, radius * 1.8f).Fade(FxFade.Quick);
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI * 2f / 8f + Random.Range(-0.2f, 0.2f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                SkillFx.Spawn("fx_dust", pos + dir * 0.2f, new Color(0.95f, 0.9f, 0.8f, 0.7f), 0.5f, SkillFx.At(pos.y, 46))
                    .Move(dir * 2.6f, 4f).Scale(0.9f, 1.8f);
            }
            Fx.Burst("fx_chip", pos, 6, 3.5f, 0.7f);
        }

        /// <summary>One step of the travelling shock wave.</summary>
        public static void SlamStep(Vector2 from, Vector2 p, float radius, bool last)
        {
            float s = radius / 0.85f;
            // Cracked ground that lingers, and a glowing fissure from the previous step.
            SkillFx.Spawn("fx_crack", p, Color.white, 1.3f, SkillFx.GroundOrder + 2)
                .Scale(s * 0.7f, s * (last ? 1.25f : 1f)).Flip(Random.value < 0.5f, Random.value < 0.5f).Fade(FxFade.Late);
            if ((p - from).sqrMagnitude > 0.01f)
                GlowLineFx.Spawn(from, p, new Color(1f, 0.45f, 0.1f, 0.9f), 0.42f * s, 0.9f, SkillFx.GroundOrder + 3);
            // Shock ring, warm light, rock spikes bursting up, flying chips and dust.
            SkillFx.Spawn("fx_shock", p + Vector2.up * 0.1f, new Color(0.95f, 0.6f, 0.25f, 0.9f), 0.3f, SkillFx.At(p.y, 45))
                .Scale(radius * 0.3f, radius * 1.15f).Fade(FxFade.Quick);
            Flash(p + Vector2.up * 0.2f, new Color(EarthOrange.r, EarthOrange.g, EarthOrange.b, 0.4f), radius * 1.6f, 0.22f, SkillFx.At(p.y, 44));
            int spikes = last ? 4 : 2;
            for (int k = 0; k < spikes; k++)
            {
                Vector2 q = p + Random.insideUnitCircle * radius * 0.55f;
                float size = Random.Range(1f, 1.4f) * (last ? 1.25f : 1f);
                SkillFx.Spawn("fx_spike", q, Color.white, Random.Range(0.55f, 0.7f), SkillFx.At(q.y, 2))
                    .Scale(size, size).Pop().Flip(Random.value < 0.5f).Delay(k * 0.03f);
            }
            Fx.Burst("fx_chip", p, last ? 7 : 4, 3f, 0.6f);
            for (int k = 0; k < 2; k++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                SkillFx.Spawn("fx_dust", p + dir * 0.2f, new Color(0.95f, 0.9f, 0.8f, 0.6f), 0.45f, SkillFx.At(p.y, 46))
                    .Move(dir * 1.8f, 4f).Scale(0.8f, 1.5f);
            }
            Game.Camera?.Shake(last ? 0.1f : 0.05f, 0.1f);
        }

        /// <summary>A monster caught by the shock wave.</summary>
        public static void EarthHit(Vector2 pos)
        {
            Flash(pos, new Color(1f, 0.7f, 0.3f, 0.7f), 1.2f, 0.16f);
            Sparks(pos, new Color(1f, 0.85f, 0.45f, 1f), 6, 6f, 0.2f);
            Fx.Burst("fx_chip", pos, 3, 2.5f, 0.5f);
        }

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

        /// <summary>The flying crescent blade (moved by the skill code).</summary>
        public static MovingFx WaveBlade(Vector2 pos, Vector2 dir, float radius)
        {
            float s = radius / 0.7f;
            var fx = new MovingFx();
            fx.Add(SkillFx.Spawn("fx_glow", pos, new Color(1f, 0.75f, 0.3f, 0.55f), 10f, SkillFx.TopOrder + 1).Additive().Scale(s * 1.6f, s * 1.6f).Fade(FxFade.None));
            fx.Add(SkillFx.Spawn("fx_crescent", pos, Color.white, 10f, SkillFx.TopOrder + 2).Rotate(Angle(dir)).Scale(s * 0.6f, s).Fade(FxFade.None));
            Flash(pos, new Color(1f, 0.85f, 0.45f, 0.5f), 1.2f, 0.15f);
            return fx;
        }

        public static void WaveTrail(Vector2 pos, Vector2 dir, float radius)
        {
            float s = radius / 0.7f;
            SkillFx.Spawn("fx_crescent", pos - dir * 0.15f, new Color(1f, 0.78f, 0.35f, 0.55f), 0.16f, SkillFx.TopOrder)
                .Additive().Rotate(Angle(dir)).Scale(s * 0.95f, s * 0.8f);
            if (Random.value < 0.5f)
            {
                var side = new Vector2(-dir.y, dir.x) * Random.Range(-0.5f, 0.5f) * s;
                SkillFx.Spawn("fx_streak", pos + side, new Color(1f, 0.92f, 0.6f, 0.9f), 0.2f, SkillFx.TopOrder)
                    .Move(-dir * 2f + side, 3f).FaceMotion().Scale(1.2f, 0.4f);
            }
        }

        public static void WaveEnd(MovingFx blade, Vector2 pos, Vector2 dir, float radius)
        {
            blade.Kill();
            float s = radius / 0.7f;
            SkillFx.Spawn("fx_crescent", pos, Color.white, 0.2f, SkillFx.TopOrder + 2).Rotate(Angle(dir)).Scale(new Vector2(s * 0.6f, s), new Vector2(s * 0.2f, s * 1.4f)).Fade(FxFade.Quick);
            Flash(pos, new Color(1f, 0.8f, 0.4f, 0.5f), 1.4f, 0.18f);
            Sparks(pos, new Color(1f, 0.85f, 0.45f, 1f), 7, 6f, 0.2f);
        }

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
            SkillFx.Spawn("fx_crack", ground, Color.white, 1.2f, SkillFx.GroundOrder + 2).Scale(radius, radius * 1.1f).Flip(Random.value < 0.5f).Fade(FxFade.Late);
            SkillFx.Spawn("fx_shock", ground + Vector2.up * 0.1f, new Color(1f, 0.78f, 0.3f, 0.95f), 0.3f, SkillFx.TopOrder).Scale(radius * 0.3f, radius * 1.2f).Fade(FxFade.Quick);
            Flash(ground + Vector2.up * 0.3f, new Color(1f, 0.85f, 0.45f, 0.6f), radius * 1.8f, 0.2f);
            Sparks(ground + Vector2.up * 0.2f, new Color(1f, 0.9f, 0.5f, 1f), 8, 7f, 0.22f);
            Fx.Burst("fx_chip", ground, 4, 3f, 0.6f);
        }

        // ---------- 빙뢰구 (Frost Orb: ice + lightning) ----------

        public static readonly Color OrbCyan = new Color(0.55f, 0.9f, 1f, 1f);

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
            SkillFx.Spawn("fx_crack", ground, Color.white, 1.6f, SkillFx.GroundOrder + 2).Scale(radius * 0.8f, radius * 1.2f).Flip(Random.value < 0.5f).Fade(FxFade.Late);
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
