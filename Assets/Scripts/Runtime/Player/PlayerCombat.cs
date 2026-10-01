using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Sword swing: cooldown, arc animation of the weapon sprite, slash effect and hit detection.
    /// The same swing damages enemies and harvests trees/rocks (anything implementing IDamageable).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class PlayerCombat : MonoBehaviour
    {
        bool meleeActive, recovering;
        int comboStage, queuedStrikes;
        public int ComboStage => comboStage + 1;
        public bool IsRecovering => recovering;
        public int ResolvedStrikeCount { get; private set; }
        public string AttackFrame => WarriorAttackMotion.Frame(AttackProgress, comboStage, recovering);
        CharacterAnimator characterAnimator;
        SpriteRenderer silverGrip;
        public float AttackProgress => IsAttacking ? Mathf.Clamp01((Time.time - attackStart) / Duration) : 0f;
        public SpriteRenderer WeaponRenderer => weapon;
        public SpriteRenderer GripRenderer => silverGrip;
        public SpriteRenderer SlashRenderer => slash;
        void LateUpdate()
        {
            if (silverGrip == null || owner == null) return;
            bool silver = owner.Class == CharacterClass.Warrior;
            silverGrip.enabled = silver && !owner.IsDead && weapon.sprite != null;
            if (!silver) return;
            SilverWarriorPresentation.Pose(weapon, silverGrip, characterAnimator, owner.Facing, !owner.IsDead);
        }

        PlayerController owner;
        PlayerStats stats;
        SpriteRenderer weapon;
        SpriteRenderer slash;
        YSort ySort;

        float attackStart = -10f;
        float attackEnd = -10f;
        float nextAttackTime;
        Facing attackFacing;
        bool hitResolved;

        readonly List<Collider2D> overlap = new List<Collider2D>();
        readonly HashSet<IDamageable> hitThisSwing = new HashSet<IDamageable>();
        ContactFilter2D filter;

        public bool IsAttacking => Ranged ? Time.time < attackEnd : meleeActive;

        CharacterClassInfo classInfo = CharacterClassInfo.Get(CharacterClass.Warrior);
        EnemyController castTarget;
        Vector2 castAim = Vector2.down;
        bool Ranged => classInfo != null && classInfo.ranged;
        float Duration => Ranged ? classInfo.castDuration : recovering ? WarriorAttackMotion.RecoveryDuration : Mathf.Max(WarriorAttackMotion.StageDuration(comboStage), stats.attackDuration);

        /// <summary>Switches weapon sprite and attack style (sword swing or magic bolt).</summary>
        public void SetClass(CharacterClassInfo info)
        {
            classInfo = info ?? CharacterClassInfo.Get(CharacterClass.Warrior);
            Cancel();
            RefreshWeapon();
        }

        /// <summary>Uses the sprite of the equipped weapon (wooden sword, bone greatsword, crystal staff...).</summary>
        public void RefreshWeapon()
        {
            if (weapon == null || Game.Session == null || owner == null || owner.Data == null) return;
            // [MERGE] Each party member shows its own weapon (owner.Data), with the katana grip art for warriors.
            string weaponKey = owner.Data.Equipment[EquipSlot.Weapon];
            weapon.sprite = classInfo.id == CharacterClass.Warrior ? Game.Art.GetWarriorWeapon(weaponKey) : Game.Art.Get(EquipmentDatabase.WeaponSprite(weaponKey, classInfo.id));
            weapon.enabled = !owner.IsDead && weapon.sprite != null;
            if (silverGrip != null) silverGrip.enabled = !Ranged && weapon.enabled;
            HdMaterial.Apply(weapon);
        }

        /// <summary>
        /// Weapon at rest: the sword hangs in the hand, the staff stands beside the character. Placed for
        /// all eight facings (left ones mirrored), behind the body when facing away.
        /// </summary>
        void HoldPose()
        {
            if (weapon == null || owner == null) return;
            weapon.enabled = !owner.IsDead && weapon.sprite != null;
            if (slash != null && slash.enabled) slash.enabled = false;
            if (!Ranged) return; // Silver warrior is attached to the current frame's hand in LateUpdate.
            var f = owner.Facing;
            float sx = f.IsLeft() ? -1f : 1f;
            Vector2 pos;
            float rot;
            int order;
            if (Ranged)
            {
                switch (f)
                {
                    case Facing.Up: pos = new Vector2(-0.34f, 0.06f); rot = 6f; order = -1; break;
                    case Facing.Down: pos = new Vector2(0.36f, 0.04f); rot = -6f; order = 1; break;
                    case Facing.UpLeft:
                    case Facing.UpRight: pos = new Vector2(0.3f * sx, 0.08f); rot = -6f * sx; order = -1; break;
                    default: pos = new Vector2(0.3f * sx, 0.04f); rot = -6f * sx; order = 1; break;
                }
            }
            else
            {
                switch (f)
                {
                    case Facing.Up: pos = new Vector2(-0.3f, 0.34f); rot = 160f; order = -1; break;
                    case Facing.Down: pos = new Vector2(0.3f, 0.34f); rot = -160f; order = 1; break;
                    case Facing.UpLeft:
                    case Facing.UpRight: pos = new Vector2(0.28f * sx, 0.36f); rot = -160f * sx; order = -1; break;
                    default: pos = new Vector2(0.24f * sx, 0.34f); rot = -150f * sx; order = 1; break;
                }
            }
            weapon.transform.localPosition = pos;
            weapon.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            weapon.transform.localScale = Vector3.one * 0.85f;
            if (ySort == null) ySort = GetComponent<YSort>();
            ySort?.SetLocalOrder(weapon, order);
        }

        public void Setup(PlayerController player, Transform visualRoot)
        {
            owner = player;
            stats = player.Stats;
            characterAnimator = player.GetComponent<CharacterAnimator>();
            silverGrip = new GameObject("SilverGrip").AddComponent<SpriteRenderer>();
            silverGrip.transform.SetParent(visualRoot, false);
            silverGrip.sprite = SilverWarriorPresentation.Grip;
            silverGrip.enabled = false;

            weapon = new GameObject("Weapon").AddComponent<SpriteRenderer>();
            weapon.transform.SetParent(visualRoot, false);
            weapon.sprite = Game.Art.Get("tool_sword");
            weapon.sortingOrder = 1;
            weapon.enabled = false;
            HdMaterial.Apply(weapon);

            slash = new GameObject("Slash").AddComponent<SpriteRenderer>();
            slash.transform.SetParent(visualRoot, false);
            slash.sprite = Game.Art.Get("fx_slash");
            slash.sortingOrder = 3;
            slash.enabled = false;

            filter = new ContactFilter2D { useTriggers = true };
        }

        public void Cancel()
        {
            attackEnd = -10f;
            meleeActive = recovering = false; queuedStrikes = comboStage = 0;
            nextAttackTime = 0;
            if (slash != null) slash.enabled = false;
        }

        public void TryAttack()
        {
            if (!Ranged && meleeActive)
            {
                // Each key-down buys exactly one additional strike; holding the key does not repeat.
                if (!recovering && comboStage + queuedStrikes < 2) queuedStrikes++;
                return;
            }
            if (Time.time < nextAttackTime) return;
            if (!Ranged)
            {
                comboStage = queuedStrikes = 0; recovering = false; meleeActive = true;
                ResolvedStrikeCount = 0;
            }
            // Mage: aim along the 8-way stick/keys direction, then lock onto the nearest monster in range.
            if (Ranged)
            {
                // The member's command: the held direction for the local player, the target for AI companions.
                Vector2 held = owner.Command.aim;
                castAim = held.sqrMagnitude > 0.01f ? PlayerController.SnapTo8(held) : owner.AimDirection;
            }
            castTarget = Ranged ? FindTarget() : null;
            if (castTarget != null) owner.FaceTowards(castTarget.Position);
            else if (Ranged) owner.FaceTowards(owner.Position + castAim);
            attackStart = Time.time;
            attackEnd = attackStart + Duration;
            // [MERGE] Combo timing from the warrior rework, attack speed of this member (not always the local one).
            float cdMul = owner.Data.Stats.CooldownMultiplier;
            nextAttackTime = attackStart + (Ranged ? classInfo.cooldown * cdMul : Mathf.Max(Duration, stats.attackCooldown * cdMul));
            attackFacing = owner.Facing;
            hitResolved = false;
            hitThisSwing.Clear();

            weapon.enabled = weapon.sprite != null;
            weapon.transform.localScale = Vector3.one;
            slash.enabled = !Ranged && weapon.sprite != null;
            if (ySort == null) ySort = GetComponent<YSort>();
            // Weapon behind the head when swinging upwards.
            ySort?.SetLocalOrder(weapon, attackFacing.IsUp() ? -1 : 1);
            Game.Audio.PlaySfx(Ranged ? "magic" : "swing", owner.IsLocal ? 1f : 0.5f);
        }

        void Update()
        {
            if (!IsAttacking)
            {
                HoldPose();
                return;
            }

            float t = Mathf.Clamp01((Time.time - attackStart) / Duration);
            Vector2 dir = attackFacing.ToVector();
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (Ranged)
            {
                UpdateCast(t, dir, baseAngle);
                return;
            }
            UpdateMelee();
        }

        void UpdateMelee()
        {
            // Resolve the contact before crossing a stage boundary, including slow frames.
            for (int transition = 0; transition < 5 && meleeActive; transition++)
            {
                float t = Mathf.Clamp01((Time.time - attackStart) / Duration);
                if (!recovering && !hitResolved && t >= WarriorAttackMotion.Contact)
                {
                    hitResolved = true; ResolvedStrikeCount++;
                    ResolveHits(attackFacing.ToVector());
                    if (weapon.sprite != null) WarriorFlameSlash.Burst(owner.Center + attackFacing.ToVector() * .6f, attackFacing.ToVector(), comboStage);
                }
                if (Time.time < attackEnd) break;
                float boundary = attackEnd;
                if (recovering)
                {
                    meleeActive = false; recovering = false;
                    nextAttackTime = Mathf.Max(nextAttackTime, boundary);
                    HoldPose(); return;
                }
                if (queuedStrikes > 0 && comboStage < 2)
                {
                    queuedStrikes--; comboStage++;
                    hitResolved = false; hitThisSwing.Clear();
                    Game.Audio.PlaySfx("swing");
                }
                else recovering = true;
                attackStart = boundary;
                attackEnd = attackStart + Duration;
            }
            UpdateMeleeTrail();
        }

        void UpdateMeleeTrail()
        {
            float t = AttackProgress;
            var pose = SilverWarriorArt.Pose(SilverWarriorArt.ViewKey(attackFacing), AttackFrame);
            float sign = WarriorAttackMotion.ReverseCut(attackFacing) ? -1 : 1;
            if (comboStage == 1) sign = -sign;
            slash.enabled = !recovering && weapon.sprite != null;
            slash.transform.localPosition = WarriorRightHandRig.WorldHand(pose);
            slash.sprite = WarriorFlameSlash.Get(comboStage, t);
            SilverWarriorPresentation.ApplyMaterial(slash);
            slash.transform.localRotation = Quaternion.Euler(0, 0, pose.swordAngle);
            slash.transform.localScale = new Vector3(1, sign, 1);
            ySort?.SetLocalOrder(slash, pose.rightHandBack ? -1 : 3);
            var c = slash.color; c.a = recovering ? 0 : WarriorAttackMotion.TrailAlpha(t); slash.color = c;
        }

        /// <summary>Mage: raise the staff towards the facing direction, then release a bolt.</summary>
        void UpdateCast(float t, Vector2 dir, float baseAngle)
        {
            // Staff tilts forward (from upright to ~45° into the facing direction).
            float tilt = Mathf.Lerp(0f, 45f, Mathf.Sin(Mathf.Min(t, 0.6f) / 0.6f * Mathf.PI * 0.5f));
            float side = dir.x != 0f ? -Mathf.Sign(dir.x) : 0f;
            weapon.transform.localPosition = new Vector3(0f, 0.35f, 0f) + (Vector3)(dir * 0.3f);
            weapon.transform.localRotation = Quaternion.Euler(0f, 0f, dir.x != 0f ? side * tilt : -tilt * 0.3f);

            if (!hitResolved && t >= 0.35f)
            {
                hitResolved = true;
                Vector2 origin = owner.Center + castAim * 0.55f;
                bool locked = castTarget != null && !castTarget.IsDead && castTarget.isActiveAndEnabled;
                Vector2 aim = locked ? castTarget.Center - origin : castAim;
                MagicBolt.Fire(owner.gameObject, origin, aim, classInfo, locked ? castTarget : null,
                    owner.Data.Stats.AttackDamage(owner.Class));
                Fx.Sparkle(origin, 2, 0.2f);
                castTarget = null;
            }
        }

        /// <summary>
        /// Nearest living monster within the bolt's range that is not hidden behind a wall or cliff.
        /// Monsters roughly in front of the player win ties, so turning towards one still picks it.
        /// </summary>
        EnemyController FindTarget()
        {
            Vector2 from = owner.Center;
            Vector2 facing = castAim;
            float range = classInfo.boltRange;
            EnemyController best = null;
            float bestScore = float.MaxValue;
            foreach (var enemy in EnemyController.Active)
            {
                if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) continue;
                Vector2 to = enemy.Center - from;
                float dist = to.magnitude;
                if (dist > range) continue;
                // Enemies behind the player count as a bit further away.
                float score = dist * (Vector2.Dot(to.normalized, facing) > 0.3f ? 1f : 1.35f);
                if (score >= bestScore || !HasLineOfSight(from, enemy)) continue;
                best = enemy;
                bestScore = score;
            }
            return best;
        }

        readonly RaycastHit2D[] losHits = new RaycastHit2D[16];

        bool HasLineOfSight(Vector2 from, EnemyController enemy)
        {
            Vector2 to = enemy.Center - from;
            var losFilter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.Raycast(from, to.normalized, losFilter, losHits, to.magnitude);
            for (int i = 0; i < count; i++)
            {
                var col = losHits[i].collider;
                if (col == null || col.attachedRigidbody != null) continue; // characters don't block
                if (col.gameObject.name == "Water") continue;               // bolts fly over water
                if (col.GetComponentInParent<IDamageable>() != null) continue; // trees/rocks: bolt would hit them anyway
                return false;
            }
            return true;
        }

        void ResolveHits(Vector2 dir)
        {
            Vector2 center = owner.Center + dir * stats.attackReach;
            overlap.Clear();
            Physics2D.OverlapCircle(center, stats.attackRadius, filter, overlap);

            bool landed = false;
            foreach (var col in overlap)
            {
                if (col == null || col.attachedRigidbody != null && col.attachedRigidbody.gameObject == owner.gameObject) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || ReferenceEquals(target, owner) || hitThisSwing.Contains(target)) continue;
                // Party members are never hurt (PlayerController also refuses), and only the local player harvests trees and rocks.
                if (target is PlayerController || (!owner.IsLocal && target is ResourceNode)) continue;
                hitThisSwing.Add(target);
                var info = new DamageInfo(owner.Data.Stats.AttackDamage(owner.Class), owner.Center, stats.attackKnockback, Team.Player, owner.gameObject);
                landed |= target.TakeDamage(info);
            }
            if (landed && owner.IsLocal) Game.Camera?.Shake(0.06f, 0.1f);
        }

        void OnDrawGizmosSelected()
        {
            if (owner == null || stats == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(owner.Center + owner.Facing.ToVector() * stats.attackReach, stats.attackRadius);
        }
    }
}
