using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [MONSTER] Monster / boss layer of <see cref="EnemyController"/>: the <see cref="MonsterDef"/> numbers,
    /// size scale, the pluggable <see cref="MonsterBehaviour"/>, super armor, the 무력화 (groggy) gauge and the
    /// small movement API behaviours drive the body with. The base file only calls the Monster* hooks, so a
    /// plain field skeleton (no definition) behaves exactly as before.
    /// </summary>
    public partial class EnemyController
    {
        // ---------- Tuning ----------
        /// <summary>그로기 length and the damage multiplier while groggy.</summary>
        public const float GroggySeconds = 5f, GroggyDamageTaken = 1.3f;
        /// <summary>Bosses are stunned / frozen for this share of the skill's time.</summary>
        public const float BossCrowdControlScale = 0.3f;
        /// <summary>Groggy damage per second of stun / freeze a skill applies (before the boss reduction).</summary>
        public const float GroggyPerCcSecond = 45f;
        /// <summary>Groggy damage per point of HP damage.</summary>
        public const float GroggyPerDamage = 1f;
        const float DefaultAcceleration = 30f;
        const float SuperArmorFxGap = 0.25f;

        /// <summary>A boss finished spawning (the boss HP bar binds to it).</summary>
        public static event Action<EnemyController> BossSpawned;

        /// <summary>Definition this monster was spawned from (null = legacy field skeleton).</summary>
        public MonsterDef Def { get; private set; }
        public int Level { get; private set; } = 1;
        public float HpMultiplier { get; private set; } = 1f;
        public float DamageMultiplier { get; private set; } = 1f;
        /// <summary>Who raised this monster (necro / boss / totem owner), or null.</summary>
        public EnemyController Summoner { get; internal set; }
        public MonsterBehaviour Behaviour => behaviour;
        public bool IsBoss => Def != null && Def.boss;
        public string DisplayName => stats != null ? stats.displayName : name;
        public Health Health => health;
        public EnemyStats Stats => stats;
        /// <summary>World size relative to the field skeleton.</summary>
        public float Size => Def != null ? Def.size : 1f;
        /// <summary>Deterministic per-monster random numbers (seeded at spawn) for AI decisions.</summary>
        public System.Random Rng { get; private set; } = new System.Random(1);
        /// <summary>In the hit-stagger state (for tests and UI).</summary>
        public bool IsStaggered => state == State.Hurt;

        // ---------- Groggy ----------
        public float GroggyMax { get; private set; }
        public float Groggy { get; private set; }
        public bool IsGroggy => groggyActive && Time.time < groggyUntil;
        public float GroggyRemaining => IsGroggy ? groggyUntil - Time.time : 0f;
        public float GroggyDuration { get; private set; }
        public event Action<EnemyController> GroggyStarted;
        public event Action<EnemyController> GroggyEnded;
        /// <summary>A hit landed but super armor ignored the stagger (HP bar flash).</summary>
        public event Action<EnemyController> SuperArmorHit;

        MonsterBehaviour behaviour;
        float visualScale = 1f;
        float groggyUntil;
        bool groggyActive;
        bool hitArmored;
        float nextArmorFx;

        float CenterHeight => Def != null ? Def.size : 1f;

        /// <summary>Hits never stagger / push it right now (definition flag or the behaviour's current action).</summary>
        public bool SuperArmorActive => Def != null && (Def.superArmor || (behaviour != null && behaviour.SuperArmorNow));

        internal void ApplyDefinition(MonsterDef def, int level, float hpMul, float dmgMul, int seed)
        {
            Def = def;
            Level = Mathf.Max(1, level);
            HpMultiplier = hpMul;
            DamageMultiplier = dmgMul;
            Rng = new System.Random(seed);
            name = def.id;

            visualScale = def.size / Mathf.Max(0.01f, def.artScale);
            visual.localScale = Vector3.one * visualScale;
            var shadow = visual.Find("Shadow");
            if (shadow != null) shadow.localScale = Vector3.one * def.artScale;
            alertIcon.transform.localPosition = new Vector3(0f, 1.3f * def.artScale + (def.boss ? 0.6f : 0f), 0f);
            if (bodyCollider is CircleCollider2D circle)
            {
                circle.radius = 0.3f * Mathf.Lerp(1f, def.size, 0.8f);
                circle.offset = new Vector2(0f, 0.25f * def.size);
            }
            body.mass = def.boss ? 60f : def.kind == MonsterKind.Totem ? 200f : 2f * def.size;
            if (def.kind == MonsterKind.Totem) body.bodyType = RigidbodyType2D.Kinematic;

            // Bosses use the HUD bar; everyone else keeps the small one, lifted over bigger heads.
            var bar = transform.Find("HealthBar");
            if (def.boss)
            {
                var small = GetComponent<EnemyHealthBar>();
                if (small != null) Destroy(small);
                if (bar != null) Destroy(bar.gameObject);
            }
            else if (bar != null) bar.localPosition = new Vector3(0f, def.kind == MonsterKind.Totem ? 1.95f : 1.2f * def.size, 0f);

            GroggyMax = def.groggyMax;
            Groggy = GroggyMax;
            behaviour = MonsterBehaviour.Attach(this, def);
            if (def.boss) BossSpawned?.Invoke(this);
        }

        // =============================== Hooks called from EnemyController.cs ===============================

        bool MonsterTick()
        {
            if (groggyActive && Time.time >= groggyUntil) EndGroggy();
            if (behaviour == null) return false;
            if (IsGroggy)
            {
                desiredVelocity = Vector2.zero;
                alertIcon.enabled = false;
                animator.Play(CharacterAnim.Hurt, facing);
                visual.localPosition = Vector3.zero;
                if (Time.time >= nextArmorFx)
                {
                    // Dizzy sparkles over the head.
                    nextArmorFx = Time.time + 0.3f;
                    Fx.Sparkle(Position + new Vector2(0f, 1.25f * Size), 2, 0.45f * Size);
                }
                return true;
            }
            return behaviour.Tick(state == State.Chase);
        }

        float MonsterAcceleration => behaviour != null ? behaviour.Acceleration : DefaultAcceleration;

        float MonsterCrowdControl(float seconds)
        {
            if (Def == null) return seconds;
            if (Def.kind == MonsterKind.Totem) return 0f;
            AddGroggy(seconds * GroggyPerCcSecond);
            if (Def.boss) return seconds * BossCrowdControlScale;
            // [SKILL v2] Diminishing returns: each stun / freeze within CcChainWindow of the last one lasts half as
            // long (1, 1/2, 1/4 ...), so two control skills can no longer keep a monster locked.
            if (!SkillGems.UseLegacy)
            {
                if (Time.time > ccChainUntil) ccChain = 0;
                seconds *= Mathf.Pow(0.5f, ccChain);
                ccChain++;
                ccChainUntil = Time.time + CcChainWindow;
            }
            if (!SuperArmorActive) behaviour?.Interrupt();
            return seconds;
        }

        const float CcChainWindow = 6f;
        int ccChain;
        float ccChainUntil;

        bool MonsterBeforeDamage(ref DamageInfo info)
        {
            hitArmored = false;
            if (Def == null) return true;
            if (behaviour != null && !behaviour.BeforeDamage(ref info)) return false;
            hitArmored = SuperArmorActive || (behaviour != null && behaviour.ArmorAgainst(info));
            if (IsGroggy) info.amount = Mathf.Max(info.amount + 1, Mathf.RoundToInt(info.amount * GroggyDamageTaken));
            return true;
        }

        float MonsterKnockback(DamageInfo info)
        {
            float kb = info.knockback > 0f ? info.knockback : stats.knockbackSpeed;
            if (Def == null) return kb;
            if (hitArmored) return 0f;
            // Heavier monsters (low knockbackSpeed) slide less; 6 = the field skeleton.
            return kb * stats.knockbackSpeed / 6f;
        }

        bool MonsterAfterHit(DamageInfo info)
        {
            if (Def == null) return false;
            AddGroggy(info.amount * GroggyPerDamage);
            behaviour?.OnHit(info);
            if (hitArmored || IsGroggy)
            {
                knockbackVelocity = Vector2.zero;
                if (Time.time >= nextArmorFx)
                {
                    nextArmorFx = Time.time + SuperArmorFxGap;
                    SkillFx.Spawn("fx_ring", Center, new Color(1f, 0.85f, 0.35f, 0.8f), 0.18f, SkillFx.At(Position.y, 60))
                        .Additive().Scale(0.5f * Size, 1.1f * Size);
                }
                SuperArmorHit?.Invoke(this);
                return true;
            }
            behaviour?.Interrupt();
            return false;
        }

        bool MonsterLoot()
        {
            if (Def == null) return true;
            if (Def.noLoot) return false;
            if (Def.goldMax > 0)
            {
                var parent = Game.World != null && Game.World.ObjectsRoot != null ? Game.World.ObjectsRoot : transform.parent;
                int total = Rng.Next(Def.goldMin, Def.goldMax + 1);
                int piles = Mathf.Clamp(total / 20, 2, 6);
                for (int i = 0; i < piles; i++)
                {
                    int part = i == piles - 1 ? total - total / piles * (piles - 1) : total / piles;
                    Pickup.Create(ConsumableDatabase.Gold, part, Position + new Vector2(0f, 0.2f), parent);
                }
                Fx.Sparkle(Position + Vector2.up * 0.5f * Size, 5, 0.6f * Size);
            }
            return true;
        }

        // =============================== Groggy ===============================

        /// <summary>Takes <paramref name="amount"/> off the groggy gauge; at 0 the monster collapses (그로기).</summary>
        public void AddGroggy(float amount)
        {
            if (GroggyMax <= 0f || amount <= 0f || IsGroggy || state == State.Dead) return;
            Groggy = Mathf.Max(0f, Groggy - amount);
            if (Groggy <= 0f) StartGroggy();
        }

        void StartGroggy()
        {
            float seconds = behaviour != null ? behaviour.OnGroggyBroken() : GroggySeconds;
            GroggyDuration = seconds;
            groggyActive = true;
            groggyUntil = Time.time + seconds;
            Groggy = 0f;
            desiredVelocity = Vector2.zero;
            Game.Audio?.PlaySfx("rock_break");
            Game.Camera?.Shake(0.2f, 0.25f);
            Fx.Burst("fx_dust", Position + new Vector2(0f, 0.2f), 8, 3f, 0.6f);
            GroggyStarted?.Invoke(this);
        }

        void EndGroggy()
        {
            groggyActive = false;
            Groggy = GroggyMax;
            GroggyEnded?.Invoke(this);
        }

        /// <summary>Test / script hook: ends a running groggy now.</summary>
        public void EndGroggyNow()
        {
            if (groggyActive) EndGroggy();
        }

        // =============================== API for behaviours ===============================

        internal bool InChase => state == State.Chase;
        internal bool CanAct => state != State.Dead && !IsFrozen && !IsStunned && !IsGroggy && Game.IsPlaying;
        internal Vector2 Home => home;
        internal Facing MonsterFacing => facing;
        internal PlayerController MonsterTarget => Target;

        /// <summary>Stop the chase and walk back to the spawn point.</summary>
        internal void GiveUp() => EnterState(State.ReturnHome, 6f);

        internal void MonsterMove(Vector2 dir, float speed)
        {
            if (dir.sqrMagnitude < 0.0001f) { desiredVelocity = Vector2.zero; return; }
            desiredVelocity = dir.normalized * speed;
            facing = FacingExtensions.FromVector(dir, facing);
        }

        internal void MonsterMoveTo(Vector2 point, float speed) => MoveTowards(point, speed);

        internal void MonsterHalt() => desiredVelocity = Vector2.zero;

        internal void MonsterFace(Vector2 point)
        {
            Vector2 d = point - Position;
            if (d.sqrMagnitude > 0.0001f) facing = FacingExtensions.FromVector(d, facing);
        }

        /// <summary>Plays an animation; <paramref name="windup"/> shakes the body and shows the "!" like the base swing.</summary>
        internal void MonsterAnim(CharacterAnim anim, bool windup = false)
        {
            animator.Play(anim, facing);
            alertIcon.enabled = windup;
            visual.localPosition = windup ? new Vector3(Mathf.Sin(Time.time * 60f) * 0.03f * Size, 0f, 0f) : Vector3.zero;
        }

        /// <summary>Walk / idle depending on the current velocity.</summary>
        internal void MonsterAutoAnim() => MonsterAnim(desiredVelocity.sqrMagnitude > 0.05f ? CharacterAnim.Walk : CharacterAnim.Idle);

        /// <summary>Attack damage × <paramref name="mul"/> (already level / difficulty scaled).</summary>
        internal int MonsterDamage(float mul) => Mathf.Max(1, Mathf.RoundToInt(stats.attackDamage * mul));

        internal float Speed => stats.chaseSpeed;
    }
}
