using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Keeps one enemy per spawn point alive, respawning defeated ones after a delay while the
    /// player is out of sight, so the area stays populated during a play session.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        EnemyStats stats;
        HuntingZone zone;
        public int SpawnCount => points.Count;
        CharacterLook look;
        readonly List<Vector2> points = new List<Vector2>();
        EnemyController[] alive;
        float[] respawnAt;

        public void Setup(EnemyStats enemyStats, CharacterLook enemyLook, IEnumerable<Vector2> spawnPoints)
        {
            stats = enemyStats;
            look = enemyLook;
            points.Clear();
            points.AddRange(spawnPoints);
            alive = new EnemyController[points.Count];
            respawnAt = new float[points.Count];
            for (int i = 0; i < points.Count; i++) SpawnAt(i);
        }

        public void SetupField(HuntingZone field, IEnumerable<Vector2> spawnPoints)
        {
            zone = field;
            points.Clear(); points.AddRange(spawnPoints);
            alive = new EnemyController[points.Count]; respawnAt = new float[points.Count];
            for (int i = 0; i < points.Count; i++) SpawnAt(i);
        }

        void SpawnAt(int index)
        {
            var enemy = zone != null
                ? MonsterDatabase.SpawnField(zone.monsters[index % zone.monsters.Length], points[index], transform, zone.monsterLevel, zone.KillXp)
                : EnemyController.Create(stats, look, points[index], transform);
            alive[index] = enemy;
            respawnAt[index] = 0f;
            enemy.Died += e => OnEnemyDied(index);
        }

        void OnEnemyDied(int index)
        {
            alive[index] = null;
            respawnAt[index] = Time.time + (zone != null ? HuntingGrounds.RespawnSeconds : stats.respawnDelay);
        }

        void Update()
        {
            if (alive == null || !Game.IsWorldRunning) return;
            for (int i = 0; i < alive.Length; i++)
            {
                if (alive[i] != null || respawnAt[i] <= 0f || Time.time < respawnAt[i]) continue;
                var player = Game.Player;
                if (player != null && Vector2.Distance(player.Position, points[i]) < (zone != null ? HuntingGrounds.RespawnSafeRadius : stats.respawnMinPlayerDistance))
                {
                    respawnAt[i] = Time.time + 2f;
                    continue;
                }
                SpawnAt(i);
            }
        }
    }
}
