using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public partial class SilverWarriorBaker
    {
        public static void Validate()
        {
            var library = new SpriteLibrary(16); int checkedFrames = 0;
            var hashes = new HashSet<int>();
            foreach (var f in SilverWarriorArt.Data.frames)
            {
                var plain = SilverWarriorArt.Compose(CharacterLook.Player, f.direction, f.clip);
                for (int i = 0; i < plain.Pixels.Length; i++)
                {
                    var p = plain.Pixels[i];
                    if (p.a != 0 && p.a != 255) throw new Exception("Soft alpha");
                    if (p.a > 0 && (i % 64 == 0 || i % 64 == 63 || i / 64 == 0 || i / 64 == 63)) throw new Exception("Clipped body");
                }
                var currentHand = SilverWarriorArt.Pose(f.direction, f.clip).rightHand;
                if (!plain.IsOpaque((int)currentHand.x, (int)currentHand.y)) throw new Exception("Hand anchor outside sprite " + f.direction + f.clip);
                foreach (string top in new[] { null, "eq_plate_1_c", "eq_plate_10_c", "eq_plate_30_c" })
                foreach (string bottom in new[] { null, "eq_greaves_1_c", "eq_greaves_10_c" })
                {
                    var look = CharacterLook.WithGear(CharacterLook.Player, top, bottom);
                    var sprite = library.GetCharacter(look, f.direction, f.clip);
                    if (!sprite.name.StartsWith("silver_")) throw new Exception("Fallback sprite");
                    var canvas = SilverWarriorArt.Compose(look, f.direction, f.clip);
                    // Equipment must never replace the white hair/face above the clothing mask.
                    for (int y = 0; y < f.waist - 13; y++) for (int x = 0; x < 64; x++)
                        if (!canvas.Get(x, y).Equals(plain.Get(x, y))) throw new Exception("Equipment overwrites head");
                    checkedFrames++;
                }
            }
            foreach (string top in new[] { null, "eq_plate_1_c", "eq_plate_10_c", "eq_plate_30_c" })
            foreach (string bottom in new[] { null, "eq_greaves_1_c", "eq_greaves_10_c" })
            {
                int hash = 17;
                foreach (var p in SilverWarriorArt.Compose(CharacterLook.WithGear(CharacterLook.Player, top, bottom), "down", "idle0").Pixels)
                    unchecked { hash = hash * 31 + p.GetHashCode(); }
                hashes.Add(hash);
            }
            if (hashes.Count != 12) throw new Exception("Not all equipment combinations change appearance");
            foreach (Facing facing in Enum.GetValues(typeof(Facing)))
            {
                string key = SilverWarriorArt.ViewKey(facing);
                var rest = SilverWarriorArt.Pose(key, "idle0");
                Vector2 forward = facing.ToVector();
                if (forward.y < -.1f && rest.rightShoulder.x >= rest.leftShoulder.x || forward.y > .1f && rest.rightShoulder.x <= rest.leftShoulder.x)
                    throw new Exception("Anatomical right shoulder switched sides: " + facing);
                if (facing == Facing.Right && rest.rightShoulder.y <= rest.leftShoulder.y || facing == Facing.Left && rest.rightShoulder.y >= rest.leftShoulder.y)
                    throw new Exception("Side view right arm depth inverted");
                for (int phase = 0; phase < 6; phase++)
                {
                    var pose = SilverWarriorArt.Pose(key, "attack" + phase);
                    if (Vector2.Distance(pose.rightShoulder, pose.rightElbow) > 6.5f || Vector2.Distance(pose.rightElbow, pose.rightHand) > 6.5f)
                        throw new Exception("Disconnected right arm: " + facing);
                    var image = library.GetCharacter(CharacterLook.Player, key, "attack" + phase);
                    if (image == null) throw new Exception("Missing independent arm pose " + facing);
                }
                var gaitHashes = new HashSet<int>();
                var hands = new HashSet<Vector2>();
                var pelvisPositions = new HashSet<Vector2>();
                var headImages = new HashSet<int>();
                for (int phase = 0; phase < WarriorGait.Frames; phase++)
                {
                    string walk = "walk" + phase;
                    int waist = SilverWarriorArt.Frame(key, walk).waist;
                    var rightLeg = WarriorGait.Sample(facing, walk, waist, true);
                    var leftLeg = WarriorGait.Sample(facing, walk, waist, false);
                    if (Mathf.Abs(rightLeg.stride + leftLeg.stride) > .001f || rightLeg.lift > 0 && leftLeg.lift > 0)
                        throw new Exception("Gait loses alternating grounded foot: " + facing);
                    pelvisPositions.Add((rightLeg.hip + leftLeg.hip) * .5f);
                    foreach (var leg in new[] { rightLeg, leftLeg })
                        if (Vector2.Distance(leg.hip, leg.knee) > 10 || Vector2.Distance(leg.knee, leg.foot) > 10)
                            throw new Exception("Disconnected walking knee: " + facing + walk);
                    var support = phase < 4 ? rightLeg : leftLeg;
                    var contact = WarriorGait.Sample(facing, phase < 4 ? "walk0" : "walk4", waist, phase < 4);
                    var travelled = WarriorAttackMotion.Project(facing.ToVector() * (phase % 4) * WarriorGait.CycleDistance / 8 * SilverWarriorArt.Ppu);
                    if (Vector2.Distance(support.foot + travelled, contact.foot) > .01f)
                        throw new Exception("Support foot slips relative to ground: " + facing + walk);
                    if (Mathf.Abs(Mathf.DeltaAngle(rest.swordAngle, SilverWarriorArt.Pose(key, walk).swordAngle)) > 4)
                        throw new Exception("Walking weapon loses stable heading: " + facing);
                    hands.Add(SilverWarriorArt.Pose(key, walk).rightHand);
                    int hash = 17;
                    var walkingBody = SilverWarriorArt.Compose(CharacterLook.Player, key, walk);
                    int headHash = 17;
                    for (int y = 0; y < 36; y++) for (int x = 0; x < 64; x++)
                        unchecked { headHash = headHash * 31 + walkingBody.Get(x, y).GetHashCode(); }
                    headImages.Add(headHash);
                    foreach (var pixel in walkingBody.Pixels)
                        unchecked { hash = hash * 31 + pixel.GetHashCode(); }
                    gaitHashes.Add(hash);
                }
                if (hands.Count < 4 || gaitHashes.Count < 6) throw new Exception("Frozen walk limbs: " + facing + "; hands=" + hands.Count + "; images=" + gaitHashes.Count);
                if (pelvisPositions.Count < 4 || headImages.Count < 4) throw new Exception("Walking torso remains frozen: " + facing);
            }
            foreach (string dir in SilverWarriorArt.Directions)
            {
                var attackHashes = new HashSet<int>();
                for (int i = 0; i < 6; i++)
                {
                    int h = 17;
                    foreach (var p in SilverWarriorArt.Compose(CharacterLook.Player, dir, "attack" + i).Pixels)
                        unchecked { h = h * 31 + p.GetHashCode(); }
                    attackHashes.Add(h);
                }
                if (attackHashes.Count != 6) throw new Exception("Repeated attack frames: " + dir);
            }
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.category != EquipCategory.Weapon || !item.UsableBy(CharacterClass.Warrior)) continue;
                var equipped = library.GetWarriorWeapon(item.id);
                var original = library.Get(EquipmentDatabase.WeaponSprite(item.id, CharacterClass.Warrior));
                if (equipped.texture != original.texture || !Mathf.Approximately(equipped.pivot.y, KatanaArt.GripY))
                    throw new Exception("Katana pixels or grip mismatch: " + item.id);
            }
            if (library.GetCharacter(CharacterLook.Mage, "down", "idle0").name.StartsWith("silver_")) throw new Exception("Mage changed");
            Directory.CreateDirectory("Docs/images/silver-warrior");
            WritePreview(library);
            WriteGaitPreview();
            ValidateAttackMotion(library);
            ValidateFlames();
            File.WriteAllText("Docs/images/silver-warrior/validation.txt", "PASS " + checkedFrames + " equipment/frame combinations; 70 anchors; 12 distinct outfits; hard alpha; unclipped bodies; head preserved; mage unchanged.");
            Debug.Log("[SilverWarrior] PASS " + checkedFrames + " equipment/frame combinations");
        }
        public static void ValidateAndBuild()
        {
            SilverWarriorArt.ResetCache();
            Validate();
            BuildScript.BuildWindows();
        }
        static void ValidateAttackMotion(SpriteLibrary library)
        {
            var directions = new[] { Facing.Down, Facing.DownLeft, Facing.Left, Facing.UpLeft, Facing.Up, Facing.UpRight, Facing.Right, Facing.DownRight };
            var atlas = new PixelCanvas((WarriorAttackMotion.Frames + WarriorAttackMotion.RecoveryFrames) * 3 * 128, 8 * 128);
            foreach (Facing facing in directions)
            {
                string key = SilverWarriorArt.ViewKey(facing);
                int row = Array.IndexOf(directions, facing);
                var rest = SilverWarriorArt.Compose(CharacterLook.Player, key, "idle0");
                var rootFoot = WarriorGait.Sample(facing, "idle0", 42, true).foot;
                var forward = facing.ToVector(); var anatomicalRight = new Vector2(forward.y, -forward.x);
                float cutSign = facing == Facing.Right || facing == Facing.UpRight || facing == Facing.DownRight ? -1 : 1;
                float restAngle = SilverWarriorArt.Angle(key, "idle0");
                float a0 = WarriorAttackMotion.SwordAngle(facing, .26f, restAngle);
                float a1 = WarriorAttackMotion.SwordAngle(facing, .6f, restAngle);
                float b0 = WarriorAttackMotion.SwordAngle(facing, .26f, restAngle, 1);
                float b1 = WarriorAttackMotion.SwordAngle(facing, .6f, restAngle, 1);
                if ((a1 - a0) * cutSign <= 0 || (b1 - b0) * cutSign >= 0)
                    throw new Exception("Combo cuts must alternate: " + key);
                for (int stage = 0; stage < 3; stage++)
                {
                    string end = WarriorAttackMotion.Frame(1, stage);
                    string next = WarriorAttackMotion.Frame(0, stage + 1);
                    var endBody = SilverWarriorArt.Compose(CharacterLook.Player, key, end);
                    var recoveryStart = SilverWarriorArt.Compose(CharacterLook.Player, key, WarriorAttackMotion.Frame(0, stage, true));
                    var recovered = SilverWarriorArt.Compose(CharacterLook.Player, key, WarriorAttackMotion.Frame(1, stage, true));
                    var nextBody = SilverWarriorArt.Compose(CharacterLook.Player, key, next);
                    for (int px = 0; px < rest.Pixels.Length; px++)
                    {
                        if (!recovered.Pixels[px].Equals(rest.Pixels[px])) throw new Exception("Recovery fails to reach idle: " + key + stage);
                        if (!endBody.Pixels[px].Equals(recoveryStart.Pixels[px])) throw new Exception("Recovery jumps: " + key + stage);
                        if (stage < 2 && !endBody.Pixels[px].Equals(nextBody.Pixels[px])) throw new Exception("Combo body transition jumps: " + key + stage);
                    }
                    if (stage < 2 && Mathf.Abs(Mathf.DeltaAngle(SilverWarriorArt.Angle(key, end), SilverWarriorArt.Angle(key, next))) > .01f)
                        throw new Exception("Combo blade transition jumps: " + key + stage);
                }
                var heads = new HashSet<int>(); var hips = new HashSet<Vector2>();
                for (int i = 0; i < (WarriorAttackMotion.Frames + WarriorAttackMotion.RecoveryFrames) * 3; i++)
                {
                    string frame = i < 72 ? "swing" + i : "settle" + (i - 72);
                    var pose = SilverWarriorArt.Pose(key, frame);
                    var body = SilverWarriorArt.Compose(CharacterLook.Player, key, frame);
                    if (Vector2.Distance(pose.rightHand, pose.leftHand) < 1.1f)
                        throw new Exception("Attack free hand overlaps sword hand: " + key + frame + "; right=" + pose.rightHand + "; left=" + pose.leftHand);
                    var leg = WarriorGait.Sample(facing, frame, 42, true);
                    hips.Add(leg.hip);
                    if (Vector2.Distance(rootFoot, leg.foot) > .001f) throw new Exception("Attack pivot foot slides: " + key);
                    if (Vector2.Distance(pose.rightShoulder, pose.rightElbow) > 6.5f || Vector2.Distance(pose.rightElbow, pose.rightHand) > 6.5f)
                        throw new Exception("Attack arm disconnected: " + key + frame);
                    int head = 17;
                    for (int y = 0; y < 36; y++) for (int x = 0; x < 64; x++)
                        unchecked { head = head * 31 + body.Get(x, y).GetHashCode(); }
                    heads.Add(head);
                    if (i == 0)
                        for (int p = 0; p < body.Pixels.Length; p++)
                            if (!body.Pixels[p].Equals(rest.Pixels[p])) throw new Exception("Attack does not settle into idle: " + key + frame);
                    foreach (var p in body.Pixels) if (p.a != 0 && p.a != 255) throw new Exception("Soft attack pixels");
                    atlas.Blit(body, i * 128 + 32, row * 128 + 32);
                    var blade = library.GetWarriorWeapon("eq_sword_10_u");
                    var pixels = blade.texture.GetPixels32();
                    float angle = (pose.swordAngle - 90) * Mathf.Deg2Rad;
                    float co = Mathf.Cos(angle), si = Mathf.Sin(angle);
                    float scale = SilverWarriorArt.Ppu / KatanaArt.Ppu * SilverWarriorArt.WeaponScale;
                    bool flip = SilverWarriorPresentation.FlipBlade(facing);
                    for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                    {
                        float dx = x - 32 - pose.rightHand.x, dy = pose.rightHand.y + 32 - y;
                        float lx = (co * dx + si * dy) / scale, ly = (-si * dx + co * dy) / scale;
                        int sx = Mathf.FloorToInt(blade.pivot.x + (flip ? -lx : lx)), sy = Mathf.FloorToInt(blade.pivot.y + ly);
                        if (sx < 0 || sx >= 64 || sy < 0 || sy >= 64) continue;
                        var color = pixels[(int)(blade.rect.y + sy) * blade.texture.width + (int)blade.rect.x + sx];
                        if (color.a == 0 || pose.rightHandBack && body.IsOpaque(x - 32, y - 32)) continue;
                        atlas.Set(i * 128 + x, row * 128 + y, color);
                    }
                }
                if (heads.Count < 6 || hips.Count < 8) throw new Exception("Attack body remains frozen: " + key);
            }
            var texture = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(atlas.ToTexturePixels()); texture.Apply();
            File.WriteAllBytes("Docs/images/silver-warrior/full-body-attack.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log("[WarriorAttack] PASS 864 combo/recovery poses: grounded pivot foot, articulated body, right hand, continuous stage/recovery transitions");
        }
        static void ValidateFlames()
        {
            var atlas = new PixelCanvas(128 * WarriorFlameSlash.Frames, 128 * 3);
            var hashes = new HashSet<int>();
            for (int stage = 0; stage < 3; stage++) for (int frame = 0; frame < WarriorFlameSlash.Frames; frame++)
            {
                var c = WarriorFlameSlash.Draw(stage, frame); int hash = 17, visible = 0;
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    var pixel = c.Get(x, y);
                    if (pixel.a != 0 && pixel.a != 255) throw new Exception("Soft flame pixel");
                    if (pixel.a > 0 && (x < 2 || y < 2 || x > 125 || y > 125)) throw new Exception("Clipped flame");
                    if (pixel.a > 0) visible++;
                    unchecked { hash = hash * 31 + pixel.GetHashCode(); }
                }
                if (visible < 500) throw new Exception("Empty flame frame");
                hashes.Add(hash); atlas.Blit(c, frame * 128, stage * 128);
            }
            if (hashes.Count < 20) throw new Exception("Frozen flame animation");
            var texture = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(atlas.ToTexturePixels()); texture.Apply();
            File.WriteAllBytes("Docs/images/silver-warrior/flame-slashes.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log("[WarriorFlame] PASS " + (3 * WarriorFlameSlash.Frames) + " frames: hard alpha, animated tongues and embers, no clipping");
        }
        static void WriteGaitPreview()
        {
            var directions = new[] { Facing.Down, Facing.DownLeft, Facing.Left, Facing.UpLeft, Facing.Up, Facing.UpRight, Facing.Right, Facing.DownRight };
            var atlas = new PixelCanvas(512, 512);
            for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++)
                atlas.Blit(SilverWarriorArt.Compose(CharacterLook.Player, SilverWarriorArt.ViewKey(directions[row]), "walk" + col), col * 64, row * 64);
            var texture = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            texture.SetPixels32(atlas.ToTexturePixels()); texture.Apply();
            File.WriteAllBytes("Docs/images/silver-warrior/eight-direction-walk.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        static void WritePreview(SpriteLibrary library)
        {
            var texture = new Texture2D(8 * 128, 5 * 128, TextureFormat.RGBA32, false);
            var output = new Color32[texture.width * texture.height];
            for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
                output[y * texture.width + x] = ((x / 16 + y / 16) % 2 == 0) ? PixelCanvas.Hex("#202833") : PixelCanvas.Hex("#26313e");
            for (int row = 0; row < 5; row++) for (int col = 0; col < 8; col++)
            {
                string top = col < 4 ? null : col == 4 ? "eq_plate_1_c" : col == 5 ? "eq_plate_10_c" : "eq_plate_30_c";
                string bot = col < 5 ? null : col == 5 ? "eq_greaves_1_c" : "eq_greaves_10_c";
                string frame = col < 4 ? "walk" + col : "idle0";
                var pixels = SilverWarriorArt.Compose(CharacterLook.WithGear(CharacterLook.Player, top, bot), SilverWarriorArt.Directions[row], frame).ToTexturePixels();
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    var p = pixels[y / 2 * 64 + x / 2];
                    if (p.a > 0) output[((4 - row) * 128 + y) * texture.width + col * 128 + x] = p;
                }
            }
            texture.SetPixels32(output); texture.Apply();
            File.WriteAllBytes("Docs/images/silver-warrior/outfits.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
