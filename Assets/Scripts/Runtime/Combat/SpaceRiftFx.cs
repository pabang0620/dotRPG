using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] 천검귀일's space rift along one arm of the cross (Docs/PLAN_SKILL_VFX.md, "공간 균열"). The caller drives it:
    /// <see cref="Gap"/> opens a dark void with white rims, <see cref="Cracks"/> grows jagged cracks out of it,
    /// <see cref="Flare"/> brightens the rims as it tears, <see cref="Shatter"/> snaps it shut and throws glass-like
    /// shards of space. The void and rims use normal alpha blending so they read on any ground colour.
    /// </summary>
    public class SpaceRiftFx : MonoBehaviour
    {
        const int CrackCount = 6, CrackPoints = 5, ShardsPerArm = 30;
        const float RimWidth = .07f, CrackWidth = .05f;
        static readonly Color Void = new Color(.03f, .04f, .13f, .93f);
        static readonly Color Rim = new Color(.86f, .97f, 1f, 1f);
        static readonly Color Glow = new Color(.4f, .78f, 1f, 1f);
        static Sprite[] shardSprites;

        LineRenderer glow, voidLine, rimA, rimB;
        readonly LineRenderer[] cracks = new LineRenderer[CrackCount];
        readonly Vector2[][] crackPath = new Vector2[CrackCount][];
        Vector2 from, to, dir, normal;
        int order;

        public Vector2 Offset;
        /// <summary>Void width in world units.</summary>
        public float Gap;
        /// <summary>0..1: how far the side cracks have grown.</summary>
        public float Cracks;
        /// <summary>0..1: extra rim brightness while the rift tears open.</summary>
        public float Flare;

        public static SpaceRiftFx Spawn(Vector2 from, Vector2 to, int order)
        {
            var go = new GameObject("SpaceRift");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<SpaceRiftFx>();
            fx.from = from;
            fx.to = to;
            fx.dir = (to - from).normalized;
            fx.normal = new Vector2(-fx.dir.y, fx.dir.x);
            fx.order = order;
            var taper = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.12f, 1f), new Keyframe(.88f, 1f), new Keyframe(1f, 0f));
            fx.glow = Line(go.transform, FxMaterials.SoftAdditive, order, taper);
            fx.voidLine = Line(go.transform, FxMaterials.Alpha, order + 1, taper);
            fx.rimA = Line(go.transform, FxMaterials.Alpha, order + 2, taper);
            fx.rimB = Line(go.transform, FxMaterials.Alpha, order + 2, taper);
            float length = (to - from).magnitude;
            for (int i = 0; i < CrackCount; i++)
            {
                // Spread along the arm, alternating sides, each a jagged path pointing away from the slit.
                float along = (i + .5f) / CrackCount + Random.Range(-.05f, .05f);
                float side = i % 2 == 0 ? 1f : -1f;
                Vector2 start = Vector2.Lerp(from, to, Mathf.Clamp(along, .1f, .9f));
                Vector2 heading = (fx.normal * side + fx.dir * Random.Range(-.6f, .6f)).normalized;
                float len = Mathf.Clamp(length * Random.Range(.05f, .11f), .6f, 1.5f);
                var path = new Vector2[CrackPoints];
                path[0] = start;
                for (int k = 1; k < CrackPoints; k++)
                {
                    var bend = new Vector2(-heading.y, heading.x) * Random.Range(-.25f, .25f) * len / CrackPoints * 2f;
                    path[k] = path[k - 1] + heading * (len / (CrackPoints - 1)) + bend;
                }
                fx.crackPath[i] = path;
                fx.cracks[i] = Line(go.transform, FxMaterials.Alpha, order + 2, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, .2f)));
                fx.cracks[i].positionCount = 0;
            }
            fx.Refresh();
            return fx;
        }

        static LineRenderer Line(Transform parent, Material material, int order, AnimationCurve width)
        {
            var line = FxMaterials.NewLine(parent, material, order);
            line.positionCount = 2;
            line.widthCurve = width;
            return line;
        }

        void LateUpdate() => Refresh();

        void Refresh()
        {
            Vector2 a = from + Offset, b = to + Offset, half = normal * (Gap * .5f + RimWidth * .5f);
            glow.SetPosition(0, a); glow.SetPosition(1, b);
            glow.widthMultiplier = Gap + .5f + Flare * .6f;
            glow.startColor = glow.endColor = new Color(Glow.r, Glow.g, Glow.b, .45f + Flare * .45f);
            voidLine.SetPosition(0, a); voidLine.SetPosition(1, b);
            voidLine.widthMultiplier = Mathf.Max(.001f, Gap);
            voidLine.startColor = voidLine.endColor = Void;
            rimA.SetPosition(0, a + half); rimA.SetPosition(1, b + half);
            rimB.SetPosition(0, a - half); rimB.SetPosition(1, b - half);
            rimA.widthMultiplier = rimB.widthMultiplier = RimWidth * (1f + Flare * .6f);
            rimA.startColor = rimA.endColor = rimB.startColor = rimB.endColor = Rim;
            for (int i = 0; i < CrackCount; i++)
            {
                // Grow each crack point by point (later cracks start a little later).
                float grow = Mathf.Clamp01(Cracks * 1.25f - i * .04f) * (CrackPoints - 1);
                int full = Mathf.FloorToInt(grow);
                var line = cracks[i];
                if (grow <= 0f) { line.positionCount = 0; continue; }
                int count = Mathf.Min(CrackPoints, full + 2);
                line.positionCount = count;
                for (int k = 0; k < count; k++)
                {
                    Vector2 p = k <= full ? crackPath[i][k] : Vector2.Lerp(crackPath[i][k - 1], crackPath[i][k], grow - full);
                    // Keep the root on the rim the crack grows out of.
                    if (k == 0) p += normal * Mathf.Sign(Vector2.Dot(crackPath[i][1] - crackPath[i][0], normal)) * Gap * .5f;
                    line.SetPosition(k, p + Offset);
                }
                line.widthMultiplier = CrackWidth * (1f + Flare);
                line.startColor = line.endColor = Rim;
            }
        }

        /// <summary>The rift snaps shut in a white flash and space breaks into shards that fly off and spin away.</summary>
        public void Shatter()
        {
            Vector2 a = from + Offset, b = to + Offset;
            GlowLineFx.Spawn(a, b, new Color(.8f, .95f, 1f, 1f), .35f, .25f, order + 3);
            for (int i = 0; i < ShardsPerArm; i++)
            {
                Vector2 at = Vector2.Lerp(a, b, Random.Range(.04f, .96f)) + normal * Random.Range(-.2f, .2f);
                float side = Random.value < .5f ? 1f : -1f;
                Vector2 v = (normal * side + dir * Random.Range(-.5f, .5f)).normalized * Random.Range(3.5f, 8f);
                Shard.Spawn(at, v, Random.Range(.25f, .7f), Random.Range(.6f, .85f), order + 3);
            }
            Destroy(gameObject);
        }

        /// <summary>Removes it at once (the skill was interrupted).</summary>
        public void Kill() => Destroy(gameObject);

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

            public static void Spawn(Vector2 at, Vector2 velocity, float size, float life, int order)
            {
                var go = new GameObject("RiftShard");
                if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
                go.transform.position = at;
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                var s = go.AddComponent<Shard>();
                s.sr = go.AddComponent<SpriteRenderer>();
                var sprites = ShardSprites;
                s.sr.sprite = sprites[Random.Range(0, sprites.Length)];
                if (FxMaterials.Alpha != null) s.sr.sharedMaterial = FxMaterials.Alpha;
                s.sr.sortingOrder = order;
                s.velocity = velocity;
                s.spin = Random.Range(360f, 900f) * (Random.value < .5f ? 1f : -1f);
                s.life = life;
                s.size = size;
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
