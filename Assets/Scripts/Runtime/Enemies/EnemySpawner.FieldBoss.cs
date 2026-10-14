using UnityEngine;

namespace DotRPG
{
    /// <summary>[FIELD BOSS] The hunting ground's boss (FieldBosses): spawned when its 30 minutes are up (server time) by this PC (or the field host).</summary>
    public partial class EnemySpawner
    {
        EnemyController fieldBoss;

        void TickFieldBoss()
        {
            if (zone == null || points.Count == 0) return;
            var def = FieldBosses.For(zone.id);
            if (def == null || fieldBoss != null) return;
            if (!FieldBosses.Ready(zone.id)) return;
            // The middle of the map's spawn points, never right next to a player.
            var at = points[points.Count / 2];
            if (NearestPlayerDistance(at) < HuntingGrounds.RespawnSafeRadius) at = points[0];
            if (NearestPlayerDistance(at) < HuntingGrounds.RespawnSafeRadius) return;
            fieldBoss = MonsterDatabase.SpawnField(def.monsterId, at, transform, FieldBosses.LevelOf(def), FieldBosses.XpOf(def));
            fieldBoss.Shared = true; // [PARTY 8] shared with the field party when this PC hosts
            string mapId = zone.id;
            fieldBoss.Died += e =>
            {
                FieldBosses.MarkDefeated(mapId);
                if (fieldBoss == e) fieldBoss = null;
                GameEvents.RaiseToast(L.F("<color=#ff9f43>필드 보스</color> {0} 처치! 다음 출현까지 {1}분", L.T(e.Stats.displayName), FieldBosses.SecondsToNext(mapId) / 60 + 1));
            };
            GameEvents.RaiseToast(L.F("<color=#ff9f43>필드 보스</color> {0}이(가) 나타났습니다!", L.T(MonsterDatabase.Get(def.monsterId)?.name)));
            Game.Audio?.PlaySfx("rank_reveal");
        }
    }
}
