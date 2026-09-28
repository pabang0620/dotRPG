using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Player character: movement, facing, animation state and reacting to damage.
    /// Attacking and interacting are delegated to <see cref="PlayerCombat"/> and <see cref="PlayerInteractor"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour, IDamageable
    {
        [SerializeField] PlayerStats stats;

        Rigidbody2D body;
        Health health;
        CharacterAnimator animator;
        PlayerCombat combat;
        PlayerInteractor interactor;
        HitFlash flash;
        Transform visual;

        Vector2 desiredVelocity;
        Vector2 knockbackVelocity;
        float knockbackUntil;

        public PlayerStats Stats => stats;
        public Facing Facing { get; private set; } = Facing.Down;
        public Health Health => health;
        public PlayerInteractor Interactor => interactor;
        public bool IsDead { get; private set; }
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        /// <summary>Centre of the body (the transform sits at the feet).</summary>
        public Vector2 Center => Position + new Vector2(0f, 0.45f);

        /// <summary>Builds the player GameObject. Visual children can be replaced with a prefab later.</summary>
        public static PlayerController Create(GameConfig config, Transform parent)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            var body = PhysicsCompat.AddTopDownBody(go, RigidbodyType2D.Dynamic);
            body.mass = 1f;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.28f;
            col.offset = new Vector2(0f, 0.22f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);

            var shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(visual, false);
            shadow.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            shadow.sprite = Game.Art.Get("shadow");
            shadow.sortingOrder = -2;

            var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(visual, false);
            sr.sortingOrder = 0;

            var player = go.AddComponent<PlayerController>();
            player.stats = config.playerStats;
            player.body = body;
            player.visual = visual;
            player.health = go.AddComponent<Health>();
            player.animator = go.AddComponent<CharacterAnimator>();
            player.animator.Setup(CharacterLook.Player, sr);
            player.flash = go.AddComponent<HitFlash>();
            player.flash.SetTargets(sr);
            player.combat = go.AddComponent<PlayerCombat>();
            player.combat.Setup(player, visual);
            player.interactor = go.AddComponent<PlayerInteractor>();
            player.interactor.Setup(player);
            go.AddComponent<YSort>().Configure(false);

            player.health.Changed += player.OnHealthChanged;
            player.animator.Footstep += () => { if (Game.IsPlaying) Fx.Dust(player.Position); };
            return player;
        }

        public void Spawn(Vector2 position, Facing facing, int currentHealth, int maxHealth)
        {
            transform.position = position;
            body.position = position;
            body.SetVelocity(Vector2.zero);
            desiredVelocity = Vector2.zero;
            knockbackUntil = 0f;
            IsDead = false;
            Facing = facing;
            visual.localRotation = Quaternion.identity;
            flash.Stop();
            combat.Cancel();
            health.Init(maxHealth, currentHealth, stats.invulnerableTime);
            animator.Play(CharacterAnim.Idle, facing);
            GetComponent<YSort>().Refresh();
        }

        void Update()
        {
            if (IsDead) return;
            if (!Game.IsPlaying)
            {
                desiredVelocity = Vector2.zero;
                if (Game.State.Current != GameState.Dialogue) animator.Play(CharacterAnim.Idle, Facing);
                return;
            }

            Game.Session.PlayTimeSeconds += Time.deltaTime;

            Vector2 move = Game.Input.Move;
            bool stunned = Time.time < knockbackUntil;
            if (combat.IsAttacking) move *= 0.35f;
            else if (!stunned && move.sqrMagnitude > 0.01f) Facing = FacingExtensions.FromVector(move, Facing);

            desiredVelocity = stunned ? Vector2.zero : move * stats.moveSpeed;

            // Ignore action buttons on the frame a menu/dialogue closed, so the same press
            // doesn't immediately trigger an attack or re-open the conversation.
            if (!stunned && !Game.State.ChangedThisFrame)
            {
                if (Game.Input.AttackPressed) combat.TryAttack();
                else if (Game.Input.InteractPressed) interactor.TryInteract();
                if (Game.Input.UseItemPressed) TryEatCarrot();
            }

            if (stunned) animator.Play(CharacterAnim.Hurt, Facing);
            else if (combat.IsAttacking) animator.Play(CharacterAnim.Attack, Facing);
            else if (move.sqrMagnitude > 0.01f)
            {
                animator.Play(CharacterAnim.Walk, Facing);
                animator.SpeedMultiplier = Mathf.Clamp(move.magnitude, 0.5f, 1f);
            }
            else animator.Play(CharacterAnim.Idle, Facing);
        }

        void FixedUpdate()
        {
            if (IsDead)
            {
                body.SetVelocity(Vector2.zero);
                return;
            }
            if (Time.time < knockbackUntil)
            {
                body.SetVelocity(knockbackVelocity);
                return;
            }
            Vector2 v = Vector2.MoveTowards(body.GetVelocity(), desiredVelocity, stats.acceleration * Time.fixedDeltaTime);
            body.SetVelocity(v);
        }

        void TryEatCarrot()
        {
            var inventory = Game.Session.Inventory;
            if (health.Current >= health.Max)
            {
                GameEvents.RaiseToast("체력이 가득 차 있다.");
                return;
            }
            if (!inventory.Remove(ItemIds.Carrot, 1))
            {
                GameEvents.RaiseToast("당근이 없다. 밭에서 뽑아 오자.");
                return;
            }
            health.Heal(stats.carrotHealAmount);
            Game.Audio.PlaySfx("heal");
            Fx.Sparkle(Center + Vector2.up * 0.4f, 2, 0.3f);
        }

        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead || info.team == Team.Player) return false;
            if (!health.TryDamage(info)) return false;

            Vector2 away = Position - info.sourcePosition;
            if (away.sqrMagnitude < 0.0001f) away = -Facing.ToVector();
            knockbackVelocity = away.normalized * Mathf.Max(info.knockback, stats.knockbackSpeed);
            knockbackUntil = Time.time + stats.knockbackDuration;
            combat.Cancel();

            flash.Flash(0.1f);
            flash.Blink(stats.invulnerableTime);
            Game.Audio.PlaySfx("hurt");
            Game.Camera?.Shake(0.12f, 0.18f);

            if (health.IsDead) Die();
            return true;
        }

        void Die()
        {
            IsDead = true;
            desiredVelocity = Vector2.zero;
            animator.Play(CharacterAnim.Hurt, Facing);
            visual.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Game.Audio.PlaySfx("player_down");
            Game.Flow.OnPlayerDied();
        }

        public void HealFull() => health.Heal(health.Max);

        public void AddMaxHealth(int amount)
        {
            health.SetMax(health.Max + amount, true);
        }

        void OnHealthChanged(int current, int max)
        {
            Game.Session.PlayerHealth = current;
            Game.Session.PlayerMaxHealth = max;
            GameEvents.RaisePlayerHealthChanged(current, max);
        }

        public void FaceTowards(Vector2 point)
        {
            Facing = FacingExtensions.FromVector(point - Position, Facing);
            animator.Play(CharacterAnim.Idle, Facing);
        }
    }
}
