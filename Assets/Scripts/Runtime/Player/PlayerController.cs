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
            Game.Session.Equipment.Changed += player.RefreshStats;
            Game.Session.Equipment.Changed += player.ApplyGear;
            Game.Session.Progression.Changed += player.RefreshStats;
            Game.Session.Progression.LeveledUp += player.OnLevelUp;
            EnemyController.Killed += player.OnEnemyKilled;
            player.skills = go.AddComponent<SkillCaster>();
            player.skills.Setup(player);
            player.animator.Footstep += () => { if (Game.IsPlaying) Fx.Dust(player.Position); };
            return player;
        }

        public CharacterClass Class { get; private set; } = CharacterClass.Warrior;
        public SkillCaster Skills => skills;
        SkillCaster skills;

        public int Mana => Mathf.FloorToInt(Game.Session.PlayerMana);
        public int MaxMana => CharacterStats.MaxMp;

        /// <summary>Re-applies level, passive tree and gear: new max HP/MP (gains are also filled).</summary>
        public void RefreshStats()
        {
            if (health == null || IsDead || !gameObject.activeInHierarchy)
                return; // Spawn() reads the stats when the player enters the world
            int newMax = CharacterStats.MaxHp;
            int delta = newMax - health.Max;
            if (delta == 0) { ClampMana(); return; }
            health.SetMax(newMax, false);
            if (delta > 0) health.Heal(delta);
            ClampMana();
        }

        void ClampMana() => Game.Session.PlayerMana = Mathf.Clamp(Game.Session.PlayerMana, 0f, CharacterStats.MaxMp);

        /// <summary>Spends MP (or HP with the Blood Magic keystone). False if there is not enough.</summary>
        public bool TrySpend(int cost, bool useLife)
        {
            if (useLife)
            {
                if (health.Current <= cost) return false;
                health.Drain(cost);
                return true;
            }
            if (Game.Session.PlayerMana < cost) return false;
            Game.Session.PlayerMana -= cost;
            GameEvents.RaisePlayerHealthChanged(health.Current, health.Max);
            return true;
        }

        public void Heal(int amount) => health.Heal(amount);

        void OnEnemyKilled(EnemyController enemy, int xp)
        {
            if (IsDead) return;
            Game.Session.Progression.AddXp(xp);
            GameEvents.RaiseToast($"+{xp} EXP");
            if (CharacterStats.LifeOnKill > 0) health.Heal(CharacterStats.LifeOnKill);
            if (CharacterStats.ManaOnKill > 0) { Game.Session.PlayerMana += CharacterStats.ManaOnKill; ClampMana(); }
        }

        void OnLevelUp(int level)
        {
            RefreshStats();
            health.Heal(health.Max);
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            Game.Audio.PlaySfx("quest");
            Fx.Sparkle(Center + Vector2.up * 0.3f, 8, 0.9f);
            Game.Camera?.Shake(0.05f, 0.15f);
            GameEvents.RaiseToast($"레벨 업!  Lv.{level}   패시브 포인트 +1  (메뉴 → 스킬)");
            for (int s = 0; s < SkillGems.Slots; s++)
            {
                if (Progression.SlotLevel(s) != level) continue;
                var gem = SkillGems.ForSlot(Class, s);
                string key = Game.Input.GetBindingLabel(SkillGems.ActionFor(s));
                GameEvents.RaiseToast(s == SkillGems.UltimateSlot ? $"각성 기술 해금!  {gem?.name}  [{key}]" : $"스킬 {s + 1} 해금: {gem?.name}  [{key}]");
            }
            foreach (var g in SkillGems.All)
                if (g.kind == GemKind.Support && g.unlockLevel == level) GameEvents.RaiseToast($"새 보조 젬: {g.name}  (메뉴 → 스킬 → 스킬 젬)");
        }

        void OnDestroy()
        {
            if (Game.Session != null)
            {
                Game.Session.Equipment.Changed -= RefreshStats;
                Game.Session.Equipment.Changed -= ApplyGear;
                Game.Session.Progression.Changed -= RefreshStats;
                Game.Session.Progression.LeveledUp -= OnLevelUp;
            }
            EnemyController.Killed -= OnEnemyKilled;
        }

        /// <summary>
        /// Last movement direction snapped to 8 directions (incl. diagonals). The sprite only has
        /// 4 facings, but ranged attacks aim along this so the mage can shoot diagonally.
        /// </summary>
        public Vector2 AimDirection { get; private set; } = Vector2.down;

        /// <summary>Rounds a direction to the nearest of the 8 compass directions (unit length).</summary>
        public static Vector2 SnapTo8(Vector2 v)
        {
            if (v.sqrMagnitude < 0.0001f) return Vector2.down;
            float angle = Mathf.Round(Mathf.Atan2(v.y, v.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>Changes the playable character (look + attack style). Safe to call any time.</summary>
        public void SetClass(CharacterClass cls)
        {
            Class = cls;
            var info = CharacterClassInfo.Get(cls);
            animator.Setup(info.Look, animator.Renderer);
            combat.SetClass(info);
            flash.SetTargets(animator.Renderer);
            skills?.ResetCooldowns();
            CharacterStats.ClearBuffs();
            ApplyGear();
        }

        /// <summary>Worn top, bottom and weapon show on the character (necklace and rings don't).</summary>
        void ApplyGear()
        {
            if (animator == null || combat == null || Game.Session == null) return;
            var eq = Game.Session.Equipment;
            animator.SetLook(CharacterLook.WithGear(CharacterClassInfo.Get(Class).Look, eq[EquipSlot.Top], eq[EquipSlot.Bottom]));
            combat.RefreshWeapon();
        }

        public void Spawn(Vector2 position, Facing facing, int currentHealth, int maxHealth)
        {
            transform.position = position;
            body.position = position;
            body.SetVelocity(Vector2.zero);
            desiredVelocity = Vector2.zero;
            knockbackUntil = 0f;
            lockedUntil = 0f;
            IsDead = false;
            Facing = facing;
            AimDirection = facing.ToVector();
            visual.localRotation = Quaternion.identity;
            flash.Stop();
            combat.Cancel();
            // Max HP comes from base + level + passives + gear (the maxHealth argument is the base, kept for callers).
            health.Init(CharacterStats.MaxHp, currentHealth, stats.invulnerableTime);
            ClampMana();
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
            if (Game.Session.PlayerMana < CharacterStats.MaxMp)
            {
                Game.Session.PlayerMana = Mathf.Min(CharacterStats.MaxMp, Game.Session.PlayerMana + CharacterStats.ManaRegen * Time.deltaTime);
            }

            Vector2 move = Game.Input.Move;
            bool stunned = Time.time < knockbackUntil;
            bool channeling = Time.time < lockedUntil;
            if (channeling) move = Vector2.zero;
            if (combat.IsAttacking || skills.IsCasting) move *= 0.35f;
            else if (!stunned && move.sqrMagnitude > 0.01f)
            {
                Facing = FacingExtensions.FromVector(move, Facing);
                AimDirection = SnapTo8(move);
            }

            desiredVelocity = stunned ? Vector2.zero : move * stats.moveSpeed * CharacterStats.SpeedMultiplier;

            // Ignore action buttons on the frame a menu/dialogue closed, so the same press
            // doesn't immediately trigger an attack or re-open the conversation.
            if (!stunned && !channeling && !Game.State.ChangedThisFrame)
            {
                if (Game.Input.AttackPressed) combat.TryAttack();
                else if (Game.Input.InteractPressed) interactor.TryInteract();
                if (Game.Input.UseItemPressed) UseHealing();
                if (Game.Input.UseManaPressed) UseConsumable(ConsumableDatabase.MpPotion);
                if (Game.Input.TownScrollPressed) UseConsumable(ConsumableDatabase.TownScroll);
                for (int s = 0; s < SkillGems.Slots; s++)
                    if (Game.Input.SkillPressed(s)) { skills.TryCast(s); break; }
            }

            if (stunned) animator.Play(CharacterAnim.Hurt, Facing);
            else if (combat.IsAttacking || skills.IsCasting) animator.Play(CharacterAnim.Attack, Facing);
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

        /// <summary>Eats one carrot to heal (Q key, or clicking a carrot in the bag).</summary>
        public void TryEatCarrot()
        {
            var inventory = Game.Session.Inventory;
            if (health.Current >= health.Max)
            {
                GameEvents.RaiseToast("체력이 가득 차 있다.");
                return;
            }
            if (!inventory.Remove(ItemIds.Carrot, 1))
            {
                GameEvents.RaiseToast("체력 물약도 당근도 없다. 마을 잡화상인에게서 물약을 사자.");
                return;
            }
            health.Heal(stats.carrotHealAmount);
            Game.Audio.PlaySfx("heal");
            Fx.Sparkle(Center + Vector2.up * 0.4f, 2, 0.3f);
        }

        float lockedUntil;
        readonly System.Collections.Generic.Dictionary<string, float> potionReadyAt = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>Holds the character still (reading the return scroll).</summary>
        public void LockMovement(float seconds) => lockedUntil = Time.time + seconds;

        /// <summary>Q: a health potion if there is one, otherwise a carrot.</summary>
        public void UseHealing()
        {
            if (Game.Session.Inventory.Count(ConsumableDatabase.HpPotion) > 0) UseConsumable(ConsumableDatabase.HpPotion);
            else TryEatCarrot();
        }

        /// <summary>Uses one potion or scroll from the bag. Returns true when it was used up.</summary>
        public bool UseConsumable(string id)
        {
            var item = ConsumableDatabase.Get(id);
            if (item == null || IsDead) return false;
            var bag = Game.Session.Inventory;
            if (bag.Count(id) <= 0)
            {
                GameEvents.RaiseToast($"{item.name}{Josa(item.name, "이", "가")} 없다. 마을 잡화상인에게서 살 수 있다.");
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            switch (item.kind)
            {
                case ConsumableKind.HealHp:
                {
                    if (health.Current >= health.Max) { GameEvents.RaiseToast("체력이 가득 차 있다."); return false; }
                    if (!PotionReady(id)) return false;
                    bag.Remove(id, 1);
                    int amount = Mathf.Max(1, Mathf.RoundToInt(health.Max * item.power / 100f));
                    health.Heal(amount);
                    Game.Audio.PlaySfx("heal");
                    SkillVisuals.Flash(Center, new Color(1f, 0.35f, 0.35f, 0.55f), 1.6f, 0.3f);
                    Fx.Sparkle(Center + Vector2.up * 0.4f, 3, 0.4f);
                    GameEvents.RaiseToast($"체력 물약  <color=#ff8a8a>+{amount} HP</color>");
                    return true;
                }
                case ConsumableKind.HealMp:
                {
                    if (Game.Session.PlayerMana >= CharacterStats.MaxMp - 0.5f) { GameEvents.RaiseToast("MP가 가득 차 있다."); return false; }
                    if (!PotionReady(id)) return false;
                    bag.Remove(id, 1);
                    int amount = Mathf.Max(1, Mathf.RoundToInt(CharacterStats.MaxMp * item.power / 100f));
                    Game.Session.PlayerMana += amount;
                    ClampMana();
                    GameEvents.RaisePlayerHealthChanged(health.Current, health.Max);
                    Game.Audio.PlaySfx("heal");
                    SkillVisuals.Flash(Center, new Color(0.4f, 0.6f, 1f, 0.6f), 1.6f, 0.3f);
                    Fx.Sparkle(Center + Vector2.up * 0.4f, 3, 0.4f);
                    GameEvents.RaiseToast($"마나 물약  <color=#8ab8ff>+{amount} MP</color>");
                    return true;
                }
                case ConsumableKind.TownScroll:
                    return Game.Flow.UseTownScroll();
            }
            return false;
        }

        bool PotionReady(string id)
        {
            if (potionReadyAt.TryGetValue(id, out float at) && Time.time < at) return false;
            potionReadyAt[id] = Time.time + 0.6f;
            return true;
        }

        /// <summary>Korean particle after a word: consonant ending → <paramref name="withFinal"/> (이/을/은), vowel → <paramref name="withoutFinal"/>.</summary>
        public static string Josa(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word)) return withoutFinal;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return withFinal;
            return (c - 0xAC00) % 28 != 0 ? withFinal : withoutFinal;
        }

        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead || info.team == Team.Player) return false;
            // Armour / rings: a chance to shrug the hit off completely.
            if (!info.unblockable && !health.IsInvulnerable && Random.Range(0, 100) < CharacterStats.Block) // [MONSTER] unblockable skips the roll
            {
                Fx.Sparkle(Center + Vector2.up * 0.3f, 3, 0.35f);
                Game.Audio.PlaySfx("mine");
                GameEvents.RaiseToast("막았다!");
                return false;
            }
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
            Game.Session.PlayerMaxHealth += amount;
            RefreshStats();
        }

        void OnHealthChanged(int current, int max)
        {
            Game.Session.PlayerHealth = current;
            GameEvents.RaisePlayerHealthChanged(current, max);
        }

        public void FaceTowards(Vector2 point)
        {
            Facing = FacingExtensions.FromVector(point - Position, Facing);
            AimDirection = SnapTo8(point - Position);
            animator.Play(CharacterAnim.Idle, Facing);
        }
    }
}
