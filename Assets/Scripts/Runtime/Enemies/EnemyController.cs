using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Melee enemy AI (the slice's skeleton): wander → notice player → chase → telegraphed swing.
    /// All numbers come from an <see cref="EnemyStats"/> asset, and visuals from a CharacterLook,
    /// so new melee enemy types are data only.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public partial class EnemyController : MonoBehaviour, IDamageable // [MONSTER] partial: monster/boss layer in EnemyController.Monster.cs
    {
        enum State
        {
            Idle,
            Wander,
            Chase,
            Windup,
            Recover,
            Hurt,
            ReturnHome,
            Dead,
        }

        EnemyStats stats;
        Rigidbody2D body;
        Health health;
        CharacterAnimator animator;
        HitFlash flash;
        SpriteRenderer alertIcon;
        Transform visual;
        Collider2D bodyCollider;

        State state = State.Idle;
        float stateUntil;
        Vector2 home;
        Vector2 wanderTarget;
        Vector2 desiredVelocity;
        Vector2 knockbackVelocity;
        Facing facing = Facing.Down;
        float stuckTimer;
        Vector2 lastPosition;

        public event Action<EnemyController> Died;
        public Vector2 Position => body.position;
        public bool IsDead => state == State.Dead;
        /// <summary>Centre of the body (the transform sits at the feet) — what spells aim at.</summary>
        public Vector2 Center => Position + new Vector2(0f, 0.4f);

        /// <summary>Every enemy currently in the world (used by the mage's auto-targeting).</summary>
        public static readonly System.Collections.Generic.List<EnemyController> Active = new System.Collections.Generic.List<EnemyController>();

        void OnEnable() => Active.Add(this);

        void OnDisable() => Active.Remove(this);

        /// <summary>(enemy, xp reward) — the player listens to gain experience.</summary>
        public static event Action<EnemyController, int> Killed;

        float frozenUntil;
        public bool IsFrozen => Time.time < frozenUntil;

        float stunnedUntil;
        public bool IsStunned => Time.time < stunnedUntil;

        /// <summary>Stops the enemy for a moment without the ice look (war cry, thunder).</summary>
        public void Stun(float seconds)
        {
            if (state == State.Dead || seconds <= 0f) return;
            seconds = MonsterCrowdControl(seconds); // [MONSTER] groggy damage, bosses ×0.3
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            if (state == State.Windup) EnterState(State.Recover, seconds);
            desiredVelocity = Vector2.zero;
        }

        /// <summary>Stops the enemy completely (Frost Nova). The body turns icy blue.</summary>
        public void Freeze(float seconds)
        {
            if (state == State.Dead || seconds <= 0f) return;
            seconds = MonsterCrowdControl(seconds); // [MONSTER] groggy damage, bosses ×0.3
            frozenUntil = Mathf.Max(frozenUntil, Time.time + seconds);
            if (state == State.Windup) EnterState(State.Recover, seconds);
            desiredVelocity = Vector2.zero;
        }

        public static EnemyController Create(EnemyStats stats, CharacterLook look, Vector2 position, Transform parent)
        {
            var go = new GameObject(stats.enemyId);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var body = PhysicsCompat.AddTopDownBody(go, RigidbodyType2D.Dynamic);
            body.mass = 2f;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.3f;
            col.offset = new Vector2(0f, 0.25f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(visual, false);
            shadow.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            shadow.sprite = Game.Art.Get("shadow");
            shadow.sortingOrder = -2;
            var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(visual, false);
            var alert = new GameObject("Alert").AddComponent<SpriteRenderer>();
            alert.transform.SetParent(visual, false);
            alert.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            alert.sprite = Game.Art.Get("fx_alert");
            alert.sortingOrder = 5;
            alert.enabled = false;

            var enemy = go.AddComponent<EnemyController>();
            enemy.stats = stats;
            enemy.body = body;
            enemy.visual = visual;
            enemy.bodyCollider = col;
            enemy.alertIcon = alert;
            enemy.home = position;
            enemy.lastPosition = position;
            enemy.health = go.AddComponent<Health>();
            enemy.health.Init(stats.maxHealth, stats.maxHealth, stats.invulnerableTime);
            enemy.animator = go.AddComponent<CharacterAnimator>();
            enemy.animator.Setup(look, sr);
            enemy.flash = go.AddComponent<HitFlash>();
            enemy.flash.SetTargets(sr);
            go.AddComponent<YSort>().Configure(false);
            // Added after YSort so the bar is not depth-sorted with the body (it always draws on top).
            go.AddComponent<EnemyHealthBar>().Setup(enemy.health, 1.2f);
            enemy.health.Damaged += info => DamageNumber.Show(enemy.Position + new Vector2(0f, 1.55f), info.amount);
            enemy.EnterState(State.Idle, UnityEngine.Random.Range(0.5f, 2f));
            return enemy;
        }

        void EnterState(State next, float duration = 0f)
        {
            state = next;
            stateUntil = Time.time + duration;
            alertIcon.enabled = next == State.Windup;
        }

        PlayerController Target
        {
            get
            {
                var p = Game.Player;
                return p != null && !p.IsDead && Game.IsPlaying ? p : null;
            }
        }

        void Update()
        {
            if (state == State.Dead) return;
            if (IsFrozen)
            {
                desiredVelocity = Vector2.zero;
                animator.Renderer.color = new Color(0.6f, 0.85f, 1f, 1f);
                return;
            }
            if (animator.Renderer.color != Color.white && animator.Renderer.color.a >= 1f) animator.Renderer.color = Color.white;
            if (IsStunned)
            {
                desiredVelocity = Vector2.zero;
                animator.Play(CharacterAnim.Hurt, facing);
                return;
            }
            if (!Game.IsPlaying)
            {
                desiredVelocity = Vector2.zero;
                animator.Play(CharacterAnim.Idle, facing);
                if (state == State.Windup) EnterState(State.Idle, 1f);
                return;
            }

            var target = Target;
            float distToPlayer = target != null ? Vector2.Distance(Position, target.Position) : float.MaxValue;
            float distFromHome = Vector2.Distance(Position, home);

            // [MONSTER] A pluggable behaviour (ranged, charge, summon, guard, boss patterns) owns the frame once engaged.
            if (MonsterTick()) return;

            switch (state)
            {
                case State.Idle:
                    desiredVelocity = Vector2.zero;
                    if (distToPlayer < stats.detectRadius) StartChase();
                    else if (Time.time >= stateUntil) PickWanderTarget();
                    break;

                case State.Wander:
                    MoveTowards(wanderTarget, stats.wanderSpeed);
                    if (distToPlayer < stats.detectRadius) StartChase();
                    else if (Vector2.Distance(Position, wanderTarget) < 0.15f || Time.time >= stateUntil || IsStuck())
                        EnterState(State.Idle, UnityEngine.Random.Range(stats.wanderPauseRange.x, stats.wanderPauseRange.y));
                    break;

                case State.Chase:
                    if (target == null || distToPlayer > stats.loseInterestRadius || distFromHome > stats.leashRadius)
                    {
                        EnterState(State.ReturnHome, 6f);
                        break;
                    }
                    MoveTowards(target.Position, stats.chaseSpeed);
                    if (distToPlayer <= stats.attackRange)
                    {
                        facing = FacingExtensions.FromVector(target.Position - Position, facing);
                        EnterState(State.Windup, stats.windupTime);
                        Game.Audio.PlaySfx("enemy_windup");
                    }
                    break;

                case State.Windup:
                    desiredVelocity = Vector2.zero;
                    if (Time.time >= stateUntil) Strike(target);
                    break;

                case State.Recover:
                case State.Hurt:
                    desiredVelocity = Vector2.zero;
                    if (Time.time >= stateUntil)
                    {
                        if (distToPlayer < stats.loseInterestRadius) EnterState(State.Chase);
                        else EnterState(State.Idle, 1f);
                    }
                    break;

                case State.ReturnHome:
                    MoveTowards(home, stats.wanderSpeed * 1.4f);
                    if (distToPlayer < stats.detectRadius * 0.7f && distFromHome < stats.leashRadius * 0.8f) StartChase();
                    else if (distFromHome < 0.3f || Time.time >= stateUntil || IsStuck()) EnterState(State.Idle, 1.5f);
                    break;
            }

            UpdateAnimation();
        }

        void FixedUpdate()
        {
            if (state == State.Dead)
            {
                body.SetVelocity(Vector2.zero);
                return;
            }
            if (state == State.Hurt && Time.time < stateUntil - stats.hurtStunTime * 0.5f)
            {
                body.SetVelocity(knockbackVelocity);
                return;
            }
            body.SetVelocity(Vector2.MoveTowards(body.GetVelocity(), desiredVelocity, MonsterAcceleration * Time.fixedDeltaTime)); // [MONSTER] dashes accelerate faster
        }

        void StartChase()
        {
            if (state != State.Chase)
            {
                alertIcon.enabled = true;
                Invoke(nameof(HideAlert), 0.4f);
            }
            state = State.Chase;
        }

        void HideAlert()
        {
            if (state != State.Windup) alertIcon.enabled = false;
        }

        void PickWanderTarget()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 candidate = home + UnityEngine.Random.insideUnitCircle * stats.wanderRadius;
                if (Physics2D.OverlapCircle(candidate + new Vector2(0f, 0.25f), 0.3f) != null) continue;
                wanderTarget = candidate;
                EnterState(State.Wander, 4f);
                return;
            }
            EnterState(State.Idle, 1f);
        }

        void MoveTowards(Vector2 point, float speed)
        {
            Vector2 delta = point - Position;
            if (delta.sqrMagnitude < 0.01f)
            {
                desiredVelocity = Vector2.zero;
                return;
            }
            desiredVelocity = delta.normalized * speed;
            facing = FacingExtensions.FromVector(delta, facing);
        }

        bool IsStuck()
        {
            float moved = Vector2.Distance(Position, lastPosition);
            lastPosition = Position;
            if (desiredVelocity.sqrMagnitude > 0.1f && moved < 0.002f) stuckTimer += Time.deltaTime;
            else stuckTimer = 0f;
            return stuckTimer > 0.6f;
        }

        void Strike(PlayerController target)
        {
            alertIcon.enabled = false;
            animator.Play(CharacterAnim.Attack, facing);
            Game.Audio.PlaySfx("enemy_attack");
            Vector2 hitCenter = Position + new Vector2(0f, 0.4f) + facing.ToVector() * 0.55f;
            Fx.Spawn("fx_slash", hitCenter, Vector2.zero, 0f, 0.15f, 0f, 10);
            if (target != null && Vector2.Distance(target.Center, hitCenter) < stats.attackRange * 0.9f + 0.35f)
            {
                target.TakeDamage(new DamageInfo(stats.attackDamage, Position, stats.attackKnockback, Team.Enemy));
            }
            EnterState(State.Recover, stats.recoverTime);
        }

        void UpdateAnimation()
        {
            switch (state)
            {
                case State.Windup:
                    animator.Play(CharacterAnim.Attack, facing);
                    // Shake in place to telegraph the attack.
                    visual.localPosition = new Vector3(Mathf.Sin(Time.time * 60f) * 0.03f, 0f, 0f);
                    return;
                case State.Hurt:
                    animator.Play(CharacterAnim.Hurt, facing);
                    break;
                case State.Recover:
                    if (Time.time > stateUntil - stats.recoverTime + 0.2f) animator.Play(CharacterAnim.Idle, facing);
                    break;
                default:
                    animator.Play(desiredVelocity.sqrMagnitude > 0.05f ? CharacterAnim.Walk : CharacterAnim.Idle, facing);
                    break;
            }
            visual.localPosition = Vector3.zero;
        }

        public bool TakeDamage(DamageInfo info)
        {
            if (state == State.Dead || info.team == Team.Enemy) return false;
            if (!MonsterBeforeDamage(ref info)) return false; // [MONSTER] shield guard, groggy +30%, totem rules
            if (!health.TryDamage(info)) return false;

            Vector2 away = Position - info.sourcePosition;
            if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
            knockbackVelocity = away.normalized * MonsterKnockback(info); // [MONSTER] follows DamageInfo.knockback
            flash.Flash(0.12f);
            Game.Audio.PlaySfx("hit");
            Fx.Burst("fx_bone", Position + new Vector2(0f, 0.5f), 2, 2.5f, 0.5f);

            if (health.IsDead)
            {
                Die();
                return true;
            }
            if (MonsterAfterHit(info)) return true; // [MONSTER] groggy gauge; super armor skips Hurt / knockback
            EnterState(State.Hurt, stats.hurtStunTime);
            return true;
        }

        void Die()
        {
            EnterState(State.Dead);
            alertIcon.enabled = false;
            bodyCollider.enabled = false;
            body.SetVelocity(Vector2.zero);
            Game.Audio.PlaySfx("enemy_die");
            Fx.Burst("fx_bone", Position + new Vector2(0f, 0.4f), 6, 3.5f, 0.9f);
            Fx.Burst("fx_dust", Position + new Vector2(0f, 0.2f), 4, 1.5f, 0.5f);
            GameEvents.RaiseEnemyKilled(stats.enemyId);
            Died?.Invoke(this);
            Killed?.Invoke(this, stats.xpReward);
            DropLoot();
            StartCoroutine(DeathRoutine());
        }

        /// <summary>Chance of dropping one piece of equipment the player's class can use.</summary>
        const float EquipmentDropChance = 0.4f;

        void DropLoot()
        {
            if (Game.Player == null || transform.parent == null) return;
            if (!MonsterLoot()) return; // [MONSTER] summons/totems drop nothing; gold skeletons drop extra gold
            // Drop into the world's object root so it survives this enemy being destroyed.
            var parent = Game.World != null && Game.World.ObjectsRoot != null ? Game.World.ObjectsRoot : transform.parent;
            // Gold for the village shops.
            Pickup.Create(ConsumableDatabase.Gold, UnityEngine.Random.Range(8, 17), Position + new Vector2(0f, 0.2f), parent);
            // Enhancement materials (bag → "기타" tab).
            foreach (var mat in EquipmentDatabase.AllMaterials)
            {
                if (UnityEngine.Random.value > mat.dropChance) continue;
                int n = UnityEngine.Random.Range(mat.minDrop, mat.maxDrop + 1);
                for (int i = 0; i < n; i++) Pickup.Create(mat.id, 1, Position + new Vector2(0f, 0.2f), parent);
            }
            string id = EquipmentDatabase.RollDrop(Game.Player.Class, EquipmentDropChance);
            if (id == null) return;
            Pickup.Create(id, 1, Position + new Vector2(0f, 0.2f), parent);
            var item = EquipmentDatabase.Get(id);
            if (item != null && item.rarity >= ItemRarity.Rare) Fx.Sparkle(Position + Vector2.up * 0.5f, 4, 0.5f);
        }

        System.Collections.IEnumerator DeathRoutine()
        {
            var sr = animator.Renderer;
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                visual.localScale = new Vector3(1f + t * 0.6f, Mathf.Max(0.1f, 1f - t * 1.8f), 1f) * visualScale; // [MONSTER] size scale
                var c = sr.color;
                c.a = 1f - t / 0.5f;
                sr.color = c;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
