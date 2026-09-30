using UnityEngine;

namespace DotRPG
{
    /// <summary>Body-local anatomical arms. Camera mirroring never changes handedness.</summary>
    public static class WarriorRightHandRig
    {
        public struct Pose
        {
            public Vector2 rightShoulder, rightElbow, rightHand, leftShoulder, leftElbow, leftHand;
            public bool rightBack, leftBack, rightHandBack;
            public float swordAngle;
        }
        static readonly float[] Side = { 2.5f, 4f, -3f, -6f, -2f, 2f };
        static readonly float[] Forward = { -1f, -3f, 5f, 4f, 0f, 0f };
        static readonly float[] Lift = { -3f, -4f, -1f, 2f, 3f, 5f };
        static readonly float[] Sweep = { -120f, -155f, -15f, 75f, 130f, 0f };
        static Vector2 Project(Vector2 ground) => new Vector2(ground.x, -ground.y * .45f);
        static Vector2 Snap(Vector2 p) => new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
        static void Solve(Vector2 root, ref Vector2 hand, float bend, out Vector2 elbow)
        {
            const float length = 5f;
            Vector2 d = hand - root;
            if (d.magnitude > length * 1.95f) d = d.normalized * length * 1.95f;
            if (d.magnitude < 1f) d = Vector2.down;
            hand = Snap(root + d);
            var middle = root + d * .5f;
            var normal = new Vector2(-d.y, d.x).normalized;
            elbow = Snap(middle + normal * Mathf.Sqrt(Mathf.Max(0, length * length - d.sqrMagnitude * .25f)) * bend);
        }
        public static Pose Sample(Facing facing, string frame, int waist)
        {
            Vector2 forward = facing.ToVector();
            // World right is a clockwise quarter turn from forward. South -> screen left.
            Vector2 right = new Vector2(forward.y, -forward.x);
            // The bob ends just above the collar. Keep shoulder/sleeve pixels below the face.
            Vector2 center = new Vector2(32, waist - 2);
            var p = new Pose();
            p.rightShoulder = Snap(center + Project(right * 6));
            p.leftShoulder = Snap(center - Project(right * 6));
            float wave = frame.StartsWith("walk") ? -WarriorGait.Stride(WarriorGait.Index(frame)) * .8f : 0;
            p.rightHand = p.rightShoulder + Project(right * 2 + forward * wave) + new Vector2(0, 7 - Mathf.Abs(wave) * .2f);
            p.leftHand = p.leftShoulder + Project(-right * 2 - forward * wave) + new Vector2(0, 7 - Mathf.Abs(wave) * .2f);
            p.rightBack = forward.x * .8f - forward.y * .35f < 0;
            p.leftBack = -forward.x * .8f - forward.y * .35f < 0;
            p.rightHandBack = p.rightBack;
            float rest = Mathf.Atan2(.7f, right.x * .75f + forward.x * .25f) * Mathf.Rad2Deg;
            p.swordAngle = rest + wave * 2f;
            if (WarriorAttackMotion.IsSwing(frame))
            {
                float t = WarriorAttackMotion.Progress(frame);
                p.rightShoulder = Snap(WarriorAttackMotion.BodyPoint(p.rightShoulder, facing, t, waist));
                p.leftShoulder = Snap(WarriorAttackMotion.BodyPoint(p.leftShoulder, facing, t, waist));
                p.rightHand = p.rightShoulder + WarriorAttackMotion.RightReach(facing, t);
                p.leftHand = p.leftShoulder + WarriorAttackMotion.LeftReach(facing, t);
                if (t >= .32f && t <= .7f && forward.y < .5f) p.rightHandBack = false;
                p.swordAngle = WarriorAttackMotion.SwordAngle(facing, t, rest);
            }
            else if (frame.StartsWith("attack") && frame.Length > 6)
            {
                int i = Mathf.Clamp(frame[6] - '0', 0, 5);
                p.rightHand = p.rightShoulder + Project(right * Side[i] + forward * Forward[i]) + new Vector2(0, Lift[i]);
                // The empty left arm counterbalances; it never becomes the sword hand.
                p.leftHand += Project(-forward * (i == 2 || i == 3 ? 1.5f : 0));
                if ((i == 2 || i == 3) && forward.y < .5f) p.rightHandBack = false;
                p.swordAngle = i == 5 ? rest : Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg + Sweep[i];
            }
            Solve(p.rightShoulder, ref p.rightHand, right.x <= 0 ? 1 : -1, out p.rightElbow);
            Solve(p.leftShoulder, ref p.leftHand, right.x <= 0 ? -1 : 1, out p.leftElbow);
            return p;
        }
        public static Vector2 WorldHand(Pose p) => new Vector2(p.rightHand.x - 32, 58 - p.rightHand.y) / SilverWarriorArt.Ppu;
        internal static void Stroke(PixelCanvas c, Vector2 a, Vector2 b, int width, Color32 color)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                c.Rect(Mathf.RoundToInt(p.x) - width / 2, Mathf.RoundToInt(p.y) - width / 2, width, width, color);
            }
        }
        static void Upper(PixelCanvas c, Vector2 shoulder, Vector2 elbow, CharacterLook look)
        {
            Stroke(c, shoulder, elbow, 6, PixelCanvas.Hex("#101116"));
            var cloth = look.armor == ArmorStyle.None ? PixelCanvas.Hex("#23242a") : look.armorColor;
            Stroke(c, shoulder, elbow, 4, cloth);
        }
        static void Lower(PixelCanvas c, Vector2 elbow, Vector2 hand, CharacterLook look)
        {
            // A bell sleeve follows the forearm; a small bare hand protrudes at its cuff.
            var cuff = Vector2.Lerp(elbow, hand, .65f);
            var cloth = look.armor == ArmorStyle.None ? PixelCanvas.Hex("#23242a") : look.armorColor;
            Stroke(c, elbow, cuff, 7, PixelCanvas.Hex("#101116"));
            Stroke(c, elbow, cuff, 5, cloth);
            Stroke(c, elbow + new Vector2(-1, 0), cuff + new Vector2(-1, 0), 1, PixelCanvas.Shade(cloth, 1.45f));
            Stroke(c, cuff, hand, 3, PixelCanvas.Hex("#dfb6b1"));
            c.Rect((int)hand.x - 1, (int)hand.y - 1, 3, 3, PixelCanvas.Hex("#f5d6c1"));
        }
        public static void Draw(PixelCanvas c, Pose p, CharacterLook look, bool behind)
        {
            if (p.leftBack == behind) { Upper(c, p.leftShoulder, p.leftElbow, look); Lower(c, p.leftElbow, p.leftHand, look); }
            if (p.rightBack == behind) Upper(c, p.rightShoulder, p.rightElbow, look);
            if (p.rightHandBack == behind) Lower(c, p.rightElbow, p.rightHand, look);
        }
    }
}
