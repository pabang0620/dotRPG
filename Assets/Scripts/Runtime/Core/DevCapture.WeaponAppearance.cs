using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator WeaponAppearanceRun()
        {
            dgnPassed = dgnFailed = 0;
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1);
            AudioListener.volume = 0; Game.Audio.SetVolumes(0, 0);
            Game.Player.Input = new ScriptedInput();
            Game.Player.Health.SetInvulnerable(600);
            Game.Session.Progression.AddXp(999999);
            // Levelling emits several timed bursts; let them finish before inspecting artwork.
            yield return Wait(3);
            Game.UI.Hud.ClearToasts();
            var bag = Game.Session.Inventory;
            var gear = Game.Session.Equipment;
            var player = Game.Player;
            var combat = player.GetComponent<PlayerCombat>();
            var animator = player.GetComponent<CharacterAnimator>();
            var spot = player.Position;
            var region = new Rect(spot.x - 1.6f, spot.y - .6f, 3.2f, 3.2f);
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
            {
                player.SetClass(cls);
                string stem = cls == CharacterClass.Warrior ? "wpn_sword_" : "wpn_staff_";
                for (int tier = 0; tier < 4; tier++)
                {
                    var item = EquipmentDatabase.All.First(i => i.category == EquipCategory.Weapon && i.UsableBy(cls) && i.Tier == tier);
                    bag.Add(item.id, 1); gear.Equip(item.id, cls);
                    player.Place(spot, cls == CharacterClass.Warrior ? Facing.DownRight : Facing.Down);
                    yield return Wait(.12f);
                    yield return new WaitForEndOfFrame();
                    string key = stem + tier;
                    var sprite = combat.WeaponRenderer.sprite;
                    if (cls == CharacterClass.Warrior)
                    {
                        DCheck(key + " dimensions, density, and grip unchanged", sprite.rect.width == 64 && sprite.rect.height == 64
                            && Mathf.Abs(sprite.pixelsPerUnit - 40) < .001f && Vector2.Distance(sprite.pivot, new Vector2(32, 11.5f)) < .001f);
                        DCheck(key + " readable nearest-neighbour source", sprite.texture.isReadable && sprite.texture.filterMode == FilterMode.Point);
                        var surface = PlayerWeaponArt.Draw(key);
                        DCheck(key + " hand remains inside opaque grip", surface.Get(31, 52).a == 255 && surface.Get(32, 52).a == 255);
                    }
                    else CheckUnchangedMageWeapon(sprite, key);
                    DCheck(key + " selected by equipment tier", EquipmentDatabase.WeaponSprite(gear[EquipSlot.Weapon], cls) == key);
                    RenderRegion(Path.Combine(folder, cls + "_tier" + tier + (cls == CharacterClass.Warrior ? "_after.png" : "_unchanged.png")), region, 160);
                    // Capture the exact same live body pose with the previous source asset. Only
                    // this synchronous screenshot substitutes it; equipment and gameplay are untouched.
                    var old = cls == CharacterClass.Warrior ? KatanaArt.Get(key) : null;
                    if (old != null)
                    {
                        var beforePos = combat.WeaponRenderer.transform.localPosition;
                        var beforeRot = combat.WeaponRenderer.transform.localRotation;
                        try { combat.WeaponRenderer.sprite = old; RenderRegion(Path.Combine(folder, cls + "_tier" + tier + "_before.png"), region, 160); }
                        finally { combat.WeaponRenderer.sprite = sprite; }
                        DCheck(key + " same pose in paired capture", combat.WeaponRenderer.transform.localPosition == beforePos && combat.WeaponRenderer.transform.localRotation == beforeRot);
                        if (cls == CharacterClass.Warrior) Destroy(old);
                    }
                }
                // Real equipped enhancement masks exercise source alpha, not a mock shader.
                var selected = EquipmentDatabase.All.First(i => i.category == EquipCategory.Weapon && i.UsableBy(cls) && i.Tier == 1 && !i.starter);
                foreach (int level in new[] { 0, 6, 7, 10, 11, 12 })
                {
                    string key = selected.KeyAt(level); bag.Add(key, 1); gear.Equip(key, cls);
                    yield return Wait(.1f);
                    var fx = player.GetComponent<WeaponEnhanceVfx>();
                    DCheck(cls + " enhancement +" + level + " unchanged", fx.Tier == WeaponEnhanceVfx.TierFor(level) && fx.LightVisible == (level >= 7) && fx.AuraVisible == (level >= 11));
                    if (level == 6 || level == 7 || level == 11) RenderRegion(Path.Combine(folder, cls + "_plus" + level + ".png"), region, 160);
                }
                foreach (Facing facing in Enum.GetValues(typeof(Facing)))
                {
                    player.Place(spot, facing); yield return Wait(.1f); yield return new WaitForEndOfFrame();
                    var weapon = combat.WeaponRenderer;
                    if (cls == CharacterClass.Warrior)
                    {
                        string view = SilverWarriorArt.ViewKey(facing);
                        DCheck("katana anatomical right-hand idle " + facing, Vector2.Distance(weapon.transform.localPosition, SilverWarriorArt.Hand(view, animator.FrameKey)) < .001f);
                        DCheck("katana blade direction preserved " + facing, weapon.flipX == SilverWarriorPresentation.FlipBlade(facing));
                    }
                    RenderRegion(Path.Combine(folder, cls + "_" + facing + ".png"), region, 128);
                    combat.TryAttack(); yield return Wait(.12f); yield return new WaitForEndOfFrame();
                    var rim = weapon.transform.Find("WeaponBlueLight").GetComponent<SpriteRenderer>();
                    var aura = weapon.transform.Find("WeaponBlueAura").GetComponent<SpriteRenderer>();
                    DCheck(cls + " attack mask alignment " + facing, rim.enabled && aura.enabled && rim.flipX == weapon.flipX && aura.flipX == weapon.flipX
                        && rim.sortingOrder == weapon.sortingOrder && rim.transform.localPosition == Vector3.zero && aura.transform.localPosition == Vector3.zero);
                    if (cls == CharacterClass.Warrior)
                        DCheck("katana anatomical right-hand attack " + facing, Vector2.Distance(weapon.transform.localPosition, SilverWarriorArt.Hand(SilverWarriorArt.ViewKey(facing), animator.FrameKey)) < .001f);
                    else CheckUnchangedMageWeapon(weapon.sprite, EquipmentDatabase.WeaponSprite(gear[EquipSlot.Weapon], cls));
                    combat.Cancel(); yield return Wait(.5f);
                }
                gear.Unequip(EquipSlot.Weapon); yield return Wait(.1f);
                DCheck(cls + " unequip removes enhancement", !player.GetComponent<WeaponEnhanceVfx>().LightVisible && !player.GetComponent<WeaponEnhanceVfx>().AuraVisible);
            }
            DCheck("audio remains muted", AudioListener.volume == 0);
            Log($"WEAPON APPEARANCE summary: {dgnPassed} passed, {dgnFailed} failed");
        }

        void CheckUnchangedMageWeapon(Sprite actual, string key)
        {
            DCheck(key + " excluded from warrior art", !PlayerWeaponArt.TryKey(key, out _) && PlayerWeaponArt.Draw(key) == null);
            // Independently reproduce the previous resource / procedural route and compare
            // actual pixels, dimensions and pivot. Ignore RGB of fully transparent bleed pixels.
            var old = Resources.Load<Sprite>("Art/Weapons/" + key) ?? Resources.Load<Sprite>("Art/" + key);
            var pixels = actual.texture.GetPixels((int)actual.rect.x, (int)actual.rect.y, (int)actual.rect.width, (int)actual.rect.height);
            Color32[] expected;
            if (old != null)
            {
                expected = old.texture.GetPixels((int)old.rect.x, (int)old.rect.y, (int)old.rect.width, (int)old.rect.height).Select(c => (Color32)c).ToArray();
                DCheck(key + " original sprite geometry", actual.rect == old.rect && actual.pivot == old.pivot && actual.pixelsPerUnit == old.pixelsPerUnit);
            }
            else
            {
                var original = ProceduralArt.Draw(key); expected = original.ToTexturePixels();
                DCheck(key + " original procedural geometry", actual.rect.width == original.Width && actual.rect.height == original.Height
                    && actual.pivot == new Vector2(original.PivotX, original.PivotY) && actual.pixelsPerUnit == Game.Config.pixelsPerUnit * Math.Max(1, original.Density));
            }
            bool identical = pixels.Length == expected.Length;
            for (int i = 0; identical && i < pixels.Length; i++)
            {
                Color32 p = pixels[i], e = expected[i];
                identical = p.a == e.a && (p.a == 0 || (p.r == e.r && p.g == e.g && p.b == e.b));
            }
            DCheck(key + " original visible pixels unchanged", identical);
        }
    }
}
