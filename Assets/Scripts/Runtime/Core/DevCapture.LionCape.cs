using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        int lionPassed, lionFailed;
        readonly string[] LionFrames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "walk4", "walk5", "walk6", "walk7" };

        void LionCheck(string name, bool ok)
        {
            if (ok) lionPassed++; else lionFailed++;
            Log("LION " + (ok ? "PASS " : "FAIL ") + name);
        }

        static bool LionSame(Color32 a, Color32 b) => a.a == b.a && (a.a == 0 || a.r == b.r && a.g == b.g && a.b == b.b);

        static string LionSheetClip(string frame) => frame.StartsWith("walk", StringComparison.Ordinal)
            ? "walk" + (int.Parse(frame.Substring(4)) / 2 % 4) : frame;

        static PixelCanvas LionBefore(Texture2D sheet, string direction, string frame)
        {
            var f = SilverWarriorArt.Data.frames.First(p => p.direction == SilverWarriorArt.Canonical(direction) && p.clip == LionSheetClip(frame));
            var pixels = sheet.GetPixels32();
            var c = new PixelCanvas(64, 64).WithPivot(32, 6);
            bool mirror = SilverWarriorArt.Mirror(SilverWarriorArt.FacingOf(direction));
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int sx = f.column * 64 + (mirror ? 63 - x : x);
                int sy = (4 - f.row) * 64 + 63 - y;
                c.Set(x, y, pixels[sy * sheet.width + sx]);
            }
            return c;
        }

        static PixelCanvas LionSprite(Sprite sprite)
        {
            var p = sprite.texture.GetPixels32();
            var c = new PixelCanvas(64, 64).WithPivot(32, 6);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                c.Set(x, y, p[((int)sprite.rect.y + 63 - y) * sprite.texture.width + (int)sprite.rect.x + x]);
            return c;
        }

        static void LionSaveCanvas(PixelCanvas pixels, string path)
        {
            var tex = new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(pixels.ToTexturePixels()); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
        }

        static bool LionCapeColor(Color32 p) => p.a >= 128 && p.r > 55 && p.r > p.g * 1.7f && p.r > p.b * 1.35f && p.b >= p.g * .78f;

        void LionCapeAttachment(PixelCanvas c, string label)
        {
            // Restrict to the back below the head: red eyes, circlet jewels and boot details are excluded.
            var red = new HashSet<Vector2Int>();
            for (int y = 28; y < 54; y++) for (int x = 12; x < 52; x++) if (LionCapeColor(c.Get(x, y))) red.Add(new Vector2Int(x, y));
            var remaining = new HashSet<Vector2Int>(red);
            var biggest = new List<Vector2Int>();
            while (remaining.Count > 0)
            {
                var component = new List<Vector2Int>(); var q = new Queue<Vector2Int>();
                var start = remaining.First(); remaining.Remove(start); q.Enqueue(start);
                while (q.Count > 0)
                {
                    var v = q.Dequeue(); component.Add(v);
                    foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        if (remaining.Remove(v + d)) q.Enqueue(v + d);
                }
                if (component.Count > biggest.Count) biggest = component;
            }
            LionCheck(label + " visible continuous cape from shoulder to lower back", biggest.Count >= 28 && biggest.Min(p => p.y) <= 40 && biggest.Max(p => p.y) >= 51);
            Log(label + " cape red pixels=" + red.Count + " largest connected area=" + biggest.Count +
                (biggest.Count == 0 ? "" : " span=" + biggest.Min(p => p.y) + ".." + biggest.Max(p => p.y)));
        }

        IEnumerator LionCapeRun()
        {
            lionPassed = lionFailed = 0;
            yield return Wait(1f);
            bool autosave = Game.Config.autosave;
            Texture2D old = null;
            try
            {
                Game.Config.autosave = false;
                Game.Flow.NewGame(CharacterClass.Warrior, "황금 사자 기사 망토 검증"); yield return Wait(1f);
                ApplyRequestedResolution(); yield return Wait(.2f);
                AudioListener.volume = 0; Game.Audio?.SetVolumes(0, 0); Game.UI.Hud.ClearToasts();
                var args = Environment.GetCommandLineArgs(); string beforePath = null;
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lionBefore") beforePath = args[i + 1];
                if (beforePath == null || !File.Exists(beforePath)) throw new FileNotFoundException("-lionBefore must point to the preserved body sheet.", beforePath);
                old = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (!old.LoadImage(File.ReadAllBytes(beforePath), false)) throw new IOException("Could not decode the preserved lion body sheet.");
                LionCheck("original sheet geometry", old.width == 512 && old.height == 320);
                LionCheck("preview cannot equip lion on mage", SkinCatalog.LookFor(CharacterClass.Mage, "skin_lion") == null);
                var look = SkinCatalog.LookFor(CharacterClass.Warrior, "skin_lion");
                var sheet = new PixelCanvas(64 * LionFrames.Length, 64 * 4);
                for (int i = 0; i < LionFrames.Length; i++)
                {
                    string frame = LionFrames[i];
                    var ne = Game.Art.GetCharacter(look, "upside", frame);
                    var nw = Game.Art.GetCharacter(look, "upleft", frame);
                    var a = LionSprite(ne); var b = LionSprite(nw);
                    LionCheck(frame + " 64 canvas, fixed feet pivot and density", ne.rect.size == new Vector2(64, 64) && nw.rect.size == ne.rect.size &&
                        ne.pivot == new Vector2(32, 6) && nw.pivot == ne.pivot && ne.pixelsPerUnit == 36 && nw.pixelsPerUnit == 36);
                    LionCheck(frame + " nearest-neighbor sampling", ne.texture.filterMode == FilterMode.Point && nw.texture.filterMode == FilterMode.Point);
                    bool mirrored = true;
                    for (int y = 0; y < 64 && mirrored; y++) for (int x = 0; x < 64 && mirrored; x++) mirrored = LionSame(a.Get(x, y), b.Get(63 - x, y));
                    LionCheck(frame + " NW is exactly one NE pixel mirror", mirrored);
                    var beforeNe = LionBefore(old, "upside", frame); var beforeNw = LionBefore(old, "upleft", frame);
                    LionCheck(frame + " upward-diagonal art was updated", !a.Pixels.Zip(beforeNe.Pixels, LionSame).All(p => p));
                    LionCapeAttachment(a, "NE " + frame); LionCapeAttachment(b, "NW " + frame);
                    sheet.Blit(beforeNe, i * 64, 0); sheet.Blit(beforeNw, i * 64, 64);
                    sheet.Blit(a, i * 64, 128); sheet.Blit(b, i * 64, 192);
                    LionSaveCanvas(a, Path.Combine(folder, "after-ne-" + frame + ".png"));
                    LionSaveCanvas(b, Path.Combine(folder, "after-nw-" + frame + ".png"));
                }
                LionSaveCanvas(sheet, Path.Combine(folder, "lion-cape-before-after-contact-sheet.png"));
                foreach (var facing in new[] { Facing.Down, Facing.DownLeft, Facing.DownRight, Facing.Left, Facing.Right, Facing.Up })
                    foreach (var frame in LionFrames)
                    {
                        string view = SilverWarriorArt.ViewKey(facing);
                        var actual = LionSprite(Game.Art.GetCharacter(look, view, frame));
                        var before = LionBefore(old, view, frame);
                        LionCheck(facing + " " + frame + " other view pixels unchanged", actual.Pixels.Zip(before.Pixels, LionSame).All(p => p));
                    }
                var normal = Game.Art.GetCharacter(CharacterLook.Player, "upside", "idle0");
                var normalSnapshot = normal.texture.GetPixels32();
                var mage = Game.Art.GetCharacter(CharacterLook.Mage, "upside", "idle0");
                var mageSnapshot = mage.texture.GetPixels32();
                var player = Game.Player; player.Health.SetInvulnerable(120);
                var input = new ScriptedInput(); player.Input = input;
                var animator = player.GetComponent<CharacterAnimator>(); var combat = player.GetComponent<PlayerCombat>();
                var origin = MobilityTestOrigin();
                foreach (var facing in new[] { Facing.UpRight, Facing.UpLeft })
                {
                    player.Place(origin, facing); animator.SetLook(look);
                    input.Move = facing == Facing.UpRight ? new Vector2(1, 1).normalized : new Vector2(-1, 1).normalized;
                    var seen = new HashSet<string>();
                    float until = Time.realtimeSinceStartup + 6f;
                    while (seen.Count < 8 && Time.realtimeSinceStartup < until)
                    {
                        yield return new WaitForEndOfFrame();
                        string frame = animator.FrameKey;
                        if (!frame.StartsWith("walk", StringComparison.Ordinal) || !seen.Add(frame)) continue;
                        string view = SilverWarriorArt.ViewKey(facing);
                        LionCheck(facing + " " + frame + " actual facing and skin", player.Facing == facing && animator.Look.skinSheet == "lion" && animator.Current == CharacterAnim.Walk);
                        LionCheck(facing + " " + frame + " no second renderer flip", !animator.Body.flipX);
                        LionCheck(facing + " " + frame + " weapon stays on drawn right hand", Vector2.Distance(combat.WeaponRenderer.transform.localPosition, SilverWarriorArt.Hand(view, frame)) < .001f);
                        LionCheck(facing + " " + frame + " grip and blade rule preserved", Vector2.Distance(combat.GripRenderer.transform.localPosition, SilverWarriorArt.Hand(view, frame)) < .001f && combat.WeaponRenderer.flipX == SilverWarriorPresentation.FlipBlade(facing));
                        var at = player.Position;
                        RenderRegion(Path.Combine(folder, "live-" + facing + "-" + frame + ".png"), new Rect(at.x - 1.15f, at.y - .45f, 2.3f, 2.3f), 192);
                    }
                    input.Move = Vector2.zero;
                    LionCheck(facing + " all eight natural locomotion phases captured", seen.Count == 8);
                    LionCheck(facing + " real movement occurred", Vector2.Distance(player.Position, origin) > WarriorGait.CycleDistance * .75f);
                    yield return Wait(.15f); Game.UI.Hud.ClearToasts();
                    yield return Shot("lion-cape-play-" + facing);
                }
                LionCheck("normal warrior sprite cache unchanged", normal.texture.GetPixels32().Zip(normalSnapshot, LionSame).All(p => p));
                LionCheck("mage sprite cache unchanged", mage.texture.GetPixels32().Zip(mageSnapshot, LionSame).All(p => p));
                LionCheck("muted before and throughout capture", AudioListener.volume == 0);
                File.WriteAllText(Path.Combine(folder, "contact-sheet-layout.json"), MiniJson.Write(HRow(
                    ("rows", new[] { "before NE", "before NW", "after NE", "after NW" }), ("columns", LionFrames),
                    ("frameCanvas", new[] { 64, 64 }), ("pivot", new[] { 32, 6 }), ("ppu", 36),
                    ("beforeBodyPath", beforePath), ("skin", "skin_lion"), ("otherDirectionsCompared", 6), ("audioVolume", 0))));
            }
            finally
            {
                if (old != null) Destroy(old);
                if (Game.Player != null) { Game.Player.Input = new LocalInput(); Game.Player.RefreshLook(); }
                Game.Config.autosave = autosave;
                Log($"LION RESULTS: {lionPassed} passed, {lionFailed} failed");
            }
        }
    }
}
