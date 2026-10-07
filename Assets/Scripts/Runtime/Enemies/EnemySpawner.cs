using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Keeps one enemy per spawn point alive, respawning defeated ones after a delay while the
    /// player is out of sight, so the area stays populated during a play session.
    /// [PARTY 8] In a field party session one PC (the host) runs the spawner for everyone; the others only
    /// draw the host's monsters (<see cref="SpawnerMode.Follower"/>). While the session is being looked up
    /// nothing spawns (<see cref="SpawnerMode.Pending"/>) so no monster exists twice.
    /// </summary>
    public partial class EnemySpawner : MonoBehaviour
    {
        public enum SpawnerMode { Local, Pending, Host, Follower }

        /// <summary>Longest wait for the field session answer before playing alone.</summary>
        const float PendingSeconds = 3f;

        /// <summary>The field spawner of the loaded map (null on maps without one).</summary>
        public static EnemySpawner Current { get; private set; }

        EnemyStats stats;
        HuntingZone zone;
        CharacterLook look;
        readonly List<Vector2> points = new List<Vector2>();
        EnemyController[] alive;
        float[] respawnAt;
        float pendingUntil;

        public SpawnerMode Mode { get; private set; } = SpawnerMode.Local;
        public EnemyStats Stats => stats;
        /// <summary>[HUNT] The hunting zone this spawner runs (null: the classic single-kind field).</summary>
        public HuntingZone Zone => zone;
        public CharacterLook Look => look;

        public void Setup(EnemyStats enemyStats, CharacterLook enemyLook, IEnumerable<Vector2> spawnPoints)
        {
            stats = enemyStats;
            look = enemyLook;
            points.Clear();
            points.AddRange(spawnPoints);
            alive = new EnemyController[points.Count];
            respawnAt = new float[points.Count];
            Current = this;
            // [PARTY 8] An online party may share this field: wait for the session before spawning.
            if (FieldSession.MayShare())
            {
                Mode = SpawnerMode.Pending;
                pendingUntil = Time.realtimeSinceStartup + PendingSeconds;
                return;
            }
            for (int i = 0; i < points.Count; i++) SpawnAt(i);
        }

        void OnDestroy() { if (Current == this) Current = null; }

        /// <summary>[PARTY 8] The field session decided who runs this map's monsters.</summary>
        public void SetMode(SpawnerMode mode)
        {
            if (alive == null) { Mode = mode; return; }
            var before = Mode;
            Mode = mode;
            if (mode == SpawnerMode.Follower)
            {
                // Local monsters go: the host's monsters arrive as puppets.
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] != null) Destroy(alive[i].gameObject);
                    alive[i] = null;
                    respawnAt[i] = 0f;
                }
                return;
            }
            if (before == SpawnerMode.Pending || before == SpawnerMode.Follower)
                for (int i = 0; i < points.Count; i++) if (alive[i] == null) SpawnAt(i);
        }

        /// <summary>
        /// [PARTY 8] Host handover: the puppets this PC was drawing became real monsters; give each to the
        /// nearest empty spawn point. Empty points count as "just died" (full respawn wait), so the new host
        /// never has more monsters out than the server's supply limit allows.
        /// </summary>
        public void TakeOver(IEnumerable<EnemyController> released)
        {
            Mode = SpawnerMode.Host;
            if (alive == null) return;
            foreach (var e in released)
            {
                if (e == null || e.IsDead) continue;
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    if (alive[i] != null) continue;
                    float d = (points[i] - e.Position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) continue;
                alive[best] = e;
                e.Shared = true;
                int index = best;
                e.Died += x => OnEnemyDied(index);
            }
            for (int i = 0; i < alive.Length; i++)
                if (alive[i] == null) respawnAt[i] = Time.time + RespawnDelay;
        }

        /// <summary>[HUNT] A hunting zone: several monster kinds at the zone's level (see <see cref="HuntingGrounds"/>).</summary>
        public void SetupField(HuntingZone field, IEnumerable<Vector2> spawnPoints)
        {
            zone = field;
            points.Clear(); points.AddRange(spawnPoints);
            alive = new EnemyController[points.Count]; respawnAt = new float[points.Count];
            Current = this;
            // [PARTY 8] An online party may share this field: wait for the session before spawning.
            if (FieldSession.MayShare())
            {
                Mode = SpawnerMode.Pending;
                pendingUntil = Time.realtimeSinceStartup + PendingSeconds;
                return;
            }
            for (int i = 0; i < points.Count; i++) SpawnAt(i);
        }

        void SpawnAt(int index)
        {
            var enemy = zone != null
                ? MonsterDatabase.SpawnField(zone.monsters[index % zone.monsters.Length], points[index], transform, zone.monsterLevel, zone.KillXp)
                : EnemyController.Create(stats, look, points[index], transform);
            enemy.Shared = true; // [PARTY 8] shared with the field party when this PC hosts
            alive[index] = enemy;
            respawnAt[index] = 0f;
            enemy.Died += e => OnEnemyDied(index);
        }

        float RespawnDelay => zone != null ? HuntingGrounds.RespawnSeconds : stats.respawnDelay;

        void OnEnemyDied(int index)
        {
            alive[index] = null;
            respawnAt[index] = Time.time + RespawnDelay;
        }

        void Update()
        {
            if (alive == null) return;
            if (Mode == SpawnerMode.Pending)
            {
                if (Time.realtimeSinceStartup >= pendingUntil) SetMode(SpawnerMode.Local); // no answer: play alone
                return;
            }
            if (Mode == SpawnerMode.Follower || !Game.IsWorldRunning) return;
            TickFieldBoss();
            for (int i = 0; i < alive.Length; i++)
            {
                if (alive[i] != null || respawnAt[i] <= 0f || Time.time < respawnAt[i]) continue;
                if (NearestPlayerDistance(points[i]) < (zone != null ? HuntingGrounds.RespawnSafeRadius : stats.respawnMinPlayerDistance))
                {
                    respawnAt[i] = Time.time + 2f;
                    continue;
                }
                SpawnAt(i);
            }
        }

        /// <summary>Hosting a field party: no monster appears next to any member.</summary>
        static float NearestPlayerDistance(Vector2 point)
        {
            float best = float.MaxValue;
            if (Game.Party != null && PartyNet.Active)
            {
                foreach (var m in Game.Party.Members)
                    if (m != null) best = Mathf.Min(best, Vector2.Distance(m.Position, point));
            }
            else if (Game.Player != null) best = Vector2.Distance(Game.Player.Position, point);
            return best;
        }
    }
}
