using System;
using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        void CheckMobility(bool condition, string message)
        {
            if (!condition) throw new Exception("MOBILITY FAIL: " + message);
            Log("PASS " + message);
        }

        Vector2 MobilityTestOrigin()
        {
            // Find a genuinely open patch: village scenery can change independently of these tests.
            var bounds = Game.World.Bounds;
            for (float y = bounds.yMin + 5; y < bounds.yMax - 5; y += 2)
                for (float x = bounds.xMin + 5; x < bounds.xMax - 5; x += 2)
                {
                    var candidate = new Vector2(x, y);
                    bool clear = true;
                    for (int i = 0; i < 8 && clear; i++)
                    {
                        var dir = new Vector2(Mathf.Cos(i * Mathf.PI / 4), Mathf.Sin(i * Mathf.PI / 4));
                        for (float d = 0; d <= 3.75f; d += .25f)
                            if (!Game.World.IsFree(candidate + dir * d)) { clear = false; break; }
                    }
                    if (clear) return candidate;
                }
            throw new Exception("MOBILITY FAIL: no open test area");
        }

        IEnumerator MobilityShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1f);
            var player = Game.Player;
            var input = new ScriptedInput();
            player.Input = input;
            var origin = MobilityTestOrigin();
            Log("Open test origin " + origin);
            CheckMobility(Game.World.IsFree(origin), "test origin is walkable");
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
            {
                float distance = cls == CharacterClass.Warrior ? PlayerController.DashDistance : PlayerController.BlinkDistance;
                // Independent normalized directions, including last facing with no held movement.
                for (int i = 0; i < 8; i++)
                {
                    player.SetClass(cls);
                    player.Place(origin, Facing.Down);
                    Vector2 dir = new Vector2(Mathf.Cos(i * Mathf.PI / 4), Mathf.Sin(i * Mathf.PI / 4));
                    player.FaceTowards(origin + dir);
                    float mana = player.Data.Mana;
                    CheckMobility(player.TryMobility(Vector2.zero), cls + " direction " + i + " starts from last facing");
                    float immediate = Vector2.Distance(origin, player.Position);
                    CheckMobility(cls == CharacterClass.Mage ? Mathf.Abs(immediate - distance) < .04f : immediate < .01f,
                        cls + " uses " + (cls == CharacterClass.Mage ? "instant teleport" : "continuous dash"));
                    CheckMobility(!player.TryMobility(dir), cls + " repeat press blocked");
                    yield return Wait(.35f);
                    CheckMobility(Vector2.Distance(player.Position, origin + dir * distance) < .07f,
                        cls + " direction " + i + " distance and no drift: " + player.Position);
                    CheckMobility(player.Data.Mana >= mana, cls + " no MP cost");
                }

                player.SetClass(cls);
                player.Place(origin, Facing.Right);
                input.PressMobility();
                input.PressAttack();
                yield return Wait(.35f);
                CheckMobility(Vector2.Distance(player.Position, origin + Vector2.right * distance) < .07f,
                    cls + " command pipeline and mobility priority");
                CheckMobility(!player.GetComponent<PlayerCombat>().IsAttacking, cls + " simultaneous attack consumed");
                yield return Wait(2.1f);
                CheckMobility(player.MobilityCooldownRemaining == 0f, cls + " cooldown recovers");

                var wall = new GameObject("MobilityTestWall");
                wall.transform.position = origin + new Vector2(1.5f, .22f);
                var solid = wall.AddComponent<BoxCollider2D>();
                solid.size = new Vector2(.08f, 4f);
                player.SetClass(cls);
                player.Place(origin, Facing.Right);
                CheckMobility(player.TryMobility(Vector2.right), cls + " shortened move starts");
                yield return Wait(.35f);
                CheckMobility(player.Position.x > origin.x + .8f && player.Position.x < origin.x + 1.19f,
                    cls + " thin wall cannot be crossed, collider clearance kept");
                player.SetClass(cls);
                CheckMobility(!player.TryMobility(Vector2.right) && player.MobilityCooldownRemaining == 0f,
                    cls + " blocked action does not consume cooldown");
                solid.isTrigger = true;
                player.Place(origin, Facing.Right);
                CheckMobility(player.TryMobility(Vector2.right), cls + " trigger allows movement");
                yield return Wait(.35f);
                CheckMobility(Vector2.Distance(player.Position, origin + Vector2.right * distance) < .07f,
                    cls + " trigger does not shorten distance");
                Destroy(wall);

                player.SetClass(cls);
                player.Place(origin, Facing.Right);
                player.LockMovement(.2f);
                CheckMobility(!player.TryMobility(Vector2.right), cls + " channel lock respected");
                yield return Wait(.25f);
                Game.Flow.OpenWindow(Game.UI.Equipment);
                yield return Wait(.1f);
                CheckMobility(!player.TryMobility(Vector2.right), cls + " menu blocks mobility");
                Game.Flow.CloseInventory();
                yield return Wait(.2f);
                player.TryMobility(Vector2.right);
                yield return Wait(cls == CharacterClass.Warrior ? .06f : .02f);
                Game.Camera?.SetTarget(player.transform, true);
                yield return Shot(cls == CharacterClass.Warrior ? "warrior_dash" : "mage_teleport");
                player.Place(origin, Facing.Down);
                yield return Wait(.3f);
                CheckMobility(!player.IsDashing && Vector2.Distance(player.Position, origin) < .02f,
                    cls + " scene reposition cancels movement");
            }
            player.Input = new LocalInput();
            Log("MOBILITY: all checks passed");
        }
    }
}
