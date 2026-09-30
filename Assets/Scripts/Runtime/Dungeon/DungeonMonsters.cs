using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The one place dungeon rooms create monsters: <see cref="MonsterDatabase.Spawn"/> with the run's
    /// party / difficulty multipliers and the monster level, then room-sized awareness (rooms are small,
    /// monsters notice the party from across the room and never walk home). Bosses bind the boss HP bar
    /// themselves through <see cref="EnemyController.BossSpawned"/>.
    /// Monster ids used by <see cref="DungeonDatabase"/>: skel_gold, skel_miner, skel_necro, skel_archer,
    /// skel_shield, skel_warrior, skel_knight; bosses boss_gold_foreman, boss_mine_captain, boss_lich,
    /// boss_archer_chief, boss_armory_warden, boss_skeleton_king.
    /// </summary>
    public static class DungeonMonsters
    {
        const float DetectRadius = 13f, LoseInterestRadius = 60f, LeashRadius = 80f;

        /// <summary>Monster level of a spawn group: 1 + the difficulty's monster level + the group's offset.</summary>
        public static int LevelFor(DifficultyDef numbers, SpawnGroup group) =>
            1 + (numbers != null ? numbers.monsterLevel : 0) + (group != null ? group.levelOffset : 0);

        /// <summary>Creates a monster of <paramref name="monsterId"/> with multiplied HP and damage at <paramref name="level"/>.</summary>
        public static EnemyController Spawn(string monsterId, Vector2 pos, Transform parent, float hpMul, float dmgMul, int level)
        {
            var enemy = MonsterDatabase.Spawn(monsterId, pos, parent, hpMul, dmgMul, level);
            if (enemy == null) return null;
            var s = enemy.Stats;
            if (s != null && s.detectRadius > 0f)
            {
                s.detectRadius = Mathf.Max(s.detectRadius, DetectRadius);
                s.loseInterestRadius = Mathf.Max(s.loseInterestRadius, LoseInterestRadius);
                s.leashRadius = Mathf.Max(s.leashRadius, LeashRadius);
            }
            return enemy;
        }
    }
}
