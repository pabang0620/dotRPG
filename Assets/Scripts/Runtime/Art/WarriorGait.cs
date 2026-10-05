using UnityEngine;

namespace DotRPG
{
    /// <summary>Distance-driven eight-phase gait. Each foot has a planted and a lifted half-cycle.</summary>
    public static class WarriorGait
    {
        public const int Frames = 8;
        // Stance sweeps 14.4 pixels during half a cycle: 28.8 / 36 world units per cycle.
        public const float CycleDistance = .8f;
        public struct Leg { public Vector2 hip, knee, foot; public float lift, stride; }
        public static int Index(string frame) => frame.StartsWith("walk") && int.TryParse(frame.Substring(4), out int i) ? i % Frames : 0;
        public static float Stride(int phase) => phase < 4 ? 7.2f - phase * 3.6f : -7.2f + (phase - 4) * 3.6f;
        static Vector2 Project(Vector2 v) => new Vector2(v.x, -v.y * .45f);
        public static Leg Sample(Facing facing, string frame, int waist, bool rightLeg)
        {
            var f = facing.ToVector(); var right = new Vector2(f.y, -f.x);
            float sign = rightLeg ? 1 : -1;
            int phase = (Index(frame) + (rightLeg ? 0 : 4)) % Frames;
            bool walk = frame.StartsWith("walk");
            float stride = walk ? Stride(phase) : 0;
            float lift = walk && phase > 4 ? Mathf.Sin((phase - 4) * Mathf.PI / 4) * 4.5f : 0;
            bool attack = frame.StartsWith("attack");
            var spread = right * sign * (attack ? 4 : 3);
            // Profile torso/obi is centered at x=34, independently of the head pivot.
            // Its reflected center is 63-34=29. Translate the entire leg chain together.
            float centerX = facing == Facing.Right ? 34 : facing == Facing.Left ? 29 : 32;
            var hip = new Vector2(centerX, waist + 3) + Project(right * sign * 3);
            var foot = new Vector2(centerX, 56) + Project(spread + f * stride) - new Vector2(0, lift);
            var knee = Vector2.Lerp(hip, foot, .53f) + Project(f * lift * .65f);
            if (walk)
            {
                hip = WarriorLocomotion.BodyPoint(hip, facing, frame, waist);
                knee = WarriorLocomotion.Knee(hip, foot, f, lift);
            }
            if (WarriorAttackMotion.IsSwing(frame))
            {
                float t = WarriorAttackMotion.Progress(frame);
                int stage = WarriorAttackMotion.Stage(frame); bool recovery = WarriorAttackMotion.IsRecovery(frame);
                hip = WarriorAttackMotion.BodyPoint(hip, facing, t, waist, stage, recovery);
                // Right foot remains planted as a pivot; left foot steps into the cut.
                lift = rightLeg ? 0 : WarriorAttackMotion.FootLift(t, stage, recovery);
                if (!rightLeg) foot += Project(f * WarriorAttackMotion.LeadStep(t, stage, recovery)) - new Vector2(0, lift);
                knee = Vector2.Lerp(hip, foot, .53f) + Project(f * (Mathf.Sin(t * Mathf.PI) * 1.7f + lift * .6f));
            }
            return new Leg { hip = hip, knee = knee, foot = foot, lift = lift, stride = stride };
        }
        static void DrawLeg(PixelCanvas c, Leg l, CharacterLook look, Vector2 forward, bool far)
        {
            var outline = PixelCanvas.Hex("#101116");
            var stocking = look.skinLeg.a > 0 ? look.skinLeg : look.bottomTier >= 0 ? look.pants : PixelCanvas.Hex("#655861");
            var boot = look.skinBoot.a > 0 ? look.skinBoot : PixelCanvas.Hex("#23242a");
            if (far) stocking = PixelCanvas.Shade(stocking, .75f);
            WarriorRightHandRig.Stroke(c, l.hip, l.knee, 5, outline);
            WarriorRightHandRig.Stroke(c, l.hip, l.knee, 3, stocking);
            WarriorRightHandRig.Stroke(c, l.knee, l.foot, 5, outline);
            WarriorRightHandRig.Stroke(c, l.knee, l.foot, 3, stocking);
            var bootTop = Vector2.Lerp(l.knee, l.foot, .3f);
            WarriorRightHandRig.Stroke(c, bootTop, l.foot, 5, outline);
            WarriorRightHandRig.Stroke(c, bootTop, l.foot, 3, boot);
            var toe = l.foot + new Vector2(forward.x * 2, forward.y < 0 ? 1 : 0);
            WarriorRightHandRig.Stroke(c, l.foot, toe, 4, outline);
            c.Set(Mathf.RoundToInt(bootTop.x), Mathf.RoundToInt(bootTop.y), look.skinTrim.a > 0 ? look.skinTrim : PixelCanvas.Hex("#71797e"));
            c.Set(Mathf.RoundToInt(bootTop.x), Mathf.RoundToInt(bootTop.y) + 2, PixelCanvas.Hex("#4c5057"));
        }
        public static void Draw(PixelCanvas c, Facing facing, string frame, int waist, CharacterLook look)
        {
            var r = Sample(facing, frame, waist, true); var l = Sample(facing, frame, waist, false);
            // Sort on ground position, excluding vertical foot lift.
            bool rightFar = r.foot.y + r.lift < l.foot.y + l.lift;
            DrawLeg(c, rightFar ? r : l, look, facing.ToVector(), true);
            DrawLeg(c, rightFar ? l : r, look, facing.ToVector(), false);
        }
    }
}
