using UnityEngine;

namespace DotRPG
{
    /// <summary>Three connected cuts plus a separate return to idle when input stops.</summary>
    public static class WarriorAttackMotion
    {
        public const int Frames = 24, Stages = 3, RecoveryFrames = 12;
        public const float Duration = .36f, Contact = .36f, RecoveryDuration = .18f;
        public static float StageDuration(int stage) => stage == 2 ? .54f : Duration;
        static readonly float[] Times = { 0, .14f, .26f, Contact, .60f, .82f, 1 };
        static readonly float[][] Load = {
            new[] { 0, -1f, -1.6f, 2.5f, 3, 2f, 1.5f },
            new[] { 1.5f, .5f, 0, 2f, 2.5f, 1f, .5f },
            new[] { .5f, -1.6f, -2f, 3f, 4, 3, 2f } };
        static readonly float[][] Drop = {
            new[] { 0, .6f, 1, .3f, 1.5f, 1.8f, 1.8f },
            new[] { 1.8f, 2, 1.2f, -.5f, -.8f, -.5f, 0 },
            new[] { 0, .6f, 1.5f, 1, 3, 3, 2.5f } };
        static readonly float[][] Turn = {
            new[] { 0, 14f, 26, -22, -30, -26, -24 },
            new[] { -24f, -30, -32, 18, 28, 26, 24 },
            new[] { 24f, 38, 44, -30, -42, -36, -28 } };
        static readonly float[][] Side = {
            new[] { 2f, 3, 5, -3, -5, -4, -4 },
            new[] { -4f, -5, -5, 3, 5, 4, 4 },
            new[] { 4f, 5, 6, -3, -5, -4, -4 } };
        static readonly float[][] Forward = {
            new[] { 0f, -2, -3, 5, 3, 2, 2 },
            new[] { 2f, 1, 0, 5, 3, -1, -2 },
            new[] { -2f, -3, -4, 5, 5, 3, 2 } };
        static readonly float[][] Lift = {
            new[] { 7f, 2, -3, 0, 5, 6, 6 },
            new[] { 6f, 7, 5, -1, -5, -4, -3 },
            new[] { -3f, -4, -5, 0, 6, 8, 8 } };
        static readonly float[][] Step = {
            new[] { 0f, 0, .7f, 3, 3, 3, 3 },
            new[] { 3f, 3, 2, 1, 1, 1, 1 },
            new[] { 1f, 0, -.5f, 4, 4, 4, 4 } };
        static readonly float[][] Balance = {
            new[] { 0f, .3f, .6f, 1, 1, .8f, .8f },
            new[] { .8f, 1, 1, .8f, .7f, .5f, .5f },
            new[] { .5f, .8f, 1, 1, 1, .9f, .8f } };

