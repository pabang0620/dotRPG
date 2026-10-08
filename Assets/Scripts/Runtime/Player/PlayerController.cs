using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// A party member's character: movement, facing, animation state and reacting to damage. The same
    /// body is used by the local player (keyboard, <see cref="LocalInput"/>) and by AI companions
    /// (<see cref="CompanionBrain"/>): each frame it reads an <see cref="ActorCommand"/> from its
    /// <see cref="Input"/>. Per-member numbers live in <see cref="Data"/> (<see cref="CharacterData"/>).
    /// Attacking and interacting are delegated to <see cref="PlayerCombat"/> and <see cref="PlayerInteractor"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public partial class PlayerController : MonoBehaviour, IDamageable
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

        /// <summary>Class, level, gear, MP and buffs of this member (the local one wraps the session).</summary>
        public CharacterData Data { get; private set; }
        /// <summary>Where this member's commands come from (keyboard, AI, scripted test input).</summary>
        public IActorInput Input { get; set; }
        /// <summary>The command read this frame (also used by <see cref="PlayerCombat"/> for the mage's aim).</summary>
        public ActorCommand Command { get; private set; } = ActorCommand.None;
        /// <summary>True for the member this PC controls (camera, HUD, toasts, pickups, portals, interaction).</summary>
        public bool IsLocal => Data != null && Data.IsLocal;
        public string DisplayName => Data != null ? Data.DisplayName : name;
        public Collider2D BodyCollider { get; private set; }

        /// <summary>Builds the local player GameObject. Visual children can be replaced with a prefab later.</summary>
        public static PlayerController Create(GameConfig config, Transform parent)
        {
            var player = Build(config, parent, "Player", CharacterData.Session, new LocalInput(), CharacterLook.Player);
            Game.Session.Equipment.Changed += player.RefreshStats;
            Game.Session.Equipment.Changed += player.ApplyGear;
            Game.Session.Progression.Changed += player.RefreshStats;
            Game.Session.Progression.LeveledUp += player.OnLevelUp;
            return player;
        }

        /// <summary>Builds an AI (or, later, remote) party member with its own data. Call <see cref="Spawn"/> to place it.</summary>
        public static PlayerController CreateCompanion(GameConfig config, Transform parent, CharacterData data, IActorInput input)
        {
            var member = Build(config, parent, "Companion_" + data.Id, data, input, data.Look ?? CharacterClassInfo.Get(data.Class).Look);
            data.Equipment.Changed += member.RefreshStats;
            data.Equipment.Changed += member.ApplyGear;
            data.Progression.Changed += member.RefreshStats;
            member.interactor.enabled = false;
            member.SetClass(data.Class);
            data.Mana = data.Stats.MaxMp;
            return member;
        }

        static PlayerController Build(GameConfig config, Transform parent, string name, CharacterData data, IActorInput input, CharacterLook look)
        {
            var go = new GameObject(name);
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
            HdMaterial.Apply(shadow);

            var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(visual, false);
            sr.sortingOrder = 0;

            var player = go.AddComponent<PlayerController>();
            player.Data = data;
            player.Input = input;
            player.BodyCollider = col;
            player.stats = config.playerStats;
            player.body = body;
            player.visual = visual;
            player.health = go.AddComponent<Health>();
            player.animator = go.AddComponent<CharacterAnimator>();
            player.animator.Setup(look, sr);
            player.flash = go.AddComponent<HitFlash>();
            player.flash.SetTargets(sr);
            player.combat = go.AddComponent<PlayerCombat>();
            player.combat.Setup(player, visual);
            player.flash.SetTargets(sr, player.combat.WeaponRenderer, player.combat.GripRenderer);
            player.interactor = go.AddComponent<PlayerInteractor>();
            player.interactor.Setup(player);
            go.AddComponent<YSort>().Configure(false);

            player.health.Changed += player.OnHealthChanged;
            player.skills = go.AddComponent<SkillCaster>();
            player.skills.Setup(player);
            player.animator.Footstep += () => { if (Game.IsWorldRunning) Fx.Dust(player.Position); };
            return player;
        }

        public CharacterClass Class { get; private set; } = CharacterClass.Warrior;
        public SkillCaster Skills => skills;
        SkillCaster skills;

        public int Mana => Mathf.FloorToInt(Data.Mana);
        public int MaxMana => Data.Stats.MaxMp;

        /// <summary>Re-applies level, passive tree and gear: new max HP/MP (gains are also filled).</summary>
        public void RefreshStats()
        {
            if (health == null || IsDead || !gameObject.activeInHierarchy)
                return; // Spawn() reads the stats when the player enters the world
            int newMax = Data.Stats.MaxHp;
            int delta = newMax - health.Max;
            if (delta == 0) { ClampMana(); return; }
            health.SetMax(newMax, false);
            if (delta > 0) health.Heal(delta);
            ClampMana();
        }

        void ClampMana() => Data.Mana = Mathf.Clamp(Data.Mana, 0f, Data.Stats.MaxMp);

        /// <summary>Spends MP (or HP with the Blood Magic keystone). False if there is not enough.</summary>
        public bool TrySpend(int cost, bool useLife)
        {
            if (useLife)
            {
                if (health.Current <= cost) return false;
                health.Drain(cost);
                return true;
            }
            if (Data.Mana < cost) return false;
            Data.Mana -= cost;
            if (IsLocal) GameEvents.RaisePlayerHealthChanged(health.Current, health.Max);
            return true;
        }

        public void Heal(int amount) => health.Heal(amount);

        /// <summary>This member landed the killing blow: kill passives (HP / MP on kill) apply to it.</summary>
        public void OnKillingBlow()
        {
            if (IsDead) return;
            var st = Data.Stats;
            if (st.LifeOnKill > 0) health.Heal(st.LifeOnKill);
            if (st.ManaOnKill > 0) { Data.Mana += st.ManaOnKill; ClampMana(); }
        }

        void OnLevelUp(int level)
        {
            RefreshStats();
            if (level == 15 && IsLocal) GameEvents.RaiseToast("<color=#ffd34a>전직 가능!</color> 마을 광장의 전직 안내원에게 가면 '전직의 길' 퀘스트를 받을 수 있습니다.");
            health.Heal(health.Max);
            Data.Mana = Data.Stats.MaxMp;
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

        void OnDisable()
        {
            CancelMobility();
            // The local player leaving the world (title screen) takes the companions with it.
            if (IsLocal && Game.Party != null) Game.Party.OnLocalHidden();
        }

        void OnDestroy()
        {
            if (Data != null && !Data.IsLocal)
            {
                Data.Equipment.Changed -= RefreshStats;
                Data.Equipment.Changed -= ApplyGear;
                Data.Progression.Changed -= RefreshStats;
            }
            else if (Game.Session != null)
            {
                Game.Session.Equipment.Changed -= RefreshStats;
                Game.Session.Equipment.Changed -= ApplyGear;
                Game.Session.Progression.Changed -= RefreshStats;
                Game.Session.Progression.LeveledUp -= OnLevelUp;
            }
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
            CancelMobility();
            mobilityReadyAt = 0f;
            Class = cls;
            var info = CharacterClassInfo.Get(cls);
            animator.Setup(BaseLook(info), animator.Renderer);
            combat.SetClass(info);
            flash.SetTargets(animator.Renderer, combat.WeaponRenderer, combat.GripRenderer);
            skills?.ResetCooldowns();
            ApplyGear();
        }

        CharacterLook BaseLook(CharacterClassInfo info) => !IsLocal && Data.Look != null ? Data.Look : info.Look;

        /// <summary>Only the weapon of the worn gear shows on the character; a costume skin replaces the body.</summary>
        void ApplyGear()
        {
            if (animator == null || combat == null || Game.Session == null || Data == null) return;
            // A costume skin replaces the whole body (worn clothes don't show over it); the weapon still does.
            if (IsLocal)
            {
                Data.SkinId = Game.Cosmetics?.SkinFor(Class) ?? "";
                Data.CosmeticDamage = (Game.Cosmetics?.DamageBonus(Class) ?? 0) + StarShopClient.CollectionAttack;
                Data.CollectionHealth = StarShopClient.CollectionHealth;
                RefreshStats();
            }
            var skinLook = string.IsNullOrEmpty(Data.SkinId) ? null : SkinCatalog.LookFor(Class, Data.SkinId);
            // [ART] Worn armour no longer changes the body: only the weapon in hand shows the gear (skins still apply).
            animator.SetLook(skinLook ?? BaseLook(CharacterClassInfo.Get(Class)));
            SkinTrail.Set(this, animator.Renderer, skinLook != null ? SkinCatalog.Find(Data.SkinId) : null);
            combat.RefreshWeapon();
        }

        /// <summary>Re-applies the body look (a costume skin was put on or taken off).</summary>
        public void RefreshLook() => ApplyGear();

        public void Spawn(Vector2 position, Facing facing, int currentHealth, int maxHealth)
        {
            CareerCombat.For(this).ResetState();
            bool wasDead = IsDead;
            Place(position, facing);
            IsDead = false;
            visual.localRotation = Quaternion.identity;
            flash.Stop();
            // Max HP comes from base + level + passives + gear (the maxHealth argument is the base, kept for callers).
            health.Init(Data.Stats.MaxHp, currentHealth, stats.invulnerableTime);
            ClampMana();
            animator.Play(CharacterAnim.Idle, facing);
            GetComponent<YSort>().Refresh();
            // Companions follow the local player to wherever it (re)appears.
            // A local player coming back from death (village respawn) brings the companions back at full HP.
            if (IsLocal && Game.Party != null) Game.Party.OnLocalSpawned(wasDead);
        }

        /// <summary>Moves the member without touching HP (companions re-placed next to the leader, teleport catch-up).</summary>
        public void Place(Vector2 position, Facing facing)
        {
            CancelMobility();
            transform.position = position;
            body.position = position;
            body.SetVelocity(Vector2.zero);
            desiredVelocity = Vector2.zero;
            knockbackUntil = 0f;
            lockedUntil = 0f;
            Facing = facing;
            AimDirection = facing.ToVector();
            combat.Cancel();
            if (!IsDead) animator.Play(CharacterAnim.Idle, facing);
        }

        /// <summary>
        /// Gets a downed member back up with <paramref name="hpFraction"/> of max HP (and optional
        /// invulnerability). Use <see cref="PartyManager.ReviveMember"/> so the party raises its event.
        /// </summary>
        public void Revive(float hpFraction, float invulnerableSeconds = 0f)
        {
            if (!IsDead) return;
            IsDead = false;
            knockbackUntil = 0f;
            visual.localRotation = Quaternion.identity;
            flash.Stop();
            int max = Data.Stats.MaxHp;
            health.Init(max, Mathf.Max(1, Mathf.CeilToInt(max * Mathf.Clamp01(hpFraction))), stats.invulnerableTime);
            if (invulnerableSeconds > 0f)
            {
                health.SetInvulnerable(invulnerableSeconds);
                flash.Blink(invulnerableSeconds);
            }
            ClampMana();
            animator.Play(CharacterAnim.Idle, Facing);
            Fx.Sparkle(Center + Vector2.up * 0.3f, 6, 0.7f);
            Game.Audio.PlaySfx("heal", IsLocal ? 1f : 0.6f);
        }

        void Update()
        {
            Command = ActorCommand.None;
            if (IsDead) return;
            if (!Game.IsWorldRunning)
            {
                CancelMobility();
                body.SetVelocity(Vector2.zero);
                desiredVelocity = Vector2.zero;
                if (Game.State.Current == GameState.Cutscene) UpdateScripted();
                else if (Game.State.Current != GameState.Dialogue) animator.Play(CharacterAnim.Idle, Facing);
                return;
            }

            if (IsLocal) Game.Session.PlayTimeSeconds += Time.deltaTime;
            var st = Data.Stats;
            int maxMp = st.MaxMp;
            if (Data.Mana < maxMp) Data.Mana = Mathf.Min(maxMp, Data.Mana + st.ManaRegen * Time.deltaTime);

            var cmd = Input != null ? Input.Read(this) : ActorCommand.None;
            // Consume/discard menu input so one-shot actions cannot leak out on resume.
            if (IsLocal && (!Game.IsPlaying || Game.State.ChangedThisFrame)) cmd = ActorCommand.None;
            Command = cmd;
            if (NetDriven) { NetUpdate(cmd); return; } // [PARTY NET] position from the network
            // Mobility owns the pose and movement until the dash ends; a successful blink
            // consumes this frame too, so an X/Q press cannot also begin an attack.
            if (IsDashing || (cmd.mobility && TryMobility(cmd.move)))
            {
                desiredVelocity = Vector2.zero;
                animator.Play(IsDashing ? CharacterAnim.Walk : CharacterAnim.Idle, Facing);
                animator.SpeedMultiplier = IsDashing ? 1.8f : 1f;
                return;
            }
            Vector2 move = cmd.move;
            bool stunned = Time.time < knockbackUntil;
            bool channeling = Time.time < lockedUntil;
            if (channeling) move = Vector2.zero;
            if (combat.IsAttacking || skills.IsCasting) move *= 0.35f;
            else if (!stunned && move.sqrMagnitude > 0.01f)
            {
                Facing = FacingExtensions.FromVector(move, Facing);
                AimDirection = SnapTo8(move);
            }

            desiredVelocity = stunned ? Vector2.zero : move * stats.moveSpeed * st.SpeedMultiplier * CareerCombat.For(this).MoveScale * skills.ChargeMoveScale;

            // Ignore action buttons on the frame a menu/dialogue closed, so the same press
            // doesn't immediately trigger an attack or re-open the conversation.
            if (!stunned && !channeling && (!IsLocal || !Game.State.ChangedThisFrame))
            {
                bool acting = cmd.attack || cmd.skillSlot >= 0;
                if (acting && cmd.faceAim && cmd.aim.sqrMagnitude > 0.0001f && !combat.IsAttacking && !skills.IsCasting)
                    FaceTowards(Position + cmd.aim);
                if (cmd.attack) combat.TryAttack();
                else if (cmd.interact && IsLocal) interactor.TryInteract();
                // Potions and the scroll come from the local bag.
                if (IsLocal)
                {
                    if (cmd.useHealing) UseHealing();
                    if (cmd.useMana) UseConsumable(Game.Session.Inventory.Count(ConsumableDatabase.MpPotionHi) > 0 ? ConsumableDatabase.MpPotionHi : ConsumableDatabase.MpPotion);
                    if (cmd.townScroll) UseConsumable(ConsumableDatabase.TownScroll);
                }
                if (cmd.skillSlot >= 0 && cmd.skillSlot < SkillGems.Slots) skills.TryCast(cmd.skillSlot);
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

        /// <summary>Trees the character stands behind turn see-through (TreeFade).</summary>
        void LateUpdate()
        {
            TreeFade.UpdateAll(Position, animator != null ? animator.Renderer : null, Time.unscaledDeltaTime);
        }

        void FixedUpdate()
        {
            if (mobilityBraking) { body.SetVelocity(Vector2.zero); mobilityBraking = false; }
            if (NetDriven)
            {
                if (!IsDead && Game.IsWorldRunning) NetFixedStep();
                else body.SetVelocity(Vector2.zero);
                return;
            }
            if (IsDead || !Game.IsWorldRunning)
            {
                CancelMobility();
                body.SetVelocity(Vector2.zero);
                return;
            }
            if (Time.time < knockbackUntil)
            {
                body.SetVelocity(knockbackVelocity);
                return;
            }
            if (IsDashing) { StepDash(); return; }
            Vector2 v = Vector2.MoveTowards(body.GetVelocity(), desiredVelocity, stats.acceleration * Time.fixedDeltaTime);
            body.SetVelocity(v);
        }
    }
}
