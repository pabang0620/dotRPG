using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [NINJA] One thrown kunai (or the top-tier throwing star, Docs/PLAN_CLASS_ROGUE.md 2.4 "닌자 무기 그림"): the picture,
    /// its position and whether it is stuck in the ground. Flight and hits are run by <see cref="CareerCombat"/>; a stuck
    /// kunai that can be picked up again (회수) stays in <see cref="Grounded"/> until it runs out.
    /// </summary>
    public sealed class KunaiProjectile : MonoBehaviour
    {
        /// <summary>Stuck kunai that 회수 can pull back, every owner's (filtered by <see cref="Owner"/>).</summary>
        public static readonly List<KunaiProjectile> Grounded = new List<KunaiProjectile>();

        /// <summary>World width of the drawn kunai / star.</summary>
        // [VFX 2026-10-29] Bigger (were .7 / .55: the steel blades vanished on the ground); hits are judged by CareerCombat's
        // corridor (KunaiHalfWidth), so the picture size changes nothing in play.
        const float KunaiSize = 1f, StarSize = .9f;
        const float TrailGap = .025f, StarSpin = 1080f;

        public PlayerController Owner { get; private set; }
        public bool Stuck { get; private set; }
        public bool Star { get; private set; }
        public Vector2 Position => transform.position;
        /// <summary>When a stuck kunai disappears (0 = flying).</summary>
        public float ExpiresAt { get; private set; }

        SpriteRenderer sr, halo;
        Sprite flying, stuck;
        float trailIn, size = 1f;
        Vector2 dir = Vector2.right;

        /// <summary>
        /// A kunai flying along <paramref name="direction"/> from <paramref name="at"/>. <paramref name="star"/>: the
        /// throwing star of the last weapon art tier (spins in flight). <paramref name="tint"/> is for the clones' copies.
        /// </summary>
        public static KunaiProjectile Spawn(PlayerController owner, Vector2 at, Vector2 direction, bool star, Color? tint = null, float scale = 1f)
        {
            var go = new GameObject(star ? "NinjaStar" : "Kunai");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = at;
            var k = go.AddComponent<KunaiProjectile>();
            k.Owner = owner;
            k.Star = star;
            k.sr = go.AddComponent<SpriteRenderer>();
            k.sr.sortingOrder = SkillFx.TopOrder + 2;
            var frames = VfxLibrary.Get(star ? "n_shuriken" : "n_kunai_v2");
            if (frames.Length == 0) frames = VfxLibrary.Get("n_kunai");
            if (frames.Length > 0)
            {
                k.flying = frames[0];
                k.stuck = frames[Mathf.Min(1, frames.Length - 1)];
            }
            k.sr.sprite = k.flying;
            k.sr.color = tint ?? Color.white;
            // [VFX 2026-10-29] A bright additive copy behind the blade (a glow edge that reads on any ground) and a short trail.
            var haloGo = new GameObject("Halo");
            haloGo.transform.SetParent(go.transform, false);
            haloGo.transform.localScale = Vector3.one * 1.35f;
            k.halo = haloGo.AddComponent<SpriteRenderer>();
            k.halo.sprite = k.flying;
            k.halo.sharedMaterial = FxMaterials.Additive;
            k.halo.color = new Color(1f, 1f, 1f, .5f);
            k.halo.sortingOrder = k.sr.sortingOrder - 1;
            k.size = (star ? StarSize : KunaiSize) * scale;
            float width = k.flying != null ? k.flying.bounds.size.x : 1f;
            go.transform.localScale = Vector3.one * ((star ? StarSize : KunaiSize) * scale / Mathf.Max(.001f, width));
            k.Aim(direction);
            return k;
        }

        /// <summary>Points the flying kunai along <paramref name="direction"/> (the picture faces right).</summary>
        public void Aim(Vector2 direction)
        {
            if (direction.sqrMagnitude < .0001f) return;
            dir = direction.normalized;
            if (!Star) transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        public void Place(Vector2 at) => transform.position = at;

        /// <summary>Lands: the stuck picture (upright, mirrored when thrown left) for <paramref name="seconds"/>.</summary>
        public void Stick(float seconds, bool recallable)
        {
            if (Stuck) return;
            Stuck = true;
            ExpiresAt = Time.time + seconds;
            transform.rotation = Quaternion.identity;
            if (stuck != null) sr.sprite = stuck;
            sr.flipX = dir.x < -.01f;
            sr.sortingOrder = SkillFx.At(Position.y, 1);
            if (halo != null) { halo.sprite = sr.sprite; halo.flipX = sr.flipX; halo.sortingOrder = sr.sortingOrder - 1; halo.color = new Color(1f, 1f, 1f, .3f); }
            if (recallable && !Grounded.Contains(this)) Grounded.Add(this);
        }

        /// <summary>Picked up again (회수): it flies again and can no longer be recalled.</summary>
        public void Lift(Vector2 direction)
        {
            Grounded.Remove(this);
            Stuck = false;
            ExpiresAt = 0f;
            sr.sprite = flying;
            sr.flipX = false;
            sr.sortingOrder = SkillFx.TopOrder + 2;
            if (halo != null) { halo.sprite = flying; halo.flipX = false; halo.sortingOrder = sr.sortingOrder - 1; halo.color = new Color(1f, 1f, 1f, .5f); }
            Aim(direction);
        }

        public void Vanish()
        {
            Grounded.Remove(this);
            if (this != null) Destroy(gameObject);
        }

        void Update()
        {
            if (Stuck)
            {
                if (Time.time >= ExpiresAt || Owner == null) Vanish();
                return;
            }
            if (Star) transform.Rotate(0f, 0f, -StarSpin * Time.deltaTime);
            trailIn -= Time.deltaTime;
            if (trailIn <= 0f)
            {
                trailIn = TrailGap;
                var c = sr.color;
                float s = size * .75f;
                SkillFx.Spawn("fx_glow", Position, new Color(Mathf.Lerp(c.r, 1f, .4f), Mathf.Lerp(c.g, 1f, .4f), Mathf.Lerp(c.b, 1f, .4f), .6f), .18f, SkillFx.TopOrder + 1)
                    .Additive().Scale(new Vector2(s, s), new Vector2(s * .25f, s * .25f));
            }
        }

        void OnDestroy() => Grounded.Remove(this);
    }
}
