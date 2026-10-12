using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] 천쇄방패's barrier (Docs/PLAN_SKILL_VFX.md): giant shields of light that fall from the sky one after
    /// another and stab into the ground around a centre (dust and a flash where each lands), seen in top-down
    /// perspective. Driven from outside: <see cref="Radius"/> (the ring closes in), <see cref="Brightness"/>. Uses the
    /// generated shield (g5_shield, else g4_wall) when present, else a code-drawn translucent pane with a bright rim. Panes in front of the centre sort over characters, the
    /// ones behind under them.
    /// </summary>
    public class ShieldWallFx : MonoBehaviour
    {
        const float Squash = .7f, PaneHeight = 2f, DropHeight = 5f, FallTime = .16f, Gap = .045f;
        static Sprite codePane;

        SpriteRenderer[] panes, glows;
        float[] angles;
        float paneWorldH = 1f, paneWorldW = 1f, pivotY;

        public Vector2 Center;
        public float Radius = 3f;
        /// <summary>1 = every shield has landed (they land one after another over about 0.7 s from the spawn).</summary>
        public float Landed => panes.Length == 0 ? 1f : Mathf.Clamp01((Time.time - spawnedAt) / ((panes.Length - 1) * Gap + FallTime));
        float spawnedAt;
        bool picture; // a generated shield keeps its own shape and colours
        bool[] hit;
        public float Brightness = 1f;
        public Color Tint = new Color(1f, .85f, .4f);

        public static ShieldWallFx Spawn(Vector2 center, float radius, int count)
        {
            var go = new GameObject("ShieldWall");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<ShieldWallFx>();
            fx.Center = center;
            fx.Radius = radius;
            string id = VfxLibrary.Has("g5_shield") ? "g5_shield" : VfxLibrary.Has("g4_wall") ? "g4_wall" : null;
            Sprite sprite = id != null && VfxLibrary.Get(id).Length > 0 ? VfxLibrary.Get(id)[0] : CodePane;
            fx.spawnedAt = Time.time;
            fx.picture = id == "g5_shield";
            fx.paneWorldH = Mathf.Max(.01f, sprite.bounds.size.y);
            fx.paneWorldW = Mathf.Max(.01f, sprite.bounds.size.x);
            fx.pivotY = sprite.pivot.y / Mathf.Max(1f, sprite.rect.height); // generated panes are centred, the code pane stands on its foot
            fx.panes = new SpriteRenderer[count];
            fx.glows = new SpriteRenderer[count];
            fx.angles = new float[count];
            fx.hit = new bool[count];
            for (int i = 0; i < count; i++)
            {
                // Landing order goes round the ring from the front.
                fx.angles[i] = -Mathf.PI * .5f + i * Mathf.PI * 2f / count;
                var p = new GameObject("Pane").AddComponent<SpriteRenderer>();
                p.transform.SetParent(go.transform, false);
                p.sprite = sprite;
                if (FxMaterials.Alpha != null) p.sharedMaterial = FxMaterials.Alpha;
                fx.panes[i] = p;
                var g = new GameObject("Glow").AddComponent<SpriteRenderer>();
                g.transform.SetParent(p.transform, false);
                g.sprite = sprite;
                if (FxMaterials.Additive != null) g.sharedMaterial = FxMaterials.Additive;
                fx.glows[i] = g;
            }
            fx.Refresh();
            return fx;
        }

        /// <summary>A translucent gold pane, brighter at the rim and the top edge (when g4_wall is not there).</summary>
        static Sprite CodePane
        {
            get
            {
                if (codePane != null) return codePane;
                const int w = 24, h = 48;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "ShieldPane" };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), h - 1 - y);
                    float rim = edge < 1.5f ? 1f : 0f;
                    float top = Mathf.Clamp01((y - h * .6f) / (h * .4f));
                    float hex = (x + (y / 6) * 3) % 6 == 0 || y % 6 == 0 ? .25f : 0f; // a faint hex-like lattice
                    float a = Mathf.Clamp01(.22f + .5f * top + hex + rim);
                    var c = Color.Lerp(new Color(1f, .85f, .45f), Color.white, Mathf.Max(rim, top * .6f));
                    px[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
                t.SetPixels32(px);
                t.Apply(false, true);
                codePane = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, 0f), 32f);
                return codePane;
            }
        }

        /// <summary>Breaks the wall: every pane bursts into golden glass, then the wall is gone.</summary>
        public void Shatter(int order)
        {
            for (int i = 0; i < panes.Length; i++)
            {
                Vector2 foot = Foot(i);
                GlassBurstFx.Burst(foot, foot + Vector2.up * PaneHeight, 5, order, Tint);
            }
            Destroy(gameObject);
        }

        public void Kill() => Destroy(gameObject);

        Vector2 Foot(int i) => Center + new Vector2(Mathf.Cos(angles[i]) * Radius, Mathf.Sin(angles[i]) * Radius * Squash);

        void LateUpdate() => Refresh();

        void Refresh()
        {
            float h = picture ? PaneHeight * 1.35f : PaneHeight; // the generated shield is narrower, so taller
            float b = Mathf.Max(0f, Brightness);
            for (int i = 0; i < panes.Length; i++)
            {
                Vector2 foot = Foot(i);
                var p = panes[i];
                // Falling: accelerating down from the sky, invisible until it starts.
                float k = Mathf.Clamp01((Time.time - spawnedAt - i * Gap) / FallTime);
                float drop = (1f - k * k) * DropHeight;
                p.enabled = glows[i].enabled = Time.time - spawnedAt >= i * Gap;
                if (k >= 1f && !hit[i])
                {
                    hit[i] = true;
                    Fx.Burst("fx_dust", foot, 3, 1.4f, .45f);
                    SkillVisuals.Flash(foot + Vector2.up * .3f, new Color(1f, .92f, .6f, .7f), .9f, .1f);
                }
                p.transform.position = foot + Vector2.up * (h * pivotY + drop);
                // Each pane covers its share of the ring (a closed wall), narrower toward the sides where it is seen
                // edge-on, full width at the front and back.
                float facing = Mathf.Lerp(.45f, 1f, Mathf.Abs(Mathf.Sin(angles[i])));
                float span = Mathf.Clamp(2f * Mathf.PI * Radius / panes.Length * .9f, .35f, 2.6f);
                float s = h / paneWorldH;
                p.transform.localScale = picture ? new Vector3(s * facing, s, 1f) : new Vector3(span * facing / paneWorldW, s, 1f);
                p.sortingOrder = SkillFx.At(foot.y, 6);
                p.color = picture ? Color.white : new Color(Tint.r, Tint.g, Tint.b, Mathf.Clamp01(.85f * Mathf.Min(1f, b)));
                // A white flash in the moment it stabs in, then the normal glow.
                float land = k >= 1f ? Mathf.Clamp01(1f - (Time.time - spawnedAt - i * Gap - FallTime) / .2f) : 0f;
                glows[i].color = new Color(Tint.r, Tint.g, Tint.b, Mathf.Clamp01(.35f * (b - .5f) + land * .8f));
                glows[i].sortingOrder = p.sortingOrder + 1;
            }
        }
    }
}
