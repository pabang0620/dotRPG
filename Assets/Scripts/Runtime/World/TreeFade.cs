using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// See-through trees. While the player stands behind a tree (the tree is drawn in front of the
    /// character and its crown or trunk covers a good part of the body) the whole tree fades to
    /// semi-transparent, and it turns solid again once the player steps out. Purely visual: only the
    /// trunk base ever blocks movement (see WorldBuilder.HdTree).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreeFade : MonoBehaviour
    {
        /// <summary>Opacity of a tree the player is standing behind.</summary>
        public const float FadedAlpha = 0.45f;
        /// <summary>Share of the body sample points the tree's pixels must cover before it fades.</summary>
        const float CoverToFade = 0.2f;
        /// <summary>Opacity change per second: solid to faded in about an eighth of a second.</summary>
        const float FadeSpeed = 4.5f;
        /// <summary>A tree pixel covers the body from this alpha up (the soft contact shadow does not).</summary>
        const float SolidAlpha = 0.5f;

        static readonly List<TreeFade> active = new List<TreeFade>();

        // Points on the character's body relative to its feet, in world units. The 32px character is one
        // tile wide and 1.25 tiles tall; its visible body is roughly 0.6 x 1.1 tiles of that.
        static readonly Vector2[] bodyPoints = BuildBodyPoints();

        SpriteRenderer main;        // the tree picture whose pixels are tested (trunk + crown)
        SpriteRenderer[] renderers; // everything faded together
        float alpha = 1f;

        /// <summary>Current opacity (1 = solid).</summary>
        public float Alpha => alpha;

        /// <summary>Makes a tree see-through while the player is behind it. Safe to call again.</summary>
        public static TreeFade Attach(GameObject tree)
        {
            var fade = tree.GetComponent<TreeFade>();
            if (fade == null) fade = tree.AddComponent<TreeFade>();
            fade.renderers = tree.GetComponentsInChildren<SpriteRenderer>(true);
            // Choppable trees keep their picture on a "Visual" child; the others on the root.
            fade.main = tree.GetComponent<SpriteRenderer>();
            if (fade.main == null && fade.renderers.Length > 0) fade.main = fade.renderers[0];
            return fade;
        }

        void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        void OnDisable()
        {
            active.Remove(this);
            SetAlpha(1f);
        }

        static Vector2[] BuildBodyPoints()
        {
            var points = new List<Vector2>();
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    points.Add(new Vector2(-0.22f + col * 0.22f, 0.12f + row * 0.22f));
            return points.ToArray();
        }

        /// <summary>
        /// Once per frame (PlayerController.LateUpdate): fades the trees that stand in front of the player
        /// and cover its body, and brings the others back to solid.
        /// </summary>
        public static void UpdateAll(Vector2 feet, SpriteRenderer body, float deltaTime)
        {
            bool visible = body != null && body.enabled && !body.forceRenderingOff && body.gameObject.activeInHierarchy;
            var boxMin = feet + new Vector2(-0.3f, 0.05f);
            var boxMax = feet + new Vector2(0.3f, 1.15f);
            float step = FadeSpeed * deltaTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var tree = active[i];
                if (tree == null) { active.RemoveAt(i); continue; }
                // A hidden player (clean map renders, cut-scenes) sees through nothing: snap back to solid.
                if (!visible) { if (tree.alpha != 1f) tree.SetAlpha(1f); continue; }
                float target = tree.Covers(feet, boxMin, boxMax, body) ? FadedAlpha : 1f;
                if (tree.alpha == target) continue;
                tree.SetAlpha(Mathf.MoveTowards(tree.alpha, target, step));
            }
        }

        bool Covers(Vector2 feet, Vector2 boxMin, Vector2 boxMax, SpriteRenderer body)
        {
            if (main == null || !main.enabled || main.sprite == null) return false;
            var b = main.bounds;
            if (b.max.x < boxMin.x || b.min.x > boxMax.x || b.max.y < boxMin.y || b.min.y > boxMax.y) return false;
            // Only a tree drawn over the character can hide it (depth sorting puts the character in
            // front of every tree whose base is further north).
            if (main.sortingLayerID == body.sortingLayerID)
            {
                if (main.sortingOrder <= body.sortingOrder) return false;
            }
            else if (SortingLayer.GetLayerValueFromID(main.sortingLayerID) < SortingLayer.GetLayerValueFromID(body.sortingLayerID)) return false;

            int covered = 0, needed = Mathf.CeilToInt(bodyPoints.Length * CoverToFade);
            for (int i = 0; i < bodyPoints.Length; i++)
                if (Opaque(main, feet + bodyPoints[i]) && ++covered >= needed) return true;
            return false;
        }

        /// <summary>Is the tree picture solid at this world point?</summary>
        static bool Opaque(SpriteRenderer sr, Vector2 world)
        {
            var sprite = sr.sprite;
            Vector3 local = sr.transform.InverseTransformPoint(world);
            float ppu = sprite.pixelsPerUnit;
            float x = (sr.flipX ? -local.x : local.x) * ppu + sprite.pivot.x;
            float y = (sr.flipY ? -local.y : local.y) * ppu + sprite.pivot.y;
            var rect = sprite.rect;
            if (x < 0f || y < 0f || x >= rect.width || y >= rect.height) return false;
            var texture = sprite.texture;
            if (!texture.isReadable) return true; // no pixel data: the sprite's box counts as solid
            return texture.GetPixel((int)(rect.x + x), (int)(rect.y + y)).a >= SolidAlpha;
        }

        void SetAlpha(float value)
        {
            alpha = value;
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var c = r.color;
                c.a = value;
                r.color = c;
            }
        }
    }
}
