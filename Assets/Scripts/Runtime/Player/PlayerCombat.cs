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
        /// <summary>[ROGUE] The second dagger (left hand); hidden for single-weapon classes.</summary>
        SpriteRenderer weaponLeft;
        public float AttackProgress => IsAttacking ? Mathf.Clamp01((Time.time - attackStart) / Duration) : 0f;
        public SpriteRenderer WeaponRenderer => weapon;
        public SpriteRenderer GripRenderer => silverGrip;
        public SpriteRenderer SlashRenderer => slash;
        void LateUpdate()
        {
            if (silverGrip == null || owner == null) return;
            // [ROGUE] The rogue's body is the same sheet-drawn rig, so its daggers follow the drawing's hands too.
            bool silver = owner.Class == CharacterClass.Warrior || owner.Class == CharacterClass.Rogue;
            silverGrip.enabled = silver && !owner.IsDead && weapon.sprite != null;
            if (weaponLeft != null && !(silver && classInfo.dualWield)) weaponLeft.enabled = false;
            if (!silver) return;
            var facing = characterAnimator!=null?characterAnimator.PresentationFacing(owner.Facing):owner.Facing;
            SilverWarriorPresentation.Pose(weapon, silverGrip, characterAnimator, facing, !owner.IsDead);
            if (weaponLeft != null && classInfo.dualWield) SilverWarriorPresentation.PoseLeft(weaponLeft, weapon, characterAnimator, facing, !owner.IsDead);
            // [ROGUE] The rogue sheets already draw both daggers in the hands: the equipped weapon still counts for stats,
            // but no second picture goes on top (it showed two blades per hand).
            if (ArtDrawsWeapon) { weapon.enabled = false; if (weaponLeft != null) weaponLeft.enabled = false; }
        }

        /// <summary>[ROGUE] The body drawing holds its own blades (rogue sheets), so the weapon overlay stays hidden.</summary>
        bool ArtDrawsWeapon => characterAnimator != null && characterAnimator.Look != null && SilverWarriorArt.IsRogue(characterAnimator.Look.id);

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

        /// <summary>Basic attacks hit 30% more often than before and each hit does 80% (slightly more damage per second, snappier).
        /// [ROGUE] Per class now (CharacterClassInfo.basicHitScale): warrior and mage 0.8, rogue 0.65.</summary>
        public float BasicHitScale => classInfo != null ? classInfo.basicHitScale : 0.8f;
        int BasicHitDamage => Mathf.Max(1, Mathf.RoundToInt(owner.Data.Stats.AttackDamage(owner.Class) * BasicHitScale));
        // [ROGUE] Melee numbers per class; the warrior keeps PlayerStats (classInfo values below 0).
        float MeleeInterval => classInfo != null && classInfo.meleeInterval > 0 ? classInfo.meleeInterval : stats.attackCooldown;
        /// <summary>Swing stages scale with the strike interval (rogue 0.24 / 0.27 s), so the combo keeps its shape.</summary>
        float SwingScale => classInfo != null && classInfo.meleeInterval > 0 ? classInfo.meleeInterval / stats.attackCooldown : 1f;
        float MeleeReach => classInfo != null && classInfo.meleeReach > 0 ? classInfo.meleeReach : stats.attackReach;
        float MeleeRadius => classInfo != null && classInfo.meleeRadius > 0 ? classInfo.meleeRadius : stats.attackRadius;
        float MeleeKnockback => classInfo != null && classInfo.meleeKnockback > 0 ? classInfo.meleeKnockback : stats.attackKnockback;
        float Duration
        {
            get
            {
                if (Ranged) return classInfo.castDuration;
                if (recovering) return WarriorAttackMotion.RecoveryDuration * SwingScale;
                return Mathf.Max(WarriorAttackMotion.StageDuration(comboStage), stats.attackDuration) * SwingScale;
            }
        }

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
            weapon.sprite = classInfo.id == CharacterClass.Warrior ? Game.Art.GetWarriorWeapon(weaponKey)
                : classInfo.id == CharacterClass.Rogue ? Game.Art.GetRogueWeapon(weaponKey, owner.Data.Progression.IsPromoted && owner.Data.Progression.Career == Career.Ninja) // [NINJA] kunai in hand
                : Game.Art.Get(EquipmentDatabase.WeaponSprite(weaponKey, classInfo.id));
            weapon.enabled = !owner.IsDead && weapon.sprite != null;
            if (weaponLeft != null)
            {
                weaponLeft.sprite = classInfo.dualWield ? weapon.sprite : null;
                weaponLeft.enabled = classInfo.dualWield && weapon.enabled;
                if (weaponLeft.enabled) HdMaterial.Apply(weaponLeft);
            }
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
                    case Facing.Up: pos = new Vector2(-0.24f, 0.06f); rot = 6f; order = -1; break;
                    case Facing.Down: pos = new Vector2(0.19f, 0.04f); rot = -6f; order = 1; break; // follows the body nudged 2 px left (CharacterAnimator)
                    case Facing.UpLeft:
                    case Facing.UpRight: pos = new Vector2(0.24f * sx, 0.08f); rot = -6f * sx; order = -1; break;
                    default: pos = new Vector2(0.24f * sx, 0.04f); rot = -6f * sx; order = 1; break;
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

            weaponLeft = new GameObject("WeaponLeft").AddComponent<SpriteRenderer>();
            weaponLeft.transform.SetParent(visualRoot, false);
            weaponLeft.enabled = false;

            slash = new GameObject("Slash").AddComponent<SpriteRenderer>();
            slash.transform.SetParent(visualRoot, false);
            slash.sprite = Game.Art.Get("fx_slash");
            slash.sortingOrder = 3;
            slash.enabled = false;

            gameObject.AddComponent<WeaponEnhanceVfx>().Setup(owner, weapon);
            filter = new ContactFilter2D { useTriggers = true };
        }

        public void Cancel()
        {
            attackEnd = -10f;
            meleeActive = recovering = false;
            queuedStrikes = comboStage = 0;
            nextAttackTime = 0;
            if (slash != null) slash.enabled = false;
        }

        /// <summary>[PARTY NET] Any member pressed attack (the host replays it on member PCs).</summary>
        public static event System.Action<PlayerController> AttackPressed;

        public void TryAttack()
        {
            if (owner.IsDashing) return;
            AttackPressed?.Invoke(owner);
            if (GetComponent<CareerCombat>()?.TryNinjaBasic() == true) return; // [NINJA] 투척술: the basic attack is a kunai throw
            if (!Ranged && meleeActive)
            {
                // Each key-down buys exactly one additional strike; holding the key does not repeat.
                if (!recovering && comboStage + queuedStrikes < 2) queuedStrikes++;
                return;
            }
            if (Time.time < nextAttackTime) return;
            if (!Ranged)
            {
                comboStage = queuedStrikes = 0;
                recovering = false;
                meleeActive = true;
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
            float cooldownMultiplier = owner.Data.Stats.CooldownMultiplier;
            nextAttackTime = attackStart + (Ranged ? classInfo.cooldown * cooldownMultiplier : Mathf.Max(Duration, MeleeInterval * cooldownMultiplier));
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
                    hitResolved = true;
                    ResolvedStrikeCount++;
                    ResolveHits(attackFacing.ToVector());
                    GetComponent<CareerCombat>()?.OnBasicStrike(attackFacing.ToVector(), comboStage); // [FIGHTER] 검기 태세
                    if (weapon.sprite != null) WarriorFlameSlash.Burst(owner.Center + attackFacing.ToVector() * .6f, attackFacing.ToVector(), comboStage);
                }
                if (Time.time < attackEnd) break;
                float stageEnd = attackEnd;
                if (recovering)
                {
                    meleeActive = false;
                    recovering = false;
                    nextAttackTime = Mathf.Max(nextAttackTime, stageEnd);
                    HoldPose();
                    return;
                }
                if (queuedStrikes > 0 && comboStage < 2)
                {
                    queuedStrikes--;
                    comboStage++;
                    hitResolved = false;
                    hitThisSwing.Clear();
                    Game.Audio.PlaySfx("swing");
                }
                else recovering = true;
                attackStart = stageEnd;
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
            // [VFX 2026-10-29] The rogue's dagger swing is shorter (0.24 s) and its trail only flashed: a bigger trail that
            // stays visible over more of the swing (picture and timing of the hit unchanged).
            bool rogueTrail = classInfo != null && classInfo.id == CharacterClass.Rogue;
            float trailScale = rogueTrail ? 1.25f : 1f;
            slash.transform.localScale = new Vector3(trailScale, sign * trailScale, 1);
            ySort?.SetLocalOrder(slash, pose.rightHandBack ? -1 : 3);
            var trailColor = slash.color;
            trailColor.a = recovering ? 0 : rogueTrail ? RogueTrailAlpha(t) : WarriorAttackMotion.TrailAlpha(t);
            slash.color = trailColor;
        }

        /// <summary>[VFX 2026-10-29] Rogue trail alpha: shown over t 0.12..0.97 (warrior 0.26..0.82), held near full in the middle.</summary>
        static float RogueTrailAlpha(float t) => t < .12f || t > .97f ? 0f : Mathf.Min(.95f, Mathf.Sin(Mathf.InverseLerp(.12f, .97f, t) * Mathf.PI) * 1.4f);

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
                    BasicHitDamage);
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

        /// <summary>Navigation-only preview of the existing melee circle against a target's actual collider.</summary>
        internal bool CanMeleeContact(EnemyController target, Vector2 aim)
        {
            var collider = target != null ? target.GetComponent<Collider2D>() : null;
            if (collider == null || !collider.enabled) return true;
            Vector2 dir = FacingExtensions.FromVector(aim, owner.Facing).ToVector();
            Vector2 center = owner.Center + dir * MeleeReach;
            return Vector2.Distance(center, collider.ClosestPoint(center)) <= Mathf.Max(0f, MeleeRadius - .02f);
        }

        void ResolveHits(Vector2 dir)
        {
            Vector2 center = owner.Center + dir * MeleeReach;
            overlap.Clear();
            Physics2D.OverlapCircle(center, MeleeRadius, filter, overlap);

            bool landed = false;
            foreach (var col in overlap)
            {
                if (col == null || col.attachedRigidbody != null && col.attachedRigidbody.gameObject == owner.gameObject) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || ReferenceEquals(target, owner) || hitThisSwing.Contains(target)) continue;
                // Party members are never hurt (PlayerController also refuses).
                if (target is PlayerController) continue;
                hitThisSwing.Add(target);
                int basic = target is EnemyController boss && boss.IsBoss ? Mathf.RoundToInt(BasicHitDamage * owner.Data.Stats.BossMul) : BasicHitDamage; // [LEGEND] 토벌형
                var info = new DamageInfo(basic, owner.Center, MeleeKnockback, Team.Player, owner.gameObject);
                bool hitIt = target.TakeDamage(info);
                landed |= hitIt;
                // Hit feel: sparks where the blow lands.
                if (hitIt && target is EnemyController e) SkillVisuals.Sparks(e.Center, new Color(1f, 0.92f, 0.7f, 0.95f), 4, 3f);
            }
            if (landed && owner.IsLocal)
            {
                Game.Camera?.Shake(0.08f, 0.1f);
                SkillCaster.TryHitStop(this, 0.022f);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (owner == null || stats == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(owner.Center + owner.Facing.ToVector() * MeleeReach, MeleeRadius);
        }
    }
}
