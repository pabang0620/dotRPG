using UnityEngine;

namespace DotRPG
{
    /// <summary>Contact, compression, passing and push-off poses shared by torso and limb roots.</summary>
    public static class WarriorLocomotion
    {
        static readonly float[] Height = { 0, 1.2f, 0, -1.2f, 0, 1.2f, 0, -1.2f };
        public static bool IsWalk(string frame) => frame.StartsWith("walk");
        public static Vector2 BodyPoint(Vector2 point, Facing facing, string frame, int waist)
        {
            if (!IsWalk(frame)) return point;
            int phase = WarriorGait.Index(frame);
            float cycle = phase * Mathf.PI / 4;
            var f = facing.ToVector(); var r = new Vector2(f.y, -f.x);
            float chest = Mathf.Clamp01((waist + 4 - point.y) / 8f);
            float head = Mathf.InverseLerp(waist - 7, waist - 14, point.y);
            float hem = Mathf.InverseLerp(waist + 1, waist + 6, point.y);
            float sway = Mathf.Sin(cycle) * Mathf.Lerp(.7f, -.9f, chest);
            float lean = (1.5f + .4f * Mathf.Sin(cycle * 2)) * chest * (1 - head * .25f);
            var shift = WarriorAttackMotion.Project(r * sway + f * lean);
            shift.y += Height[phase] * (1 - head * .25f);
            // The skirt follows one beat later, while the head stays comparatively level.
            shift += WarriorAttackMotion.Project(-f * Mathf.Sin(cycle - .65f) * hem * .8f);
            return point + shift;
        }
        public static void BlitBody(PixelCanvas target, PixelCanvas source, Facing facing, string frame, int waist)
        {
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                var destination = new Vector2(x, y); var sample = destination;
                for (int i = 0; i < 3; i++) sample += destination - BodyPoint(sample, facing, frame, waist);
                int sx = Mathf.RoundToInt(sample.x), sy = Mathf.RoundToInt(sample.y);
                if (sy >= waist + 6) continue;
                var pixel = source.Get(sx, sy);
                if (pixel.a > 0) target.Set(x, y, pixel);
            }
        }
        public static Vector2 Knee(Vector2 hip, Vector2 foot, Vector2 forward, float lift)
        {
            // Projected two-bone knee: strong flexion in profile, foreshortened from front/back.
            const float thigh = 7.5f, shin = 8;
            var d = foot - hip; float distance = Mathf.Max(.1f, d.magnitude);
            float along = Mathf.Clamp((thigh * thigh - shin * shin + distance * distance) / (2 * distance), 0, distance);
            float bend = Mathf.Sqrt(Mathf.Max(0, thigh * thigh - along * along));
            var normal = new Vector2(-d.y, d.x) / distance;
            if (Vector2.Dot(normal, WarriorAttackMotion.Project(forward)) < 0) normal = -normal;
            float profile = Mathf.Abs(forward.x);
            return hip + d / distance * along + normal * bend * profile
                + WarriorAttackMotion.Project(forward * lift * .5f) * (1 - profile);
        }
    }
}
