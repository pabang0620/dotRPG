using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public static class FemaleWarriorValidation
    {
        public static void ValidateAndBuild()
        {
            Validate();
            BuildScript.BuildWindowsQA();
        }

        [MenuItem("dotRPG/Art/Validate Approved Female Warrior")]
        public static void Validate()
        {
            AssetDatabase.Refresh();
            SilverWarriorArt.ResetCache();
            string root = "Docs/art/FemaleWarrior";
            Directory.CreateDirectory(root);
            var atlas = JsonUtility.FromJson<SilverAtlas>(Resources.Load<TextAsset>("FemaleWarrior/frames").text);
            if (atlas.frames.Length != 70) throw new Exception("Expected 70 female warrior frames");
            int checkedFrames = 0;
            foreach (var f in atlas.frames)
            {
                var canvas = SilverWarriorArt.Compose(CharacterLook.Player, f.direction, f.clip);
                int visible = 0;
                for (int y=0;y<64;y++) for (int x=0;x<64;x++)
                    if (canvas.Get(x,y).a > 0) {
                        visible++;
                        if (x==0||x==63||y==0||y==63) throw new Exception("Clipped sprite "+f.direction+"/"+f.clip);
                    }
                if (visible < 150) throw new Exception("Empty sprite "+f.clip);
                var hand = SilverWarriorArt.Hand(f.direction,f.clip);
                if (float.IsNaN(hand.x)||Mathf.Abs(hand.x)>.9f||Mathf.Abs(hand.y)>1.5f)
                    throw new Exception("Invalid weapon anchor "+f.clip);
                checkedFrames++;
            }
            string[] clips={"idle0","idle1","walk0","walk2","walk4","walk6","attack0","attack1","attack2","attack3","attack4","attack5","hurt"};
            var output = new Texture2D(clips.Length*64,8*64,TextureFormat.RGBA32,false);
            output.SetPixels32(new Color32[output.width*output.height]);
            int row=0;
            foreach(Facing facing in Enum.GetValues(typeof(Facing))) {
                string direction=SilverWarriorArt.ViewKey(facing);
                for(int c=0;c<clips.Length;c++) {
                    var sprite=SilverWarriorArt.Get(CharacterLook.Player,direction,clips[c]);
                    output.SetPixels(c*64,(7-row)*64,64,64,sprite.texture.GetPixels());
                    UnityEngine.Object.DestroyImmediate(sprite.texture);
                    UnityEngine.Object.DestroyImmediate(sprite);
                }
                row++;
            }
            output.Apply();
            File.WriteAllBytes(root+"/unity-eight-direction.png",output.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(output);
            int motionSamples = 0;
            foreach (Facing facing in Enum.GetValues(typeof(Facing)))
            {
                string direction = SilverWarriorArt.ViewKey(facing);
                for (int stage = 0; stage < WarriorAttackMotion.Stages; stage++)
                    for (int recovery = 0; recovery < 2; recovery++)
                    {
                        int count = recovery == 1 ? WarriorAttackMotion.RecoveryFrames : WarriorAttackMotion.Frames;
                        for (int i = 0; i < count; i++)
                        {
                            string frame = WarriorAttackMotion.Frame(i / (float)(count - 1), stage, recovery == 1);
                            var pixels = SilverWarriorArt.Compose(CharacterLook.Player, direction, frame);
                            var anchor = SilverWarriorArt.Hand(direction, frame);
                            float angle = SilverWarriorArt.Angle(direction, frame);
                            if (float.IsNaN(angle) || float.IsInfinity(angle) ||
                                float.IsNaN(anchor.x) || float.IsNaN(anchor.y) ||
                                Mathf.Abs(anchor.x) > .9f || Mathf.Abs(anchor.y) > 1.5f)
                                throw new Exception("Invalid motion attachment " + direction + "/" + frame);
                            int visible = 0;
                            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                                if (pixels.Get(x, y).a > 0) visible++;
                            if (visible < 150) throw new Exception("Missing motion body " + direction + "/" + frame);
                            motionSamples++;
                        }
                    }
            }
            File.WriteAllText(root+"/unity-validation.txt","PASS: "+checkedFrames+" base frames, 104 runtime-facing/animation samples, "+motionSamples+" combo/recovery samples, transparent borders and valid anchors.\n");
            ValidateCatalog(root);
            Debug.Log("FEMALE_WARRIOR_VALIDATION_PASS");
        }

        static void ValidateCatalog(string root)
        {
            var previousArt = Game.Art;
            Game.Art = new SpriteLibrary(36);
            try
            {
                int paletteChecks = 0, cards = 0;
                foreach (var sheet in FemaleWarriorPalette.Sheets)
                {
                    if (Resources.Load<Texture2D>("SilverWarrior/Skins/" + sheet + "/body") != null)
                        throw new Exception("Obsolete body still ships: " + sheet);
                    var look = CharacterLook.Player.Clone();
                    look.id = "player_" + sheet;
                    look.skinSheet = sheet;
                    bool changedColour = false;
                    foreach (Facing facing in Enum.GetValues(typeof(Facing)))
                    {
                        string direction = SilverWarriorArt.ViewKey(facing);
                        foreach (string frame in new[] { "idle0", "walk0", "walk2", "walk4", "walk6", "swing0", "swing9", "swing23", "settle11", "hurt" })
                        {
                            var a = SilverWarriorArt.Compose(CharacterLook.Player, direction, frame);
                            var b = SilverWarriorArt.Compose(look, direction, frame);
                            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                            {
                                var p = a.Get(x, y); var q = b.Get(x, y);
                                if (p.a != q.a) throw new Exception("Palette changed approved silhouette: " + sheet);
                                changedColour |= p.r != q.r || p.g != q.g || p.b != q.b;
                            }
                            if (SilverWarriorArt.Hand(direction, frame) != SilverWarriorArt.Hand(direction, frame, sheet))
                                throw new Exception("Palette detached weapon: " + sheet);
                            paletteChecks++;
                        }
                    }
                    if (!changedColour) throw new Exception("Missing distinct palette: " + sheet);
                }
                int count = SkinCatalog.All.Count + FemaleWarriorPalette.Sheets.Length;
                var contact = new Texture2D(8 * 64, ((count + 7) / 8) * 64, TextureFormat.RGBA32, false);
                contact.SetPixels32(new Color32[contact.width * contact.height]);
                var names = new System.Collections.Generic.List<string>();
                int index = 0;
                foreach (var sheet in FemaleWarriorPalette.Sheets)
                {
                    var look = CharacterLook.Player.Clone();
                    look.id = "player_" + sheet; look.skinSheet = sheet;
                    AddCard(Game.Art.GetCharacter(look, "down", "idle0"), sheet);
                }
                foreach (var skin in SkinCatalog.All)
                {
                    if (skin.cls == CharacterClass.Warrior && !FemaleWarriorPalette.UsesApprovedBody(skin.sheet))
                    {
                        foreach (var sheet in new[] { "body", "attack" })
                            if (Resources.Load<Texture2D>("SilverWarrior/Skins/" + skin.sheet + "/" + sheet) == null)
                                throw new Exception("Missing authored costume sheet: " + skin.id + "/" + sheet);
                    }
                    else if (skin.cls == CharacterClass.Mage)
                    {
                        foreach (var direction in SilverWarriorArt.Directions)
                            foreach (var frame in SilverWarriorArt.BaseFrames)
                                if (Resources.Load<Sprite>("Art/char_" + skin.id + "_" + direction + "_" + frame) == null)
                                    throw new Exception("Missing authored mage costume frame: " + skin.id + "/" + direction + "/" + frame);
                    }
                    var look = SkinCatalog.LookFor(skin.cls, skin.id);
                    var sprite = Game.Art.GetCharacter(look, "down", "idle0");
                    CosmeticProduct product = null;
                    foreach (var candidate in CosmeticCatalog.All) if (candidate.Id == skin.id) { product = candidate; break; }
                    if (product == null || CosmeticAura.Card(product) != sprite)
                        throw new Exception("Shop/draw card differs from equipped skin: " + skin.id);
                    AddCard(sprite, skin.id);
                    cards++;
                }
                contact.Apply();
                File.WriteAllBytes(root + "/catalog-preview.png", contact.EncodeToPNG());
                File.WriteAllLines(root + "/catalog-preview-order.txt", names);
                UnityEngine.Object.DestroyImmediate(contact);
                File.AppendAllText(root + "/unity-validation.txt",
                    "PASS: " + paletteChecks + " palette silhouette/grip checks, " + cards + " shop/draw/equipped sprite identities.\n");

                void AddCard(Sprite sprite, string name)
                {
                    var rect = sprite.textureRect;
                    if (rect.width != 64 || rect.height != 64) throw new Exception("Unexpected skin frame size: " + name);
                    contact.SetPixels((index % 8) * 64, contact.height - (index / 8 + 1) * 64, 64, 64,
                        sprite.texture.GetPixels((int)rect.x, (int)rect.y, 64, 64));
                    names.Add(index + ": " + name); index++;
                }
            }
            finally { Game.Art = previousArt; }
        }
    }
}