        public static bool IsRecovery(string frame) => frame.StartsWith("settle");
        public static bool IsSwing(string frame) => frame.StartsWith("swing") || IsRecovery(frame);
        static int Index(string frame) => int.TryParse(frame.Substring(IsRecovery(frame) ? 6 : 5), out int n) ? Mathf.Max(0, n) : 0;
        public static int Stage(string frame) => Mathf.Clamp(Index(frame) / (IsRecovery(frame) ? RecoveryFrames : Frames), 0, 2);
        public static float Progress(string frame) { int count = IsRecovery(frame) ? RecoveryFrames : Frames; return Index(frame) % count / (float)(count - 1); }
        public static string Frame(float t, int stage = 0, bool recovery = false)
        {
            int count = recovery ? RecoveryFrames : Frames;
            int i = Mathf.Clamp(stage, 0, 2) * count + Mathf.Clamp(Mathf.FloorToInt(t * count), 0, count - 1);
            // [P5] Cached names: asked every frame while swinging.
            var keys = recovery ? SettleKeys : SwingKeys;
            if (i < keys.Length) return keys[i] ?? (keys[i] = (recovery ? "settle" : "swing") + i);
            return (recovery ? "settle" : "swing") + i;
        }
        static readonly string[] SwingKeys = new string[64], SettleKeys = new string[64];
        public static bool ReverseCut(Facing facing) => facing == Facing.Right || facing == Facing.UpRight || facing == Facing.DownRight;
        public static Vector2 Project(Vector2 v) => new Vector2(v.x, -v.y * .45f);
        static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        static float Curve(float t, float[] values)
        {
            for (int i = 1; i < Times.Length; i++) if (t <= Times[i])
                return Mathf.Lerp(values[i - 1], values[i], Smooth(Mathf.InverseLerp(Times[i - 1], Times[i], t)));
            return values[values.Length - 1];
        }
        static float Value(float t, float[][] values, int stage, bool recovery) => recovery
            ? Mathf.Lerp(values[stage][6], values[0][0], Smooth(t)) : Curve(t, values[stage]);
        static float Lag(float t, float delay) => Mathf.Clamp01((t - delay) / (1 - delay));
        public static Vector2 BodyPoint(Vector2 point, Facing facing, float t, int waist, int stage = 0, bool recovery = false)
        {
            var f = facing.ToVector(); var right = new Vector2(f.y, -f.x);
            float head = Mathf.InverseLerp(waist - 7, waist - 14, point.y);
            float cloth = Mathf.InverseLerp(waist + 1, waist + 6, point.y);
            float turn = Mathf.Lerp(Value(t, Turn, stage, recovery), Value(Lag(t, .06f), Turn, stage, recovery), head * .8f + cloth * .2f);
            if (ReverseCut(facing)) turn = -turn;
            float chest = Mathf.Clamp01((waist + 4 - point.y) / 8f);
            var shift = Project(f * Value(t, Load, stage, recovery)) + new Vector2(0, Value(t, Drop, stage, recovery));
            shift += Project(right * (turn / 12f)) * Mathf.Lerp(.2f, 1, chest) * (1 - head * .65f);
            shift += Project(f * Value(Lag(t, .04f), Load, stage, recovery)) * head * .3f;
            float scale = 1 - Mathf.Abs(turn) / 300f * (1 - head);
            return new Vector2(32 + (point.x - 32) * scale, point.y) + shift;
        }
        public static void BlitBody(PixelCanvas target, PixelCanvas source, Facing facing, float t, int waist, int stage = 0, bool recovery = false)
        {
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                var destination = new Vector2(x, y); var sample = destination;
                for (int i = 0; i < 3; i++) sample += destination - BodyPoint(sample, facing, t, waist, stage, recovery);
                int sx = Mathf.RoundToInt(sample.x), sy = Mathf.RoundToInt(sample.y);
                if (sy >= waist + 6) continue;
                var p = source.Get(sx, sy);
                if (p.a > 0) target.Set(x, y, p);
            }
        }
        public static Vector2 RightReach(Facing facing, float t, int stage = 0, bool recovery = false)
        {
            var f = facing.ToVector(); var r = new Vector2(f.y, -f.x);
            float side = Value(t, Side, stage, recovery);
            // Preserve the same idle right hand while reversing the eastward cut.
            if (ReverseCut(facing)) side = -side + (stage == 0 && !recovery ? 4 * (1 - Smooth(t / .14f)) : recovery ? 4 * Smooth(t) : 0);
            return Project(r * side + f * Value(t, Forward, stage, recovery)) + new Vector2(0, Value(t, Lift, stage, recovery));
        }
        public static Vector2 LeftReach(Facing facing, float t, int stage = 0, bool recovery = false)
        {
            var f = facing.ToVector(); var r = new Vector2(f.y, -f.x);
            float balance = Value(t, Balance, stage, recovery);
            return Project(-r * (2 + balance * 2) - f * Value(t, Forward, stage, recovery) * .55f) + new Vector2(0, 7 + balance * 2);
        }
        public static float SwordAngle(Facing facing, float t, float rest, int stage = 0, bool recovery = false)
        {
            var f = facing.ToVector(); var right = new Vector2(f.y, -f.x);
            float outside = Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
            float sign = ReverseCut(facing) ? -1 : 1;
            float windup = rest + Mathf.DeltaAngle(rest, outside + (ReverseCut(facing) ? 225 : -45));
            float low = windup + sign * 215, high = low - sign * 215;
            float[] angles = stage == 0 ? new[] { rest, Mathf.Lerp(rest, windup, .6f), windup, windup + sign * 135, low, low, low }
                : stage == 1 ? new[] { low, low + sign * 15, low + sign * 20, low - sign * 125, high, high, high }
                : new[] { high, high - sign * 55, high - sign * 90, high + sign * 135, low, low + sign * 12, low + sign * 12 };
            float end = angles[6];
            return recovery ? end + Mathf.DeltaAngle(end, rest) * Smooth(t) : Curve(t, angles);
        }
        public static float LeadStep(float t, int stage = 0, bool recovery = false) => Value(t, Step, stage, recovery);
        public static float FootLift(float t, int stage = 0, bool recovery = false)
        {
            if (recovery) return Mathf.Sin(t * Mathf.PI) * 1.8f;
            return t > .14f && t < Contact ? Mathf.Sin(Mathf.InverseLerp(.14f, Contact, t) * Mathf.PI) * (stage == 2 ? 3 : 2) : 0;
        }
        public static float TrailAlpha(float t) => t < .26f || t > .82f ? 0 : Mathf.Sin(Mathf.InverseLerp(.26f, .82f, t) * Mathf.PI) * .95f;
    }
}
