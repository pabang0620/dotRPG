using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] The field rules of the chapter 3 monsters (Docs/ch3/WORLD_AND_STORY.md 7): 공허 폭발체 bursts, 잿빛 손 사제 wards
    /// its allies, 공허의 부름꾼 leaves a small rift when it falls. Also the incoming-damage hook shared with the field rift.
    /// Everything runs on the PC that simulates the monsters (solo or the field host), like the rest of monster combat.
    /// </summary>
    public static class Chapter3Monsters
    {
        /// <summary>Called once a monster is spawned and configured (EnemyController.Configure).</summary>
        public static void Attach(EnemyController e, MonsterDef def)
        {
            if (e == null || def == null || def.chapter < 3) return;
            if (def.id == MonsterDatabase.VoidBurster) e.gameObject.AddComponent<BursterFuse>();
            else if (def.id == MonsterDatabase.GreyPriest) e.gameObject.AddComponent<PriestWard>();
            else if (def.id == MonsterDatabase.VoidCaller) e.Died += CallerRift;
        }

        static void CallerRift(EnemyController e)
        {
            if (e == null || e.Puppet) return;
            // A small rift for one second where it fell: step out of it.
            Telegraph.Circle(null, e.Position, 1.2f, 1f, e.MonsterDamage(0.6f), 4f);
        }

        // ------------------------------------------------------------ incoming damage

        /// <summary>Shield points of warded monsters (잿빛 손 사제).</summary>
        static readonly Dictionary<EnemyController, int> wards = new Dictionary<EnemyController, int>();

        /// <summary>
        /// After armour: an open field rift nearby hardens the monster (-30%), then a ward soaks the rest.
        /// Returns what reaches the health bar (0 = all absorbed).
        /// </summary>
        public static int Incoming(EnemyController e, int amount)
        {
            if (e == null || amount <= 0) return amount;
            if (FieldRift.Hardens(e.Position)) amount = Mathf.Max(1, Mathf.RoundToInt(amount * (1f - FieldRift.DamageCut)));
            if (wards.Count == 0 || !wards.TryGetValue(e, out int shield) || shield <= 0) return amount;
            int soaked = Mathf.Min(shield, amount);
            shield -= soaked;
            if (shield > 0) wards[e] = shield; else wards.Remove(e);
            if (soaked > 0) Fx.Sparkle(e.Position + Vector2.up * 0.6f, 2, 0.3f);
            return amount - soaked;
        }

        static void Ward(EnemyController e, int points)
        {
            if (e == null || e.IsDead || points <= 0) return;
            wards.TryGetValue(e, out int now);
            wards[e] = Mathf.Max(now, points); // refreshes, never stacks
        }

        static void Forget(EnemyController e) => wards.Remove(e);

        // ------------------------------------------------------------ behaviours

        /// <summary>Rushes the hero, glows for 1.2 s and bursts (radius 2 m, three times its hit). Killed first: no burst.</summary>
        sealed class BursterFuse : MonoBehaviour
        {
            const float Trigger = 1.6f, FuseSeconds = 1.2f, Radius = 2f, Mul = 3f;
            EnemyController e;
            CharacterAnimator body;
            Telegraph fuse;
            float burstAt = -1f;

            void Awake() { e = GetComponent<EnemyController>(); body = GetComponent<CharacterAnimator>(); }

            /// <summary>While the fuse burns the body shows the swollen glowing frame (after the animator set its pose).</summary>
            void LateUpdate()
            {
                if (burstAt < 0f || e == null || e.IsDead || body == null || body.Renderer == null) return;
                var glow = Chapter3MonsterArt.BursterGlow();
                if (glow != null) body.Renderer.sprite = glow;
            }

            void Update()
            {
                if (e == null || e.IsDead || e.Puppet || !Game.IsWorldRunning) return;
                if (burstAt < 0f)
                {
                    var hero = Game.Player;
                    if (hero == null || hero.IsDead || Vector2.Distance(hero.Position, e.Position) > Trigger) return;
                    fuse = Telegraph.Circle(e, e.Position, Radius, FuseSeconds, e.MonsterDamage(Mul), 8f);
                    burstAt = Time.time + FuseSeconds;
                    return;
                }
                if (Time.time < burstAt) return;
                // The telegraph dealt the blast; the burster is spent (no loot, no kill: nobody killed it).
                Fx.Burst("fx_sparkle", e.Position, 14, 3f, 0.5f);
                e.Expire();
            }
        }

        /// <summary>Every 4 s: a shield of 15% max HP on itself and its allies within 4 m.</summary>
        sealed class PriestWard : MonoBehaviour
        {
            const float Every = 4f, Radius = 4f, Share = .15f;
            EnemyController e;
            float next;

            void Awake() { e = GetComponent<EnemyController>(); next = Time.time + 1.5f; }

            void Update()
            {
                if (e == null || e.IsDead || e.Puppet || !Game.IsWorldRunning || Time.time < next) return;
                next = Time.time + Every;
                foreach (var ally in EnemyController.Active)
                {
                    if (ally == null || ally.IsDead || Vector2.Distance(ally.Position, e.Position) > Radius) continue;
                    Ward(ally, Mathf.RoundToInt(ally.Health.Max * Share));
                    Fx.Sparkle(ally.Position + Vector2.up * 0.8f, 3, 0.4f);
                    ally.Died -= Forget;
                    ally.Died += Forget;
                }
            }
        }
    }
}
