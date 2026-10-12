using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] A glowing line that breaks like glass (천검귀일's burst, Docs/PLAN_SKILL_VFX.md): a thick white flash along
    /// the line and glass shards (generated f3_glass_shards, else plain triangles) thrown off both sides, spinning away.
    /// </summary>
    public static class GlassBurstFx
    {
        static Sprite[] shardSprites;

        /// <summary>
        /// A line of space breaks like glass: a thick white flash along it and shards thrown off both sides. Uses the
        /// generated glass shard sprites (f3_glass_shards) when present, else plain triangles.
        /// </summary>
        public static void Burst(Vector2 a, Vector2 b, int count, int order)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x);
            GlowLineFx.Spawn(a, b, new Color(.8f, .95f, 1f, 1f), .6f, .3f, order);
            var glass = VfxLibrary.Has("f3_glass_shards") ? VfxLibrary.Get("f3_glass_shards") : null;
            for (int i = 0; i < count; i++)
            {
                Vector2 at = Vector2.Lerp(a, b, Random.Range(.04f, .96f)) + n * Random.Range(-.2f, .2f);
                float side = Random.value < .5f ? 1f : -1f;
                Vector2 v = (n * side + d * Random.Range(-.5f, .5f)).normalized * Random.Range(3.5f, 8f);
                // The last generated shard has the watermark corner: use the first seven.
                var sprite = glass != null && glass.Length > 0 ? glass[Random.Range(0, Mathf.Min(7, glass.Length))] : null;
                Shard.Spawn(at, v, Random.Range(.6f, 1.2f), Random.Range(.7f, .95f), order, sprite);
            }
        }

        /// <summary>Three irregular triangle sprites (32 px = 1 unit): pale cyan glass with a white edge.</summary>
        static Sprite[] ShardSprites
        {
            get
            {
                if (shardSprites != null) return shardSprites;
                var shapes = new[]
                {
                    new[] { new Vector2(2, 3), new Vector2(29, 9), new Vector2(11, 29) },
                    new[] { new Vector2(4, 14), new Vector2(30, 2), new Vector2(22, 30) },
                    new[] { new Vector2(1, 28), new Vector2(15, 1), new Vector2(31, 21) },
                };
                shardSprites = new Sprite[shapes.Length];
                for (int s = 0; s < shapes.Length; s++)
                {
                    const int n = 32;
                    var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RiftShard" + s };
                    var px = new Color32[n * n];
                    var tri = shapes[s];
                    for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector2(x + .5f, y + .5f);
                        float d = Mathf.Min(EdgeDistance(p, tri[0], tri[1], tri[2]), Mathf.Min(EdgeDistance(p, tri[1], tri[2], tri[0]), EdgeDistance(p, tri[2], tri[0], tri[1])));
                        if (d < 0f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                        px[y * n + x] = d < 2f ? new Color32(255, 255, 255, 255) : new Color32(170, 220, 255, 215);
                    }
                    t.SetPixels32(px);
                    t.Apply(false, true);
                    shardSprites[s] = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
                }
                return shardSprites;
            }
        }

        /// <summary>Signed distance from p to edge a-b, positive on the side where c is.</summary>
        static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 e = (b - a).normalized, n = new Vector2(-e.y, e.x);
            if (Vector2.Dot(c - a, n) < 0f) n = -n;
            return Vector2.Dot(p - a, n);
        }

        /// <summary>One flying shard of space: shoots out, slows, spins, shrinks a little and fades.</summary>
        sealed class Shard : MonoBehaviour
        {
            SpriteRenderer sr;
            Vector2 velocity;
            float spin, life, age, size;

            /// <param name="size">World width of the shard.</param>
            public static void Spawn(Vector2 at, Vector2 velocity, float size, float life, int order, Sprite sprite)
            {
                var go = new GameObject("RiftShard");
                if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
                go.transform.position = at;
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                var s = go.AddComponent<Shard>();
                s.sr = go.AddComponent<SpriteRenderer>();
                var sprites = ShardSprites;
                s.sr.sprite = sprite != null ? sprite : sprites[Random.Range(0, sprites.Length)];
                if (FxMaterials.Alpha != null) s.sr.sharedMaterial = FxMaterials.Alpha;
                s.sr.sortingOrder = order;
                s.velocity = velocity;
                s.spin = Random.Range(360f, 900f) * (Random.value < .5f ? 1f : -1f);
                s.life = life;
                s.size = size / Mathf.Max(.01f, s.sr.sprite.bounds.size.x); // the sprite's own width becomes `size`
                go.transform.localScale = Vector3.one * size;
            }

            void Update()
            {
                age += Time.deltaTime;
                if (age >= life) { Destroy(gameObject); return; }
                float k = age / life;
                velocity *= Mathf.Pow(.08f, Time.deltaTime); // strong drag: a burst, then drifting pieces
                transform.position += (Vector3)(velocity * Time.deltaTime);
                transform.Rotate(0f, 0f, spin * Time.deltaTime);
                transform.localScale = Vector3.one * size * (1f - .35f * k);
                sr.color = new Color(1f, 1f, 1f, k < .55f ? 1f : 1f - (k - .55f) / .45f);
            }
        }
    }
}
