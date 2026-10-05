using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The motes of a costume skin: a few pixel particles around the wearer, more while walking (gold sparks,
    /// moon motes, twinkling stars, rising embers), and (unique skins only) while moving a fading afterimage of the body in the
    /// skin's colour left along the path. Purely visual: no colliders, no combat random numbers.
    /// </summary>
    public sealed class SkinTrail : MonoBehaviour
    {
        const int Max = 28;
        static Sprite dot;

        sealed class Mote { public SpriteRenderer r; public Vector2 vel; public float age, life, size, twinkle; }

        readonly List<Mote> motes = new List<Mote>();
        sealed class Ghost { public SpriteRenderer r; public float age; }
        readonly List<Ghost> ghosts = new List<Ghost>();
        const float GhostEvery = 0.07f, GhostLife = 0.38f;
        float ghostAcc;
        SkinDef skin;
        SpriteRenderer body;
        Vector3 lastPos;
        float spawnAcc;
        readonly System.Random rng = new System.Random();

        static Sprite Dot
        {
            get
            {
                if (dot != null) return dot;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "SkinMote" };
                t.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
                t.Apply();
                dot = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 36f);
                return dot;
            }
        }

        /// <summary>Shows (or clears with null) the skin's motes on this character.</summary>
        public static void Set(PlayerController who, SpriteRenderer body, SkinDef skin)
        {
            var t = who.GetComponent<SkinTrail>();
            if (skin == null) { if (t != null) t.Clear(); return; }
            if (t == null) t = who.gameObject.AddComponent<SkinTrail>();
            t.skin = skin;
            t.body = body;
            t.lastPos = who.transform.position;
        }

        void Clear()
        {
            skin = null;
            foreach (var m in motes) if (m.r != null) Destroy(m.r.gameObject);
            motes.Clear();
            foreach (var g in ghosts) if (g.r != null) Destroy(g.r.gameObject);
            ghosts.Clear();
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void Update()
        {
            if (skin == null) return;
            float dt = Time.deltaTime;
            var pos = transform.position;
            float moved = ((Vector2)(pos - lastPos)).magnitude;
            lastPos = pos;
            bool visible = body != null && body.enabled && body.gameObject.activeInHierarchy;
            // Idle: a slow trickle. Walking: a trail behind the feet.
            spawnAcc += dt * (visible ? (moved > .0005f ? 22f : 5f) : 0f);
            while (spawnAcc >= 1f && motes.Count < Max) { spawnAcc -= 1f; Spawn(moved > .0005f); }
            if (spawnAcc >= 1f) spawnAcc = 0f;
            // Afterimages along the path while moving (unique skins only).
            if (visible && skin.afterimage && moved > .002f)
            {
                ghostAcc += dt;
                if (ghostAcc >= GhostEvery) { ghostAcc = 0f; SpawnGhost(); }
            }
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var g = ghosts[i];
                g.age += dt;
                if (g.age >= GhostLife || g.r == null)
                {
                    if (g.r != null) Destroy(g.r.gameObject);
                    ghosts.RemoveAt(i);
                    continue;
                }
                float k = g.age / GhostLife;
                var c = Color.Lerp(skin.trailA, skin.trailB, k);
                c.a = .55f * (1f - k) * (1f - k);
                g.r.color = c;
            }
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i];
                m.age += dt;
                if (m.age >= m.life || m.r == null)
                {
                    if (m.r != null) Destroy(m.r.gameObject);
                    motes.RemoveAt(i);
                    continue;
                }
                float k = m.age / m.life;
                m.r.transform.position += (Vector3)(m.vel * dt);
                if (skin.trail == SkinTrailKind.MoonMotes) m.vel = new Vector2(Mathf.Sin((m.age + m.twinkle) * 4f) * .25f, m.vel.y);
                var c = Color.Lerp(skin.trailA, skin.trailB, skin.trail == SkinTrailKind.Embers ? k : Mathf.PingPong(m.age * 3f + m.twinkle, 1f));
                float fade = k < .15f ? k / .15f : 1f - (k - .15f) / .85f;
                if (skin.trail == SkinTrailKind.Stars) fade *= .45f + .55f * Mathf.Abs(Mathf.Sin((m.age + m.twinkle) * 7f));
                c.a = fade;
                m.r.color = c;
                float s = m.size * (skin.trail == SkinTrailKind.Embers ? 1f - k * .6f : 1f);
                m.r.transform.localScale = new Vector3(s, s, 1f);
                if (body != null) m.r.sortingOrder = body.sortingOrder + 1;
            }
        }

        void SpawnGhost()
        {
            if (body == null || body.sprite == null || ghosts.Count >= 8) return;
            var go = new GameObject("SkinAfterimage");
            go.transform.position = body.transform.position;
            go.transform.localScale = body.transform.lossyScale;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = body.sprite;
            r.flipX = body.flipX;
            r.sharedMaterial = FxMaterials.Additive != null ? FxMaterials.Additive : r.sharedMaterial;
            r.sortingLayerID = body.sortingLayerID;
            r.sortingOrder = body.sortingOrder - 1;
            ghosts.Add(new Ghost { r = r });
        }

        void Spawn(bool walking)
        {
            var go = new GameObject("SkinMote");
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = Dot;
            r.sharedMaterial = FxMaterials.Additive != null ? FxMaterials.Additive : r.sharedMaterial;
            var origin = (Vector2)transform.position;
            Vector2 at, vel;
            float life, size;
            switch (skin.trail)
            {
                case SkinTrailKind.Embers:
                    at = origin + new Vector2(R(-.3f, .3f), R(0f, .25f));
                    vel = new Vector2(R(-.15f, .15f), R(.5f, 1.1f));
                    life = R(.6f, 1.1f); size = R(.8f, 1.4f);
                    break;
                case SkinTrailKind.Stars:
                    at = origin + new Vector2(R(-.55f, .55f), R(.1f, 1.3f));
                    vel = new Vector2(0f, R(.02f, .12f));
                    life = R(.9f, 1.6f); size = R(.7f, 1.3f);
                    break;
                case SkinTrailKind.MoonMotes:
                    at = origin + new Vector2(R(-.4f, .4f), R(.05f, .9f));
                    vel = new Vector2(0f, R(.15f, .4f));
                    life = R(.9f, 1.4f); size = R(.7f, 1.1f);
                    break;
                default: // gold sparks: kicked up from the feet while walking, a glint now and then at rest
                    at = origin + new Vector2(R(-.3f, .3f), walking ? R(0f, .15f) : R(.2f, 1.1f));
                    vel = walking ? new Vector2(R(-.35f, .35f), R(.35f, .8f)) : new Vector2(0f, R(.05f, .2f));
                    life = R(.45f, .85f); size = R(.7f, 1.2f);
                    break;
            }
            go.transform.position = new Vector3(at.x, at.y, transform.position.z);
            motes.Add(new Mote { r = r, vel = vel, life = life, size = size, twinkle = R(0f, 6f) });
        }

        void OnDestroy() => Clear();
    }
}
