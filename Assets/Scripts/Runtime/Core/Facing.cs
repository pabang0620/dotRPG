using UnityEngine;

namespace DotRPG
{
    /// <summary>Eight facings. The first four keep their old values so saved data stays valid.</summary>
    public enum Facing
    {
        Down,
        Up,
        Left,
        Right,
        DownLeft,
        DownRight,
        UpLeft,
        UpRight,
    }

    public static class FacingExtensions
    {
        const float Diagonal = 0.70710678f;

        public static Vector2 ToVector(this Facing facing)
        {
            switch (facing)
            {
                case Facing.Up: return Vector2.up;
                case Facing.Left: return Vector2.left;
                case Facing.Right: return Vector2.right;
                case Facing.DownLeft: return new Vector2(-Diagonal, -Diagonal);
                case Facing.DownRight: return new Vector2(Diagonal, -Diagonal);
                case Facing.UpLeft: return new Vector2(-Diagonal, Diagonal);
                case Facing.UpRight: return new Vector2(Diagonal, Diagonal);
                default: return Vector2.down;
            }
        }

        static readonly Facing[] BySector =
        {
            Facing.Right, Facing.UpRight, Facing.Up, Facing.UpLeft, Facing.Left, Facing.DownLeft, Facing.Down, Facing.DownRight,
        };

        /// <summary>Nearest of the 8 directions. Returns <paramref name="fallback"/> for ~zero input.</summary>
        public static Facing FromVector(Vector2 v, Facing fallback)
        {
            if (v.sqrMagnitude < 0.0001f) return fallback;
            int sector = Mathf.RoundToInt(Mathf.Atan2(v.y, v.x) / (Mathf.PI / 4f));
            return BySector[(sector % 8 + 8) % 8];
        }

        /// <summary>Faces left (the renderer flips the right-facing art).</summary>
        public static bool IsLeft(this Facing f) => f == Facing.Left || f == Facing.DownLeft || f == Facing.UpLeft;

        /// <summary>Faces away from the camera (weapons go behind the body).</summary>
        public static bool IsUp(this Facing f) => f == Facing.Up || f == Facing.UpLeft || f == Facing.UpRight;

        /// <summary>
        /// Key used by sprite lookups: down, up, side, downside (3/4 front), upside (3/4 back).
        /// Left-facing views share the right-facing art and are flipped.
        /// </summary>
        public static string SpriteKey(this Facing facing)
        {
            switch (facing)
            {
                case Facing.Up: return "up";
                case Facing.Left:
                case Facing.Right: return "side";
                case Facing.DownLeft:
                case Facing.DownRight: return "downside";
                case Facing.UpLeft:
                case Facing.UpRight: return "upside";
                default: return "down";
            }
        }
    }
}
