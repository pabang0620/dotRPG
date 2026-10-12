using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] A glowing line that breaks like glass (천검귀일's burst, Docs/PLAN_SKILL_VFX.md). Shards are drawn in code
    /// (64 px, smooth filtering): a clear pale-cyan pane with a thin bright rim and a diagonal reflection. They fly off
    /// both sides of the line tumbling end over end (the width flips like a pane turning in the air) and catch the
    /// light with an additive glint each time they face the camera.
    /// </summary>
    public static class GlassBurstFx
    {
        const int Size = 64, Variants = 6;
        static Sprite[] shardSprites;

        /// <summary>A line of space breaks like glass: shards thrown off both sides of it. No new line is drawn here.</summary>
        public static void Burst(Vector2 a, Vector2 b, int count, int order)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x);
            var sprites = ShardSprites;
            for (int i = 0; i < count; i++)
            {
                Vector2 at = Vector2.Lerp(a, b, Random.Range(.03f, .97f)) + n * Random.Range(-.15f, .15f);
                float side = Random.value < .5f ? 1f : -1f;
                // Mostly small slivers with a few large panes; small ones fly faster.
                float size = Random.value < .25f ? Random.Range(.55f, .9f) : Random.Range(.18f, .45f);
                Vector2 v = (n * side + d * Random.Range(-.6f, .6f)).normalized * Random.Range(4f, 9f) * Mathf.Lerp(1.2f, .7f, size);
                Shard.Spawn(at, v, size, Random.Range(.7f, 1.05f), order, sprites[Random.Range(0, sprites.Length)]);
            }
        }

        /// <summary>Irregular shard panes (3 to 5 corners), drawn once.</summary>
        static Sprite[] ShardSprites
        {
            get
            {
                if (shardSprites != null) return shardSprites;
                var rng = new System.Random(4217);
                shardSprites = new Sprite[Variants];
                for (int s = 0; s < Variants; s++)
                {
                    // A long, sharp polygon: corners around an ellipse, stretched along one axis.
                    int corners = 3 + s % 3;
                    var poly = new Vector2[corners];
                    float start = (float)rng.NextDouble() * Mathf.PI * 2f;
                    for (int k = 0; k < corners; k++)
                    {
                        float ang = start + k * Mathf.PI * 2f / corners + ((float)rng.NextDouble() - .5f) * .9f;
                        float r = .62f + (float)rng.NextDouble() * .38f;
                        poly[k] = new Vector2(Size / 2f + Mathf.Cos(ang) * r * Size * .48f, Size / 2f + Mathf.Sin(ang) * r * Size * .2f);
                    }
                    var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "GlassShard" + s };
                    var px = new Color32[Size * Size];
                    for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var p = new Vector2(x + .5f, y + .5f);
                        float edge = InsideDistance(p, poly);
                        if (edge <= 0f) { px[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                        // Rim: bright, anti-aliased over the outer 2 px. Body: faint, clearer toward the middle.
                        float rim = Mathf.Clamp01(1f - (edge - 1f) / 1.5f);
                        float aa = Mathf.Clamp01(edge);
                        // A diagonal reflection band across the pane.
                        float band = Mathf.Abs((x - y) * .7071f + Size * .1f) < Size * .07f ? .55f : 0f;
                        float bodyA = .28f + band;
                        var body = Color.Lerp(new Color(.62f, .86f, 1f), Color.white, band);
                        var c = Color.Lerp(body, Color.white, rim);
                        c.a = Mathf.Lerp(bodyA, .95f, rim) * aa;
                        px[y * Size + x] = c;
                    }
                    t.SetPixels32(px);
                    t.Apply(false, true);
                    shardSprites[s] = Sprite.Create(t, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), Size);
                }
                return shardSprites;
            }
        }

        /// <summary>Distance in pixels from p to the nearest edge of a convex-ish polygon, negative outside.</summary>
        static float InsideDistance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            Vector2 centre = Vector2.zero;
            foreach (var v in poly) centre += v;
            centre /= poly.Length;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                Vector2 e = (b - a).normalized, n = new Vector2(-e.y, e.x);
                if (Vector2.Dot(centre - a, n) < 0f) n = -n;
                best = Mathf.Min(best, Vector2.Dot(p - a, n));
            }
            return best;
        }

        /// <summary>One flying shard: shoots out, slows, spins and tumbles (glinting when it faces the camera), fades.</summary>
        sealed class Shard : MonoBehaviour
        {
            SpriteRenderer pane, glint;
            Vector2 velocity;
            float spin, tumble, phase, life, age, size;

            /// <param name="size">World width of the shard.</param>
            public static void Spawn(Vector2 at, Vector2 velocity, float size, float life, int order, Sprite sprite)
            {
                var go = new GameObject("GlassShard");
                if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
                go.transform.position = at;
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                var s = go.AddComponent<Shard>();
                s.pane = go.AddComponent<SpriteRenderer>();
                s.pane.sprite = sprite;
                if (FxMaterials.Alpha != null) s.pane.sharedMaterial = FxMaterials.Alpha;
                s.pane.sortingOrder = order;
                // The glint: the same pane drawn additively on top, bright only while it faces the camera.
                s.glint = new GameObject("Glint").AddComponent<SpriteRenderer>();
                s.glint.transform.SetParent(go.transform, false);
                s.glint.sprite = sprite;
                if (FxMaterials.Additive != null) s.glint.sharedMaterial = FxMaterials.Additive;
                s.glint.sortingOrder = order + 1;
                s.velocity = velocity;
                s.spin = Random.Range(180f, 540f) * (Random.value < .5f ? 1f : -1f);
                s.tumble = Random.Range(8f, 16f);
                s.phase = Random.Range(0f, Mathf.PI * 2f);
                s.life = life;
                s.size = size;
                s.Apply(0f);
            }

            void Update()
            {
                age += Time.deltaTime;
                if (age >= life) { Destroy(gameObject); return; }
                velocity *= Mathf.Pow(.1f, Time.deltaTime); // strong drag: a burst, then drifting pieces
                transform.position += (Vector3)(velocity * Time.deltaTime);
                transform.Rotate(0f, 0f, spin * Time.deltaTime);
                Apply(age / life);
            }

            void Apply(float k)
            {
                // Tumbling: the pane turns in the air, so its width swings through edge-on and back.
                float turn = Mathf.Cos(phase + age * tumble);
                float w = size * (1f - .3f * k);
                transform.localScale = new Vector3(w * Mathf.Max(.12f, Mathf.Abs(turn)), w, 1f);
                float fade = k < .6f ? 1f : 1f - (k - .6f) / .4f;
                pane.color = new Color(1f, 1f, 1f, fade);
                float glintA = Mathf.Pow(Mathf.Abs(turn), 8f) * fade;
                glint.color = new Color(.9f, .97f, 1f, glintA);
            }
        }
    }
}
