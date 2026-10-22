using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] 잿빛 손 정찰대장 (Docs/ch3/WORLD_AND_STORY.md 7): in 검은 수호석 잔해 and 공허의 틈, every 60 field kills one elite
    /// appears on a spawn point away from the players, with one sure equipment drop. Spawned by this PC (or the field host).
    /// </summary>
    public partial class EnemySpawner
    {
        public const int CaptainEveryKills = 60;
        /// <summary>The field's monster spawn points (the field rifts open on them).</summary>
        public System.Collections.Generic.IReadOnlyList<Vector2> Points => points;
        int captainKills;
        bool captainDue;
        EnemyController captain;

        static bool CaptainGround(HuntingZone z) => z != null && (z.id == MapRegistry.DryStones || z.id == MapRegistry.DryRift);

        void CountFieldKill()
        {
            if (!CaptainGround(zone) || captain != null) return;
            if (++captainKills < CaptainEveryKills) return;
            captainKills = 0;
            captainDue = true;
        }

        void TickCaptain()
        {
            if (!captainDue || captain != null || points.Count == 0) return;
            var at = points[Random.Range(0, points.Count)];
            if (NearestPlayerDistance(at) < HuntingGrounds.RespawnSafeRadius) return; // try again next frame
            captainDue = false;
            captain = MonsterDatabase.SpawnField(MonsterDatabase.GreyCaptain, at, transform, zone.monsterLevel, zone.KillXp * 8);
            captain.Shared = true; // [PARTY 8] shared with the field party when this PC hosts
            captain.Died += e => { if (captain == e) captain = null; };
            GameEvents.RaiseToast(L.T("<color=#ff9f43>잿빛 손 정찰대장</color>이 나타났습니다!"));
            Game.Audio?.PlaySfx("rank_reveal");
        }
    }
}
