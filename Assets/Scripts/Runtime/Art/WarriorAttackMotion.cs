using UnityEngine;

namespace DotRPG
{
    /// <summary>Shared full-body attack timeline. Visual offsets never move the collision body.</summary>
    public static class WarriorAttackMotion
    {
        public const int Frames = 24;
        public const float Duration = .5f;
        public const float Contact = .44f;
        static readonly float[] Times = { 0, .14f, .26f, Contact, .60f, .82f, 1 };
        static readonly float[] Load = { 0, -1.1f, -2, 2.5f, 3, .9f, 0 };
        static readonly float[] Drop = { 0, .8f, 1.8f, .4f, 1.1f, .3f, 0 };
        static readonly float[] Turn = { 0, 14, 26, -22, -30, -8, 0 };
        static readonly float[] ReachSide = { 2, 3, 5, -3, -5, -1, 2 };
        static readonly float[] ReverseReachSide = { 2, -2, -5, 3, 5, 3, 2 };
        static readonly float[] ReachForward = { 0, -2, -3, 5, 3, 1, 0 };
        static readonly float[] ReachLift = { 7, 2, -3, 0, 3, 5, 7 };
        static readonly float[] Step = { 0, 0, .7f, 3, 3, 2, 0 };
        public static bool IsSwing(string frame) => frame.StartsWith("swing");
        public static bool ReverseCut(Facing facing) => facing == Facing.Right || facing == Facing.UpRight || facing == Facing.DownRight;
        public static float Progress(string frame) => IsSwing(frame) && int.TryParse(frame.Substring(5), out int n) ? Mathf.Clamp01(n / (float)(Frames - 1)) : 0;
        public static string Frame(float t) => "swing" + Mathf.Clamp(Mathf.FloorToInt(t * Frames), 0, Frames - 1);
        public static Vector2 Project(Vector2 v) => new Vector2(v.x, -v.y * .45f);
        static float Curve(float t, float[] values)
        {
            for (int i = 1; i < Times.Length; i++) if (t <= Times[i])
            {
                float u = Mathf.InverseLerp(Times[i - 1], Times[i], t);
                return Mathf.Lerp(values[i - 1], values[i], u * u * (3 - 2 * u));
            }
            return values[values.Length - 1];
        }
        static float Lag(float t, float delay) => Mathf.Clamp01((t - delay) / (1 - delay));
        public static Vector2 BodyPoint(Vector2 point, Facing facing, float t, int waist)
        {
            var f = facing.ToVector(); var right = new Vector2(f.y, -f.x);
            // Head follows the chest; the skirt settles a little later than the pelvis.
            float head = Mathf.InverseLerp(waist - 7, waist - 14, point.y);
            float cloth = Mathf.InverseLerp(waist + 1, waist + 6, point.y);
            float turn = Mathf.Lerp(Curve(t, Turn), Curve(Lag(t, .06f), Turn), head * .8f + cloth * .2f);
            if (ReverseCut(facing)) turn = -turn;
            float chest = Mathf.Clamp01((waist + 4 - point.y) / 8f);
            var shift = Project(f * Curve(t, Load)) + new Vector2(0, Curve(t, Drop));
            shift += Project(right * (turn / 12f)) * Mathf.Lerp(.2f, 1, chest) * (1 - head * .65f);
            shift += Project(f * Curve(Lag(t, .04f), Load)) * head * .3f;
            float scale = 1 - Mathf.Abs(turn) / 300f * (1 - head);
            return new Vector2(32 + (point.x - 32) * scale, point.y) + shift;
        }
        public static void BlitBody(PixelCanvas target, PixelCanvas source, Facing facing, float t, int waist)
        {
            // Inverse nearest-neighbor sampling preserves hard pixel edges and avoids holes.
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                var destination = new Vector2(x, y); var sample = destination;
                for (int i = 0; i < 3; i++) sample += destination - BodyPoint(sample, facing, t, waist);
                int sx = Mathf.RoundToInt(sample.x), sy = Mathf.RoundToInt(sample.y);
                if (sy >= waist + 6) continue;
                var p = source.Get(sx, sy);
                if (p.a > 0) target.Set(x, y, p);
            }
        }
        public static Vector2 RightReach(Facing facing, float t)
        {
            var f = facing.ToVector(); var r = new Vector2(f.y, -f.x);
            return Project(r * Curve(t, ReverseCut(facing) ? ReverseReachSide : ReachSide) + f * Curve(t, ReachForward)) + new Vector2(0, Curve(t, ReachLift));
        }
        public static Vector2 LeftReach(Facing facing, float t)
        {
            var f = facing.ToVector(); var r = new Vector2(f.y, -f.x);
            float balance = Mathf.Sin(t * Mathf.PI);
            float handHeight = ReverseCut(facing) ? 7 + balance : 7 - balance * 2;
            return Project(-r * (2 + balance * 2) - f * Curve(t, ReachForward) * .55f) + new Vector2(0, handHeight);
        }
        public static float SwordAngle(Facing facing, float t, float rest)
        {
            var f = facing.ToVector(); var right = new Vector2(f.y, -f.x);
            float outside = Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
            // Eastward views use the opposite cut; timing and the anatomical sword hand stay fixed.
            float sign = ReverseCut(facing) ? -1 : 1;
            float windup = rest + Mathf.DeltaAngle(rest, outside + (ReverseCut(facing) ? 225 : -45));
            float strike = windup + sign * 135, follow = windup + sign * 215;
            float finish = follow + Mathf.DeltaAngle(follow, rest);
            return Curve(t, new[] { rest, Mathf.Lerp(rest, windup, .6f), windup, strike, follow, Mathf.Lerp(follow, finish, .75f), finish });
        }
        public static float LeadStep(float t) => Curve(t, Step);
        public static float FootLift(float t)
        {
            if (t > .14f && t < Contact) return Mathf.Sin(Mathf.InverseLerp(.14f, Contact, t) * Mathf.PI) * 2;
            if (t > .82f && t < 1) return Mathf.Sin(Mathf.InverseLerp(.82f, 1, t) * Mathf.PI) * 1.3f;
            return 0;
        }
    }
}
