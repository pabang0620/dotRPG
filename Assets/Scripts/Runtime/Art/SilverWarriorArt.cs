using System;
using UnityEngine;

namespace DotRPG
{
    [Serializable] public class SilverFrame
    {
        public string clip, direction;
        public int column, row, waist;
        public Vector2 hand;
    }
    [Serializable] public class SilverAtlas
    {
        public int size = 64;
        public float ppu = 36;
        public Vector2 pivot = new Vector2(32, 6);
        public SilverFrame[] frames;
    }

    /// <summary>Generated warrior body with deterministic, pose-aligned clothing masks.
    /// Only player/player_t* use this art; NPCs and the mage keep their original renderer.</summary>
    public static class SilverWarriorArt
    {
        public const int Size = 64;
        public const float Ppu = 36;
        public const float WeaponScale = 0.92f;
        public static readonly string[] Directions = { "down", "downside", "side", "upside", "up" };
        public static readonly string[] BaseFrames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt" };
        static SilverAtlas data;
        static Texture2D bodySheet, attackSheet;
        public static bool Supports(string id) => id == "player" || (id != null && id.StartsWith("player_", StringComparison.Ordinal));
        public static SilverAtlas Data
        {
            get
            {
                if (data == null)
                {
                    var json = Resources.Load<TextAsset>("SilverWarrior/frames");
                    if (json == null) throw new InvalidOperationException("Silver warrior sheets have not been baked.");
                    data = JsonUtility.FromJson<SilverAtlas>(json.text);
                    bodySheet = Resources.Load<Texture2D>("SilverWarrior/body");
                    attackSheet = Resources.Load<Texture2D>("SilverWarrior/attack");
                }
                return data;
            }
        }
        public static void ResetCache() { data = null; bodySheet = attackSheet = null; }
        public static SilverFrame Frame(string direction, string frame)
        {
            direction = Canonical(direction);
            if (WarriorAttackMotion.IsSwing(frame))
            {
                var rest = Frame(direction, "idle0");
                return new SilverFrame { direction = direction, clip = frame, column = rest.column,
                    row = rest.row, waist = rest.waist, hand = rest.hand };
            }
            if (frame.StartsWith("walk"))
            {
                // Use one stable source; locomotion deforms the torso and every limb root together.
                var rest = Frame(direction, "idle0");
                return new SilverFrame { direction = direction, clip = frame, column = rest.column,
                    row = rest.row, waist = rest.waist, hand = rest.hand };
            }
            foreach (var f in Data.frames) if (f.direction == direction && f.clip == frame) return f;
            throw new ArgumentException("Unknown silver warrior frame: " + direction + "/" + frame);
        }
        public static Vector2 Hand(string direction, string frame)
        {
            return WarriorRightHandRig.WorldHand(Pose(direction, frame));
        }
        public static string ViewKey(Facing f) => f == Facing.DownLeft ? "downleft" : f == Facing.DownRight ? "downright" : f == Facing.Left ? "left" : f == Facing.UpLeft ? "upleft" : f.SpriteKey();
        public static string Canonical(string key) => key == "downleft" || key == "downright" ? "downside" : key == "left" ? "side" : key == "upleft" ? "upside" : key;
        public static Facing FacingOf(string key)
        {
            switch (key)
            {
                case "downside": case "downleft": return Facing.DownLeft;
                case "downright": return Facing.DownRight;
                case "side": return Facing.Right;
                case "left": return Facing.Left;
                case "upside": return Facing.UpRight;
                case "upleft": return Facing.UpLeft;
                case "up": return Facing.Up;
                default: return Facing.Down;
            }
        }
        public static WarriorRightHandRig.Pose Pose(string direction, string frame) => WarriorRightHandRig.Sample(FacingOf(direction), frame, Frame(direction, frame).waist);
        public static bool Behind(string direction) => Pose(direction, "idle0").rightHandBack;
        // This warrior's lower-diagonal source faces the opposite side of the engine convention.
        // Swap only its SE/SW visual mapping; world movement and other characters stay unchanged.
        public static bool Mirror(Facing facing) => facing == Facing.DownRight || (facing.IsLeft() && facing != Facing.DownLeft);
        public static int AttackIndex(float t) => t < .16f ? 0 : t < .30f ? 1 : t < .44f ? 2 : t < .60f ? 3 : t < .80f ? 4 : 5;
        public static float Angle(string direction, string frame)
        {
            return Pose(direction, frame).swordAngle;
        }
        public static Color32[] Pixels(SilverFrame f)
        {
            var unused = Data;
            bool attack = f.clip.StartsWith("attack") && f.clip.Length > 6;
            var sheet = attack ? attackSheet : bodySheet;
            var src = sheet.GetPixels(f.column * Size, (4 - f.row) * Size, Size, Size);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                int sourceY = y;
                if (sourceY >= 0 && sourceY < Size) pixels[y * Size + x] = src[(Size - 1 - sourceY) * Size + x];
            }
            return pixels;
        }
        static bool Skin(Color32 c) => c.a > 0 && c.r > 145 && c.r > c.g + 6 && c.g > c.b + 5;
        static bool Dark(Color32 c) => c.a > 0 && c.r < 135 && c.g < 115 && c.b < 155;
        static Color32 C(string hex) => PixelCanvas.Hex(hex);

