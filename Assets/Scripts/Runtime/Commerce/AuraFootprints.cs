using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [COSMETIC] Footprints left by the worn aura while walking, graded by the aura's rarity (skins leave afterimages,
    /// auras leave footprints). Common/free: none. Rare: plain prints. Epic: glowing prints. Unique: glowing prints and
    /// rising sparkles. Legend: colour-cycling glowing prints, sparkles and a small ground ripple on every step.
    /// Purely visual: no colliders, no gameplay state, pooled renderers.
    /// </summary>
    public sealed class AuraFootprints : MonoBehaviour
    {
        const float Stride = .42f, Side = .09f, Teleport = 2f;
        const int Pool = 28;

        sealed class Print { public SpriteRenderer r, glow; public float age, life; public Color color; }

        static Sprite foot;
        System.Func<CosmeticProduct> worn;
        PlayerController player;
        readonly Print[] prints = new Print[Pool];
        int next;
        Vector2 last;
        float walked;
        bool left;

        /// <summary>Footprints for <paramref name="player"/>; <paramref name="worn"/> tells the aura it wears now (local store or network).</summary>
        public static void Attach(PlayerController player, System.Func<CosmeticProduct> worn)
        {
            var f = player.gameObject.AddComponent<AuraFootprints>();
            f.worn = worn;
            f.player = player;
            f.last = player.transform.position;
        }

        /// <summary>A small pixel shoe print (sole and heel), pointing up; rotated to the walking direction.</summary>
        static Sprite Foot
        {
            get
            {
                if (foot != null) return foot;
                const int w = 7, h = 12;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "AuraFoot" };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = x + .5f - w / 2f;
                    bool sole = y >= 5 && (cx * cx) / 10f + ((y - 8.2f) * (y - 8.2f)) / 13f <= 1f;
                    bool heel = y <= 3 && (cx * cx) / 6f + ((y - 1.8f) * (y - 1.8f)) / 4f <= 1f;
                    px[y * w + x] = sole || heel ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
                t.SetPixels32(px);
                t.Apply(false, true);
                foot = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 36f);
                return foot;
            }
        }

        static int Grade(CosmeticProduct p)
        {
            if (p == null || p.IsFree || p.IsSkin) return 0;
            switch (p.Rarity)
            {
                case CosmeticRarity.Rare: return 1;
                case CosmeticRarity.Epic: return 2;
                case CosmeticRarity.Unique: return 3;
                case CosmeticRarity.Legend: return 4;
                default: return 0;
            }
        }

        void Update()
        {
            Fade();
            if (player == null || worn == null) return;
            Vector2 now = player.transform.position;
            Vector2 step = now - last;
            last = now;
            if (step.sqrMagnitude > Teleport * Teleport || player.IsDead) { walked = 0f; return; } // portals, respawns
            var product = worn();
            int grade = Grade(product);
            if (grade == 0) { walked = 0f; return; }
            walked += step.magnitude;
            if (walked < Stride || step.sqrMagnitude < 1e-6f) return;
            walked = 0f;
            Stamp(now, step.normalized, grade, product);
        }

        void Stamp(Vector2 at, Vector2 dir, int grade, CosmeticProduct product)
        {
            left = !left;
            Vector2 side = new Vector2(-dir.y, dir.x) * (left ? Side : -Side);
            Vector2 pos = at + side + new Vector2(0f, .02f);
            Color c = product.Effect != CosmeticEffect.None ? product.ColorAt(Time.time) : product.Color;
            c.a = 1f;
            var p = prints[next];
            // The pooled renderers live under Fx.Root, which a map change destroys; the local player outlives it.
            if (p == null || p.r == null || p.glow == null) p = prints[next] = Make();
            next = (next + 1) % Pool;
            p.age = 0f;
            p.life = grade >= 4 ? 2f : grade == 3 ? 1.6f : grade == 2 ? 1.3f : .9f;
            p.color = c;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            foreach (var r in new[] { p.r, p.glow })
            {
                r.transform.position = pos;
                r.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                r.gameObject.SetActive(true);
            }
            p.glow.gameObject.SetActive(grade >= 2);
            p.glow.transform.localScale = new Vector3(2.2f, 1.7f, 1f);
            if (grade >= 3)
                SkillVisuals.Sparks(pos + Vector2.up * .1f, Color.Lerp(c, Color.white, .4f), grade >= 4 ? 4 : 2, 1.2f, .5f);
            if (grade >= 4)
                SkillFx.Spawn("fx_ring", pos, new Color(c.r, c.g, c.b, .55f), .5f, SkillFx.GroundOrder + 3).Scale(new Vector2(.1f, .07f), new Vector2(.7f, .49f)).Fade(FxFade.Late);
        }

        Print Make()
        {
            var p = new Print();
            p.r = new GameObject("AuraFootprint").AddComponent<SpriteRenderer>();
            p.r.sprite = Foot;
            p.r.sortingOrder = SkillFx.GroundOrder + 2;
            p.glow = new GameObject("AuraFootGlow").AddComponent<SpriteRenderer>();
            p.glow.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) p.glow.sharedMaterial = FxMaterials.Additive;
            p.glow.sortingOrder = SkillFx.GroundOrder + 1;
            if (Fx.Root != null) { p.r.transform.SetParent(Fx.Root, true); p.glow.transform.SetParent(Fx.Root, true); }
            return p;
        }

        void Fade()
        {
            foreach (var p in prints)
            {
                if (p == null || p.r == null || p.glow == null || !p.r.gameObject.activeSelf) continue;
                p.age += Time.deltaTime;
                if (p.age >= p.life) { p.r.gameObject.SetActive(false); p.glow.gameObject.SetActive(false); continue; }
                float a = 1f - p.age / p.life;
                p.r.color = new Color(p.color.r, p.color.g, p.color.b, .75f * a);
                p.glow.color = new Color(p.color.r, p.color.g, p.color.b, .35f * a);
            }
        }

        void OnDestroy()
        {
            foreach (var p in prints)
            {
                if (p == null) continue;
                if (p.r != null) Destroy(p.r.gameObject);
                if (p.glow != null) Destroy(p.glow.gameObject);
            }
        }
    }
}
