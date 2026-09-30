using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The one place dungeon rooms create monsters. For now every id is a skeleton with scaled numbers
    /// (bosses: 1.6× size, ×8 HP). MONSTER's MonsterDatabase replaces the body of <see cref="Spawn"/>.
    /// Monster ids used by <see cref="DungeonDatabase"/>: skel_gold, skel_miner, skel_necro, skel_archer,
    /// skel_shield, skel_warrior, skel_knight; bosses boss_gold_foreman, boss_mine_captain, boss_lich,
    /// boss_archer_chief, boss_armory_warden, boss_skeleton_king.
    /// </summary>
    public static class DungeonMonsters
    {
        /// <summary>Rooms are small: monsters notice the player from across the room and never walk home.</summary>
        const float DetectRadius = 13f, LoseInterestRadius = 60f, LeashRadius = 80f;
        const float BossDamageMul = 1.5f, BossXpMul = 6f, BossKnockback = 1.2f;

        static readonly Dictionary<string, EnemyStats> statsCache = new Dictionary<string, EnemyStats>();

        /// <summary>Creates a monster of <paramref name="monsterId"/> with multiplied HP and damage.</summary>
        public static EnemyController Spawn(string monsterId, Vector2 pos, Transform parent, float hpMul, float dmgMul, bool isBoss)
        {
            // INTEGRATION TODO: MonsterDatabase.Spawn — replace this skeleton stand-in with the real monster
            // definition (look, behaviour, boss HP bar, super armor). Keep the multipliers and the boss flag.
            var stats = StatsFor(monsterId, hpMul, dmgMul, isBoss);
            var enemy = EnemyController.Create(stats, CharacterLook.Skeleton, pos, parent);
            if (isBoss) enemy.transform.localScale = new Vector3(DungeonDatabase.BossScale, DungeonDatabase.BossScale, 1f);
            return enemy;
        }

        static EnemyStats StatsFor(string monsterId, float hpMul, float dmgMul, bool isBoss)
        {
            string key = $"{monsterId}|{hpMul:0.###}|{dmgMul:0.###}|{isBoss}";
            if (statsCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var src = Game.Config.skeletonStats;
            var s = Object.Instantiate(src);
            s.name = key;
            s.enemyId = monsterId;
            float hp = src.maxHealth * hpMul * (isBoss ? DungeonDatabase.BossHpMul : 1f);
            s.maxHealth = Mathf.Max(1, Mathf.RoundToInt(hp));
            s.attackDamage = Mathf.Max(1, Mathf.RoundToInt(src.attackDamage * dmgMul * (isBoss ? BossDamageMul : 1f)));
            s.detectRadius = DetectRadius;
            s.loseInterestRadius = LoseInterestRadius;
            s.leashRadius = LeashRadius;
            if (isBoss)
            {
                s.xpReward = Mathf.RoundToInt(src.xpReward * BossXpMul);
                s.knockbackSpeed = BossKnockback;
                s.attackRange = src.attackRange * DungeonDatabase.BossScale;
            }
            statsCache[key] = s;
            return s;
        }
    }
}