        public static PixelCanvas Compose(CharacterLook look, string direction, string frame)
        {
            var f = Frame(direction, frame);
            var src = Pixels(f);
            var canvas = new PixelCanvas(Size, Size).WithPivot(32, 6);
            Array.Copy(src, canvas.Pixels, src.Length);
            int waist = f.waist;
            int left = 26, right = 38;
            // Hair and skin remain immutable; clothing is confined to the underlying garment.
            for (int y = waist - 6; y < waist - 1; y++) for (int x = left; x <= right; x++)
            {
                var p = canvas.Get(x, y);
                if (!Dark(p) || look.armor == ArmorStyle.None) continue;
                bool outline = p.r < 36 && p.g < 36 && p.b < 42;
                if (outline && (!canvas.IsOpaque(x - 1, y) || !canvas.IsOpaque(x + 1, y))) continue;
                var col = look.armorColor;
                if (look.armor == ArmorStyle.Vest)
                    col = Mathf.Abs(x - 32) <= 1 ? C("#40364b") : PixelCanvas.Shade(col, x < 32 ? 1.05f : .78f);
                else if (look.armor == ArmorStyle.Leather)
                    col = (x + y) % 13 < 2 ? C("#e0bb77") : PixelCanvas.Shade(col, x < 32 ? 1.08f : .74f);
                else
                    col = y == waist - 4 || x == left + 2 ? C("#eef4f1") : y >= waist ? C("#677988") : PixelCanvas.Shade(col, x < 32 ? 1.05f : .72f);
                canvas.Set(x, y, col);
            }
            // Shoulder plates extend the silhouette only over sleeve pixels, never over hair/face.
            if (look.armor == ArmorStyle.Plate)
                for (int y = waist - 4; y < waist - 2; y++)
                    for (int x = left - 2; x <= right + 2; x++)
                    {
                        if (!Dark(src[y * Size + x])) continue;
                        if (x > left && x < right) continue;
                        canvas.Set(x, y, y == waist - 4 ? C("#edf1ee") : C("#8a9ba6"));
                        int outward = x < 32 ? -1 : 1;
                        if (!canvas.IsOpaque(x + outward, y)) canvas.Set(x + outward, y, C("#29343f"));
                    }
            // The skirt is an independent lower-equipment region. Legs are articulated below it.
            if (look.bottomTier >= 0)
                for (int y = waist + 2; y < waist + 6; y++) for (int x = 22; x <= 42; x++)
                {
                    var p = canvas.Get(x, y);
                    if (p.a == 0) continue;
                    if (!Skin(p) && !Dark(p)) continue;
                    if (y > 54 && !Skin(p)) continue;
                    if (!canvas.IsOpaque(x - 1, y) || !canvas.IsOpaque(x + 1, y))
                    { canvas.Set(x, y, PixelCanvas.Shade(look.pants, .48f)); continue; }
                    bool knee = look.bottomTier == 1 && y == 53;
                    canvas.Set(x, y, PixelCanvas.Shade(look.pants, knee ? 1.4f : x < 32 ? 1.05f : .78f));
                }
            var oriented = new PixelCanvas(Size, Size).WithPivot(32, 6);
            bool flip = Mirror(FacingOf(direction));
            var pose = Pose(direction, frame);
            WarriorGait.Draw(oriented, FacingOf(direction), frame, waist, look);
            WarriorRightHandRig.Draw(oriented, pose, look, true);
            var torso = new PixelCanvas(Size, Size);
            for (int y = 0; y < waist + 6; y++) for (int x = 0; x < Size; x++)
            {
                var p = canvas.Get(flip ? Size - 1 - x : x, y);
                if (p.a > 0) torso.Set(x, y, p);
            }
            if (WarriorAttackMotion.IsSwing(frame))
                WarriorAttackMotion.BlitBody(oriented, torso, FacingOf(direction), WarriorAttackMotion.Progress(frame), waist, WarriorAttackMotion.Stage(frame), WarriorAttackMotion.IsRecovery(frame));
            else if (WarriorLocomotion.IsWalk(frame))
                WarriorLocomotion.BlitBody(oriented, torso, FacingOf(direction), frame, waist);
            else oriented.Blit(torso, 0, 0);
            WarriorRightHandRig.Draw(oriented, pose, look, false);
            return oriented;
        }
        public static Sprite Get(CharacterLook look, string direction, string frame)
        {
            var c = Compose(look, direction, frame);
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { name = "silver_" + look.id + "_" + direction + "_" + frame, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(c.ToTexturePixels()); texture.Apply(false, false);
            var s = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, 6f / Size), Ppu, 0, SpriteMeshType.FullRect);
            s.name = texture.name;
            return s;
        }
    }
}
