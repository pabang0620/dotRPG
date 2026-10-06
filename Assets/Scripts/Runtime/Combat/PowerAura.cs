using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [FX] A body aura for a class's power window (검귀 해방 first): a glowing silhouette pulsing behind the body,
    /// afterimages left while moving and flame motes rising off the body. Purely visual; it ends by itself.
    /// </summary>
    public sealed class PowerAura : MonoBehaviour
    {
        const float GhostLife = .35f;
        static Sprite dot;

        sealed class Bit { public SpriteRenderer r; public Vector2 vel; public float age, life, size; }

        SpriteRenderer body, glow;
        readonly List<Bit> motes = new List<Bit>();
        readonly List<Bit> ghosts = new List<Bit>();
        Color inner, outer;
        float end, ghostAcc, moteAcc;
        Vector3 lastPos;

        static Sprite Dot
        {
            get
            {
                if (dot != null) return dot;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "AuraMote" };
                t.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
                t.Apply();
                dot = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 36f);
                return dot;
            }
        }

        /// <summary>Starts (or extends) the aura on a character for <paramref name="seconds"/>.</summary>
        public static void Play(PlayerController who, float seconds, Color inner, Color outer)
        {
            var anim = who.GetComponent<CharacterAnimator>();
            if (anim == null || anim.Renderer == null) return;
            var a = who.GetComponent<PowerAura>();
            if (a == null) a = who.gameObject.AddComponent<PowerAura>();
            a.body = anim.Renderer;
            a.inner = inner;
            a.outer = outer;
            a.end = Mathf.Max(a.end, Time.time + seconds);
            a.lastPos = who.transform.position;
            a.enabled = true;
        }

        static SpriteRenderer Additive(string name, SpriteRenderer like)
        {
            var r = new GameObject(name).AddComponent<SpriteRenderer>();
            if (FxMaterials.Additive != null) r.sharedMaterial = FxMaterials.Additive;
            r.sortingLayerID = like.sortingLayerID;
            return r;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool on = Time.time < end && body != null;
            bool visible = on && body.enabled && body.gameObject.activeInHierarchy && body.sprite != null;

            // The silhouette: the body's own frame, a little larger and glowing, behind the body.
            if (visible)
            {
                if (glow == null) glow = Additive("PowerAuraGlow", body);
                float left = end - Time.time;
                float fade = Mathf.Clamp01(left / .4f);
                float pulse = .5f + .5f * Mathf.Sin(Time.time * 9f);
                glow.enabled = true;
                glow.sprite = body.sprite;
                glow.flipX = body.flipX;
                glow.transform.position = body.transform.position;
                glow.transform.localScale = body.transform.lossyScale * (1.1f + .04f * pulse);
                glow.sortingOrder = body.sortingOrder - 1;
                var g = Color.Lerp(inner, outer, pulse);
                g.a = (.45f + .3f * pulse) * fade;
                glow.color = g;
            }
            else if (glow != null) glow.enabled = false;

            var pos = transform.position;
            float moved = ((Vector2)(pos - lastPos)).magnitude;
            lastPos = pos;
            if (visible)
            {
                // Afterimages: a steady trail while moving, a slower one while standing.
                ghostAcc += dt;
                if (ghostAcc >= (moved > .002f ? .05f : .16f) && ghosts.Count < 10) { ghostAcc = 0f; SpawnGhost(); }
                moteAcc += dt * 34f;
                while (moteAcc >= 1f && motes.Count < 40) { moteAcc -= 1f; SpawnMote(); }
            }

            Age(ghosts, dt, (b, k) =>
            {
                var c = Color.Lerp(inner, outer, k);
                c.a = .6f * (1f - k) * (1f - k);
                b.r.color = c;
            });
            Age(motes, dt, (b, k) =>
            {
                b.r.transform.position += (Vector3)(b.vel * dt);
                b.vel *= 1f - dt * 1.5f;
                var c = Color.Lerp(outer, inner, k);
                c.a = k < .2f ? k / .2f : 1f - (k - .2f) / .8f;
                b.r.color = c;
                float s = b.size * (1f - k * .5f);
                b.r.transform.localScale = new Vector3(s, s, 1f);
                if (body != null) b.r.sortingOrder = body.sortingOrder + 1;
            });

            if (!on && ghosts.Count == 0 && motes.Count == 0)
            {
                if (glow != null) glow.enabled = false;
                enabled = false;
            }
        }

        static void Age(List<Bit> list, float dt, System.Action<Bit, float> step)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var b = list[i];
                b.age += dt;
                if (b.r == null || b.age >= b.life)
                {
                    if (b.r != null) Destroy(b.r.gameObject);
                    list.RemoveAt(i);
                    continue;
                }
                step(b, b.age / b.life);
            }
        }

        void SpawnGhost()
        {
            var r = Additive("PowerAuraGhost", body);
            r.sprite = body.sprite;
            r.flipX = body.flipX;
            r.transform.position = body.transform.position;
            r.transform.localScale = body.transform.lossyScale;
            r.sortingOrder = body.sortingOrder - 2;
            ghosts.Add(new Bit { r = r, life = GhostLife });
        }

        void SpawnMote()
        {
            var r = Additive("PowerAuraMote", body);
            r.sprite = Dot;
            var b = body.bounds;
            r.transform.position = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.center.y + b.extents.y * .5f), 0f);
            float size = Random.Range(.8f, 1.6f);
            motes.Add(new Bit { r = r, vel = new Vector2(Random.Range(-.25f, .25f), Random.Range(1.2f, 2.4f)), life = Random.Range(.45f, .8f), size = size });
        }

        void OnDisable()
        {
            if (glow != null) glow.enabled = false;
        }

        void OnDestroy()
        {
            if (glow != null) Destroy(glow.gameObject);
            foreach (var b in ghosts) if (b.r != null) Destroy(b.r.gameObject);
            foreach (var b in motes) if (b.r != null) Destroy(b.r.gameObject);
        }
    }
}
