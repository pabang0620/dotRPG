using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [NINJA] One 분신술 clone (Docs/PLAN_CLASS_ROGUE.md 2.4): the body's own frame drawn see-through (alpha 0.55) with a
    /// grey tint, following 1.2 m to one side of the body. No HP, never hit, never taunts; its throws are cast by
    /// <see cref="CareerCombat"/> as the body's own damage.
    /// </summary>
    public sealed class NinjaClone : MonoBehaviour
    {
        public const float SideOffset = 1.2f;
        /// <summary>How fast the clone catches up with its place beside the body (per second).</summary>
        const float FollowRate = 9f;
        static readonly Color Tint = new Color(.72f, .74f, .78f, .55f); // [2026-10-23] plain grey (was navy)

        PlayerController owner;
        SpriteRenderer sr, body;
        public int Side { get; private set; }
        /// <summary>Feet position.</summary>
        public Vector2 Position => transform.position;
        public Vector2 Center => Position + new Vector2(0f, .45f);

        public static NinjaClone Spawn(PlayerController owner, int side)
        {
            var go = new GameObject("NinjaClone");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var c = go.AddComponent<NinjaClone>();
            c.owner = owner;
            c.Side = side;
            c.body = owner.GetComponent<CharacterAnimator>()?.Body;
            c.sr = go.AddComponent<SpriteRenderer>();
            c.sr.color = Tint;
            go.transform.position = c.Home;
            Puff(c.Center);
            c.Copy();
            return c;
        }

        /// <summary>Where the clone stands: beside the body, left or right.</summary>
        Vector2 Home => owner.Position + new Vector2(Side * SideOffset, 0f);

        /// <summary>Puts the clone straight on a spot (swap, teleport) and lets it catch up from there.</summary>
        public void Snap(Vector2 feet) => transform.position = feet;

        /// <summary>Back beside the body at once (the body teleported: the clones come along).</summary>
        public void SnapHome() => transform.position = Home;

        public void Dismiss()
        {
            Puff(Center);
            if (this != null) Destroy(gameObject);
        }

        public static void Puff(Vector2 at) =>
            CareerFx.Gen("n_puff", null, at, Vector2.zero, 1.1f, 0f, 16f, VfxLayer.Top, false);

        void LateUpdate()
        {
            if (owner == null || owner.IsDead) { Destroy(gameObject); return; }
            Vector2 to = Home;
            transform.position = Vector2.Lerp(Position, to, 1f - Mathf.Exp(-FollowRate * Time.deltaTime));
            Copy();
        }

        /// <summary>The body's current frame (same motion as the body, so the clone visibly "does the same").</summary>
        void Copy()
        {
            if (body == null) return;
            sr.sprite = body.sprite;
            sr.flipX = body.flipX;
            transform.localScale = body.transform.lossyScale;
            sr.sortingOrder = body.sortingOrder - 1;
        }
    }
}
