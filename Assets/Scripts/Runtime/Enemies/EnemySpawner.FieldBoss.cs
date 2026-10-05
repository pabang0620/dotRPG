using UnityEngine;

namespace DotRPG
{
    /// <summary>[FIELD BOSS] The hunting ground's boss (FieldBosses): spawned once per quarter hour by this PC (or the field host).</summary>
    public partial class EnemySpawner
    {
        EnemyController fieldBoss;
        long bossSpawnedIn = -1;

        void TickFieldBoss()
        {
            if (zone == null || points.Count == 0) return;
            var def = FieldBosses.For(zone.id);
            if (def == null || fieldBoss != null) return;
            long window = FieldBosses.Window;
            if (bossSpawnedIn == window || FieldBosses.DefeatedThisWindow(zone.id)) return;
            // The middle of the map's spawn points, never right next to a player.
            var at = points[points.Count / 2];
            if (NearestPlayerDistance(at) < HuntingGrounds.RespawnSafeRadius) at = points[0];
            if (NearestPlayerDistance(at) < HuntingGrounds.RespawnSafeRadius) return;
            bossSpawnedIn = window;
            fieldBoss = MonsterDatabase.SpawnField(def.monsterId, at, transform, FieldBosses.LevelOf(def), FieldBosses.XpOf(def));
            fieldBoss.Shared = true; // [PARTY 8] shared with the field party when this PC hosts
            string mapId = zone.id;
            fieldBoss.Died += e =>
            {
                FieldBosses.MarkDefeated(mapId);
                if (fieldBoss == e) fieldBoss = null;
                GameEvents.RaiseToast($"<color=#ff9f43>필드 보스</color> {e.Stats.displayName} 처치! 다음 출현까지 {FieldBosses.SecondsToNext / 60 + 1}분");
            };
            GameEvents.RaiseToast($"<color=#ff9f43>필드 보스</color> {MonsterDatabase.Get(def.monsterId)?.name}이(가) 나타났다!");
            Game.Audio?.PlaySfx("rank_reveal");
        }
    }
}
