using UnityEngine;

namespace DotRPG
{
    public enum Facing
    {
        Down,
        Up,
        Left,
        Right,
    }

    public static class FacingExtensions
    {
        public static Vector2 ToVector(this Facing facing)
        {
            switch (facing)
            {
                case Facing.Up: return Vector2.up;
                case Facing.Left: return Vector2.left;
                case Facing.Right: return Vector2.right;
                default: return Vector2.down;
            }
        }

        /// <summary>Picks the dominant axis of a direction. Returns <paramref name="fallback"/> for ~zero input.</summary>
        public static Facing FromVector(Vector2 v, Facing fallback)
        {
            if (v.sqrMagnitude < 0.0001f) return fallback;
            if (Mathf.Abs(v.x) > Mathf.Abs(v.y) + 0.01f) return v.x > 0 ? Facing.Right : Facing.Left;
            return v.y > 0 ? Facing.Up : Facing.Down;
        }

        /// <summary>Key used by sprite lookups. Left/Right share the "side" art (Left is flipped).</summary>
        public static string SpriteKey(this Facing facing)
        {
            switch (facing)
            {
                case Facing.Up: return "up";
                case Facing.Left:
                case Facing.Right: return "side";
                default: return "down";
            }
        }
    }
}
