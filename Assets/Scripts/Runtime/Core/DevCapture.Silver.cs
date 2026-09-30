using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator SilverShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(.8f);
            yield return Teleport(new Vector2(28.5f, 17.5f));
            var player = Game.Player;
            var animator = player.GetComponent<CharacterAnimator>();
            var combat = player.GetComponent<PlayerCombat>();
            var camera = Game.Camera.Camera;
            float zoom = camera.orthographicSize;
            camera.orthographicSize = 2.4f;
            int passed = 0;
            void Check(bool okay, string message)
            {
                if (!okay) throw new System.Exception("SILVER FAIL: " + message);
                passed++; Log("PASS " + message);
            }
            var gear = Game.Session.Equipment;
            gear.Unequip(EquipSlot.Top); gear.Unequip(EquipSlot.Bottom);
            foreach (var direction in new[] { Facing.Down, Facing.DownLeft, Facing.Left, Facing.UpLeft, Facing.Up, Facing.UpRight, Facing.Right, Facing.DownRight })
            {
                Game.Input.MoveOverride = direction.ToVector();
                yield return Wait(.25f);
                yield return new WaitForEndOfFrame();
                Check(player.Facing == direction && animator.Current == CharacterAnim.Walk, "walk " + direction);
                Check(animator.Renderer.sprite.name.StartsWith("silver_"), "generated art " + direction);
                string view = SilverWarriorArt.ViewKey(direction);
                var anchor = SilverWarriorArt.Hand(view, animator.FrameKey);
                Check(!animator.Renderer.flipX, "anatomical arms rendered without whole-body reflection " + direction);
                Check(Vector2.Distance(combat.WeaponRenderer.transform.localPosition, anchor) < .001f, "hand attachment " + direction);
                Check((combat.WeaponRenderer.sortingOrder < animator.Renderer.sortingOrder) == SilverWarriorArt.Pose(view, animator.FrameKey).rightHandBack, "weapon depth " + direction);
                var gaitFrames = new System.Collections.Generic.HashSet<string>();
                var handPositions = new System.Collections.Generic.HashSet<Vector2>();
                float until = Time.time + .45f;
                while (Time.time < until)
                {
                    yield return new WaitForEndOfFrame();
                    if (animator.Current != CharacterAnim.Walk) continue;
                    gaitFrames.Add(animator.FrameKey);
                    handPositions.Add(SilverWarriorArt.Pose(view, animator.FrameKey).rightHand);
                }
                Check(gaitFrames.Count >= 6 && handPositions.Count >= 4, "articulated walk arms and legs " + direction);
                if (direction == Facing.DownLeft || direction == Facing.DownRight) yield return Shot("horn_walk_" + direction);
                Game.Input.MoveOverride = Vector2.zero;
                yield return Wait(.1f);
                Check(player.Facing == direction && animator.Current == CharacterAnim.Idle, "stop " + direction);
            }
            foreach (Facing direction in new[] { Facing.Down, Facing.DownLeft, Facing.Left, Facing.UpLeft, Facing.Up, Facing.UpRight, Facing.Right, Facing.DownRight })
            {
                player.FaceTowards(player.Position + direction.ToVector());
                yield return Wait(.5f);
                combat.TryAttack();
                var seen = new System.Collections.Generic.HashSet<string>();
                yield return null;
                while (combat.IsAttacking)
                {
                    yield return new WaitForEndOfFrame();
                    if (!WarriorAttackMotion.IsSwing(animator.FrameKey)) continue;
                    string view = SilverWarriorArt.ViewKey(direction);
                    var pose = SilverWarriorArt.Pose(view, animator.FrameKey);
                    var right = WarriorRightHandRig.WorldHand(pose);
                    var left = new Vector2(pose.leftHand.x - 32, 58 - pose.leftHand.y) / SilverWarriorArt.Ppu;
                    if (Vector2.Distance(combat.WeaponRenderer.transform.localPosition, right) > .001f || Vector2.Distance(combat.WeaponRenderer.transform.localPosition, left) < .03f)
                        throw new System.Exception("Sword switched away from anatomical right hand: " + direction + "/" + animator.FrameKey + "; weapon=" + combat.WeaponRenderer.transform.localPosition + "; right=" + right + "; left=" + left);
                    seen.Add(animator.FrameKey);
                }
                Check(seen.Count >= 12, "smooth full-body right-handed swing " + direction);
            }
            player.FaceTowards(player.Position + Vector2.down);
            foreach (string top in new[] { "eq_top_cloth", "eq_top_leather", "eq_top_iron" })
            {
                Game.Session.Inventory.Add(top, 1); gear.Equip(top, CharacterClass.Warrior);
                foreach (string bottom in new[] { "eq_bot_cloth", "eq_bot_leather" })
                {
                    Game.Session.Inventory.Add(bottom, 1); gear.Equip(bottom, CharacterClass.Warrior);
                    Game.Input.MoveOverride = Vector2.right;
                    yield return Wait(.12f);
                    Check(animator.Current == CharacterAnim.Walk && animator.Look.bottomTier == EquipmentDatabase.TierOf(bottom), "live outfit " + top + "/" + bottom);
                }
            }
            Game.Input.MoveOverride = Vector2.zero;
            gear.Unequip(EquipSlot.Top); gear.Unequip(EquipSlot.Bottom);
            int tier = 0;
            foreach (string item in new[] { "eq_sword_wood", "eq_sword_iron", "eq_sword_bone", "eq_sword_dragon" })
            {
                Game.Session.Inventory.Add(item, 1); gear.Equip(item, CharacterClass.Warrior);
                player.FaceTowards(player.Position + Vector2.down);
                yield return Wait(.5f);
                Check(combat.WeaponRenderer.sprite.name == "silver_held_" + item, "existing weapon " + item);
                yield return Shot("silver_weapon_" + tier);
                combat.TryAttack();
                yield return Wait(.15f);
                yield return new WaitForEndOfFrame();
                Check(WarriorAttackMotion.IsSwing(animator.FrameKey), "swing body " + item);
                Check(Vector2.Distance(combat.WeaponRenderer.transform.localPosition, SilverWarriorArt.Hand("down", animator.FrameKey)) < .001f, "swing grip " + item);
                yield return Shot("silver_attack_" + tier);
                yield return Wait(.5f); tier++;
            }
            gear.Unequip(EquipSlot.Top); gear.Unequip(EquipSlot.Bottom); gear.Unequip(EquipSlot.Weapon);
            yield return Wait(.15f);
            yield return new WaitForEndOfFrame();
            Check(!combat.WeaponRenderer.enabled && !combat.GripRenderer.enabled, "unequipped invisible weapon");
            Check(animator.Look.armor == ArmorStyle.None && animator.Look.bottomTier == -1, "base clothes restored");
            yield return Shot("silver_base");
            var saved = Game.Session.Capture(player.Position, player.Facing);
            Game.Session.Restore(saved, Game.Config);
            Check(string.IsNullOrEmpty(gear[EquipSlot.Weapon]), "save preserves empty weapon slot");
            saved.equipped = null;
            Game.Session.Restore(saved, Game.Config);
            Check(gear[EquipSlot.Weapon] == EquipmentDatabase.StarterWeapon(CharacterClass.Warrior), "legacy save gets starter weapon");
            camera.orthographicSize = zoom;
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(.6f);
            Check(!Game.Player.GetComponent<CharacterAnimator>().Renderer.sprite.name.StartsWith("silver_"), "mage unchanged");
            Log("SILVER COMPLETE " + passed + " checks");
        }
    }
}
