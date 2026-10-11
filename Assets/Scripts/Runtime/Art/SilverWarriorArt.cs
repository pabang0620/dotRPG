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
        public static void ResetCache() { data = null; bodySheet = attackSheet = null; skinSheets.Clear(); }
        static readonly System.Collections.Generic.Dictionary<string, (Texture2D body, Texture2D attack)> skinSheets =
            new System.Collections.Generic.Dictionary<string, (Texture2D, Texture2D)>();
        /// <summary>A costume skin's sheets (same layout as the shipped ones); null when the skin has none.</summary>
        static (Texture2D body, Texture2D attack)? SkinSheets(string skin)
        {
            if (string.IsNullOrEmpty(skin)) return null;
            if (!skinSheets.TryGetValue(skin, out var s))
            {
                s = (Resources.Load<Texture2D>("SilverWarrior/Skins/" + skin + "/body"), Resources.Load<Texture2D>("SilverWarrior/Skins/" + skin + "/attack"));
                skinSheets[skin] = s;
            }
            if (s.body == null || s.attack == null) return null;
            return s;
        }
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
            // The drawing's own hand (same pixel space as the rig: x from the left, y from the top).
            var f = SheetFrame(direction, frame);
            float x = Mirror(FacingOf(direction)) ? Size - 1 - f.hand.x : f.hand.x;
            return new Vector2(x - 32, 58 - f.hand.y) / Ppu;
        }

        // [ART] Every body frame is one of the sheets' own drawings (one image set): no code-drawn arms or legs and
        // no per-pixel warping. The blade still follows the rig's angle, pivoted at the drawing's hand.
        static readonly string[] SheetWalk = { "walk0", "walk1", "walk2", "walk3" };
        static readonly string[] SheetAttack = { "attack0", "attack1", "attack2", "attack3", "attack4", "attack5" };
        // Swing progress at which each attack drawing starts (wind-up, lift, cut, contact, follow-through, hold).
        static readonly float[] SwingStarts = { 0f, .14f, .26f, WarriorAttackMotion.Contact, .60f, .82f };

        /// <summary>The drawing a frame name shows: eight gait phases share the four walk drawings, a swing walks through the six attack drawings.</summary>
        static string SheetClip(string frame)
        {
            if (WarriorLocomotion.IsWalk(frame)) return SheetWalk[WarriorGait.Index(frame) / 2 % SheetWalk.Length];
            if (WarriorAttackMotion.IsRecovery(frame)) return WarriorAttackMotion.Progress(frame) < .5f ? SheetAttack[5] : "idle0";
            if (WarriorAttackMotion.IsSwing(frame))
            {
                float t = WarriorAttackMotion.Progress(frame);
                int i = 0;
                while (i + 1 < SwingStarts.Length && t >= SwingStarts[i + 1]) i++;
                return SheetAttack[i];
            }
            return frame;
        }

        static SilverFrame SheetFrame(string direction, string frame)
        {
            string canonical = Canonical(direction), clip = SheetClip(frame);
            foreach (var f in Data.frames) if (f.direction == canonical && f.clip == clip) return f;
            return Frame(direction, frame);
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
        // This warrior's lower-diagonal source faces the opposite side of the engine convention.
        // Swap only its SE/SW visual mapping; world movement and other characters stay unchanged.
        public static bool Mirror(Facing facing) => facing == Facing.DownRight || (facing.IsLeft() && facing != Facing.DownLeft);
        public static float Angle(string direction, string frame)
        {
            return Pose(direction, frame).swordAngle;
        }
        public static Color32[] Pixels(SilverFrame f, string skin = null)
        {
            var unused = Data;
            bool attack = f.clip.StartsWith("attack") && f.clip.Length > 6;
            var skinned = SkinSheets(skin);
            var sheet = skinned.HasValue ? (attack ? skinned.Value.attack : skinned.Value.body) : attack ? attackSheet : bodySheet;
            var src = sheet.GetPixels(f.column * Size, (4 - f.row) * Size, Size, Size);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                int sourceY = y;
                if (sourceY >= 0 && sourceY < Size) pixels[y * Size + x] = src[(Size - 1 - sourceY) * Size + x];
            }
            return pixels;
        }
        /// <summary>
        /// [ART] One of the sheets' own drawings, mirrored for the facings the sheet doesn't draw. Nothing is painted
        /// over it (worn armour doesn't change the body; a costume skin brings its own sheets).
        /// </summary>
        public static PixelCanvas Compose(CharacterLook look, string direction, string frame)
        {
            var src = Pixels(SheetFrame(direction, frame), look.skinSheet);
            var oriented = new PixelCanvas(Size, Size).WithPivot(32, 6);
            bool flip = Mirror(FacingOf(direction));
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                var p = src[y * Size + (flip ? Size - 1 - x : x)];
                if (p.a > 0) oriented.Set(x, y, p);
            }
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
