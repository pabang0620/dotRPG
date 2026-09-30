using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>How a monster fights (picks the EnemyBehaviour attached at spawn).</summary>
    public enum MonsterKind
    {
        /// <summary>Plain melee skeleton (EnemyController's own chase + swing).</summary>
        Melee,
        /// <summary>Runs away and scatters gold when hit (황금 해골).</summary>
        GoldRunner,
        /// <summary>Telegraphed straight charge (해골 광부).</summary>
        Charger,
        /// <summary>Keeps distance, slow bolts, raises skeletons (해골 사령술사).</summary>
        Necro,
        /// <summary>Keeps distance, telegraphed arrows (해골 궁수).</summary>
        Archer,
        /// <summary>Front shield blocks 80% (해골 방패병).</summary>
        ShieldGuard,
        /// <summary>Two-hit combo with super armor (해골 기사).</summary>
        Knight,
        /// <summary>Static object (사령 토템).</summary>
        Totem,
        /// <summary>Boss pattern sequencer (<see cref="BossBrain"/>).</summary>
        Boss,
    }

    /// <summary>Shared boss pattern kinds; bosses list them with their own numbers.</summary>
    public enum PatternKind
    {
        /// <summary>Three cone slashes in front, each telegraphed (3연타).</summary>
        Slash3,
        /// <summary>Line telegraph, then a dash along it.</summary>
        Charge,
        /// <summary>Circle around the boss.</summary>
        Slam,
        /// <summary>Circles under the heroes and around the room (바닥 장판).</summary>
        Fields,
        /// <summary>Ring around the boss; standing close is safe.</summary>
        Donut,
        /// <summary>Fan of telegraphed arrows.</summary>
        ArrowVolley,
        /// <summary>Slow bolts in every direction.</summary>
        BoltRing,
        /// <summary>Raises skeletons (capped).</summary>
        Summon,
        /// <summary>Raises the shield (frontal damage -80%), then counter-slams.</summary>
        Guard,
        /// <summary>Gold nuggets rain on circles (and drop a little gold).</summary>
        GoldRain,
        /// <summary>망자의 심판: long cast, break the groggy gauge or everyone takes huge damage.</summary>
        Judgment,
    }

    [System.Serializable]
    public class BossPattern
    {
        public PatternKind kind;
        public float cooldown = 6f;
        public float weight = 1f;
        /// <summary>× the boss's attack damage.</summary>
        public float damageMul = 1f;
        /// <summary>Hits / circles / arrows / summons, depending on the kind.</summary>
        public int count = 1;
        /// <summary>Only used from phase minPhase up to maxPhase (1-based).</summary>
        public int minPhase = 1, maxPhase = 3;
        /// <summary>Only picked when the target is closer than this (0 = any distance).</summary>
        public float maxRange;

        public BossPattern(PatternKind kind, float cooldown, float damageMul = 1f, int count = 1, int minPhase = 1, int maxPhase = 3, float maxRange = 0f, float weight = 1f)
        {
            this.kind = kind;
            this.cooldown = cooldown;
            this.damageMul = damageMul;
            this.count = count;
            this.minPhase = minPhase;
            this.maxPhase = maxPhase;
            this.maxRange = maxRange;
            this.weight = weight;
        }
    }

    /// <summary>One monster type: numbers, look, behaviour and (bosses) patterns.</summary>
    public class MonsterDef
    {
        public string id;
        public string name;
        public CharacterLook look;
        public MonsterKind kind;
        /// <summary>World size relative to the field skeleton (bosses 2–2.5).</summary>
        public float size = 1f;
        /// <summary>How much bigger the art is than the 16x20 frame (bosses are drawn at 2x).</summary>
        public float artScale = 1f;

        public int hp = 30;
        public int damage = 10;
        public float wanderSpeed = 1.1f, chaseSpeed = 2.3f;
        public float attackRange = 0.95f, windup = 0.5f, recover = 0.7f;
        public float detect = 6f, loseInterest = 9f, leash = 12f;
        public float knockbackSpeed = 6f, hurtTime = 0.3f, invulnerable = 0.15f, attackKnockback = 7f;
        public int xp = 20;

        /// <summary>Hits never stagger or push it (bosses; knights only while attacking — see the behaviour).</summary>
        public bool superArmor;
        /// <summary>무력화 게이지. 0 = no gauge.</summary>
        public float groggyMax;
        public bool boss, raid;
        /// <summary>Number of HP bar lines (×N) on the boss bar.</summary>
        public int hpLines = 1;
        /// <summary>HP ratios where a new phase starts (e.g. 0.7, 0.35).</summary>
        public float[] phases = new float[0];
        public List<BossPattern> patterns = new List<BossPattern>();

        // Behaviour numbers (kind-specific).
        public float keepDistance;
        public float skillInterval = 2.5f;
        public float projectileSpeed = 8f;
        public float summonInterval = 10f;
        public int maxMinions = 2;
        public string minionId = "skel_warrior";

        // Rewards.
        public bool noLoot;
        public int goldMin, goldMax;
        public int goldPerHitMin, goldPerHitMax;

        public MonsterDef Clone() => (MonsterDef)MemberwiseClone();
    }

    /// <summary>
    /// Code table of every dungeon monster and boss (PLAN_DUNGEON_RAID §6.1 / §6.2) and the spawn
    /// factory the dungeon uses. Numbers are level-1 values; <see cref="Spawn"/> applies the party /
    /// difficulty multipliers and the monster level.
    /// </summary>
    public static class MonsterDatabase
    {
        // ---------- Level scaling (per level above 1) ----------
        const float HpPerLevel = 0.12f;
        const float DamagePerLevel = 0.08f;
        const float XpPerLevel = 0.1f;

        public const string Warrior = "skel_warrior";
        public const string Totem = "totem";

        static Dictionary<string, MonsterDef> table;

        public static IEnumerable<MonsterDef> All { get { Build(); return table.Values; } }

        public static MonsterDef Get(string id)
        {
            Build();
            return id != null && table.TryGetValue(id, out var def) ? def : null;
        }

        /// <summary>
        /// Spawns a monster by id. hpMul / dmgMul come from the party size and difficulty; level
        /// scales HP, damage and XP. Unknown ids spawn a plain skeleton (with a warning).
        /// </summary>
        public static EnemyController Spawn(string id, Vector2 pos, Transform parent, float hpMul = 1f, float dmgMul = 1f, int level = 1)
        {
            var def = Get(id);
            if (def == null)
            {
                Debug.LogWarning($"[dotRPG] Unknown monster id '{id}', spawning a skeleton.");
                def = Get(Warrior);
            }
            return SpawnDef(def, pos, parent, hpMul, dmgMul, level);
        }

        /// <summary>Summoned helpers (necro, boss, totems): no XP and no loot.</summary>
        public static EnemyController SpawnMinion(string id, Vector2 pos, Transform parent, EnemyController summoner)
        {
            var def = (Get(id) ?? Get(Warrior)).Clone();
            def.xp = 0;
            def.noLoot = true;
            float hpMul = summoner != null ? summoner.HpMultiplier : 1f;
            float dmgMul = summoner != null ? summoner.DamageMultiplier : 1f;
            int level = summoner != null ? summoner.Level : 1;
            var e = SpawnDef(def, pos, parent, hpMul, dmgMul, level);
            e.Summoner = summoner;
            return e;
        }

        static EnemyController SpawnDef(MonsterDef def, Vector2 pos, Transform parent, float hpMul, float dmgMul, int level)
        {
            level = Mathf.Max(1, level);
            var stats = ScriptableObject.CreateInstance<EnemyStats>();
            stats.name = def.id;
            stats.enemyId = def.id;
            stats.displayName = def.name;
            stats.maxHealth = Mathf.Max(1, Mathf.RoundToInt(def.hp * hpMul * (1f + HpPerLevel * (level - 1))));
            stats.attackDamage = Mathf.Max(def.damage > 0 ? 1 : 0, Mathf.RoundToInt(def.damage * dmgMul * (1f + DamagePerLevel * (level - 1))));
            stats.hurtStunTime = def.hurtTime;
            stats.invulnerableTime = def.invulnerable;
            stats.knockbackSpeed = def.knockbackSpeed;
            stats.wanderSpeed = def.wanderSpeed;
            stats.chaseSpeed = def.chaseSpeed;
            stats.wanderRadius = def.boss ? 0.5f : 2f;
            stats.detectRadius = def.detect;
            stats.loseInterestRadius = def.loseInterest;
            stats.leashRadius = def.leash;
            stats.attackRange = def.attackRange;
            stats.windupTime = def.windup;
            stats.recoverTime = def.recover;
            stats.attackKnockback = def.attackKnockback;
            stats.xpReward = Mathf.RoundToInt(def.xp * (1f + XpPerLevel * (level - 1)));
            var enemy = EnemyController.Create(stats, def.look, pos, parent);
            enemy.ApplyDefinition(def, level, hpMul, dmgMul);
            return enemy;
        }

        // ---------- Table ----------

        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

        static CharacterLook Look(string id, Color32 bone) => new CharacterLook
        {
            id = id,
            body = BodyKind.Monster,
            hairStyle = HairStyle.Bald,
            skin = bone,
            hair = bone,
            shirt = bone,
            pants = PixelCanvas.Shade(bone, 0.83f),
        };

        static readonly Color32 Bone = C(240, 232, 212);

        static void Add(MonsterDef d) => table[d.id] = d;

        static void Build()
        {
            if (table != null) return;
            table = new Dictionary<string, MonsterDef>();

            // ----- Field / dungeon monsters -----
            Add(new MonsterDef
            {
                id = Warrior, name = "해골 전사", look = CharacterLook.Skeleton, kind = MonsterKind.Melee,
                hp = 30, damage = 10, xp = 20,
            });
            Add(new MonsterDef
            {
                id = "skel_gold", name = "황금 해골", look = Look("skel_gold", C(236, 196, 84)), kind = MonsterKind.GoldRunner,
                hp = 40, damage = 0, xp = 30, chaseSpeed = 3.1f, wanderSpeed = 1.4f, keepDistance = 5.5f,
                goldMin = 60, goldMax = 100, goldPerHitMin = 3, goldPerHitMax = 6, knockbackSpeed = 7f,
            });
            Add(new MonsterDef
            {
                id = "skel_miner", name = "해골 광부", look = Look("skel_miner", C(226, 214, 190)), kind = MonsterKind.Charger,
                hp = 45, damage = 13, xp = 28, chaseSpeed = 2.0f, attackRange = 1.0f,
                skillInterval = 3.5f, projectileSpeed = 11f, // dash speed
            });
            Add(new MonsterDef
            {
                id = "skel_necro", name = "해골 사령술사", look = Look("skel_necro", C(222, 226, 206)), kind = MonsterKind.Necro,
                hp = 35, damage = 12, xp = 35, chaseSpeed = 1.9f, keepDistance = 4.5f,
                skillInterval = 2.8f, projectileSpeed = 3.2f, summonInterval = 10f, maxMinions = 2, minionId = Warrior,
            });
            Add(new MonsterDef
            {
                id = "skel_archer", name = "해골 궁수", look = Look("skel_archer", Bone), kind = MonsterKind.Archer,
                hp = 28, damage = 11, xp = 26, chaseSpeed = 2.2f, keepDistance = 5f,
                skillInterval = 2.4f, projectileSpeed = 10f, detect = 7.5f, loseInterest = 11f,
            });
            Add(new MonsterDef
            {
                id = "skel_shield", name = "해골 방패병", look = Look("skel_shield", C(228, 220, 200)), kind = MonsterKind.ShieldGuard,
                hp = 60, damage = 12, xp = 32, chaseSpeed = 1.5f, attackRange = 1.1f, windup = 0.55f, recover = 0.9f,
            });
            Add(new MonsterDef
            {
                id = "skel_knight", name = "해골 기사", look = Look("skel_knight", C(214, 208, 196)), kind = MonsterKind.Knight,
                hp = 90, damage = 16, xp = 45, chaseSpeed = 2.3f, attackRange = 1.3f, windup = 0.45f, recover = 0.8f,
                knockbackSpeed = 4f,
            });
            Add(new MonsterDef
            {
                id = Totem, name = "사령 토템", look = Look(Totem, Bone), kind = MonsterKind.Totem,
                hp = 120, damage = 0, xp = 0, noLoot = true, superArmor = true, chaseSpeed = 0f, wanderSpeed = 0f,
                knockbackSpeed = 0f, detect = 0f,
            });

            // ----- Weekday bosses (§6.1) -----
            Add(Boss("boss_gold_foreman", "황금 광부장", Bone, hp: 900, dmg: 20, lines: 18, speed: 2.1f,
                new BossPattern(PatternKind.Slash3, 5f, 1f, 3, maxRange: 3.2f, weight: 1.4f),
                new BossPattern(PatternKind.Charge, 7f, 1.3f),
                new BossPattern(PatternKind.GoldRain, 9f, 1.1f, 5),
                new BossPattern(PatternKind.Slam, 8f, 1.4f, maxRange: 3.5f)));
            Add(Boss("boss_mine_captain", "광산 해골대장", C(216, 204, 176), hp: 950, dmg: 22, lines: 18, speed: 2.0f,
                new BossPattern(PatternKind.Charge, 5.5f, 1.3f, weight: 1.5f),
                new BossPattern(PatternKind.Slam, 7f, 1.5f, maxRange: 3.5f),
                new BossPattern(PatternKind.Slash3, 5f, 1f, 3, maxRange: 3.2f),
                new BossPattern(PatternKind.Fields, 10f, 1.1f, 4)));
            Add(Boss("boss_lich", "묘지기 리치", C(226, 234, 210), hp: 850, dmg: 20, lines: 16, speed: 1.7f,
                new BossPattern(PatternKind.BoltRing, 6f, 1f, 10, weight: 1.3f),
                new BossPattern(PatternKind.Summon, 12f, 1f, 2),
                new BossPattern(PatternKind.Fields, 8f, 1.2f, 5),
                new BossPattern(PatternKind.Donut, 11f, 1.4f)));
            Add(Boss("boss_archer_chief", "해골 사수장", Bone, hp: 850, dmg: 20, lines: 16, speed: 2.3f,
                new BossPattern(PatternKind.ArrowVolley, 4.5f, 1f, 5, weight: 1.5f),
                new BossPattern(PatternKind.Charge, 8f, 1.2f),
                new BossPattern(PatternKind.Fields, 9f, 1.1f, 4),
                new BossPattern(PatternKind.Slash3, 6f, 0.9f, 3, maxRange: 3f)));
            Add(Boss("boss_armory_warden", "무기고 수문장", C(222, 214, 196), hp: 1100, dmg: 22, lines: 20, speed: 1.7f,
                new BossPattern(PatternKind.Guard, 9f, 1.5f, weight: 1.2f),
                new BossPattern(PatternKind.Slash3, 5f, 1.1f, 3, maxRange: 3.2f, weight: 1.3f),
                new BossPattern(PatternKind.Charge, 7f, 1.3f),
                new BossPattern(PatternKind.Donut, 12f, 1.4f)));

            // ----- Raid boss (§6.2) -----
            var king = Boss("boss_skeleton_king", "해골왕", C(244, 238, 222), hp: 3200, dmg: 25, lines: 40, speed: 2.2f,
                new BossPattern(PatternKind.Slash3, 4.5f, 1f, 3, maxRange: 3.6f, weight: 1.5f),
                new BossPattern(PatternKind.Charge, 6f, 1.4f),
                new BossPattern(PatternKind.Slam, 8f, 1.4f, maxRange: 3.8f, minPhase: 1, maxPhase: 1),
                new BossPattern(PatternKind.Fields, 7f, 1.2f, 6, minPhase: 2),
                new BossPattern(PatternKind.Donut, 12f, 1.5f, minPhase: 3),
                new BossPattern(PatternKind.Judgment, 35f, 8f, minPhase: 3, weight: 0f));
            king.raid = true;
            king.size = 2.4f;
            king.groggyMax = 500f;
            king.phases = new[] { 0.7f, 0.35f };
            king.maxMinions = 6;
            Add(king);
        }

        static MonsterDef Boss(string id, string name, Color32 bone, int hp, int dmg, int lines, float speed, params BossPattern[] patterns)
        {
            return new MonsterDef
            {
                id = id, name = name, look = Look(id, bone), kind = MonsterKind.Boss, boss = true,
                size = 2f, artScale = 2f, hp = hp, damage = dmg, xp = 300, chaseSpeed = speed, wanderSpeed = 1f,
                attackRange = 2.2f, detect = 11f, loseInterest = 999f, leash = 999f, superArmor = true,
                groggyMax = 320f, hpLines = lines, invulnerable = 0.05f, knockbackSpeed = 0f, attackKnockback = 9f,
                goldMin = 150, goldMax = 250, maxMinions = 4,
                patterns = new List<BossPattern>(patterns),
            };
        }
    }
}
